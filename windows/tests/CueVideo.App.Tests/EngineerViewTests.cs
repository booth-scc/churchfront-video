using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CueVideo.Core;
using CueVideo.TestSupport;

namespace CueVideo.App.Tests;

/// <summary>
/// The Engineer screen is the adapter the matching tests cannot see: a scan of the network
/// must select the sender a saved source now resolves to.
/// </summary>
public sealed class EngineerViewTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "cuevideo-engineer-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch (Exception) { /* best effort */ }
    }

    [AvaloniaFact]
    public async Task ScanSelectsTheSenderUnderItsRenamedComputer()
    {
        Directory.CreateDirectory(_directory);
        var store = new ConfigStore(_directory, readOnly: false);
        File.WriteAllText(store.Path, """
        {
          "atemHost": "127.0.0.1",
          "sources": [ { "input": 1, "label": "SIDE" } ],
          "pictures": { "1": "PRODUCTION-01S-MAC-MINI.LOCAL (CueScreens Side Screens)" }
        }
        """);

        var ndi = new FakeNdi("PRODUCTN01SMINI (CueScreens Side Screens)");
        using var booth = new Booth(store, new RecordingSwitcher(), action => Dispatcher.UIThread.Post(action));
        booth.Start();
        using var view = new EngineerView(booth, ndi.CreateFinder);
        var window = new Window { Content = view };

        window.Show();

        try
        {
            await TestWait.Until(
                () => FeedCombos(window).Any(combo =>
                    Equals(combo.SelectedItem, "PRODUCTN01SMINI (CueScreens Side Screens)")),
                "The Engineer feed list kept the saved computer name instead of the sender's new one.",
                () => Dispatcher.UIThread.RunJobs());
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<ComboBox> FeedCombos(Window window) =>
        window.GetVisualDescendants().OfType<ComboBox>();
}
