using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CueVideo.TestSupport;

namespace CueVideo.App.Tests;

/// <summary>Checks the adapter: an NdiView subscribes to the shared hub instead of owning a receiver.</summary>
public sealed class NdiViewTests
{
    [AvaloniaFact]
    public async Task TwoViewsOfOneSourceShareOneReceiverAndBothShowIt()
    {
        var ndi = new FakeNdi("STUDIO-PC (Cam 1)");
        using var hub = ndi.CreateHub();
        var first = new NdiView(hub, "STUDIO-PC (Cam 1)", "");
        var second = new NdiView(hub, "STUDIO-PC (Cam 1)", "");
        var window = new Window { Content = new StackPanel { Children = { first, second } } };

        window.Show();

        try
        {
            first.Start();
            second.Start();

            await TestWait.Until(
                () => HasPicture(first) && HasPicture(second),
                $"Both views never showed a picture (opens={ndi.OpenReceiverCount}, " +
                    $"captures={ndi.Receivers.FirstOrDefault()?.Captures ?? -1}).",
                () => Dispatcher.UIThread.RunJobs());
            Assert.Equal(1, ndi.OpenReceiverCount);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
            window.Close();
        }

        await TestWait.Until(
            () => ndi.Receivers.All(receiver => receiver.IsDisposed),
            "A receiver stayed open after both views closed.",
            () => Dispatcher.UIThread.RunJobs());
    }

    private static bool HasPicture(NdiView view) =>
        view.Children.OfType<Image>().Any(image => image.Source is not null);
}
