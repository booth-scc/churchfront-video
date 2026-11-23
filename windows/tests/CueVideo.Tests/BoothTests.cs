using CueVideo.Atem;
using CueVideo.Core;

namespace CueVideo.Tests;

/// <summary>
/// The Operator screen renders from one Core snapshot. These tests drive a fake switcher
/// and read that snapshot, so no window or Avalonia type is involved.
/// </summary>
public sealed class BoothTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cuevideo-booth-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch (Exception) { /* best effort */ }
    }

    [Fact]
    public void OneRenderPathShowsProgramPreviewTallyAndStatus()
    {
        using var booth = NewBooth(out var switcher);

        var state = new AtemState();
        state.Inputs[1] = new AtemInput(1, "Camera 1", "CAM1");
        state.Inputs[3] = new AtemInput(3, "Camera 3", "CAM3");
        state.Program[0] = 1;
        state.Preview[0] = 3;
        state.Tally[1] = 1;
        state.Tally[3] = 2;
        state.MixRate[0] = 30;
        state.FtbBlack[0] = true;

        switcher.Connect("192.168.10.159");
        switcher.Push(state);
        switcher.SetLink(new AtemLink(AtemLinkState.Online));

        var rendered = booth.Render();

        Assert.Equal(1, rendered.ProgramInput);
        Assert.Equal("CAM1", rendered.ProgramLabel);
        Assert.Equal(3, rendered.PreviewInput);
        Assert.Equal("CAM3", rendered.PreviewLabel);

        Assert.Equal(new SourceTally(Live: true, Next: false), rendered.TallyFor(1));
        Assert.Equal(new SourceTally(Live: false, Next: true), rendered.TallyFor(3));
        Assert.Equal(new SourceTally(Live: false, Next: false), rendered.TallyFor(2));

        Assert.Equal(BoothStatusTone.Ready, rendered.Status.Tone);
        Assert.Equal("Ready", rendered.Status.Text);

        Assert.Equal(1.0, rendered.FadeSeconds, 3);
        Assert.True(rendered.Black);
    }

    [Fact]
    public void SwitcherChangesArriveAsRenderedState()
    {
        using var booth = NewBooth(out var switcher);
        var rendered = new List<BoothSnapshot>();
        booth.Rendered += rendered.Add;

        var state = new AtemState();
        state.Inputs[2] = new AtemInput(2, "Camera 2", "CAM2");
        state.Program[0] = 2;
        state.Preview[0] = 4;

        switcher.Connect("192.168.10.159");
        switcher.Push(state);

        var afterState = rendered[^1];
        Assert.Equal(2, afterState.ProgramInput);
        Assert.Equal("CAM2", afterState.ProgramLabel);
        Assert.Equal(4, afterState.PreviewInput);

        switcher.SetLink(new AtemLink(AtemLinkState.Online));

        var afterLink = rendered[^1];
        Assert.Equal(BoothStatusTone.Ready, afterLink.Status.Tone);
        Assert.Equal(2, afterLink.ProgramInput);
    }

    [Theory]
    [InlineData(AtemLinkState.Connecting, BoothStatusTone.Connecting, "Connecting to 192.168.10.159…")]
    [InlineData(AtemLinkState.Online, BoothStatusTone.Ready, "Ready")]
    [InlineData(AtemLinkState.Offline, BoothStatusTone.Offline, "Switcher offline")]
    public void StatusNamesTheSwitcherLink(AtemLinkState state, BoothStatusTone tone, string text)
    {
        using var booth = NewBooth(out var switcher);

        switcher.Connect("192.168.10.159");
        switcher.SetLink(new AtemLink(state));

        var rendered = booth.Render();

        Assert.Equal(tone, rendered.Status.Tone);
        Assert.Equal(text, rendered.Status.Text);
    }

    [Fact]
    public void OperatorCommandsUseTheConfiguredMe()
    {
        using var booth = NewBooth(out var switcher);
        booth.Config.Me = 2;

        booth.SetPreview(4);
        booth.Cut();
        booth.Auto();
        booth.FadeToBlack();

        Assert.Equal(
            new[] { "preview 4 on 1", "cut 1", "auto 1", "black 1" },
            switcher.Commands);
    }

    [Fact]
    public void RendersTheConfiguredMe()
    {
        using var booth = NewBooth(out var switcher);
        booth.Config.Me = 2;

        var state = new AtemState();
        state.Inputs[1] = new AtemInput(1, "Camera 1", "CAM1");
        state.Inputs[2] = new AtemInput(2, "Camera 2", "CAM2");
        state.Program[1] = 2;
        state.Preview[1] = 1;
        switcher.Push(state);

        var rendered = booth.Render();

        Assert.Equal(2, rendered.ProgramInput);
        Assert.Equal("CAM2", rendered.ProgramLabel);
        Assert.Equal(1, rendered.PreviewInput);
        Assert.Equal("CAM1", rendered.PreviewLabel);
    }

    [Fact]
    public void KeyButtonsToggleTheConfiguredKeyAndDsk()
    {
        using var booth = NewBooth(out var switcher);
        var state = new AtemState();
        state.KeyOnAir[0] = new Dictionary<int, bool> { [0] = false };
        state.DskOnAir[1] = true;
        switcher.Push(state);

        booth.Toggle(new KeyButtonConfig { Label = "KEY 1", Me = 1, Keyer = 1 });
        booth.Toggle(new KeyButtonConfig { Label = "DSK 2", Dsk = 2 });

        Assert.Equal(new[] { "key 0 True on 0", "dsk 1 False" }, switcher.Commands);
    }

    [Fact]
    public void StatusSaysWhenNoSwitcherAddressIsSet()
    {
        using var booth = NewBooth(out _);

        var status = booth.Render().Status;

        Assert.Equal(BoothStatusTone.Starting, status.Tone);
        Assert.Equal("No switcher address", status.Text);
    }

    [Fact]
    public void OfflineStatusKeepsTheSwitcherReason()
    {
        using var booth = NewBooth(out var switcher);

        switcher.SetLink(new AtemLink(AtemLinkState.Offline, "No answer from 192.168.10.159"));

        Assert.Equal("No answer from 192.168.10.159", booth.Render().Status.Text);
    }

    private Booth NewBooth(out FakeSwitcher switcher)
    {
        var store = new ConfigStore(_directory);
        store.Load();
        switcher = new FakeSwitcher();
        return new Booth(store, switcher);
    }
}

