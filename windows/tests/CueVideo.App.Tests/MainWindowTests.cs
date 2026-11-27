using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CueVideo.Core;
using CueVideo.Ndi;
using CueVideo.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace CueVideo.App.Tests;

/// <summary>
/// The window is the adapter the Core tests cannot see: these tests boot it headless and
/// check that a setup change reaches the controls the operator reads.
/// </summary>
public sealed class MainWindowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cuevideo-window-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch (Exception) { /* best effort */ }
    }

    [AvaloniaFact]
    public async Task AHandEditRebuildsTheOperatorTilesAndStatus()
    {
        var time = new FakeTimeProvider();
        var store = new ConfigStore(_directory, readOnly: false, time: time);
        using var booth = new Booth(store, new RecordingSwitcher(), action => Dispatcher.UIThread.Post(action));
        using var hub = new FakeNdi().CreateHub();
        var window = new MainWindow { Booth = booth, NdiHub = hub };

        window.Show();

        try
        {
            await TestWait.Until(
                () => SourceTileTexts(window).Contains("PRES MAIN"),
                "The operator screen never showed the configured source row.",
                () => Dispatcher.UIThread.RunJobs());

            File.WriteAllText(store.Path, """
            {
              "atemHost": "127.0.0.1",
              "sources": [ { "input": 1, "label": "PRES EDITED" } ]
            }
            """);

            time.Advance(TimeSpan.FromMilliseconds(999));

            await TestWait.Until(
                () => SourceTileTexts(window).Contains("PRES EDITED"),
                "A hand edit to config.json never rebuilt the source tiles.",
                () => Dispatcher.UIThread.RunJobs());

            await TestWait.Until(
                () => window.FindControl<TextBlock>("StatusText")?.Text == "Connecting to 127.0.0.1…",
                "The status chip never followed the edited switcher address.",
                () => Dispatcher.UIThread.RunJobs());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SixTiledSourcesOpenSixReceivers()
    {
        var time = new FakeTimeProvider();
        var store = new ConfigStore(_directory, readOnly: false, time: time);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.Path, """
        {
          "atemHost": "127.0.0.1",
          "sources": [
            { "input": 1, "label": "CAM 1" },
            { "input": 2, "label": "CAM 2" },
            { "input": 3, "label": "CAM 3" },
            { "input": 4, "label": "CAM 4" },
            { "input": 5, "label": "CAM 5" },
            { "input": 6, "label": "CAM 6" }
          ],
          "pictures": {
            "1": "CueVideo CAM1",
            "2": "CueVideo CAM2",
            "3": "CueVideo CAM3",
            "4": "CueVideo CAM4",
            "5": "CueVideo CAM5",
            "6": "CueVideo CAM6"
          }
        }
        """);

        var ndi = new FakeNdi("CueVideo CAM1", "CueVideo CAM2", "CueVideo CAM3",
            "CueVideo CAM4", "CueVideo CAM5", "CueVideo CAM6");
        using var hub = ndi.CreateHub();
        using var booth = new Booth(store, new RecordingSwitcher(), action => Dispatcher.UIThread.Post(action));
        var window = new MainWindow { Booth = booth, NdiHub = hub };

        window.Show();

        try
        {
            await TestWait.Until(
                () => ndi.OpenReceiverCount == 6,
                $"The six tiled sources opened {ndi.OpenReceiverCount} receivers, not six.",
                () => Dispatcher.UIThread.RunJobs());

            Assert.Equal(6, ndi.OpenReceiverCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TwoTilesOnOneSourceShareOneReceiver()
    {
        var time = new FakeTimeProvider();
        var store = new ConfigStore(_directory, readOnly: false, time: time);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.Path, """
        {
          "atemHost": "127.0.0.1",
          "sources": [
            { "input": 1, "label": "CAM 1" },
            { "input": 6, "label": "CAM 6" }
          ],
          "pictures": {
            "1": "CueVideo CAM1",
            "6": "CueVideo CAM1"
          }
        }
        """);

        var ndi = new FakeNdi("CueVideo CAM1");
        using var hub = ndi.CreateHub();
        using var booth = new Booth(store, new RecordingSwitcher(), action => Dispatcher.UIThread.Post(action));
        var window = new MainWindow { Booth = booth, NdiHub = hub };

        window.Show();

        try
        {
            await TestWait.Until(
                () => SourceTileTexts(window).Contains("CAM 6") &&
                    ndi.Receivers.Count == 1 &&
                    ndi.Receivers[0].Captures > 0,
                "Two tiles on one source did not share one receiver.",
                () => Dispatcher.UIThread.RunJobs());

            Assert.Single(ndi.Receivers);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AConfigEditReleasesTheFeedItReplaced()
    {
        var time = new FakeTimeProvider();
        var store = new ConfigStore(_directory, readOnly: false, time: time);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.Path, """
        {
          "atemHost": "127.0.0.1",
          "sources": [ { "input": 4, "label": "PRES" } ],
          "pictures": { "4": "CueVideo CAM4" }
        }
        """);

        var ndi = new FakeNdi("CueVideo CAM4");
        using var hub = ndi.CreateHub();
        using var booth = new Booth(store, new RecordingSwitcher(), action => Dispatcher.UIThread.Post(action));
        var window = new MainWindow { Booth = booth, NdiHub = hub };

        window.Show();

        try
        {
            await TestWait.Until(
                () => ndi.OpenReceiverCount == 1,
                "The tiled source never opened a receiver.",
                () => Dispatcher.UIThread.RunJobs());

            var receiver = ndi.Receivers[0];

            File.WriteAllText(store.Path, """
            {
              "atemHost": "127.0.0.1",
              "sources": [ { "input": 4, "label": "PRES" } ]
            }
            """);
            time.Advance(TimeSpan.FromMilliseconds(999));

            await TestWait.Until(
                () => receiver.IsDisposed,
                "The receiver stayed open after its tile was removed.",
                () => Dispatcher.UIThread.RunJobs());

            Assert.Equal(0, ndi.OpenReceiverCount);
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<string> SourceTileTexts(Window window) =>
        (window.FindControl<StackPanel>("SourceTiles")?.GetVisualDescendants().OfType<TextBlock>()
            ?? Enumerable.Empty<TextBlock>())
        .Select(block => block.Text ?? "");
}
