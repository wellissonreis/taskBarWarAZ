using System.Text.Json;
using System.Text.Json.Serialization;

namespace SideWar.Persistence.Session;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(OfflineSnapshot))]
internal sealed partial class SessionJsonContext : JsonSerializerContext;

/// <summary>JSON da sessão (source-gen). Floats e doubles ida e volta exatos (round-trip padrão do STJ).</summary>
public static class SessionSerializer
{
    public const int MaxEntities = 4096;
    public const int MaxZones = SideWar.Core.Simulation.StripLayout.MaxFreeZones;
    public const int MaxTowers = SideWar.Core.Simulation.TowerStore.MaxTowers;

    public static void Write(Utf8JsonWriter w, OfflineSnapshot s) =>
        JsonSerializer.Serialize(w, Normalize(s), SessionJsonContext.Default.OfflineSnapshot);

    /// <summary>Lança <see cref="JsonException"/> se faltar campo obrigatório ou o formato não for o do DTO.</summary>
    public static OfflineSnapshot Read(JsonElement payload)
    {
        var s = payload.Deserialize(SessionJsonContext.Default.OfflineSnapshot) ?? throw new JsonException("payload nulo");
        if (string.IsNullOrEmpty(s.GameContentHash) || string.IsNullOrEmpty(s.StateHash)) throw new JsonException("hash ausente");
        if (s.SavedUtc == default) throw new JsonException("savedUtc ausente");
        if (s.Layout.FreeZones.Count > MaxZones || s.Sim.Towers.Count > MaxTowers || s.Sim.Enemies.Count > MaxEntities)
            throw new JsonException("listas acima do limite");
        return Normalize(s);
    }

    /// <summary>Datas sempre em UTC; nada mais é alterado (o restore precisa do estado exato).</summary>
    private static OfflineSnapshot Normalize(OfflineSnapshot s) => s with
    {
        SavedUtc = s.SavedUtc.ToUniversalTime(),
        Presence = s.Presence with { LastInteractionUtc = s.Presence.LastInteractionUtc?.ToUniversalTime() },
    };
}
