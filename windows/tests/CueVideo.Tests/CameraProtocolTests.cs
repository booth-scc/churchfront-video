using CueVideo.Core;

namespace CueVideo.Tests;

public class CameraProtocolTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cuevideo-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch (Exception) { /* best effort */ }
    }

    [Fact]
    public void MacConfigWithCanonCamerasLoadsAndReportsTheGap()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "config.json"), """
        {
          "atemHost": "192.168.10.159",
          "cameras": [
            { "name": "Canon 1", "host": "192.168.10.183", "proto": "canonXC", "atemInput": 1 },
            { "name": "BirdDog 2", "host": "192.168.10.137", "proto": "sonyUDP", "atemInput": 2, "firstPreset": 20 }
          ]
        }
        """);

        using var store = new ConfigStore(_directory, readOnly: false);
        store.Load();

        Assert.Null(store.FileError);
        Assert.Equal(2, store.Config.Cameras.Count);
        Assert.Equal(80, store.Config.CameraForInput(1)!.EffectivePort);
        Assert.Equal("Canon (HTTP)", store.Config.CameraForInput(1)!.ProtocolLabel);
        Assert.Equal("Canon 1", Assert.Single(store.Config.UnsupportedCameras).Name);
        Assert.True(store.Config.CameraForInput(2)!.IsSupported);
    }

    [Theory]
    [InlineData("sonyUDP", true)]
    [InlineData("SonyUDP", true)]
    [InlineData(" rawTCP ", false)]
    [InlineData("canonXC", false)]
    [InlineData("futureProto", false)]
    [InlineData("", false)]
    public void OnlyViscaOverIpIsSupported(string proto, bool supported)
    {
        Assert.Equal(supported, CameraProtocols.IsSupported(proto));
    }

    [Theory]
    [InlineData("sonyUDP", "VISCA over IP (UDP)")]
    [InlineData("rawTCP", "VISCA over TCP")]
    [InlineData("canonXC", "Canon (HTTP)")]
    [InlineData("futureProto", "futureProto")]
    [InlineData("", "no protocol")]
    public void LabelsMatchTheMacConfigNames(string proto, string label)
    {
        Assert.Equal(label, CameraProtocols.Label(proto));
    }

    [Theory]
    [InlineData("sonyUDP", null, 52381)]
    [InlineData("rawTCP", null, 5678)]
    [InlineData("canonXC", null, 80)]
    [InlineData("canonXC", 8080, 8080)]
    public void PortDefaultsFollowTheProtocol(string proto, int? port, int expected)
    {
        var camera = new PtzCameraConfig { Proto = proto, Port = port };

        Assert.Equal(expected, camera.EffectivePort);
    }
}
