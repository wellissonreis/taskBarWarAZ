using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Diagnostics;

/// <summary>Process-wide memory and handle counters for the benchmark CSV (SDD-16 RF-16.2). Read-only, off T-Game.</summary>
public static class ProcessMetrics
{
    public readonly record struct Snapshot(double PrivateMB, double WorkingSetMB, int Handles, int GdiObjects, int UserObjects);

    private const double MB = 1024.0 * 1024.0;

    public static Snapshot Read()
    {
        using var process = Process.GetCurrentProcess();
        var handle = PInvoke.GetCurrentProcess();
        int gdi = (int)PInvoke.GetGuiResources(handle, GET_GUI_RESOURCES_FLAGS.GR_GDIOBJECTS);
        int user = (int)PInvoke.GetGuiResources(handle, GET_GUI_RESOURCES_FLAGS.GR_USEROBJECTS);
        return new Snapshot(process.PrivateMemorySize64 / MB, process.WorkingSet64 / MB, process.HandleCount, gdi, user);
    }
}
