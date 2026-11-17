using System.Text.Json;
using System.Text.Json.Serialization;

namespace SideWar.Core.Content.Td;

/// <summary>Afirmação Verdadeiro/Falso do Preparo (RF-28.23): <c>Text</c> ≤ 60 caracteres e <c>Explanation</c> ≤ 8 palavras.</summary>
public sealed record TdQuestion(string Id, string Text, bool Answer, string Explanation);

public sealed record TdQuestionBank(TdQuestion[] Questions)
{
    public const int MaxTextLength = 60;
    public const int MaxExplanationWords = 8;
    public const int MinQuestions = 60;

    /// <summary>Banco embarcado (<c>td-questions.json</c>); fora do ContentHash TD: não influencia a simulação.</summary>
    public static TdQuestionBank LoadEmbedded()
    {
        using var stream = typeof(TdQuestionBank).Assembly.GetManifestResourceStream("SideWar.Core.Content.td-questions.json")
            ?? throw new InvalidOperationException("td-questions.json não embarcado.");
        var bank = JsonSerializer.Deserialize(stream, TdQuestionJsonContext.Default.TdQuestionBank)
            ?? throw new InvalidOperationException("td-questions.json vazio.");
        var errors = Validate(bank);
        if (errors.Count > 0) throw new ContentException(errors);
        return bank;
    }

    public static List<string> Validate(TdQuestionBank bank)
    {
        var e = new List<string>();
        if (bank.Questions.Length < MinQuestions) e.Add($"td-questions: mínimo de {MinQuestions} afirmações");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int t = 0;
        foreach (var q in bank.Questions)
        {
            if (string.IsNullOrWhiteSpace(q.Id) || !ids.Add(q.Id)) e.Add($"td-questions: id vazio ou repetido ({q.Id})");
            if (string.IsNullOrWhiteSpace(q.Text) || q.Text.Length > MaxTextLength) e.Add($"td-questions.{q.Id}: texto vazio ou > {MaxTextLength} caracteres");
            if (string.IsNullOrWhiteSpace(q.Explanation) || q.Explanation.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxExplanationWords)
                e.Add($"td-questions.{q.Id}: explicação vazia ou > {MaxExplanationWords} palavras");
            if (q.Answer) t++;
        }
        int f = bank.Questions.Length - t;
        if (bank.Questions.Length > 0 && Math.Abs(t - f) * 5 > bank.Questions.Length) e.Add($"td-questions: V/F desequilibrado ({t}/{f})");
        return e;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(TdQuestionBank))]
internal sealed partial class TdQuestionJsonContext : JsonSerializerContext;
