namespace CueVideo.Ndi;

/// <summary>
/// The sender to open for a saved source name. NDI names a source "COMPUTER (Channel)", and
/// the computer half is not stable: the same machine can announce itself by its sharing name
/// one day and its network name the next ("PRODUCTN01SMINI", "PRODUCTION-01S-MAC-MINI.LOCAL").
/// So when the saved name is not on the network but exactly one sender has the same channel,
/// that is the one. Otherwise the saved name, as it is.
/// </summary>
public static class NdiName
{
    /// <summary>Resolves <paramref name="wanted"/> against the senders currently advertised.</summary>
    public static string Match(string wanted, IReadOnlyList<string> names)
    {
        // Names come from NDI exactly as scanned, and the rest of the port compares them
        // case-insensitively, so a saved name that only differs in case still resolves.
        foreach (var name in names)
        {
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) return name;
        }

        // A saved name may be the channel alone ("CueVideo CAM1") or the whole
        // "COMPUTER (Channel)" name; either half identifies the sender.
        var channel = Channel(wanted) ?? wanted;

        string? found = null;

        foreach (var name in names)
        {
            if (!string.Equals(Channel(name), channel, StringComparison.OrdinalIgnoreCase)) continue;

            // Two senders claiming one channel is not a guess; keep what was saved.
            if (found is not null) return wanted;
            found = name;
        }

        return found ?? wanted;
    }

    /// <summary>"COMPUTER (Channel)" gives "Channel".</summary>
    public static string? Channel(string full)
    {
        var open = full.IndexOf('(');
        if (open < 0 || !full.EndsWith(")", StringComparison.Ordinal)) return null;
        return full[(open + 1)..^1];
    }
}
