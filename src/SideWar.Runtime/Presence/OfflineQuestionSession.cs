using SideWar.Core.Events;

namespace SideWar.Runtime.Presence;

/// <summary>
/// Pergunta Azure do COLETAR ×2 (SDD-25 RF-25.9, SDD-26 RF-26.16): 45 s de SimTime, uma resposta por relatório,
/// não conta para estrelas nem missões. Modelo puro: o sorteio (mesmo conjunto de usadas do tema da fase) e o ledger por
/// relatório são do Core (pendência registrada no REPORT); aqui entram a pergunta já sorteada e o tempo.
/// </summary>
public sealed class OfflineQuestionSession(long reportId, QuestionDef question, double limitSeconds = BossEncounter.TimeLimitSeconds)
{
    public long ReportId { get; } = reportId;
    public QuestionDef Question { get; } = question ?? throw new ArgumentNullException(nameof(question));
    public double Elapsed { get; private set; }
    public double Remaining => Math.Max(0, limitSeconds - Elapsed);
    public BossResult? Result { get; private set; }
    public int? OptionIndex { get; private set; }
    public bool IsResolved => Result is not null;

    /// <summary>Avança o SimTime; true se acabou de esgotar (resultado <see cref="BossResult.Timeout"/>).</summary>
    public bool Advance(double dtSeconds)
    {
        if (IsResolved) return false;
        Elapsed += dtSeconds;
        if (Elapsed < limitSeconds) return false;
        Elapsed = limitSeconds;
        Result = BossResult.Timeout;
        return true;
    }

    /// <summary>Responde com o índice da alternativa; devolve true se acertou. Já resolvida: false e nada muda.</summary>
    public bool Answer(int optionIndex)
    {
        if (IsResolved) return false;
        ArgumentOutOfRangeException.ThrowIfNegative(optionIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(optionIndex, Question.Options.Length);
        var ok = Question.Options[optionIndex].Correct;
        Result = ok ? BossResult.Correct : BossResult.Wrong;
        OptionIndex = optionIndex;
        return ok;
    }
}
