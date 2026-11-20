using CueVideo.Visca;

namespace CueVideo.Tests;

public class ViscaCommandTests
{
    [Fact]
    public void DriveSetsSpeedDirectionAndStopsIdleAxes()
    {
        var right = ViscaCommands.Drive(1, 0);

        Assert.Equal(new byte[] { 0x81, 0x01, 0x06, 0x01, 0x18, 0x01, 0x02, 0x03, 0xFF }, right);

        var stopped = ViscaCommands.Drive(0, 0);
        Assert.Equal(new byte[] { 0x81, 0x01, 0x06, 0x01, 0x01, 0x01, 0x03, 0x03, 0xFF }, stopped);
    }

    [Fact]
    public void ZoomEncodesDirectionAndAmount()
    {
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x07, 0x00, 0xFF }, ViscaCommands.Zoom(0));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x07, 0x27, 0xFF }, ViscaCommands.Zoom(1));
        Assert.Equal(new byte[] { 0x81, 0x01, 0x04, 0x07, 0x34, 0xFF }, ViscaCommands.Zoom(-0.5));
    }

    [Fact]
    public void PresetSlotsRespectFirstPreset()
    {
        Assert.Equal(20, ViscaCommands.PresetSlot(1, 20));
        Assert.Equal(27, ViscaCommands.PresetSlot(8, 20));
        Assert.Equal(4, ViscaCommands.PresetSlot(4, 1));
        Assert.Equal(0x01, ViscaCommands.PresetStore(21, 20)[4]);
        Assert.Equal(40, ViscaCommands.PresetStore(21, 20)[5]);
    }

    [Fact]
    public void AbsoluteMovesUseFourNibbles()
    {
        var packet = ViscaCommands.AbsolutePanTilt(0x0ABC, -2);

        Assert.Equal(new byte[]
        {
            0x81, 0x01, 0x06, 0x02, 0x14, 0x12,
            0x00, 0x0A, 0x0B, 0x0C,
            0x0F, 0x0F, 0x0F, 0x0E,
            0xFF,
        }, packet);
    }

    [Fact]
    public void FieldOfViewNarrowsAcrossTheZoomRange()
    {
        var units = ViscaUnits.PtzOptics;

        Assert.Equal(58, units.Fov(0), 3);
        Assert.True(units.Fov(16384) < units.Fov(4096));
        Assert.True(units.Fov(16384) > 0);
    }

    [Fact]
    public void UnitsConvertCountsToDegrees()
    {
        var position = ViscaUnits.PtzOptics.ToPosition(1440, -720, 0);

        Assert.Equal(10000, position.Pan);
        Assert.Equal(-5000, position.Tilt);
        Assert.Equal(5800, position.Zoom);
    }
}
