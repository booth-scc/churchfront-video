using System.Text.Json;
using CueVideo.Core;

namespace CueVideo.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cuevideo-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch (Exception) { /* best effort */ }
    }

    [Fact]
    public void ReadsTheMacSchemaAndKeepsUnknownKeys()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");

        File.WriteAllText(path, """
        {
          "atemHost": "192.168.10.159",
          "me": 1,
          "sources": [ { "input": 1, "label": "CAM 1", "ndi": "CAM1", "futureField": true } ],
          "cameras": [
            {
              "name": "Cam-1",
              "host": "192.168.10.183",
              "atemInput": 1,
              "firstPreset": 20,
              "presetNames": { "8": "Full Stage", "9": "Sermon" },
              "shots": [],
              "macOnlyField": { "a": 1 }
            }
          ],
          "keys": [ { "label": "KEY 1", "me": 1, "keyer": 0 } ],
          "trackSpeed": 1.4,
          "unknownRoot": "keep me"
        }
        """);

        var store = new ConfigStore(_directory);
        store.Load();

        Assert.Null(store.FileError);
        Assert.False(store.UsedLastGood);
        Assert.Equal("192.168.10.159", store.Config.AtemHost);
        Assert.Equal(52381, store.Config.Cameras[0].EffectivePort);
        Assert.Equal("Full Stage", store.Config.Cameras[0].PresetName(8));
        Assert.Equal(1.4, store.Config.TrackSpeed, 3);
        Assert.True(store.Config.Sources[0].Extra!.ContainsKey("futureField"));
        Assert.True(store.Config.Cameras[0].Extra!.ContainsKey("macOnlyField"));
        Assert.True(store.Config.Extra!.ContainsKey("unknownRoot"));

        // Unknown keys survive a save/load cycle, and computed values are not written.
        store.Save();
        var written = File.ReadAllText(store.Path);
        Assert.DoesNotContain("EffectivePort", written);

        var again = new ConfigStore(_directory);
        again.Load();

        Assert.True(again.Config.Extra!.ContainsKey("unknownRoot"));
        Assert.True(again.Config.Cameras[0].Extra!.ContainsKey("macOnlyField"));
        Assert.Equal(JsonValueKind.Object, again.Config.Cameras[0].Extra!["macOnlyField"].ValueKind);
    }

    [Fact]
    public void CreatesDefaultsWhenThereIsNoFile()
    {
        var store = new ConfigStore(_directory);
        store.Load();

        Assert.True(store.CreatedDefaults);
        Assert.True(File.Exists(store.Path));
        Assert.Equal("192.168.10.159", store.Config.AtemHost);
        Assert.Equal(4, store.Config.Cameras.Count);
        Assert.Equal(2, store.Config.CameraForInput(2)!.AtemInput);
    }

    [Fact]
    public void FallsBackToLastGoodWhenTheFileIsBroken()
    {
        Directory.CreateDirectory(_directory);
        var store = new ConfigStore(_directory);
        store.Load();               // writes defaults and last-good
        var good = store.Config;

        File.WriteAllText(store.Path, "{ this is not json");

        var next = new ConfigStore(_directory);
        next.Load();

        Assert.True(next.UsedLastGood);
        Assert.NotNull(next.FileError);
        Assert.Equal(good.AtemHost, next.Config.AtemHost);
        Assert.Equal(4, next.Config.Cameras.Count);
    }
}
