using SideWar.Rendering.Icons;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Panel;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>
/// Renderer Skia puro do card de pergunta do chefe (SDD-24 RF-24.9), para plugar em
/// <c>OverlayWindow.Renderer(OverlayFrame, OverlayRenderContext)</c>. Frame completo: sombra dura (+6,+6), borda de
/// 2 DIP <c>perigo</c>, fundo, cabeçalho, pergunta, 4 alternativas por estado, rodapé ou explicação, e o botão
/// minimizar (traço <c>M3 12h10</c> como <c>SKPath</c>, DP-20.2) ou fechar (<see cref="IconId.Fechar"/>).
/// </summary>
public sealed class QuestionCardRenderer(Typography typography, IconCatalog icons, int dpi) : IDisposable
{
    private readonly PanelPainter p = new(typography, dpi);
    private readonly PanelTextMetricsSkia tm = new(typography);
    private readonly SKPaint iconPaint = new() { IsAntialias = false };
    private readonly SKPaint strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Butt };
    private readonly SKPath minimize = SKPath.ParseSvgPathData("M3 12h10")!;

    /// <summary>Tamanho físico da janela para a altura (DIP) do layout: caixa + margem de sombra (RF-24.2).</summary>
    public (int W, int H) WindowSize(float heightDip) => ((int)(p.Px(QuestionCardLayout.Width) + p.Px(6)), (int)(p.Px(heightDip) + p.Px(6)));

    public void Draw(SKCanvas c, QuestionCardModel m, QuestionCardLayout l, QuestionHit hover = default)
    {
        _ = hover; // o design não define hover no card de pergunta (RF-24.12, DP-24.14)
        c.Clear(SKColors.Transparent);
        var box = p.Rect(0, 0, QuestionCardLayout.Width, l.Height);
        p.Shadow(c, box, Palette.SombraCard);
        p.Fill(c, box, Palette.Perigo);
        var b = p.Px(QuestionCardLayout.Border);
        p.Fill(c, new SKRect(box.Left + b, box.Top + b, box.Right - b, box.Bottom - b), Palette.BgPainel);

        p.Text(c, m.Header, PanelFont.Silkscreen, 11, l.HeaderBox, Palette.PerigoClaro, letterSpacing: 1f);
        var timerColor = m.State == QuestionCardState.Correct ? Palette.Menta : Palette.PerigoClaro;
        p.Text(c, m.ResultText ?? m.TimerText, PanelFont.Silkscreen, 11, l.TimerBox, timerColor, TextAlign.Right);

        var lh13 = tm.LineHeight(PanelFont.JetBrainsMono, 13);
        for (var i = 0; i < l.ProblemLines.Length; i++)
        {
            var top = l.ProblemBox.Y + i * QuestionCardLayout.ProblemLineHeight + (QuestionCardLayout.ProblemLineHeight - lh13) / 2f;
            p.Text(c, l.ProblemLines[i], PanelFont.JetBrainsMono, 13, new Runtime.Geometry.DipRect(l.ProblemBox.X, top, l.ProblemBox.W, lh13), Palette.TxtPrincipal);
        }

        for (var i = 0; i < l.Options.Length; i++)
        {
            var (border, fill) = m.Visuals[i] switch
            {
                OptionVisual.Correct => (Palette.Menta, Palette.MentaFundo),
                OptionVisual.Wrong => (Palette.Perigo, Palette.PerigoFundo),
                _ => (Palette.BordaForte, Palette.BgItem),
            };
            p.Box(c, l.Options[i], border, fill);
            p.Text(c, l.OptionTexts[i], PanelFont.JetBrainsMono, 11, l.OptionTextBoxes[i], Palette.TxtPrincipal);
        }

        var size = l.ShowsExplanation ? 11f : 10f;
        var lh = tm.LineHeight(PanelFont.JetBrainsMono, size);
        var step = l.ShowsExplanation ? QuestionCardLayout.ExplanationLineHeight : lh;
        for (var i = 0; i < l.FooterLines.Length; i++)
            p.Text(c, l.FooterLines[i], PanelFont.JetBrainsMono, size,
                new Runtime.Geometry.DipRect(l.FooterBox.X, l.FooterBox.Y + i * step + (step - lh) / 2f, l.FooterBox.W, lh),
                l.ShowsExplanation ? Palette.TxtSecundario : Palette.TxtRotulo);

        var cx = p.Px(l.ActionButton.X + l.ActionButton.W / 2f);
        var cy = p.Px(l.ActionButton.Y + l.ActionButton.H / 2f);
        if (m.State == QuestionCardState.Waiting)
        {
            strokePaint.Color = Palette.TxtRotulo;
            c.Save();
            c.Translate(cx - 8f * p.Scale, cy - 8f * p.Scale);
            c.Scale(p.Scale);
            strokePaint.StrokeWidth = 1.8f;
            c.DrawPath(minimize, strokePaint);
            c.Restore();
        }
        else
        {
            iconPaint.Color = Palette.TxtRotulo;
            icons.DrawTinted(c, IconId.Fechar, dpi, cx, cy, iconPaint);
        }
    }

    public void Dispose()
    {
        p.Dispose();
        iconPaint.Dispose();
        strokePaint.Dispose();
        minimize.Dispose();
    }
}
