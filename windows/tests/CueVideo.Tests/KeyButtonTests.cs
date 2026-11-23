using System.Text.Json;
using CueVideo.Atem;
using CueVideo.Core;

namespace CueVideo.Tests;

public sealed class KeyButtonTests
{
    [Theory]
    [InlineData("Lower thirds", KeyTarget.Upstream, 0, 0, true)]
    [InlineData("Second key", KeyTarget.Upstream, 0, 1, false)]
    [InlineData("DSK 2", KeyTarget.Downstream, 0, 1, false)]
    public void MacShapedConfigTogglesTheIntendedKeyAndDsk(
        string label,
        KeyTarget target,
        int me,
        int keyIndex,
        bool on)
    {
        var config = JsonSerializer.Deserialize<CueConfig>("""
        {
          "keys": [
            { "label": "Lower thirds", "me": 1, "keyer": 1, "ndi": "GRAPHICS-MAC (CueScreens Lower Thirds)" },
            { "label": "Second key", "me": 1, "keyer": 2 },
            { "label": "DSK 2", "dsk": 2 }
          ]
        }
        """)!;

        var state = new AtemState();
        state.KeyOnAir[0] = new Dictionary<int, bool> { [0] = false, [1] = true };
        state.DskOnAir[1] = true;

        var toggle = config.Keys.Single(key => key.Label == label).Toggle(state);

        Assert.NotNull(toggle);
        Assert.Equal(new KeyToggle(target, me, keyIndex, on), toggle.Value);
    }

    [Fact]
    public void InvalidKeyNumbersAreIgnored()
    {
        Assert.Null(new KeyButtonConfig { Keyer = 0 }.Toggle(new AtemState()));
        Assert.Null(new KeyButtonConfig { Dsk = 0 }.Toggle(new AtemState()));
        Assert.Null(new KeyButtonConfig { Me = 0, Keyer = 1 }.Toggle(new AtemState()));
    }

    [Fact]
    public void BoothDefaultsAddressKeysFromOne()
    {
        var keys = CueConfig.BoothDefaults().Keys;

        Assert.Equal(new[] { 1, 2, 3, 4 }, keys.Select(key => key.Keyer!.Value));
        Assert.Equal(
            new[] { 0, 1, 2, 3 },
            keys.Select(key => key.Toggle(new AtemState())!.Value.KeyIndex));
    }
}
