using SideWar.Runtime.Taskbar;

namespace SideWar.Runtime.Geometry;

public static class SceneLayout
{
    /// <summary>Places the 96-DIP scene inside the work area (SDD-22 RF-22.1).</summary>
    /// <exception cref="ArgumentOutOfRangeException">DPI is zero or the work area cannot fit the scene and auto-hide margin.</exception>
    public static PixelRect Compute(MonitorInfo m, TaskbarLayout t)
    {
        if (m.Dpi == 0 || m.Dpi > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(m), "DPI must be positive and fit physical pixel coordinates.");

        // round(96 * (dpi / 96.0)) == dpi: preserve the exact integral height.
        var heightPx = (int)m.Dpi;
        var marginPx = t.AutoHide ? 2 : 0;
        var work = m.WorkArea;
        if ((long)work.Right - work.Left <= 0 ||
            (long)work.Bottom - work.Top < (long)heightPx + marginPx)
            throw new ArgumentOutOfRangeException(nameof(m), "WorkArea must fit the 96-DIP scene and physical auto-hide margin.");

        var bottom = checked(work.Bottom - marginPx);
        return new PixelRect(work.Left, checked(bottom - heightPx), work.Right, bottom);
    }
}
