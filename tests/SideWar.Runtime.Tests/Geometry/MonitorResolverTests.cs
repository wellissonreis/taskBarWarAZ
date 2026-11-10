using SideWar.Runtime.Geometry;
using Xunit;

namespace SideWar.Runtime.Tests.Geometry;

public sealed class MonitorResolverTests
{
    private static MonitorInfo M(string name, int left, int top, int w, int h, bool primary = false) =>
        new(name, new PixelRect(left, top, left + w, top + h), new PixelRect(left, top, left + w, top + h - 48), 96, primary, 0);

    private static readonly MonitorInfo D1 = M(@"\\.\DISPLAY1", 0, 0, 1920, 1080, true);
    private static readonly MonitorInfo D2 = M(@"\\.\DISPLAY2", 1920, 0, 2560, 1440);

    [Fact]
    public void SameNameAndBoundsWins()
    {
        var hint = new DockMonitorHint(D2.DeviceName, D2.Bounds);
        Assert.Equal(D2, MonitorResolver.Resolve([D1, D2], hint));
    }

    [Fact]
    public void SameNameWithDifferentBoundsBeatsIntersection()
    {
        // D2 moved, but the name matches; the hint rect overlaps D1 more.
        var hint = new DockMonitorHint(D2.DeviceName, new PixelRect(0, 0, 1000, 1000));
        Assert.Equal(D2, MonitorResolver.Resolve([D1, D2], hint));
    }

    [Fact]
    public void LargestIntersectionWhenNameIsGone()
    {
        var hint = new DockMonitorHint(@"\\.\DISPLAY9", new PixelRect(1900, 0, 4400, 1400));
        var d3 = M(@"\\.\DISPLAY3", 1920, 0, 2560, 1440);
        Assert.Equal(d3, MonitorResolver.Resolve([D1, d3], hint));
    }

    [Fact]
    public void PrimaryWhenNothingMatchesOrNoHint()
    {
        var hint = new DockMonitorHint(@"\\.\DISPLAY9", new PixelRect(9000, 9000, 9100, 9100));
        Assert.Equal(D1, MonitorResolver.Resolve([D2, D1], hint)); // CA-05.4: disconnected monitor -> primary
        Assert.Equal(D1, MonitorResolver.Resolve([D2, D1], null));
    }

    [Fact]
    public void FirstWhenNoPrimaryFlagAndNullWhenEmpty()
    {
        var a = M("A", 0, 0, 100, 100);
        Assert.Equal(a, MonitorResolver.Resolve([a, D2], null));
        Assert.Null(MonitorResolver.Resolve([], null));
    }

    [Fact]
    public void OrderIsByLeftThenTop()
    {
        var a = M("A", 0, 500, 100, 100);
        var b = M("B", 0, 0, 100, 100);
        var c = M("C", -100, 0, 100, 100);
        Assert.Equal(["C", "B", "A"], MonitorResolver.Order([a, b, c]).Select(m => m.DeviceName));
    }
}
