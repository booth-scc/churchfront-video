using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CueVideo.Atem;

public enum AtemLinkState
{
    Idle,
    Connecting,
    Online,
    Offline
}

public readonly record struct AtemLink(AtemLinkState State, string Message = "");

public sealed record AtemCommandLog(DateTime Time, string Command, byte[] Data, bool Sent);

/// <summary>
/// ATEM control over UDP 9910. Mirrors <c>ATEM.swift</c>: session handshake,
/// acknowledgements, retransmission and de-duplication so a late packet can
/// never overwrite a newer one.
/// </summary>
public sealed class AtemClient : ISwitcher
{
    private sealed class InFlight
    {
        public required ushort Id { get; init; }
        public required byte[] Data { get; init; }
        public double Sent { get; set; }
        public int Tries { get; set; }
    }

    private readonly object _gate = new();
    private readonly AtemStateUpdater _updater = new();
    private readonly List<InFlight> _inFlight = [];
    private readonly List<AtemCommandLog> _log = [];
    private IPEndPoint _any = new(IPAddress.Any, 0);

    private UdpClient? _udp;
    private IPEndPoint? _endpoint;
    private Thread? _loop;
    private volatile bool _running;
    private double _lastInbound;
    private double _lastHello;
    private ushort _sessionId = 0x53AB;
    private ushort _localId;
    private ushort? _lastRemoteId;
    private bool _connected;
    private bool _ready;
    private int _port = 9910;

    public event Action<AtemLink>? LinkChanged;
    public event Action<AtemState>? StateChanged;

    public AtemLink Link { get; private set; } = new(AtemLinkState.Idle);
    public string Host { get; private set; } = "";

    public AtemState State
    {
        get { lock (_gate) return _updater.Snapshot(); }
    }

    public IReadOnlyList<AtemCommandLog> SentLog
    {
        get { lock (_gate) return _log.ToArray(); }
    }

    public void Connect(string host, int port = 9910)
    {
        Disconnect();

        Host = host.Trim();
        _port = port;
        if (Host.Length == 0)
        {
            SetLink(new AtemLink(AtemLinkState.Idle));
            return;
        }

        SetLink(new AtemLink(AtemLinkState.Connecting));
        _running = true;
        Open();
        _loop = new Thread(Loop) { IsBackground = true, Name = "atem" };
        _loop.Start();
    }

    public void Disconnect()
    {
        _running = false;
        try { _loop?.Join(500); } catch (Exception) { /* shutting down */ }
        _loop = null;
        CloseSocket();
        Host = "";
        SetLink(new AtemLink(AtemLinkState.Idle));
    }

    private void Open()
    {
        CloseSocket();

        try
        {
            _endpoint = Resolve(Host, _port);
        }
        catch (Exception ex)
        {
            SetLink(new AtemLink(AtemLinkState.Offline, ex.Message));
            return;
        }

        var udp = new UdpClient(AddressFamily.InterNetwork) { Client = { ReceiveTimeout = 10 } };
        udp.Connect(_endpoint);
        _udp = udp;

        lock (_gate)
        {
            _inFlight.Clear();
            _sessionId = 0x53AB;
            _localId = 0;
            _lastRemoteId = null;
            _connected = false;
            _ready = false;
            _updater.Reset();
            _lastHello = Now();
            _lastInbound = Now();
        }

        SendRaw(AtemPacket.Hello);
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                var udp = _udp;
                if (udp is null) break;

                var data = udp.Receive(ref _any);
                if (data.Length >= AtemPacket.HeaderSize) Handle(data);
            }
            catch (SocketException)
            {
                // Receive timeout; fall through to the tick.
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                if (!_running) break;
                Thread.Sleep(50);
            }

