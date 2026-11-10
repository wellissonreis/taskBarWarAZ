namespace SideWar.Runtime.Fullscreen;

/// <summary>Tracks fullscreen windows per monitor (SDD-06 RF-06.4). Pure; re-reads the system through <see cref="IWindowProbe"/>.</summary>
public sealed class FullscreenTracker
{
    private readonly IWindowProbe probe;
    private readonly uint ownPid;
    private readonly Dictionary<string, nint> tracked = new(StringComparer.Ordinal);
    private readonly HashSet<string> monitors = new(StringComparer.Ordinal);

    public FullscreenTracker(IWindowProbe probe, uint ownPid)
    {
        this.probe = probe;
        this.ownPid = ownPid;
    }

    /// <summary>Monitors that currently hold a tracked fullscreen window.</summary>
    public IReadOnlySet<string> FullscreenMonitors => monitors;

    public bool HasTracked => tracked.Count > 0;

    /// <summary>Last foreground verdict, for diagnostics (SDD-06 RF-06.6).</summary>
    public Verdict LastForegroundVerdict { get; private set; } = Verdict.Ignore;

    public IReadOnlySet<string> Evaluate()
    {
        var foreground = probe.GetForeground();
        var fg = probe.Capture(foreground);
        var verdict = FullscreenClassifier.Classify(fg, probe.QueryQuns(), ownPid);
        LastForegroundVerdict = verdict;

        // Step 1: register the foreground; it is not re-validated against R7 this round (D3D/presentation windows may not cover).
        nint justRegistered = 0;
        if (verdict == Verdict.Fullscreen && fg.MonitorDevice is not null)
        {
            tracked[fg.MonitorDevice] = fg.Hwnd;
            justRegistered = fg.Hwnd;
        }

        // Step 2..4: re-validate the other tracked windows.
        foreach (var (monitor, hwnd) in tracked.ToArray())
        {
            if (hwnd == justRegistered) continue;
            if (!StillFullscreen(monitor, hwnd, fg, verdict)) tracked.Remove(monitor);
        }

        monitors.Clear();
        foreach (var monitor in tracked.Keys) monitors.Add(monitor);
        return monitors;
    }

    private bool StillFullscreen(string monitor, nint hwnd, in WindowSnapshot fg, Verdict fgVerdict)
    {
        if (!probe.IsWindow(hwnd)) return false;
        var w = probe.Capture(hwnd);
        if (w.Hwnd == 0 || w.IsIconic || w.IsCloaked || !w.IsVisible) return false;
        if (!FullscreenClassifier.Covers(w.Frame, w.MonitorBounds) || w.IsZoomed) return false;
        if (w.MonitorDevice != monitor) return false;

        if (fgVerdict == Verdict.Ignore) return true;
        if (fg.Hwnd == hwnd) return fgVerdict == Verdict.Fullscreen;
        // Step 3: foreground on another monitor does not disturb the tracked window.
        if (!string.Equals(fg.MonitorDevice, monitor, StringComparison.Ordinal)) return true;
        // Step 4: another window came to the front on the same monitor and is not fullscreen itself.
        if (fgVerdict == Verdict.NotFullscreen && probe.IsAbove(fg.Hwnd, hwnd)) return false;
        return true;
    }
}
