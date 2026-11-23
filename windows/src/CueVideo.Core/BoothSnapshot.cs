using CueVideo.Atem;

namespace CueVideo.Core;

/// <summary>The tally bits for one source: on air, on preview, or neither.</summary>
public readonly record struct SourceTally(bool Live, bool Next);

/// <summary>How the status chip should read, and what it should say.</summary>
public enum BoothStatusTone
{
    Starting,
    Connecting,
    Ready,
    Offline,
}

public sealed record BoothStatus(BoothStatusTone Tone, string Text)
{
    /// <summary>What the status chip says for a link, naming the host while connecting.</summary>
    public static BoothStatus From(AtemLink link, string host, string? configError = null)
    {
        var error = string.IsNullOrEmpty(configError) ? null : configError;
        var noAddress = string.IsNullOrWhiteSpace(host);

        return link.State switch
        {
            AtemLinkState.Online => new BoothStatus(BoothStatusTone.Ready, "Ready"),
            AtemLinkState.Connecting => new BoothStatus(
                BoothStatusTone.Connecting,
                error ?? (noAddress ? "No switcher address" : $"Connecting to {host.Trim()}…")),
            AtemLinkState.Offline => new BoothStatus(
                BoothStatusTone.Offline,
                string.IsNullOrWhiteSpace(link.Message) ? "Switcher offline" : link.Message),
            _ => new BoothStatus(
                BoothStatusTone.Starting,
                error ?? (noAddress ? "No switcher address" : "Starting…")),
        };
    }
}

/// <summary>
/// What the Operator screen shows about the switcher, taken together: what is live, what
/// is next, each source's tally, the link status, and the transition state.
/// </summary>
public sealed record BoothSnapshot(
    int ProgramInput,
    int PreviewInput,
    string ProgramLabel,
    string PreviewLabel,
    IReadOnlyDictionary<int, SourceTally> Tally,
    BoothStatus Status,
    bool InTransition,
    double FadeSeconds,
    bool Black,
    PtzCameraConfig? CameraOnNext,
    ExposureReadout Exposure)
{
    /// <summary>A source's tally, or neither bus when the switcher has not named it.</summary>
    public SourceTally TallyFor(int input) =>
        Tally.TryGetValue(input, out var tally) ? tally : new SourceTally(Live: false, Next: false);

    /// <summary>The label under the camera chip: the camera on Next, or the preview source when there is none.</summary>
    public string CameraOnNextLabel => CameraOnNext?.Name ?? PreviewLabel;

    /// <summary>The notice under the camera chip when the port cannot drive its protocol.</summary>
    public string? CameraOnNextNotice => CameraOnNext is { IsSupported: false } camera
        ? $"{camera.ProtocolLabel} · unsupported by this port"
        : null;

    /// <summary>Why the camera on Next cannot be driven; null when it can.</summary>
    public string? CameraOnNextProblem => CameraOnNext switch
    {
        null => "No camera on Next.",
        var camera => camera.DriveProblem,
    };

    /// <summary>The subject of the exposure rows: which camera they describe.</summary>
    public string ExposureSubject => CameraOnNext switch
    {
        null => "No camera on Next",
        { IsSupported: false } camera => $"{camera.Name} · {camera.ProtocolLabel} unsupported",
        var camera => $"{camera.Name} exposure",
    };
}
