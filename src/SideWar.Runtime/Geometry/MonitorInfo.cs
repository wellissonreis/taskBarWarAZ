namespace SideWar.Runtime.Geometry;

public readonly record struct MonitorInfo(
    string DeviceName,
    PixelRect Bounds,
    PixelRect WorkArea,
    uint Dpi,
    bool IsPrimary,
    nint Handle);
