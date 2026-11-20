using System.Net;
using System.Net.Sockets;

namespace CueVideo.Spike;

/// <summary>
/// A VISCA-over-IP camera stand-in: answers position, zoom and exposure inquiries,
/// stores and recalls presets, and follows absolute moves, so the operator UI
/// behaves as if real cameras were connected.
/// </summary>
internal static class FakeCamera
{
    public static void Run(int port)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port))
        {
            Client = { ReceiveTimeout = 100 },
        };

        var client = new IPEndPoint(IPAddress.Any, 0);
        var pan = 1000;
        var tilt = -200;
        var zoom = 4096;
        var presets = new Dictionary<int, (int Pan, int Tilt, int Zoom)>();

        Console.WriteLine($"Fake VISCA camera on 127.0.0.1:{port}. Ctrl+C to stop.");

        while (true)
        {
            byte[] packet;
            try
            {
                packet = udp.Receive(ref client);
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (packet.Length <= 8) continue;
            var payload = packet[8..];
            if (payload.Length < 5) continue;

            if (payload[1] == 0x09)
            {
                var reply = Inquiry(payload, pan, tilt, zoom);
                if (reply is not null)
                {
                    var response = new byte[8 + reply.Length];
                    packet.AsSpan(0, 8).CopyTo(response);
                    reply.CopyTo(response, 8);
                    try { udp.Send(response, response.Length, client); } catch (Exception) { /* gone */ }
                }
            }
            else if (payload[1] == 0x01)
            {
                Command(payload, ref pan, ref tilt, ref zoom, presets);
            }
        }
    }

    private static byte[]? Inquiry(byte[] p, int pan, int tilt, int zoom) => p[3] switch
    {
        0x00 => Reply4(0x02),
        0x12 => ReplyPanTilt(pan, tilt),
        0x47 => ReplyNibbles(zoom),
        0x39 => Reply4(0x03),
        0x4A => ReplyExposure(2),
        0x4B => ReplyExposure(4),
        0x4C => ReplyExposure(6),
        0x35 => Reply4(0x00),
        0x20 => Reply4(0x10),
        _ => null,
    };

    private static void Command(byte[] p, ref int pan, ref int tilt, ref int zoom,
        Dictionary<int, (int Pan, int Tilt, int Zoom)> presets)
    {
        switch (p[2], p[3])
        {
            case (0x06, 0x02) when p.Length >= 14:
                pan = Word(p, 6);
                tilt = Word(p, 10);
                break;

            case (0x04, 0x47) when p.Length >= 8:
                zoom = Word(p, 4);
                break;

            case (0x04, 0x3F) when p.Length >= 7:
            {
                var slot = p[5];
                switch (p[4])
                {
                    case 0x01:
                        presets[slot] = (pan, tilt, zoom);
                        break;
                    case 0x02:
                        if (presets.TryGetValue(slot, out var preset))
                        {
                            pan = preset.Pan;
                            tilt = preset.Tilt;
                            zoom = preset.Zoom;
                        }
                        break;
                    case 0x00:
                        presets.Remove(slot);
                        break;
                }
                break;
            }
        }
    }

    private static byte[] Reply4(int value) => [0x90, 0x50, (byte)value, 0xFF];

    private static byte[] ReplyNibbles(int value) =>
        [0x90, 0x50, Nibble(value, 0), Nibble(value, 1), Nibble(value, 2), Nibble(value, 3), 0xFF];

    private static byte[] ReplyPanTilt(int pan, int tilt) =>
    [
        0x90, 0x50,
        Nibble(pan, 0), Nibble(pan, 1), Nibble(pan, 2), Nibble(pan, 3),
        Nibble(tilt, 0), Nibble(tilt, 1), Nibble(tilt, 2), Nibble(tilt, 3),
        0xFF,
    ];

    private static byte[] ReplyExposure(int position) =>
        [0x90, 0x50, 0x00, 0x00, 0x00, (byte)position, 0xFF];

    private static int Word(byte[] data, int at)
    {
        var value = 0;
        for (var i = at; i < at + 4 && i < data.Length; i++) value = (value << 4) | (data[i] & 0x0F);
        return value >= 0x8000 ? value - 0x10000 : value;
    }

    private static byte Nibble(int value, int index) => (byte)((value >> (12 - 4 * index)) & 0x0F);
}