            Tick();
        }
    }

    private void Handle(byte[] buffer)
    {
        if (!AtemPacket.TryReadHeader(buffer, out var header)) return;

        AtemState? snapshot = null;
        AtemLink? link = null;

        lock (_gate)
        {
            _lastInbound = Now();
            _sessionId = header.Session;

            if ((header.Flags & AtemPacket.FlagNewSession) != 0)
            {
                _connected = true;
                _lastRemoteId = null;
                SendAck(header.RemoteId);
                link = new AtemLink(AtemLinkState.Connecting);
            }

            if ((header.Flags & AtemPacket.FlagAckRequest) != 0)
            {
                SendAck(header.RemoteId);

                // Apply each packet once, or an old update can land after a newer one.
                var fresh = _lastRemoteId is null
                    ? 1
                    : ((int)header.RemoteId - _lastRemoteId.Value + 32768) % 32768;

                if (fresh > 0 && fresh < 16384)
                {
                    _lastRemoteId = header.RemoteId;

                    if (header.Length > AtemPacket.HeaderSize && buffer.Length >= header.Length)
                    {
                        Parse(buffer.AsSpan(AtemPacket.HeaderSize, header.Length - AtemPacket.HeaderSize));

                        if (_updater.Ready && !_ready)
                        {
                            _ready = true;
                            link = new AtemLink(AtemLinkState.Online);
                        }

                        snapshot = _updater.Snapshot();
                    }
                }
            }

            if ((header.Flags & AtemPacket.FlagAckReply) != 0)
            {
                var acked = header.Ack;
                _inFlight.RemoveAll(p => ((int)acked - p.Id + 32768) % 32768 < 16384);
            }

            if ((header.Flags & AtemPacket.FlagRetransmitRequest) != 0)
            {
                foreach (var packet in _inFlight) SendRaw(packet.Data);
            }
        }

        if (link is { } l) SetLink(l);
        if (snapshot is not null) StateChanged?.Invoke(snapshot);
    }

    private void Parse(ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        while (offset + 8 <= payload.Length)
        {
            var length = (payload[offset] << 8) | payload[offset + 1];
            if (length < 8 || offset + length > payload.Length) break;

            var name = Encoding.ASCII.GetString(payload.Slice(offset + 4, 4));
            _updater.Apply(name, payload.Slice(offset + 8, length - 8));
            offset += length;
        }
    }

    private void Tick()
    {
        AtemLink? link = null;

        lock (_gate)
        {
            var t = Now();

            // Keep knocking until the switcher opens a session; UDP hello can be lost.
            if (!_connected && t - _lastHello > 0.5)
            {
                SendRaw(AtemPacket.Hello);
                _lastHello = t;
            }

            if (t - _lastInbound > 3)
            {
                link = new AtemLink(AtemLinkState.Offline, $"No answer from {Host}");
            }
            else
            {
                foreach (var packet in _inFlight)
                {
                    if (t - packet.Sent <= 0.06) continue;

                    if (packet.Tries >= 10)
                    {
                        link = new AtemLink(AtemLinkState.Offline, "The switcher stopped answering");
                        break;
                    }

                    var data = (byte[])packet.Data.Clone();
                    data[0] |= AtemPacket.FlagRetransmit << 3;
                    SendRaw(data);
                    packet.Sent = t;
                    packet.Tries++;
                }
            }

            if (link is { State: AtemLinkState.Offline }) _lastHello = 0;
        }

        if (link is { } l)
        {
            SetLink(l);
            if (l.State == AtemLinkState.Offline) Open();
        }
    }

    private void SendAck(ushort remoteId)
    {
        var packet = AtemPacket.Build(AtemPacket.FlagAckReply, _sessionId, remoteId, 0, ReadOnlySpan<byte>.Empty);
        SendRaw(packet);
    }

    private void SendRaw(byte[] packet)
    {
        try
        {
            _udp?.Send(packet, packet.Length);
        }
        catch (Exception)
        {
            // A failed send is recovered by the retransmit loop.
        }
    }

    private void SetLink(AtemLink link)
    {
        if (link == Link) return;
        Link = link;
        LinkChanged?.Invoke(link);
    }

    private void CloseSocket()
    {
        try { _udp?.Dispose(); } catch (Exception) { /* already gone */ }
        _udp = null;
    }

    private static IPEndPoint Resolve(string host, int port)
    {
        if (IPAddress.TryParse(host, out var address)) return new IPEndPoint(address, port);

        var addresses = Dns.GetHostAddresses(host);
        if (addresses.Length == 0) throw new InvalidOperationException($"Cannot resolve {host}");
        return new IPEndPoint(addresses[0], port);
    }

    private static double Now() => Environment.TickCount64 / 1000.0;

    // ---- Commands ----

    public void SetProgram(int source, int me = 0) => Send("CPgI", [(byte)me, 0, Hi(source), Lo(source)]);
    public void SetPreview(int source, int me = 0) => Send("CPvI", [(byte)me, 0, Hi(source), Lo(source)]);
    public void Cut(int me = 0) => Send("DCut", [(byte)me, 0, 0, 0]);
    public void Auto(int me = 0) => Send("DAut", [(byte)me, 0, 0, 0]);

    public void SetPosition(double value, int me = 0)
    {
        var scaled = (int)Math.Round(Math.Clamp(value, 0, 1) * 10000);
        Send("CTPs", [(byte)me, 0, Hi(scaled), Lo(scaled)]);
    }

    public void SetStyle(int style, int me = 0) => Send("CTTp", [1, (byte)me, (byte)style, 0]);
    public void SetNextSelection(int mask, int me = 0) => Send("CTTp", [2, (byte)me, 0, (byte)Math.Max(1, mask)]);

    public void SetMixRate(int frames, int me = 0) =>
        Send("CTMx", [(byte)me, (byte)Math.Clamp(frames, 1, 250), 0, 0]);

    public void SetKeyOnAir(int keyer, bool on, int me = 0) =>
        Send("CKOn", [(byte)me, (byte)keyer, on ? (byte)1 : (byte)0, 0]);

    public void SetDskOnAir(int dsk, bool on) => Send("CDsL", [(byte)dsk, on ? (byte)1 : (byte)0, 0, 0]);
    public void SetDskTie(int dsk, bool on) => Send("CDsT", [(byte)dsk, on ? (byte)1 : (byte)0, 0, 0]);
    public void DskAuto(int dsk) => Send("DDsA", [(byte)dsk, 0, 0, 0]);
    public void FadeToBlack(int me = 0) => Send("FtbA", [(byte)me, 0, 0, 0]);

    public void SetAux(int aux, int source) => Send("CAuS", [1, (byte)aux, Hi(source), Lo(source)]);
    public void RunMacro(int index) => Send("MAct", [Hi(index), Lo(index), 0, 0]);
    public void StopMacro() => Send("MAct", [0xFF, 0xFF, 1, 0]);
    public void SetStreaming(bool on) => Send("StrR", [on ? (byte)1 : (byte)0, 0, 0, 0]);
    public void SetRecording(bool on) => Send("RcTM", [on ? (byte)1 : (byte)0, 0, 0, 0]);

    private void Send(string command, byte[] data)
    {
        lock (_gate)
        {
            var sent = _connected && _ready;
            _log.Add(new AtemCommandLog(DateTime.Now, command, data, sent));
            if (_log.Count > 60) _log.RemoveRange(0, _log.Count - 60);

            if (!sent) return;

            _localId = (ushort)((_localId + 1) % 32768);
            var payload = AtemPacket.Command(command, data);
            var packet = AtemPacket.Build(AtemPacket.FlagAckRequest, _sessionId, 0, _localId, payload);

            _inFlight.Add(new InFlight { Id = _localId, Data = packet, Sent = Now() });
            SendRaw(packet);
        }
    }

    private static byte Hi(int value) => (byte)((ushort)Math.Clamp(value, 0, 65535) >> 8);
    private static byte Lo(int value) => (byte)((ushort)Math.Clamp(value, 0, 65535) & 0xFF);

    public void Dispose() => Disconnect();
}
