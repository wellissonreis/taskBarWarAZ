using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Panel;

/// <summary>Alvos do card de pergunta (SDD-24 RF-24.11): <c>QuestionOption(i)</c>, <c>QuestionMinimize</c>, <c>QuestionClose</c>, <c>Background</c>.</summary>
public enum QuestionHitKind { None, Background, Option, Minimize, Close }

public readonly record struct QuestionHit(QuestionHitKind Kind, int Index = -1);

/// <summary>
/// Layout puro do card de pergunta (SDD-24 RF-24.9). Coordenadas em DIP da caixa visível (origem no canto superior
/// esquerdo; a janela tem +6 DIP de margem para a sombra). A caixa cresce <b>para cima</b> com a explicação: o host
/// mantém a base (<c>bottom = stripTop − 8</c>) usando <see cref="Height"/>.
/// </summary>
public sealed record QuestionCardLayout(
    float Height,
    DipRect HeaderBox,
    DipRect TimerBox,
    DipRect ProblemBox,
    string[] ProblemLines,
    DipRect[] Options,
    string[] OptionTexts,
    DipRect[] OptionTextBoxes,
    DipRect FooterBox,
    string[] FooterLines,
    DipRect ActionButton,
    bool ShowsExplanation)
{
    public const float Width = 520f, BaseHeight = 242f, Border = 2f, Pad = 16f, Gap = 12f, OptionGap = 8f, OptionMinHeight = 44f;
    public const float ProblemLineHeight = 13f * 1.45f, ExplanationLineHeight = 11f * 1.45f;
    public const float ActionSize = 44f;
    public const int MaxExplanationLines = 3, MaxProblemLines = 2;

    public static QuestionCardLayout Compute(QuestionCardModel m, IPanelTextMetrics tm)
    {
        var cx = Border + Pad;
        var cw = Width - 2 * (Border + Pad);
        var y = cx;

        // Cabeçalho (centralizado na vertical); o timer/resultado fica à esquerda do botão de 16 + gap 12.
        var hh = tm.LineHeight(PanelFont.Silkscreen, 11);
        var action = new DipRect(Width - Border - ActionSize, Border, ActionSize, ActionSize);
        var headerBox = new DipRect(cx, y, cw, hh);
        var timerText = m.ResultText ?? m.TimerText;
        var timerW = tm.Width(PanelFont.Silkscreen, 11, timerText);
        var iconLeft = action.X + (ActionSize - 16f) / 2f;
        var timerBox = new DipRect(iconLeft - 12f - timerW, y, timerW, hh);
        y += hh + Gap;

        var problem = TextFit.Wrap(m.Problem, cw, PanelFont.JetBrainsMono, 13, tm, MaxProblemLines);
        var problemBox = new DipRect(cx, y, cw, problem.Length * ProblemLineHeight);
        y += problemBox.H + Gap;

        // Alternativas: 2 colunas, gap 8, min-height 44, padding 8 10, borda 1, texto em 1 linha.
        var ow = (cw - OptionGap) / 2f;
        var lh11 = tm.LineHeight(PanelFont.JetBrainsMono, 11);
        var oh = Math.Max(OptionMinHeight, 2 + 16 + lh11);
        var count = m.OptionTexts.Count;
        var options = new DipRect[count];
        var texts = new string[count];
        var textBoxes = new DipRect[count];
        for (var i = 0; i < count; i++)
        {
            var col = i % 2;
            var row = i / 2;
            options[i] = new DipRect(cx + col * (ow + OptionGap), y + row * (oh + OptionGap), ow, oh);
            texts[i] = TextFit.Ellipsize(m.OptionTexts[i], ow - 2 - 20, PanelFont.JetBrainsMono, 11, tm);
            textBoxes[i] = new DipRect(options[i].X + 1 + 10, options[i].Y + (oh - lh11) / 2f, ow - 2 - 20, lh11);
        }
        var rows = (count + 1) / 2;
        y += rows * oh + Math.Max(0, rows - 1) * OptionGap + Gap;

        // Rodapé (Mono 10) ou explicação (Mono 11, lh 1,45, ≤ 3 linhas).
        var explains = m.Explanation is not null && m.State != QuestionCardState.Waiting;
        string[] footerLines;
        float footerH;
        if (explains)
        {
            footerLines = TextFit.Wrap(m.Explanation!, cw, PanelFont.JetBrainsMono, 11, tm, MaxExplanationLines);
            footerH = footerLines.Length * ExplanationLineHeight;
        }
        else
        {
            footerLines = TextFit.Wrap(QuestionCardModel.Footer, cw, PanelFont.JetBrainsMono, 10, tm);
            footerH = footerLines.Length * tm.LineHeight(PanelFont.JetBrainsMono, 10);
        }
        var footerBox = new DipRect(cx, y, cw, footerH);

        // Altura: 242 − rodapé de design + bloco atual (RF-24.9). O rodapé de design é o de Waiting.
        var designFooter = TextFit.Wrap(QuestionCardModel.Footer, cw, PanelFont.JetBrainsMono, 10, tm).Length * tm.LineHeight(PanelFont.JetBrainsMono, 10);
        var height = explains ? BaseHeight - designFooter + footerH : BaseHeight;
        return new QuestionCardLayout(height, headerBox, timerBox, problemBox, problem, options, texts, textBoxes,
            footerBox, footerLines, action, explains);
    }

    /// <summary>
    /// Alvo na caixa visível. Fora da caixa: <see cref="QuestionHitKind.None"/>. Ação (minimizar ou fechar) tem 44 × 44
    /// limitado ao interior do card; opções só respondem enquanto habilitadas.
    /// </summary>
    public QuestionHit Hit(float x, float y, QuestionCardModel m)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return new(QuestionHitKind.None);
        if (MissionsLayout.Contains(ActionButton, x, y))
            return new(m.State == QuestionCardState.Waiting ? QuestionHitKind.Minimize : QuestionHitKind.Close);
        if (m.OptionsEnabled)
            for (var i = 0; i < Options.Length; i++)
                if (MissionsLayout.Contains(Options[i], x, y)) return new(QuestionHitKind.Option, i);
        return new(QuestionHitKind.Background);
    }
}
