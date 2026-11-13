using SideWar.Rendering.Icons;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Widget;
using SkiaSharp;

namespace SideWar.Rendering.Widget;

/// <summary>
/// Renderer puro do widget compacto de largura natural (ADR-034) (SDD-23 RF-23.3 a RF-23.7, RF-23.9): fundo em cache, créditos, onda,
/// selo de presença (D-16), chip do bônus, hover/ativo, ícones, barra de ativo e badges. Sem hit-test nem
/// janela (T5.2/T1.9). Uso na T-Game; não é thread-safe.
/// </summary>
public sealed class WidgetRenderer : IDisposable
{
    private const float RadiusDip = 4f; // só dos botões do nav; a janela não tem raio

    private readonly Typography _typography;
    private readonly IconCatalog _icons;
    private readonly SKPaint _fill = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint _fillAa = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _text = new() { IsAntialias = false };
    private readonly SKPaint _textAa = new() { IsAntialias = true };
    private readonly SKPaint _tint = new() { Color = Palette.TxtPrincipal };
    private readonly TextRunCache _runs = new();
    private readonly List<WidgetTextBox> _boxes = new(12);
    private SKBitmap? _background;
    private SKImage? _backgroundImage; // DrawBitmap criaria um SKImage por frame
    private (int W, int H) _backgroundKey;

    public WidgetRenderer(Typography typography, IconCatalog icons)
    {
        _typography = typography;
        _icons = icons;
    }

    /// <summary>Caixas de texto desenhadas no último <see cref="Render"/> (px da janela), para golden de bbox (T-23.4).</summary>
    public IReadOnlyList<WidgetTextBox> LastTextBoxes => _boxes;

    // WidgetModel.SealText concatena a cada acesso (alocação); aqui o texto do selo é memorizado por (presença, multiplicador).
    private bool _sealPresent;
    private string? _sealMultiplier, _sealText;

    private string SealOf(in WidgetModel m)
    {
        if (_sealText is null || _sealPresent != m.IsPresent || !string.Equals(_sealMultiplier, m.MultiplierText, StringComparison.Ordinal))
        {
            _sealPresent = m.IsPresent;
            _sealMultiplier = m.MultiplierText;
            _sealText = m.SealText;
        }
        return _sealText;
    }

    /// <summary>Layout vigente do modelo em DIP (selo e chip medidos nas fontes reais).</summary>
    public WidgetLayout GetLayout(in WidgetModel model, float heightDip, int dpi)
    {
        double scale = dpi / 96.0;
        float sealW = MeasureBoxDip(FontRole.SilkscreenRegular, 10, dpi, SealOf(model), scale);
        float boxH = (float)(LineHeightPx(_typography.GetFont(FontRole.SilkscreenRegular, 10, dpi)) / scale) + 8 + 2;
        float chipW = model.BonusChipText is { } chip ? MeasureBoxDip(FontRole.SilkscreenRegular, 10, dpi, chip, scale) : 0;
        return WidgetLayout.Compute(heightDip, sealW, boxH, chipW);
    }

    /// <summary>Largura natural da janela em DIP para o modelo (RF-23.2); alimenta <c>WidgetGeometry.Compute</c>.</summary>
    public float MeasureWidthDip(in WidgetModel model, int dpi) => GetLayout(model, WidgetGeometry.HeightDip, dpi).WidthDip;

    /// <summary>Largura natural do selo em DIP para um texto (CA-23.10).</summary>
    public float MeasureSealDip(string sealText, int dpi) => MeasureBoxDip(FontRole.SilkscreenRegular, 10, dpi, sealText, dpi / 96.0);

    private float MeasureBoxDip(FontRole role, float size, int dpi, string text, double scale)
    {
        float px = _runs.Measure(text, _typography.GetFont(role, size, dpi));
        return (float)(Math.Ceiling(px) / scale) + 12 + 2;
    }

    /// <summary>Desenha o widget inteiro (frame completo) em <paramref name="canvas"/>, origem no canto da janela.</summary>
    public void Render(SKCanvas canvas, in WidgetModel model, float heightDip, int dpi)
    {
        double scale = dpi / 96.0;
        WidgetLayout layout = GetLayout(model, heightDip, dpi);
        int wPx = Px(layout.WidthDip, scale), hPx = Px(heightDip, scale);
        _boxes.Clear();
        canvas.Clear(SKColors.Transparent);
        DrawBackground(canvas, wPx, hPx, scale);

        DrawCredits(canvas, model, layout, hPx, dpi, scale);
        DrawWave(canvas, model, layout, hPx, dpi, scale);
        DrawSeal(canvas, layout.Seal, SealOf(model), model.IsPresent ? Palette.SeloPresente : Palette.SeloAusente, dpi, scale);
        // Pico ativo: o chip ×3 vem depois do selo (ADR-034; a largura natural já o inclui).
        if (model.BonusChipText is { } chip && layout.HasChip)
            DrawSeal(canvas, layout.Chip, chip, Palette.Ambar, dpi, scale);
        DrawNav(canvas, model, layout, dpi, scale);
    }

