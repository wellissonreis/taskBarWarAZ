using SideWar.Rendering.Text;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>Alinhamento horizontal do texto dentro da caixa.</summary>
internal enum TextAlign { Left, Center, Right }

/// <summary>
/// Primitivas de desenho em pixel físico para os renderers de T5.5 (sem AA nos retângulos, Silkscreen em Alias).
/// Coordenadas de entrada em DIP; <see cref="Px"/> arredonda uma vez. Paints pré-criados.
/// </summary>
internal sealed class PanelPainter(Typography typography, int dpi) : IDisposable
{
    private readonly SKPaint fill = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint text = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly TextRunCache runs = new();

    public int Dpi => dpi;
    public float Scale => dpi / 96f;
    public float OffsetY { get; set; }

    /// <summary>Falso: layout idêntico, mas nenhum glifo é desenhado (goldens "-notext", comparáveis entre SOs).</summary>
    public bool TextEnabled { get; set; } = true;

    public float Px(float dip) => (float)Math.Round(dip * dpi / 96.0, MidpointRounding.AwayFromZero);

    public SKRect Rect(DipRect r) => Rect(r.X, r.Y, r.W, r.H);

    public SKRect Rect(float x, float y, float w, float h)
    {
        var l = Px(x);
        var t = Px(y - OffsetY);
        return new SKRect(l, t, Px(x + w), Px(y - OffsetY + h));
    }

    public void Fill(SKCanvas c, SKRect r, SKColor color)
    {
        fill.Color = color;
        c.DrawRect(r, fill);
    }

    public void Fill(SKCanvas c, DipRect r, SKColor color) => Fill(c, Rect(r), color);

    /// <summary>Retângulo com borda de <paramref name="borderDip"/> DIP e miolo <paramref name="inner"/>.</summary>
    public void Box(SKCanvas c, DipRect r, SKColor border, SKColor inner, float borderDip = 1f)
    {
        var o = Rect(r);
        Fill(c, o, border);
        var b = Px(borderDip);
        Fill(c, new SKRect(o.Left + b, o.Top + b, o.Right - b, o.Bottom - b), inner);
    }

    /// <summary>Sombra dura do corpo <paramref name="body"/> (px) com o paint deste painter (sem alocação).</summary>
    public void Shadow(SKCanvas c, SKRect body, SKColor color) => Theme.HardShadow.Draw(c, body, color, dpi, fill);

    public SKFont Font(PanelFont face, float sizeDip) => typography.GetFont(PanelTextMetricsSkia.Role(face), sizeDip, dpi);

    /// <summary>Texto numa caixa de linha (topo = <c>box.Y</c>); baseline = topo + ascendente físico.</summary>
    public void Text(SKCanvas c, string s, PanelFont face, float sizeDip, DipRect box, SKColor color,
        TextAlign align = TextAlign.Left, float letterSpacing = 0f)
    {
        if (!TextEnabled) return;
        var font = Font(face, sizeDip);
        text.Color = color;
        var ls = letterSpacing * Scale;
        var width = runs.Measure(s, font) + ls * s.Length - (s.Length > 0 ? ls : 0f);
        var left = Px(box.X);
        var boxW = Px(box.X + box.W) - left;
        var x = align switch
        {
            TextAlign.Center => left + (float)Math.Round((boxW - width) / 2f),
            TextAlign.Right => left + boxW - width,
            _ => left,
        };
        var y = Px(box.Y - OffsetY) + (float)Math.Round(-font.Metrics.Ascent);
        if (ls == 0f)
        {
            c.DrawText(runs.Get(s, font).Blob, x, y, text);
            return;
        }
        foreach (var ch in s)
        {
            var g = runs.Get(runs.Char(ch), font);
            c.DrawText(g.Blob, x, y, text);
            x += g.Width + ls;
        }
    }

    public void Dispose()
    {
        fill.Dispose();
        text.Dispose();
        runs.Dispose();
    }
}
