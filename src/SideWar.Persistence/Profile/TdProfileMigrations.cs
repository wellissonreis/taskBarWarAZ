using System.Text.Json.Nodes;
using SideWar.Persistence.Migrations;

namespace SideWar.Persistence.Profile;

/// <summary>Migrações do schema <c>sidewar.td-profile</c> (SDD-14 RF-14.10).</summary>
public static class TdProfileMigrations
{
    /// <summary>v1 → v2 (TD.13): cada fase ganha <c>crown=false</c>, <c>medals=0</c>, <c>bestScore=0</c>; nada se perde.</summary>
    public static MigrationRegistry Default { get; } = new MigrationRegistry().Register(TdProfileData.Schema, 1, V1ToV2);

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
