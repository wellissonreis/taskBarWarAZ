using SideWar.Runtime.Input;
using Xunit;

namespace SideWar.Runtime.Tests.Input;

public sealed class OverlayModeControllerTests
{
    private readonly FakeClock clock = new();
    private OverlayModeController New() => new(clock.TicksPerSecond);
    private long Ticks(double seconds) => (long)(seconds * clock.TicksPerSecond);

    [Fact]
    public void StartsPassiveTransparentAndNeverFullyTransparent() // CA-23.1
    {
        var c = New();
        Assert.Equal(OverlayMode.Passive, c.Mode);
        Assert.Equal(new ModeEffects(true, 255, false), c.Effects);
        Assert.Null(c.NextDeadline);
    }

    [Theory]
    [InlineData(false, 255)]
    [InlineData(true, 128)]
    public void AlphaFollowsPresenceNotMode(bool absent, int alpha)
    {
        var c = New();
        c.SetAbsent(absent);
        Assert.Equal(alpha, c.Effects.Alpha);
        c.Toggle(0);
        Assert.Equal(alpha, c.Effects.Alpha);
        Assert.All(new[] { c.Effects.Alpha }, a => Assert.True(a > 0));
    }

    [Fact]
    public void ToggleSwitchesBothWays()
    {
        var c = New();
        Assert.Equal(new ModeEffects(false, 255, false), c.Toggle(0));
        Assert.Equal(OverlayMode.Interactive, c.Mode);
        Assert.Equal(new ModeEffects(true, 255, false), c.Toggle(1));
        Assert.Null(c.NextDeadline);
    }

    [Fact]
    public void SetSameModeIsNoOp()
    {
        var c = New();
        Assert.Null(c.SetMode(OverlayMode.Passive, 0));
        c.SetMode(OverlayMode.Interactive, 0);
        Assert.Null(c.SetMode(OverlayMode.Interactive, 5));
    }

    [Fact]
    public void ReturnsToPassiveAfterEightSecondsWithoutCursor() // CA-04.4
    {
        var c = New();
        c.Toggle(clock.NowTicks);
        Assert.Equal(Ticks(8), c.NextDeadline);
        Assert.Null(c.OnTick(Ticks(7.99)));
        var fx = c.OnTick(Ticks(8));
        Assert.Equal(new ModeEffects(true, 255, false), fx);
        Assert.Equal(OverlayMode.Passive, c.Mode);
    }

    [Fact]
    public void CursorOverCancelsAndLeaveRestartsCountdown()
    {
        var c = New();
        c.Toggle(0);
        c.OnMouseMove();
        Assert.Null(c.NextDeadline);
        Assert.Null(c.OnTick(Ticks(100)));
        c.OnMouseLeave(Ticks(100));
        Assert.Equal(Ticks(108), c.NextDeadline);
        c.OnMouseMove();
        Assert.Null(c.NextDeadline);
    }

    [Fact]
    public void ActiveDragHoldsModeAndItsEndRestartsCountdown()
    {
        var c = New();
        c.Toggle(0);
        c.SetDragActive(true, Ticks(1));
        Assert.Null(c.NextDeadline);
        c.OnMouseLeave(Ticks(2)); // leaving during a drag must not start the countdown
        Assert.Null(c.NextDeadline);
        c.SetDragActive(false, Ticks(10));
        Assert.Equal(Ticks(18), c.NextDeadline);
    }

    [Fact]
    public void DragEndWithCursorStillOverKeepsCountdownOff()
    {
        var c = New();
        c.Toggle(0);
        c.OnMouseMove();
        c.SetDragActive(true, 1);
        c.SetDragActive(false, 2);
        Assert.Null(c.NextDeadline);
    }

    [Fact]
    public void LeavingInteractiveWithDragCancelsDrag() // RF-04.2
    {
        var c = New();
        c.Toggle(0);
        c.SetDragActive(true, 1);
        var fx = c.Toggle(2);
        Assert.True(fx!.Value.CancelDrag);
        Assert.False(c.DragActive);
    }

    [Fact]
    public void LeavingInteractiveWithoutDragDoesNotCancel()
    {
        var c = New();
        c.Toggle(0);
        Assert.False(c.Toggle(1)!.Value.CancelDrag);
    }

    [Fact]
    public void HiddenAlwaysResultsInPassive()
    {
        var c = New();
        Assert.Null(c.OnHidden(0)); // already passive
        c.Toggle(0);
        Assert.Equal(new ModeEffects(true, 255, false), c.OnHidden(1));
        Assert.Equal(OverlayMode.Passive, c.Mode);
    }

    [Fact]
    public void PassiveIgnoresMouseAndDragEvents()
    {
        var c = New();
        c.OnMouseMove();
        c.OnMouseLeave(0);
        c.SetDragActive(true, 0);
        Assert.False(c.CursorOver);
        Assert.False(c.DragActive);
        Assert.Null(c.NextDeadline);
        Assert.Null(c.OnTick(Ticks(100)));
    }

    [Fact]
    public void ReenteringInteractiveStartsFreshCountdown()
    {
        var c = New();
        c.Toggle(0);
        c.OnTick(Ticks(8));
        c.Toggle(Ticks(20));
        Assert.Equal(Ticks(28), c.NextDeadline);
    }

    [Fact]
    public void AutoPassiveSecondsIsConfigurable()
    {
        var c = new OverlayModeController(clock.TicksPerSecond, 3);
        c.Toggle(0);
        Assert.Equal(Ticks(3), c.NextDeadline);
    }

    [Fact]
    public void InvalidArgumentsThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayModeController(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayModeController(1, 0));
    }
}

