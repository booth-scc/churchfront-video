using System.Buffers.Binary;
using System.Text;

namespace CueVideo.Atem;

/// <summary>Decoded 12-byte ATEM UDP header.</summary>
public readonly record struct AtemHeader(byte Flags, int Length, ushort Session, ushort Ack, ushort RemoteId);

/// <summary>
/// Header and command framing for the ATEM control protocol. The layout matches
/// the hand-rolled implementation in <c>ATEM.swift</c>: five flag bits above an
/// 11-bit length, then session, ack and packet id.
/// </summary>
public static class AtemPacket
{
    public const int HeaderSize = 12;

    public const byte FlagAckRequest = 0x01;
    public const byte FlagNewSession = 0x02;
    public const byte FlagRetransmit = 0x04;
    public const byte FlagRetransmitRequest = 0x08;
    public const byte FlagAckReply = 0x10;

    /// <summary>The 20-byte hello every ATEM session opens with.</summary>
    public static byte[] Hello { get; } =
    [
        0x10, 0x14, 0x53, 0xAB, 0, 0, 0, 0, 0, 0x3A, 0, 0, 0x01, 0, 0, 0, 0, 0, 0, 0
    ];

    public static bool TryReadHeader(ReadOnlySpan<byte> buffer, out AtemHeader header)
    {
        header = default;
        if (buffer.Length < HeaderSize) return false;

        var flags = (byte)(buffer[0] >> 3);
        var length = ((buffer[0] & 0x07) << 8) | buffer[1];
        if (length < HeaderSize) return false;

        header = new AtemHeader(
            flags,
            length,
            BinaryPrimitives.ReadUInt16BigEndian(buffer[2..]),
            BinaryPrimitives.ReadUInt16BigEndian(buffer[4..]),
            BinaryPrimitives.ReadUInt16BigEndian(buffer[10..]));
        return true;
    }

    public static byte[] Build(byte flags, ushort session, ushort ack, ushort localId, ReadOnlySpan<byte> payload)
    {
        var length = HeaderSize + payload.Length;
        var packet = new byte[length];

        packet[0] = (byte)((flags << 3) | ((length >> 8) & 0x07));
        packet[1] = (byte)(length & 0xFF);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), session);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), ack);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10), localId);
        payload.CopyTo(packet.AsSpan(HeaderSize));

        return packet;
    }

    /// <summary>One command: big-endian length, four ASCII bytes, payload.</summary>
    public static byte[] Command(string fourCc, params byte[] data)
    {
        if (fourCc.Length != 4) throw new ArgumentException("ATEM command names are four ASCII characters.", nameof(fourCc));

        var command = new byte[8 + data.Length];
        BinaryPrimitives.WriteUInt16BigEndian(command, (ushort)command.Length);
        Encoding.ASCII.GetBytes(fourCc, command.AsSpan(4, 4));
        data.CopyTo(command, 8);
        return command;
    }
}

/// <summary>Applies ATEM state commands to an <see cref="AtemState"/>.</summary>
public sealed class AtemStateUpdater
{
    private readonly AtemState _state = new();
    private readonly Dictionary<int, double> _lastPosition = new();

    public bool Ready { get; private set; }

    /// <summary>Called when a new session starts; the state stays, the ready flag does not.</summary>
    public void Reset() => Ready = false;

    /// <summary>Raised when a transition finishes so the UI can flip its T-bar rest state.</summary>
    public event Action<int>? TransitionEnded;

    public AtemState Snapshot() => _state.Clone();

