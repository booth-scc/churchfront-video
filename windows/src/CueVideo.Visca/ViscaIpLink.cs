using System.Net;
using System.Net.Sockets;

namespace CueVideo.Visca;

/// <summary>
/// VISCA over IP (UDP 52381) for the BirdDog cameras. Ported from the VISCALink in
/// <c>PTZ.swift</c>: sequence-numbered messages, an 8-byte header, and inquiry answers
/// matched to questions by shape because replies do not say what they answer.
/// </summary>
public sealed class ViscaIpLink : IDisposable
{
    private sealed class Waiter
    {
        public required bool Counts { get; init; }
        public required TaskCompletionSource<PtzPosition?> Source { get; init; }
    }

    private enum InquiryKind
    {
        PanTilt,
        Zoom,
        AeMode,
        Shutter,
        Iris,
        Gain,
        WbMode,
        ColourTemperature,
    }

    private readonly object _gate = new();
    private readonly string _host;
    private readonly int _port;
    private readonly int _firstPreset;
    private readonly ViscaUnits _units;

    private readonly List<byte> _rx = [];
    private readonly List<(InquiryKind Kind, double At)> _pending = [];
    private readonly List<Waiter> _waiters = [];

    private UdpClient? _udp;
    private IPEndPoint? _endpoint;
    private IPEndPoint _any = new(IPAddress.Any, 0);
    private Thread? _loop;
    private volatile bool _running;
    private uint _sequence;
    private double _lastReceive;
    private bool _online;

    private (int Pan, int Tilt)? _positionPanTilt;
    private int? _positionZoom;
    private int _positionAsk;

    private CameraExposure? _reading;
    private TaskCompletionSource<CameraExposure?>? _exposureWaiter;
    private int _aeMode = -1;
    private CameraExposure? _lastExposure;

    public ViscaIpLink(string host, int port = 52381, int firstPreset = 20, ViscaUnits? units = null)
    {
        _host = host;
        _port = port;
        _firstPreset = firstPreset;
        _units = units ?? ViscaUnits.PtzOptics;
    }

    public event Action<ViscaLinkState>? LinkChanged;
    public event Action<PtzPosition>? RawPositionUpdated;
    public event Action<CameraExposure>? ExposureUpdated;

    public ViscaLinkState Link { get; private set; } = ViscaLinkState.Idle;
    public string Host => _host;
    public ViscaUnits Units => _units;

    public void Start()
    {
        Stop();

        SetLink(ViscaLinkState.Connecting);
        _running = true;
        Open();
        _loop = new Thread(Loop) { IsBackground = true, Name = $"visca:{_host}" };
        _loop.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _loop?.Join(500); } catch (Exception) { /* shutting down */ }
        _loop = null;

        lock (_gate)
        {
            _udp?.Dispose();
            _udp = null;
            _rx.Clear();
            _pending.Clear();
            _positionPanTilt = null;
            _positionZoom = null;
        }

