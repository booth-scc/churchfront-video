using CueVideo.Core;
using CueVideo.Visca;

namespace CueVideo.Tests;

/// <summary>
/// The Operator screen's exposure rows are strings. These tests pin how a camera reading
/// becomes those strings, away from any link or window.
/// </summary>
public class ExposureReadoutTests
{
    [Fact]
    public void NamesGainShutterIrisAndWhiteBalanceTheWayThePanelShowsThem()
    {
        var readout = ExposureReadout.From(new CameraExposure
        {
            Gain = 6,
            Shutter = 250,
            Iris = 280,
            WhiteBalance = "kelvin",
            Kelvin = 5600,
        });

        Assert.Equal("6 dB", readout.Gain);
        Assert.Equal("1/250", readout.Shutter);
        Assert.Equal("f/2.8", readout.Iris);
        Assert.Equal("5600 K", readout.WhiteBalance);
    }

    [Fact]
    public void ValuesTheCameraDidNotReportReadAsAuto()
    {
        var readout = ExposureReadout.From(new CameraExposure { WhiteBalance = "auto" });

        Assert.Equal("Auto", readout.Gain);
        Assert.Equal("Auto", readout.Shutter);
        Assert.Equal("Auto", readout.Iris);
        Assert.Equal("Auto", readout.WhiteBalance);
    }

    [Fact]
    public void AKelvinModeWithoutATemperatureStillNamesTheMode()
    {
        var readout = ExposureReadout.From(new CameraExposure { WhiteBalance = "kelvin" });

        Assert.Equal("Kelvin", readout.WhiteBalance);
    }

    [Fact]
    public void WhiteBalanceWithNoReadingShowsADash()
    {
        var readout = ExposureReadout.From(new CameraExposure());

        Assert.Equal("—", readout.WhiteBalance);
    }

    [Fact]
    public void ACameraThatCannotBeReadShowsDashes()
    {
        Assert.Equal(ExposureReadout.Unknown, ExposureReadout.From(null));
    }
}
