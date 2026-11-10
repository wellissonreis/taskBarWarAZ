using SkiaSharp;

namespace SideWar.Rendering.Theme;

/// <summary>Razao de contraste WCAG 2.x (SDD-20 RF-20.8). Pura; ignora alfa.</summary>
public static class Contrast
{
    public const double MinText = 4.5;
    public const double MinGraphic = 3.0;

    public static double Luminance(SKColor c) =>
        0.2126 * Linear(c.Red) + 0.7152 * Linear(c.Green) + 0.0722 * Linear(c.Blue);

    public static double Ratio(SKColor a, SKColor b)
    {
        double la = Luminance(a), lb = Luminance(b);
        double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Linear(byte v)
    {
        double s = v / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
