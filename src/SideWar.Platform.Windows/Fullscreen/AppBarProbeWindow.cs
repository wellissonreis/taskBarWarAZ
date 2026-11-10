using System.Diagnostics;
using System.Runtime.InteropServices;
using SideWar.Platform.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Fullscreen;

/// <summary>
/// Hidden top-level probe registered with ABM_NEW so the shell reports ABN_FULLSCREENAPP (SDD-06 RF-06.1 item 4).
/// The signal only schedules a re-evaluation; it is never treated as truth. Must be created/disposed on T-Game.
/// </summary>
internal sealed class AppBarProbeWindow : IDisposable
{
    internal const uint CallbackMessage = PInvoke.WM_APP + 0x60;
    private const string ClassName = "SideWar.AppBarProbe";
    private static readonly WNDPROC Procedure = WindowProcedure;
    private readonly Action onFullscreenApp;
    private readonly Action<Exception> report;
    private readonly HINSTANCE module;
    private GCHandle self;
    private ushort atom;
    private HWND handle;
    private bool registered;

    internal unsafe AppBarProbeWindow(Action onFullscreenApp, Action<Exception> report)
    {
        this.onFullscreenApp = onFullscreenApp;
        this.report = report;
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
                    lpszClassName = className,
                };
                atom = PInvoke.RegisterClassEx(cls);
                if (atom == 0) throw Win32Error.LastError("RegisterClassEx(AppBarProbe)");
                self = GCHandle.Alloc(this);
                // Top-level, never shown (no WS_VISIBLE), tool window so it stays out of Alt+Tab.
                handle = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
                    className, null, WINDOW_STYLE.WS_POPUP, 0, 0, 0, 0, default, default, module,
                    (void*)GCHandle.ToIntPtr(self));
                if (handle.IsNull) throw Win32Error.LastError("CreateWindowEx(AppBarProbe)");
            }
            var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = handle, uCallbackMessage = CallbackMessage };
            // Registration can fail on locked-down shells: polling still covers detection, so this is not fatal.
            registered = PInvoke.SHAppBarMessage(PInvoke.ABM_NEW, ref data) != 0;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal bool IsRegistered => registered;
    internal unsafe nint Handle => (nint)handle.Value;

    public unsafe void Dispose()
    {
        if (registered)
        {
            var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = handle };
            PInvoke.SHAppBarMessage(PInvoke.ABM_REMOVE, ref data);
            registered = false;
        }
        if (!handle.IsNull)
        {
            PInvoke.DestroyWindow(handle);
            handle = default;
        }
        if (self.IsAllocated) self.Free();
        if (atom != 0)
        {
            fixed (char* name = ClassName) PInvoke.UnregisterClass(name, module);
            atom = 0;
        }
    }

    private static unsafe LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        AppBarProbeWindow? window = null;
        try
        {
            if (message == PInvoke.WM_NCCREATE)
            {
                var data = (CREATESTRUCTW*)lParam.Value;
                WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, (nint)data->lpCreateParams);
            }
            var pointer = WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
            if (pointer != 0) window = (AppBarProbeWindow?)GCHandle.FromIntPtr(pointer).Target;
            if (message == CallbackMessage && wParam.Value == PInvoke.ABN_FULLSCREENAPP) window?.onFullscreenApp();
            else if (message == PInvoke.WM_NCDESTROY) WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, 0);
        }
        catch (Exception error)
        {
            if (window is not null) window.report(error);
            else Trace.WriteLine(error);
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
