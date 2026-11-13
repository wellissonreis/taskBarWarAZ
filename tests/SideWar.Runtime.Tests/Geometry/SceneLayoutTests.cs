using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Xunit;

namespace SideWar.Runtime.Tests.Geometry;

public sealed class SceneLayoutTests
{
    [Theory]
    [InlineData(1366, 768, 96, 624)]
    [InlineData(1920, 1080, 96, 936)]
    [InlineData(1920, 1080, 120, 912)]
    [InlineData(1920, 1080, 144, 888)]
    [InlineData(2560, 1440, 144, 1248)]
    [InlineData(3840, 2160, 144, 1968)]
    [InlineData(3840, 2160, 192, 1920)]
    [InlineData(3440, 1440, 96, 1296)]
    [InlineData(1280, 800, 144, 608)]
    [InlineData(1920, 1080, 168, 864)]
    [InlineData(3840, 2160, 288, 1824)]
    [InlineData(1920, 1080, 137, 895)]
    public void Compute_MonitorTable(int width, int height, uint dpi, int expectedTop)
    {
        var monitor = Monitor(new(0, 0, width, height - 48), dpi);
        var actual = SceneLayout.Compute(monitor, Bar(TaskbarEdge.Bottom));
        Assert.Equal(new PixelRect(0, expectedTop, width, height - 48), actual);
        Assert.Equal(96, actual.Height / (dpi / 96.0), 8);
        AssertInside(monitor.WorkArea, actual);
    }

    [Theory]
    [InlineData(TaskbarEdge.Bottom, false, TaskbarSource.Uia, 0, 0, 1920, 1032, 888, 1032)]
    [InlineData(TaskbarEdge.Top, false, TaskbarSource.Fallback, 0, 48, 1920, 1080, 936, 1080)]
    [InlineData(TaskbarEdge.Left, false, TaskbarSource.Fallback, 48, 0, 1920, 1080, 936, 1080)]
    [InlineData(TaskbarEdge.Right, false, TaskbarSource.Fallback, 0, 0, 1872, 1080, 936, 1080)]
    [InlineData(TaskbarEdge.Unknown, false, TaskbarSource.Fallback, 0, 0, 1920, 1080, 936, 1080)]
    [InlineData(TaskbarEdge.Bottom, true, TaskbarSource.Fallback, 0, 0, 1920, 1080, 934, 1078)]
    [InlineData(TaskbarEdge.Top, true, TaskbarSource.Fallback, 0, 0, 1920, 1080, 934, 1078)]
    [InlineData(TaskbarEdge.Left, true, TaskbarSource.Fallback, -1920, -1080, 0, 0, -146, -2)]
    [InlineData(TaskbarEdge.Right, true, TaskbarSource.Fallback, -1920, 0, 0, 1080, 934, 1078)]
    [InlineData(TaskbarEdge.Bottom, false, TaskbarSource.Uia, -1920, -1080, 0, -48, -192, -48)]
    public void Compute_EdgesAndNegativeOrigins(TaskbarEdge edge, bool autoHide, TaskbarSource source,
        int left, int top, int right, int bottom, int expectedTop, int expectedBottom)
    {
        var monitor = Monitor(new(left, top, right, bottom), 144);
        var taskbar = Bar(edge, autoHide) with { Source = source, Dpi = 96 };
        var actual = SceneLayout.Compute(monitor, taskbar);
        Assert.Equal(new PixelRect(left, expectedTop, right, expectedBottom), actual);
        AssertInside(monitor.WorkArea, actual);
        Assert.Equal(actual, SceneLayout.Compute(monitor, taskbar));
    }

    [Theory]
    [InlineData(96, false, 96, 0)]
    [InlineData(96, true, 98, 0)]
    [InlineData(120, true, 122, 0)]
    [InlineData(288, true, 290, 0)]
    public void Compute_ExactFitPreservesHeightAndPhysicalMargin(uint dpi, bool autoHide, int bottom, int expectedTop)
    {
        var monitor = Monitor(new(0, 0, 100, bottom), dpi);
        var result = SceneLayout.Compute(monitor, Bar(TaskbarEdge.Bottom, autoHide));
        Assert.Equal(new PixelRect(0, expectedTop, 100, (int)dpi), result);
    }

    [Theory]
    [InlineData(0, 100, 1000, false)]
    [InlineData(96, 0, 1000, false)]
    [InlineData(96, -1, 1000, false)]
    [InlineData(96, 100, 95, false)]
    [InlineData(96, 100, 97, true)]
    [InlineData(96, 100, -1, false)]
    [InlineData(uint.MaxValue, 100, 1000, false)]
    public void Compute_ImpossibleGeometryIsRejected(uint dpi, int width, int height, bool autoHide)
    {
        var monitor = Monitor(new(0, 0, width, height), dpi);
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneLayout.Compute(monitor, Bar(TaskbarEdge.Bottom, autoHide)));
    }

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
