using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Taskbar;

/// <summary>Read-only snapshot, without personal names. Ancestry is resolved by the probe.</summary>
public readonly record struct UiaNode(string ClassName, string ParentClassName,
    string AutomationId, PixelRect Rect, bool HasNotificationAreaAncestor = false);

public readonly record struct TaskbarContext(MonitorInfo Monitor, PixelRect TaskbarRectPx,
    TaskbarEdge Edge, bool AutoHide, bool UseUia = true);