    private void DrawBackground(SKCanvas canvas, int wPx, int hPx, double scale)
    {
        if (_background is null || _backgroundKey != (wPx, hPx))
        {
            _backgroundImage?.Dispose();
            _background?.Dispose();
            _background = new SKBitmap(new SKImageInfo(wPx, hPx, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var c = new SKCanvas(_background);
            c.Clear(SKColors.Transparent);
            // Fundo bg-trilho-fundo alfa 235, sem raio; borda 1 DIP borda-painel (RF-23.3). Todo pixel tem alfa >= 1.
            float b = Math.Max(1, (float)Math.Round(scale));
            _fill.Color = Palette.BgTrilhoFundo.WithAlpha(235);
            c.DrawRect(0, 0, wPx, hPx, _fill);
            _fill.Color = Palette.BordaPainel;
            c.DrawRect(0, 0, wPx, b, _fill);
            c.DrawRect(0, hPx - b, wPx, b, _fill);
            c.DrawRect(0, b, b, hPx - 2 * b, _fill);
            c.DrawRect(wPx - b, b, b, hPx - 2 * b, _fill);
            _backgroundImage = SKImage.FromBitmap(_background);
            _backgroundKey = (wPx, hPx);
        }
        canvas.DrawImage(_backgroundImage!, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
    }

    private void DrawCredits(SKCanvas canvas, in WidgetModel m, in WidgetLayout l, int hPx, int dpi, double scale)
    {
        SKFont f1 = _typography.GetFont(FontRole.SilkscreenRegular, 13, dpi);
        SKFont f2 = _typography.GetFont(FontRole.JetBrainsMonoRegular, 10, dpi);
        float lh1 = (float)(13 * 1.1 * scale);
        bool pending = m.PendingText is not null;
        float lh2 = pending ? (float)(14 * scale) : LineHeightPx(f2);
        float top = (hPx - (lh1 + lh2)) / 2f;
        float x = Px(l.Credits.X, scale);
        var clip = new SKRect(x, 0, x + Px(l.Credits.W, scale), hPx);

        canvas.Save();
        canvas.ClipRect(clip);
        DrawLine(canvas, m.CreditsText, f1, _text, Palette.Ambar, x, Baseline(f1, top, lh1));
        float top2 = top + lh1;
        if (pending)
        {
            SKFont fp = _typography.GetFont(FontRole.SilkscreenRegular, 9, dpi);
            float pillW = (float)Math.Ceiling(_runs.Measure(m.PendingText!, fp)) + Px(10, scale);
            pillW = Math.Min(pillW, clip.Width);
            var pill = new SKRect(x, (float)Math.Round(top2), x + pillW, (float)Math.Round(top2) + lh2);
            _fillAa.Color = Palette.Ambar;
            DrawRound(canvas, pill, lh2 / 2f, _fillAa);
            DrawLine(canvas, m.PendingText!, fp, _text, Palette.BgTrilhoFundo,
                pill.Left + (pill.Width - _runs.Measure(m.PendingText!, fp)) / 2f, Baseline(fp, pill.Top, pill.Height));
        }
        else
        {
            DrawLine(canvas, m.RateText, f2, _textAa, Palette.Menta, x, Baseline(f2, top2, lh2));
        }
        canvas.Restore();
    }

    private void DrawWave(SKCanvas canvas, in WidgetModel m, in WidgetLayout l, int hPx, int dpi, double scale)
    {
        SKFont f1 = _typography.GetFont(FontRole.SilkscreenRegular, 12, dpi);
        SKFont f2 = _typography.GetFont(FontRole.JetBrainsMonoRegular, 10, dpi);
        float lh1 = (float)(12 * 1.1 * scale), lh2 = LineHeightPx(f2);
        float top = (hPx - (lh1 + lh2)) / 2f;
        float x = Px(l.Wave.X, scale);
        canvas.Save();
        canvas.ClipRect(new SKRect(x, 0, x + Px(l.Wave.W, scale), hPx));
        DrawLine(canvas, m.WaveText, f1, _text, Palette.TxtPrincipal, x, Baseline(f1, top, lh1));
        DrawLine(canvas, "onda", f2, _textAa, Palette.TxtWidgetRotulo, x, Baseline(f2, top + lh1, lh2));
        canvas.Restore();
    }

    /// <summary>Caixa com borda de 1 DIP na cor do texto, padding 4×6, sem fundo, raio 0 (RF-23.6).</summary>
    private void DrawSeal(SKCanvas canvas, in DipRect r, string text, SKColor color, int dpi, double scale)
    {
        SKFont f = _typography.GetFont(FontRole.SilkscreenRegular, 10, dpi);
        int x = Px(r.X, scale), y = Px(r.Y, scale), w = Px(r.W, scale), h = Px(r.H, scale);
        int b = Math.Max(1, Px(1, scale));
        _fill.Color = color;
        canvas.DrawRect(x, y, w, b, _fill);
        canvas.DrawRect(x, y + h - b, w, b, _fill);
        canvas.DrawRect(x, y + b, b, h - 2 * b, _fill);
        canvas.DrawRect(x + w - b, y + b, b, h - 2 * b, _fill);
        float pad = (float)(6 * scale) + b;
        DrawLine(canvas, text, f, _text, color, x + pad, Baseline(f, y + b + (float)(4 * scale), LineHeightPx(f)));
    }

    private void DrawNav(SKCanvas canvas, in WidgetModel m, in WidgetLayout l, int dpi, double scale)
    {
        ReadOnlySpan<IconId> icons = [IconId.Arsenal, IconId.Fases, IconId.Missoes, IconId.Migrar];
        SKFont badgeFont = _typography.GetFont(FontRole.JetBrainsMonoRegular, 9, dpi);
        float rr = (float)(RadiusDip * scale);
        for (int i = 0; i < WidgetModel.NavButtonCount; i++)
        {
            DipRect d = l.Button(i);
            var rect = new SKRect(Px(d.X, scale), Px(d.Y, scale), Px(d.X + d.W, scale), Px(d.Y + d.H, scale));
            bool active = m.ActiveTab is { } t && (int)t == i;
            // Ativo + hover usa o fundo do ativo, sem somar (RF-23.7).
            if (active || m.HoverIndex == i)
            {
                _fillAa.Color = active ? Palette.AtivoNav : Palette.HoverNav;
                DrawRound(canvas, rect, rr, _fillAa);
            }

            _icons.DrawTinted(canvas, icons[i], dpi, (rect.Left + rect.Right) / 2f, (rect.Top + rect.Bottom) / 2f, _tint);

            if (active)
            {
                float bw = (float)(16 * scale), bh = (float)(3 * scale);
                float bx = rect.Left + (float)(14 * scale), by = rect.Bottom - (float)(2 * scale) - bh;
                _fillAa.Color = Palette.Ambar;
                DrawRound(canvas, new SKRect(bx, by, bx + bw, by + bh), (float)(2 * scale), _fillAa);
            }

            if (i == (int)WidgetTab.Missoes && m.BadgeText.Length > 0)
            {
                float diam = (float)(14 * scale);
                float bx = rect.Right - (float)(3 * scale) - diam, by = rect.Top + (float)(3 * scale);
                _fillAa.Color = Palette.Ambar;
                DrawRound(canvas, new SKRect(bx, by, bx + diam, by + diam), diam / 2f, _fillAa);
                string s = m.BadgeText;
                float tw = _runs.Measure(s, badgeFont);
                DrawLine(canvas, s, badgeFont, _textAa, Palette.BgTrilhoFundo, bx + (diam - tw) / 2f, Baseline(badgeFont, by, diam));
            }
        }
    }

    private static void DrawRound(SKCanvas canvas, SKRect rect, float radius, SKPaint paint)
    {
        canvas.DrawRoundRect(rect.Left, rect.Top, rect.Width, rect.Height, radius, radius, paint); // sem SKRoundRect (objeto nativo por chamada)
    }

    private void DrawLine(SKCanvas canvas, string text, SKFont font, SKPaint paint, SKColor color, float x, float baseline)
    {
        x = (float)Math.Round(x);
        baseline = (float)Math.Round(baseline);
        paint.Color = color;
        var run = _runs.Get(text, font);
        canvas.DrawText(run.Blob, x, baseline, paint);
        float w = run.Width;
        SKFontMetrics fm = font.Metrics;
        _boxes.Add(new WidgetTextBox(text, new SKRect(x, baseline + fm.Ascent, x + w, baseline + fm.Descent)));
    }

    /// <summary>Altura de linha "normal" (ascent + descent + leading) em px.</summary>
    private static float LineHeightPx(SKFont font)
    {
        SKFontMetrics m = font.Metrics;
        return -m.Ascent + m.Descent + m.Leading;
    }

    /// <summary>Linha de base para uma linha de altura <paramref name="lineHeight"/> começando em <paramref name="top"/> (meia-entrelinha, como CSS).</summary>
    private static float Baseline(SKFont font, float top, float lineHeight)
    {
        SKFontMetrics m = font.Metrics;
        return top + (lineHeight - (-m.Ascent + m.Descent)) / 2f - m.Ascent;
    }

    private static int Px(float dip, double scale) => (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);

    public void Dispose()
    {
        _background?.Dispose();
        _fill.Dispose();
        _fillAa.Dispose();
        _text.Dispose();
        _textAa.Dispose();
        _tint.Dispose();
        _backgroundImage?.Dispose();
        _runs.Dispose();
    }
}

/// <summary>Texto desenhado e sua caixa em px da janela.</summary>
public readonly record struct WidgetTextBox(string Text, SKRect Bounds);
