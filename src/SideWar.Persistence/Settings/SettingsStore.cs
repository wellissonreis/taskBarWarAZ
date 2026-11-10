using SideWar.Persistence.IO;

namespace SideWar.Persistence.Settings;

public enum SettingsSource { File, Backup, Defaults }

public readonly record struct SettingsLoadResult(AppSettings Settings, SettingsSource Source);

/// <summary>settings.json versionado com .bak, corrupt\ e padrões como último recurso (SDD-14 RF-14.2..4, 14.9).</summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    private readonly IFileSystem _fs;
    private readonly AtomicFileWriter _writer;
    private readonly CorruptQuarantine _quarantine;
    private readonly string _path;
    private readonly string _appVersion;
    private readonly Func<DateTime> _utcNow;
    private readonly Action<string>? _log;

    public SettingsStore(IFileSystem fs, string dataDirectory, string appVersion = "0.1.0",
        Func<DateTime>? utcNow = null, Action<string>? log = null, AtomicFileWriter? writer = null)
    {
        _fs = fs;
        _path = Path.Combine(dataDirectory, FileName);
        _appVersion = appVersion;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _log = log;
        _writer = writer ?? new AtomicFileWriter(fs, log);
        _quarantine = new CorruptQuarantine(fs, Path.Combine(dataDirectory, "corrupt"), _utcNow, log);
    }

    public string FilePath => _path;

    /// <summary>Principal → (corrompido vai para corrupt\) → .bak → padrões. Nunca lança por conteúdo ou I/O.</summary>
    public SettingsLoadResult Load()
    {
        if (TryLoad(_path, out var s)) return new(s, SettingsSource.File);
        if (TryLoad(_path + ".bak", out s)) return new(s, SettingsSource.Backup);
        return new(AppSettings.Default, SettingsSource.Defaults);
    }

    private bool TryLoad(string path, out AppSettings settings)
    {
        settings = AppSettings.Default;
        try
        {
            if (!_fs.FileExists(path)) return false;
            var length = _fs.GetLength(path);
            EnvelopeReadResult r = length > JsonEnvelope.MaxReadBytes
                ? new(EnvelopeStatus.Corrupt, "tamanho acima de 1 MB", null)
                : JsonEnvelope.Read(_fs.ReadAllBytes(path), AppSettings.Schema, AppSettings.SchemaVersion);
            if (r.Status != EnvelopeStatus.Ok)
            {
                _quarantine.Quarantine(path, r.Reason);
                return false;
            }
            using (r.Payload)
            {
                settings = SettingsSerializer.Read(r.Payload!.RootElement.GetProperty("payload"));
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"falha ao ler '{System.IO.Path.GetFileName(path)}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Grava atomicamente. false = falha de I/O (já registrada); o jogo continua.</summary>
    public bool Save(AppSettings settings)
    {
        var bytes = JsonEnvelope.Write(AppSettings.Schema, AppSettings.SchemaVersion, _appVersion, _utcNow(),
            w => SettingsSerializer.Write(w, settings));
        return _writer.Write(_path, bytes);
    }
}
