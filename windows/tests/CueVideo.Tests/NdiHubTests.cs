using CueVideo.Ndi;
using CueVideo.TestSupport;

namespace CueVideo.Tests;

public sealed class NdiHubTests
{
    [Fact]
    public async Task OneSourceShownTwiceOpensOneReceiver()
    {
        var ndi = new FakeNdi("STUDIO-PC (Cam 1)");
        using var hub = ndi.CreateHub();
        var first = new FrameCounter();
        var second = new FrameCounter();

        using var firstSubscription = hub.Subscribe("STUDIO-PC (Cam 1)", first.OnFrame);
        using var secondSubscription = hub.Subscribe("STUDIO-PC (Cam 1)", second.OnFrame);

        await TestWait.Until(
            () => first.Count > 0 && second.Count > 0,
            "Both subscriptions never received a frame.");

        Assert.Equal(1, ndi.OpenReceiverCount);
    }

    [Fact]
    public async Task TheReceiverStaysOpenUntilTheLastSubscriptionLeaves()
    {
        var ndi = new FakeNdi("STUDIO-PC (Cam 1)");
        using var hub = ndi.CreateHub();

        var first = hub.Subscribe("STUDIO-PC (Cam 1)", (in NdiFrameView _) => { });
        var second = hub.Subscribe("STUDIO-PC (Cam 1)", (in NdiFrameView _) => { });

        await TestWait.Until(
            () => ndi.Receivers.Count == 1 && ndi.Receivers[0].Captures > 0,
            "The shared receiver never started.");

        first.Dispose();
        await Task.Delay(100);
        Assert.False(ndi.Receivers[0].IsDisposed, "The receiver closed while a subscription still wanted it.");

        second.Dispose();
        await TestWait.Until(
            () => ndi.Receivers[0].IsDisposed,
            "The receiver stayed open after the last subscription left.");
    }

    [Fact]
    public async Task AQuickResubscribeKeepsTheSameReceiver()
    {
        var ndi = new FakeNdi("STUDIO-PC (Cam 1)");
        using var hub = ndi.CreateHub();

        var first = hub.Subscribe("STUDIO-PC (Cam 1)", (in NdiFrameView _) => { });
        await TestWait.Until(
            () => ndi.Receivers.Count == 1 && ndi.Receivers[0].Captures > 0,
            "The shared receiver never started.");
        var receiver = ndi.Receivers[0];

        first.Dispose();
        using var second = hub.Subscribe("STUDIO-PC (Cam 1)", (in NdiFrameView _) => { });

        await Task.Delay(700);
        Assert.False(receiver.IsDisposed, "The feed reopened instead of handing the same receiver to the new subscriber.");
        Assert.Single(ndi.Receivers);
    }

    [Fact]
    public async Task TheRemainingSubscriptionKeepsReceivingAfterItsPeerLeaves()
    {
        var ndi = new FakeNdi("STUDIO-PC (Cam 1)");
        using var hub = ndi.CreateHub();
        var first = new FrameCounter();
        var second = new FrameCounter();

        var firstSubscription = hub.Subscribe("STUDIO-PC (Cam 1)", first.OnFrame);
        using var secondSubscription = hub.Subscribe("STUDIO-PC (Cam 1)", second.OnFrame);

        await TestWait.Until(
            () => first.Count > 0 && second.Count > 0,
            "Both subscriptions never received a frame.");

        firstSubscription.Dispose();

        var before = second.Count;
        await TestWait.Until(
            () => second.Count > before + 2,
            "The surviving subscription stopped receiving frames.");
        Assert.Equal(1, ndi.OpenReceiverCount);
    }

    [Fact]
    public async Task OneBrokenSubscriberDoesNotStopTheOther()
    {
        var ndi = new FakeNdi("STUDIO-PC (Cam 1)");
        using var hub = ndi.CreateHub();
        var healthy = new FrameCounter();

        using var broken = hub.Subscribe(
            "STUDIO-PC (Cam 1)",
            (in NdiFrameView _) => throw new InvalidOperationException("broken subscriber"));
        using var other = hub.Subscribe("STUDIO-PC (Cam 1)", healthy.OnFrame);

        await TestWait.Until(
            () => healthy.Count > 0,
            "The healthy subscription never received a frame past the broken one.");
        Assert.Equal(1, ndi.OpenReceiverCount);
    }

    [Fact]
    public async Task DifferentSourcesOpenTheirOwnReceivers()
    {
        var ndi = new FakeNdi("STUDIO-PC (Cam 1)", "STUDIO-PC (Cam 2)");
        using var hub = ndi.CreateHub();
        var first = new FrameCounter();
        var second = new FrameCounter();

        using var firstSubscription = hub.Subscribe("STUDIO-PC (Cam 1)", first.OnFrame);
        using var secondSubscription = hub.Subscribe("STUDIO-PC (Cam 2)", second.OnFrame);

        await TestWait.Until(
            () => first.Count > 0 && second.Count > 0,
            "Both sources never received a frame.");
        Assert.Equal(2, ndi.OpenReceiverCount);
    }

    [Fact]
    public async Task AWaitingFeedReportsTheSourcesItCanSee()
    {
        var ndi = new FakeNdi("STUDIO-PC (Other)");
        using var hub = ndi.CreateHub();
        NdiFeedStatus? status = null;

        using var subscription = hub.Subscribe(
            "STUDIO-PC (Cam 1)",
            (in NdiFrameView _) => { },
            value => Volatile.Write(ref status, value));

        await TestWait.Until(
            () => Volatile.Read(ref status) is not null,
            "The waiting feed never reported the sources it could see.");

        var reported = Volatile.Read(ref status)!;
        Assert.Null(reported.Error);
        Assert.Contains("Other", reported.SeenSources);
    }

    private sealed class FrameCounter
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void OnFrame(in NdiFrameView frame) => Interlocked.Increment(ref _count);
    }
}