/// <summary>A switcher that pushes state on demand instead of speaking UDP.</summary>
internal sealed class FakeSwitcher : ISwitcher
{
    public AtemLink Link { get; private set; } = new(AtemLinkState.Idle);
    public AtemState State { get; private set; } = new();
    public string Host { get; private set; } = "";
    public List<string> Commands { get; } = [];

    public event Action<AtemLink>? LinkChanged;
    public event Action<AtemState>? StateChanged;

    public void Connect(string host, int port = 9910)
    {
        Host = host;
        SetLink(new AtemLink(AtemLinkState.Connecting));
    }

    public void Push(AtemState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public void SetLink(AtemLink link)
    {
        Link = link;
        LinkChanged?.Invoke(link);
    }

    public void SetPreview(int source, int me = 0) => Commands.Add($"preview {source} on {me}");

    public void Cut(int me = 0) => Commands.Add($"cut {me}");

    public void Auto(int me = 0) => Commands.Add($"auto {me}");

    public void FadeToBlack(int me = 0) => Commands.Add($"black {me}");

    public void SetKeyOnAir(int keyer, bool on, int me = 0) => Commands.Add($"key {keyer} {on} on {me}");

    public void SetDskOnAir(int dsk, bool on) => Commands.Add($"dsk {dsk} {on}");

    public void Dispose() { }
}
