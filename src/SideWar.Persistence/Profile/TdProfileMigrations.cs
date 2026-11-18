using System.Text.Json.Nodes;
using SideWar.Persistence.Migrations;

namespace SideWar.Persistence.Profile;

/// <summary>Migrações do schema <c>sidewar.td-profile</c> (SDD-14 RF-14.10).</summary>
public static class TdProfileMigrations
{
    /// <summary>v1 → v2: resultados completos; v2 → v3: reembolso das compras obsoletas e saneamento dos nós.</summary>
    public static MigrationRegistry Default { get; } = new MigrationRegistry()
        .Register(TdProfileData.Schema, 1, V1ToV2)
        .Register(TdProfileData.Schema, 2, V2ToV3)
        .Register(TdProfileData.Schema, 3, V3ToV4);

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
