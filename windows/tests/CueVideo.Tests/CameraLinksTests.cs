using CueVideo.Core;
using CueVideo.Visca;

namespace CueVideo.Tests;

public class CameraLinksTests
{
    [Fact]
    public async Task SendsNothingToACameraOnAnUnsupportedProtocol()
    {
        using var fake = new FakeViscaCamera();
        var canon = new PtzCameraConfig
        {
            Name = "Cam-Canon",
            Host = "127.0.0.1",
            Port = fake.Port,
            Proto = "canonXC",
        };

        using var link = CameraLinks.Open(canon);

        Assert.Null(link);
        await Task.Delay(250);
        Assert.Empty(fake.Received);
    }

    [Fact]
    public async Task OpensViscaOverIpAndRecallsPresets()
    {
        using var fake = new FakeViscaCamera();
        var visca = new PtzCameraConfig
        {
            Name = "Cam-1",
            Host = "127.0.0.1",
            Port = fake.Port,
            Proto = "sonyUDP",
            FirstPreset = 20,
        };

        using var link = CameraLinks.Open(visca);

        Assert.NotNull(link);
        link.Drive(1, 0);
        link.Recall(8);

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline &&
               !fake.Received.Any(p => p.SequenceEqual(ViscaCommands.PresetRecall(8, 20))))
        {
            await Task.Delay(25);
        }

        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.Drive(1, 0)));
        Assert.Contains(fake.Received, p => p.SequenceEqual(ViscaCommands.PresetRecall(8, 20)));
    }

    [Fact]
    public void RefusesACameraWithoutAnAddress()
    {
        var camera = new PtzCameraConfig { Host = "  ", Proto = "sonyUDP" };

        Assert.Null(CameraLinks.Open(camera));
    }
}
