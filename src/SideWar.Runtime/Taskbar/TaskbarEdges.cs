using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Taskbar;

public static class TaskbarEdges
{
    /// <summary>RF-21.2 item 3 for secondary bars: compare the bar rectangle with the monitor bounds.
    /// Any inconsistency (or an ambiguous match) is Unknown.</summary>
    public static TaskbarEdge Infer(PixelRect bar, PixelRect monitor)
    {
        if (bar.Width <= 0 || bar.Height <= 0 || monitor.Width <= 0 || monitor.Height <= 0) return TaskbarEdge.Unknown;
        var fullWidth = bar.Left == monitor.Left && bar.Right == monitor.Right;
        var fullHeight = bar.Top == monitor.Top && bar.Bottom == monitor.Bottom;
        if (fullWidth && fullHeight) return TaskbarEdge.Unknown;
        if (fullWidth && bar.Bottom == monitor.Bottom) return TaskbarEdge.Bottom;
        if (fullWidth && bar.Top == monitor.Top) return TaskbarEdge.Top;
        if (fullHeight && bar.Left == monitor.Left) return TaskbarEdge.Left;
        if (fullHeight && bar.Right == monitor.Right) return TaskbarEdge.Right;
        return TaskbarEdge.Unknown;
    }
}
