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
              "presetOrder": [ 9, 8 ],
              "shots": [
                { "id": "11111111-1111-1111-1111-111111111111", "name": "Stage", "kind": "position", "pan": 1200, "tilt": -300, "zoom": 4000, "auto": true },
                { "name": "Sermon", "pan": 800, "tilt": -100, "zoom": 2000 },
                { "name": "Dim", "exposure": { "gain": 6, "kelvin": 4300, "macOnly": 1 } }
              ],
              "macOnlyField": { "a": 1 }
            }
          ],
          "keys": [ { "label": "KEY 1", "me": 1, "keyer": 1 } ],
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
        Assert.Equal("Full Stage", store.Config.Cameras[0].PresetNames["8"]);
        Assert.Equal(1.4, store.Config.TrackSpeed, 3);
        Assert.True(store.Config.Sources[0].Extra!.ContainsKey("futureField"));
        Assert.True(store.Config.Cameras[0].Extra!.ContainsKey("macOnlyField"));
        Assert.True(store.Config.Extra!.ContainsKey("unknownRoot"));

        // Saved shots are typed, and keys this port does not use are carried through.
        var shots = store.Config.Cameras[0].Shots;
        Assert.Equal(3, shots.Count);
        Assert.Equal("Stage", shots[0].Name);
        Assert.True(shots[0].IsPosition);
        Assert.Equal(1200, shots[0].Pan);
        Assert.True(shots[0].Extra!.ContainsKey("id"));
        Assert.True(shots[0].Extra!.ContainsKey("auto"));
        Assert.True(shots[1].IsPosition);                     // no kind, no exposure
        Assert.True(shots[2].IsExposure);                     // no kind, but an exposure
        Assert.Equal(JsonValueKind.Object, shots[2].Exposure!.Value.ValueKind);
        Assert.Equal(1, shots[2].Exposure!.Value.GetProperty("macOnly").GetInt32());
        Assert.Equal([9, 8], store.Config.Cameras[0].PresetOrder);

        // Unknown keys survive a save/load cycle, and computed values are not written.
        store.Save();
        var written = File.ReadAllText(store.Path);
        Assert.DoesNotContain("EffectivePort", written);

        var again = new ConfigStore(_directory);
        again.Load();

        Assert.True(again.Config.Extra!.ContainsKey("unknownRoot"));
        Assert.True(again.Config.Cameras[0].Extra!.ContainsKey("macOnlyField"));
        Assert.Equal(JsonValueKind.Object, again.Config.Cameras[0].Extra!["macOnlyField"].ValueKind);
        Assert.Equal(3, again.Config.Cameras[0].Shots.Count);
        Assert.True(again.Config.Cameras[0].Shots[0].Extra!.ContainsKey("auto"));
        Assert.Equal(1, again.Config.Cameras[0].Shots[2].Exposure!.Value.GetProperty("macOnly").GetInt32());
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
