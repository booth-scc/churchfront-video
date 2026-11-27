using CueVideo.Ndi;

namespace CueVideo.Tests;

/// <summary>
/// A saved NDI source still connects when its computer announces itself under another name.
/// Mirrors the Mac's Tests/CueVideoTests/NDINameTests.swift.
/// </summary>
public sealed class NdiNameTests
{
    private static readonly string[] OnNetwork =
    [
        "PRODUCTN01SMINI (CueScreens Side Screens)",
        "PRODUCTN01SMINI (CueScreens Stage)",
        "PTZOPTICS (Chan 1, 192.168.20.178)",
        "N5-A (Channel-NDI)",
        "N5-B (Channel-NDI)",
    ];

    [Fact]
    public void ExactNameIsKept() =>
        Assert.Equal(
            "PTZOPTICS (Chan 1, 192.168.20.178)",
            NdiName.Match("PTZOPTICS (Chan 1, 192.168.20.178)", OnNetwork));

    [Fact]
    public void SameChannelUnderAnotherComputerName() =>
        Assert.Equal(
            "PRODUCTN01SMINI (CueScreens Side Screens)",
            NdiName.Match("PRODUCTION-01S-MAC-MINI.LOCAL (CueScreens Side Screens)", OnNetwork));

    [Fact]
    public void TwoSendersWithTheChannelIsNotAGuess() =>
        Assert.Equal(
            "N5-C (Channel-NDI)",
            NdiName.Match("N5-C (Channel-NDI)", OnNetwork));

    [Fact]
    public void NothingLikeItStaysAsSaved()
    {
        Assert.Equal("OTHER (Lobby)", NdiName.Match("OTHER (Lobby)", OnNetwork));
        Assert.Equal("No brackets", NdiName.Match("No brackets", OnNetwork));
    }
}
