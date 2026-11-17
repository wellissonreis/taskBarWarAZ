using System.Text.Json;
using System.Text.Json.Serialization;
using SideWar.Core.Content.Td;

namespace SideWar.Persistence.Profile;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(TdProfileData))]
internal sealed partial class TdProfileJsonContext : JsonSerializerContext;

/// <summary>JSON do perfil v3 (source-gen, ADR-007) e saneamento por campo (valores fora da faixa voltam ao padrão do campo).</summary>
public static class TdProfileSerializer
{
    private static readonly HashSet<string> KnownNodes = TdContentLoader.LoadEmbedded().Meta.Branches
        .SelectMany(b => b.Nodes).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

    internal static string[] SanitizeNodes(IReadOnlyList<string>? nodes) =>
        Distinct(nodes).Where(KnownNodes.Contains).ToArray();

    public const int MaxStages = 64;
    public const int MaxListEntries = 4096;
    public const int MaxWave = 1_000_000;
    public const int MaxScore = 100_000_000;
    /// <summary>Máscara das medalhas conhecidas (<c>TdMedals</c>: 1, 2, 4, 8).</summary>
    public const int AllMedals = 15;

    public static void Write(Utf8JsonWriter w, TdProfileData p) =>
        JsonSerializer.Serialize(w, p, TdProfileJsonContext.Default.TdProfileData);

    /// <summary>Lança <see cref="JsonException"/> se o payload não tem o formato do DTO (o store trata como corrompido).</summary>
    public static TdProfileData Read(JsonElement payload) =>
        Sanitize(payload.Deserialize(TdProfileJsonContext.Default.TdProfileData) ?? throw new JsonException("payload nulo"));

    public static TdProfileData Sanitize(TdProfileData p)
    {
        var stages = (p.Stages ?? [])
            .Where(s => s is not null && s.StageId >= 1)
            .GroupBy(s => s.StageId)
            .Select(g => new TdStageProgressData
            {
                StageId = g.Key,
                Unlocked = g.Any(s => s.Unlocked) || g.Any(s => s.Stars > 0),
                Stars = Math.Clamp(g.Max(s => s.Stars), 0, 3),
                ChallengeCompleted = g.Any(s => s.ChallengeCompleted),
                Crown = g.Any(s => s.Crown),
                Medals = g.Aggregate(0, (a, s) => a | s.Medals) & AllMedals,
                BestScore = Math.Clamp(g.Max(s => s.BestScore), 0, MaxScore),
            })
            .OrderBy(s => s.StageId).Take(MaxStages).ToList();
        var stage1 = stages.FindIndex(s => s.StageId == 1);
        if (stage1 < 0) stages.Insert(0, new TdStageProgressData { StageId = 1, Unlocked = true });
        else if (!stages[stage1].Unlocked) stages[stage1] = stages[stage1] with { Unlocked = true };

        var earned = Math.Max(0, p.SealsEarned);
        return p with
        {
            SealsEarned = earned,
            MigratedSeals = Math.Clamp(p.MigratedSeals, 0, earned),
            Stages = stages,
            PurchasedNodes = SanitizeNodes(p.PurchasedNodes),
            UnlockedTowers = Distinct(p.UnlockedTowers),
            InfiniteRuns = (p.InfiniteRuns ?? [])
                .Where(r => r is not null)
                .GroupBy(r => r.Seed)
                .Select(g => new TdInfiniteRunData { Seed = g.Key, MaxWave = Math.Clamp(g.Max(r => r.MaxWave), 0, MaxWave) })
                .OrderBy(r => r.Seed).Take(MaxListEntries).ToArray(),
        };
    }

    private static string[] Distinct(IReadOnlyList<string>? list) =>
        (list ?? []).Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.Ordinal).Take(MaxListEntries).ToArray();
}
