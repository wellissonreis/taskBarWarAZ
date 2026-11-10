using SideWar.Runtime.Taskbar;

namespace SideWar.Runtime.Geometry;

/// <summary>Where the widget goes and how it was chosen.</summary>
public readonly record struct WidgetPlacement(PixelRect Bounds, bool IsFallback);

/// <summary>Pure widget geometry (SDD-23 RF-23.2).</summary>
public static class WidgetGeometry
{
    public const float WidthDip = 420f;
    public const float MinHeightDip = 46f;
    public const float FallbackHeightDip = 48f;

    /// <summary>
    /// Normal case: bottom taskbar whose left free zone is at least 420 DIP wide; the widget sits on the bar with
    /// its base on the bar base. Otherwise (other edge, narrower zone, or <paramref name="taskbar"/> null):
    /// bottom-left corner of <paramref name="scene"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">DPI is zero or the DPI-scaled size does not fit.</exception>
    public static WidgetPlacement Compute(MonitorInfo monitor, PixelRect scene, TaskbarLayout? taskbar)
    {
        if (monitor.Dpi == 0 || monitor.Dpi > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(monitor), "DPI must be positive.");
        var scale = monitor.Dpi / 96.0;
        if (taskbar is { Edge: TaskbarEdge.Bottom } t && IsUsable(t) && t.LeftFreeZone.X1 - t.LeftFreeZone.X0 >= WidthDip)
        {
            var heightDip = Math.Max(t.HeightDip, MinHeightDip);
            var width = Px(WidthDip, scale);
            var height = Px(heightDip, scale);
            var left = checked(monitor.Bounds.Left + Px(t.LeftFreeZone.X0, scale));
            var bottom = checked(monitor.Bounds.Top + Px(t.TaskbarRect.Y + t.TaskbarRect.H, scale));
            return new(new PixelRect(left, checked(bottom - height), checked(left + width), bottom), false);
        }
        var fw = Px(WidthDip, scale);
        var fh = Px(FallbackHeightDip, scale);
        return new(new PixelRect(scene.Left, checked(scene.Bottom - fh), checked(scene.Left + fw), scene.Bottom), true);
    }

    private static bool IsUsable(TaskbarLayout t) =>
        float.IsFinite(t.HeightDip) && float.IsFinite(t.LeftFreeZone.X0) && float.IsFinite(t.LeftFreeZone.X1) &&
        float.IsFinite(t.TaskbarRect.Y) && float.IsFinite(t.TaskbarRect.H) && t.HeightDip > 0;

    private static int Px(double dip, double scale) => checked((int)Math.Round(dip * scale, MidpointRounding.AwayFromZero));
}
