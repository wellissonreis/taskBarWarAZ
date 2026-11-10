using System.Text.Json;
using System.Text.Json.Nodes;
using SideWar.Persistence.IO;
using SideWar.Persistence.Migrations;

namespace SideWar.Persistence;

public enum DocumentSource { None, File, Backup }

/// <summary>Veredito de um portão sobre o payload já validado pelo envelope, antes de desserializar.</summary>
internal readonly record struct PayloadGate(string? RejectReason, Func<JsonNode, JsonNode>? Transform)
{
    public static PayloadGate Accept => default;
    public static PayloadGate Reject(string reason) => new(reason, null);
    public static PayloadGate Migrate(Func<JsonNode, JsonNode> transform) => new(null, transform);
}

internal readonly record struct LoadedDocument<T>(T? Value, DocumentSource Source, bool Migrated, string? RejectReason) where T : class;

/// <summary>
/// Núcleo comum de profile.json e session.json: envelope (T2.4), migrações de schema, principal → .bak, quarentena em corrupt\
/// e escrita atômica (SDD-14 RF-14.2, 14.3, 14.9, 14.10). Nunca lança por conteúdo ou I/O.
/// </summary>
internal sealed class VersionedDocumentStore<T> where T : class
{
    private readonly IFileSystem _fs;
    private readonly AtomicFileWriter _writer;
    private readonly CorruptQuarantine _quarantine;
    private readonly MigrationRegistry _migrations;
    private readonly string _schema;
    private readonly int _version;
    private readonly string _appVersion;
    private readonly Func<DateTime> _utcNow;
    private readonly Action<string>? _log;
    private readonly Func<JsonElement, T> _read;
    private readonly Action<Utf8JsonWriter, T> _write;

    public VersionedDocumentStore(IFileSystem fs, string path, string schema, int version, MigrationRegistry migrations,
        Func<JsonElement, T> read, Action<Utf8JsonWriter, T> write, string appVersion, Func<DateTime> utcNow,
        Action<string>? log, AtomicFileWriter writer, CorruptQuarantine quarantine)
    {
        _fs = fs; Path = path; _schema = schema; _version = version; _migrations = migrations;
        _read = read; _write = write; _appVersion = appVersion; _utcNow = utcNow; _log = log;
        _writer = writer; _quarantine = quarantine;
    }

    public string Path { get; }

    public byte[] Encode(T value) => JsonEnvelope.Write(_schema, _version, _appVersion, _utcNow(), w => _write(w, value));

    public bool Write(ReadOnlySpan<byte> bytes) => _writer.Write(Path, bytes);

    public bool Save(T value) => Write(Encode(value));

    /// <summary>Principal → (inválido vai para corrupt\) → .bak. Se migrou, grava de volta na versão nova (RF-14.10).</summary>
    public LoadedDocument<T> Load(Func<JsonElement, PayloadGate>? gate = null)
    {
        string? lastReject = null;
        foreach (var (path, source) in new[] { (Path, DocumentSource.File), (Path + ".bak", DocumentSource.Backup) })
        {
            var r = TryLoad(path, gate);
            if (r.Value is not null)
            {
                if (r.Migrated) Save(r.Value);
                return new(r.Value, source, r.Migrated, null);
            }
            lastReject ??= r.RejectReason;
        }
        return new(null, DocumentSource.None, false, lastReject);
    }

    /// <summary>Move o principal e o .bak para corrupt\ (restore rejeitado depois de lido: hash do estado, conteúdo).</summary>
    public void Discard(string reason)
    {
        foreach (var p in new[] { Path, Path + ".bak" })
        {
            try { if (_fs.FileExists(p)) _quarantine.Quarantine(p, reason); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Invoke($"falha ao descartar: {ex.Message}"); }
        }
    }

    /// <summary>Apaga o principal e o .bak, sem quarentena.</summary>
    public void Delete()
    {
        foreach (var p in new[] { Path, Path + ".bak" })
        {
            try { if (_fs.FileExists(p)) _fs.Delete(p); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Invoke($"falha ao apagar: {ex.Message}"); }
        }
    }

    private LoadedDocument<T> TryLoad(string path, Func<JsonElement, PayloadGate>? gate)
    {
        try
        {
            if (!_fs.FileExists(path)) return new(null, DocumentSource.None, false, null);
            var length = _fs.GetLength(path);
            var minVersion = _migrations.MinVersion(_schema, _version);
            EnvelopeReadResult r = length > JsonEnvelope.MaxReadBytes
                ? new(EnvelopeStatus.Corrupt, "tamanho acima de 1 MB", null)
                : JsonEnvelope.Read(_fs.ReadAllBytes(path), _schema, _version, minVersion);
            if (r.Status != EnvelopeStatus.Ok) return Reject(path, r.Reason);

            using (r.Payload)
            {
                var root = r.Payload!.RootElement;
                var payload = root.GetProperty("payload");
                var fileVersion = root.GetProperty("schemaVersion").GetInt32();
                var migrated = false;
                JsonDocument? owned = null;
                try
                {
                    if (fileVersion < _version)
                    {
                        var node = JsonNode.Parse(payload.GetRawText());
                        if (node is null) return Reject(path, "payload ausente");
                        if (!_migrations.TryMigrate(_schema, fileVersion, _version, node, out var m, out var why))
                            return Reject(path, why);
                        owned = JsonDocument.Parse(m.ToJsonString());
                        payload = owned.RootElement;
                        migrated = true;
                    }
                    if (gate is not null)
                    {
                        var verdict = gate(payload);
                        if (verdict.RejectReason is not null) return Reject(path, verdict.RejectReason);
                        if (verdict.Transform is { } transform)
                        {
                            var node = JsonNode.Parse(payload.GetRawText())!;
                            var transformed = transform(node);
                            owned?.Dispose();
                            owned = JsonDocument.Parse(transformed.ToJsonString());
                            payload = owned.RootElement;
                            migrated = true;
                        }
                    }
                    var value = _read(payload);
                    return new(value, DocumentSource.File, migrated, null);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
                {
                    return Reject(path, $"payload inválido ({ex.GetType().Name})");
                }
                finally { owned?.Dispose(); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"falha ao ler '{System.IO.Path.GetFileName(path)}': {ex.Message}");
            return new(null, DocumentSource.None, false, null);
        }
    }

    private LoadedDocument<T> Reject(string path, string reason)
    {
        _quarantine.Quarantine(path, reason);
        return new(null, DocumentSource.None, false, reason);
    }
}
