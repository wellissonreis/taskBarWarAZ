using System.Text.Json;
using System.Text.Json.Nodes;
using SideWar.Persistence.IO;
using SideWar.Persistence.Migrations;

namespace SideWar.Persistence.Session;

public enum SessionLoadStatus
{
    /// <summary>Sem sessão (arquivo ausente ou todos inválidos e já movidos para corrupt\): partida nova.</summary>
    None,
    /// <summary>Sessão lida e com o ContentHash atual (ou migrada para ele).</summary>
    Restored,
    /// <summary>Sessão de outro ContentHash sem migração: descartada para corrupt\; o perfil é mantido (RF-14.8 passo 5). Mostrar o aviso de 8 s.</summary>
    DiscardedContentMismatch,
}

public readonly record struct SessionLoadResult(OfflineSnapshot? Snapshot, SessionLoadStatus Status, DocumentSource Source);

/// <summary>session.json (OfflineSnapshot) versionado (SDD-14 RF-14.6, 14.8 passos 4–5, 14.9, 14.10; SDD-25 RF-25.11). Só persistência: gatilhos e restore são T6.9.</summary>
public sealed class SessionStore
{
    public const string FileName = "session.json";

    private readonly VersionedDocumentStore<OfflineSnapshot> _doc;
    private readonly Dictionary<(string From, string To), Func<JsonNode, JsonNode>> _contentMigrations = [];

    public SessionStore(IFileSystem fs, string dataDirectory, string appVersion = "0.1.0", Func<DateTime>? utcNow = null,
        Action<string>? log = null, AtomicFileWriter? writer = null, MigrationRegistry? migrations = null)
    {
        var now = utcNow ?? (() => DateTime.UtcNow);
        _doc = new VersionedDocumentStore<OfflineSnapshot>(fs, Path.Combine(dataDirectory, FileName), OfflineSnapshot.Schema,
            OfflineSnapshot.SchemaVersion, migrations ?? MigrationRegistry.Empty, SessionSerializer.Read, SessionSerializer.Write,
            appVersion, now, log, writer ?? new AtomicFileWriter(fs, log), new CorruptQuarantine(fs, Path.Combine(dataDirectory, "corrupt"), now, log));
    }

    public string FilePath => _doc.Path;

    /// <summary>Migração registrada para o par de ContentHash (RF-14.8 passo 5). O ContentHash do payload é ajustado automaticamente.</summary>
    public SessionStore RegisterContentMigration(string fromContentHash, string toContentHash, Func<JsonNode, JsonNode> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _contentMigrations[(fromContentHash, toContentHash)] = step;
        return this;
    }

    /// <summary>
    /// Principal → (inválido vai para corrupt\) → .bak. ContentHash diferente do atual: migração do par, ou a sessão é
    /// descartada (principal e .bak para corrupt\). O StateHash NÃO é conferido aqui (exige o SimState): use <see cref="RejectRestored"/>.
    /// </summary>
    public SessionLoadResult Load(string currentContentHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(currentContentHash);
        var mismatch = false;
        var r = _doc.Load(payload =>
        {
            var stored = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("gameContentHash", out var h)
                && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
            if (stored is null) return PayloadGate.Reject("gameContentHash ausente");
            if (stored == currentContentHash) return PayloadGate.Accept;
            if (_contentMigrations.TryGetValue((stored, currentContentHash), out var step))
                return PayloadGate.Migrate(node =>
                {
                    var migrated = step(node);
                    migrated["gameContentHash"] = currentContentHash;
                    return migrated;
                });
            mismatch = true;
            return PayloadGate.Reject("contentHash do jogo diverge");
        });
        if (r.Value is { } snap)
            return new(snap, SessionLoadStatus.Restored, r.Source);
        if (mismatch)
        {
            // O .bak do mesmo conteúdo antigo também não serve: não deixar uma sessão obsoleta para trás.
            _doc.Discard("contentHash do jogo diverge");
            return new(null, SessionLoadStatus.DiscardedContentMismatch, DocumentSource.None);
        }
        return new(null, SessionLoadStatus.None, DocumentSource.None);
    }

    /// <summary>Restore rejeitado depois de lido (StateHash divergente, RF-14.9): move principal e .bak para corrupt\.</summary>
    public void RejectRestored(string reason) => _doc.Discard(reason);

    /// <summary>Grava atomicamente. false = falha de I/O (já registrada); o jogo continua.</summary>
    public bool Save(OfflineSnapshot snapshot) => _doc.Save(snapshot);

    /// <summary>Bytes do envelope para a T-IO (modo assíncrono, RF-14.7); entregue-os a <see cref="WriteEncoded"/>.</summary>
    public byte[] Encode(OfflineSnapshot snapshot) => _doc.Encode(snapshot);

    public bool WriteEncoded(ReadOnlySpan<byte> bytes) => _doc.Write(bytes);

    /// <summary>Apaga session.json e .bak (sem quarentena).</summary>
    public void Delete() => _doc.Delete();
}
