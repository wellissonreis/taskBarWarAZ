using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Xunit;

namespace SideWar.Runtime.Tests.Geometry;

public sealed class WidgetGeometryTests
{
    private static MonitorInfo Monitor(uint dpi, int left = 0, int top = 0, int w = 1920, int h = 1080) =>
        new("M", new PixelRect(left, top, left + w, top + h), new PixelRect(left, top, left + w, top + h), dpi, true, 0);

    private static TaskbarLayout Bar(TaskbarEdge edge, float x0, float x1, float height = 48, float barY = 1032, float barH = 48) =>
        new("M", edge, false, new DipRect(0, barY, 1920, barH), height, [], null, (x0, x1), [], 1900, TaskbarAlignment.Left,
            TaskbarSource.Uia, 96);

    private static PixelRect Scene(MonitorInfo m) => SceneLayout.Compute(m, Bar(TaskbarEdge.Bottom, 0, 0));

    // CA-23.3: 100/125/150/175/200 %, bottom bar 48 DIP, free zone >= 420 DIP.
    [Theory]
    [InlineData(96u, 420, 48)]
    [InlineData(120u, 525, 60)]
    [InlineData(144u, 630, 72)]
    [InlineData(168u, 735, 84)]
    [InlineData(192u, 840, 96)]
    public void NormalCase_SizeAndBaseOnBarBase(uint dpi, int widthPx, int heightPx)
    {
        var m = Monitor(dpi);
        var scale = dpi / 96.0;
        var bar = Bar(TaskbarEdge.Bottom, 10, 600, barY: 1032, barH: 48);
        var p = WidgetGeometry.Compute(m, Scene(m), bar);
        Assert.False(p.IsFallback);
        Assert.Equal(widthPx, p.Bounds.Width);
        Assert.Equal(heightPx, p.Bounds.Height);
        Assert.Equal((int)Math.Round(10 * scale, MidpointRounding.AwayFromZero), p.Bounds.Left);
        Assert.Equal((int)Math.Round(1080 * scale, MidpointRounding.AwayFromZero), p.Bounds.Bottom);
    }

    [Theory]
    [InlineData(300f, true)]
    [InlineData(419.9f, true)]
    [InlineData(420f, false)]
    [InlineData(600f, false)]
    public void FreeZoneThreshold(float zone, bool fallback)
    {
        var m = Monitor(96);
        var p = WidgetGeometry.Compute(m, Scene(m), Bar(TaskbarEdge.Bottom, 0, zone));
        Assert.Equal(fallback, p.IsFallback);
    }

    [Theory]
    [InlineData(40f, 46)]
    [InlineData(46f, 46)]
    [InlineData(48f, 48)]
    [InlineData(52f, 52)]
    public void HeightIsMaxOfBarAnd46(float bar, int expected)
    {
        var m = Monitor(96);
        var p = WidgetGeometry.Compute(m, Scene(m), Bar(TaskbarEdge.Bottom, 0, 600, bar, 1080 - bar, bar));
        Assert.False(p.IsFallback);
        Assert.Equal(expected, p.Bounds.Height);
        Assert.Equal(1080, p.Bounds.Bottom);
    }

    [Theory]
    [InlineData(TaskbarEdge.Top)]
    [InlineData(TaskbarEdge.Left)]
    [InlineData(TaskbarEdge.Right)]
    [InlineData(TaskbarEdge.Unknown)]
    public void NonBottomEdgeFallsBackToSceneBottomLeft(TaskbarEdge edge)
    {
        var m = Monitor(96);
        var scene = Scene(m);
        var p = WidgetGeometry.Compute(m, scene, Bar(edge, 0, 800));
        Assert.True(p.IsFallback);
        Assert.Equal(new PixelRect(scene.Left, scene.Bottom - 48, scene.Left + 420, scene.Bottom), p.Bounds);
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(120u)]
    [InlineData(144u)]
    [InlineData(168u)]
    [InlineData(192u)]
    public void FallbackScalesAndStaysInsideScene(uint dpi)
    {
        var m = Monitor(dpi);
        var scene = Scene(m);
        var p = WidgetGeometry.Compute(m, scene, null);
        Assert.True(p.IsFallback);
        Assert.Equal((int)(420 * dpi / 96), p.Bounds.Width);
        Assert.Equal((int)(48 * dpi / 96), p.Bounds.Height);
        Assert.Equal(scene.Left, p.Bounds.Left);
        Assert.Equal(scene.Bottom, p.Bounds.Bottom);
        Assert.True(p.Bounds.Top >= scene.Top);
    }

    [Fact]
    public void SecondMonitorOffsetIsApplied()
    {
        var m = Monitor(96, left: 1920, top: -200);
        var bar = Bar(TaskbarEdge.Bottom, 100, 700, barY: 1032, barH: 48);
        var p = WidgetGeometry.Compute(m, Scene(m), bar);
        Assert.Equal(new PixelRect(2020, 832, 2440, 880), p.Bounds);
    }

    [Fact]
    public void NonFiniteGeometryFallsBack()
    {
        var m = Monitor(96);
        var p = WidgetGeometry.Compute(m, Scene(m), Bar(TaskbarEdge.Bottom, float.NaN, 800));
        Assert.True(p.IsFallback);
    }

    [Fact]
    public void ZeroDpiThrows()
    {
        var m = Monitor(96) with { Dpi = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => WidgetGeometry.Compute(m, default, null));
    }
}
