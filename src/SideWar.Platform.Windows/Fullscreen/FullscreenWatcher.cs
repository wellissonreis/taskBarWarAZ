using System.Diagnostics;
using SideWar.Runtime;
using SideWar.Runtime.Fullscreen;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace SideWar.Platform.Windows.Fullscreen;

/// <summary>
/// Event-driven fullscreen watcher (SDD-06 RF-06.1): WinEvent hooks (foreground, minimize, per-process location),
/// the AppBar probe and the pure <see cref="FullscreenController"/> for coalescing, polling and hysteresis.
/// Everything runs on T-Game: construct, call <see cref="OnWake"/> and <see cref="Dispose"/> there. The host
/// (the App, later) forwards <see cref="NextDeadline"/> and <see cref="OnWake"/> and subscribes to
/// <see cref="Transition"/>; this class knows nothing about the state machine.
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    private const int ObjIdWindow = 0; // OBJID_WINDOW (winuser.h)
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly Action<Exception> report;
    private readonly WinEventProc foregroundProc;
    private readonly WinEventProc minimizeProc;
    private readonly WinEventProc locationProc;
    private readonly FullscreenController controller;
    private readonly AppBarProbeWindow probe;
    private UnhookWinEventSafeHandle? foregroundHook, minimizeHook, locationHook;
    private nint locationHwnd;
    private bool disposed;

    private delegate void WinEventProc(HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint thread, uint time);

    /// <param name="clock">Same monotonic timebase the host passes to its loop (Stopwatch).</param>
    public FullscreenWatcher(IClock clock, HideOnFullscreen mode, string overlayMonitor, Action<Exception> report,
        IWindowProbe? windowProbe = null)
    {
        this.report = report;
        controller = new FullscreenController(clock, windowProbe ?? new WindowProbe(), (uint)Environment.ProcessId, mode, overlayMonitor);
        foregroundProc = OnForeground;
        minimizeProc = OnMinimize;
        locationProc = OnLocation;
        probe = new AppBarProbeWindow(Signal, report);
        try
        {
            foregroundHook = Hook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, foregroundProc, 0, 0, true);
            minimizeHook = Hook(PInvoke.EVENT_SYSTEM_MINIMIZESTART, PInvoke.EVENT_SYSTEM_MINIMIZEEND, minimizeProc, 0, 0, true);
            RehookLocation(PInvoke.GetForegroundWindow());
            Signal(); // initial evaluation
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Hidden-by-fullscreen edge after hysteresis (SDD-06 RF-06.5). Raised on T-Game.</summary>
    public event Action<FullscreenTransition>? Transition
    {
        add => controller.Transition += value;
        remove => controller.Transition -= value;
    }

    public bool Hidden => controller.Hidden;
    public IReadOnlySet<string> FullscreenMonitors => controller.FullscreenMonitors;
    public long? NextDeadline => disposed ? null : controller.NextDeadline;
    public bool ShellNotificationRegistered => probe.IsRegistered;

    /// <summary>Overlay hidden for a non-fullscreen reason: stops the safety polling (SDD-06 RF-06.1 item 5).</summary>
    public bool HiddenForOtherReason
    {
        get => controller.HiddenForOtherReason;
        set => controller.HiddenForOtherReason = value;
    }

    public void OnWake() { if (!disposed) controller.OnWake(); }
    public void Reconfigure(HideOnFullscreen mode, string overlayMonitor) => controller.Reconfigure(mode, overlayMonitor);
    public void Signal() { if (!disposed) controller.Signal(); }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Unhook(ref foregroundHook);
        Unhook(ref minimizeHook);
        Unhook(ref locationHook);
        probe.Dispose();
    }

    private static UnhookWinEventSafeHandle Hook(uint min, uint max, WinEventProc proc, uint pid, uint thread, bool skipOwn)
    {
        uint flags = PInvoke.WINEVENT_OUTOFCONTEXT | (skipOwn ? PInvoke.WINEVENT_SKIPOWNPROCESS : 0);
        var hook = PInvoke.SetWinEventHook(min, max, default, new WINEVENTPROC(proc.Invoke), pid, thread, flags);
        if (hook.IsInvalid) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "SetWinEventHook failed");
        return hook;
    }

    private static void Unhook(ref UnhookWinEventSafeHandle? hook)
    {
        hook?.Dispose();
        hook = null;
    }

    // Replaced on each foreground change so only the foreground process generates LOCATIONCHANGE (RF-06.1 item 3).
    private unsafe void RehookLocation(HWND foreground)
    {
        Unhook(ref locationHook);
        locationHwnd = (nint)foreground.Value;
        if (foreground.IsNull) return;
        uint thread = PInvoke.GetWindowThreadProcessId(foreground, out uint pid);
        if (thread == 0 || pid == 0) return;
        try { locationHook = Hook(PInvoke.EVENT_OBJECT_LOCATIONCHANGE, PInvoke.EVENT_OBJECT_LOCATIONCHANGE, locationProc, pid, 0, true); }
        catch (Exception error) { report(error); }
    }

    private void Guard(Action action)
    {
        try
        {
            if (disposed) return;
            Debug.Assert(Environment.CurrentManagedThreadId == ownerThread, "WinEvent callback off the owner thread");
            action();
        }
        catch (Exception error) { report(error); }
    }

    private void OnForeground(HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint thread, uint time) =>
        Guard(() => { RehookLocation(hwnd); controller.Signal(); });

    private void OnMinimize(HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint thread, uint time) =>
        Guard(controller.Signal);

    private unsafe void OnLocation(HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != ObjIdWindow || idChild != (int)PInvoke.CHILDID_SELF || (nint)hwnd.Value != locationHwnd) return;
        Guard(controller.Signal);
    }
}
