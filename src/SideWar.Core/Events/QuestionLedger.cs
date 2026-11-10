namespace SideWar.Core.Events;

/// <summary>Chave do ledger: (fase, tentativa, onda) — SDD-26 RF-26.7.</summary>
public readonly record struct LedgerKey(int Stage, int Attempt, int Wave);

/// <summary>Valor do ledger. <c>OptionIndex</c> nulo = timeout.</summary>
public sealed record LedgerEntry(string QuestionId, int? OptionIndex, bool Correct, bool BonusConsumed)
{
    public bool TimedOut => OptionIndex is null;
}

public enum LedgerStatus { Ok, AlreadyUsed }

/// <summary>Ledger de respostas de chefe (SDD-26 RF-26.7). Segunda resposta à mesma chave é rejeitada.</summary>
public sealed class QuestionLedger
{
    /// <summary>Bônus de HP do chefe quando a resposta foi correta (SDD-26 RF-26.6).</summary>
    public const double CorrectBossHpMultiplier = 0.4;

    private readonly Dictionary<LedgerKey, LedgerEntry> _entries = [];

    public int Count => _entries.Count;

    public bool Has(LedgerKey key) => _entries.ContainsKey(key);

    public LedgerEntry? Get(LedgerKey key) => _entries.GetValueOrDefault(key);

    public LedgerStatus Record(LedgerKey key, string questionId, int? optionIndex, bool correct)
    {
        if (_entries.ContainsKey(key))
            return LedgerStatus.AlreadyUsed;
        _entries[key] = new LedgerEntry(questionId, optionIndex, correct && optionIndex is not null, false);
        return LedgerStatus.Ok;
    }

    /// <summary>Bônus pendente derivado: <c>correct &amp;&amp; !bonusConsumed</c>.</summary>
    public bool HasPendingBonus(LedgerKey key)
        => _entries.TryGetValue(key, out var e) && e.Correct && !e.BonusConsumed;

    /// <summary>Consome o bônus no spawn do chefe; true só na primeira vez (restore após o spawn não reaplica).</summary>
    public bool TryConsumeBonus(LedgerKey key)
    {
        if (!HasPendingBonus(key))
            return false;
        _entries[key] = _entries[key] with { BonusConsumed = true };
        return true;
    }

    /// <summary>Multiplicador de HP para o spawn do chefe: 0,4 se há bônus pendente (consome), senão 1.</summary>
    public double ConsumeBossHpMultiplier(LedgerKey key)
        => TryConsumeBonus(key) ? CorrectBossHpMultiplier : 1.0;

    /// <summary>Acertos de chefe na passagem (fase, tentativa) — insumo das estrelas 2 e 3 (SDD-27 RF-27.9).</summary>
    public int CorrectCount(int stage, int attempt)
        => _entries.Count(kv => kv.Key.Stage == stage && kv.Key.Attempt == attempt && kv.Value.Correct);

    /// <summary>Respostas (acerto ou erro, sem timeout) na passagem — insumo da missão "Responda 3 perguntas".</summary>
    public int AnsweredCount(int stage, int attempt)
        => _entries.Count(kv => kv.Key.Stage == stage && kv.Key.Attempt == attempt && !kv.Value.TimedOut);

    /// <summary>Entradas em ordem determinística (fase, tentativa, onda), para save e hash.</summary>
    public IReadOnlyList<KeyValuePair<LedgerKey, LedgerEntry>> Snapshot()
        => [.. _entries.OrderBy(kv => kv.Key.Stage).ThenBy(kv => kv.Key.Attempt).ThenBy(kv => kv.Key.Wave)];

    public void Restore(IEnumerable<KeyValuePair<LedgerKey, LedgerEntry>> entries)
    {
        _entries.Clear();
        foreach (var (k, v) in entries)
            _entries[k] = v;
    }
}
