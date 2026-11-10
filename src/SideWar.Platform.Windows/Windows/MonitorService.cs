using System.Runtime.InteropServices;
using SideWar.Runtime.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace SideWar.Platform.Windows.Windows;

/// <summary>Monitor enumeration with physical rects, work area and effective DPI (SDD-05 RF-05.2).</summary>
public static class MonitorService
{
    private const uint PrimaryFlag = 1; // MONITORINFOF_PRIMARY

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2, i.e. (HANDLE)-4 (no generated constant).</summary>
    internal static readonly DPI_AWARENESS_CONTEXT PerMonitorV2 = new(-4);

    public static unsafe IReadOnlyList<MonitorInfo> Enumerate()
    {
        var result = new List<MonitorInfo>();
        MONITORENUMPROC callback = (HMONITOR monitor, HDC _, RECT* _, LPARAM _) =>
        {
            var info = new MONITORINFOEXW { monitorInfo = { cbSize = (uint)sizeof(MONITORINFOEXW) } };
            if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info)) return true;
            uint dpi = 96;
            if (PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded && dpiX != 0)
                dpi = dpiX;
            var m = info.monitorInfo;
            result.Add(new MonitorInfo(info.szDevice.ToString(),
                new PixelRect(m.rcMonitor.left, m.rcMonitor.top, m.rcMonitor.right, m.rcMonitor.bottom),
                new PixelRect(m.rcWork.left, m.rcWork.top, m.rcWork.right, m.rcWork.bottom),
                dpi, (m.dwFlags & PrimaryFlag) != 0, (nint)monitor.Value));
            return true;
        };
        if (!PInvoke.EnumDisplayMonitors(default, null, callback, default))
            throw new InvalidOperationException("EnumDisplayMonitors failed: " + Marshal.GetLastWin32Error());
        GC.KeepAlive(callback);
        return result;
    }
}
