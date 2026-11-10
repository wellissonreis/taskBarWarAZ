namespace SideWar.Core.Events;

/// <summary>Validador do banco (SDD-26 RF-26.9, CA-26.8). Acumula todos os erros.</summary>
public static class QuestionBankValidator
{
    public const int RecommendedPerTopic = 8;
    private static readonly string[] Kinds = ["mandatory", "transfer"];

    /// <summary>Mínimo duro por tópico: maior número de chefes (waves/bossEvery) entre as fases do tópico.</summary>
    public static Dictionary<string, int> MinimumPerTopic(IEnumerable<Content.StageDef> stages, int bossEvery)
    {
        var min = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in stages)
            min[s.StageTopic] = Math.Max(min.GetValueOrDefault(s.StageTopic), s.Waves / bossEvery);
        return min;
    }

    public static IReadOnlyList<string> Validate(QuestionBank bank, IEnumerable<Content.StageDef> stages, int bossEvery)
    {
        var e = new List<string>();
        var min = MinimumPerTopic(stages, bossEvery);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in bank.Questions)
        {
            var id = q.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                e.Add("questions: id vazio");
                id = "?";
            }
            else if (!ids.Add(id))
                e.Add($"questions: id duplicado '{id}'");
            if (!min.ContainsKey(q.StageTopic))
                e.Add($"questions.{id}: stageTopic desconhecido '{q.StageTopic}'");
            if (!Kinds.Contains(q.Kind, StringComparer.Ordinal))
                e.Add($"questions.{id}: kind inválido '{q.Kind}'");
            if (string.IsNullOrWhiteSpace(q.Problem))
                e.Add($"questions.{id}: problem vazio");
            if (q.Signals is { Length: > 0 } && q.Signals.Length != 3)
                e.Add($"questions.{id}: signals deve ter exatamente 3 quando presente");
            if (q.Signals is not null && q.Signals.Any(string.IsNullOrWhiteSpace))
                e.Add($"questions.{id}: signal vazio");
            if (q.Options.Length is < 3 or > 4)
                e.Add($"questions.{id}: options deve ter 3 ou 4 itens (tem {q.Options.Length})");
            var correct = q.Options.Count(o => o.Correct);
            if (correct != 1)
                e.Add($"questions.{id}: deve haver exatamente uma alternativa correta (tem {correct})");
            if (q.Options.Any(o => string.IsNullOrWhiteSpace(o.Text) || string.IsNullOrWhiteSpace(o.Explanation)))
                e.Add($"questions.{id}: texto ou explicação de alternativa vazio");
        }
        foreach (var (topic, need) in min)
        {
            var have = bank.Questions.Count(q => q.StageTopic == topic);
            if (have < need)
                e.Add($"questions: tópico '{topic}' tem {have} perguntas; mínimo {need}");
        }
        return e;
    }

    /// <summary>Avisos (não bloqueiam): tópicos abaixo do tamanho proposto por DP-26.3.</summary>
    public static IReadOnlyList<string> Warnings(QuestionBank bank, IEnumerable<Content.StageDef> stages, int bossEvery)
        => [.. MinimumPerTopic(stages, bossEvery).Keys
            .Where(t => bank.Questions.Count(q => q.StageTopic == t) < RecommendedPerTopic)
            .Select(t => $"questions: tópico '{t}' abaixo do recomendado ({RecommendedPerTopic}, DP-26.3)")];
}
