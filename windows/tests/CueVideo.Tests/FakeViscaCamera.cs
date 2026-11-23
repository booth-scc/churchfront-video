using System.Net;
using System.Net.Sockets;

namespace CueVideo.Tests;

/// <summary>
/// A minimal VISCA-over-IP camera: answers the power, position, zoom and exposure
/// inquiries with fixed values and records every command it receives.
/// </summary>
internal sealed class FakeViscaCamera : IDisposable
{
    private readonly UdpClient _udp;
    private readonly Thread _thread;
    private IPEndPoint _client = new(IPAddress.Any, 0);
    private volatile bool _running = true;
    private readonly List<byte[]> _received = [];

    public int Port { get; }
    public int Pan { get; set; } = 1000;
    public int Tilt { get; set; } = -200;
    public int Zoom { get; set; } = 4096;
    public int ShutterPosition { get; set; } = 2;
    public int IrisPosition { get; set; } = 4;
    public int GainLevel { get; set; } = 6;
    public int AeMode { get; set; } = 0x03;
    public int WhiteBalanceMode { get; set; } = 0x00;
    public int ColourTemperature { get; set; } = 0x10;
    public int ReplyDelayMs { get; set; }

    public FakeViscaCamera()
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        _thread = new Thread(Loop) { IsBackground = true, Name = "fake-visca" };
        _thread.Start();
    }

    public IReadOnlyList<byte[]> Received
    {
        get { lock (_received) return _received.ToArray(); }
    }

    private void Loop()
    {
        _udp.Client.ReceiveTimeout = 50;

        while (_running)
        {
            byte[] packet;
            try
            {
                packet = _udp.Receive(ref _client);
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                break;
            }

            if (packet.Length <= 8) continue;
            var payload = packet[8..];

            lock (_received) _received.Add(payload);

            if (ReplyDelayMs > 0) Thread.Sleep(ReplyDelayMs);

            var reply = Answer(payload);
            if (reply is null) continue;

            var response = new byte[8 + reply.Length];
            packet.AsSpan(0, 8).CopyTo(response); // mirror the header, sequence included
            reply.CopyTo(response, 8);
            _udp.Send(response, response.Length, _client);
        }
    }

    private byte[]? Answer(byte[] payload)
    {
        if (payload.Length < 5 || payload[1] != 0x09) return null;

        return payload[3] switch
        {
            0x00 => Reply4(0x02),                       // power
            0x12 => ReplyPanTilt(),                     // pan/tilt (0x06 0x12)
            0x47 => ReplyNibbles(Zoom),                 // zoom
            0x39 => Reply4(AeMode),                     // exposure mode
            0x4A => ReplyExposure(ShutterPosition),     // shutter
            0x4B => ReplyExposure(IrisPosition),        // iris
            0x4C => ReplyExposure(GainLevel),           // gain
            0x35 => Reply4(WhiteBalanceMode),           // white balance mode
            0x20 => Reply4(ColourTemperature),          // colour temperature
            _ => null,
        };
    }

    private static byte[] Reply4(int value) => [0x90, 0x50, (byte)value, 0xFF];

    private byte[] ReplyNibbles(int value) =>
        [0x90, 0x50, Nibble(value, 0), Nibble(value, 1), Nibble(value, 2), Nibble(value, 3), 0xFF];

    private byte[] ReplyPanTilt() =>
    [
        0x90, 0x50,
        Nibble(Pan, 0), Nibble(Pan, 1), Nibble(Pan, 2), Nibble(Pan, 3),
        Nibble(Tilt, 0), Nibble(Tilt, 1), Nibble(Tilt, 2), Nibble(Tilt, 3),
        0xFF,
    ];

    private static byte[] ReplyExposure(int position) =>
        [0x90, 0x50, 0x00, 0x00, 0x00, (byte)position, 0xFF];

    private static byte Nibble(int value, int index) => (byte)((value >> (12 - 4 * index)) & 0x0F);

    public void Dispose()
    {
        _running = false;
        try { _thread.Join(500); } catch (Exception) { /* shutting down */ }
        _udp.Dispose();
    }
}
