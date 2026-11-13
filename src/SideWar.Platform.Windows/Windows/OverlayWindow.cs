using System.Diagnostics;
using System.Runtime.InteropServices;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Rendering;
using SideWar.Platform.Windows.Threading;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Rendering;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Windows;

/// <summary>What a renderer needs to draw one frame of an overlay window.</summary>
public readonly record struct OverlayRenderContext(OverlayKind Kind, OverlayPlacement Placement, PanelViewState View);

/// <summary>
/// Shared base of the three SDD-24 layered windows (panel, question card, offline card): top-level (no owner),
/// LAYERED|TOOLWINDOW|NOACTIVATE|TOPMOST, never TRANSPARENT, MA_NOACTIVATE, no keyboard. All members run on T-Game.
/// Content is drawn by <see cref="Renderer"/> (T5.4/T5.5); without one a 1/255-alpha fill keeps every pixel of the
/// window (shadow included) consuming clicks (RF-24.11).
/// </summary>
public abstract class OverlayWindow : IDisposable
{
    private static readonly WNDPROC Procedure = WindowProcedure;
    private readonly GameThread owner;
    private readonly string className;
    private readonly HINSTANCE module;
    private GCHandle self;
    private ushort atom;
    private HWND handle;
    private bool disposed;
    private bool tracking;
    private PanelHit pressed = PanelHit.None;
    private long renderedVersion = -1;

    private protected unsafe OverlayWindow(GameThread owner, OverlayKind kind, string className)
    {
        this.owner = owner;
        this.className = className;
        Kind = kind;
        owner.VerifyAccess();
        module = new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value);
        try
        {
            fixed (char* name = className)
            {
                var cls = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = Procedure,
                    hInstance = module,
                    hCursor = PInvoke.LoadCursor(default, PInvoke.IDC_ARROW),
                    lpszClassName = name,
                };
                atom = PInvoke.RegisterClassEx(cls);
                if (atom == 0) throw Win32Error.LastError($"RegisterClassEx({className})");
                self = GCHandle.Alloc(this);
                // No owner (RF-24.1): hWndParent is null.
                handle = PInvoke.CreateWindowEx(WindowStyles.ForOverlay(false), name, null, WindowStyles.Popup,
                    0, 0, 1, 1, default, default, module, (void*)GCHandle.ToIntPtr(self));
                if (handle.IsNull) throw Win32Error.LastError($"CreateWindowEx({className})");
            }
            SetDwm(DWMWINDOWATTRIBUTE.DWMWA_TRANSITIONS_FORCEDISABLED, 1);
            SetDwm(DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND);
            SetDwm(DWMWINDOWATTRIBUTE.DWMWA_EXCLUDED_FROM_PEEK, 1);
            Surface = new LayeredSurface((nint)handle.Value);
            View = new PanelViewState();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public OverlayKind Kind { get; }
    public unsafe nint Handle { get { owner.VerifyAccess(); return (nint)handle.Value; } }
    public LayeredSurface Surface { get; private set; } = null!;
    public PanelViewState View { get; private set; } = null!;
    public OverlayPlacement Placement { get; private set; }
    public bool WantsVisible { get; private set; }
    public bool IsVisible { get; private set; }

    /// <summary>Frames presented since creation (RF-24.13: zero while nothing changes).</summary>
    public long FrameCount { get; private set; }

    /// <summary>Draws a full frame; null = the 1/255 hit-surface fallback.</summary>
    public Action<OverlayFrame, OverlayRenderContext>? Renderer { get; set; }

    /// <summary>Raised once per button press anywhere in the window, left or right (RF-24.11, CA-24.12). Not for hover/move/wheel.</summary>
    public Action? Interaction { get; set; }

    /// <summary>Raw left button-down (client pixels), for the card windows whose hit-test lives in Runtime (T4.10b). Not for hover/move.</summary>
    public Action<int, int>? LeftDown { get; set; }

    /// <summary>Raw left button-up (client pixels), paired with <see cref="LeftDown"/>.</summary>
    public Action<int, int>? LeftUp { get; set; }

    /// <summary>Raised on button-up over the same target as the button-down (not for scroll thumb/track).</summary>
    public Action<PanelHit>? Clicked { get; set; }

