using System.Diagnostics;
using System.Runtime.InteropServices;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Threading;
using SideWar.Runtime.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Windows;

/// <summary>The hidden, passive scene HWND. All operations require its owning T-Game.</summary>
public sealed class SceneWindow : IDisposable
{
    private const string ClassName = "SideWar.Scene";
    private static readonly WNDPROC Procedure = WindowProcedure;
    private readonly GameThread owner;
    private readonly HINSTANCE module;
    private GCHandle self;
    private ushort atom;
    private HWND handle;
    private bool disposed;

    internal unsafe SceneWindow(GameThread owner)
    {
        this.owner = owner;
        owner.VerifyAccess();
        module = new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value);
        try
        {
            fixed (char* className = ClassName)
            {
                var cls = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = Procedure,
                    hInstance = module,
                    hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
                    lpszClassName = className,
                };
                atom = PInvoke.RegisterClassEx(cls);
                if (atom == 0) throw Win32Error.LastError("RegisterClassEx");
                self = GCHandle.Alloc(this);
                handle = PInvoke.CreateWindowEx(WindowStyles.ForOverlay(true), className, null,
                    WindowStyles.Popup, 0, 0, 1, 1, default, default, module,
                    (void*)GCHandle.ToIntPtr(self));
                if (handle.IsNull) throw Win32Error.LastError("CreateWindowEx");
            }
            SetDwm(DWMWINDOWATTRIBUTE.DWMWA_TRANSITIONS_FORCEDISABLED, 1);
            SetDwm(DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND);
            SetDwm(DWMWINDOWATTRIBUTE.DWMWA_EXCLUDED_FROM_PEEK, 1);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public unsafe nint Handle { get { owner.VerifyAccess(); return (nint)handle.Value; } }
    public bool IsVisible { get; private set; }

    /// <summary>The widget window, or null until <see cref="GetOrCreateWidget"/> (created on first use, RNF-03.2).</summary>
    public WidgetWindow? Widget { get; private set; }

    /// <summary>Creates the widget owned by this scene window (RF-03.9); it is destroyed before the scene.</summary>
    public WidgetWindow GetOrCreateWidget()
    {
        CheckAccess();
        return Widget ??= new WidgetWindow(owner, this);
    }

    private readonly List<OverlayChildWindow> children = [];
    private HotspotWindow? banner, supplyBox;
    private SceneSealWindow? seal;

    /// <summary>Banner hotspot window (peak/boss), created on first use (RF-22.8); at most one banner at a time.</summary>
    public HotspotWindow GetOrCreateBanner()
    {
        CheckAccess();
        return banner ??= new HotspotWindow(owner, this, HotspotKind.Banner);
    }

    /// <summary>Supply box hotspot window, created on first use (RF-22.8); at most one box at a time.</summary>
    public HotspotWindow GetOrCreateSupplyBox()
    {
        CheckAccess();
        return supplyBox ??= new HotspotWindow(owner, this, HotspotKind.SupplyBox);
    }

    /// <summary>Click-through absent-mode seal window, created on first use (RF-22.7, ADR-028).</summary>
    public SceneSealWindow GetOrCreateSeal()
    {
        CheckAccess();
        return seal ??= new SceneSealWindow(owner, this);
    }

    internal void Register(OverlayChildWindow child) => children.Add(child);

    internal void Unregister(OverlayChildWindow child)
    {
        children.Remove(child);
        if (ReferenceEquals(child, banner)) banner = null;
        if (ReferenceEquals(child, supplyBox)) supplyBox = null;
        if (ReferenceEquals(child, seal)) seal = null;
    }

    internal void ReassertChildren()
    {
        foreach (var child in children) child.Reassert();
    }

    /// <summary>SDD-24 windows, created on first use (RF-24.1); z-order: widget, panel, offline card, question card.</summary>
    public PanelWindow? Panel { get; private set; }
    public OfflineCardWindow? OfflineCard { get; private set; }
    public QuestionCardWindow? QuestionCard { get; private set; }
    public PanelWindow GetOrCreatePanel() { CheckAccess(); return Panel ??= new PanelWindow(owner); }
    public OfflineCardWindow GetOrCreateOfflineCard() { CheckAccess(); return OfflineCard ??= new OfflineCardWindow(owner); }
    public QuestionCardWindow GetOrCreateQuestionCard() { CheckAccess(); return QuestionCard ??= new QuestionCardWindow(owner); }
    internal IEnumerable<OverlayWindow> Overlays()
    {
        if (Panel is { } p) yield return p;
        if (OfflineCard is { } o) yield return o;
        if (QuestionCard is { } q) yield return q;
    }

