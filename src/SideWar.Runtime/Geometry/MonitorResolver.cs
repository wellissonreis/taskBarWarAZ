namespace SideWar.Runtime.Geometry;

/// <summary>Last chosen dock monitor: device name and bounds (SDD-05 RF-05.3, <c>settings.dock.monitor</c>).</summary>
public readonly record struct DockMonitorHint(string DeviceName, PixelRect Bounds);

/// <summary>Pure monitor choice (SDD-05 RF-05.3, T-05.2).</summary>
public static class MonitorResolver
{
    /// <summary>
    /// Priority: same name and same bounds; same name; largest bounds intersection with the hint; primary.
    /// Returns null only when <paramref name="monitors"/> is empty. Without a hint the primary (or first) wins.
    /// </summary>
    public static MonitorInfo? Resolve(IReadOnlyList<MonitorInfo> monitors, DockMonitorHint? hint)
    {
        if (monitors.Count == 0) return null;
        if (hint is { } h)
        {
            foreach (var m in monitors)
                if (m.DeviceName == h.DeviceName && m.Bounds == h.Bounds) return m;
            foreach (var m in monitors)
                if (m.DeviceName == h.DeviceName) return m;
            MonitorInfo? best = null;
            long bestArea = 0;
            foreach (var m in monitors)
            {
                var area = IntersectionArea(m.Bounds, h.Bounds);
                if (area > bestArea) { best = m; bestArea = area; }
            }
            if (best is not null) return best;
        }
        foreach (var m in monitors)
            if (m.IsPrimary) return m;
        return monitors[0];
    }

    /// <summary>Menu numbering order: by Bounds.Left, then Bounds.Top (RF-05.3). Stable; input is not modified.</summary>
    public static IReadOnlyList<MonitorInfo> Order(IReadOnlyList<MonitorInfo> monitors) =>
        monitors.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToArray();

    private static long IntersectionArea(PixelRect a, PixelRect b)
    {
        long w = (long)Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        long h = (long)Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return w > 0 && h > 0 ? w * h : 0;
    }
}
