using SideWar.Rendering.Theme;
using SideWar.Runtime.Td;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>
/// Ícones do HUD TD (TD.14) desenhados por código (vetoriais, sem sprites): coração, moeda, onda, play, avançar, construir, menu,
/// minimizar, restaurar, cadeado e as três habilidades (Escalar, Firewall, Snapshot). Cada ícone cabe num quadrado de lado
/// <c>size</c> pixels com origem em (<c>x</c>, <c>y</c>).
/// </summary>
internal static class TdIconPainter
{
    public static void Draw(SKCanvas c, TdIcon icon, float x, float y, float size, SKColor color, SKColor accent)
    {
        if (icon == TdIcon.None || size < 2f) return;
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = color };
        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = Math.Max(1f, size / 10f), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        float u = size / 16f;
        SKPoint P(float px, float py) => new(x + px * u, y + py * u);
        switch (icon)
        {
            case TdIcon.Heart:
            {
                using var b = new SKPathBuilder();
                b.MoveTo(P(8, 14));
                b.CubicTo(P(1, 9), P(0, 5), P(3, 2.5f));
                b.CubicTo(P(5.5f, 1), P(7.5f, 2.5f), P(8, 4));
                b.CubicTo(P(8.5f, 2.5f), P(10.5f, 1), P(13, 2.5f));
                b.CubicTo(P(16, 5), P(15, 9), P(8, 14));
                b.Close();
                using var path = b.Detach();
                c.DrawPath(path, fill);
                break;
            }
            case TdIcon.Coin:
            {
                fill.Color = accent;
                c.DrawCircle(P(8, 8), 7f * u, fill);
                line.Color = color.WithAlpha(255);
                line.StrokeWidth = Math.Max(1f, u * 1.4f);
                c.DrawCircle(P(8, 8), 5f * u, line);
                c.DrawLine(P(8, 5), P(8, 11), line);
                break;
            }
            case TdIcon.Wave:
            {
                using var b = new SKPathBuilder();
                b.MoveTo(P(1, 5));
                b.CubicTo(P(3, 2), P(5, 2), P(7, 5)); b.CubicTo(P(9, 8), P(11, 8), P(15, 5));
                b.MoveTo(P(1, 11));
                b.CubicTo(P(3, 8), P(5, 8), P(7, 11)); b.CubicTo(P(9, 14), P(11, 14), P(15, 11));
                using var path = b.Detach();
                c.DrawPath(path, line);
                break;
            }
            case TdIcon.Play: Poly(c, fill, P(4, 2), P(14, 8), P(4, 14)); break;
            case TdIcon.FastForward: Poly(c, fill, P(1, 3), P(8, 8), P(1, 13)); Poly(c, fill, P(8, 3), P(15, 8), P(8, 13)); break;
            case TdIcon.Build:
                c.DrawLine(P(8, 3), P(8, 13), line); c.DrawLine(P(3, 8), P(13, 8), line); break;
            case TdIcon.Menu:
                c.DrawLine(P(2, 4), P(14, 4), line); c.DrawLine(P(2, 8), P(14, 8), line); c.DrawLine(P(2, 12), P(14, 12), line); break;
            case TdIcon.Minus: c.DrawLine(P(3, 12), P(13, 12), line); break;
            case TdIcon.Restore:
                line.Style = SKPaintStyle.Stroke;
                c.DrawRect(SKRect.Create(P(3, 3).X, P(3, 3).Y, 10 * u, 10 * u), line); break;
            case TdIcon.Lock:
            {
                c.DrawRect(SKRect.Create(P(3, 7).X, P(3, 7).Y, 10 * u, 7 * u), fill);
                using var b = new SKPathBuilder();
                b.MoveTo(P(5, 7)); b.LineTo(P(5, 5)); b.CubicTo(P(5, 1), P(11, 1), P(11, 5)); b.LineTo(P(11, 7));
                using var path = b.Detach();
                c.DrawPath(path, line);
                break;
            }
            case TdIcon.Scale: // duas setas para cima: mais cadência
                Poly(c, fill, P(8, 1), P(14, 7), P(2, 7)); Poly(c, fill, P(8, 8), P(14, 14), P(2, 14)); break;
            case TdIcon.Firewall: // muro de tijolos
                c.DrawRect(SKRect.Create(P(1, 2).X, P(1, 2).Y, 14 * u, 12 * u), line);
                c.DrawLine(P(1, 6), P(15, 6), line); c.DrawLine(P(1, 10), P(15, 10), line);
                c.DrawLine(P(8, 2), P(8, 6), line); c.DrawLine(P(5, 6), P(5, 10), line); c.DrawLine(P(11, 6), P(11, 10), line); c.DrawLine(P(8, 10), P(8, 14), line);
                break;
            case TdIcon.Snapshot: // seta circular (restaurar)
            {
                var oval = SKRect.Create(P(2, 2).X, P(2, 2).Y, 12 * u, 12 * u);
                using var b = new SKPathBuilder();
                b.AddArc(oval, -60f, 290f);
                using var path = b.Detach();
                c.DrawPath(path, line);
                Poly(c, fill, P(11, 0.5f), P(15, 5), P(8.5f, 5));
                break;
            }
        }
    }

    private static void Poly(SKCanvas c, SKPaint paint, params SKPoint[] pts)
    {
        using var b = new SKPathBuilder();
        b.MoveTo(pts[0]);
        for (int i = 1; i < pts.Length; i++) b.LineTo(pts[i]);
        b.Close();
        using var path = b.Detach();
        c.DrawPath(path, paint);
    }
}