    public void Apply(string name, ReadOnlySpan<byte> d)
    {
        switch (name)
        {
            case "InCm":
                Ready = true;
                break;

            case "_ver" when d.Length >= 4:
                _state.Version = $"{U16(d, 0)}.{U16(d, 2)}";
                break;

            case "_pin":
                _state.Product = Str(d, 0, 44);
                break;

            case "InPr" when d.Length >= 26:
                {
                    var id = U16(d, 0);
                    _state.Inputs[id] = new AtemInput(id, Str(d, 2, 20), Str(d, 22, 4));
                    break;
                }

            case "PrgI" when d.Length >= 4:
                {
                    var me = d[0];
                    var source = U16(d, 2);
                    // A finished transition changes program; the switcher does not always
                    // send a final "not in transition" update, so end it here.
                    if (_state.Program.TryGetValue(me, out var current) && current != source &&
                        _state.InTransition.TryGetValue(me, out var running) && running)
                    {
                        EndTransition(me);
                    }

                    _state.Program[me] = source;
                    break;
                }

            case "PrvI" when d.Length >= 4:
                _state.Preview[d[0]] = U16(d, 2);
                break;

            case "TrPs" when d.Length >= 6:
                {
                    var me = d[0];
                    var was = _state.InTransition.TryGetValue(me, out var prev) && prev;
                    var isOn = d[1] != 0;
                    var position = U16(d, 4) / 10000.0;

                    if (was && !isOn) EndTransition(me);

                    _state.InTransition[me] = isOn;
                    _state.Position[me] = position;
                    _lastPosition[me] = isOn ? position : 0;
                    break;
                }

            case "TrSS" when d.Length >= 5:
                _state.Style[d[0]] = d[3];
                _state.NextSelection[d[0]] = d[4];
                break;

            case "TMxP" when d.Length >= 2:
                _state.MixRate[d[0]] = d[1];
                break;

            case "KeOn" when d.Length >= 3:
                GetOrAdd(_state.KeyOnAir, d[0])[d[1]] = d[2] != 0;
                break;

            case "DskS" when d.Length >= 3:
                _state.DskOnAir[d[0]] = d[1] != 0;
                _state.DskInTransition[d[0]] = d[2] != 0;
                break;

            case "DskP" when d.Length >= 2:
                _state.DskTie[d[0]] = d[1] != 0;
                break;

            case "KeBP" when d.Length >= 10:
                GetOrAdd(_state.KeyFill, d[0])[d[1]] = U16(d, 6);
                GetOrAdd(_state.KeyCut, d[0])[d[1]] = U16(d, 8);
                break;

            case "DskB" when d.Length >= 6:
                _state.DskFill[d[0]] = U16(d, 2);
                _state.DskCut[d[0]] = U16(d, 4);
                break;

            case "FtbS" when d.Length >= 3:
                _state.FtbBlack[d[0]] = d[1] != 0;
                _state.FtbInTransition[d[0]] = d[2] != 0;
                break;

            case "AuxS" when d.Length >= 4:
                _state.Aux[d[0]] = U16(d, 2);
                break;

            case "MPrp" when d.Length >= 8:
                {
                    var index = U16(d, 0);
                    if (d[2] != 0)
                    {
                        var name2 = Str(d, 8, U16(d, 4));
                        _state.Macros[index] = name2.Length == 0 ? $"Macro {index + 1}" : name2;
                    }
                    else
                    {
                        _state.Macros.Remove(index);
                    }

                    break;
                }

            case "MRPr" when d.Length >= 4:
                _state.MacroRunning = (d[0] & 1) != 0 ? U16(d, 2) : null;
                break;

            case "StRS" when d.Length >= 2:
                _state.StreamStatus = U16(d, 0);
                _state.HasStreaming = true;
                break;

            case "RTMS" when d.Length >= 2:
                _state.RecordStatus = U16(d, 0);
                _state.HasRecording = true;
                break;

            case "TlSr" when d.Length >= 2:
                {
                    var count = U16(d, 0);
                    var tally = new Dictionary<int, byte>();
                    for (var i = 0; i < count && 2 + i * 3 + 2 < d.Length; i++)
                    {
                        tally[U16(d, 2 + i * 3)] = d[2 + i * 3 + 2];
                    }

                    _state.Tally.Clear();
                    foreach (var (key, value) in tally) _state.Tally[key] = value;
                    break;
                }
        }
    }

    private void EndTransition(int me)
    {
        if (_lastPosition.TryGetValue(me, out var position) && position > 0.5) TransitionEnded?.Invoke(me);
        _state.InTransition[me] = false;
        _state.Position[me] = 0;
        _lastPosition[me] = 0;
    }

    private static Dictionary<int, TValue> GetOrAdd<TValue>(Dictionary<int, Dictionary<int, TValue>> map, int key)
    {
        if (!map.TryGetValue(key, out var inner))
        {
            inner = new Dictionary<int, TValue>();
            map[key] = inner;
        }

        return inner;
    }

    private static int U16(ReadOnlySpan<byte> d, int i) =>
        i + 1 < d.Length ? (d[i] << 8) | d[i + 1] : 0;

    private static string Str(ReadOnlySpan<byte> d, int start, int length)
    {
        if (start >= d.Length) return "";
        var end = Math.Min(start + length, d.Length);
        var slice = d[start..end];
        var nul = slice.IndexOf((byte)0);
        if (nul >= 0) slice = slice[..nul];
        return Encoding.UTF8.GetString(slice);
    }
}
