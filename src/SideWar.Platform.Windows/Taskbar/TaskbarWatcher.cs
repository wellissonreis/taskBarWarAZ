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
    /// <summary>Win32 hook surface, replaceable by tests (no real hook, no window).</summary>
    internal interface IHookApi
    {
        bool TryGetProcessId(nint window, out uint pid);
        nint Install(uint pid, WINEVENTPROC callback);
        void Remove(nint hook);
    }

    private sealed class Win32HookApi : IHookApi
    {
        public unsafe bool TryGetProcessId(nint window, out uint pid)
        {
            uint value = 0;
            var ok = PInvoke.GetWindowThreadProcessId((HWND)window, &value) != 0 && value != 0;
            pid = value;
            return ok;
        }

        public unsafe nint Install(uint pid, WINEVENTPROC callback) =>
            (nint)PInvoke.SetWinEventHook(PInvoke.EVENT_OBJECT_CREATE, PInvoke.EVENT_OBJECT_LOCATIONCHANGE,
                HMODULE.Null, callback, pid, 0, PInvoke.WINEVENT_OUTOFCONTEXT).Value;

        public unsafe void Remove(nint hook) => PInvoke.UnhookWinEvent(new HWINEVENTHOOK((void*)hook));
    }

    private const uint ObjidWindow = 0, ObjidClient = 0xFFFFFFFC;
    private readonly Action _signal;
    private readonly WINEVENTPROC _callback; // rooted for the lifetime of the hook
    private readonly IHookApi _api;
    private nint _hook;
    private HWND _bar;
    private bool _wanted;

    public TaskbarWatcher(Action signal) : this(signal, new Win32HookApi()) { }

    internal TaskbarWatcher(Action signal, IHookApi api)
    {
        _api = api;
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _callback = OnEvent;
    }

    public bool IsInstalled => _hook != 0;
    /// <summary>Times the hook was reinstalled for a new Explorer process ("TaskbarCreated").</summary>
    public int ReinstallCount { get; private set; }
    public long EventsReceived { get; private set; }
    /// <summary>taskbar.events.dropped (RNF-21.6).</summary>
    public long EventsDropped { get; private set; }
    public long EventsAccepted => EventsReceived - EventsDropped;

    /// <summary>Replaces any previous hook. Returns false if the hook could not be installed.</summary>
    public bool Install(nint taskbarHwnd)
    {
        Uninstall();
        _wanted = true;
        _bar = (HWND)taskbarHwnd;
        if (!_api.TryGetProcessId(taskbarHwnd, out var pid)) return false;
        _hook = _api.Install(pid, _callback);
        return _hook != 0;
    }

    /// <summary>Removes the hook (Hide) and forgets that one is wanted, so TaskbarCreated does not reinstall it.</summary>
    public void Uninstall()
    {
        _wanted = false;
        if (_hook == 0) return;
        _api.Remove(_hook);
        _hook = 0;
    }

    /// <summary>
    /// "TaskbarCreated" arrived (Explorer restarted): when a hook is wanted (Show), reinstalls it for the new
    /// Explorer PID using the freshly located taskbar window, then signals one reevaluation. Nothing happens while
    /// hidden. Returns whether a hook is installed afterwards.
    /// </summary>
    public bool OnTaskbarCreated(Func<nint> locateTaskbar)
    {
        ArgumentNullException.ThrowIfNull(locateTaskbar);
        if (!_wanted) return false;
        var window = locateTaskbar();
        var installed = window != 0 && Install(window);
        if (!installed) _wanted = true; // keep wanting it: a later TaskbarCreated retries
        ReinstallCount++;
        _signal();
        return installed;
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
