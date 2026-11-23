using System.Net;
using System.Net.Sockets;
using System.Text;
using CueVideo.Atem;

namespace CueVideo.Spike;

/// <summary>
/// A small stand-in switcher with working program/preview, cut, auto, black, keys and
/// mix rate, so the operator UI can be exercised without the real ATEM.
/// </summary>
internal static class FakeSwitcher
{
    public static void Run(int port)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port))
        {
            Client = { ReceiveTimeout = 100 },
        };

        var client = new IPEndPoint(IPAddress.Any, 0);
        var gate = new object();
        ushort session = 0x53AB;
        ushort packetId = 1;

        var program = 1;
        var preview = 2;
        var mixRate = 30;
        var ftb = false;
        var keys = new bool[4];

        Console.WriteLine($"Fake ATEM listening on 127.0.0.1:{port}. Ctrl+C to stop.");

        void Send(params byte[][] commands)
        {
            lock (gate)
            {
                try
                {
                    var payload = commands.SelectMany(c => c).ToArray();
                    var packet = AtemPacket.Build(AtemPacket.FlagAckRequest, session, 0, packetId++, payload);
                    udp.Send(packet, packet.Length, client);
                }
                catch (Exception)
                {
                    // client went away
                }
            }
        }

        void SendInitial()
        {
            var inputs = new (int Id, string Long, string Short)[]
            {
                (1, "Camera 1", "CAM1"),
                (2, "Camera 2", "CAM2"),
                (3, "Camera 3", "CAM3"),
                (4, "Camera 4", "CAM4"),
                (5, "PRES MAIN", "PP M"),
                (6, "PRES STAGE", "PP S"),
            };

            var commands = new List<byte[]>
            {
                AtemPacket.Command("InCm"),
                AtemPacket.Command("_pin", Product()),
                AtemPacket.Command("_ver", 0, 9, 0, 6),
            };

            foreach (var (id, longName, shortName) in inputs)
            {
                commands.Add(AtemPacket.Command("InPr", InputProperties(id, longName, shortName)));
            }

            commands.Add(AtemPacket.Command("PrgI", 0, 0, 0, (byte)program));
            commands.Add(AtemPacket.Command("PrvI", 0, 0, 0, (byte)preview));
            commands.Add(AtemPacket.Command("TrSS", 0, 0, 0, 0, 1));
            commands.Add(AtemPacket.Command("TMxP", 0, (byte)mixRate));
            commands.Add(AtemPacket.Command("FtbS", 0, (byte)(ftb ? 1 : 0), 0));

            for (var i = 0; i < keys.Length; i++)
            {
                commands.Add(AtemPacket.Command("KeOn", 0, (byte)i, (byte)(keys[i] ? 1 : 0)));
            }

            commands.Add(AtemPacket.Command("TlSr", 0, 3,
                (byte)(program >> 8), (byte)program, 1,
                (byte)(preview >> 8), (byte)preview, 2));

            Send(commands.ToArray());
        }

        void Apply(string name, byte[] data)
        {
            int U16(int at) => at + 1 < data.Length ? (data[at] << 8) | data[at + 1] : 0;

            switch (name)
            {
                case "CPgI":
                    program = U16(2);
                    Send(AtemPacket.Command("PrgI", 0, 0, 0, (byte)program));
                    break;

                case "CPvI":
                    preview = U16(2);
                    Send(AtemPacket.Command("PrvI", 0, 0, 0, (byte)preview));
                    break;

                case "DCut":
                    {
                        (program, preview) = (preview, program);
                        Send(
                            AtemPacket.Command("PrgI", 0, 0, 0, (byte)program),
                            AtemPacket.Command("PrvI", 0, 0, 0, (byte)preview));
                        break;
                    }

                case "DAut":
                    {
                        var target = preview;
                        Send(AtemPacket.Command("TrPs", 0, 1, 0, 0, 0, 0));

                        _ = Task.Delay(1000).ContinueWith(_ =>
                        {
                            (program, preview) = (target, program);
                            Send(
                                AtemPacket.Command("PrgI", 0, 0, 0, (byte)program),
                                AtemPacket.Command("PrvI", 0, 0, 0, (byte)preview),
                                AtemPacket.Command("TrPs", 0, 0, 0, 0, 0, 0));
                        }, TaskScheduler.Default);
                        break;
                    }

                case "FtbA":
                    ftb = !ftb;
                    Send(AtemPacket.Command("FtbS", 0, (byte)(ftb ? 1 : 0), 0));
                    break;

                case "CKOn":
                    if (data.Length >= 3)
                    {
                        keys[data[1]] = data[2] != 0;
                        Send(AtemPacket.Command("KeOn", 0, data[1], data[2]));
                    }
                    break;

                case "CTMx":
                    if (data.Length >= 2)
                    {
                        mixRate = data[1];
                        Send(AtemPacket.Command("TMxP", 0, (byte)mixRate));
                    }
                    break;

                case "CTTp":
                    if (data.Length >= 4 && data[0] == 1)
                    {
                        Send(AtemPacket.Command("TrSS", 0, 0, 0, data[2], data[3]));
                    }
                    break;
            }
        }

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

            if ((header.Flags & AtemPacket.FlagNewSession) != 0)
            {
                lock (gate)
                {
                    try
                    {
                        var opening = AtemPacket.Build(AtemPacket.FlagNewSession, session, 0, packetId++, ReadOnlySpan<byte>.Empty);
                        udp.Send(opening, opening.Length, client);
                    }
                    catch (Exception)
                    {
                        // client went away
                    }
                }

                SendInitial();
                continue;
            }

            if ((header.Flags & AtemPacket.FlagAckRequest) == 0) continue;

            lock (gate)
            {
                try
                {
                    var ack = AtemPacket.Build(AtemPacket.FlagAckReply, session, header.RemoteId, 0, ReadOnlySpan<byte>.Empty);
                    udp.Send(ack, ack.Length, client);
                }
                catch (Exception)
                {
                    // client went away
                }
            }

            var payload = data.AsSpan(AtemPacket.HeaderSize, header.Length - AtemPacket.HeaderSize);
            var offset = 0;

            while (offset + 8 <= payload.Length)
            {
                var length = (payload[offset] << 8) | payload[offset + 1];
                if (length < 8 || offset + length > payload.Length) break;

                var name = Encoding.ASCII.GetString(payload.Slice(offset + 4, 4));
                Apply(name, payload.Slice(offset + 8, length - 8).ToArray());
                offset += length;
            }
        }
    }

    private static byte[] Product()
    {
        var data = new byte[44];
        Encoding.ASCII.GetBytes("ATEM 1 M/E Constellation HD").CopyTo(data, 0);
        return data;
    }

    private static byte[] InputProperties(int id, string longName, string shortName)
    {
        var data = new byte[26];
        data[0] = (byte)(id >> 8);
        data[1] = (byte)(id & 0xFF);
        Encoding.ASCII.GetBytes(longName).CopyTo(data, 2);
        Encoding.ASCII.GetBytes(shortName).CopyTo(data, 22);
        return data;
    }
}
