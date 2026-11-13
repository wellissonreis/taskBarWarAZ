namespace SideWar.Runtime.Taskbar;

/// <summary>Where towers live: inside the taskbar, or the fallback tower band at the top of the scene (DP-33.5/33.6).</summary>
public enum TowerHost { Taskbar, Band }

/// <summary>Free X interval, integer DIP, [X0, X1), relative to the strip's left edge.</summary>
public readonly record struct FreeZone(int X0, int X1);

public readonly record struct BuildZones(TowerHost Host, IReadOnlyList<FreeZone> Free, int HostTopDip, int HostHeightDip);

/// <summary>settings.taskbar.useUia / towersInBar; both default to true (also for new()).</summary>
public readonly record struct BuildZoneOptions
{
    public bool UseUia { get; init; } = true;
    public bool TowersInBar { get; init; } = true;

    public BuildZoneOptions() { }
}

/// <summary>SDD-21 RF-21.7. Pure; the Core conversion (StripLayout) belongs to TA.5.</summary>
public static class BuildZoneMap
{
    public const int CollisionWidthDip = 44;
    public const int MaxZones = 8;
    public const int TaskbarHostTopDip = 104;
    public const int BandHostTopDip = 2;
    public const int BandHostHeightDip = 44;

    public static BuildZones From(TaskbarLayout layout, int stripWidthDip, BuildZoneOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stripWidthDip);
        if (!(options.UseUia && options.TowersInBar && layout.Source == TaskbarSource.Uia &&
              layout.Edge == TaskbarEdge.Bottom && !layout.AutoHide && layout.AppButtons.Count > 0 &&
              layout.Blocked.Count > 0))
            return Band(stripWidthDip);

        var free = new List<FreeZone>();
        var cursor = (double)layout.TaskbarRect.X;
        var end = (double)layout.TaskbarRect.X + layout.TaskbarRect.W;
        foreach (var span in layout.Blocked.OrderBy(s => s.X0))
        {
            Add(free, cursor, span.X0);
            cursor = Math.Max(cursor, span.X1);
        }
        Add(free, cursor, end);
        if (free.Count > MaxZones)
        {
            System.Diagnostics.Debug.WriteLine($"BuildZoneMap: {free.Count - MaxZones} free zone(s) dropped (max {MaxZones}).");
            free.RemoveRange(MaxZones, free.Count - MaxZones);
        }
        return new(TowerHost.Taskbar, Array.AsReadOnly(free.ToArray()), TaskbarHostTopDip,
            (int)Math.Round(layout.HeightDip, MidpointRounding.AwayFromZero));
    }

    private static BuildZones Band(int w)
    {
        var free = new List<FreeZone>();
        Add(free, 96, w / 2 - 240);
        Add(free, w / 2 + 240, w - 560);
        return new(TowerHost.Band, Array.AsReadOnly(free.ToArray()), BandHostTopDip, BandHostHeightDip);
    }

    private static void Add(List<FreeZone> zones, double x0, double x1)
    {
        var a = (int)Math.Ceiling(x0);
        var b = (int)Math.Floor(x1);
        if (b - a >= CollisionWidthDip) zones.Add(new(a, b));
    }
}
