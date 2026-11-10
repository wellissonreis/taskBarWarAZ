using SkiaSharp;

namespace SideWar.Rendering.Theme;

/// <summary>
/// Sombra dura (SDD-20 RF-20.4): retangulo preenchido deslocado (+6,+6) DIP,
/// sem blur, sem SKImageFilter e sem SKMaskFilter. Chamar antes do corpo.
/// Uso restrito a thread de render (paint estatico reutilizado, zero alocacao).
/// </summary>
public static class HardShadow
{
    public const int OffsetDip = 6;

    private static readonly SKPaint Paint = new() { IsAntialias = false, Style = SKPaintStyle.Fill };

    /// <summary>Deslocamento fisico: round(6 x dpi/96), meio para cima.</summary>
    public static int OffsetPx(int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        return (int)Math.Round(OffsetDip * dpi / 96.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Retangulo da sombra para um corpo em pixels fisicos.</summary>
    public static SKRect ShadowRect(SKRect body, int dpi)
    {
        float o = OffsetPx(dpi);
        return new SKRect(body.Left + o, body.Top + o, body.Right + o, body.Bottom + o);
    }

    public static void Draw(SKCanvas canvas, SKRect body, SKColor color, int dpi)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        Paint.Color = color;
        canvas.DrawRect(ShadowRect(body, dpi), Paint);
    }
}
