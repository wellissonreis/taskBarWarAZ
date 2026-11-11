using System.Globalization;
using System.Text;

namespace SideWar.Runtime.Lifecycle;

/// <summary>Collaborators of <see cref="CrashReporter"/>; everything platform-specific is injected.</summary>
public sealed class CrashReporterOptions
{
    public required string LogsDirectory { get; init; }
    public required string DataDirectory { get; init; }
    public Func<DateTime>? Now { get; init; }
    /// <summary>Serialized session for the emergency save, or null when none is available (best effort, may throw).</summary>
    public Func<byte[]?>? CaptureSession { get; init; }
    /// <summary>Atomic write (AtomicFileWriter in production); returns false on failure.</summary>
    public Func<string, byte[], bool>? WriteAtomic { get; init; }
    public Action? RemoveTrayIcon { get; init; }
    public Action<int>? Exit { get; init; }
    public Action<string>? Log { get; init; }
}

/// <summary>
/// Fatal-failure path of SDD-07 RF-07.12: <c>logs/crash-yyyyMMdd-HHmmss.txt</c>, emergency <c>session.crash.json</c>
/// (never <c>session.json</c>), tray removal and exit code 1. Every step is isolated; runs at most once.
/// </summary>
public sealed class CrashReporter
{
    public const string SessionFile = "session.json";
    public const string CrashSessionFile = "session.crash.json";
    public const int ExitCode = 1;

    private readonly CrashReporterOptions o;
    private int handling;

    public CrashReporter(CrashReporterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        o = options;
    }

    public string? LastCrashFile { get; private set; }

    /// <summary>Non-fatal sink (UnobservedTaskException): log only.</summary>
    public void LogOnly(string source, Exception ex) => Safe(() => o.Log?.Invoke($"{source}: {ex}"));

    /// <summary>Handles a fatal exception. Returns false if another fatal handling is already in progress.</summary>
    public bool HandleFatal(string source, Exception? ex, bool exit = true)
    {
        if (Interlocked.Exchange(ref handling, 1) != 0) return false;
        Safe(() => LastCrashFile = WriteCrashFile(source, ex));
        Safe(SaveEmergency);
        Safe(() => o.RemoveTrayIcon?.Invoke());
        if (exit) Safe(() => o.Exit?.Invoke(ExitCode));
        return true;
    }

    /// <summary>True when <c>session.crash.json</c> exists and is newer than <c>session.json</c> (RF-07.12; the HUD offers recovery).</summary>
    public static bool HasPendingRecovery(string dataDirectory)
    {
        var crash = Path.Combine(dataDirectory, CrashSessionFile);
        if (!File.Exists(crash)) return false;
        var session = Path.Combine(dataDirectory, SessionFile);
        return !File.Exists(session) || File.GetLastWriteTimeUtc(crash) > File.GetLastWriteTimeUtc(session);
    }

    private string WriteCrashFile(string source, Exception? ex)
    {
        var now = (o.Now ?? (() => DateTime.Now))();
        Directory.CreateDirectory(o.LogsDirectory);
        var stem = "crash-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(o.LogsDirectory, stem + ".txt");
        for (var i = 1; File.Exists(path); i++) path = Path.Combine(o.LogsDirectory, $"{stem}-{i}.txt");
        var sb = new StringBuilder();
        sb.Append("SideWar crash\n");
        sb.Append("time: ").Append(now.ToString("o", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("source: ").Append(source).Append('\n');
        sb.Append("os: ").Append(Environment.OSVersion).Append('\n');
        sb.Append("runtime: ").Append(Environment.Version).Append('\n');
        sb.Append("thread: ").Append(Environment.CurrentManagedThreadId).Append('\n');
        sb.Append('\n').Append(ex?.ToString() ?? "(no exception object)").Append('\n');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        return path;
    }

    private void SaveEmergency()
    {
        var bytes = o.CaptureSession?.Invoke();
        if (bytes is null || o.WriteAtomic is null) return;
        if (!o.WriteAtomic(Path.Combine(o.DataDirectory, CrashSessionFile), bytes)) o.Log?.Invoke("emergency save failed");
    }

    private static void Safe(Action a)
    {
        try { a(); } catch { /* the crash path must never throw */ }
    }
}
