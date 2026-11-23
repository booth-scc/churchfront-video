using System.Text.Json;
using System.Text.Json.Serialization;
using CueVideo.Visca;

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
    public List<ScreenOutput> Screens
    {
        get => _screens;
        set => _screens = value ?? [];
    }

    [JsonPropertyName("routes")]
    public List<RouteButton> Routes
    {
        get => _routes;
        set => _routes = value ?? [];
    }

    /// <summary>Follow-shot framing changes, by size: "shoulders", "waist" or "full".</summary>
    [JsonPropertyName("framing")]
    public Dictionary<string, FramingRule> Framing
    {
        get => _framing;
        set => _framing = value ?? new();
    }

    [JsonPropertyName("trackTuning")]
    public TrackTuning TrackTuning
    {
        get => _trackTuning;
        set => _trackTuning = value ?? new();
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    // The Mac reads a missing or null top-level section as empty, as each key does there.
    private List<ScreenOutput> _screens = [];
    private List<RouteButton> _routes = [];
    private Dictionary<string, FramingRule> _framing = new();
    private TrackTuning _trackTuning = new();

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
            DefaultCamera("Cam-1", "192.168.10.183", 1),
            DefaultCamera("Cam-2S", "192.168.10.137", 2),
            DefaultCamera("Cam-3", "192.168.10.149", 3),
            DefaultCamera("Cam-4", "192.168.10.136", 4),
        ],
        Keys =
        [
            new KeyButtonConfig { Label = "KEY 1", Me = 1, Keyer = 1 },
            new KeyButtonConfig { Label = "KEY 2", Me = 1, Keyer = 2 },
            new KeyButtonConfig { Label = "KEY 3", Me = 1, Keyer = 3 },
            new KeyButtonConfig { Label = "KEY 4", Me = 1, Keyer = 4 },
        ],
    };

    private static PtzCameraConfig DefaultCamera(string name, string host, int input) => new()
    {
        Name = name,
        Host = host,
        AtemInput = input,
        FirstPreset = 20,
        PresetNames = new Dictionary<string, string>
        {
            ["8"] = "Full Stage",
            ["9"] = "Sermon",
        },
    };

    public PtzCameraConfig? CameraForInput(int input) =>
        Cameras.FirstOrDefault(c => c.AtemInput == input);

    /// <summary>Configured cameras whose protocol this port cannot drive, for status lines.</summary>
    [JsonIgnore]
    public IReadOnlyList<PtzCameraConfig> UnsupportedCameras =>
        Cameras.Where(c => !c.IsSupported).ToList();
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

    /// <summary>"sonyUDP", "rawTCP" or "canonXC" from the macOS config. Only sonyUDP is ported.</summary>
    [JsonPropertyName("proto")]
    public string Proto { get; set; } = CameraProtocols.SonyUdp;

    [JsonPropertyName("atemInput")]
    public int? AtemInput { get; set; }

    [JsonPropertyName("firstPreset")]
    public int FirstPreset { get; set; }

    [JsonPropertyName("presetNames")]
    public Dictionary<string, string> PresetNames { get; set; } = new();

    /// <summary>The order the camera's own presets are shown in, by preset number.</summary>
    [JsonPropertyName("presetOrder")]
    public List<int> PresetOrder { get; set; } = [];

    /// <summary>Shots saved in CueVideo's config file, in button order.</summary>
    [JsonPropertyName("shots")]
    public List<PtzShot> Shots { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool IsSupported => CameraProtocols.IsSupported(Proto);

    [JsonIgnore]
    public string ProtocolLabel => CameraProtocols.Label(Proto);

    [JsonIgnore]
    public int EffectivePort => Port ?? CameraProtocols.DefaultPort(Proto);

    /// <summary>Why this camera cannot be driven; null when it can. Names the camera and the gap.</summary>
    [JsonIgnore]
    public string? DriveProblem =>
        !IsSupported ? $"{Name} · {ProtocolLabel} unsupported by this port."
        : string.IsNullOrWhiteSpace(Host) ? $"{Name} has no address."
        : null;

    /// <summary>The buttons in this camera's shot row, from the setup file.</summary>
    [JsonIgnore]
    public IReadOnlyList<ShotButton> ShotButtons => CameraShotRow.For(this);
}

