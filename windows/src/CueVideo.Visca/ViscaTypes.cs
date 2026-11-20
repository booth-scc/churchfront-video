namespace CueVideo.Visca;

/// <summary>
/// A camera position. Pan and tilt are hundredths of a degree (right and up positive);
/// zoom is the horizontal angle of view in hundredths of a degree, as the macOS app's
/// Canon path reported it. Raw mode uses the camera's own counts instead.
/// </summary>
public readonly record struct PtzPosition(int Pan, int Tilt, int Zoom);

/// <summary>How one make of VISCA camera counts. VISCA fixes commands, not units.</summary>
public sealed class ViscaUnits
{
    /// <summary>Pan and tilt counts in one degree.</summary>
    public required double CountsPerDegree { get; init; }

    /// <summary>Zoom count when fully zoomed in.</summary>
    public required double ZoomMax { get; init; }

    /// <summary>Horizontal angle of view zoomed fully out, in degrees.</summary>
    public required double WideFov { get; init; }

    public required double ZoomRatio { get; init; }

    public IReadOnlyDictionary<int, int> Shutter { get; init; } = new Dictionary<int, int>();
    public IReadOnlyDictionary<int, int> Iris { get; init; } = new Dictionary<int, int>();
    public int GainMax { get; init; }
    public int KelvinBase { get; init; } = 2500;
    public int KelvinStep { get; init; } = 100;
    public int KelvinTop { get; init; } = 8000;

    /// <summary>
    /// PTZOptics figures, the same defaults the macOS app shipped: 0x0990 counts is 170 degrees,
    /// zoom runs 0..0x4000, and the angle of view narrows evenly across the zoom range.
    /// BirdDog numbers still need measuring at the booth; keeping the gentler estimate is safe.
    /// </summary>
    public static ViscaUnits PtzOptics { get; } = new()
    {
        CountsPerDegree = 14.4,
        ZoomMax = 16384,
        WideFov = 58,
        ZoomRatio = 20,
        Shutter = new Dictionary<int, int>
        {
            [1] = 30, [2] = 60, [3] = 90, [4] = 100, [5] = 125, [6] = 180, [7] = 250,
            [8] = 350, [9] = 500, [10] = 725, [11] = 1000, [12] = 1500, [13] = 2000,
            [14] = 3000, [15] = 4000, [16] = 6000, [17] = 10000,
        },
        Iris = new Dictionary<int, int>
        {
            [1] = 1100, [2] = 960, [3] = 800, [4] = 680, [5] = 560, [6] = 480,
            [7] = 400, [8] = 340, [9] = 280, [10] = 240, [11] = 200, [12] = 180,
        },
        GainMax = 14,
    };

    public int? PositionOf(int value, IReadOnlyDictionary<int, int> table) =>
        table.Where(kv => kv.Value == value).Select(kv => (int?)kv.Key).FirstOrDefault();

    public double Fov(int zoom) =>
        WideFov * Math.Pow(1 / ZoomRatio, Math.Clamp((double)zoom / ZoomMax, 0, 1));

    /// <summary>Counts to the Canon-style reporting the rest of the app uses.</summary>
    public PtzPosition ToPosition(int pan, int tilt, int zoom) => new(
        (int)Math.Round(pan / CountsPerDegree * 100),
        (int)Math.Round(tilt / CountsPerDegree * 100),
        (int)Math.Round(Fov(zoom) * 100));
}

/// <summary>Exposure as read back from the camera.</summary>
public sealed record CameraExposure
{
    public double? Shutter { get; init; }
    public double? Iris { get; init; }
    public int? Gain { get; init; }
    public string? WhiteBalance { get; init; }
    public int? Kelvin { get; init; }
    public bool ShutterAuto { get; init; }
    public bool IrisAuto { get; init; }
    public bool GainAuto { get; init; }
    public bool HasExposure { get; init; }
}

/// <summary>Exposure changes to apply; null fields are left alone.</summary>
public sealed record ExposureRequest
{
    public double? Shutter { get; init; }
    public double? Iris { get; init; }
    public int? Gain { get; init; }
    public string? WhiteBalance { get; init; }
    public int? Kelvin { get; init; }

    public bool HasExposure => Shutter is not null || Iris is not null || Gain is not null;
}

public enum ViscaLinkState
{
    Idle,
    Connecting,
    Online,
    Offline,
}

/// <summary>Raw camera occupant of one VISCA reply, kept for diagnostics.</summary>
public sealed record ViscaReply(byte Address, byte Type, byte[] Payload);
