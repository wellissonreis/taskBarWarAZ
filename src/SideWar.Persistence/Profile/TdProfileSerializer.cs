using System.Text.Json;
using System.Text.Json.Serialization;
using SideWar.Core.Content.Td;
using SideWar.Core.Td;

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
            LegacyTotal = Math.Clamp(p.LegacyTotal, 0, TdLegacy.MaxTotal),
            BestRunWaves = Math.Max(0, p.BestRunWaves),
            LegacyLastRun = p.LegacyLastRun ?? "",
            RunsPlayed = Math.Max(0, p.RunsPlayed),
            RuneWeb = TdRuneWeb.ToText(TdRuneWeb.Parse(p.RuneWeb)),
            RuneFragments = Math.Clamp(p.RuneFragments, 0, TdRuneWeb.MaxFragments),
            Reputation = Math.Clamp(p.Reputation, 0, MaxScore),
            RunStageNumber = Math.Clamp(p.RunStageNumber, 0, MaxWave),
            BestStageNumber = Math.Clamp(p.BestStageNumber, 0, MaxWave),
            BestStageWave = p.BestStageNumber <= 0 ? 0 : Math.Clamp(p.BestStageWave, 0, MaxWave),
            SealsEarned = earned,
            MigratedSeals = Math.Clamp(p.MigratedSeals, 0, earned),
            Stages = stages,
            PurchasedNodes = SanitizeNodes(p.PurchasedNodes),
            UnlockedTowers = Distinct(p.UnlockedTowers),
            RunTowers = (p.RunTowers ?? []).Where(t => t is not null && !string.IsNullOrWhiteSpace(t.DefId)).Take(TdMatchState.TechnicalTowerLimit)
                .Select(t => t with { X = Math.Clamp(t.X, 0, (int)TdWorld.PathLength), PurchasePrice = Math.Clamp(t.PurchasePrice, 0, MaxScore), ModulePrice = Math.Clamp(t.ModulePrice, 0, MaxScore),
                    Targeting = Math.Clamp(t.Targeting, 0, 4), ModuleDefId = string.IsNullOrWhiteSpace(t.ModuleDefId) ? null : t.ModuleDefId }).ToArray(),
            Encounters = (p.Encounters ?? []).Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Id) && e.Count > 0)
                .GroupBy(e => e.Id, StringComparer.Ordinal).Select(g => new TdEncounterData { Id = g.Key, Count = Math.Clamp(g.Max(e => e.Count), 1, MaxScore) })
                .Take(MaxListEntries).ToArray(),
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