    /// <summary>Applies a computed placement (RF-24.2). Pure moves use NOSIZE|NOACTIVATE and draw nothing.</summary>
    public void Apply(OverlayPlacement placement)
    {
        CheckAccess();
        var b = placement.Bounds;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(b.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(b.Height);
        var sizeChanged = b.Width != Placement.Bounds.Width || b.Height != Placement.Bounds.Height;
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER;
        if (!sizeChanged) flags |= SET_WINDOW_POS_FLAGS.SWP_NOSIZE;
        if (!PInvoke.SetWindowPos(handle, default, b.Left, b.Top, b.Width, b.Height, flags))
            throw Win32Error.LastError("SetWindowPos(overlay bounds)");
        if (sizeChanged)
        {
            Surface.Resize(new PixelSize(b.Width, b.Height));
            renderedVersion = -1;
        }
        if (Kind == OverlayKind.Panel) View.SetContent(View.Content, placement.ViewportDip);
        Placement = placement;
        if (sizeChanged && IsVisible) Render();
    }

    /// <summary>Requests visibility; appears only while the scene is visible (offline card included: it never shows with the scene hidden).</summary>
    public void Show()
    {
        CheckAccess();
        if (Placement.Bounds.Width == 0) throw new InvalidOperationException("Apply a placement before Show.");
        WantsVisible = true;
        if (owner.Scene.IsVisible) Present();
    }

    public void Hide()
    {
        CheckAccess();
        WantsVisible = false;
        HideNative();
    }

    /// <summary>Forces a redraw of the visible window (content owned by the caller changed). No-op while hidden.</summary>
    public void Redraw()
    {
        CheckAccess();
        if (IsVisible) Render();
    }

    /// <summary>Redraws only if the view version changed since the last frame (RF-24.13).</summary>
    public bool InvalidateIfChanged()
    {
        CheckAccess();
        if (!IsVisible || View.Version == renderedVersion) return false;
        Render();
        return true;
    }

    internal void OnSceneShown() { if (WantsVisible && !disposed) Present(); }
    internal void OnSceneHidden() { if (!disposed) HideNative(); }

    internal void Reassert()
    {
        if (!IsVisible) return;
        if (!PInvoke.SetWindowPos(handle, HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER))
            throw Win32Error.LastError("SetWindowPos(overlay topmost)");
    }

    private void Present()
    {
        Render();
        PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        IsVisible = true;
        Reassert();
    }

    private void HideNative()
    {
        if (handle.IsNull) return;
        PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_HIDE);
        IsVisible = false;
        tracking = false;
        pressed = PanelHit.None;
        View.EndDrag();
        View.SetHover(PanelHit.None);
        Surface?.Release(); // RF-24.14: surface freed on hide; logical state stays in the model
        renderedVersion = -1;
    }

    private unsafe void Render()
    {
        var frame = Surface.BeginFrame();
        if (Renderer is { } draw) draw(frame, new OverlayRenderContext(Kind, Placement, View));
        else new Span<uint>((void*)frame.Pixels, frame.Width * frame.Height).Fill(0x01000000u); // premultiplied BGRA, alpha 1, black
        Surface.EndFrame(null);
        renderedVersion = View.Version;
        FrameCount++;
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
        Surface?.Dispose();
        if (!handle.IsNull && !PInvoke.DestroyWindow(handle)) throw Win32Error.LastError("DestroyWindow(overlay)");
        handle = default;
        IsVisible = false;
        WantsVisible = false;
        if (self.IsAllocated) self.Free();
        if (atom != 0)
        {
            fixed (char* name = className)
                if (!PInvoke.UnregisterClass(name, module)) throw Win32Error.LastError("UnregisterClass(overlay)");
            atom = 0;
        }
        disposed = true;
        GC.SuppressFinalize(this);
    }

    // ---- input -----------------------------------------------------------------------------------------

    private PanelHit HitAt(int xPx, int yPx)
    {
        if (Kind != OverlayKind.Panel) return PanelHit.Background; // card contents arrive in T5.5
        var s = (float)Placement.Scale;
        return PanelChromeHit.Test(xPx / s, yPx / s, Placement.Box.Height / s, View.Content, View.Viewport, View.Offset);
    }

    private float TrackY(int yPx) => yPx / (float)Placement.Scale - PanelScroll.TrackTopDip;

