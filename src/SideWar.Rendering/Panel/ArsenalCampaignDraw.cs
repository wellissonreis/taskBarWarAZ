using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Runtime.Geometry;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>Recursos de pintura das abas (T-Game): fontes, atlas pré-escalado e DPI. O dono mantém e descarta cada um.</summary>
public sealed record ArsenalCampaignPaintContext(Typography Fonts, ScaledSpriteCache Sprites, int Dpi)
{
    public float Scale => Dpi / 96f;
}

/// <summary>
/// Primitivas de desenho em pixel físico inteiro (sem AA nas caixas; Silkscreen com Alias). As coordenadas de entrada
/// são DIP de conteúdo; <see cref="Map"/> traduz para a caixa do painel e aplica a rolagem. Paints estáticos: só T-Game.
/// </summary>
internal static class ArsenalCampaignDraw
{
    private static readonly SKPaint Fill = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private static readonly SKPaint Aa = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private static readonly SKPaint Layer = new();
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest);
    private static readonly Dictionary<char, string> OneChar = [];

    /// <summary>Estrela de 5 pontas, centrada em (0,0), diâmetro 1 (DP-20.2: Silkscreen/JetBrains não têm ★☆).</summary>
    private static readonly SKPath Star = SKPath.ParseSvgPathData("M0.0000 -0.5000L0.1176 -0.1618L0.4755 -0.1545L0.1902 0.0618L0.2939 0.4045L0.0000 0.2000L-0.2939 0.4045L-0.1902 0.0618L-0.4755 -0.1545L-0.1176 -0.1618Z")!;

    /// <summary>Marca de seleção da caixa do toggle, em coordenadas de uma caixa 20×20 (DP-24.6).</summary>
    private static readonly SKPath Check = SKPath.ParseSvgPathData("M5 10.5L8.5 14L15 6.5")!;

    public static float Px(float dip, float s) => MathF.Round(dip * s);

    /// <summary>Origem do corpo do painel em DIP: a caixa + borda 2, e topo do viewport em 55.</summary>
    public static (float X, float Y) Map(float contentX, float contentY, float offset) =>
        (ArsenalLayoutLeft + contentX, PanelScroll.ViewportTopDip + contentY - offset);

    private const float ArsenalLayoutLeft = 2f;

    public static SKRect Rect(DipRect r, float s, float offset)
    {
        var (x, y) = Map(r.X, r.Y, offset);
        return new SKRect(Px(x, s), Px(y, s), Px(x + r.W, s), Px(y + r.H, s));
    }

    public static void Box(SKCanvas c, DipRect r, float s, float offset, SKColor fill, SKColor border, float borderDip = 1f)
    {
        var o = Rect(r, s, offset);
        float b = MathF.Max(1f, MathF.Round(borderDip * s));
        Fill.Color = border;
        c.DrawRect(o, Fill);
        Fill.Color = fill;
        c.DrawRect(new SKRect(o.Left + b, o.Top + b, o.Right - b, o.Bottom - b), Fill);
    }

    public static void Solid(SKCanvas c, DipRect r, float s, float offset, SKColor color)
    {
        Fill.Color = color;
        c.DrawRect(Rect(r, s, offset), Fill);
    }

    public static void SolidPx(SKCanvas c, SKRect r, SKColor color)
    {
        Fill.Color = color;
        c.DrawRect(r, Fill);
    }

    /// <summary>Abre uma camada com opacidade (linha inteira a 0,45 / 0,55); feche com <c>Restore</c>.</summary>
    public static void BeginOpacity(SKCanvas c, float opacity)
    {
        if (opacity >= 1f) { c.Save(); return; }
        Layer.Color = Theme.Palette.TxtBranco.WithAlpha((byte)MathF.Round(opacity * 255f));
        c.SaveLayer(Layer);
    }

    /// <summary>Largura em px de <paramref name="text"/> com letter-spacing em DIP.</summary>
    public static float TextWidthPx(SKFont font, string text, float letterSpacingDip, float s) =>
        font.MeasureText(text) + MathF.Round(letterSpacingDip * s) * text.Length;

    /// <summary>Texto com a linha de base dada (px); retorna a largura desenhada em px. Letter-spacing por caractere (sem alocação: cache de strings de 1 char).</summary>
    public static float TextBaseline(SKCanvas c, string text, SKFont font, float xPx, float baselinePx, SKColor color, float letterSpacingDip, float s)
    {
        Aa.Color = color;
        Aa.IsAntialias = font.Edging != SKFontEdging.Alias;
        float x = MathF.Round(xPx);
        float y = MathF.Round(baselinePx);
        if (letterSpacingDip == 0f)
        {
            c.DrawText(text, x, y, SKTextAlign.Left, font, Aa);
            return font.MeasureText(text);
        }
        float ls = MathF.Round(letterSpacingDip * s);
        float start = x;
        foreach (char ch in text)
        {
            if (!OneChar.TryGetValue(ch, out var str)) OneChar[ch] = str = ch.ToString();
            c.DrawText(str, x, y, SKTextAlign.Left, font, Aa);
            x += font.MeasureText(str) + ls;
        }
        return x - start;
    }

    /// <summary>Texto cujo topo de caixa de linha é <paramref name="topDip"/> (ascent da fonte aplicado).</summary>
    public static float TextTop(SKCanvas c, string text, SKFont font, float xDip, float topDip, float s, float offset, SKColor color, float letterSpacingDip = 0f)
    {
        var (x, y) = Map(xDip, topDip, offset);
        return TextBaseline(c, text, font, Px(x, s), Px(y, s) - font.Metrics.Ascent, color, letterSpacingDip, s);
    }

    /// <summary>Altura de linha em px da fonte física.</summary>
    public static float LinePx(SKFont font) { var m = font.Metrics; return m.Descent - m.Ascent + m.Leading; }

    public static void DrawStar(SKCanvas c, float cxPx, float cyPx, float diameterPx, SKColor color, bool filled, float strokePx)
    {
        c.Save();
        c.Translate(cxPx, cyPx);
        c.Scale(diameterPx, diameterPx);
        Aa.Color = color;
        if (filled) { Aa.Style = SKPaintStyle.Fill; }
        else { Aa.Style = SKPaintStyle.Stroke; Aa.StrokeWidth = strokePx / diameterPx; Aa.StrokeJoin = SKStrokeJoin.Miter; }
        c.DrawPath(Star, Aa);
        Aa.Style = SKPaintStyle.Fill;
        c.Restore();
    }

    public static void DrawCheck(SKCanvas c, SKRect box, float s, SKColor color)
    {
        c.Save();
        c.Translate(box.Left, box.Top);
        c.Scale(box.Width / 20f, box.Height / 20f);
        Aa.Color = color;
        Aa.Style = SKPaintStyle.Stroke;
        Aa.StrokeWidth = 2f;
        Aa.StrokeCap = SKStrokeCap.Butt;
        Aa.StrokeJoin = SKStrokeJoin.Miter;
        c.DrawPath(Check, Aa);
        Aa.Style = SKPaintStyle.Fill;
        c.Restore();
    }

    /// <summary>Sprite do atlas pré-escalado centrado em <paramref name="slotPx"/>, <c>object-fit: contain</c> em <paramref name="boxDip"/>, Nearest.</summary>
    public static void DrawSprite(SKCanvas c, ScaledSpriteCache cache, SpriteId id, SKRect slotPx, float boxDip, float s)
    {
        if (cache.Image is not { } image || !cache.TryGet(id, out var src, out _)) return;
        float box = Px(boxDip, s);
        float k = MathF.Min(box / src.Width, box / src.Height);
        float w = MathF.Round(src.Width * k), h = MathF.Round(src.Height * k);
        float x = MathF.Round(slotPx.MidX - w / 2f), y = MathF.Round(slotPx.MidY - h / 2f);
        c.DrawImage(image, SKRect.Create(src.Left, src.Top, src.Width, src.Height), SKRect.Create(x, y, w, h), Nearest, Fill);
    }

    public static SpriteId SpriteFor(SideWar.Core.Simulation.TowerType type, bool locked) =>
        locked ? SpriteId.SlotEmpty : type switch
        {
            SideWar.Core.Simulation.TowerType.Frost => SpriteId.TowerFrost,
            SideWar.Core.Simulation.TowerType.Cannon => SpriteId.TowerCannon,
            SideWar.Core.Simulation.TowerType.ScaleSet => SpriteId.TowerScaleSet,
            _ => SpriteId.TowerBolt,
        };
}
