using CueVideo.Atem;

namespace CueVideo.Tests;

public class AtemClientTests
{
    [Fact]
    public async Task HandshakesAndAppliesInitialState()
    {
        using var switcher = new FakeAtem();
        using var client = new AtemClient();

        var online = new TaskCompletionSource();
        client.LinkChanged += link =>
        {
            if (link.State == AtemLinkState.Online) online.TrySetResult();
        };

        client.Connect("127.0.0.1", switcher.Port);

        var finished = await Task.WhenAny(online.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(online.Task, finished);
        Assert.Equal(AtemLinkState.Online, client.Link.State);

        var state = client.State;
        Assert.Equal(1, state.Program[0]);
        Assert.Equal(3, state.Preview[0]);
        Assert.Equal("CAM1", state.Name(1));
    }

    [Fact]
    public async Task GoesOfflineWhenNobodyAnswers()
    {
        using var client = new AtemClient();
        var offline = new TaskCompletionSource();
        client.LinkChanged += link =>
        {
            if (link.State == AtemLinkState.Offline) offline.TrySetResult();
        };

        client.Connect("127.0.0.1", 9); // discard port, nothing listens

        var finished = await Task.WhenAny(offline.Task, Task.Delay(TimeSpan.FromSeconds(6)));
        Assert.Same(offline.Task, finished);
    }
}
