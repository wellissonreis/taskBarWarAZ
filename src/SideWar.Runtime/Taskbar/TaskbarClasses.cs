namespace SideWar.Runtime.Taskbar;

/// <summary>SDD-21 RF-21.4 hypotheses; still require confirmation with real Windows fixtures.</summary>
public static class TaskbarClasses
{
    // XAML system button AutomationIds from SDD-21 C1.
    public static IReadOnlySet<string> SystemButtonIds { get; } =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(
            new[] { "StartButton", "SearchButton", "TaskViewButton", "WidgetsButton", "CopilotButton" },
            StringComparer.Ordinal);
    // XAML notification classes, SDD-21 C2; ancestry is supplied separately by the probe.
    public const string TrayClassPrefix = "SystemTray.";
    // XAML app class fragment and legacy Win32 app parent, SDD-21 C3.
    public const string AppClassFragment = "TaskListButton";
    public const string AppParentClass = "MSTaskListWClass";

    // SDD-21 RF-21.5 blocking margins transcribed from the canvas (DP-33.3); confirm with real fixtures.
    public const float IconMarginDip = 4;
    public const float TrayMarginDip = 16;
}
