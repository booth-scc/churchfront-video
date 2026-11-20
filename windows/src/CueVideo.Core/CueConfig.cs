using System.Text.Json;
using System.Text.Json.Serialization;

namespace CueVideo.Core;

/// <summary>
/// The setup file. Field names and defaults follow the macOS app's CueConfig so an existing
/// config.json opens unchanged; unknown keys are carried through untouched.
/// </summary>
public sealed class CueConfig
{
    [JsonPropertyName("atemHost")]
    public string AtemHost { get; set; } = "";

    /// <summary>The M/E the operator runs, counted from 1 as on the switcher.</summary>
    [JsonPropertyName("me")]
    public int Me { get; set; } = 1;

    [JsonPropertyName("sources")]
    public List<VolunteerSource> Sources { get; set; } = [];

    [JsonPropertyName("cameras")]
    public List<PtzCameraConfig> Cameras { get; set; } = [];

    [JsonPropertyName("keys")]
    public List<KeyButtonConfig> Keys { get; set; } = [];

    [JsonPropertyName("trackSpeed")]
    public double TrackSpeed { get; set; } = 1.0;

    [JsonPropertyName("ptzSpeed")]
    public double PtzSpeed { get; set; } = 1.0;

    [JsonPropertyName("trackTilt")]
    public bool TrackTilt { get; set; } = true;

    [JsonPropertyName("trackZoom")]
    public bool TrackZoom { get; set; } = true;

    [JsonPropertyName("mixNames")]
    public List<string> MixNames { get; set; } = [];

    /// <summary>NDI picture for an input that is not in the source row, keyed by input number.</summary>
    [JsonPropertyName("pictures")]
    public Dictionary<string, string> Pictures { get; set; } = new();

    [JsonPropertyName("screens")]
    public List<JsonElement> Screens { get; set; } = [];

    [JsonPropertyName("routes")]
    public List<JsonElement> Routes { get; set; } = [];

    [JsonPropertyName("framing")]
    public Dictionary<string, JsonElement> Framing { get; set; } = new();

    [JsonPropertyName("trackTuning")]
    public JsonElement? TrackTuning { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>The booth as audited on 2026-11-20, used only when no config file exists.</summary>
    public static CueConfig BoothDefaults() => new()
    {
        AtemHost = "192.168.10.159",
        Me = 1,
        Sources =
        [
            new VolunteerSource { Input = 1, Label = "CAM 1" },
            new VolunteerSource { Input = 2, Label = "CAM 2" },
            new VolunteerSource { Input = 3, Label = "CAM 3" },
            new VolunteerSource { Input = 4, Label = "CAM 4" },
            new VolunteerSource { Input = 5, Label = "PRES MAIN" },
            new VolunteerSource { Input = 6, Label = "PRES STAGE" },
        ],
        Cameras =
        [
            new PtzCameraConfig { Name = "Cam-1", Host = "192.168.10.183", AtemInput = 1, FirstPreset = 20 },
            new PtzCameraConfig { Name = "Cam-2S", Host = "192.168.10.137", AtemInput = 2, FirstPreset = 20 },
            new PtzCameraConfig { Name = "Cam-3", Host = "192.168.10.149", AtemInput = 3, FirstPreset = 20 },
            new PtzCameraConfig { Name = "Cam-4", Host = "192.168.10.136", AtemInput = 4, FirstPreset = 20 },
        ],
        Keys =
        [
            new KeyButtonConfig { Label = "KEY 1", Me = 1, Keyer = 0 },
            new KeyButtonConfig { Label = "KEY 2", Me = 1, Keyer = 1 },
            new KeyButtonConfig { Label = "KEY 3", Me = 1, Keyer = 2 },
            new KeyButtonConfig { Label = "KEY 4", Me = 1, Keyer = 3 },
        ],
    };

    public PtzCameraConfig? CameraForInput(int input) =>
        Cameras.FirstOrDefault(c => c.AtemInput == input);
}

public sealed class VolunteerSource
{
    [JsonPropertyName("input")]
    public int Input { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("ndi")]
    public string? Ndi { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class PtzCameraConfig
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Camera";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public int? Port { get; set; }

    /// <summary>"sonyUDP", "rawTCP" or a macOS-only "canonXC"; only sonyUDP is used here.</summary>
    [JsonPropertyName("proto")]
    public string Proto { get; set; } = "sonyUDP";

    [JsonPropertyName("atemInput")]
    public int? AtemInput { get; set; }

    [JsonPropertyName("firstPreset")]
    public int FirstPreset { get; set; }

    [JsonPropertyName("presetNames")]
    public Dictionary<string, string> PresetNames { get; set; } = new();

    [JsonPropertyName("shots")]
    public List<JsonElement> Shots { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public int EffectivePort => Port ?? (Proto == "sonyUDP" ? 52381 : 5678);

    public string PresetName(int number) =>
        PresetNames.TryGetValue(number.ToString(), out var name) && name.Length > 0 ? name : number.ToString();
}

public sealed class KeyButtonConfig
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "KEY";

    [JsonPropertyName("me")]
    public int? Me { get; set; }

    [JsonPropertyName("keyer")]
    public int? Keyer { get; set; }

    [JsonPropertyName("dsk")]
    public int? Dsk { get; set; }

    [JsonPropertyName("ndi")]
    public string? Ndi { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
