using CueVideo.Atem;

namespace CueVideo.Core;

/// <summary>Which keyer a volunteer button drives.</summary>
public enum KeyTarget
{
    Upstream,
    Downstream,
}

/// <summary>A key toggle in the switcher's 0-based wire addressing.</summary>
/// <param name="Target">Upstream keyer or downstream key.</param>
/// <param name="Me">0-based M/E index; 0 for a downstream key.</param>
/// <param name="KeyIndex">0-based keyer or DSK index.</param>
/// <param name="On">Whether the key should be on air after the toggle.</param>
public readonly record struct KeyToggle(KeyTarget Target, int Me, int KeyIndex, bool On);

/// <summary>
/// Converts the Mac's 1-based key-button config into switcher commands. The config is the
/// only place that names a key or DSK, and the conversion to wire values happens here once.
/// </summary>
public static class KeyButtons
{
    public static KeyToggle? Toggle(this KeyButtonConfig button, AtemState? state)
    {
        if (button.Dsk is { } dsk)
        {
            if (dsk < 1) return null;

            var index = SwitcherAddress.DskIndex(dsk);
            var on = state?.DskOnAir.GetValueOrDefault(index) ?? false;
            return new KeyToggle(KeyTarget.Downstream, 0, index, !on);
        }

        if (button.Keyer is not { } keyer || keyer < 1 || button.Me is <= 0) return null;

        var me = SwitcherAddress.MeIndex(button.Me ?? 1);
        var keyerIndex = SwitcherAddress.KeyerIndex(keyer);
        var keyOn = state?.KeyOnAir.TryGetValue(me, out var keys) == true &&
                    keys.GetValueOrDefault(keyerIndex);
        return new KeyToggle(KeyTarget.Upstream, me, keyerIndex, !keyOn);
    }
}
