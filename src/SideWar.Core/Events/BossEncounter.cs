namespace SideWar.Core.Events;

public enum BossResult { Correct, Wrong, Timeout }

/// <summary>Resultado puro do encontro (SDD-26 RF-26.6). Aplicação ao spawn/baú/estrelas é da parte B.</summary>
public sealed record BossOutcome(
    BossResult Result, string QuestionId, int? OptionIndex, int CorrectIndex,
    string? ChosenExplanation, string CorrectExplanation)
{
    /// <summary>Acerto: chefe entra com HP ×0,4 (derivado do ledger no spawn); senão força total.</summary>
    public double BossHpMultiplier => Result == BossResult.Correct ? QuestionLedger.CorrectBossHpMultiplier : 1.0;
    public bool GrantsCommonChest => Result == BossResult.Correct;
    public bool CountsForStars => Result == BossResult.Correct;
    public bool CountsForMission => Result != BossResult.Timeout;
}

/// <summary>Estado persistível de um encontro em espera (RF-26.5: a pergunta é regravada no restore).</summary>
public readonly record struct BossEncounterState(LedgerKey Key, string QuestionId, double Elapsed);

/// <summary>
/// Encontro de chefe com pergunta (SDD-26 RF-26.4 a RF-26.10), modelo puro: sem simulação, UI ou relógio.
/// O tempo corre por <see cref="Advance"/> (SimTime), com o card aberto ou fechado.
/// </summary>
public sealed class BossEncounter
{
    public const double TimeLimitSeconds = 45.0;

    private readonly QuestionLedger _ledger;
    private readonly QuestionSelector _selector;

    private BossEncounter(QuestionSelector selector, QuestionLedger ledger,
        LedgerKey key, QuestionDef question, double elapsed)
    {
        _selector = selector;
        _ledger = ledger;
        Key = key;
        Question = question;
        Elapsed = elapsed;
    }

    public LedgerKey Key { get; }
    public QuestionDef Question { get; }
    public double Elapsed { get; private set; }
    public double Remaining => Math.Max(0, TimeLimitSeconds - Elapsed);
    public bool CardOpen { get; private set; }
    public bool IsCancelled { get; private set; }
    public BossOutcome? Outcome { get; private set; }
    public bool IsResolved => Outcome is not null;
    public bool IsActive => !IsResolved && !IsCancelled;

    public BossEncounterState State => new(Key, Question.Id, Elapsed);

    /// <summary>
    /// Abre o encontro ao aparecer o banner: sorteia a pergunta (RF-26.5). Retorna <c>null</c> se o ledger já
    /// tem resposta para a chave (RF-26.4): a onda começa direto, com o efeito derivado do ledger.
    /// </summary>
    public static BossEncounter? Begin(QuestionBank bank, QuestionSelector selector, QuestionLedger ledger,
        LedgerKey key, string topic, IQuestionRng rng)
    {
        if (ledger.Has(key))
            return null;
        var question = selector.Draw(key.Stage, key.Attempt, topic, rng);
        return new BossEncounter(selector, ledger, key, question, 0);
    }

    /// <summary>Restore anterior à resposta: regrava a mesma pergunta e o tempo decorrido; null se o ledger já tem a chave.</summary>
    public static BossEncounter? Restore(QuestionBank bank, QuestionSelector selector, QuestionLedger ledger,
        BossEncounterState state)
    {
        if (ledger.Has(state.Key))
            return null;
        var question = bank.Find(state.QuestionId)
            ?? throw new InvalidOperationException($"Pergunta '{state.QuestionId}' ausente do banco.");
        return new BossEncounter(selector, ledger, state.Key, question, state.Elapsed);
    }

    public void OpenCard()
    {
        if (IsActive)
            CardOpen = true;
    }

    /// <summary>Fechar ou minimizar o card não altera o tempo (RF-26.10).</summary>
    public void CloseCard() => CardOpen = false;

    /// <summary>Avança o SimTime; ao chegar em 45 s resolve como timeout e grava o ledger.</summary>
    public void Advance(double dtSeconds)
    {
        if (!IsActive)
            return;
        Elapsed += dtSeconds;
        if (Elapsed >= TimeLimitSeconds)
        {
            Elapsed = TimeLimitSeconds;
            Resolve(BossResult.Timeout, null);
        }
    }

    /// <summary>Responde com o índice da alternativa. <c>AlreadyUsed</c> se já resolvido ou se o ledger já tem a chave.</summary>
    public LedgerStatus Answer(int optionIndex)
    {
        if (!IsActive || _ledger.Has(Key))
            return LedgerStatus.AlreadyUsed;
        if (optionIndex < 0 || optionIndex >= Question.Options.Length)
            throw new ArgumentOutOfRangeException(nameof(optionIndex));
        Resolve(Question.Options[optionIndex].Correct ? BossResult.Correct : BossResult.Wrong, optionIndex);
        return LedgerStatus.Ok;
    }

    /// <summary>
    /// Cancela sem gravar o ledger (Ausente/Offline, RF-26.8): a pergunta volta ao banco e é sorteada de novo
    /// em um novo encontro.
    /// </summary>
    public void Cancel()
    {
        if (!IsActive)
            return;
        IsCancelled = true;
        CardOpen = false;
        _selector.Release(Key.Stage, Key.Attempt, Question.Id);
    }

    private void Resolve(BossResult result, int? optionIndex)
    {
        var correctIndex = Question.CorrectIndex;
        _ledger.Record(Key, Question.Id, optionIndex, result == BossResult.Correct);
        Outcome = new BossOutcome(result, Question.Id, optionIndex, correctIndex,
            optionIndex is { } i ? Question.Options[i].Explanation : null,
            Question.Options[correctIndex].Explanation);
        CardOpen = false;
    }
}
