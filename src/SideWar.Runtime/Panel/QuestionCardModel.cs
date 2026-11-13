using SideWar.Runtime.Localization;
using SideWar.Core.Events;

namespace SideWar.Runtime.Panel;

/// <summary>Estado do card de pergunta (SDD-24 RF-24.9): aguardando, correto, errado ou tempo esgotado.</summary>
public enum QuestionCardState { Waiting, Correct, Wrong, TimedOut }

/// <summary>Estado visual de uma alternativa (RF-24.9): normal, correta (menta) ou errada escolhida (perigo).</summary>
public enum OptionVisual { Normal, Correct, Wrong }

/// <summary>
/// Modelo imutável do card de pergunta do chefe (SDD-24 RF-24.9). Leitura apenas do <see cref="BossEncounter"/> (T6.6B):
/// nenhuma chamada altera o encontro. <see cref="OptionTexts"/> já traz o prefixo <c>A · </c>.
/// </summary>
public sealed record QuestionCardModel(
    string Header,
    string TimerText,
    string Problem,
    IReadOnlyList<string> OptionTexts,
    IReadOnlyList<OptionVisual> Visuals,
    QuestionCardState State,
    string? Explanation,
    long Version = 0)
{
    public const string Footer = "Pergunta minimizada não pausa o trabalho: o chefe espera na entrada até o tempo acabar.";
    public const string ResultCorrect = "ACERTOU";
    public const string ResultWrong = "ERROU";
    public const string ResultTimeout = "TEMPO ESGOTADO";

    /// <summary>Opções clicáveis só enquanto aguarda (RF-24.9: as demais ficam desabilitadas).</summary>
    public bool OptionsEnabled => State == QuestionCardState.Waiting;

    /// <summary>Texto que substitui o timer depois de respondido; null enquanto aguarda.</summary>
    public string? ResultText => State switch
    {
        QuestionCardState.Correct => ResultCorrect,
        QuestionCardState.Wrong => ResultWrong,
        QuestionCardState.TimedOut => ResultTimeout,
        _ => null,
    };

    /// <summary><c>m:ss</c> com teto de segundos (45 s → <c>0:45</c>; 0 → <c>0:00</c>).</summary>
    public static string FormatTimer(double remainingSeconds)
    {
        var s = (int)Math.Ceiling(Math.Max(0, remainingSeconds));
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>Lê o encontro (sem alterá-lo). <paramref name="wave"/> e <paramref name="topic"/> vêm do chefe/fase (cabeçalho <c>CHEFE · ONDA 40 · IAAS</c>).</summary>
    public static QuestionCardModel From(BossEncounter encounter, int wave, string topic)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        var q = encounter.Question;
        var texts = new string[q.Options.Length];
        var visuals = new OptionVisual[q.Options.Length];
        for (var i = 0; i < texts.Length; i++)
            texts[i] = $"{(char)('A' + i)} · {q.Options[i].Text}";

        var state = QuestionCardState.Waiting;
        string? explanation = null;
        if (encounter.Outcome is { } o)
        {
            state = o.Result switch
            {
                BossResult.Correct => QuestionCardState.Correct,
                BossResult.Wrong => QuestionCardState.Wrong,
                _ => QuestionCardState.TimedOut,
            };
            if (o.CorrectIndex >= 0) visuals[o.CorrectIndex] = OptionVisual.Correct;
            if (o.Result == BossResult.Wrong && o.OptionIndex is { } chosen) visuals[chosen] = OptionVisual.Wrong;
            explanation = o.Result == BossResult.Wrong && !string.IsNullOrWhiteSpace(o.ChosenExplanation)
                ? o.ChosenExplanation + " " + o.CorrectExplanation
                : o.CorrectExplanation;
        }
        return new(Strings.BossQuestionHeader(wave, topic), FormatTimer(encounter.Remaining),
            q.Problem, texts, visuals, state, explanation);
    }

    /// <summary>
    /// Card da pergunta do COLETAR ×2 (SDD-24 RF-24.9 reaproveitado; T6.4b): mesmo modelo e renderer do chefe, cabeçalho
    /// <c>COLETAR ×2 · FASE nn</c>. Leitura apenas da sessão.
    /// </summary>
    public static QuestionCardModel FromOfflineSession(Presence.OfflineQuestionSession session, int stage)
    {
        ArgumentNullException.ThrowIfNull(session);
        var q = session.Question;
        var texts = new string[q.Options.Length];
        var visuals = new OptionVisual[q.Options.Length];
        for (var i = 0; i < texts.Length; i++)
            texts[i] = $"{(char)('A' + i)} · {q.Options[i].Text}";
        var state = QuestionCardState.Waiting;
        string? explanation = null;
        if (session.Result is { } result)
        {
            state = result switch
            {
                BossResult.Correct => QuestionCardState.Correct,
                BossResult.Wrong => QuestionCardState.Wrong,
                _ => QuestionCardState.TimedOut,
            };
            var correct = q.CorrectIndex;
            if (correct >= 0) visuals[correct] = OptionVisual.Correct;
            if (result == BossResult.Wrong && session.OptionIndex is { } chosen) visuals[chosen] = OptionVisual.Wrong;
            var correctExp = correct >= 0 ? q.Options[correct].Explanation : "";
            var chosenExp = result == BossResult.Wrong && session.OptionIndex is { } c2 ? q.Options[c2].Explanation : null;
            explanation = !string.IsNullOrWhiteSpace(chosenExp) ? chosenExp + " " + correctExp : correctExp;
        }
        return new(Strings.CollectQuestionHeader(stage), FormatTimer(session.Remaining), q.Problem, texts, visuals, state, explanation);
    }

    /// <summary>Dados de teste do design (Presence.dc.html): onda 40, IAAS, 4 alternativas, 0:45, aguardando.</summary>
    public static QuestionCardModel TestData(QuestionCardState state = QuestionCardState.Waiting, int? chosen = null,
        double remaining = 45)
    {
        string[] options = ["A · A Microsoft, sempre", "B · Você (cliente)", "C · Ninguém, é automático", "D · O provedor de rede"];
        const int correct = 1;
        var visuals = new OptionVisual[4];
        string? explanation = null;
        if (state != QuestionCardState.Waiting)
        {
            visuals[correct] = OptionVisual.Correct;
            if (state == QuestionCardState.Wrong && chosen is { } c && c != correct) visuals[c] = OptionVisual.Wrong;
            explanation = "No IaaS o cliente gerencia o sistema operacional, inclusive os patches; a Microsoft cuida do host físico.";
        }
        return new("CHEFE · ONDA 40 · IAAS", state == QuestionCardState.TimedOut ? FormatTimer(0) : FormatTimer(remaining),
            "Numa Máquina Virtual do Azure, quem aplica os patches do sistema operacional?",
            options, visuals, state, explanation);
    }
}
