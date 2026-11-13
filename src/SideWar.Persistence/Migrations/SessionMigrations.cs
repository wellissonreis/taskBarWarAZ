using System.Text.Json.Nodes;
using SideWar.Persistence.Session;

namespace SideWar.Persistence.Migrations;

/// <summary>Migrações de <c>sidewar.session</c> embutidas (SDD-14 RF-14.10).</summary>
public static class SessionMigrations
{
    /// <summary>Registra os passos embutidos que ainda não existem em <paramref name="registry"/>.</summary>
    public static void RegisterBuiltIn(MigrationRegistry registry)
    {
        if (!registry.Has(OfflineSnapshot.Schema, 1)) registry.Register(OfflineSnapshot.Schema, 1, V1ToV2);
    }

    /// <summary>
    /// v1 → v2 (ADR-034, DP-33.7). Save com <c>slotIndex</c> (layout sem <c>width</c>): toda torre vira SEM POSIÇÃO
    /// (<c>anchor = Start</c>, <c>offsetDip = 0</c>, <c>placed = false</c>), <c>slotX</c> sai, o layout ganha <c>width</c> derivado
    /// (teto de <c>max(spawnX, portalX, coreX)</c>), <c>host = Taskbar</c>, <c>freeZones = []</c> e <c>migratedSlots = true</c>
    /// (o restore liga a pendência de <c>AutoPlaceUnpositioned</c>). O StateHash antigo não vale mais e vira
    /// <see cref="OfflineSnapshot.MigratedStateHash"/>. Save v1 já no formato ADR-034 (gravado pela TA.3) só perde os campos mortos.
    /// </summary>
    public static JsonNode V1ToV2(JsonNode node)
    {
        var root = node.AsObject();
        var layout = root["layout"]?.AsObject() ?? throw new InvalidOperationException("layout ausente");
        var legacy = layout["width"] is not JsonValue w || !w.TryGetValue<int>(out var width) || width <= 0;
        layout.Remove("slotX");
        if (legacy)
        {
            double Num(string k) => layout[k] is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : 0;
            layout["width"] = Math.Max(1, (int)Math.Ceiling(Math.Max(Num("spawnX"), Math.Max(Num("portalX"), Num("coreX")))));
            layout["host"] = "Taskbar";
            layout["freeZones"] = new JsonArray();
            layout["migratedSlots"] = true;
            root["stateHash"] = OfflineSnapshot.MigratedStateHash;
        }
        if (root["sim"]?["towers"] is JsonArray towers)
            foreach (var t in towers)
            {
                var o = t!.AsObject();
                o.Remove("slotIndex");
                if (!legacy) continue;
                o["anchor"] = "Start";
                o["offsetDip"] = 0;
                o["placed"] = false;
            }
        return root;
    }
}
