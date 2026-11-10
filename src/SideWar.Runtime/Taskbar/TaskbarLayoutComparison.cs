using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Taskbar;

public static class TaskbarLayoutComparison
{
    /// <summary>Content comparison for layout notifications; record equality compares list identity.</summary>
    public static bool Equivalent(TaskbarLayout a, TaskbarLayout b) =>
        a.MonitorDevice == b.MonitorDevice && a.Edge == b.Edge && a.AutoHide == b.AutoHide &&
        a.Source == b.Source && a.Dpi == b.Dpi && a.Alignment == b.Alignment &&
        Near(a.TaskbarRect, b.TaskbarRect) && Near(a.HeightDip, b.HeightDip) &&
        Near(a.TrayLeftX, b.TrayLeftX) && Near(a.LeftFreeZone.X0, b.LeftFreeZone.X0) &&
        Near(a.LeftFreeZone.X1, b.LeftFreeZone.X1) && NearNullable(a.WidgetsButton, b.WidgetsButton) &&
        a.AppButtons.Count == b.AppButtons.Count &&
        a.AppButtons.Zip(b.AppButtons).All(pair => Near(pair.First.Rect, pair.Second.Rect));

    private static bool Near(float a, float b) => Math.Abs(a - b) <= 0.5f;
    private static bool Near(DipRect a, DipRect b) =>
        Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.W, b.W) && Near(a.H, b.H);
    private static bool NearNullable(DipRect? a, DipRect? b) =>
        a is { } x && b is { } y ? Near(x, y) : a is null && b is null;
}
