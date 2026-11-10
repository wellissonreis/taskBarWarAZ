using System.Runtime.InteropServices;
using SideWar.Runtime;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Shell;

namespace SideWar.Platform.Windows.Taskbar;

/// <summary>SDD-21 RF-21.2: synchronous and cheap, safe on T-Game. Never touches UIA.</summary>
public sealed class TaskbarLocator : ITaskbarLocator
{
    public const string PrimaryClass = "Shell_TrayWnd";
    public const string SecondaryClass = "Shell_SecondaryTrayWnd";

    public sealed record Bar(nint Hwnd, PixelRect Rect, MonitorInfo Monitor, bool IsPrimary);

    /// <summary>Primary bar first, then every secondary bar.</summary>
    public static IReadOnlyList<Bar> FindBars()
    {
        var bars = new List<Bar>();
        var primary = PInvoke.FindWindow(PrimaryClass, null);
        if (!primary.IsNull) Add(primary, true);
        HWND previous = HWND.Null;
        while (true)
        {
            var next = PInvoke.FindWindowEx(HWND.Null, previous, SecondaryClass, null);
            if (next.IsNull) break;
            Add(next, false);
            previous = next;
        }
        return bars;

        void Add(HWND hwnd, bool isPrimary)
        {
            if (!PInvoke.GetWindowRect(hwnd, out var r)) return;
            var monitor = MonitorSnapshot.FromWindow(hwnd);
            if (monitor is { } m) bars.Add(new(hwnd, new(r.left, r.top, r.right, r.bottom), m, isPrimary));
        }
    }

    public TaskbarTarget? Locate(MonitorInfo monitor)
    {
        foreach (var bar in FindBars())
            if (bar.Monitor.Handle == monitor.Handle)
                return Describe(bar, monitor);
        return null; // RF-21.2 item 5: the game monitor has no taskbar
    }

    public static TaskbarTarget Describe(Bar bar, MonitorInfo monitor)
    {
        var edge = bar.IsPrimary ? PrimaryEdge(bar.Hwnd) ?? TaskbarEdges.Infer(bar.Rect, monitor.Bounds)
            : TaskbarEdges.Infer(bar.Rect, monitor.Bounds);
        return new(bar.Hwnd, new(monitor, bar.Rect, edge, IsAutoHide()));
    }

    public static bool IsAutoHide()
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        return ((uint)PInvoke.SHAppBarMessage(PInvoke.ABM_GETSTATE, ref data) & PInvoke.ABS_AUTOHIDE) != 0;
    }

    private static TaskbarEdge? PrimaryEdge(nint hwnd)
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = (HWND)hwnd };
        if (PInvoke.SHAppBarMessage(PInvoke.ABM_GETTASKBARPOS, ref data) == 0) return null;
        return data.uEdge switch
        {
            PInvoke.ABE_BOTTOM => TaskbarEdge.Bottom,
            PInvoke.ABE_TOP => TaskbarEdge.Top,
            PInvoke.ABE_LEFT => TaskbarEdge.Left,
            PInvoke.ABE_RIGHT => TaskbarEdge.Right,
            _ => TaskbarEdge.Unknown,
        };
    }
}

/// <summary>Minimal MonitorInfo builder for the taskbar module; SDD-05 owns the full monitor model.</summary>
internal static class MonitorSnapshot
{
    public static MonitorInfo? FromWindow(HWND hwnd)
    {
        var hmon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (hmon.IsNull) return null;
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>();
        if (!PInvoke.GetMonitorInfo(hmon, ref info.monitorInfo)) return null;
        uint dpi = 96;
        if (PInvoke.GetDpiForMonitor(hmon, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var x, out _).Succeeded && x != 0) dpi = x;
        var b = info.monitorInfo.rcMonitor;
        var w = info.monitorInfo.rcWork;
        return new(info.szDevice.ToString(), new(b.left, b.top, b.right, b.bottom),
            new(w.left, w.top, w.right, w.bottom), dpi, (info.monitorInfo.dwFlags & 1) != 0, hmon);
    }
}
