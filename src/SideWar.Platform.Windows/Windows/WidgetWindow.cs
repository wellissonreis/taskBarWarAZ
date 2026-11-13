using System.Diagnostics;
using System.Runtime.InteropServices;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Rendering;
using SideWar.Platform.Windows.Threading;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Widget;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Windows;

/// <summary>
/// The empty HUD widget HWND (SDD-23 RF-23.1): owned by the scene window, always clickable (never
/// WS_EX_TRANSPARENT), non-activating. Content arrives in E5. All operations require the owning T-Game.
/// </summary>
public sealed class WidgetWindow : IDisposable
{
    private const string ClassName = "SideWar.Widget";
    private static readonly WNDPROC Procedure = WindowProcedure;
    private readonly GameThread owner;
    private readonly HINSTANCE module;
    private GCHandle self;
    private ushort atom;
    private HWND handle;
    private bool disposed;
    private bool trackingLeave;

    internal unsafe WidgetWindow(GameThread owner, SceneWindow scene)
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
                if (atom == 0) throw Win32Error.LastError("RegisterClassEx(widget)");
                self = GCHandle.Alloc(this);
                // hWndParent on a WS_POPUP makes it owned: above the scene, hidden/destroyed with it.
                handle = PInvoke.CreateWindowEx(WindowStyles.ForOverlay(false), className, null,
                    WindowStyles.Popup, 0, 0, 1, 1, new HWND(scene.Handle), default, module,
                    (void*)GCHandle.ToIntPtr(self));
                if (handle.IsNull) throw Win32Error.LastError("CreateWindowEx(widget)");
            }
            SetDwm(DWMWINDOWATTRIBUTE.DWMWA_TRANSITIONS_FORCEDISABLED, 1);
            SetDwm(DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND);
            SetDwm(DWMWINDOWATTRIBUTE.DWMWA_EXCLUDED_FROM_PEEK, 1);
            Surface = new LayeredSurface((nint)handle.Value);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public unsafe nint Handle { get { owner.VerifyAccess(); return (nint)handle.Value; } }
    public LayeredSurface Surface { get; private set; } = null!;

    /// <summary>True once <see cref="Show"/> was requested; the scene's Show/Hide drive the real visibility.</summary>
    public bool WantsVisible { get; private set; }
    public bool IsVisible { get; private set; }
    public PixelRect Bounds { get; private set; }

    /// <summary>Lógica de mouse (hit-test, hover, cliques; RF-23.8). Sem ela a janela só consome os cliques.</summary>
    public WidgetInteraction? Interaction { get; set; }

    /// <summary>O hover mudou (índice em <see cref="WidgetInteraction.HoverIndex"/>): o controlador redesenha.</summary>
    public event Action? HoverChanged;

    /// <summary>Clique direito dentro do widget, em coordenadas de tela (px): abrir o menu nativo (RF-23.8).</summary>
    public event Action<int, int>? ContextMenuRequested;

    /// <summary>Mostrado (quadro transparente já apresentado): o controlador desenha o conteúdo.</summary>
    public event Action? Shown;

    /// <summary>Ocultado (a superfície foi liberada): o controlador invalida o canvas.</summary>
    public event Action? Hidden;

    /// <summary>Tamanho da superfície mudou (DIB recriado no próximo quadro): redesenhar.</summary>
    public event Action? Resized;

    /// <summary>Positions and sizes the widget from a computed placement (RF-23.2). Size change resizes the surface.</summary>
    public void Apply(WidgetPlacement placement)
    {
        CheckAccess();
        var b = placement.Bounds;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(b.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(b.Height);
        var sizeChanged = b.Width != Bounds.Width || b.Height != Bounds.Height;
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER;
        if (!sizeChanged) flags |= SET_WINDOW_POS_FLAGS.SWP_NOSIZE;
        if (!PInvoke.SetWindowPos(handle, default, b.Left, b.Top, b.Width, b.Height, flags))
            throw Win32Error.LastError("SetWindowPos(widget bounds)");
        if (sizeChanged) Surface.Resize(new PixelSize(b.Width, b.Height));
        Bounds = b;
        if (sizeChanged) Resized?.Invoke();
    }

    /// <summary>Requests visibility; the widget only appears while the scene is visible.</summary>
    public void Show()
    {
        CheckAccess();
        if (Bounds.Width == 0) throw new InvalidOperationException("Apply a placement before Show.");
        WantsVisible = true;
        if (owner.Scene.IsVisible) Present();
    }

    public void Hide()
    {
        CheckAccess();
        WantsVisible = false;
        HideNative();
    }

    // Called by SceneWindow.Show/Hide so the widget follows the scene (RF-23.1 visibility).
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
            throw Win32Error.LastError("SetWindowPos(widget topmost)");
    }

    private void Present()
    {
        // A layered window only appears after its first UpdateLayeredWindow: present one empty (transparent) frame.
        Surface.BeginFrame();
        Surface.EndFrame(null);
        PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        IsVisible = true;
        Reassert();
        Shown?.Invoke();
    }

    private void HideNative()
    {
        if (handle.IsNull) return;
        PInvoke.ShowWindow(handle, SHOW_WINDOW_CMD.SW_HIDE);
        IsVisible = false;
        trackingLeave = false;
        Surface?.Release();
        Hidden?.Invoke();
    }

    private static int X(LPARAM l) => (short)(l.Value & 0xFFFF);
    private static int Y(LPARAM l) => (short)((l.Value >> 16) & 0xFFFF);
    private float Scale => Surface.Scale;

    private void ApplyCursor()
    {
        PCWSTR id = Interaction is { HoverIndex: >= 0 } ? PInvoke.IDC_HAND : PInvoke.IDC_ARROW;
        PInvoke.SetCursor(PInvoke.LoadCursor(default, id));
    }

    private void OnLeftDown(LPARAM l)
    {
        if (Interaction is null) return;
        // Pressionamento só conta uma vez; captura apenas sobre botão do nav (RF-23.8).
        if (Interaction.OnLeftDown(X(l) / Scale, Y(l) / Scale)) PInvoke.SetCapture(handle);
    }

    private void OnLeftUp(LPARAM l)
    {
        if (Interaction is null) return;
        bool captured = Interaction.PressedIndex >= 0;
        Interaction.OnLeftUp(X(l) / Scale, Y(l) / Scale);
        if (captured) PInvoke.ReleaseCapture();
    }

    private void OnRightDown(LPARAM l)
    {
        if (Interaction is null || !Interaction.OnRightDown(X(l) / Scale, Y(l) / Scale)) return;
        var point = new System.Drawing.Point(X(l), Y(l));
        PInvoke.ClientToScreen(handle, ref point);
        ContextMenuRequested?.Invoke(point.X, point.Y);
    }

    private unsafe void OnMouseMove(LPARAM l)
    {
        if (Interaction is null) return;
        if (!trackingLeave)
        {
            var track = new TRACKMOUSEEVENT
            {
                cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE,
                hwndTrack = handle,
            };
            trackingLeave = PInvoke.TrackMouseEvent(ref track);
        }
        if (!Interaction.OnMouseMove(X(l) / Scale, Y(l) / Scale)) return;
        ApplyCursor(); // WM_SETCURSOR precede o MOUSEMOVE: atualiza já ao entrar/sair do botão.
        HoverChanged?.Invoke();
    }

    private void OnMouseLeave()
    {
        trackingLeave = false;
        if (Interaction?.OnLeave() == true) HoverChanged?.Invoke();
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
        if (!handle.IsNull && !PInvoke.DestroyWindow(handle)) throw Win32Error.LastError("DestroyWindow(widget)");
        handle = default;
        IsVisible = false;
        WantsVisible = false;
        if (self.IsAllocated) self.Free();
        if (atom != 0)
        {
            fixed (char* name = ClassName)
                if (!PInvoke.UnregisterClass(name, module)) throw Win32Error.LastError("UnregisterClass(widget)");
            atom = 0;
        }
        disposed = true;
    }

    private static unsafe LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        WidgetWindow? window = null;
        try
        {
            if (message == PInvoke.WM_NCCREATE)
            {
                var data = (CREATESTRUCTW*)lParam.Value;
                WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, (nint)data->lpCreateParams);
            }
            var pointer = WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
            if (pointer != 0) window = (WidgetWindow?)GCHandle.FromIntPtr(pointer).Target;
            switch (message)
            {
                case PInvoke.WM_MOUSEACTIVATE: return new LRESULT((nint)PInvoke.MA_NOACTIVATE);
                case PInvoke.WM_NCHITTEST: return new LRESULT((nint)PInvoke.HTCLIENT);
                case PInvoke.WM_SETCURSOR:
                    window?.ApplyCursor();
                    return new LRESULT(1);
                case PInvoke.WM_LBUTTONDOWN:
                case PInvoke.WM_LBUTTONDBLCLK:
                    window?.OnLeftDown(lParam);
                    return new LRESULT(0);
                case PInvoke.WM_LBUTTONUP:
                    window?.OnLeftUp(lParam);
                    return new LRESULT(0);
                case PInvoke.WM_RBUTTONDOWN:
                case PInvoke.WM_RBUTTONDBLCLK:
                    window?.OnRightDown(lParam);
                    return new LRESULT(0);
                case PInvoke.WM_MOUSEMOVE:
                    window?.OnMouseMove(lParam);
                    return new LRESULT(0);
                case PInvoke.WM_MOUSELEAVE:
                    window?.OnMouseLeave();
                    return new LRESULT(0);
                case PInvoke.WM_CAPTURECHANGED:
                    window?.Interaction?.OnCaptureLost();
                    return new LRESULT(0);
                case PInvoke.WM_SETFOCUS:
                case PInvoke.WM_ACTIVATE:
                case PInvoke.WM_NCACTIVATE:
                    Debug.WriteLine($"WidgetWindow activation diagnostic: {message:X}");
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
