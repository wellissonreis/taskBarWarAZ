using SideWar.Runtime.Taskbar;

namespace SideWar.Runtime.Geometry;

public static class SceneLayout
{
    /// <summary>DIP of the scene above the bar: route/core/portal 0..102 plus the 2-DIP status gap (SDD-22 RF-22.1, ADR-034).</summary>
    public const int SceneAboveBarDip = 104;

    /// <summary>
    /// Places the scene window (SDD-22 RF-22.1). <see cref="TowerHost.Taskbar"/>: 104 DIP above the bar plus the bar itself
    /// (the window covers the taskbar; replaces RNF-05.3 for this window only). <see cref="TowerHost.Band"/>: only the scene,
    /// raised 2 px when the bar auto-hides.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">DPI is zero/too large, or the work area cannot fit the scene (and auto-hide margin).</exception>
    public static PixelRect Compute(MonitorInfo m, TaskbarLayout t, TowerHost host)
    {
        if (m.Dpi == 0 || m.Dpi > int.MaxValue / 2)
            throw new ArgumentOutOfRangeException(nameof(m), "DPI must be positive and fit physical pixel coordinates.");

        var scale = m.Dpi / 96.0;
        var abovePx = (int)Math.Round(SceneAboveBarDip * scale, MidpointRounding.AwayFromZero);
        var onBar = host == TowerHost.Taskbar;
        var marginPx = !onBar && t.AutoHide ? 2 : 0;
        var work = m.WorkArea;
        if ((long)work.Right - work.Left <= 0 ||
            (long)work.Bottom - work.Top < (long)abovePx + marginPx)
            throw new ArgumentOutOfRangeException(nameof(m), "WorkArea must fit the 104-DIP scene and physical auto-hide margin.");

        var bottom = checked(work.Bottom - marginPx);
        var top = checked(bottom - abovePx);
        var barPx = onBar ? (int)Math.Round(Math.Max(0f, t.HeightDip) * scale, MidpointRounding.AwayFromZero) : 0;
        return new PixelRect(work.Left, top, work.Right, checked(bottom + barPx));
    }
}
