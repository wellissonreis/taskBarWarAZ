using SideWar.Core.Simulation;
using CoreFreeZone = SideWar.Core.Simulation.FreeZone;
using CoreTowerHost = SideWar.Core.Simulation.TowerHost;

namespace SideWar.Runtime.Taskbar;

/// <summary>BuildZones (Runtime) -> StripLayout (Core). Pure (SDD-22 RF-22.2, SDD-11 RF-11.3, ADR-034).</summary>
public static class StripLayoutFactory
{
    /// <summary>Core sprite left edge, DIP (canvas: x = 4).</summary>
    public const int CoreXDip = 4;
    /// <summary>Right edge of the core (4 + 84): enemies leak here.</summary>
    public const float LeakXDip = 88f;
    /// <summary>Portal sits over the clock: <c>portal.Left = W - 80</c>.</summary>
    public const int PortalRightInsetDip = 80;
    /// <summary>Spawn at the portal centre: <c>portal.Left + 34</c>.</summary>
    public const int SpawnRightInsetDip = 46;
    /// <summary>Smallest strip for which SpawnX &gt; LeakX.</summary>
    public const int MinWidthDip = 135;

    /// <summary>Zones are clipped to [0, W], sorted, de-overlapped and capped at <see cref="StripLayout.MaxFreeZones"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="widthDip"/> is below <see cref="MinWidthDip"/>.</exception>
    public static StripLayout Create(BuildZones zones, int widthDip)
    {
        if (widthDip < MinWidthDip) throw new ArgumentOutOfRangeException(nameof(widthDip), "Strip too narrow for core and portal.");
        var list = new List<CoreFreeZone>();
        var cursor = 0;
        foreach (var z in zones.Free.OrderBy(z => z.X0))
        {
            var x0 = Math.Max(Math.Max(z.X0, 0), cursor);
            var x1 = Math.Min(z.X1, widthDip);
            if (x1 <= x0) continue;
            list.Add(new CoreFreeZone(x0, x1));
            cursor = x1;
            if (list.Count == StripLayout.MaxFreeZones) break;
        }
        var host = zones.Host == TowerHost.Taskbar ? CoreTowerHost.Taskbar : CoreTowerHost.Band;
        return StripLayout.Create(widthDip, widthDip - SpawnRightInsetDip, LeakXDip, CoreXDip,
            widthDip - PortalRightInsetDip, host, list.ToArray());
    }

    /// <summary>Strip width in DIP for a physical work-area width: <c>round(px * 96 / dpi)</c>.</summary>
    public static int WidthDip(int widthPx, uint dpi)
    {
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        return (int)Math.Round(widthPx * 96.0 / dpi, MidpointRounding.AwayFromZero);
    }
}
