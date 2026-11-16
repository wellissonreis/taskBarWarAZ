using SideWar.Persistence.IO;
using SideWar.Persistence.Migrations;

namespace SideWar.Persistence.Profile;

/// <summary>profile-td.json versionado com migrações, .bak, corrupt\ e perfil novo como último recurso (SDD-14 RF-14.9/14.10, RNF-28.6).</summary>
public sealed class TdProfileStore
{
    public const string FileName = "profile-td.json";

    private readonly VersionedDocumentStore<TdProfileData> _doc;

    public TdProfileStore(IFileSystem fs, string dataDirectory, string appVersion = "0.1.0", Func<DateTime>? utcNow = null,
        Action<string>? log = null, AtomicFileWriter? writer = null, MigrationRegistry? migrations = null)
    {
        var now = utcNow ?? (() => DateTime.UtcNow);
        _doc = new VersionedDocumentStore<TdProfileData>(fs, Path.Combine(dataDirectory, FileName), TdProfileData.Schema,
            TdProfileData.SchemaVersion, migrations ?? TdProfileMigrations.Default, TdProfileSerializer.Read, TdProfileSerializer.Write,
            appVersion, now, log, writer ?? new AtomicFileWriter(fs, log), new CorruptQuarantine(fs, Path.Combine(dataDirectory, "corrupt"), now, log));
    }

    public string FilePath => _doc.Path;

    /// <summary>
    /// Principal → (inválido vai para corrupt\) → .bak → perfil novo. Nunca lança por conteúdo ou I/O.
    /// <see cref="TdProfileSource.Defaults"/> com arquivo presente e rejeitado = falha de leitura (o chamador mostra o aviso, RNF-28.6).
    /// </summary>
    public TdProfileLoadResult Load()
    {
        var r = _doc.Load();
        return r.Value is { } p
            ? new(p, r.Source == DocumentSource.Backup ? TdProfileSource.Backup : TdProfileSource.File, r.RejectReason)
            : new(TdProfileData.Default, TdProfileSource.Defaults, r.RejectReason);
    }

    public bool Save(TdProfileData profile) => _doc.Save(TdProfileSerializer.Sanitize(profile));

    public byte[] Encode(TdProfileData profile) => _doc.Encode(TdProfileSerializer.Sanitize(profile));

    public bool WriteEncoded(ReadOnlySpan<byte> bytes) => _doc.Write(bytes);
}

public enum TdProfileSource { File, Backup, Defaults }

/// <param name="RejectReason">Motivo do último arquivo rejeitado (quarentena), se houve; base do aviso de perfil recomeçado.</param>
public readonly record struct TdProfileLoadResult(TdProfileData Profile, TdProfileSource Source, string? RejectReason);
