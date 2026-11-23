using CueVideo.Visca;

namespace CueVideo.Core;

/// <summary>
/// Opens camera links, and refuses the ones this port cannot drive. Windows speaks VISCA
/// over IP only; a macOS config may also name Canon HTTP or VISCA over TCP cameras.
/// One owner keeps a started link per input, so every camera control shares one connection.
/// </summary>
public sealed class CameraLinks : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<CueConfig> _config;
    private readonly Dictionary<int, ViscaIpLink> _links = [];
    private bool _closed;

    public CameraLinks(Func<CueConfig> config) => _config = config;

    /// <summary>A started link, or null when the camera has no address or an unsupported protocol.</summary>
    public static ViscaIpLink? Open(PtzCameraConfig camera)
    {
        if (!camera.IsSupported || string.IsNullOrWhiteSpace(camera.Host)) return null;

        var link = new ViscaIpLink(camera.Host, camera.EffectivePort, camera.FirstPreset);
        link.Start();
        return link;
    }

    /// <summary>
    /// The started link for an input's camera, opened on first use. Null when the input has
    /// no camera, the camera cannot be driven, or the links have been closed.
    /// </summary>
    public ViscaIpLink? LinkFor(int input)
    {
        lock (_gate)
        {
            if (_closed) return null;
            if (_links.TryGetValue(input, out var existing)) return existing;
        }

        var camera = _config().CameraForInput(input);
        if (camera is null) return null;

        var link = Open(camera);
        if (link is null) return null;

        lock (_gate)
        {
            if (_closed)
            {
                link.Dispose();
                return null;
            }

            // Another caller may have opened this camera while Open was starting the socket.
            if (_links.TryGetValue(input, out var existing))
            {
                link.Dispose();
                return existing;
            }

            _links[input] = link;
            return link;
        }
    }

    /// <summary>Drops every link, so the next command opens each camera as it is configured now.</summary>
    public void Close()
    {
        ViscaIpLink[] links;

        lock (_gate)
        {
            links = [.. _links.Values];
            _links.Clear();
        }

        foreach (var link in links) link.Dispose();
    }

    public void Dispose()
    {
        lock (_gate) _closed = true;
        Close();
    }
}
