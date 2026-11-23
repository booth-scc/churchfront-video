using CueVideo.Visca;

namespace CueVideo.Core;

/// <summary>
/// Opens camera links, and refuses the ones this port cannot drive. Windows speaks VISCA
/// over IP only; a macOS config may also name Canon HTTP or VISCA over TCP cameras.
/// </summary>
public static class CameraLinks
{
    /// <summary>A started link, or null when the camera has no address or an unsupported protocol.</summary>
    public static ViscaIpLink? Open(PtzCameraConfig camera)
    {
        if (!camera.IsSupported || string.IsNullOrWhiteSpace(camera.Host)) return null;

        var link = new ViscaIpLink(camera.Host, camera.EffectivePort, camera.FirstPreset);
        link.Start();
        return link;
    }
}
