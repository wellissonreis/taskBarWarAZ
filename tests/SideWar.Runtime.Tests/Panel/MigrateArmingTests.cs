using SideWar.Runtime.Panel;
using Xunit;

namespace SideWar.Runtime.Tests.Panel;

public class MigrateArmingTests
{
    private static long Ticks(FakeClock c, double s) => (long)(s * c.TicksPerSecond);

    [Fact(DisplayName = "CA-24.8 first click arms without migrating; second within 5 s confirms")]
    public void ArmThenConfirm()
    {
        var clock = new FakeClock();
        var a = new MigrateArming(clock);
        Assert.Equal(MigrateClick.Armed, a.Click(true));
        Assert.True(a.Armed);
        clock.AdvanceTicks(Ticks(clock, 4.9));
        Assert.False(a.Update());
        Assert.Equal(MigrateClick.Confirmed, a.Click(true));
        Assert.False(a.Armed);
    }

    [Fact(DisplayName = "CA-24.8 5 s without click disarms (one render event); next click arms again")]
    public void ExpiresAfterFiveSeconds()
    {
        var clock = new FakeClock();
        var a = new MigrateArming(clock);
        a.Click(true);
        var v = a.Version;
        clock.AdvanceTicks(Ticks(clock, 5.0));
        Assert.True(a.Update());
        Assert.Equal(v + 1, a.Version);
        Assert.False(a.Update());
        Assert.Equal(MigrateClick.Armed, a.Click(true));
    }

    [Fact(DisplayName = "Late click after expiry without Update behaves as a first click")]
    public void LateClickIsFirstClick()
    {
        var clock = new FakeClock();
        var a = new MigrateArming(clock);
        a.Click(true);
        clock.AdvanceTicks(Ticks(clock, 6));
        Assert.Equal(MigrateClick.Armed, a.Click(true));
    }

    [Fact(DisplayName = "Cancel (tab switch, close, other click) disarms; scroll does not; unavailable ignores")]
    public void CancelAndUnavailable()
    {
        var a = new MigrateArming(new FakeClock());
        Assert.False(a.Cancel());
        a.Click(true);
        var v = a.Version;
        Assert.True(a.Cancel());
        Assert.False(a.Armed);
        Assert.Equal(v + 1, a.Version);
        Assert.Equal(MigrateClick.Ignored, a.Click(false));
        Assert.False(a.Armed);
    }
}