public sealed class PeekControllerTests
{
    private readonly FakeClock clock = new();
    private PeekController New() => new(clock.TicksPerSecond);
    private long Ms(int ms) => (long)(ms / 1000.0 * clock.TicksPerSecond);

    [Fact]
    public void DisabledOrNotPassiveOrHiddenDoesNotPoll()
    {
        var p = New();
        Assert.False(p.Configure(false, true, true, 0));
        Assert.Null(p.NextPoll);
        p.Configure(true, false, true, 0);
        Assert.Null(p.NextPoll);
        p.Configure(true, true, false, 0);
        Assert.Null(p.NextPoll);
        p.Configure(true, true, true, 0);
        Assert.Equal(0, p.NextPoll);
    }

    [Fact]
    public void PollsAtEightHz()
    {
        var p = New();
        p.Configure(true, true, true, 0);
        Assert.True(p.PollDue(0));
        p.OnSample(0, false);
        Assert.Equal(Ms(125), p.NextPoll);
        Assert.False(p.PollDue(Ms(124)));
        Assert.True(p.PollDue(Ms(125)));
    }

    [Fact]
    public void FadesWithin300msOfEntering() // CA-04.5
    {
        var p = New();
        p.Configure(true, true, true, 0);
        Assert.False(p.OnSample(0, true));
        Assert.False(p.OnSample(Ms(125), true));
        Assert.True(p.OnSample(Ms(250), true));
        Assert.True(p.Peeking);
        Assert.True(250 <= 300);
    }

    [Fact]
    public void ShortVisitDoesNotFadeAndResetsOnLeave()
    {
        var p = New();
        p.Configure(true, true, true, 0);
        p.OnSample(0, true);
        p.OnSample(Ms(125), false);
        p.OnSample(Ms(250), true); // restarts presence timer
        Assert.False(p.OnSample(Ms(375), true));
        Assert.False(p.Peeking);
        Assert.True(p.OnSample(Ms(500), true));
    }

    [Fact]
    public void RestoresAfter150msOutside()
    {
        var p = New();
        p.Configure(true, true, true, 0);
        p.OnSample(0, true);
        p.OnSample(Ms(250), true);
        Assert.False(p.OnSample(Ms(375), false));
        Assert.False(p.OnSample(Ms(500), false)); // 125 ms outside < 150 ms
        Assert.True(p.OnSample(Ms(625), false));
        Assert.False(p.Peeking);
    }

    [Fact]
    public void LeavingPassiveRestoresImmediately()
    {
        var p = New();
        p.Configure(true, true, true, 0);
        p.OnSample(0, true);
        p.OnSample(Ms(250), true);
        Assert.True(p.Configure(true, false, true, Ms(260)));
        Assert.False(p.Peeking);
        Assert.Null(p.NextPoll);
        Assert.False(p.OnSample(Ms(300), true)); // inactive: ignored
    }

    [Fact]
    public void ReconfiguringSameStateKeepsPeeking()
    {
        var p = New();
        p.Configure(true, true, true, 0);
        p.OnSample(0, true);
        p.OnSample(Ms(250), true);
        Assert.False(p.Configure(true, true, true, Ms(260)));
        Assert.True(p.Peeking);
    }
}

public sealed class SceneHitTesterTests
{
    private static readonly HitRegion[] Regions =
    [
        new(HitKind.SceneEmptySlot, 0, 10, 40, 44, 50),
        new(HitKind.SceneTower, 1, 60, 20, 44, 70),
        new(HitKind.SceneEmptySlot, 2, 100, 40, 44, 50),
        new(HitKind.SceneTower, 3, 110, 20, 44, 70),
    ];

    [Theory]
    [InlineData(20, 60, HitKind.SceneEmptySlot, 0)]
    [InlineData(70, 30, HitKind.SceneTower, 1)]
    [InlineData(10, 40, HitKind.SceneEmptySlot, 0)]   // inclusive top-left
    [InlineData(54, 40, HitKind.None, -1)]            // exclusive right edge
    [InlineData(55, 100, HitKind.None, -1)]
    [InlineData(5, 5, HitKind.None, -1)]
    [InlineData(120, 50, HitKind.SceneTower, 3)]      // tower wins over overlapping empty slot
    [InlineData(105, 60, HitKind.SceneEmptySlot, 2)]  // only the empty slot there
    public void TableOfPoints(float x, float y, HitKind kind, int slot)
    {
        Assert.Equal(new HitTarget(kind, slot), SceneHitTester.Test(Regions, x, y));
    }

    [Fact]
    public void OverlappingSameKindPrefersLowerSlot()
    {
        HitRegion[] r = [new(HitKind.SceneTower, 5, 0, 0, 50, 50), new(HitKind.SceneTower, 2, 0, 0, 50, 50)];
        Assert.Equal(new HitTarget(HitKind.SceneTower, 2), SceneHitTester.Test(r, 10, 10));
    }

    [Fact]
    public void EmptyAndNonFiniteAreNone()
    {
        Assert.Equal(HitTarget.None, SceneHitTester.Test([], 1, 1));
        Assert.Equal(HitTarget.None, SceneHitTester.Test(Regions, float.NaN, 1));
    }

    [Theory]
    [InlineData(150, 75, 1.5f, 100f, 50f)]
    [InlineData(96, 96, 1f, 96f, 96f)]
    [InlineData(384, 192, 2f, 192f, 96f)]
    public void PixelsToDip(int x, int y, float scale, float ex, float ey)
    {
        Assert.Equal((ex, ey), SceneHitTester.ToDip(x, y, scale));
    }
}
