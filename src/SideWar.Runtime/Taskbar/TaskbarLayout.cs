using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Taskbar;

public enum BlockReason { WindowsWidget, StartSearchApps, TrayClock, Edge }

/// <summary>Blocked stretch of the bar, DIP, same origin as DipRect (SDD-21 RF-21.1).</summary>
public readonly record struct BlockedSpan(float X0, float X1, BlockReason Reason);

public readonly record struct TaskbarLayout(
    string MonitorDevice,
    TaskbarEdge Edge,
    bool AutoHide,
    DipRect TaskbarRect,
    float HeightDip,
    IReadOnlyList<AppButton> AppButtons,
    DipRect? WidgetsButton,
    (float X0, float X1) LeftFreeZone,
    IReadOnlyList<BlockedSpan> Blocked,
    float TrayLeftX,
    TaskbarAlignment Alignment,
    TaskbarSource Source,
    uint Dpi);
