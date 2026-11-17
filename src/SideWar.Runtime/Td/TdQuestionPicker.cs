using SideWar.Core.Content.Td;

namespace SideWar.Runtime.Td;

/// <summary>
/// Sorteio da afirmação da pergunta-bônus (TD.17, RF-28.23) sem repetir até esgotar o banco. O "visto" é uma string <c>id;id</c>
/// persistida pelo chamador (settings); ao esgotar, recomeça. Estado de UI: o sorteio não é determinístico e nunca entra na simulação
/// (o log registra só o <c>AnswerBonus</c> com o id e o resultado).
/// </summary>
public sealed class TdQuestionPicker
{
    private readonly TdQuestionBank _bank;
    private readonly Func<int, int> _next;
    private readonly Action<string> _persist;
    private readonly HashSet<string> _seen;

    public TdQuestionPicker(TdQuestionBank bank, string seen, Func<int, int> next, Action<string> persist)
    {
        _bank = bank ?? throw new ArgumentNullException(nameof(bank));
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _seen = new HashSet<string>((seen ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    }

    public string Seen => string.Join(';', _seen.Order(StringComparer.Ordinal));
    public int Count => _bank.Questions.Length;

    /// <summary>Próxima afirmação não vista (<paramref name="forceId"/> escolhe uma específica, ex.: a de exemplo do tutorial).</summary>
    public TdQuestion? Pick(string? forceId = null)
    {
        if (_bank.Questions.Length == 0) return null;
        TdQuestion? q = null;
        if (forceId is not null) q = Array.Find(_bank.Questions, x => x.Id == forceId);
        if (q is null)
        {
            var pool = _bank.Questions.Where(x => !_seen.Contains(x.Id)).ToArray();
            if (pool.Length == 0) { _seen.Clear(); pool = _bank.Questions; }
            q = pool[Math.Clamp(_next(pool.Length), 0, pool.Length - 1)];
        }
        _seen.Add(q.Id);
        _persist(Seen);
        return q;
    }
}
