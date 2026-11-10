using System.Text.Json;
using System.Text.Json.Serialization;

namespace SideWar.Persistence.Profile;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ProfileData))]
internal sealed partial class ProfileJsonContext : JsonSerializerContext;

/// <summary>JSON do perfil (source-gen, sem reflexão, ADR-007) e saneamento por campo: valores fora da faixa voltam ao padrão do campo.</summary>
public static class ProfileSerializer
{
    public const int MaxStages = 64;
    public const int MaxListEntries = 4096;

    public static void Write(Utf8JsonWriter w, ProfileData p) =>
        JsonSerializer.Serialize(w, p, ProfileJsonContext.Default.ProfileData);

    /// <summary>Lança <see cref="JsonException"/> se o payload não tem o formato do DTO (o store trata como corrompido).</summary>
    public static ProfileData Read(JsonElement payload) =>
        Sanitize(payload.Deserialize(ProfileJsonContext.Default.ProfileData) ?? throw new JsonException("payload nulo"));

    public static ProfileData Sanitize(ProfileData p)
    {
        var d = ProfileData.Default;
        return p with
        {
            Level = p.Level >= 1 ? p.Level : d.Level,
            Xp = Math.Max(0, p.Xp),
            Reservas = Math.Max(0, p.Reservas),
            TotalKills = Math.Max(0, p.TotalKills),
            PlaySeconds = double.IsFinite(p.PlaySeconds) && p.PlaySeconds >= 0 ? p.PlaySeconds : 0,
            Stages = Cap(p.Stages, MaxStages)
                .Where(s => s is not null && s.StageId >= 1)
                .Select(s => s with { Stars = Math.Clamp(s.Stars, 0, 3), Attempt = Math.Max(1, s.Attempt) })
                .ToArray(),
            Region = string.IsNullOrEmpty(p.Region) ? d.Region : p.Region,
            UnlockedRegions = p.UnlockedRegions is { Count: > 0 } r ? Cap(r, MaxListEntries) : d.UnlockedRegions,
            UnlockedTowers = Cap(p.UnlockedTowers, MaxListEntries),
            Missions = p.Missions ?? d.Missions,
            Ledger = Cap(p.Ledger, MaxListEntries),
            DrawnQuestions = Cap(p.DrawnQuestions, MaxListEntries),
            CollectLedger = Cap(p.CollectLedger, MaxListEntries),
            Counters = p.Counters is { } c ? c with
            {
                Answers = Math.Max(0, c.Answers), PeaksScaled = Math.Max(0, c.PeaksScaled),
                CratesCollected = Math.Max(0, c.CratesCollected), Kills = Math.Max(0, c.Kills),
                ManualUpgrades = Math.Max(0, c.ManualUpgrades), PresentMinutesToday = Math.Max(0, c.PresentMinutesToday),
                EventClockSeconds = double.IsFinite(c.EventClockSeconds) && c.EventClockSeconds >= 0 ? c.EventClockSeconds : 0,
            } : d.Counters,
        };
    }

    private static IReadOnlyList<T> Cap<T>(IReadOnlyList<T>? list, int max) =>
        list is null ? [] : list.Count <= max ? list : list.Take(max).ToArray();
}
