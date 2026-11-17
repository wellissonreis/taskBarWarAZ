using SideWar.Runtime.Td;
using Xunit;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.14 (RF-28.19): ausente depois de ativar outro app; presente ao interagir com o SideWar; esmaecer é configurável.</summary>
public class TdFocusStateTests
{
    [Fact]
    public void Starts_present_and_opaque()
    {
        var f = new TdFocusState();
        Assert.True(f.IsPresent);
        Assert.False(f.FadeActive);
        Assert.Equal(255, f.Alpha);
    }

    [Fact]
    public void Foreign_app_makes_the_player_absent_and_fades_to_the_configured_level_until_interaction()
    {
        var f = new TdFocusState();
        int changes = 0;
        f.Changed += () => changes++;
        f.OnForeignActivated();
        Assert.False(f.IsPresent);
        Assert.True(f.FadeActive);
        Assert.Equal(127, f.Alpha); // 50% (padrão)
        f.OnForeignActivated(); // repetido: sem novo evento
        Assert.Equal(1, changes);
        f.OnInteraction();
        Assert.True(f.IsPresent);
        Assert.Equal(255, f.Alpha);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Fade_can_be_turned_off_but_absence_is_still_tracked_and_level_is_configurable()
    {
        var f = new TdFocusState();
        f.Configure(false, 50);
        f.OnForeignActivated();
        Assert.False(f.IsPresent);
        Assert.False(f.FadeActive);
        Assert.Equal(255, f.Alpha);
        f.Configure(true, 30);
        Assert.Equal(76, f.Alpha);
    }
}
