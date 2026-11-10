using System.Text.Json.Nodes;

namespace SideWar.Persistence.Migrations;

/// <summary>
/// Lista ordenada de <c>(schema, fromVersion) → Func&lt;JsonNode, JsonNode&gt;</c> (SDD-14 RF-14.10). Cada passo leva o payload
/// de <c>fromVersion</c> a <c>fromVersion + 1</c>. Só roda na carga; quem chama grava de volta na versão nova.
/// </summary>
public sealed class MigrationRegistry
{
    private readonly Dictionary<(string Schema, int From), Func<JsonNode, JsonNode>> _steps = [];

    public static MigrationRegistry Empty { get; } = new();

    public MigrationRegistry Register(string schema, int fromVersion, Func<JsonNode, JsonNode> step)
    {
        ArgumentException.ThrowIfNullOrEmpty(schema);
        ArgumentNullException.ThrowIfNull(step);
        if (!_steps.TryAdd((schema, fromVersion), step))
            throw new InvalidOperationException($"migração já registrada para {schema} v{fromVersion}");
        return this;
    }

    /// <summary>Menor versão da qual há cadeia contígua até <paramref name="currentVersion"/> (a própria atual se não há migrações).</summary>
    public int MinVersion(string schema, int currentVersion)
    {
        var v = currentVersion;
        while (_steps.ContainsKey((schema, v - 1))) v--;
        return v;
    }

    /// <summary>Aplica os passos em sequência. false (sem lançar) se faltar um passo ou um passo falhar.</summary>
    public bool TryMigrate(string schema, int fromVersion, int toVersion, JsonNode payload, out JsonNode migrated, out string reason)
    {
        migrated = payload;
        reason = "";
        for (var v = fromVersion; v < toVersion; v++)
        {
            if (!_steps.TryGetValue((schema, v), out var step))
            {
                reason = $"schemaVersion sem migração registrada (v{v})";
                return false;
            }
            try { migrated = step(migrated); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                reason = $"migração v{v} falhou: {ex.GetType().Name}";
                return false;
            }
        }
        return true;
    }
}
