using System.Diagnostics;
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

        using var store = new ConfigStore(_directory, readOnly: false);
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

        using var again = new ConfigStore(_directory, readOnly: false);
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
        using var store = new ConfigStore(_directory, readOnly: false);
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

        using (var store = new ConfigStore(_directory, readOnly: false))
        {
            store.Load();               // writes defaults and last-good
        }

        var good = File.ReadAllText(Path.Combine(_directory, "config.last-good.json"));

        File.WriteAllText(Path.Combine(_directory, "config.json"), "{ this is not json");

        using var next = new ConfigStore(_directory, readOnly: false);
        next.Load();

        Assert.True(next.UsedLastGood);
        Assert.NotNull(next.FileError);
        Assert.Equal("192.168.10.159", next.Config.AtemHost);
        Assert.Equal(4, next.Config.Cameras.Count);
        Assert.Equal(good, File.ReadAllText(Path.Combine(_directory, "config.last-good.json")));
    }

    [Fact]
    public async Task AnExternalEditReachesTheStoreWithinASecond()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """{ "atemHost": "192.168.10.159" }""");

        using var store = new ConfigStore(_directory, readOnly: false);
        store.Load();

        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Changed += () => reloaded.TrySetResult();

        var started = Stopwatch.StartNew();
        File.WriteAllText(path, """{ "atemHost": "10.9.8.7" }""");

        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(1), $"the change took {started.Elapsed}.");
        Assert.Equal("10.9.8.7", store.Config.AtemHost);
        Assert.Null(store.FileError);
    }

    [Fact]
    public async Task ATransientReadFailureIsRetriedWithoutArchivingTheFile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """{ "atemHost": "192.168.10.159" }""");

        using var store = new ConfigStore(_directory, readOnly: false);
        store.Load();

        // A file that cannot be read for a moment must not be treated as a broken setup.
        File.Delete(path);
        Directory.CreateDirectory(path);
        await WaitUntil(() => store.FileError is not null);

        Directory.Delete(path);

        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Changed += () => { if (store.FileError is null) reloaded.TrySetResult(); };

        File.WriteAllText(path, """{ "atemHost": "10.9.8.7" }""");
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("10.9.8.7", store.Config.AtemHost);
        Assert.Empty(Directory.GetFiles(_directory, "config.unreadable-*.json"));

        store.Update(config => config.AtemHost = "10.9.8.8");
        Assert.Empty(Directory.GetFiles(_directory, "config.unreadable-*.json"));
    }

    [Fact]
    public void ReadOnlyRunsNeverWrite()
    {
        using var store = new ConfigStore(_directory, readOnly: true);
        store.Load();

        Assert.True(store.CreatedDefaults);
        Assert.False(File.Exists(store.Path));
        Assert.False(File.Exists(store.LastGoodPath));

        store.Update(config => config.AtemHost = "10.0.0.5");

        Assert.Equal("10.0.0.5", store.Config.AtemHost);
        Assert.False(File.Exists(store.Path));
        Assert.False(File.Exists(store.LastGoodPath));
    }

    [Fact]
    public async Task ARepairedFileRaisesEvenWhenTheSetupDidNotChange()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """{ "atemHost": "10.1.1.1" }""");

        using var store = new ConfigStore(_directory, readOnly: true);
        store.Load();

        File.WriteAllText(path, "{ this is not json");
        await WaitUntil(() => store.FileError is not null);

        var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Changed += () => { if (store.FileError is null) cleared.TrySetResult(); };

        // The same setup as the one already loaded: only the error state changes.
        File.WriteAllText(path, """{ "atemHost": "10.1.1.1" }""");
        await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(store.FileError);
    }

    [Fact]
    public void SelfTestRunsAreReadOnly()
    {
        Assert.True(ConfigStore.IsSelfTestRun(["CUEVIDEO_ATEM"]));
        Assert.True(ConfigStore.IsSelfTestRun(["CUEVIDEO_CONFIG_DIR"]));
        Assert.False(ConfigStore.IsSelfTestRun(["PATH", "USERNAME"]));
    }

    [Fact]
    public void AnUnreadableFileIsKeptWhenTheNextSaveReplacesIt()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, "{ this is not json");

        using var store = new ConfigStore(_directory, readOnly: false);
        store.Load();

        Assert.NotNull(store.FileError);
        Assert.Equal("{ this is not json", File.ReadAllText(path));     // never overwritten on load

        store.Update(config => config.AtemHost = "10.0.0.5");

        Assert.Null(store.FileError);
        var aside = Assert.Single(Directory.GetFiles(_directory, "config.unreadable-*.json"));
        Assert.Equal("{ this is not json", File.ReadAllText(aside));

        using var next = new ConfigStore(_directory, readOnly: false);
        next.Load();
        Assert.Equal("10.0.0.5", next.Config.AtemHost);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);

        Assert.True(condition());
    }
}
