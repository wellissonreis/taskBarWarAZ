using SideWar.Rendering.Icons;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>
/// Renderer Skia puro do card "Bem-vindo de volta" (SDD-24 RF-24.10; canvas <c>Offline.dc.html</c>), para plugar em
/// <c>OverlayWindow.Renderer</c> da janela <c>OfflineCard</c>. Frame completo: sombra dura (+6,+6), borda de 2 DIP
/// <c>ambar</c>, fundo <c>bg-painel</c>, cabeçalho, barra de tempo, 4 tiles, aviso de perdas (se houver) e os 2 botões.
/// Sem hover (RF-24.12: COLETAR ×2 e COLETAR +X não têm hover no design).
/// </summary>
public sealed class OfflineCardRenderer(Typography typography, IconCatalog icons, int dpi) : IDisposable
{
    private readonly PanelPainter p = new(typography, dpi);
    private readonly SKPaint iconPaint = new() { IsAntialias = false };

    /// <summary>Tamanho físico da janela: caixa + margem de sombra (RF-24.2).</summary>
    public (int W, int H) WindowSize => ((int)(p.Px(OfflineCardLayout.Width) + p.Px(6)), (int)(p.Px(OfflineCardLayout.Height) + p.Px(6)));

    private static SKColor Tone(OfflineTone t) => t switch
    {
        OfflineTone.Ambar => Palette.Ambar,
        OfflineTone.Menta => Palette.Menta,
        OfflineTone.Rotulo => Palette.TxtRotulo,
        _ => Palette.TxtPrincipal,
    };

    public void Draw(SKCanvas c, OfflineCardModel m, OfflineCardLayout l)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(l);
        var tm = new PanelTextMetricsSkia(typography);
        c.Clear(SKColors.Transparent);
        var box = p.Rect(0, 0, OfflineCardLayout.Width, OfflineCardLayout.Height);
        HardShadow.Draw(c, box, Palette.SombraCard, dpi);
        p.Fill(c, box, Palette.Ambar);
        var b = p.Px(OfflineCardLayout.Border);
        p.Fill(c, new SKRect(box.Left + b, box.Top + b, box.Right - b, box.Bottom - b), Palette.BgPainel);

        p.Text(c, m.Overline, PanelFont.JetBrainsMono, 10, l.OverlineBox, Palette.TxtRotulo, letterSpacing: 1f);
        p.Text(c, m.Title, PanelFont.Silkscreen, 24, l.TitleBox, Palette.Ambar);
        DrawIntro(c, l, tm);

        p.Text(c, "TEMPO ACUMULADO", PanelFont.JetBrainsMono, 10, l.TimeRowBox, Palette.TxtRotulo);
        p.Text(c, m.TimeLabelRight, PanelFont.JetBrainsMono, 10, l.TimeRowBox, Palette.TxtRotulo, TextAlign.Right);
        p.Fill(c, l.BarTrack, Palette.BgTrilho);
        if (l.BarFill.W > 0) p.Fill(c, l.BarFill, Palette.Azul);

        for (var i = 0; i < l.Tiles.Length; i++)
        {
            var t = m.Tiles[i];
            var tl = l.Tiles[i];
            p.Box(c, tl.Box, Palette.BordaPainel, Palette.BgItem);
            p.Text(c, t.Label, PanelFont.JetBrainsMono, 10, tl.LabelBox, Palette.TxtRotulo);
            DrawSilkWithFallback(c, t.Value, 20, tl.ValueBox, Tone(t.ValueTone));
            p.Text(c, tl.Sub, PanelFont.JetBrainsMono, 10, tl.SubBox, Tone(t.SubTone));
        }

