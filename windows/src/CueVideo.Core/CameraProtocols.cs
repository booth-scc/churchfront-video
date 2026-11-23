namespace CueVideo.Core;

/// <summary>
/// The camera protocols the Windows port can speak. The macOS config names three
/// ("sonyUDP", "rawTCP", "canonXC"); only VISCA over IP on UDP 52381 is ported here.
/// </summary>
public static class CameraProtocols
{
    public const string SonyUdp = "sonyUDP";

    private sealed record Known(string Label, int Port, bool Supported);

    private static readonly Dictionary<string, Known> KnownProtocols =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SonyUdp] = new("VISCA over IP (UDP)", 52381, Supported: true),
            ["rawTCP"] = new("VISCA over TCP", 5678, Supported: false),
            ["canonXC"] = new("Canon (HTTP)", 80, Supported: false),
        };

    public static bool IsSupported(string? proto) =>
        KnownProtocols.TryGetValue(Key(proto), out var known) && known.Supported;

    /// <summary>The label the macOS app uses, so a Mac-authored config reads the same here.</summary>
    public static string Label(string? proto)
    {
        var key = Key(proto);
        if (KnownProtocols.TryGetValue(key, out var known)) return known.Label;
        return key.Length == 0 ? "no protocol" : key;
    }

    public static int DefaultPort(string? proto) =>
        KnownProtocols.TryGetValue(Key(proto), out var known) ? known.Port : 5678;

    private static string Key(string? proto) => proto?.Trim() ?? "";
}
