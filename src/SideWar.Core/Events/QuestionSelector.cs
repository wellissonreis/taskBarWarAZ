namespace SideWar.Core.Events;

/// <summary>
/// Sorteio de perguntas sem repetição por (fase, tentativa), com reciclagem ao esgotar (SDD-26 RF-26.5, CA-26.4).
/// O conjunto de usadas é persistido (Snapshot/Restore).
/// </summary>
public sealed class QuestionSelector(QuestionBank bank)
{
    private readonly Dictionary<(int Stage, int Attempt), List<string>> _used = [];

    /// <summary>Sorteia uma pergunta do tópico; se todas já foram usadas, zera o conjunto antes de sortear. Consome 1 valor do rng.</summary>
    public QuestionDef Draw(int stage, int attempt, string topic, IQuestionRng rng)
    {
        var topicQuestions = bank.ForTopic(topic);
        if (topicQuestions.Count == 0)
            throw new InvalidOperationException($"Banco sem perguntas para o tópico '{topic}'.");
        if (!_used.TryGetValue((stage, attempt), out var used))
            _used[(stage, attempt)] = used = [];
        var candidates = topicQuestions.Where(q => !used.Contains(q.Id)).ToList();
        if (candidates.Count == 0)
        {
            used.RemoveAll(id => topicQuestions.Any(q => q.Id == id));
            candidates = [.. topicQuestions];
        }
        var picked = candidates[rng.NextInt(candidates.Count)];
        used.Add(picked.Id);
        return picked;
    }

    /// <summary>Devolve a pergunta ao banco (encontro cancelado, RF-26.8).</summary>
    public void Release(int stage, int attempt, string questionId)
    {
        if (_used.TryGetValue((stage, attempt), out var used))
            used.Remove(questionId);
    }

    public IReadOnlyList<string> Used(int stage, int attempt)
        => _used.TryGetValue((stage, attempt), out var u) ? [.. u] : [];

    public IReadOnlyList<((int Stage, int Attempt) Key, string[] Used)> Snapshot()
        => [.. _used.OrderBy(kv => kv.Key.Stage).ThenBy(kv => kv.Key.Attempt).Select(kv => (kv.Key, kv.Value.ToArray()))];

    public void Restore(IEnumerable<((int Stage, int Attempt) Key, string[] Used)> sets)
    {
        _used.Clear();
        foreach (var (key, used) in sets)
            _used[key] = [.. used];
    }
}
