using CueVideo.Visca;

namespace CueVideo.Core;

/// <summary>
/// Exposure for the camera on Next, as the Operator panel shows it. The camera reports
/// numbers; the panel shows "6 dB", "1/250", "f/2.8" and a white-balance name.
/// </summary>
public sealed record ExposureReadout(string Gain, string Shutter, string Iris, string WhiteBalance)
{
    /// <summary>Nothing read yet: the camera is gone, unsupported, or off the preview bus.</summary>
    public static ExposureReadout Unknown { get; } = new("—", "—", "—", "—");

    /// <summary>Formats a camera reading; a missing reading is <see cref="Unknown"/>.</summary>
    public static ExposureReadout From(CameraExposure? exposure) => exposure is null
        ? Unknown
        : new ExposureReadout(
            Gain: exposure.Gain is { } gain ? $"{gain} dB" : "Auto",
            Shutter: exposure.Shutter is { } shutter ? $"1/{shutter:0}" : "Auto",
            Iris: exposure.Iris is { } iris ? $"f/{iris / 100.0:0.#}" : "Auto",
            WhiteBalance: exposure.WhiteBalance switch
            {
                "auto" => "Auto",
                "kelvin" => exposure.Kelvin is { } kelvin ? $"{kelvin} K" : "Kelvin",
                null => "—",
                var mode => mode,
            });
}
