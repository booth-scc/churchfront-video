using CueVideo.Ndi;

namespace CueVideo.Tests;

public class NdiTests
{
    [Fact]
    public void RuntimeLoadsWhenInstalled()
    {
        if (!NdiRuntime.Initialize())
        {
            // Machines without NDI Tools skip; the booth PC has it.
            return;
        }

        Assert.True(NdiRuntime.IsAvailable);
        Assert.NotNull(NdiRuntime.RuntimePath);
    }

    [Fact]
    public async Task LocalSenderIsFoundAndFramesArrive()
    {
        if (!NdiRuntime.Initialize()) return;

        using var sender = NdiSender.Create("CueVideo Loopback Test");
        using var finder = NdiFinder.Create(showLocalSources: true);

        NdiSourceInfo? source = null;
        var discoverDeadline = DateTime.UtcNow.AddSeconds(10);

        while (source is null && DateTime.UtcNow < discoverDeadline)
        {
            source = finder.GetSources(TimeSpan.FromMilliseconds(500))
                .FirstOrDefault(s => s.Name.Contains("CueVideo Loopback Test", StringComparison.OrdinalIgnoreCase));

            if (source is null) await Task.Delay(100);
        }

        Assert.NotNull(source);

        using var receiver = NdiReceiver.Open(source.Name, lowestBandwidth: false);

        const int width = 64;
        const int height = 36;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 40;      // B
            pixels[i + 1] = 200; // G
            pixels[i + 2] = 90;  // R
            pixels[i + 3] = 255; // X
        }

        var received = false;
        var receivedWidth = 0;
        var receivedHeight = 0;
        var firstByte = -1;

        var pump = Task.Run(() =>
        {
            var until = DateTime.UtcNow.AddSeconds(8);
            while (!received && DateTime.UtcNow < until)
            {
                sender.Send(pixels, width, height, width * 4, 30, 1);
                Thread.Sleep(20);
            }
        });

        var captureDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!received && DateTime.UtcNow < captureDeadline)
        {
            receiver.TryCapture(100, (in NdiFrameView frame) =>
            {
                received = true;
                receivedWidth = frame.Width;
                receivedHeight = frame.Height;
                firstByte = frame.Pixels[0];
            });
        }

        await pump;

        Assert.True(received, "No NDI frame arrived from the local sender.");
        Assert.Equal(width, receivedWidth);
        Assert.Equal(height, receivedHeight);
        Assert.Equal(40, firstByte);
    }
}
