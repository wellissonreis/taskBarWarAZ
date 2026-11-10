using System.Runtime.InteropServices;
using SideWar.Runtime.Fullscreen;
using SideWar.Runtime.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Fullscreen;

/// <summary>Reads <see cref="WindowSnapshot"/> and z-order from Win32 (SDD-06 RF-06.2). Used only on T-Game.</summary>
public sealed class WindowProbe : IWindowProbe
{
    private const int MaxZOrderWalk = 4096;
    private readonly Dictionary<nint, string> classCache = new();

    public unsafe nint GetForeground() => (nint)PInvoke.GetForegroundWindow().Value;

    public unsafe bool IsWindow(nint hwnd) => hwnd != 0 && PInvoke.IsWindow(new HWND((void*)hwnd));

    public Quns QueryQuns() => QunsReader.Read();

    public unsafe WindowSnapshot Capture(nint hwnd)
    {
        var h = new HWND((void*)hwnd);
        if (hwnd == 0 || !PInvoke.IsWindow(h)) return default;

        PInvoke.GetWindowThreadProcessId(h, out uint pid);
        if (!classCache.TryGetValue(hwnd, out var cls))
        {
            if (classCache.Count > 256) classCache.Clear();
            cls = ReadClassName(h);
            classCache[hwnd] = cls;
        }

        // GetWindowLongPtr can legitimately fail if the window dies between calls; treat as gone.
        uint style, exStyle;
        try
        {
            style = (uint)Interop.WindowLong.Get(h, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
            exStyle = (uint)Interop.WindowLong.Get(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        }
        catch (System.ComponentModel.Win32Exception) { return default; }

        uint cloaked = 0;
        PInvoke.DwmGetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint));

        RECT frame;
        if (PInvoke.DwmGetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &frame, (uint)sizeof(RECT)).Failed)
            if (!PInvoke.GetWindowRect(h, out frame)) return default;

        string device = string.Empty;
        PixelRect bounds = default, work = default;
        var monitor = PInvoke.MonitorFromWindow(h, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (!monitor.IsNull)
        {
            var info = new MONITORINFOEXW { monitorInfo = { cbSize = (uint)sizeof(MONITORINFOEXW) } };
            if (PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            {
                device = info.szDevice.ToString();
                bounds = ToRect(info.monitorInfo.rcMonitor);
                work = ToRect(info.monitorInfo.rcWork);
            }
        }

        return new WindowSnapshot(hwnd, pid, cls, style, exStyle,
            PInvoke.IsZoomed(h), PInvoke.IsIconic(h), PInvoke.IsWindowVisible(h), cloaked != 0,
            ToRect(frame), device, bounds, work);
    }

    public unsafe bool IsAbove(nint upper, nint lower)
    {
        var current = new HWND((void*)upper);
        for (int i = 0; i < MaxZOrderWalk && !current.IsNull; i++)
        {
            current = PInvoke.GetWindow(current, GET_WINDOW_CMD.GW_HWNDNEXT);
            if ((nint)current.Value == lower) return true;
        }
        return false;
    }

    private static unsafe string ReadClassName(HWND h)
    {
        Span<char> buffer = stackalloc char[64];
        fixed (char* p = buffer)
        {
            int n = PInvoke.GetClassName(h, p, buffer.Length);
            return n > 0 ? new string(p, 0, n) : string.Empty;
        }
    }

    private static PixelRect ToRect(RECT r) => new(r.left, r.top, r.right, r.bottom);
}
