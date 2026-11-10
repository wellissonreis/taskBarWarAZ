using System.Text.Json;
using System.Text.Json.Serialization;

namespace SideWar.Core.Events;

/// <summary>Alternativa de uma pergunta (SDD-19 RF-19.10, SDD-26 RF-26.9).</summary>
public sealed record QuestionOption(string Text, bool Correct, string Explanation);

/// <summary>Pergunta do banco. <c>Signals</c> é opcional nas perguntas novas (DP-26.1). <c>Kind</c>: mandatory | transfer.</summary>
public sealed record QuestionDef(
    string Id, string StageTopic, string Kind, string Problem, QuestionOption[] Options, string[]? Signals = null)
{
    /// <summary>Índice da única alternativa correta (-1 se o banco for inválido).</summary>
    [JsonIgnore]
    public int CorrectIndex => Array.FindIndex(Options, o => o.Correct);
}

/// <summary>Banco de perguntas (questions.json). <c>Provisional</c>: conteúdo ainda sem revisão humana.</summary>
public sealed record QuestionBank(bool Provisional, QuestionDef[] Questions)
{
    public IReadOnlyList<QuestionDef> ForTopic(string topic)
        => Questions.Where(q => string.Equals(q.StageTopic, topic, StringComparison.Ordinal)).ToArray();

    public QuestionDef? Find(string id)
        => Questions.FirstOrDefault(q => string.Equals(q.Id, id, StringComparison.Ordinal));
}

/// <summary>Fonte de sorteio. Na integração, adapta o <c>Rng</c> PCG32 persistido (SDD-11 RF-11.13).</summary>
public interface IQuestionRng
{
    /// <summary>Inteiro em [0, <paramref name="exclusiveMax"/>).</summary>
    int NextInt(int exclusiveMax);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    ReadCommentHandling = JsonCommentHandling.Disallow)]
[JsonSerializable(typeof(QuestionBank))]
internal sealed partial class QuestionJsonContext : JsonSerializerContext;
