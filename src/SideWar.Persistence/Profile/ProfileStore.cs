using SideWar.Persistence.IO;
using SideWar.Persistence.Migrations;

namespace SideWar.Persistence.Profile;

public enum ProfileSource { File, Backup, Defaults }

public readonly record struct ProfileLoadResult(ProfileData Profile, ProfileSource Source);

/// <summary>profile.json versionado com migrações, .bak, corrupt\ e perfil novo como último recurso (SDD-14 RF-14.5, 14.9, 14.10).</summary>
public sealed class ProfileStore
{
    public const string FileName = "profile.json";

    private readonly VersionedDocumentStore<ProfileData> _doc;

    public ProfileStore(IFileSystem fs, string dataDirectory, string appVersion = "0.1.0", Func<DateTime>? utcNow = null,
        Action<string>? log = null, AtomicFileWriter? writer = null, MigrationRegistry? migrations = null)
    {
        var now = utcNow ?? (() => DateTime.UtcNow);
        _doc = new VersionedDocumentStore<ProfileData>(fs, Path.Combine(dataDirectory, FileName), ProfileData.Schema,
            ProfileData.SchemaVersion, migrations ?? MigrationRegistry.Empty, ProfileSerializer.Read, ProfileSerializer.Write,
            appVersion, now, log, writer ?? new AtomicFileWriter(fs, log), new CorruptQuarantine(fs, Path.Combine(dataDirectory, "corrupt"), now, log));
    }

    public string FilePath => _doc.Path;

    /// <summary>Principal → (inválido vai para corrupt\) → .bak → perfil novo. Nunca lança por conteúdo ou I/O.</summary>
    public ProfileLoadResult Load()
    {
        var r = _doc.Load();
        return r.Value is { } p
            ? new(p, r.Source == DocumentSource.Backup ? ProfileSource.Backup : ProfileSource.File)
            : new(ProfileData.Default, ProfileSource.Defaults);
    }

    /// <summary>Grava atomicamente. false = falha de I/O (já registrada); o jogo continua.</summary>
    public bool Save(ProfileData profile) => _doc.Save(ProfileSerializer.Sanitize(profile));

    /// <summary>Bytes do envelope, para o chamador entregar à T-IO sem serializar de novo.</summary>
    public byte[] Encode(ProfileData profile) => _doc.Encode(ProfileSerializer.Sanitize(profile));

    public bool WriteEncoded(ReadOnlySpan<byte> bytes) => _doc.Write(bytes);
}