        _online = false;
        SetLink(ViscaLinkState.Idle);
    }

    private void Open()
    {
        try
        {
            _endpoint = Resolve(_host, _port);
        }
        catch (Exception)
        {
            SetLink(ViscaLinkState.Offline);
            return;
        }

        var udp = new UdpClient(AddressFamily.InterNetwork) { Client = { ReceiveTimeout = 10 } };
        udp.Connect(_endpoint);

        lock (_gate)
        {
            _udp = udp;
            _sequence = 0;
            _lastReceive = Now();
        }

        // Reset the camera's sequence handling, then ask it something so silence is visible.
        Send(ViscaCommands.SequenceReset());
        Send(ViscaCommands.PowerInquiry(), inquiry: true);
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
                if (data.Length > 0) Receive(data);
            }
            catch (SocketException)
            {
                // Receive timeout; the watchdog below runs on the same tick.
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

            if (_online && Now() - _lastReceive > 3) SetLink(ViscaLinkState.Offline);
        }
    }

    private void Receive(byte[] data)
    {
        _lastReceive = Now();
        if (!_online)
        {
            _online = true;
            SetLink(ViscaLinkState.Online);
        }

        lock (_gate)
        {
            // VISCA over IP prefixes every message with an 8-byte header.
            if (data.Length > 8) _rx.AddRange(data.AsSpan(8).ToArray());
            else if (data.Length > 0 && data[0] == 0x90) { /* bare VISCA on some firmware */ _rx.AddRange(data); }
            else return;

            while (true)
            {
                var end = _rx.IndexOf(0xFF);
                if (end < 0) break;

                var message = _rx.GetRange(0, end + 1).ToArray();
                _rx.RemoveRange(0, end + 1);

                if (message.Length < 3 || _pending.Count == 0) continue;
                if (message[1] != 0x50 && message[1] != 0x60) continue; // ack / completion

                if (message[1] == 0x60)
                {
                    _pending.RemoveAt(0); // refusal, assumed to be the oldest question
                    continue;
                }

                var index = _pending.FindIndex(p => LengthOf(p.Kind) == message.Length);
                if (index < 0) continue;

                var kind = _pending[index].Kind;
                _pending.RemoveAt(index);
                Apply(kind, message);
            }

            if (_rx.Count > 256) _rx.Clear();
        }
    }

    private void Apply(InquiryKind kind, byte[] message)
    {
        var low = message.Length == 7
            ? ((message[4] & 0x0F) << 4) | (message[5] & 0x0F)
            : message[2];

        switch (kind)
        {
            case InquiryKind.PanTilt:
                _positionPanTilt = (Word(message, 2), Word(message, 6));
                MaybePublishPosition();
                break;

            case InquiryKind.Zoom:
                _positionZoom = Word(message, 2);
                MaybePublishPosition();
                break;

            case InquiryKind.AeMode:
                _aeMode = low;
                _reading = new CameraExposure
                {
                    ShutterAuto = low is not (0x03 or 0x0A),
                    IrisAuto = low is not (0x03 or 0x0B),
                    GainAuto = low != 0x03,
                };
                break;

            case InquiryKind.Shutter:
                if (_reading is not null)
                    _reading = _reading with
                    {
                        Shutter = _units.Shutter.TryGetValue(low, out var shutter) ? shutter : null,
                    };
                break;

            case InquiryKind.Iris:
                if (_reading is not null)
                    _reading = _reading with
                    {
                        Iris = _units.Iris.TryGetValue(low, out var iris) ? iris : null,
                    };
                break;

            case InquiryKind.Gain:
                if (_reading is not null) _reading = _reading with { Gain = low };
                break;

            case InquiryKind.WbMode:
                if (_reading is not null)
                    _reading = _reading with
                    {
                        WhiteBalance = low switch
                        {
                            0x00 => "auto",
                            0x01 => "tungsten",
                            0x02 => "daylight",
                            0x20 => "kelvin",
                            _ => "custom",
                        },
                    };
                break;

            case InquiryKind.ColourTemperature:
                if (_reading is not null)
                {
                    _reading = _reading with
                    {
                        Kelvin = Math.Min(_units.KelvinTop, _units.KelvinBase + low * _units.KelvinStep),
                        HasExposure = true,
                    };
                    _lastExposure = _reading;
                    ExposureUpdated?.Invoke(_reading);
                    _exposureWaiter?.TrySetResult(_reading);
                    _exposureWaiter = null;
                }
                break;
        }
    }

    /// <summary>Counts the way the camera reports them; the tracker converts to degrees.</summary>
    public Task<PtzPosition?> ReadPositionAsync(bool counts = false, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<PtzPosition?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int ask = 0;
        var schedule = false;

        lock (_gate)
        {
            _waiters.Add(new Waiter { Counts = counts, Source = tcs });

            if (_waiters.Count == 1)
            {
                _positionPanTilt = null;
                _positionZoom = null;
                ask = ++_positionAsk;
                Ask(InquiryKind.PanTilt, ViscaCommands.PanTiltInquiry());
                Ask(InquiryKind.Zoom, ViscaCommands.ZoomInquiry());
                schedule = true;
            }
        }

        if (schedule)
        {
            var wait = timeout ?? TimeSpan.FromMilliseconds(500);
            _ = Task.Delay(wait).ContinueWith(_ => ExpirePosition(ask), TaskScheduler.Default);
        }

        return tcs.Task;
    }

    private void ExpirePosition(int ask)
    {
        List<Waiter>? waiters = null;

        lock (_gate)
        {
            if (_positionAsk == ask && _waiters.Count > 0)
            {
                waiters = [.. _waiters];
                _waiters.Clear();
            }
        }

        if (waiters is null) return;
        foreach (var waiter in waiters) waiter.Source.TrySetResult(null);
    }

    private void MaybePublishPosition()
    {
        if (_positionPanTilt is not { } panTilt || _positionZoom is not { } zoom) return;
        if (_waiters.Count == 0) return;

        var waiters = _waiters.ToArray();
        _waiters.Clear();

        var counts = new PtzPosition(panTilt.Pan, panTilt.Tilt, zoom);
        var degrees = _units.ToPosition(counts.Pan, counts.Tilt, counts.Zoom);

        RawPositionUpdated?.Invoke(counts);

        foreach (var waiter in waiters)
        {
            waiter.Source.TrySetResult(waiter.Counts ? counts : degrees);
        }
    }

    /// <summary>Absolute move using counts from <see cref="ReadPositionAsync"/> with counts: true.</summary>
    public void GoTo(PtzPosition counts)
    {
        Send(ViscaCommands.AbsolutePanTilt(counts.Pan, counts.Tilt));
        Send(ViscaCommands.AbsoluteZoom(Math.Max(0, counts.Zoom)));
    }

    public void Drive(double pan, double tilt) => Send(ViscaCommands.Drive(pan, tilt));
    public void DriveRaw(int pan, int tilt) => Send(ViscaCommands.DriveRaw(pan, tilt));
    public void StopMove() => Send(ViscaCommands.Stop());
    public void Zoom(double speed) => Send(ViscaCommands.Zoom(speed));
    public void AutoFocus(bool on) => Send(ViscaCommands.AutoFocus(on));
    public void FocusNear() => Send(ViscaCommands.FocusNear());
    public void FocusFar() => Send(ViscaCommands.FocusFar());
    public void StopFocus() => Send(ViscaCommands.FocusStop());
    public void FocusOnce() => Send(ViscaCommands.FocusOnce());
    public void Home() => Send(ViscaCommands.Home());

    public void Recall(int number) => Send(ViscaCommands.PresetRecall(number, _firstPreset));
    public void Store(int number) => Send(ViscaCommands.PresetStore(number, _firstPreset));
    public void ClearPreset(int number) => Send(ViscaCommands.PresetClear(number, _firstPreset));

    public async Task<CameraExposure?> ReadExposureAsync(TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<CameraExposure?>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            _exposureWaiter = tcs;
            _reading = null;
            Ask(InquiryKind.AeMode, ViscaCommands.AutoExposureInquiry());
            Ask(InquiryKind.Shutter, ViscaCommands.ShutterInquiry());
            Ask(InquiryKind.Iris, ViscaCommands.IrisInquiry());
            Ask(InquiryKind.Gain, ViscaCommands.GainInquiry());
            Ask(InquiryKind.WbMode, ViscaCommands.WhiteBalanceModeInquiry());
            Ask(InquiryKind.ColourTemperature, ViscaCommands.ColourTemperatureInquiry());
        }

        var wait = timeout ?? TimeSpan.FromMilliseconds(1500);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(wait));
        return completed == tcs.Task ? await tcs.Task : null;
    }

    public async Task SetExposureAsync(ExposureRequest request)
    {
        if (request.HasExposure)
        {
            var had = _aeMode == 0x03 ? null : _lastExposure;
            Send(ViscaCommands.SetManualExposure());

            if ((request.Shutter ?? had?.Shutter) is { } shutter &&
                _units.PositionOf((int)Math.Round(shutter), _units.Shutter) is { } shutterPosition)
            {
                Send(ViscaCommands.SetShutterPosition(shutterPosition));
            }

            if ((request.Iris ?? had?.Iris) is { } iris &&
                _units.PositionOf((int)Math.Round(iris), _units.Iris) is { } irisPosition)
            {
                Send(ViscaCommands.SetIrisPosition(irisPosition));
            }

            if ((request.Gain ?? had?.Gain) is { } gain)
            {
                Send(ViscaCommands.SetGainLevel(Math.Clamp(gain, 0, _units.GainMax)));
            }
        }

        if (request.Kelvin is { } kelvin && (request.WhiteBalance is null or "kelvin"))
        {
            var position = Math.Clamp((kelvin - _units.KelvinBase) / _units.KelvinStep, 0,
                (_units.KelvinTop - _units.KelvinBase) / _units.KelvinStep);
            Send(ViscaCommands.SetWhiteBalanceMode(0x20));
            Send(ViscaCommands.SetColourTemperature(position));
        }
        else if (request.WhiteBalance is { } mode)
        {
            byte? code = mode switch
            {
                "auto" => 0x00,
                "tungsten" => 0x01,
                "daylight" => 0x02,
                _ => null,
            };

            if (code is { } value) Send(ViscaCommands.SetWhiteBalanceMode(value));
        }

        await Task.Delay(500);
        await ReadExposureAsync();
    }

    public Task AutoExposureAsync()
    {
        Send(ViscaCommands.SetAutoExposure());
        return Task.Delay(500).ContinueWith(_ => ReadExposureAsync(), TaskScheduler.Default).Unwrap();
    }

    /// <summary>Raw VISCA bytes for diagnostics; adds the IP header when the link is UDP.</summary>
    public void SendRaw(byte[] visca, bool inquiry = false) => Send(visca, inquiry);

    private void Send(byte[] visca, bool inquiry = false)
    {
        UdpClient? udp;
        lock (_gate) udp = _udp;
        if (udp is null) return;

        _sequence++;

        var packet = new byte[8 + visca.Length];
        packet[0] = 0x01;
        packet[1] = inquiry ? (byte)0x10 : (byte)0x00;
        packet[2] = (byte)(visca.Length >> 8);
        packet[3] = (byte)(visca.Length & 0xFF);
        packet[4] = (byte)(_sequence >> 24);
        packet[5] = (byte)((_sequence >> 16) & 0xFF);
        packet[6] = (byte)((_sequence >> 8) & 0xFF);
        packet[7] = (byte)(_sequence & 0xFF);
        visca.CopyTo(packet, 8);

        try
        {
            udp.Send(packet, packet.Length);
        }
        catch (Exception)
        {
            // Nothing useful to do here; the next command or poll will try again.
        }
    }

    private void Ask(InquiryKind kind, byte[] inquiry)
    {
        var now = Now();

        // Answers arrive within hundredths of a second. Anything older than 1.5 s was lost and
        // would put every later answer against the wrong question.
        if (_pending.Count > 0 && now - _pending[0].At > 1.5) _pending.Clear();
        _pending.Add((kind, now));

        Send(inquiry, inquiry: true);
    }

    private void SetLink(ViscaLinkState link)
    {
        if (Link == link) return;
        Link = link;
        LinkChanged?.Invoke(link);
    }

    private static int LengthOf(InquiryKind kind) => kind switch
    {
        InquiryKind.PanTilt => 11,
        InquiryKind.Zoom or InquiryKind.Shutter or InquiryKind.Iris or InquiryKind.Gain => 7,
        _ => 4,
    };

    /// <summary>Signed 16-bit value packed as four nibbles.</summary>
    private static int Word(byte[] message, int at)
    {
        var value = 0;
        for (var i = at; i < at + 4 && i < message.Length; i++) value = (value << 4) | (message[i] & 0x0F);
        return value >= 0x8000 ? value - 0x10000 : value;
    }

    private static IPEndPoint Resolve(string host, int port)
    {
        if (IPAddress.TryParse(host, out var address)) return new IPEndPoint(address, port);
        var addresses = Dns.GetHostAddresses(host);
        if (addresses.Length == 0) throw new InvalidOperationException($"Cannot resolve {host}");
        return new IPEndPoint(addresses[0], port);
    }

    private static double Now() => Environment.TickCount64 / 1000.0;

    public void Dispose() => Stop();
}
