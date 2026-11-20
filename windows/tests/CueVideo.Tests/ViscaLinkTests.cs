using CueVideo.Visca;

namespace CueVideo.Tests;

public class ViscaLinkTests
{
    [Fact]
    public async Task ReadsCountsAndDegrees()
    {
        using var camera = new FakeViscaCamera();
        using var link = new ViscaIpLink("127.0.0.1", camera.Port);
        link.Start();

        var counts = await link.ReadPositionAsync(counts: true, TimeSpan.FromSeconds(2));

        Assert.Equal(new PtzPosition(1000, -200, 4096), counts);

        var degrees = await link.ReadPositionAsync(counts: false, TimeSpan.FromSeconds(2));

        Assert.Equal(link.Units.ToPosition(1000, -200, 4096), degrees);
        Assert.True(degrees!.Value.Pan > 6000); // 1000 counts is roughly 69 degrees
    }

    [Fact]
    public async Task SendsAbsoluteMoveForGoTo()
    {
        using var camera = new FakeViscaCamera();
        using var link = new ViscaIpLink("127.0.0.1", camera.Port);
        link.Start();

        await link.ReadPositionAsync(counts: true, TimeSpan.FromSeconds(2));
        link.GoTo(new PtzPosition(500, -500, 2000));

        var expectedPanTilt = ViscaCommands.AbsolutePanTilt(500, -500);
        var expectedZoom = ViscaCommands.AbsoluteZoom(2000);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline &&
               !camera.Received.Any(p => p.SequenceEqual(expectedPanTilt)))
        {
            await Task.Delay(25);
        }

        Assert.Contains(camera.Received, p => p.SequenceEqual(expectedPanTilt));
        Assert.Contains(camera.Received, p => p.SequenceEqual(expectedZoom));
    }

    [Fact]
    public async Task ReadsExposureAndMapsTables()
    {
        using var camera = new FakeViscaCamera();
        using var link = new ViscaIpLink("127.0.0.1", camera.Port);
        link.Start();

        var exposure = await link.ReadExposureAsync(TimeSpan.FromSeconds(3));

        Assert.NotNull(exposure);
        Assert.True(exposure!.HasExposure);
        Assert.Equal(60, exposure.Shutter);        // position 2 is 1/60 in the PTZOptics table
        Assert.Equal(680, exposure.Iris);          // position 4 is f/6.8
        Assert.Equal(6, exposure.Gain);
        Assert.Equal("auto", exposure.WhiteBalance);
        Assert.Equal(4100, exposure.Kelvin);       // base 2500 + 0x10 steps of 100
        Assert.False(exposure.GainAuto);
    }

    [Fact]
    public async Task StoreAndRecallUseConfiguredFirstPreset()
    {
        using var camera = new FakeViscaCamera();
        using var link = new ViscaIpLink("127.0.0.1", camera.Port, firstPreset: 20);
        link.Start();

        await Task.Delay(150);
        link.Store(1);
        link.Recall(8);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && camera.Received.Count < 2) await Task.Delay(25);

        Assert.Contains(camera.Received, p => p.SequenceEqual(ViscaCommands.PresetStore(1, 20)));
        Assert.Contains(camera.Received, p => p.SequenceEqual(ViscaCommands.PresetRecall(8, 20)));
    }

    [Fact]
    public async Task PositionTimesOutWhenTheCameraStaysSilent()
    {
        using var link = new ViscaIpLink("127.0.0.1", 9); // discard port
        link.Start();

        var position = await link.ReadPositionAsync(counts: true, TimeSpan.FromMilliseconds(300));

        Assert.Null(position);
    }
}
