using System.Text.Json.Nodes;
using SideWar.Core.Td;
using SideWar.Persistence.Migrations;

namespace SideWar.Persistence.Profile;

/// <summary>Migrações do schema <c>sidewar.td-profile</c> (SDD-14 RF-14.10).</summary>
public static class TdProfileMigrations
{
    /// <summary>v1 → v2: resultados completos; v2 → v3: reembolso das compras obsoletas e saneamento dos nós.</summary>
    public static MigrationRegistry Default { get; } = new MigrationRegistry()
        .Register(TdProfileData.Schema, 1, V1ToV2)
        .Register(TdProfileData.Schema, 2, V2ToV3)
        .Register(TdProfileData.Schema, 3, V3ToV4)
        .Register(TdProfileData.Schema, 4, V4ToV5)
        .Register(TdProfileData.Schema, 5, V5ToV6)
        .Register(TdProfileData.Schema, 6, V6ToV7)
        .Register(TdProfileData.Schema, 7, V7ToV8)
        .Register(TdProfileData.Schema, 8, V8ToV9)
        .Register(TdProfileData.Schema, 9, V9ToV10)
        .Register(TdProfileData.Schema, 10, V10ToV11);

    /// <summary>v10 → v11: modo sombra desligado e sem resto de selos.</summary>
    private static JsonNode V10ToV11(JsonNode root)
    {
        root["shadow"] ??= false;
        root["shadowSealCents"] ??= 0;
        return root;
    }

    /// <summary>v9 → v10: torres da corrida salvas no perfil; perfis antigos começam sem torres salvas.</summary>
    private static JsonNode V9ToV10(JsonNode root)
    {
        root["runTowers"] ??= new JsonArray();
        return root;
    }

    private static JsonNode V8ToV9(JsonNode root)
    {
        if (root["reputation"] is not JsonValue value || !value.TryGetValue<int>(out var xp)) return root;
        int[] oldThresholds = [0, 4_500, 24_000, 68_000, 145_000, 300_000];
        int rank = 0;
        for (int i = 1; i < oldThresholds.Length; i++) if (xp >= oldThresholds[i]) rank = i;
        root["reputation"] = TdRank.Threshold((TdRankLevel)rank);
        return root;
    }

    private static JsonNode V7ToV8(JsonNode root)
    {
        root["encounters"] ??= new JsonArray();
        return root;
    }

    /// <summary>
    /// v6 → v7: corrida infinita. Sem corrida salva (<c>runStageNumber</c> = 0: abre na fase sugerida, como antes). O recorde nasce das fases já
    /// vencidas: fase seguinte à mais alta com estrelas, onda 0; perfil sem estrelas fica sem recorde.
    /// </summary>
    private static JsonNode V6ToV7(JsonNode root)
    {
        root["runStageNumber"] ??= 0;
        if (root["bestStageNumber"] is null)
        {
            int best = 0;
            if (root["stages"] is JsonArray stages)
                foreach (var st in stages)
                    if (st is JsonObject o && o["stars"] is JsonValue sv && sv.TryGetValue<int>(out var stars) && stars > 0
                        && o["stageId"] is JsonValue iv && iv.TryGetValue<int>(out var id))
                        best = Math.Max(best, id + 1);
            root["bestStageNumber"] = best;
            root["bestStageWave"] = 0;
        }
        return root;
    }

    /// <summary>
    /// v5 → v6: as runas passam a ser progressão permanente. O Legado antigo (<c>legacyTotal</c>) vira nós iniciais, como antes
    /// (min(3, total/6) runas): os primeiros nós do anel interno (slots 0..n-1, sempre despertáveis), sem sorteio. Sem fragmentos.
    /// </summary>
    private static JsonNode V5ToV6(JsonNode root)
    {
        if (root["runeWeb"] is not null) return root;
        int total = root["legacyTotal"] is JsonValue v && v.TryGetValue<int>(out var t) ? Math.Max(0, t) : 0;
        int n = TdLegacy.StartingRunes(total);
        root["runeWeb"] = TdRuneWeb.ToText((UInt128.One << n) - UInt128.One);
        root["runeFragments"] ??= 0;
        return root;
    }

    /// <summary>v4 → v5: reputação inicial = soma dos recordes de pontuação por fase (o que definia a patente), sem perder progresso.</summary>
    private static JsonNode V4ToV5(JsonNode root)
    {
        if (root["reputation"] is not null) return root;
        long sum = 0;
        if (root["stages"] is JsonArray stages)
            foreach (var st in stages)
                if (st is JsonObject o && o["bestScore"] is JsonValue v && v.TryGetValue<int>(out var s)) sum += Math.Max(0, s);
        root["reputation"] = (int)Math.Min(TdProfileSerializer.MaxScore, sum);
        return root;
    }

    private static JsonNode V3ToV4(JsonNode root)
    {
        root["legacyTotal"] ??= 0;
        root["bestRunWaves"] ??= 0;
        root["legacyLastRun"] ??= "";
        root["runsPlayed"] ??= 0;
        return root;
    }

    // SealsEarned é cumulativo; remover compras obsoletas reembolsa seu custo integral
    // por SealsAvailable, sem conceder selos novos nem comprar os substitutos automaticamente.
    private static JsonNode V2ToV3(JsonNode root)
    {
        if (root["purchasedNodes"] is JsonArray nodes)
        {
            var ids = nodes.Select(n => n?.GetValue<string>()).Where(n => n is not null).Cast<string>().ToArray();
            root["purchasedNodes"] = new JsonArray(TdProfileSerializer.SanitizeNodes(ids)
                .Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        }
        return root;
    }

    private static JsonNode V1ToV2(JsonNode root)
    {
        if (root["stages"] is JsonArray stages)
            foreach (var st in stages)
                if (st is JsonObject o)
                {
                    o["crown"] ??= false;
                    o["medals"] ??= 0;
                    o["bestScore"] ??= 0;
                }
        return root;
    }
}