        if (l.WarningBox is { } wb)
        {
            p.Box(c, wb, Palette.PerigoBorda, Palette.PerigoFundo);
            iconPaint.Color = Palette.PerigoClaro;
            icons.DrawTinted(c, IconId.Alerta, dpi, p.Px(l.WarningIcon.X + l.WarningIcon.W / 2f), p.Px(l.WarningIcon.Y + l.WarningIcon.H / 2f), iconPaint);
            var lh = tm.LineHeight(PanelFont.JetBrainsMono, 11);
            for (var i = 0; i < l.WarningLines.Length; i++)
            {
                var top = l.WarningTextBox.Y + i * OfflineCardLayout.WarningLineHeight + (OfflineCardLayout.WarningLineHeight - lh) / 2f;
                p.Text(c, l.WarningLines[i], PanelFont.JetBrainsMono, 11, new DipRect(l.WarningTextBox.X, top, l.WarningTextBox.W, lh), Palette.PerigoTexto);
            }
        }

        p.Fill(c, l.CollectX2, Palette.Ambar);
        p.Text(c, m.CollectX2Label, PanelFont.Silkscreen, 13, l.CollectX2LabelBox, Palette.BgTrilhoFundo, TextAlign.Center, 1f);
        p.Text(c, m.CollectX2Hint, PanelFont.JetBrainsMono, 10, l.CollectX2HintBox, Palette.BgTrilhoFundo, TextAlign.Center);
        p.Box(c, l.Collect, Palette.BordaForte, Palette.BgPainel);   // transparente no design: sobre bg-painel
        p.Text(c, m.CollectLabel, PanelFont.Silkscreen, 11, l.CollectLabelBox, Palette.TxtBotaoNeutro, TextAlign.Center, 1f);
    }

    /// <summary>
    /// Texto Silkscreen à esquerda; glifo sem cobertura na face (ex.: <c>→</c>) cai para JetBrains Mono no mesmo tamanho
    /// (SDD-20 RF-20.3 item 7). Cada trecho é desenhado em sequência, avançando pela largura medida.
    /// </summary>
    private void DrawSilkWithFallback(SKCanvas c, string text, float size, DipRect box, SKColor color)
    {
        var silk = p.Font(PanelFont.Silkscreen, size);
        var x = box.X;
        var i = 0;
        var tm = new PanelTextMetricsSkia(typography);
        while (i < text.Length)
        {
            var covered = silk.GetGlyph(text[i]) != 0 || text[i] == ' ';
            var j = i + 1;
            while (j < text.Length && (silk.GetGlyph(text[j]) != 0 || text[j] == ' ') == covered) j++;
            var run = text[i..j];
            var face = covered ? PanelFont.Silkscreen : PanelFont.JetBrainsMono;
            var lh = tm.LineHeight(face, size);
            var top = box.Y + (box.H - lh) / 2f;
            p.Text(c, run, face, size, new DipRect(x, top, box.W - (x - box.X), lh), color);
            x += tm.Width(face, size, run);
            i = j;
        }
    }

    private void DrawIntro(SKCanvas c, OfflineCardLayout l, PanelTextMetricsSkia tm)
    {
        var lh = tm.LineHeight(PanelFont.JetBrainsMono, 12);
        for (var i = 0; i < l.IntroLines.Length; i++)
        {
            var line = l.IntroLines[i];
            var y = l.IntroBox.Y + i * lh;
            if (line.HighlightStart < 0)
            {
                p.Text(c, line.Text, PanelFont.JetBrainsMono, 12, new DipRect(l.IntroBox.X, y, l.IntroBox.W, lh), Palette.TxtSecundario);
                continue;
            }
            var pre = line.Text[..line.HighlightStart];
            var mid = line.Text.Substring(line.HighlightStart, line.HighlightLen);
            var post = line.Text[(line.HighlightStart + line.HighlightLen)..];
            var x = l.IntroBox.X;
            foreach (var (s, color) in new[] { (pre, Palette.TxtSecundario), (mid, Palette.TxtPrincipal), (post, Palette.TxtSecundario) })
            {
                if (s.Length == 0) continue;
                p.Text(c, s, PanelFont.JetBrainsMono, 12, new DipRect(x, y, l.IntroBox.W, lh), color);
                x += tm.Width(PanelFont.JetBrainsMono, 12, s);
            }
        }
    }

    public void Dispose()
    {
        p.Dispose();
        iconPaint.Dispose();
    }
}
