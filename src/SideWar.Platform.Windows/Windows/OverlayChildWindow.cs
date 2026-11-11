using System.Diagnostics;
using System.Runtime.InteropServices;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Rendering;
using SideWar.Platform.Windows.Threading;
using SideWar.Runtime.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Windows;

/// <summary>
/// Base of the small layered, non-activating windows owned by the scene (hotspot banners/boxes and the absent seal;
/// ADR-021, ADR-028, SDD-22 RF-22.7/RF-22.8). Same pattern as <see cref="WidgetWindow"/>: WS_POPUP owned by the scene,
/// LayeredSurface, hidden with the scene, DIB released while hidden. All members require the owning T-Game.
/// </summary>
public abstract unsafe class OverlayChildWindow : IDisposable
{
    private static readonly WNDPROC Procedure = WindowProcedure;
    private readonly string className;
    private readonly SceneWindow scene;
    private readonly HINSTANCE module;
    private GCHandle self;
    private ushort atom;
    private HWND handle;
    private bool disposed;

    private protected OverlayChildWindow(GameThread owner, SceneWindow scene, string className, bool clickThrough)
    {
        Owner = owner;
        this.scene = scene;
        this.className = className;
        owner.VerifyAccess();
        module = new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value);
        try
        {
            Create(clickThrough);
            Surface = new LayeredSurface((nint)handle.Value);
        }
        catch
        {
            Dispose();
            throw;
        }
        scene.Register(this);
    }

    private protected GameThread Owner { get; }
    public nint Handle { get { Owner.VerifyAccess(); return (nint)handle.Value; } }
    public LayeredSurface Surface { get; private set; } = null!;

    /// <summary>True once <see cref="Show"/> was requested; the scene's Show/Hide drive the real visibility.</summary>
    public bool WantsVisible { get; private set; }
    public bool IsVisible { get; private set; }
    public PixelRect Bounds { get; private set; }

    /// <summary>Positions and sizes the window in screen pixels; a size change resizes the surface (DIB recreated lazily).</summary>
    public void Apply(PixelRect bounds)
    {
        CheckAccess();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bounds.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bounds.Height);
        var sizeChanged = bounds.Width != Bounds.Width || bounds.Height != Bounds.Height;
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER;
        if (!sizeChanged) flags |= SET_WINDOW_POS_FLAGS.SWP_NOSIZE;
        if (!PInvoke.SetWindowPos(handle, default, bounds.Left, bounds.Top, bounds.Width, bounds.Height, flags))
            throw Win32Error.LastError("SetWindowPos(overlay child)");
        if (sizeChanged) Surface.Resize(new PixelSize(bounds.Width, bounds.Height));
        Bounds = bounds;
    }

    /// <summary>
    /// Requests visibility; the window only appears while the scene is visible. Presents one transparent frame (a
    /// layered window needs one UpdateLayeredWindow to appear); draw the content after this call.
    /// </summary>
    public void Show()
    {
        CheckAccess();
        if (Bounds.Width == 0) throw new InvalidOperationException("Apply bounds before Show.");
        WantsVisible = true;
        if (scene.IsVisible) Present();
    }

    public void Hide()
    {
        CheckAccess();
        WantsVisible = false;
        HideNative();
    }

    internal void OnSceneShown()
    {
        if (WantsVisible && !disposed) Present();
    }

    internal void OnSceneHidden()
    {
        if (!disposed) HideNative();
    }

    internal void Reassert()
    {
        if (!IsVisible) return;
        if (!PInvoke.SetWindowPos(handle, HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER))
            throw Win32Error.LastError("SetWindowPos(overlay child topmost)");
    }

    /// <summary>Client-area mouse release (WM_LBUTTONUP) at (<paramref name="x"/>, <paramref name="y"/>) in window pixels.</summary>
    private protected virtual void OnLeftButtonUp(int x, int y) { }

    /// <summary>True when the cursor over the client area should be the hand.</summary>
    private protected virtual bool UsesHandCursor => false;

    /// <summary>True when the window must report HTTRANSPARENT to hit-testing (click-through seal).</summary>
    private protected virtual bool HitTransparent => false;

    private unsafe void Create(bool clickThrough)
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
            // hWndParent on a WS_POPUP makes it owned: above the scene, hidden/destroyed with it.
            handle = PInvoke.CreateWindowEx(WindowStyles.ForOverlay(clickThrough), name, null,
                WindowStyles.Popup, 0, 0, 1, 1, new HWND(scene.Handle), default, module,
                (void*)GCHandle.ToIntPtr(self));
            if (handle.IsNull) throw Win32Error.LastError($"CreateWindowEx({className})");
        }
        SetDwm(DWMWINDOWATTRIBUTE.DWMWA_TRANSITIONS_FORCEDISABLED, 1);
        SetDwm(DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND);
        SetDwm(DWMWINDOWATTRIBUTE.DWMWA_EXCLUDED_FROM_PEEK, 1);
    }

    private void Present()
    {
        Surface.BeginFrame();
        Surface.EndFrame(null);
        PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        IsVisible = true;
        Reassert();
    }

    private void HideNative()
    {
        if (handle.IsNull) return;
        PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_HIDE);
        IsVisible = false;
        Surface?.Release();
    }

    private unsafe void SetDwm(DWMWINDOWATTRIBUTE attribute, int value) =>
        PInvoke.DwmSetWindowAttribute(handle, attribute, &value, sizeof(int)).ThrowOnFailure();

    private protected void CheckAccess()
    {
        Owner.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public unsafe void Dispose()
    {
        Owner.VerifyAccess();
        if (disposed) return;
        scene.Unregister(this);
        Surface?.Dispose();
        if (!handle.IsNull && !PInvoke.DestroyWindow(handle)) throw Win32Error.LastError($"DestroyWindow({className})");
        handle = default;
        IsVisible = false;
        WantsVisible = false;
        if (self.IsAllocated) self.Free();
        if (atom != 0)
        {
            fixed (char* name = className)
                if (!PInvoke.UnregisterClass(name, module)) throw Win32Error.LastError($"UnregisterClass({className})");
            atom = 0;
        }
        disposed = true;
        GC.SuppressFinalize(this);
    }

    private static unsafe LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        OverlayChildWindow? window = null;
        try
        {
            if (message == PInvoke.WM_NCCREATE)
            {
                var data = (CREATESTRUCTW*)lParam.Value;
                WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, (nint)data->lpCreateParams);
            }
            var pointer = WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
            if (pointer != 0) window = (OverlayChildWindow?)GCHandle.FromIntPtr(pointer).Target;
            switch (message)
            {
                case PInvoke.WM_MOUSEACTIVATE: return new LRESULT((nint)PInvoke.MA_NOACTIVATE);
                case PInvoke.WM_NCHITTEST:
                    return new LRESULT(window?.HitTransparent == true ? PInvoke.HTTRANSPARENT : (nint)PInvoke.HTCLIENT);
                case PInvoke.WM_SETCURSOR:
                    PInvoke.SetCursor(PInvoke.LoadCursor(default, window?.UsesHandCursor == true ? PInvoke.IDC_HAND : PInvoke.IDC_ARROW));
                    return new LRESULT(1);
                case PInvoke.WM_LBUTTONUP:
                    // Client coordinates are signed 16-bit words (no capture, no focus).
                    window?.OnLeftButtonUp((short)(lParam.Value & 0xFFFF), (short)((lParam.Value >> 16) & 0xFFFF));
                    return new LRESULT(0);
                case PInvoke.WM_SETFOCUS:
                case PInvoke.WM_ACTIVATE:
                case PInvoke.WM_NCACTIVATE:
                    Debug.WriteLine($"OverlayChildWindow activation diagnostic: {message:X}");
                    break;
                case PInvoke.WM_NCDESTROY:
                    WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, 0);
                    break;
            }
        }
        catch (Exception error)
        {
            if (window is not null) window.Owner.Report(error);
            else Trace.WriteLine(error);
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
