namespace SideWar.Persistence.IO;

/// <summary>Escrita atômica com .tmp, File.Replace + .bak e fallback (SDD-14 RF-14.2).</summary>
public sealed class AtomicFileWriter
{
    public const int MaxRetries = 2;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    /// <summary>Worst-case blocked time spent in retry sleeps (100 ms; RNF-14.4 allows 250 ms on the caller). Sleeps only after an IOException, never in a loop without a limit.</summary>
    public static readonly TimeSpan MaxTotalDelay = RetryDelay * MaxRetries;

    private readonly IFileSystem _fs;
    private readonly Action<string>? _logError;
    private readonly Action<TimeSpan> _sleep;

    public AtomicFileWriter(IFileSystem fs, Action<string>? logError = null, Action<TimeSpan>? sleep = null)
    {
        _fs = fs;
        _logError = logError;
        _sleep = sleep ?? Thread.Sleep;
    }

    /// <summary>Retorna false (sem lançar) se não conseguiu gravar; o jogo não para por falha de save.</summary>
    public bool Write(string path, ReadOnlySpan<byte> utf8)
    {
        var tmp = path + ".tmp";
        var bak = path + ".bak";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) _fs.CreateDirectory(dir);
                _fs.WriteAllBytesDurable(tmp, utf8);
                Commit(path, tmp, bak);
                return true;
            }
            catch (IOException ex) when (attempt < MaxRetries)
            {
                _ = ex;
                _sleep(RetryDelay);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logError?.Invoke($"falha ao salvar '{Path.GetFileName(path)}': {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }
    }

    private void Commit(string path, string tmp, string bak)
    {
        if (!_fs.FileExists(path))
        {
            _fs.Move(tmp, path, overwrite: false);
            return;
        }
        try
        {
            _fs.Replace(tmp, path, bak);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            _fs.Copy(path, bak, overwrite: true);
            _fs.Move(tmp, path, overwrite: true);
        }
    }
}
