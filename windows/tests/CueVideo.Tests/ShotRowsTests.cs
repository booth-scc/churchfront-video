using System.Text.Json;
using CueVideo.Core;

namespace CueVideo.Tests;

/// <summary>
/// The shot row is each camera's shots, from the setup file, or the presets it has named.
/// These tests drive the row policy in Core, not the window.
/// </summary>
public sealed class ShotRowsTests
{
    [Fact]
    public void SavedShotsDriveTheRowInConfigOrder()
    {
        var camera = new PtzCameraConfig
        {
            Shots =
            [
                new PtzShot { Name = "Stage", Pan = 100, Tilt = 200, Zoom = 300 },
                new PtzShot { Name = "Sermon", Pan = 400, Tilt = 500, Zoom = 600 },
            ],
            PresetNames = new Dictionary<string, string>
            {
                ["8"] = "Full Stage",
                ["9"] = "Sermon",
            },
        };

        var buttons = camera.ShotButtons;

        Assert.Equal(["Stage", "Sermon"], buttons.Select(button => button.Label));
        Assert.All(buttons, button => Assert.NotNull(button.Shot));
        Assert.All(buttons, button => Assert.Null(button.Preset));
        Assert.All(buttons, button => Assert.Null(button.Problem));
    }

    [Fact]
    public void ACameraWithNoSavedShotsFallsBackToItsNamedPresets()
    {
        var camera = new PtzCameraConfig
        {
            PresetNames = new Dictionary<string, string>
            {
                ["9"] = "Sermon",
                ["8"] = "Full Stage",
            },
        };

        var buttons = camera.ShotButtons;

        Assert.Equal(["Full Stage", "Sermon"], buttons.Select(button => button.Label));
        Assert.Equal([8, 9], buttons.Select(button => button.Preset));
        Assert.All(buttons, button => Assert.Null(button.Shot));
    }

    [Fact]
    public void PresetFallbackFollowsPresetOrderThenNumber()
    {
        var camera = new PtzCameraConfig
        {
            PresetOrder = [9, 8],
            PresetNames = new Dictionary<string, string>
            {
                ["8"] = "Full Stage",
                ["9"] = "Sermon",
                ["1"] = "Pulpit",
            },
        };

        var buttons = camera.ShotButtons;

        Assert.Equal(["Sermon", "Full Stage", "Pulpit"], buttons.Select(button => button.Label));
        Assert.Equal([9, 8, 1], buttons.Select(button => button.Preset));
    }

    [Fact]
    public void ExposureShotsShareTheRowWithSavedPlaces()
    {
        var camera = new PtzCameraConfig
        {
            Shots =
            [
                new PtzShot { Name = "Stage", Pan = 100, Tilt = 200, Zoom = 300 },
                new PtzShot { Name = "Dim", Kind = "exposure", Exposure = Json("""{ "gain": 6 }""") },
            ],
            PresetNames = new Dictionary<string, string> { ["8"] = "Full Stage" },
        };

        var buttons = camera.ShotButtons;

        Assert.Equal(["Stage", "Dim"], buttons.Select(button => button.Label));
        Assert.All(buttons, button => Assert.Null(button.Problem));
    }

    [Fact]
    public void ASavedShotWithoutAllThreeCountsIsShownWithTheReason()
    {
        var camera = new PtzCameraConfig
        {
            Shots = [new PtzShot { Name = "Half a place", Pan = 100, Tilt = 200 }],
            PresetNames = new Dictionary<string, string> { ["8"] = "Full Stage" },
        };

        var button = Assert.Single(camera.ShotButtons);

        Assert.Equal("Half a place", button.Label);
        Assert.Equal("Half a place has no saved position.", button.Problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    public void AnExposureShotWithoutValuesSaysSo(string? exposure)
    {
        var camera = new PtzCameraConfig
        {
            Shots =
            [
                new PtzShot
                {
                    Name = "Dim",
                    Kind = "exposure",
                    Exposure = exposure is null ? null : Json(exposure),
                },
            ],
        };

        var button = Assert.Single(camera.ShotButtons);

        Assert.Equal("Dim", button.Label);
        Assert.Equal("Dim has no exposure.", button.Problem);
    }

    [Fact]
    public void ABlankShotNameIsShownAsShot()
    {
        var camera = new PtzCameraConfig
        {
            Shots = [new PtzShot { Name = "", Pan = 100, Tilt = 200, Zoom = 300 }],
        };

        Assert.Equal("Shot", Assert.Single(camera.ShotButtons).Label);
    }

    [Fact]
    public void AWhiteSpacePresetNameIsNotAButton()
    {
        var camera = new PtzCameraConfig
        {
            PresetNames = new Dictionary<string, string> { ["8"] = "", ["9"] = "  " },
        };

        Assert.Empty(camera.ShotButtons);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