    private void OnMouseMove(int x, int y)
    {
        if (!tracking)
        {
            var track = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE, hwndTrack = handle };
            tracking = PInvoke.TrackMouseEvent(ref track);
        }
        if (View.Dragging) { Changed(View.Drag(TrackY(y))); return; }
        Changed(View.SetHover(HitAt(x, y)));
    }

    private void OnButtonDown(int x, int y, bool left)
    {
        Interaction?.Invoke();
        if (!left) return;
        LeftDown?.Invoke(x, y);
        var hit = HitAt(x, y);
        pressed = hit;
        if (hit == PanelHit.ScrollThumb) { PInvoke.SetCapture(handle); View.BeginDrag(TrackY(y)); }
        else if (hit == PanelHit.ScrollTrack) View.TrackClick(TrackY(y));
        else if (hit != PanelHit.Background && hit != PanelHit.None) PInvoke.SetCapture(handle); // complete the click
        Changed(true);
    }

    private void OnLeftUp(int x, int y)
    {
        LeftUp?.Invoke(x, y);
        var dragging = View.Dragging;
        var hit = HitAt(x, y);
        var down = pressed;
        pressed = PanelHit.None;
        View.EndDrag();
        if (PInvoke.GetCapture() == handle) PInvoke.ReleaseCapture();
        if (!dragging && down == hit && hit is not (PanelHit.None or PanelHit.Background or PanelHit.ScrollThumb or PanelHit.ScrollTrack))
            Clicked?.Invoke(hit);
        Changed(true);
    }

    private void OnWheel(int delta)
    {
        if (Kind == OverlayKind.Panel) Changed(View.Wheel(delta));
    }

    // Input already runs on T-Game: redraw at once, but only if the version rose (RF-24.13).
    private void Changed(bool changed) { if (changed) InvalidateIfChanged(); }

    private static unsafe LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        OverlayWindow? window = null;
        try
        {
            if (message == PInvoke.WM_NCCREATE)
            {
                var data = (CREATESTRUCTW*)lParam.Value;
                WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, (nint)data->lpCreateParams);
            }
            var pointer = WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
            if (pointer != 0) window = (OverlayWindow?)GCHandle.FromIntPtr(pointer).Target;
            var x = (short)(lParam.Value & 0xFFFF);
            var y = (short)((lParam.Value >> 16) & 0xFFFF);
            switch (message)
            {
                case PInvoke.WM_MOUSEACTIVATE: return new LRESULT((nint)PInvoke.MA_NOACTIVATE);
                case PInvoke.WM_NCHITTEST: return new LRESULT((nint)PInvoke.HTCLIENT);
                case PInvoke.WM_SETCURSOR:
                {
                    var hand = false;
                    if (window is { IsVisible: true } w && PInvoke.GetCursorPos(out var p) && PInvoke.ScreenToClient(hwnd, ref p))
                        hand = w.HitAt(p.X, p.Y) is PanelHit.Close or PanelHit.ScrollThumb or PanelHit.ScrollTrack;
                    PInvoke.SetCursor(PInvoke.LoadCursor(default, hand ? PInvoke.IDC_HAND : PInvoke.IDC_ARROW));
                    return new LRESULT(1);
                }
                case PInvoke.WM_MOUSEMOVE: window?.OnMouseMove(x, y); return new LRESULT(0);
                case PInvoke.WM_MOUSELEAVE:
                    if (window is not null) { window.tracking = false; window.View.SetHover(PanelHit.None); }
                    return new LRESULT(0);
                case PInvoke.WM_LBUTTONDOWN: window?.OnButtonDown(x, y, true); return new LRESULT(0);
                case PInvoke.WM_RBUTTONDOWN: window?.OnButtonDown(x, y, false); return new LRESULT(0);
                case PInvoke.WM_LBUTTONUP: window?.OnLeftUp(x, y); return new LRESULT(0);
                case PInvoke.WM_CAPTURECHANGED:
                    if (window is not null) { window.pressed = PanelHit.None; window.View.EndDrag(); }
                    return new LRESULT(0);
                case PInvoke.WM_MOUSEWHEEL:
                    window?.OnWheel((short)((wParam.Value >> 16) & 0xFFFF));
                    return new LRESULT(0);
                case PInvoke.WM_SETFOCUS:
                case PInvoke.WM_ACTIVATE:
                case PInvoke.WM_NCACTIVATE:
                    Debug.WriteLine($"OverlayWindow activation diagnostic: {message:X}");
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

public sealed class PanelWindow : OverlayWindow
{
    internal PanelWindow(GameThread owner) : base(owner, OverlayKind.Panel, "SideWar.Panel") { }
}

public sealed class QuestionCardWindow : OverlayWindow
{
    internal QuestionCardWindow(GameThread owner) : base(owner, OverlayKind.QuestionCard, "SideWar.QuestionCard") { }
}

public sealed class OfflineCardWindow : OverlayWindow
{
    internal OfflineCardWindow(GameThread owner) : base(owner, OverlayKind.OfflineCard, "SideWar.OfflineCard") { }
}
