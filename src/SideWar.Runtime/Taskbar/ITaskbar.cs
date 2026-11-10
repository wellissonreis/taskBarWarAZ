using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Taskbar;

/// <summary>Taskbar window found by RF-21.2 plus the context for the classifier.</summary>
public readonly record struct TaskbarTarget(nint Hwnd, TaskbarContext Context);

/// <summary>RF-21.2: cheap synchronous lookup, T-Game only. Null means the monitor has no taskbar.</summary>
public interface ITaskbarLocator
{
    TaskbarTarget? Locate(MonitorInfo monitor);
}

/// <summary>RF-21.3: UIA read off the T-Game. Faulted or canceled tasks count as one failure.</summary>
public interface ITaskbarProbe
{
    Task<UiaNode[]> QueryAsync(nint taskbarHwnd);
}
