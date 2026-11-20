using System.Net;
using System.Net.Sockets;
using CueVideo.Atem;

namespace CueVideo.Spike;

/// <summary>
/// A tiny stand-in switcher for UI work when the real ATEM is powered down.
/// Sends the same initial state the booth switcher would, and acknowledges
/// commands without acting on them.
/// </summary>
internal static class FakeSwitcher
{
    public static void Run(int port)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        udp.Client.ReceiveTimeout = 100;

        var client = new IPEndPoint(IPAddress.Any, 0);
        ushort packetId = 1;
        ushort session = 0x53AB;

        Console.WriteLine($"Fake ATEM listening on 127.0.0.1:{port}. Ctrl+C to stop.");
        Console.WriteLine("Point the app at it with $env:CUEVIDEO_ATEM = \"127.0.0.1\"");

        while (true)
        {
            byte[] data;
            try
            {
                data = udp.Receive(ref client);
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (!AtemPacket.TryReadHeader(data, out var header)) continue;

            if ((header.Flags & AtemPacket.FlagAckRequest) != 0)
            {
                // Acknowledge client commands so its retransmit loop stays quiet.
                var ack = AtemPacket.Build(AtemPacket.FlagAckReply, session, header.RemoteId, 0, ReadOnlySpan<byte>.Empty);
                udp.Send(ack, ack.Length, client);
                Console.WriteLine($"acked packet {header.RemoteId}");
                continue;
            }

            if ((header.Flags & AtemPacket.FlagNewSession) == 0) continue;

            var opening = AtemPacket.Build(AtemPacket.FlagNewSession, session, 0, packetId++, ReadOnlySpan<byte>.Empty);
            udp.Send(opening, opening.Length, client);

            var commands = new List<byte>();
            commands.AddRange(AtemPacket.Command("InCm"));
            commands.AddRange(AtemPacket.Command("_pin", Product()));
            commands.AddRange(AtemPacket.Command("_ver", 0, 9, 0, 6));

            var inputs = new (int Id, string Long, string Short)[]
            {
                (1, "Camera 1", "CAM1"),
                (2, "Camera 2", "CAM2"),
                (3, "Camera 3", "CAM3"),
                (4, "Camera 4", "CAM4"),
                (5, "PRES MAIN", "PP M"),
                (6, "PRES STAGE", "PP S"),
            };

            foreach (var (id, longName, shortName) in inputs)
            {
                commands.AddRange(AtemPacket.Command("InPr", InputProperties(id, longName, shortName)));
            }

            commands.AddRange(AtemPacket.Command("PrgI", 0, 0, 0, 1));
            commands.AddRange(AtemPacket.Command("PrvI", 0, 0, 0, 2));
            commands.AddRange(AtemPacket.Command("TrSS", 0, 0, 0, 0, 1));
            commands.AddRange(AtemPacket.Command("TMxP", 0, 25));
            commands.AddRange(AtemPacket.Command("KeOn", 0, 0, 0));

            var state = AtemPacket.Build(AtemPacket.FlagAckRequest, session, 0, packetId++, commands.ToArray());
            udp.Send(state, state.Length, client);

            Console.WriteLine("session opened, initial state sent");
        }
    }

    private static byte[] Product()
    {
        var data = new byte[44];
        System.Text.Encoding.ASCII.GetBytes("ATEM 1 M/E Constellation HD").CopyTo(data, 0);
        return data;
    }

    private static byte[] InputProperties(int id, string longName, string shortName)
    {
        var data = new byte[26];
        data[0] = (byte)(id >> 8);
        data[1] = (byte)(id & 0xFF);
        System.Text.Encoding.ASCII.GetBytes(longName).CopyTo(data, 2);
        System.Text.Encoding.ASCII.GetBytes(shortName).CopyTo(data, 22);
        return data;
    }
}
