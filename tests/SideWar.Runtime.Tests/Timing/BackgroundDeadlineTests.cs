using SideWar.Runtime.Timing;
using Xunit;

namespace SideWar.Runtime.Tests.Timing;

public sealed class RearmableDeadlineTests
{
    [Fact]
    public void LastTriggerWinsAndPostponesTheDeadline() // SDD-16 RF-16.8: reagendável
    {
        var d = new RearmableDeadline();
        Assert.Null(d.Deadline);
        d.Trigger(100, 1000);
        Assert.Equal(1100, d.Deadline);
        d.Trigger(600, 1000);
        Assert.Equal(1600, d.Deadline);
        Assert.False(d.IsDue(1100));
        Assert.True(d.IsDue(1600));
    }

    [Fact]
    public void ShorterLaterTriggerCanAccelerateAndClearStops()
    {
        var d = new RearmableDeadline();
        d.Trigger(0, 1000); // z-order normal (1 s)
        d.Trigger(10, 100); // shell foreground (100 ms): the last one wins
        Assert.Equal(110, d.Deadline);
        d.Clear();
        Assert.Null(d.Deadline);
        Assert.False(d.IsDue(long.MaxValue));
    }
}

public sealed class TooltipThrottleTests
{
    private const long Second = 1000;

    [Fact]
    public void UnchangedTextNeverApplies()
    {
        var t = new TooltipThrottle("A", Second, 0);
        Assert.False(t.Offer("A", 5 * Second));
        Assert.Null(t.NextDeadline);
    }

    [Fact]
    public void HeldBackTextExposesDeadlineAndFlushAppliesOnlyWhenDue()
    {
        var t = new TooltipThrottle("A", Second, 0);
        Assert.False(t.Offer("B", 200));
        Assert.Equal("A", t.Current);
        Assert.Equal(Second, t.NextDeadline);
        Assert.False(t.Flush(999));
        Assert.True(t.Flush(Second));
        Assert.Equal("B", t.Current);
        Assert.Null(t.NextDeadline);
        Assert.False(t.Flush(10 * Second)); // nothing pending: no wake needed
    }

    [Fact]
    public void LatestHeldTextWinsAndRevertingCancelsPending()
    {
        var t = new TooltipThrottle("A", Second, 0);
        t.Offer("B", 100);
        t.Offer("C", 200);
        Assert.True(t.Flush(Second));
        Assert.Equal("C", t.Current);
        t.Offer("D", Second + 10);
        t.Offer("C", Second + 20); // back to the shown text
        Assert.Null(t.NextDeadline);
    }

    [Fact]
    public void AppliesImmediatelyOnceIntervalElapsed()
    {
        var t = new TooltipThrottle("A", Second, 0);
        Assert.True(t.Offer("B", Second));
        Assert.False(t.Offer("C", Second + 1));
        Assert.Equal(2 * Second, t.NextDeadline);
    }
}
