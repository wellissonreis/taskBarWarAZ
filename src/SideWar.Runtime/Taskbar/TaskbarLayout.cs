using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Taskbar;

public readonly record struct TaskbarLayout(
    string MonitorDevice,
    TaskbarEdge Edge,
    bool AutoHide,
    DipRect TaskbarRect,
    float HeightDip,
    IReadOnlyList<AppButton> AppButtons,
    DipRect? WidgetsButton,
    (float X0, float X1) LeftFreeZone,
    float TrayLeftX,
    TaskbarAlignment Alignment,
    TaskbarSource Source,
    uint Dpi);
