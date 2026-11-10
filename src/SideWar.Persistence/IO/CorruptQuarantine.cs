using System.Globalization;
using System.Text.RegularExpressions;

namespace SideWar.Persistence.IO;

/// <summary>Move arquivos inválidos para corrupt\&lt;nome&gt;-&lt;yyyyMMddHHmmss&gt;.json e mantém no máximo 10 (SDD-14 RF-14.9).</summary>
public sealed partial class CorruptQuarantine
{
    public const int MaxFiles = 10;

    private readonly IFileSystem _fs;
    private readonly string _dir;
    private readonly Func<DateTime> _utcNow;
    private readonly Action<string>? _log;

    public CorruptQuarantine(IFileSystem fs, string corruptDirectory, Func<DateTime>? utcNow = null, Action<string>? log = null)
    {
        _fs = fs;
        _dir = corruptDirectory;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _log = log;
    }

    [GeneratedRegex(@"-(\d{14})(?:-\d+)?\.json$")]
    private static partial Regex NameRegex();

    /// <summary>Nunca lança: se não for possível mover, tenta apagar; falha final só é registrada.</summary>
    public void Quarantine(string path, string reason)
    {
        try
        {
            _fs.CreateDirectory(_dir);
            var stamp = _utcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var name = Path.GetFileName(path);
            var baseName = name.EndsWith(".bak", StringComparison.Ordinal) ? name[..^4] + ".bak" : Path.GetFileNameWithoutExtension(name);
            var target = Path.Combine(_dir, $"{baseName}-{stamp}.json");
            for (var i = 1; _fs.FileExists(target); i++) target = Path.Combine(_dir, $"{baseName}-{stamp}-{i}.json");
            _fs.Move(path, target, overwrite: false);
            _log?.Invoke($"'{Path.GetFileName(path)}' movido para corrupt\\ ({reason})");
            Prune();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"falha ao quarentenar '{Path.GetFileName(path)}': {ex.Message}");
            try { _fs.Delete(path); }
            catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException) { _log?.Invoke($"falha ao apagar: {ex2.Message}"); }
        }
    }

    private void Prune()
    {
        var files = _fs.EnumerateFiles(_dir)
            .Select(p => (Path: p, M: NameRegex().Match(Path.GetFileName(p))))
            .Where(x => x.M.Success)
            .OrderBy(x => x.M.Groups[1].Value, StringComparer.Ordinal)
            .ThenBy(x => Path.GetFileName(x.Path), StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < files.Count - MaxFiles; i++)
        {
            try { _fs.Delete(files[i].Path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log?.Invoke($"falha ao podar corrupt\\: {ex.Message}"); }
        }
    }
}
