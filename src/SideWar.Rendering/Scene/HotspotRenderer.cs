using SideWar.Rendering.Animation;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>Variante do banner (SDD-22 RF-22.8).</summary>
public enum BannerVariant
{
    Peak,
    Boss,
}

/// <summary>
/// Visual das janelas hotspot (banners de pico/chefe com adPulse, caixa de suprimentos com adBlink) e do selo de
/// modo ausente (SDD-22 RF-22.7 e RF-22.8). Cada Draw* limpa o canvas (superfície da própria janela) e desenha o
/// conteúdo; quem decide quando e onde aparecem é o SDD-26 / host. T-Game, não thread-safe; sem alocação em regime
/// (glifos cacheados por (char, tamanho físico); só <see cref="FormatAway"/> e <see cref="FormatChip"/> alocam texto).
/// </summary>
public sealed class HotspotRenderer : IDisposable
{
    public const string PeakText = "PICO DE DEMANDA · ESCALAR ×3";
    public const string BossText = "CHEFE · RESPONDER PERGUNTA AZURE";
    public const string SealText = "MODO GERENCIADO · AUTO-ESCALA ATIVA";

    private const int BannerFontDip = 12;
    private const int SealFontDip = 11;
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);

    private readonly ScaledSpriteCache _cache;
    private readonly Typography _typography;
    private readonly IconCatalog _icons;
    private readonly SKPaint _fill = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint _text = new() { IsAntialias = false };
    private readonly SKPaint _sprite = new() { IsAntialias = false };
    private readonly SKPaint _icon = new() { IsAntialias = false };
    private readonly Dictionary<(char Ch, int Px), (SKTextBlob Blob, float Width)> _glyphs = [];
    private int _dpi;
    private float _scale = 1f;

    /// <param name="spriteCache">Cache do <see cref="SceneRenderer.SpriteCache"/> (já assado no mesmo DPI).</param>
    public HotspotRenderer(ScaledSpriteCache spriteCache, Typography typography, IconCatalog icons)
    {
        ArgumentNullException.ThrowIfNull(spriteCache);
        ArgumentNullException.ThrowIfNull(typography);
        ArgumentNullException.ThrowIfNull(icons);
        _cache = spriteCache;
        _typography = typography;
        _icons = icons;
    }

    public int Dpi => _dpi;

    /// <summary>Define o DPI (chamar junto de <see cref="SceneRenderer.SetDpi"/> e <c>Typography.Clear()</c> em WM_DPICHANGED).</summary>
    public void SetDpi(int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        if (dpi == _dpi) return;
        _dpi = dpi;
        _scale = dpi / 96f;
        ClearGlyphs();
    }

    // ---------- textos ----------

    /// <summary>Tempo restante do chip no formato <c>m:ss</c> (exemplos do design: <c>0:24</c>, <c>0:45</c>).</summary>
    public static string FormatChip(int seconds)
    {
        int s = Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>"ausente há Xh Ymin"; abaixo de 1 h omite o "Xh" (DP-22.11).</summary>
    public static string FormatAway(TimeSpan away)
    {
        long minutes = Math.Max(0, (long)away.TotalMinutes);
        long h = minutes / 60, m = minutes % 60;
        return h == 0 ? $"ausente há {m}min" : $"ausente há {h}h {m}min";
    }

    // ---------- banner ----------

    /// <summary>Largura do controle do banner em pixels (conteúdo medido; altura = <c>round(36 × scale)</c>).</summary>
    public int MeasureBannerWidth(BannerVariant variant, string chip)
    {
        RequireDpi();
        var font = _typography.GetFont(FontRole.SilkscreenRegular, BannerFontDip, _dpi);
        int gap = P(variant == BannerVariant.Peak ? 12 : 10);
        int w = 2 * P(16);
        if (variant == BannerVariant.Peak) w += P(16) + gap;
        w += MeasureSpaced(Text(variant), font, BannerFontDip) + gap;
        w += 2 * P(6) + MeasureChip(chip, font);
        return w;
    }

    /// <summary>
    /// Desenha o banner na superfície da janela (controle + margem de 6 DIP): anel pulsante (adPulse, ou fixo de 2 DIP
    /// sob reducedMotion/Saver) e controle. <paramref name="now"/> = relógio de animação em segundos.
    /// </summary>
    public void DrawBanner(SKCanvas canvas, BannerVariant variant, string chip, double now, AnimationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(chip);
        RequireDpi();
        canvas.Clear(SKColors.Transparent);
        var bounds = canvas.DeviceClipBounds;
        int m = P(HotspotGeometry.BannerRingMarginDip);
        int h = P(HotspotGeometry.BannerControlHeightDip);
        var control = new SKRectI(m, m, bounds.Width - m, m + h);

        // anel: box-shadow 0 0 0 spread — contorno por fora do controle (DP-22.15: âmbar também no chefe)
        var ring = policy.Pulse(now);
        int spread = (int)MathF.Round((float)ring.SpreadDip * _scale, MidpointRounding.AwayFromZero); // 0 DIP = sem anel
        byte alpha = (byte)Math.Round(255 * ring.Alpha);
        if (spread > 0 && alpha > 0)
        {
            canvas.Save();
            canvas.ClipRect(SKRect.Create(control.Left, control.Top, control.Width, control.Height), SKClipOperation.Difference);
            _fill.Color = Palette.Ambar.WithAlpha(alpha);
            canvas.DrawRect(control.Left - spread, control.Top - spread, control.Width + 2 * spread, control.Height + 2 * spread, _fill);
            canvas.Restore();
        }

        bool peak = variant == BannerVariant.Peak;
        SKColor bg = peak ? Palette.Ambar : Palette.Perigo;
        SKColor fg = peak ? Palette.BgTrilhoFundo : Palette.PerigoTinta;
        SKColor chipBg = peak ? Palette.BgTrilhoFundo : Palette.PerigoTinta;
        SKColor chipFg = peak ? Palette.Ambar : Palette.PerigoClaro;
        _fill.Color = bg;
        canvas.DrawRect(control.Left, control.Top, control.Width, control.Height, _fill);

        var font = _typography.GetFont(FontRole.SilkscreenRegular, BannerFontDip, _dpi);
        int gap = P(peak ? 12 : 10);
        float x = control.Left + P(16);
        float cy = control.Top + control.Height / 2f;
        if (peak)
        {
            _icon.Color = fg;
            _icons.DrawTinted(canvas, IconId.SetaPico, _dpi, x + P(16) / 2f, cy, _icon);
            x += P(16) + gap;
        }
        float baseline = Baseline(font, cy);
        x += DrawSpaced(canvas, Text(variant), font, BannerFontDip, x, baseline, fg) + gap;

        int chipW = 2 * P(6) + MeasureChip(chip, font);
        int chipH = LineHeight(font) + 2 * P(2);
        _fill.Color = chipBg;
        canvas.DrawRect(x, MathF.Round(cy - chipH / 2f), chipW, chipH, _fill);
        DrawSpaced(canvas, chip, font, BannerFontDip, x + P(6) + (MeasureChip(chip, font) - MeasureSpaced(chip, font, BannerFontDip)) / 2f,
            baseline, chipFg);
    }

    // ---------- caixa de suprimentos ----------

    /// <summary>
    /// Desenha a caixa (superfície 52 × 44 DIP): sprite 45 × 24 centrado, base alinhada, piscando em adBlink
    /// (opacidade 1 ↔ 0,45; <c>reducedMotion</c>/Saver = 1).
    /// </summary>
    public void DrawSupplyBox(SKCanvas canvas, double now, AnimationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        RequireDpi();
        canvas.Clear(SKColors.Transparent);
        if (!_cache.TryGet(SpriteId.SupplyCrate, out var src, out _) || _cache.Image is not { } image) return;
        var b = canvas.DeviceClipBounds;
        var dst = HotspotGeometry.SupplySprite(new PixelRect(0, 0, b.Width, b.Height), src.Width, src.Height);
        _sprite.Color = Palette.TxtBranco.WithAlpha((byte)Math.Round(255 * policy.Blink(now)));
        canvas.DrawImage(image, SKRect.Create(src.Left, src.Top, src.Width, src.Height),
            new SKRect(dst.Left, dst.Top, dst.Right, dst.Bottom), Nearest, _sprite);
    }

    // ---------- selo de modo ausente ----------

    /// <summary>Tamanho em pixels do selo (borda 1 + padding 7/12 + conteúdo) para o texto "ausente há …".</summary>
    public PixelSize MeasureSeal(string awayText)
    {
        ArgumentNullException.ThrowIfNull(awayText);
        RequireDpi();
        var font = _typography.GetFont(FontRole.SilkscreenRegular, SealFontDip, _dpi);
        int border = P(1);
        int w = 2 * border + 2 * P(12) + P(8) + P(10) + MeasureSpaced(SealText, font, SealFontDip) + P(10) + MeasureSpaced(awayText, font, SealFontDip);
        int h = 2 * border + 2 * P(7) + LineHeight(font);
        return new PixelSize(w, h);
    }

    /// <summary>Desenha o selo (fundo bg-ausente, borda 1 px borda-forte) na superfície da janela, alfa 255.</summary>
    public void DrawSeal(SKCanvas canvas, string awayText)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(awayText);
        RequireDpi();
        canvas.Clear(SKColors.Transparent);
        var b = canvas.DeviceClipBounds;
        int border = P(1);
        _fill.Color = Palette.BordaForte;
        canvas.DrawRect(0, 0, b.Width, b.Height, _fill);
        _fill.Color = Palette.BgAusente;
        canvas.DrawRect(border, border, b.Width - 2 * border, b.Height - 2 * border, _fill);

        var font = _typography.GetFont(FontRole.SilkscreenRegular, SealFontDip, _dpi);
        float cy = b.Height / 2f;
        float x = border + P(12);
        int sq = P(8);
        _fill.Color = Palette.TxtRotulo;
        canvas.DrawRect(x, MathF.Round(cy - sq / 2f), sq, sq, _fill);
        x += sq + P(10);
        float baseline = Baseline(font, cy);
        x += DrawSpaced(canvas, SealText, font, SealFontDip, x, baseline, Palette.TxtSecundario) + P(10);
        DrawSpaced(canvas, awayText, font, SealFontDip, x, baseline, Palette.TxtRotulo);
    }

    // ---------- texto com letter-spacing: 1 px ----------

    private static string Text(BannerVariant v) => v == BannerVariant.Peak ? PeakText : BossText;

    private int P(float dip) => Math.Max(1, (int)MathF.Round(dip * _scale, MidpointRounding.AwayFromZero));

    private static int LineHeight(SKFont font) => (int)MathF.Round(font.Metrics.Descent - font.Metrics.Ascent, MidpointRounding.AwayFromZero);

    private static float Baseline(SKFont font, float centerY) =>
        MathF.Round(centerY - LineHeight(font) / 2f) + MathF.Round(-font.Metrics.Ascent);

    // CSS letter-spacing soma o espaçamento após cada caractere (inclusive o último).
    private int MeasureSpaced(string text, SKFont font, int sizeDip)
    {
        int spacing = P(1);
        float w = 0;
        foreach (char c in text) w += Glyph(c, font, sizeDip).Width + spacing;
        return (int)MathF.Round(w, MidpointRounding.AwayFromZero);
    }

    // Largura do chip com dígitos tabulares (o mais largo): a janela não muda de tamanho a cada segundo (SDD-22 RF-22.8).
    private int MeasureChip(string chip, SKFont font)
    {
        float digit = 0;
        for (char d = '0'; d <= '9'; d++) digit = Math.Max(digit, Glyph(d, font, BannerFontDip).Width);
        int spacing = P(1);
        float w = 0;
        foreach (char c in chip) w += (char.IsAsciiDigit(c) ? digit : Glyph(c, font, BannerFontDip).Width) + spacing;
        return (int)MathF.Round(w, MidpointRounding.AwayFromZero);
    }

    private int DrawSpaced(SKCanvas canvas, string text, SKFont font, int sizeDip, float x, float baseline, SKColor color)
    {
        int spacing = P(1);
        _text.Color = color;
        float cursor = x;
        foreach (char c in text)
        {
            var (blob, width) = Glyph(c, font, sizeDip);
            if (c != ' ') canvas.DrawText(blob, MathF.Round(cursor), baseline, _text);
            cursor += width + spacing;
        }
        return (int)MathF.Round(cursor - x, MidpointRounding.AwayFromZero);
    }

    private (SKTextBlob Blob, float Width) Glyph(char c, SKFont font, int sizeDip)
    {
        var key = (c, Typography.PhysicalSize(sizeDip, _dpi));
        if (_glyphs.TryGetValue(key, out var g)) return g;
        string s = c.ToString();
        g = (SKTextBlob.Create(s, font) ?? throw new InvalidOperationException("SKTextBlob.Create failed."), font.MeasureText(s));
        _glyphs[key] = g;
        return g;
    }

    private void ClearGlyphs()
    {
        foreach (var g in _glyphs.Values) g.Blob.Dispose();
        _glyphs.Clear();
    }

    private void RequireDpi()
    {
        if (_dpi == 0) throw new InvalidOperationException("Chame SetDpi antes de desenhar.");
    }

    public void Dispose()
    {
        ClearGlyphs();
        _fill.Dispose();
        _text.Dispose();
        _sprite.Dispose();
        _icon.Dispose();
    }
}
