using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CueVideo.Core;
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
        var window = new MainWindow { Booth = booth };

        window.Show();

        try
        {
            await WaitFor(
                () => SourceTileTexts(window).Contains("PRES MAIN"),
                "The operator screen never showed the configured source row.");

            File.WriteAllText(store.Path, """
            {
              "atemHost": "127.0.0.1",
              "sources": [ { "input": 1, "label": "PRES EDITED" } ]
            }
            """);

            time.Advance(TimeSpan.FromMilliseconds(999));

            await WaitFor(
                () => SourceTileTexts(window).Contains("PRES EDITED"),
                "A hand edit to config.json never rebuilt the source tiles.");

            await WaitFor(
                () => window.FindControl<TextBlock>("StatusText")?.Text == "Connecting to 127.0.0.1…",
                "The status chip never followed the edited switcher address.");
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

    private static async Task WaitFor(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();

            if (condition()) return;

            await Task.Delay(25);
        }

        Assert.Fail(failure);
    }
}
