using CueVideo.Atem;

namespace CueVideo.Tests;

public class AtemProtocolTests
{
    [Fact]
    public void HeaderRoundTrips()
    {
        var payload = AtemPacket.Command("CPvI", 0, 0, 0, 9);
        var packet = AtemPacket.Build(AtemPacket.FlagAckRequest, 0x1234, 0x0007, 0x0042, payload);

        Assert.True(AtemPacket.TryReadHeader(packet, out var header));
        Assert.Equal(AtemPacket.FlagAckRequest, header.Flags);
        Assert.Equal(12 + payload.Length, header.Length);
        Assert.Equal(0x1234, header.Session);
        Assert.Equal(0x0007, header.Ack);
        Assert.Equal(0x0042, header.RemoteId);
    }

    [Fact]
    public void CommandEncodesLengthNameAndData()
    {
        var command = AtemPacket.Command("CPgI", 0, 0, 0, 4);

        Assert.Equal(12, command.Length);
        Assert.Equal(12, (command[0] << 8) | command[1]);
        Assert.Equal("CPgI", System.Text.Encoding.ASCII.GetString(command, 4, 4));
        Assert.Equal(new byte[] { 0, 0, 0, 4 }, command[8..]);
    }

    [Fact]
    public void RejectsShortBuffers()
    {
        Assert.False(AtemPacket.TryReadHeader(new byte[11], out _));
    }

    [Fact]
    public void UpdaterAppliesInputsProgramPreviewAndKeys()
    {
        var updater = new AtemStateUpdater();

        var input = new byte[26];
        input[0] = 0;
        input[1] = 1;
        System.Text.Encoding.UTF8.GetBytes("Camera 1").CopyTo(input, 2);
        System.Text.Encoding.UTF8.GetBytes("CAM1").CopyTo(input, 22);
        updater.Apply("InPr", input);

        updater.Apply("PrgI", [0, 0, 0, 1]);
        updater.Apply("PrvI", [0, 0, 0, 3]);
        updater.Apply("KeOn", [0, 0, 1]);
        updater.Apply("TrSS", [0, 0, 0, 0, 3]);

        var state = updater.Snapshot();
        Assert.Equal("CAM1", state.Inputs[1].Short);
        Assert.Equal("Camera 1", state.Inputs[1].Long);
        Assert.Equal(1, state.Program[0]);
        Assert.Equal(3, state.Preview[0]);
        Assert.True(state.KeyOnAir[0][0]);
        Assert.Equal(3, state.NextSelection[0]);
    }

    [Fact]
    public void ProgramChangeEndsTransition()
    {
        var updater = new AtemStateUpdater();
        var ended = 0;
        updater.TransitionEnded += _ => ended++;

        updater.Apply("PrgI", [0, 0, 0, 1]);
        updater.Apply("TrPs", [0, 1, 0, 0, 0x27, 0x10]); // running, 100% (10000)
        Assert.True(updater.Snapshot().InTransition[0]);

        updater.Apply("PrgI", [0, 0, 0, 2]);
        Assert.False(updater.Snapshot().InTransition[0]);
        Assert.Equal(1, ended);
    }

    [Fact]
    public void InCmMarksReady()
    {
        var updater = new AtemStateUpdater();
        Assert.False(updater.Ready);
        updater.Apply("InCm", ReadOnlySpan<byte>.Empty);
        Assert.True(updater.Ready);
    }

    [Fact]
    public void SnapshotIsIndependent()
    {
        var updater = new AtemStateUpdater();
        updater.Apply("PrgI", [0, 0, 0, 1]);
        var snapshot = updater.Snapshot();
        updater.Apply("PrgI", [0, 0, 0, 2]);

        Assert.Equal(1, snapshot.Program[0]);
        Assert.Equal(2, updater.Snapshot().Program[0]);
    }
}
