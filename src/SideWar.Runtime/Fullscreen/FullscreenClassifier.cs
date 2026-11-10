using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Fullscreen;

/// <summary>Pure classifier, SDD-06 RF-06.3 rules R1..R12 in order.</summary>
public static class FullscreenClassifier
{
    public const uint WsCaption = 0x00C00000;
    public const uint WsPopup = 0x80000000;
    public const int CoverTolerancePx = 1;

    public static Verdict Classify(in WindowSnapshot w, Quns q, uint ownPid)
    {
        if (w.Hwnd == 0) return Verdict.NotFullscreen;                         // R1
        if (w.ProcessId == ownPid) return Verdict.Ignore;                      // R2
        if (w.IsCloaked || !w.IsVisible) return Verdict.Ignore;                // R3
        if (w.IsIconic) return Verdict.NotFullscreen;                          // R4
        var cls = w.ClassName ?? string.Empty;
        if (ShellClasses.ShellTransient.Contains(cls)) return Verdict.Ignore;  // R5
        if (ShellClasses.Desktop.Contains(cls)) return Verdict.NotFullscreen;  // R6
        var covers = Covers(w.Frame, w.MonitorBounds);                         // R7
        if (covers && !w.IsZoomed && (w.Style & WsCaption) != WsCaption)      // R8
            return Verdict.Fullscreen;
        if (covers && !w.IsZoomed && (w.Style & WsPopup) != 0)                 // R9
            return Verdict.Fullscreen;
        if (q is Quns.D3DFullscreen or Quns.PresentationMode)                  // R10
            return Verdict.Fullscreen;
        return Verdict.NotFullscreen;                                          // R11, R12
    }

    /// <summary>R7: frame contains the monitor bounds, 1 px tolerance per side. Also used by the tracker (RF-06.4).</summary>
    public static bool Covers(PixelRect frame, PixelRect monitor) =>
        (long)frame.Left <= (long)monitor.Left + CoverTolerancePx &&
        (long)frame.Top <= (long)monitor.Top + CoverTolerancePx &&
        (long)frame.Right >= (long)monitor.Right - CoverTolerancePx &&
        (long)frame.Bottom >= (long)monitor.Bottom - CoverTolerancePx;
}
