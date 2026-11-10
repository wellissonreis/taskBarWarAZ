using SideWar.Runtime.Taskbar;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace SideWar.Platform.Windows.Taskbar;

/// <summary>
/// SDD-21 RF-21.6 WinEvent hook (out of context) for the Explorer process. Install and Uninstall must run on a thread
/// with a message loop (T-Game). The hook is removed on Hide and reinstalled on Show (zero cost while hidden) and
/// reinstalled with the new PID when "TaskbarCreated" arrives. The callback only counts and signals; filtering
/// does not allocate. Signal is the single entry to TaskbarService.Signal (debounced to 500 ms there).
/// </summary>
public sealed class TaskbarWatcher : IDisposable
{
    private const uint ObjidWindow = 0, ObjidClient = 0xFFFFFFFC;
    private readonly Action _signal;
    private readonly WINEVENTPROC _callback; // rooted for the lifetime of the hook
    private HWINEVENTHOOK _hook;
    private HWND _bar;

    public TaskbarWatcher(Action signal)
    {
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _callback = OnEvent;
    }

    public bool IsInstalled => !_hook.IsNull;
    public long EventsReceived { get; private set; }
    /// <summary>taskbar.events.dropped (RNF-21.6).</summary>
    public long EventsDropped { get; private set; }
    public long EventsAccepted => EventsReceived - EventsDropped;

    /// <summary>Replaces any previous hook. Returns false if the hook could not be installed.</summary>
    public unsafe bool Install(nint taskbarHwnd)
    {
        Uninstall();
        _bar = (HWND)taskbarHwnd;
        uint pid = 0;
        if (PInvoke.GetWindowThreadProcessId(_bar, &pid) == 0 || pid == 0) return false;
        _hook = PInvoke.SetWinEventHook(PInvoke.EVENT_OBJECT_CREATE, PInvoke.EVENT_OBJECT_LOCATIONCHANGE,
            HMODULE.Null, _callback, pid, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        return !_hook.IsNull;
    }

    public void Uninstall()
    {
        if (_hook.IsNull) return;
        PInvoke.UnhookWinEvent(_hook);
        _hook = default;
    }

    private void OnEvent(HWINEVENTHOOK hook, uint evt, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        EventsReceived++;
        if (!Accepts(evt, hwnd, idObject)) { EventsDropped++; return; }
        _signal();
    }

    private bool Accepts(uint evt, HWND hwnd, int idObject)
    {
        if (evt != PInvoke.EVENT_OBJECT_CREATE && evt != PInvoke.EVENT_OBJECT_DESTROY &&
            evt != PInvoke.EVENT_OBJECT_LOCATIONCHANGE) return false;
        if ((uint)idObject != ObjidWindow && (uint)idObject != ObjidClient) return false;
        return hwnd == _bar || (!hwnd.IsNull && PInvoke.IsChild(_bar, hwnd));
    }

    public void Dispose() => Uninstall();
}
