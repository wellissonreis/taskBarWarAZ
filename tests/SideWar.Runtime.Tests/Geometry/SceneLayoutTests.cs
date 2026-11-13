using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Xunit;

namespace SideWar.Runtime.Tests.Geometry;

public sealed class SceneLayoutTests
{
    // Work area bottom = bar top. above = round(104 * dpi / 96).
    [Theory]
    [InlineData(1920, 1080, 96, 104)]
    [InlineData(1920, 1080, 120, 130)]
    [InlineData(1920, 1080, 144, 156)]
    [InlineData(3840, 2160, 192, 208)]
    [InlineData(1920, 1080, 137, 148)] // 148.416...
    [InlineData(3840, 2160, 288, 312)]
    public void Band_IsOnlyTheScene(int width, int height, uint dpi, int abovePx)
    {
        var monitor = Monitor(new(0, 0, width, height - 48), dpi);
        var actual = SceneLayout.Compute(monitor, Bar(TaskbarEdge.Bottom), TowerHost.Band);
        Assert.Equal(new PixelRect(0, height - 48 - abovePx, width, height - 48), actual);
        AssertInside(monitor.WorkArea, actual);
    }

    [Theory]
    [InlineData(96, 104, 48)]
    [InlineData(120, 130, 60)]
    [InlineData(144, 156, 72)]
    [InlineData(192, 208, 96)]
    public void Taskbar_CoversTheBar(uint dpi, int abovePx, int barPx)
    {
        var monitor = Monitor(new(0, 0, 1920, 1000), dpi);
        var actual = SceneLayout.Compute(monitor, Bar(TaskbarEdge.Bottom), TowerHost.Taskbar);
        Assert.Equal(new PixelRect(0, 1000 - abovePx, 1920, 1000 + barPx), actual);
        Assert.Equal(abovePx + barPx, actual.Height);
    }

    [Fact]
    public void Taskbar_IgnoresAutoHideMargin()
    {
        var monitor = Monitor(new(0, 0, 1920, 1032), 96);
        var actual = SceneLayout.Compute(monitor, Bar(TaskbarEdge.Bottom, autoHide: true), TowerHost.Taskbar);
        Assert.Equal(new PixelRect(0, 928, 1920, 1080), actual);
    }

    [Theory]
    [InlineData(TaskbarEdge.Bottom, false, 0, 0, 1920, 1080, 976, 1080)]
    [InlineData(TaskbarEdge.Top, false, 0, 48, 1920, 1080, 976, 1080)]
    [InlineData(TaskbarEdge.Bottom, true, 0, 0, 1920, 1080, 974, 1078)]
    [InlineData(TaskbarEdge.Right, true, -1920, 0, 0, 1080, 974, 1078)]
    [InlineData(TaskbarEdge.Bottom, false, -1920, -1080, 0, -48, -152, -48)]
    public void Band_EdgesAutoHideAndNegativeOrigins(TaskbarEdge edge, bool autoHide, int left, int top, int right, int bottom,
        int expectedTop, int expectedBottom)
    {
        var monitor = Monitor(new(left, top, right, bottom), 96);
        var taskbar = Bar(edge, autoHide);
        var actual = SceneLayout.Compute(monitor, taskbar, TowerHost.Band);
        Assert.Equal(new PixelRect(left, expectedTop, right, expectedBottom), actual);
        Assert.Equal(actual, SceneLayout.Compute(monitor, taskbar, TowerHost.Band));
    }

    [Theory]
    [InlineData(104, false, 0)]
    [InlineData(106, true, 0)]
    public void Band_ExactFit(int height, bool autoHide, int expectedTop)
    {
        var monitor = Monitor(new(0, 0, 100, height), 96);
        var r = SceneLayout.Compute(monitor, Bar(TaskbarEdge.Bottom, autoHide), TowerHost.Band);
        Assert.Equal(new PixelRect(0, expectedTop, 100, height - (autoHide ? 2 : 0)), r);
    }

    [Theory]
    [InlineData(0, 100, 1000, false)]
    [InlineData(96, 0, 1000, false)]
    [InlineData(96, 100, 103, false)]
    [InlineData(96, 100, -1, false)]
    [InlineData(uint.MaxValue, 100, 1000, false)]
    public void ImpossibleGeometryIsRejected(uint dpi, int width, int height, bool autoHide)
    {
        var monitor = Monitor(new(0, 0, width, height), dpi);
        foreach (var host in new[] { TowerHost.Band, TowerHost.Taskbar })
            Assert.Throws<ArgumentOutOfRangeException>(() => SceneLayout.Compute(monitor, Bar(TaskbarEdge.Bottom, autoHide), host));
    }

    [Fact]
    public void Band_AutoHideMarginMustFit() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SceneLayout.Compute(Monitor(new(0, 0, 100, 105), 96), Bar(TaskbarEdge.Bottom, true), TowerHost.Band));

    private static MonitorInfo Monitor(PixelRect work, uint dpi) =>
        new("DISPLAY2", work, work, dpi, false, 0);

    private static TaskbarLayout Bar(TaskbarEdge edge, bool autoHide = false) =>
        new("DISPLAY2", edge, autoHide, default, 48, Array.Empty<AppButton>(), null,
            default, [], 0, TaskbarAlignment.Center, TaskbarSource.Uia, 96);

    private static void AssertInside(PixelRect work, PixelRect actual)
    {
        Assert.True(actual.Left >= work.Left && actual.Top >= work.Top);
        Assert.True(actual.Right <= work.Right && actual.Bottom <= work.Bottom);
    }
}
