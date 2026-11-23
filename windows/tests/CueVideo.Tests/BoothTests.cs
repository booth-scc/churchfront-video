using System.Collections.Concurrent;
using System.Text.Json;
using CueVideo.Atem;
using CueVideo.Core;
using CueVideo.Visca;

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
    public void CameraOnNextFollowsThePreviewBus()
    {
        using var booth = NewBooth(out var switcher);
        booth.Config.Cameras.Clear();
        booth.Config.Cameras.Add(new PtzCameraConfig { Name = "Cam-1", Host = "192.168.10.183", AtemInput = 1 });
        booth.Config.Cameras.Add(new PtzCameraConfig { Name = "Cam-3", Host = "192.168.10.149", AtemInput = 3 });

        var state = new AtemState();
        state.Inputs[1] = new AtemInput(1, "Camera 1", "CAM1");
        state.Inputs[3] = new AtemInput(3, "Camera 3", "CAM3");
        state.Preview[0] = 3;
        switcher.Push(state);

        var onCamera = booth.Render();
        Assert.Equal("Cam-3", onCamera.CameraOnNext?.Name);
        Assert.Equal("Cam-3", onCamera.CameraOnNextLabel);
        Assert.Null(onCamera.CameraOnNextNotice);
        Assert.Null(onCamera.CameraOnNextProblem);

        state.Preview[0] = 2;
        switcher.Push(state);

        var onNothing = booth.Render();
        Assert.Null(onNothing.CameraOnNext);
        Assert.Equal(onNothing.PreviewLabel, onNothing.CameraOnNextLabel);
        Assert.Equal("No camera on Next.", onNothing.CameraOnNextProblem);
    }

    [Fact]
    public void CameraOnNextSaysWhenThePortCannotDriveItsProtocol()
    {
        using var booth = NewBooth(out var switcher);
        booth.Config.Cameras.Clear();
        booth.Config.Cameras.Add(new PtzCameraConfig
        {
            Name = "Cam-Canon",
            Host = "192.168.10.99",
            AtemInput = 1,
            Proto = "canonXC",
        });

        var state = new AtemState();
        state.Preview[0] = 1;
        switcher.Push(state);

        var rendered = booth.Render();

        Assert.Equal("Cam-Canon", rendered.CameraOnNext?.Name);
        Assert.Equal("Canon (HTTP) · unsupported by this port", rendered.CameraOnNextNotice);
        Assert.Equal("Cam-Canon · Canon (HTTP) unsupported by this port.", rendered.CameraOnNextProblem);
    }

    [Fact]
    public async Task CameraCommandsGoThroughTheBoothOwnedLinkOnNext()
    {
        using var fake = new FakeViscaCamera();
        using var booth = NewBooth(out var switcher);
        booth.Config.Cameras.Clear();
        booth.Config.Cameras.Add(new PtzCameraConfig
        {
            Name = "Cam-1",
            Host = "127.0.0.1",
            Port = fake.Port,
            AtemInput = 1,
            FirstPreset = 20,
        });

        var state = new AtemState();
        state.Preview[0] = 1;
        switcher.Push(state);

        var link = booth.LinkOnNext();
        Assert.NotNull(link);
        Assert.Same(link, booth.LinkOnNext());            // one connection per camera on Next
        Assert.Same(link, booth.Cameras.LinkFor(1));       // and the shots rows share it

        link.Drive(1, 0);
        link.Recall(8);
        link.Store(20);
        booth.Cameras.LinkFor(1)!.Recall(9);

        await WaitFor(fake, ViscaCommands.PresetRecall(9, 20));

        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.Drive(1, 0)));
        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.PresetRecall(8, 20)));
        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.PresetStore(20, 20)));
        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.PresetRecall(9, 20)));
    }

    [Fact]
    public async Task ASavedShotSendsTheCameraToItsPlace()
    {
        using var fake = new FakeViscaCamera();
        using var booth = NewBooth(out var switcher);
        booth.Config.Cameras.Clear();
        booth.Config.Cameras.Add(new PtzCameraConfig
        {
            Name = "Cam-1",
            Host = "127.0.0.1",
            Port = fake.Port,
            AtemInput = 1,
            Shots = [new PtzShot { Name = "Stage", Pan = 1200, Tilt = -300, Zoom = 4000 }],
        });

        var state = new AtemState();
        state.Preview[0] = 1;
        switcher.Push(state);

        var button = Assert.Single(booth.Config.CameraForInput(1)!.ShotButtons);
        await booth.RecallShotAsync(1, button);

        await WaitFor(fake, ViscaCommands.AbsolutePanTilt(1200, -300));

        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.AbsolutePanTilt(1200, -300)));
        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.AbsoluteZoom(4000)));
    }

    [Fact]
    public async Task AnExposureShotAppliesItsExposure()
    {
        using var fake = new FakeViscaCamera();
        using var booth = NewBooth(out var switcher);
        booth.Config.Cameras.Clear();
        booth.Config.Cameras.Add(new PtzCameraConfig
        {
            Name = "Cam-1",
            Host = "127.0.0.1",
            Port = fake.Port,
            AtemInput = 1,
            Shots =
            [
                new PtzShot
                {
                    Name = "Dim",
                    Kind = "exposure",
                    Exposure = JsonDocument.Parse("""{ "gain": 6, "shutter": 60, "iris": 400 }""").RootElement.Clone(),
                },
            ],
        });

        var state = new AtemState();
        state.Preview[0] = 1;
        switcher.Push(state);

        var button = Assert.Single(booth.Config.CameraForInput(1)!.ShotButtons);
        await booth.RecallShotAsync(1, button);
        await WaitFor(fake, ViscaCommands.SetGainLevel(6));

        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.SetManualExposure()));
        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.SetShutterPosition(2)));
        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.SetIrisPosition(7)));
        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.SetGainLevel(6)));
    }

    [Fact]
    public async Task AShotWithAnExposureIsAnExposureEvenIfItHasCounts()
    {
        using var fake = new FakeViscaCamera();
        using var booth = NewBooth(out var switcher);
        booth.Config.Cameras.Clear();
        booth.Config.Cameras.Add(new PtzCameraConfig
        {
            Name = "Cam-1",
            Host = "127.0.0.1",
            Port = fake.Port,
            AtemInput = 1,
            Shots =
            [
                new PtzShot
                {
                    Name = "Dim",
                    Pan = 100,
                    Tilt = 200,
                    Zoom = 300,
                    Exposure = JsonDocument.Parse("""{ "gain": 6 }""").RootElement.Clone(),
                },
            ],
        });

        var state = new AtemState();
        state.Preview[0] = 1;
        switcher.Push(state);

        var button = Assert.Single(booth.Config.CameraForInput(1)!.ShotButtons);
        await booth.RecallShotAsync(1, button);
        await WaitFor(fake, ViscaCommands.SetGainLevel(6));

        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.SetGainLevel(6)));
        Assert.DoesNotContain(fake.Received, p => p.SequenceEqual(ViscaCommands.AbsolutePanTilt(100, 200)));
    }

    [Fact]
    public async Task AShotRowPresetFallsBackToTheCameraPreset()
    {
        using var fake = new FakeViscaCamera();
        using var booth = NewBooth(out var switcher);
        booth.Config.Cameras.Clear();
        booth.Config.Cameras.Add(new PtzCameraConfig
        {
            Name = "Cam-1",
            Host = "127.0.0.1",
            Port = fake.Port,
            AtemInput = 1,
            FirstPreset = 20,
            PresetNames = new Dictionary<string, string> { ["8"] = "Full Stage" },
        });

        var state = new AtemState();
        state.Preview[0] = 1;
        switcher.Push(state);

        var button = Assert.Single(booth.Config.CameraForInput(1)!.ShotButtons);
        await booth.RecallShotAsync(1, button);

        await WaitFor(fake, ViscaCommands.PresetRecall(8, 20));

        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.PresetRecall(8, 20)));
    }

    [Fact]
    public async Task ExposureForTheCameraOnNextArrivesInTheSnapshot()
    {
        using var fake = new FakeViscaCamera();
        using var booth = NewBooth(out var switcher);
        booth.Config.Cameras.Clear();
        booth.Config.Cameras.Add(new PtzCameraConfig
        {
            Name = "Cam-1",
            Host = "127.0.0.1",
            Port = fake.Port,
            AtemInput = 1,
        });

        var state = new AtemState();
        state.Preview[0] = 1;
        switcher.Push(state);

        await booth.RefreshExposureAsync();

        var rendered = booth.Render();
        Assert.Equal("6 dB", rendered.Exposure.Gain);
        Assert.Equal("1/60", rendered.Exposure.Shutter);
        Assert.Equal("f/6.8", rendered.Exposure.Iris);
        Assert.Equal("Auto", rendered.Exposure.WhiteBalance);
        Assert.Equal("Cam-1 exposure", rendered.ExposureSubject);
    }

    [Fact]
    public async Task ExposureIsUnknownWhenNothingIsOnNext()
    {
        using var booth = NewBooth(out var switcher);
        booth.Config.Cameras.Clear();

        await booth.RefreshExposureAsync();

        Assert.Equal(ExposureReadout.Unknown, booth.Render().Exposure);
    }

    [Fact]
    public async Task ALateExposureReadForTheOldCameraIsDropped()
    {
        using var slow = new FakeViscaCamera { ReplyDelayMs = 600, GainLevel = 10 };
        using var fast = new FakeViscaCamera { GainLevel = 4 };
        using var booth = NewBooth(out var switcher);
        booth.Config.Cameras.Clear();
        booth.Config.Cameras.Add(new PtzCameraConfig
        {
            Name = "Cam-1",
            Host = "127.0.0.1",
            Port = slow.Port,
            AtemInput = 1,
        });
        booth.Config.Cameras.Add(new PtzCameraConfig
        {
            Name = "Cam-3",
            Host = "127.0.0.1",
            Port = fast.Port,
            AtemInput = 3,
        });

        var state = new AtemState();
        state.Preview[0] = 1;
        switcher.Push(state);

        var stale = booth.RefreshExposureAsync();     // asks the slow camera on 1

        state.Preview[0] = 3;
        switcher.Push(state);

        await booth.RefreshExposureAsync();           // asks the fast camera on 3
        await stale;

        var rendered = booth.Render();
        Assert.Equal(3, rendered.PreviewInput);
        Assert.Equal("4 dB", rendered.Exposure.Gain);
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

    [Fact]
    public void EngineerEditsGoThroughTheBoothAndReachTheOperatorScreen()
    {
        using var booth = NewBooth(out var switcher);
        var changes = 0;
        booth.ConfigChanged += () => changes++;

        var camera = booth.Config.CameraForInput(1)!;
        booth.SetCameraHost(camera.Id, "192.168.10.200");
        booth.SetPicture(2, "CueVideo CAM2");
        booth.SetSwitcherHost("192.168.10.160");

        Assert.Equal("192.168.10.200", booth.Config.CameraForInput(1)!.Host);
        Assert.Equal("CueVideo CAM2", booth.PictureFor(2));
        Assert.Equal("192.168.10.160", booth.Config.AtemHost);
        Assert.Equal("192.168.10.160", switcher.Host);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void EngineerEditsAreWrittenToTheFile()
    {
        using var booth = NewBooth(out _);

        booth.SetPicture(5, "CueVideo CAM5");

        using var saved = new ConfigStore(_directory, readOnly: false);
        saved.Load();
        Assert.Equal("CueVideo CAM5", saved.Config.Pictures["5"]);
    }

    [Fact]
    public async Task AnExternalConfigEditReachesTheOperatorScreen()
    {
        using var booth = NewBooth(out var switcher);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        booth.ConfigChanged += () => changed.TrySetResult();

        var state = new AtemState();
        state.Preview[0] = 3;
        switcher.Push(state);

        File.WriteAllText(Path.Combine(_directory, "config.json"), """
        {
          "atemHost": "192.168.10.159",
          "cameras": [ { "name": "Cam-New", "host": "192.168.10.77", "atemInput": 3 } ]
        }
        """);

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("Cam-New", booth.Render().CameraOnNext?.Name);
    }

    [Fact]
    public async Task AHandEditIsAppliedOnThePostingThread()
    {
        var store = new ConfigStore(_directory, readOnly: false);
        store.Load();
        var switcher = new FakeSwitcher();
        var posted = new ConcurrentQueue<Action>();
        using var booth = new Booth(store, switcher, posted.Enqueue);

        File.WriteAllText(Path.Combine(_directory, "config.json"), """{ "atemHost": "192.168.10.160" }""");

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!posted.TryPeek(out _) && DateTime.UtcNow < deadline) await Task.Delay(25);

        Assert.True(posted.TryDequeue(out var apply), "the setup change was not posted to the UI thread.");
        Assert.Equal("", switcher.Host);                  // the poll thread touched nothing
        apply();
        Assert.Equal("192.168.10.160", switcher.Host);
    }

    [Fact]
    public void ADemoRunAppliesEditsWithoutWritingTheFile()
    {
        var store = new ConfigStore(_directory, readOnly: true);
        store.Load();
        using var booth = new Booth(store, new FakeSwitcher());

        booth.SetPicture(2, "CueVideo CAM2");

        Assert.Equal("CueVideo CAM2", booth.PictureFor(2));
        Assert.False(File.Exists(store.Path));
    }

    private static async Task WaitFor(FakeViscaCamera camera, byte[] command)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && !camera.Received.Any(p => p.SequenceEqual(command)))
        {
            await Task.Delay(25);
        }
    }

    /// <summary>
    /// A Booth over the booth-default config. That config already names Cam-1..4 on inputs
    /// 1-4, so a test named for a camera setup must clear or replace them.
    /// </summary>
    private Booth NewBooth(out FakeSwitcher switcher)
    {
        var store = new ConfigStore(_directory, readOnly: false);
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
