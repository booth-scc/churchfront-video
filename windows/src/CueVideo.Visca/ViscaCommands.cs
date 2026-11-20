namespace CueVideo.Visca;

/// <summary>
/// VISCA byte builders. Everything is addressed to camera 1 (0x81) and terminated with 0xFF,
/// exactly as the macOS implementation sends it.
/// </summary>
public static class ViscaCommands
{
    public const byte Terminator = 0xFF;

    /// <summary>Resets the camera's sequence handling; sent once when a UDP link opens.</summary>
    public static byte[] SequenceReset() => [0x02, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01];

    public static byte[] PowerInquiry() => [0x81, 0x09, 0x04, 0x00, Terminator];

    /// <summary>Joystick-style move. Pan and tilt are -1..1; 0 stops that axis.</summary>
    public static byte[] Drive(double pan, double tilt)
    {
        pan = Math.Clamp(pan, -1, 1);
        tilt = Math.Clamp(tilt, -1, 1);

        var panSpeed = pan == 0 ? 1 : Math.Clamp((int)Math.Round(pan * pan * 24), 1, 0x18);
        var tiltSpeed = tilt == 0 ? 1 : Math.Clamp((int)Math.Round(tilt * tilt * 20), 1, 0x14);

        var panDirection = pan == 0 ? (byte)0x03 : pan < 0 ? (byte)0x01 : (byte)0x02;
        var tiltDirection = tilt == 0 ? (byte)0x03 : tilt > 0 ? (byte)0x01 : (byte)0x02;

        return [0x81, 0x01, 0x06, 0x01, (byte)panSpeed, (byte)tiltSpeed, panDirection, tiltDirection, Terminator];
    }

    /// <summary>-10000..10000 raw drive, mapped the way the tracker and joystick use it.</summary>
    public static byte[] DriveRaw(int pan, int tilt)
    {
        static double Normalise(int raw) => raw == 0
            ? 0
            : Math.Sign(raw) * Math.Min(1, Math.Sqrt(Math.Abs(raw) / 10000.0) * 1.8);

        return Drive(Normalise(pan), Normalise(tilt));
    }

    public static byte[] Stop() => Drive(0, 0);

    /// <summary>Zoom speed -1..1.</summary>
    public static byte[] Zoom(double speed)
    {
        speed = Math.Clamp(speed, -1, 1);
        var amount = (byte)Math.Clamp((int)Math.Round(Math.Abs(speed) * 7), 0, 7);

        if (speed == 0) return [0x81, 0x01, 0x04, 0x07, 0x00, Terminator];

        var direction = speed > 0 ? 0x20 : 0x30;
        return [0x81, 0x01, 0x04, 0x07, (byte)(direction | amount), Terminator];
    }

    public static byte[] AutoFocus(bool on) => [0x81, 0x01, 0x04, 0x38, on ? (byte)0x02 : (byte)0x03, Terminator];
    public static byte[] FocusNear() => [0x81, 0x01, 0x04, 0x08, 0x02, Terminator];
    public static byte[] FocusFar() => [0x81, 0x01, 0x04, 0x08, 0x03, Terminator];
    public static byte[] FocusStop() => [0x81, 0x01, 0x04, 0x08, 0x00, Terminator];
    public static byte[] FocusOnce() => [0x81, 0x01, 0x04, 0x18, 0x01, Terminator];
    public static byte[] Home() => [0x81, 0x01, 0x06, 0x04, Terminator];

    /// <summary>Preset numbers are 1-based; the camera stores them from firstPreset upwards.</summary>
    public static byte[] PresetRecall(int number, int firstPreset) =>
        [0x81, 0x01, 0x04, 0x3F, 0x02, PresetSlot(number, firstPreset), Terminator];

    public static byte[] PresetStore(int number, int firstPreset) =>
        [0x81, 0x01, 0x04, 0x3F, 0x01, PresetSlot(number, firstPreset), Terminator];

    public static byte[] PresetClear(int number, int firstPreset) =>
        [0x81, 0x01, 0x04, 0x3F, 0x00, PresetSlot(number, firstPreset), Terminator];

    /// <summary>
    /// StreamDeck slots 1-9 map straight through; the Windows app's own slots start at
    /// <c>firstPreset</c> (20), so the two never overwrite each other.
    /// </summary>
    public static byte PresetSlot(int number, int firstPreset) =>
        (byte)Math.Clamp(number - 1 + firstPreset, 0, 255);

    public static byte[] PanTiltInquiry() => [0x81, 0x09, 0x06, 0x12, Terminator];
    public static byte[] ZoomInquiry() => [0x81, 0x09, 0x04, 0x47, Terminator];

    public static byte[] AutoExposureInquiry() => [0x81, 0x09, 0x04, 0x39, Terminator];
    public static byte[] ShutterInquiry() => [0x81, 0x09, 0x04, 0x4A, Terminator];
    public static byte[] IrisInquiry() => [0x81, 0x09, 0x04, 0x4B, Terminator];
    public static byte[] GainInquiry() => [0x81, 0x09, 0x04, 0x4C, Terminator];
    public static byte[] WhiteBalanceModeInquiry() => [0x81, 0x09, 0x04, 0x35, Terminator];
    public static byte[] ColourTemperatureInquiry() => [0x81, 0x09, 0x04, 0x20, Terminator];

    public static byte[] SetAutoExposure() => [0x81, 0x01, 0x04, 0x39, 0x00, Terminator];
    public static byte[] SetManualExposure() => [0x81, 0x01, 0x04, 0x39, 0x03, Terminator];

    /// <summary>Shutter position, iris position or gain level in the camera's own numbering.</summary>
    public static byte[] SetShutterPosition(int position) => [0x81, 0x01, 0x04, 0x4A, 0x00, 0x00, High(position), Low(position), Terminator];
    public static byte[] SetIrisPosition(int position) => [0x81, 0x01, 0x04, 0x4B, 0x00, 0x00, High(position), Low(position), Terminator];
    public static byte[] SetGainLevel(int level) => [0x81, 0x01, 0x04, 0x0C, 0x00, 0x00, High(level), Low(level), Terminator];

    public static byte[] SetWhiteBalanceMode(byte code) => [0x81, 0x01, 0x04, 0x35, code, Terminator];

    public static byte[] SetColourTemperature(int position) =>
        [0x81, 0x01, 0x04, 0x20, High(position), Low(position), Terminator];

    /// <summary>Absolute move in the camera's own counts, at a brisk speed.</summary>
    public static byte[] AbsolutePanTilt(int pan, int tilt, byte panSpeed = 0x14, byte tiltSpeed = 0x12) =>
    [
        0x81, 0x01, 0x06, 0x02, panSpeed, tiltSpeed,
        Nibble(pan, 0), Nibble(pan, 1), Nibble(pan, 2), Nibble(pan, 3),
        Nibble(tilt, 0), Nibble(tilt, 1), Nibble(tilt, 2), Nibble(tilt, 3),
        Terminator,
    ];

    public static byte[] AbsoluteZoom(int counts) =>
    [
        0x81, 0x01, 0x04, 0x47,
        Nibble(counts, 0), Nibble(counts, 1), Nibble(counts, 2), Nibble(counts, 3),
        Terminator,
    ];

    /// <summary>Signed 16-bit value as four VISCA nibbles, most significant first.</summary>
    public static byte Nibble(int value, int index)
    {
        var shift = 12 - 4 * index;
        return (byte)((value >> shift) & 0x0F);
    }

    private static byte High(int value) => (byte)(((value & 0xF0) >> 4) & 0x0F);
    private static byte Low(int value) => (byte)(value & 0x0F);
}
