using System.Text.Json;
using System.Text.Json.Nodes;
using CueVideo.Core;

namespace CueVideo.Tests;

/// <summary>
/// Replays the canonical setup documents in <c>tests/fixtures/config</c> through the store the
/// app uses. The fixtures are the contract with the macOS app: a Mac-written document must load,
/// save and come back with the same keys and values in both directions, and a hand-written one
/// must keep what it says. The typed assertions fail when a field is renamed; a replay alone
/// would let the old key survive as an unknown one.
/// </summary>
public sealed class ConfigContractTests : IDisposable
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures", "config");
    private static readonly string MacFixturesDirectory = Path.Combine(Fixtures, "mac-written");
    private static readonly string HandWrittenFixture = Path.Combine("hand-written", "minimal.json");
    private static readonly string BrokenFixture = Path.Combine("invalid", "broken.json");
    private static readonly string MacFullFixture = Path.Combine("mac-written", "full.json");
    private static readonly string UnknownKeysFixture = Path.Combine("mac-written", "unknown-keys.json");
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cuevideo-contract-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch (Exception) { /* best effort */ }
    }

    /// <summary>Fixtures the Mac writes itself: every key it knows, so the save must match exactly.</summary>
    public static TheoryData<string> MacFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(MacFixturesDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MacFixtures))]
    public void EveryMacFixtureLoadsSavesAndComesBackUnchanged(string fixture)
    {
        var path = InstallMac(fixture);
        var original = JsonNode.Parse(File.ReadAllText(path))!;

        using var store = new ConfigStore(_directory, readOnly: false);
        store.Load();

        Assert.Null(store.FileError);
        Assert.False(store.CreatedDefaults);
        Assert.False(store.UsedLastGood);
        Assert.True(store.Save());

        AssertSameDocument(original, JsonNode.Parse(File.ReadAllText(path))!);

        // A second lap is byte-for-byte stable: the file is now in the Windows shape.
        var first = File.ReadAllText(path);
        using var again = new ConfigStore(_directory, readOnly: false);
        again.Load();
        again.Save();
        Assert.Equal(first, File.ReadAllText(path));
    }

    [Fact]
    public void AHandWrittenMinimalSetupLoadsAndKeepsWhatItSays()
    {
        var path = Install(HandWrittenFixture);
        var original = JsonNode.Parse(File.ReadAllText(path))!;

        using var store = new ConfigStore(_directory, readOnly: false);
        store.Load();

        Assert.Null(store.FileError);
        Assert.False(store.CreatedDefaults);
        Assert.True(store.Save());

        AssertContainsEveryValue(original, JsonNode.Parse(File.ReadAllText(path))!, "the save dropped");

        using var again = new ConfigStore(_directory, readOnly: false);
        again.Load();
        Assert.Equal("10.9.8.7", again.Config.AtemHost);
        Assert.Empty(again.Config.Cameras);
    }

    [Fact]
    public void AMacSetupExposesRoutingFramingAndTuning()
    {
        Install(MacFullFixture);

        using var store = new ConfigStore(_directory, readOnly: false);
        store.Load();

        var config = store.Config;

        var lobby = Assert.Single(config.Screens, screen => screen.Name == "Lobby TVs");
        Assert.Equal(new[] { 4, 5 }, lobby.Aux);
        Assert.Equal("Converters 4 and 5", lobby.Note);
        Assert.True(lobby.Volunteer);
        Assert.True(lobby.Pinned);

        var recorder = Assert.Single(config.Screens, screen => screen.Name == "Recorder");
        Assert.False(recorder.Volunteer);
        Assert.Null(recorder.Pinned);

        var lobbyRoute = Assert.Single(config.Routes, route => route.Label == "Lobby: stream");
        Assert.Equal(
            new[] { (4, 10010), (5, 10010) },
            lobbyRoute.Set.Select(change => (change.Aux, change.Source)));

        var sideRoute = Assert.Single(config.Routes, route => route.Label == "Side: program");
        var sideChange = Assert.Single(sideRoute.Set);
        Assert.Equal((8, 1), (sideChange.Aux, sideChange.Source));

        var waist = config.Framing["waist"];
        Assert.Equal(0.24, waist.EyeLine!.Value, 6);
        Assert.Equal(0.42, waist.Scale!.Value, 6);
        Assert.Equal(0.12, waist.LeadRoom!.Value, 6);
        Assert.Equal(0.03, waist.DeadX!.Value, 6);
        Assert.Equal(0.02, waist.DeadY!.Value, 6);

        // A rule may set only part of the framing; the rest falls back to the standard.
        var full = config.Framing["full"];
        Assert.Equal(0.18, full.EyeLine!.Value, 6);
        Assert.Equal(0.2, full.LeadRoom!.Value, 6);
        Assert.Equal(0.2, full.Scale!.Value, 6);
        Assert.Null(full.DeadX);
        Assert.Null(full.DeadY);

        Assert.Equal(1.4, config.TrackTuning.Correction!.Value, 6);
        Assert.Equal(0.4, config.TrackTuning.Latency!.Value, 6);
        Assert.Equal(0.2, config.TrackTuning.WalkThreshold!.Value, 6);
        Assert.Equal(0.25, config.TrackTuning.ZoomThreshold!.Value, 6);
        Assert.Equal(2.0, config.TrackTuning.ZoomWait!.Value, 6);
    }

    [Fact]
    public void UnknownKeysAtEveryLevelSurviveALoadSaveAndReload()
    {
        Install(UnknownKeysFixture);

        using (var store = new ConfigStore(_directory, readOnly: false))
        {
            store.Load();
            UnknownKeysAreThere(store.Config);
            Assert.True(store.Save());
        }

        using var again = new ConfigStore(_directory, readOnly: false);
        again.Load();
        UnknownKeysAreThere(again.Config);

        static void UnknownKeysAreThere(CueConfig config)
        {
            Assert.True(config.Extra!.ContainsKey("futureRoot"));
            Assert.True(Assert.Single(config.Screens).Extra!.ContainsKey("futureScreen"));

            var route = Assert.Single(config.Routes);
            Assert.True(route.Extra!.ContainsKey("futureRoute"));
            Assert.True(Assert.Single(route.Set).Extra!.ContainsKey("futureChange"));

            Assert.True(config.Framing["waist"].Extra!.ContainsKey("futureFrame"));
            Assert.True(config.TrackTuning.Extra!.ContainsKey("futureTuning"));
        }
    }

    [Fact]
    public void ANullSectionReadsAsEmpty()
    {
        // The Mac reads a missing or null section as empty; a hand edit must not crash Windows.
        var config = JsonSerializer.Deserialize<CueConfig>("""
        {
          "screens": null,
          "routes": null,
          "framing": null,
          "trackTuning": null
        }
        """)!;

        Assert.Empty(config.Screens);
        Assert.Empty(config.Routes);
        Assert.Empty(config.Framing);
        Assert.NotNull(config.TrackTuning);
        Assert.Null(config.TrackTuning.Correction);
    }

    [Fact]
    public void TheBrokenFixtureIsReportedAndLeftAsWritten()
    {
        var path = Install(BrokenFixture);
        var before = File.ReadAllBytes(path);

        using var store = new ConfigStore(_directory, readOnly: false);
        store.Load();

        Assert.Contains("could not be parsed", store.FileError);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private string InstallMac(string fixture) => Install(Path.Combine("mac-written", fixture));

    private string Install(string fixture)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        File.Copy(Path.Combine(Fixtures, fixture), path, overwrite: true);
        return path;
    }

    /// <summary>Both directions: the save must lose nothing the document says and add nothing.</summary>
    private static void AssertSameDocument(JsonNode original, JsonNode saved)
    {
        AssertContainsEveryValue(original, saved, "the save dropped");
        AssertContainsEveryValue(saved, original, "the save added");
    }

    private static void AssertContainsEveryValue(JsonNode expected, JsonNode actual, string direction, string at = "")
    {
        if (expected is JsonObject expectedObject)
        {
            Assert.True(actual is JsonObject, $"{direction} {at} or changed its shape");
            var actualObject = (JsonObject)actual;
            foreach (var (key, value) in expectedObject)
            {
                Assert.True(actualObject.TryGetPropertyValue(key, out var actualValue), $"{direction} {at}.{key}");
                AssertContainsEveryValue(value!, actualValue!, direction, $"{at}.{key}");
            }

            return;
        }

        if (expected is JsonArray expectedArray)
        {
            Assert.True(actual is JsonArray, $"{direction} {at} or changed its shape");
            var actualArray = (JsonArray)actual;
            Assert.True(
                expectedArray.Count == actualArray.Count,
                $"{direction} {at}: expected {expectedArray.Count} items, found {actualArray.Count}");
            for (var index = 0; index < expectedArray.Count; index++)
            {
                AssertContainsEveryValue(expectedArray[index]!, actualArray[index]!, direction, $"{at}[{index}]");
            }

            return;
        }

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"{direction} {at}: {expected?.ToJsonString()} became {actual?.ToJsonString()}");
    }
}
