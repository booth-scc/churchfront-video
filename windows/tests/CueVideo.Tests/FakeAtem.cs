using System.Net;
using System.Net.Sockets;
using CueVideo.Atem;

namespace CueVideo.Tests;

/// <summary>
/// Minimal ATEM stand-in for protocol tests: answers the hello with a new
/// session and then pushes InCm, program and preview. Same framing as the
/// real switcher, so it exercises handshake, ack and parsing without hardware.
/// </summary>
internal sealed class FakeAtem : IDisposable
{
    private readonly UdpClient _udp;
    private readonly Thread _thread;
    private IPEndPoint _client = new(IPAddress.Any, 0);
    private volatile bool _running = true;

    public int Port { get; }

    public FakeAtem()
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        _thread = new Thread(Loop) { IsBackground = true, Name = "fake-atem" };
        _thread.Start();
    }

    private void Loop()
    {
        _udp.Client.ReceiveTimeout = 50;
        ushort packetId = 1;

        while (_running)
        {
            byte[] data;
            try
            {
                data = _udp.Receive(ref _client);
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

            if (data.Length < AtemPacket.HeaderSize) continue;
            if (!AtemPacket.TryReadHeader(data, out var header)) continue;

            // Only the hello opens the session; acks from the client are ignored.
            if ((header.Flags & AtemPacket.FlagNewSession) == 0) continue;

            Send(AtemPacket.Build(AtemPacket.FlagNewSession, 0x53AB, 0, packetId++, ReadOnlySpan<byte>.Empty));

            var payload = new[]
                {
                    AtemPacket.Command("InCm"),
                    AtemPacket.Command("InPr", InputProperties(1, "Camera 1", "CAM1")),
                    AtemPacket.Command("PrgI", 0, 0, 0, 1),
                    AtemPacket.Command("PrvI", 0, 0, 0, 3),
                }
                .SelectMany(x => x)
                .ToArray();

            Send(AtemPacket.Build(AtemPacket.FlagAckRequest, 0x53AB, 0, packetId++, payload));
        }
    }

    private static byte[] InputProperties(int id, string longName, string shortName)
    {
        var data = new byte[26];
        data[0] = (byte)(id >> 8);
        data[1] = (byte)(id & 0xFF);
        System.Text.Encoding.UTF8.GetBytes(longName).CopyTo(data, 2);
        System.Text.Encoding.UTF8.GetBytes(shortName).CopyTo(data, 22);
        return data;
    }

    private void Send(byte[] packet)
    {
        try
        {
            _udp.Send(packet, packet.Length, _client);
        }
        catch (Exception)
        {
            // Client went away mid-test.
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _thread.Join(500); } catch (Exception) { /* shutting down */ }
        _udp.Dispose();
    }
}
