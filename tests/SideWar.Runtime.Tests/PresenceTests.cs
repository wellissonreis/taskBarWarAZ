using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests;

public sealed class PresenceTests
{
    private static long Sec(double s) => (long)(s * 30_000_000);
    private static long Ms(double ms) => (long)(ms * 30_000);

    private static (PresenceTracker t, FakeClock c) Visible()
    {
        var c = new FakeClock();
        var t = new PresenceTracker(c, () => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        t.VisibilityChanged(true, c.NowTicks, byUser: true);
        return (t, c);
    }

    [Fact]
    public void StartsOffline() => Assert.Equal(PresenceState.Offline, new PresenceTracker(new FakeClock()).State);

    [Fact]
    public void UserShowIsPresentAndSetsLastInteractionUtc()
    {
        var (t, _) = Visible();
        Assert.Equal(PresenceState.Presente, t.State);
        Assert.NotNull(t.LastInteractionUtc);
    }

    [Fact]
    public void PresentToAbsentBoundaries()
    {
        var (t, _) = Visible();
        t.Advance(Sec(299));
        Assert.Equal(PresenceState.Presente, t.State);
        t.Advance(Sec(300));
        Assert.Equal(PresenceState.Presente, t.State);
        Assert.Equal(Sec(300) + Ms(1), t.NextDeadlineTicks);
        t.Advance(Sec(300) + Ms(1));
        Assert.Equal(PresenceState.Ausente, t.State);
        Assert.Null(t.NextDeadlineTicks);
        Assert.Equal(0, t.PresentSeconds);
    }

    [Fact]
    public void InteractionMovesDeadlineAndAbsentReturnsToPresent()
    {
        var (t, _) = Visible();
        t.InteractionObserved(Sec(200));
        t.Advance(Sec(499));
        Assert.Equal(PresenceState.Presente, t.State);
        t.Advance(Sec(501));
        Assert.Equal(PresenceState.Ausente, t.State);
        t.InteractionObserved(Sec(600));
        Assert.Equal(PresenceState.Presente, t.State);
        Assert.Equal(0, t.PresentSeconds);
    }

    [Fact]
    public void PresentSecondsAccumulatesAndResetsWhenAbsent()
    {
        var (t, _) = Visible();
        t.InteractionObserved(Sec(100));
        t.Advance(Sec(250));
        Assert.Equal(250, t.PresentSeconds, 6);
        t.Advance(Sec(401));
        Assert.Equal(PresenceState.Ausente, t.State);
        Assert.Equal(0, t.PresentSeconds);
    }

    [Theory]
    [InlineData("User")] [InlineData("Fullscreen")] [InlineData("SessionLocked")]
    [InlineData("RemoteDisconnected")] [InlineData("DisplayOff")] [InlineData("Suspended")] [InlineData("Starting")]
    public void AnyHiddenReasonIsOfflineAndResetsPresentSeconds(string reason)
    {
        var (t, _) = Visible();
        t.Advance(Sec(60));
        Assert.True(t.PresentSeconds > 0, reason);
        t.VisibilityChanged(false, Sec(60));
        Assert.Equal(PresenceState.Offline, t.State);
        Assert.Equal(0, t.PresentSeconds);
        t.InteractionObserved(Sec(61)); // ignorada oculto
        Assert.Equal(PresenceState.Offline, t.State);
    }

    [Fact]
    public void AutomaticShowDoesNotCountAsInteraction()
    {
        var (t, _) = Visible();
        t.VisibilityChanged(false, Sec(10));
        t.VisibilityChanged(true, Sec(400)); // automático, última interação há 400 s
        Assert.Equal(PresenceState.Ausente, t.State);
        t.VisibilityChanged(false, Sec(410));
        t.VisibilityChanged(true, Sec(420), byUser: true);
        Assert.Equal(PresenceState.Presente, t.State);
    }

    [Fact]
    public void AutomaticShowWithin5MinutesIsPresent()
    {
        var (t, _) = Visible();
        t.VisibilityChanged(false, Sec(10));
        t.VisibilityChanged(true, Sec(100));
        Assert.Equal(PresenceState.Presente, t.State);
        Assert.Equal(Sec(300) + Ms(1), t.NextDeadlineTicks);
    }

    [Fact]
    public void AutomaticShowWithoutAnyInteractionIsAbsent()
    {
        var c = new FakeClock();
        var t = new PresenceTracker(c);
        t.VisibilityChanged(true, 0);
        Assert.Equal(PresenceState.Ausente, t.State);
    }

    [Theory]
    [InlineData(0, 1.5)] [InlineData(899.9, 1.5)] [InlineData(900, 2.0)] [InlineData(3599.9, 2.0)]
    [InlineData(3600, 2.5)] [InlineData(7200, 2.5)]
    public void PresentMultiplierTiers(double secs, double expected) =>
        Assert.Equal(expected, PresenceMultiplier.Evaluate(PresenceState.Presente, secs));

    [Theory]
    [InlineData(PresenceState.Ausente)] [InlineData(PresenceState.Offline)]
    public void NotPresentIsQuarter(PresenceState s) =>
        Assert.Equal(0.25, PresenceMultiplier.Evaluate(s, 5000));

    [Fact]
    public void TrackerMultiplierFollowsContinuousPresenceAndResets()
    {
        var (t, _) = Visible();
        Assert.Equal(1.5, t.Multiplier());
        for (int s = 100; s <= 3600; s += 100) t.InteractionObserved(Sec(s));
        t.Advance(Sec(3600));
        Assert.Equal(2.5, t.Multiplier());
        t.Advance(Sec(3600 + 301));
        Assert.Equal(0.25, t.Multiplier());
        t.InteractionObserved(Sec(4000));
        Assert.Equal(1.5, t.Multiplier());
    }

    [Fact]
    public void CustomConfigIsHonored() =>
        Assert.Equal(9, PresenceMultiplier.Evaluate(PresenceState.Presente, 10,
            new PresenceMultiplierConfig(5, 20, 1, 9, 3, 0.1)));
}