/// <summary>
/// A named shot saved in CueVideo's config file, so it can be made and edited by hand. A
/// position shot holds pan, tilt and zoom in the camera's own counts; an exposure shot holds
/// an exposure object instead.
/// </summary>
public sealed class PtzShot
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Shot";

    /// <summary>"position" or "exposure"; omitted when the shot says it another way.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("pan")]
    public int? Pan { get; set; }

    [JsonPropertyName("tilt")]
    public int? Tilt { get; set; }

    [JsonPropertyName("zoom")]
    public int? Zoom { get; set; }

    /// <summary>The Mac's exposure object, kept whole until the exposure panel needs it.</summary>
    [JsonPropertyName("exposure")]
    public JsonElement? Exposure { get; set; }

    /// <summary>Keys this port does not know yet, such as "id" and "auto"; carried through unchanged.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Shot" : Name;

    [JsonIgnore]
    public bool IsExposure =>
        Kind is { Length: > 0 } kind ? string.Equals(kind, "exposure", StringComparison.OrdinalIgnoreCase) : Exposure is not null;

    /// <summary>The place this shot sends the camera to; null unless all three counts are set.</summary>
    [JsonIgnore]
    public PtzPosition? Position => Pan is { } pan && Tilt is { } tilt && Zoom is { } zoom
        ? new PtzPosition(pan, tilt, zoom)
        : null;

    [JsonIgnore]
    public bool IsPosition => !IsExposure && Position is not null;

    /// <summary>The exposure a shot sets, read from the Mac object when it is recalled.</summary>
    [JsonIgnore]
    public ExposureRequest? ExposureValues
    {
        get
        {
            if (!IsExposure || Exposure is not { } exposure || exposure.ValueKind != JsonValueKind.Object) return null;

            int? Int(string key) =>
                exposure.TryGetProperty(key, out var value) && value.TryGetInt32(out var number) ? number : null;

            string? Text(string key) =>
                exposure.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            var request = new ExposureRequest
            {
                Gain = Int("gain"),
                Shutter = Int("shutter"),
                Iris = Int("iris"),
                WhiteBalance = Text("wb"),
                Kelvin = Int("kelvin"),
            };

            return request.HasExposure || request.WhiteBalance is not null || request.Kelvin is not null
                ? request
                : null;
        }
    }

    /// <summary>What is missing before this shot can move or set the camera; null when it can.</summary>
    [JsonIgnore]
    public string? Problem
    {
        get
        {
            if (IsExposure) return ExposureValues is null ? $"{DisplayName} has no exposure." : null;
            return Position is null ? $"{DisplayName} has no saved position." : null;
        }
    }
}

public sealed class KeyButtonConfig
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "KEY";

    /// <summary>The M/E, counted from 1 as on the switcher.</summary>
    [JsonPropertyName("me")]
    public int? Me { get; set; }

    /// <summary>The upstream keyer, counted from 1 as on the switcher.</summary>
    [JsonPropertyName("keyer")]
    public int? Keyer { get; set; }

    /// <summary>The downstream key, counted from 1 as on the switcher.</summary>
    [JsonPropertyName("dsk")]
    public int? Dsk { get; set; }

    [JsonPropertyName("ndi")]
    public string? Ndi { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// A screen or other destination fed by one or more switcher aux outputs, counted from 1.
/// Auxes listed together switch together, such as a left and right pair.
/// </summary>
public sealed class ScreenOutput
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("aux")]
    public List<int> Aux { get; set; } = [];

    /// <summary>How it is wired, for the Routing page: "Converter 1 → LED processor".</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>Shown in the volunteer's screen summary. Off for encoders, recorders and patch bay feeds.</summary>
    [JsonPropertyName("volunteer")]
    public bool? Volunteer { get; set; }

    /// <summary>Shown as a small live preview in the top bar.</summary>
    [JsonPropertyName("pinned")]
    public bool? Pinned { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>One change a routing button makes: send this source to this aux, counted from 1.</summary>
public sealed class RouteChange
{
    [JsonPropertyName("aux")]
    public int Aux { get; set; }

    [JsonPropertyName("source")]
    public int Source { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A volunteer button that routes one or more screens at once, like a macro.</summary>
public sealed class RouteButton
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("set")]
    public List<RouteChange> Set { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// How a follow shot is framed. Any part left out uses the built-in standard. Measured as a
/// share of the frame: <see cref="EyeLine"/> down from the top, <see cref="Scale"/> is eyes to
/// shoulders against frame height (bigger is tighter), <see cref="LeadRoom"/> the offset toward
/// where the subject faces or walks, and <see cref="DeadX"/> and <see cref="DeadY"/> how far
/// they can drift before the camera moves.
/// </summary>
public sealed class FramingRule
{
    [JsonPropertyName("eyeLine")]
    public double? EyeLine { get; set; }

    [JsonPropertyName("scale")]
    public double? Scale { get; set; }

    [JsonPropertyName("leadRoom")]
    public double? LeadRoom { get; set; }

    [JsonPropertyName("deadX")]
    public double? DeadX { get; set; }

    [JsonPropertyName("deadY")]
    public double? DeadY { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// Tracking response, for tuning while it runs. Any member left out uses the built-in value:
/// <see cref="Correction"/> pulls toward the framing mark, <see cref="Latency"/> is how far
/// behind the preview picture runs in seconds, <see cref="WalkThreshold"/> how fast (frames a
/// second) counts as walking, <see cref="ZoomThreshold"/> how wrong the size must look before
/// zooming as a log ratio, and <see cref="ZoomWait"/> how long it must stay wrong first.
/// </summary>
public sealed class TrackTuning
{
    [JsonPropertyName("correction")]
    public double? Correction { get; set; }

    [JsonPropertyName("latency")]
    public double? Latency { get; set; }

    [JsonPropertyName("walkThreshold")]
    public double? WalkThreshold { get; set; }

    [JsonPropertyName("zoomThreshold")]
    public double? ZoomThreshold { get; set; }

    [JsonPropertyName("zoomWait")]
    public double? ZoomWait { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
