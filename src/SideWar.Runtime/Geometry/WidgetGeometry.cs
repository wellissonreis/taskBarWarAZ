namespace SideWar.Runtime.Geometry;

/// <summary>Where the widget goes (window bounds in screen px).</summary>
public readonly record struct WidgetPlacement(PixelRect Bounds);

/// <summary>Pure widget geometry (SDD-23 RF-23.2, ADR-034): compact, right side, top of the scene, no fallback.</summary>
public static class WidgetGeometry
{
    public const float HeightDip = 48f;
    public const float CanvasRightOffsetDip = 548f;
    public const float RightMarginDip = 8f;

    /// <summary>Largura nominal (≈ sem chip ×3) para quem ainda não mediu o layout; o real vem de <c>WidgetLayout.WidthDip</c>.</summary>
    public const float NominalWidthDip = 480f;

    /// <summary>
    /// <c>left = min(W − 548, W − 8 − largura)</c> relative to the scene, <c>top</c> = scene top, 48 DIP tall;
    /// size <c>round(largura × dpi/96)</c> × <c>round(48 × dpi/96)</c>, rounded once.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">DPI is zero/huge, width not positive, or the widget does not fit the scene.</exception>
    public static WidgetPlacement Compute(MonitorInfo monitor, PixelRect scene, float widthDip)
    {
        if (monitor.Dpi == 0 || monitor.Dpi > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(monitor), "DPI must be positive.");
        if (!float.IsFinite(widthDip) || widthDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthDip), "Width must be positive.");
        var scale = monitor.Dpi / 96.0;
        var w = Px(widthDip, scale);
        var h = Px(HeightDip, scale);
        var sceneW = scene.Width;
        var rel = Math.Min(sceneW - Px(CanvasRightOffsetDip, scale), sceneW - Px(RightMarginDip, scale) - w);
        if (rel < 0 || h > scene.Height)
            throw new ArgumentOutOfRangeException(nameof(scene), "Widget does not fit the scene.");
        var left = checked(scene.Left + rel);
        return new(new PixelRect(left, scene.Top, checked(left + w), checked(scene.Top + h)));
    }

    private static int Px(double dip, double scale) => checked((int)Math.Round(dip * scale, MidpointRounding.AwayFromZero));
}
