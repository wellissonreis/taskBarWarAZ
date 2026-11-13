using System.Diagnostics;
using SideWar.Platform.Windows.Rendering;
using SideWar.Platform.Windows.Threading;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Windows.Win32;

namespace SideWar.Platform.Windows.Windows;

/// <summary>
/// Places the scene strip and the widget per monitor and reacts to DPI, display and work-area changes
/// (SDD-05 RF-05.3/05.6/05.7). Geometry comes only from <see cref="SceneLayout.Compute"/> and
/// <see cref="WidgetGeometry.Compute"/>. All members require the owning T-Game.
/// </summary>
public sealed class DockController
{
    /// <summary>Debounce for system-change bursts (RF-05.6).</summary>
    public const double DebounceSeconds = 0.2;

    private const uint SpiSetWorkArea = 0x2F;
    private readonly GameThread owner;
    private long? deadline;
    private bool dirty;
    private TaskbarLayout? taskbar;
    private DockMonitorHint? hint;

    internal DockController(GameThread owner) => this.owner = owner;

    /// <summary>Monitor source; replaceable by tests.</summary>
    internal Func<IReadOnlyList<MonitorInfo>> Enumerate { get; set; } = MonitorService.Enumerate;

    /// <summary>The strip's own surface, resized with the scene (set by whoever created it; may be null).</summary>
    public LayeredSurface? SceneSurface { get; set; }

    /// <summary>Raised after a relayout changed anything, so the host can redraw and drop scaled caches (RF-05.6).</summary>
    public event Action<DockLayoutResult>? LayoutApplied;

    public DockLayoutResult? Current { get; private set; }
    public DockMonitorHint? MonitorHint => hint;
    public int RelayoutCount { get; private set; }
    internal long? NextDeadline => deadline;

    /// <summary>Latest taskbar layout (from the taskbar service, by the App). Null = unknown. Schedules a relayout.</summary>
    public void SetTaskbar(TaskbarLayout? layout)
    {
        owner.VerifyAccess();
        taskbar = layout;
        Invalidate();
    }

    /// <summary>"Move to monitor N": remembers the monitor and relays out immediately (RF-05.3).</summary>
    public void MoveToMonitor(MonitorInfo monitor)
    {
        owner.VerifyAccess();
        hint = new DockMonitorHint(monitor.DeviceName, monitor.Bounds);
        Relayout();
    }

    /// <summary>Restores the persisted choice (settings.dock.monitor) without relaying out.</summary>
    public void SetMonitorHint(DockMonitorHint? value)
    {
        owner.VerifyAccess();
        hint = value;
        Invalidate();
    }

    /// <summary>Debounced request (ignored until the first <see cref="Relayout"/>); while the scene is hidden only marks <c>layoutDirty</c> (RF-05.6).</summary>
    public void Invalidate()
    {
        owner.VerifyAccess();
        // Inactive until the App's first explicit Relayout: manual SetBounds/Apply keep working untouched.
        if (Current is null) return;
        dirty = true;
        if (!owner.Scene.IsVisible) return;
        var due = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * DebounceSeconds);
        // Coalesce a burst without postponing its first deadline (bounded latency, CA-05.3/05.5).
        deadline = deadline is { } previous ? Math.Min(previous, due) : due;
    }

    /// <summary>Recomputes and applies now (scene bounds, scene surface, widget placement).</summary>
    public DockLayoutResult Relayout()
    {
        owner.VerifyAccess();
        deadline = null;
        var monitors = Enumerate();
        var monitor = MonitorResolver.Resolve(monitors, hint)
            ?? throw new InvalidOperationException("No monitor available.");
        // A taskbar that lives on another monitor says nothing about this one: no autohide margin, the bar is ignored.
        TaskbarLayout? bar = taskbar is { } t && (string.IsNullOrEmpty(t.MonitorDevice) || t.MonitorDevice == monitor.DeviceName)
            ? t : null;
        var sceneBounds = SceneLayout.Compute(monitor, bar ?? default, TowerHost.Band);
        var scene = owner.Scene;
        var previous = Current;
        if (previous is null || previous.Scene != sceneBounds) scene.SetBounds(sceneBounds);
        if (SceneSurface is { } surface && surface.Size != new PixelSize(sceneBounds.Width, sceneBounds.Height))
            surface.Resize(new PixelSize(sceneBounds.Width, sceneBounds.Height));
        WidgetPlacement placement = WidgetGeometry.Compute(monitor, sceneBounds, WidgetGeometry.NominalWidthDip); // TODO(TA.10-Windows): largura medida (WidgetRenderer.MeasureWidthDip)
        scene.Widget?.Apply(placement);
        var result = new DockLayoutResult(monitor, sceneBounds, placement);
        Current = result;
        dirty = false;
        RelayoutCount++;
        if (previous != result) LayoutApplied?.Invoke(result);
        return result;
    }

    internal void OnWake(long now)
    {
        if (deadline is { } due && now >= due) Relayout();
    }

    /// <summary>Show after changes while hidden recomputes before the window appears (active controller only).</summary>
    internal void OnSceneShown()
    {
        if (Current is not null && dirty) Relayout();
    }

    /// <summary>Called from the scene WndProc: WM_DPICHANGED (suggested rect ignored), WM_DISPLAYCHANGE, work-area/tray settings.</summary>
    internal void OnSystemMessage(uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case PInvoke.WM_DPICHANGED:
            case PInvoke.WM_DISPLAYCHANGE:
                Invalidate();
                break;
            case PInvoke.WM_SETTINGCHANGE when wParam == SpiSetWorkArea || IsTraySettings(wParam, lParam):
                Invalidate();
                break;
        }
    }

    private static bool IsTraySettings(nuint wParam, nint lParam) =>
        wParam == 0 && lParam != 0 && System.Runtime.InteropServices.Marshal.PtrToStringUni(lParam) == "TraySettings";
}

/// <summary>Outcome of a relayout.</summary>
public sealed record DockLayoutResult(MonitorInfo Monitor, PixelRect Scene, WidgetPlacement Widget);