    internal Action<uint>? MessageObserver { get; set; }
    /// <summary>Registration point for HotkeyService (T1.12): receives the WM_HOTKEY id.</summary>
    internal Action<int>? HotkeyHandler { get; set; }
    /// <summary>Registration point for SessionPowerWatcher (T2.5): WM_WTSSESSION_CHANGE / WM_POWERBROADCAST; true = handled.</summary>
    internal Func<uint, nuint, nint, bool>? SystemMessageHandler { get; set; }

    public void SetBounds(PixelRect bounds)
    {
        CheckAccess();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bounds.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bounds.Height);
        if (!PInvoke.SetWindowPos(handle, default, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER))
            throw Win32Error.LastError("SetWindowPos(bounds)");
    }

    public void Show()
    {
        CheckAccess();
        owner.Dock?.OnSceneShown(); // recompute layoutDirty before appearing (RF-05.6)
        PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        IsVisible = true;
        Widget?.OnSceneShown();
        foreach (var child in children.ToArray()) child.OnSceneShown();
        foreach (var overlay in Overlays()) overlay.OnSceneShown();
        owner.ZOrder.ReassertNow();
        owner.Mode?.OnSceneShown();
    }

    public void Hide()
    {
        CheckAccess();
        PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_HIDE);
        IsVisible = false;
        foreach (var overlay in Overlays()) overlay.OnSceneHidden();
        Widget?.OnSceneHidden();
        foreach (var child in children.ToArray()) child.OnSceneHidden();
        owner.ZOrder.Cancel();
        owner.Mode?.OnSceneHidden(); // hidden → Passive (SDD-04 RF-04.2)
    }

    private unsafe void SetDwm(DWMWINDOWATTRIBUTE attribute, int value) =>
        PInvoke.DwmSetWindowAttribute(handle, attribute, &value, sizeof(int)).ThrowOnFailure();

    private void CheckAccess()
    {
        owner.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public unsafe void Dispose()
    {
        owner.VerifyAccess();
        if (disposed) return;
        foreach (var child in children.ToArray()) child.Dispose();
        // Un-owned SDD-24 windows first, then the owned widget.
        foreach (var overlay in Overlays().ToArray()) overlay.Dispose();
        Panel = null; OfflineCard = null; QuestionCard = null;
        // Owned windows go first (RF-03.7); a failing widget keeps the scene alive rather than dangling it.
        if (Widget is { } widget)
        {
            widget.Dispose();
            Widget = null;
        }
        // Preserve the root if native destruction failed: a live HWND must never point at freed state.
        if (!handle.IsNull && !PInvoke.DestroyWindow(handle)) throw Win32Error.LastError("DestroyWindow");
        handle = default;
        IsVisible = false;
        if (self.IsAllocated) self.Free();
        if (atom != 0)
        {
            fixed (char* name = ClassName)
                if (!PInvoke.UnregisterClass(name, module)) throw Win32Error.LastError("UnregisterClass");
            atom = 0;
        }
        disposed = true;
    }

    private static unsafe LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        SceneWindow? window = null;
        try
        {
            if (message == PInvoke.WM_NCCREATE)
            {
                var data = (CREATESTRUCTW*)lParam.Value;
                WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, (nint)data->lpCreateParams);
            }
            var pointer = WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
            if (pointer != 0) window = (SceneWindow?)GCHandle.FromIntPtr(pointer).Target;
            window?.MessageObserver?.Invoke(message);
            window?.owner.Dock?.OnSystemMessage(message, wParam.Value, lParam.Value); // SDD-05 RF-05.6
            window?.owner.Mode?.OnMessage(hwnd, message, wParam.Value, lParam.Value); // SDD-04 RF-04.3
            if (window?.SystemMessageHandler?.Invoke(message, wParam.Value, lParam.Value) == true) return new LRESULT(1); // SDD-07; TRUE for PBT_*
            switch (message)
            {
                case PInvoke.WM_MOUSEACTIVATE: return new LRESULT((nint)PInvoke.MA_NOACTIVATE);
                case PInvoke.WM_NCHITTEST: return new LRESULT((nint)PInvoke.HTCLIENT);
                case PInvoke.WM_SETCURSOR:
                    PInvoke.SetCursor(PInvoke.LoadCursor(default, PInvoke.IDC_ARROW));
                    return new LRESULT(1);
                case PInvoke.WM_SETFOCUS:
                case PInvoke.WM_ACTIVATE:
                case PInvoke.WM_NCACTIVATE:
                    Debug.WriteLine($"SceneWindow activation diagnostic: {message:X}");
                    break;
                case PInvoke.WM_HOTKEY:
                    window?.HotkeyHandler?.Invoke((int)wParam.Value);
                    return new LRESULT(0);
                case PInvoke.WM_DISPLAYCHANGE:
                    window?.owner.ZOrder.Request();
                    break;
                case PInvoke.WM_NCDESTROY:
                    WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, 0);
                    break;
            }
        }
        catch (Exception error)
        {
            if (window is not null) window.owner.Report(error);
            else Trace.WriteLine(error);
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
