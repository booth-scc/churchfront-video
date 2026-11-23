namespace CueVideo.Core;

/// <summary>
/// Converts the 1-based names on the switcher to the 0-based values ATEM puts on the wire.
/// This is the only place that conversion happens.
/// </summary>
public static class SwitcherAddress
{
    public static int MeIndex(int me) => me - 1;
    public static int KeyerIndex(int keyer) => keyer - 1;
    public static int DskIndex(int dsk) => dsk - 1;
}
