namespace SideWar.Runtime.Geometry;

/// <summary>The three SDD-24 overlay windows.</summary>
public enum OverlayKind { Panel, QuestionCard, OfflineCard }

/// <summary>Result of <see cref="PanelGeometry.Compute"/>. <see cref="Box"/> is the visible box in pixels.</summary>
public readonly record struct OverlayPlacement(PixelRect Bounds, PixelSize Box, int ShadowPx, double Scale, float ViewportDip, bool Clamped);

/// <summary>Pure panel/card geometry (SDD-24 RF-24.1, RF-24.2).</summary>
public static class PanelGeometry
{
    public const float ShadowDip = 6f;
    public const float GapAboveStripDip = 8f;
    /// <summary>ADR-034: borda direita do painel a 132 DIP da borda direita da faixa (<c>right 132</c> do Main.dc.html): <c>left = W − 532</c>.</summary>
    public const float PanelRightInsetDip = 132f;
    public const float PanelWidthDip = 400f, PanelHeightDip = 548f;
    public const float PanelChromeHeightDip = 57f; // 2 border + 53 header + 2 border
    public const float QuestionWidthDip = 520f, QuestionHeightDip = 242f;
    public const float OfflineWidthDip = 472f, OfflineHeightDip = 612f;

    public static (float W, float H) BoxDip(OverlayKind kind) => kind switch
    {
        OverlayKind.Panel => (PanelWidthDip, PanelHeightDip),
        OverlayKind.QuestionCard => (QuestionWidthDip, QuestionHeightDip),
        OverlayKind.OfflineCard => (OfflineWidthDip, OfflineHeightDip),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Places the window (visible box + 6 DIP shadow margin right/bottom). <paramref name="strip"/> is the scene
    /// strip (SceneLayout.Compute, Band host: already with the auto-hide margin): its top is <c>stripTop</c>.
    /// Not enough height: top = WorkArea.Top; the panel shrinks its viewport when taller than the work area.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">DPI is zero or the work area is empty.</exception>
    public static OverlayPlacement Compute(OverlayKind kind, MonitorInfo monitor, PixelRect strip)
    {
        if (monitor.Dpi == 0 || monitor.Dpi > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(monitor), "DPI must be positive.");
        var work = monitor.WorkArea;
        if (work.Width <= 0 || work.Height <= 0) throw new ArgumentOutOfRangeException(nameof(monitor), "WorkArea is empty.");
        var s = monitor.Dpi / 96.0;
        var (wDip, hDip) = BoxDip(kind);
        var shadow = Px(ShadowDip, s);
        var boxW = Px(wDip, s);
        var boxH = Px(hDip, s);
        var clamped = false;
        if (kind == OverlayKind.Panel && boxH + shadow > work.Height)
        {
            boxH = Math.Max(1, work.Height - shadow);
            clamped = true;
        }
        var left = kind == OverlayKind.Panel
            ? strip.Left + Px(strip.Width / s - PanelRightInsetDip - wDip, s)
            : strip.Left + Px((strip.Width / s - wDip) / 2.0, s);
        var maxLeft = work.Right - boxW - shadow;
        if (left > maxLeft) left = maxLeft;
        if (left < work.Left) left = work.Left;
        var bottom = strip.Top - Px(GapAboveStripDip, s);
        var top = bottom - boxH;
        if (top < work.Top) { top = work.Top; clamped = true; }
        var viewport = kind == OverlayKind.Panel ? (float)(boxH / s) - PanelChromeHeightDip : 0f;
        return new OverlayPlacement(new PixelRect(left, top, left + boxW + shadow, top + boxH + shadow),
            new PixelSize(boxW, boxH), shadow, s, viewport, clamped);
    }

    private static int Px(double dip, double scale) => checked((int)Math.Round(dip * scale, MidpointRounding.AwayFromZero));
}
