namespace SideWar.Core.Events;

/// <summary>Entrada do ledger do COLETAR ×2. <c>Answered = false</c>: pergunta sorteada, ainda sem resposta.</summary>
public sealed record OfflineLedgerEntry(string QuestionId, bool Answered, int? OptionIndex, bool Correct);

public enum OfflineLedgerStatus { Ok, NotDrawn, AlreadyDrawn, AlreadyAnswered }

/// <summary>
/// Ledger do COLETAR ×2 por relatório de retorno (SDD-26 RF-26.16), separado do ledger de chefes (fase, tentativa, onda).
/// Um relatório sorteia e responde no máximo uma vez. Não alimenta estrelas nem missões (SDD-27 RF-27.9/27.11).
/// </summary>
public sealed class OfflineQuestionLedger
{
    private readonly Dictionary<long, OfflineLedgerEntry> _entries = [];

    public int Count => _entries.Count;

    public bool Has(long reportId) => _entries.ContainsKey(reportId);

    public OfflineLedgerEntry? Get(long reportId) => _entries.GetValueOrDefault(reportId);

    /// <summary>Maior id de relatório já usado (0 se vazio): o host nunca reutiliza ids.</summary>
    public long MaxReportId => _entries.Count == 0 ? 0 : _entries.Keys.Max();

    public OfflineLedgerStatus RecordDraw(long reportId, string questionId)
    {
        if (_entries.ContainsKey(reportId)) return OfflineLedgerStatus.AlreadyDrawn;
        _entries[reportId] = new OfflineLedgerEntry(questionId, false, null, false);
        return OfflineLedgerStatus.Ok;
    }

    /// <summary><paramref name="optionIndex"/> nulo = tempo esgotado ou card fechado (×1).</summary>
    public OfflineLedgerStatus RecordAnswer(long reportId, int? optionIndex, bool correct)
    {
        if (!_entries.TryGetValue(reportId, out var e)) return OfflineLedgerStatus.NotDrawn;
        if (e.Answered) return OfflineLedgerStatus.AlreadyAnswered;
        _entries[reportId] = e with { Answered = true, OptionIndex = optionIndex, Correct = correct && optionIndex is not null };
        return OfflineLedgerStatus.Ok;
    }

    /// <summary>Entradas por id crescente (save e hash).</summary>
    public IReadOnlyList<KeyValuePair<long, OfflineLedgerEntry>> Snapshot()
        => [.. _entries.OrderBy(kv => kv.Key)];

    public void Restore(IEnumerable<KeyValuePair<long, OfflineLedgerEntry>> entries)
    {
        _entries.Clear();
        foreach (var (k, v) in entries) _entries[k] = v;
    }
}
