using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Fullscreen;

/// <summary>Window state captured by Platform (SDD-06 RF-06.2). Coordinates are physical pixels.</summary>
public readonly record struct WindowSnapshot(
    nint Hwnd, uint ProcessId, string ClassName,
    uint Style, uint ExStyle, bool IsZoomed, bool IsIconic, bool IsVisible,
    bool IsCloaked, PixelRect Frame,
    string MonitorDevice, PixelRect MonitorBounds, PixelRect MonitorWork);
