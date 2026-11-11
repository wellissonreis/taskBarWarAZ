using SideWar.Runtime.Lifecycle;
using Xunit;

namespace SideWar.Runtime.Tests.Lifecycle;

public sealed class ShutdownCrashTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "sidewar-t26-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    [Fact]
    public void Shutdown_runs_steps_in_mandatory_order_and_logs_ok()
    {
        var order = new List<string>();
        var c = new ShutdownCoordinator(new ShutdownSteps
        {
            MarkShuttingDown = () => order.Add("mark"),
            SaveSession = _ => { order.Add("save"); return true; },
            StopGameThread = () => { order.Add("game"); return Task.CompletedTask; },
            CloseUi = () => order.Add("ui"),
            Log = m => order.Add(m.StartsWith("shutdown ok in ", StringComparison.Ordinal) ? "log" : m),
            Exit = _ => order.Add("EXIT"),
        });
        var r = c.Run();
        Assert.Equal(["mark", "save", "game", "ui", "log"], order);
        Assert.True(r.SaveOk && r.GameThreadOk && !r.ForcedExit);
        Assert.Empty(r.Failures);
    }

    [Fact]
    public void Shutdown_save_timeout_still_stops_game_and_ui_without_exit()
    {
        var order = new List<string>();
        var c = new ShutdownCoordinator(new ShutdownSteps
        {
            SaveSession = ct => { ct.WaitHandle.WaitOne(2000); return false; },
            StopGameThread = () => { order.Add("game"); return Task.CompletedTask; },
            CloseUi = () => order.Add("ui"),
            Exit = _ => order.Add("EXIT"),
        }, new ShutdownOptions(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5)));
        var r = c.Run();
        Assert.True(r.SaveTimedOut);
        Assert.False(r.SaveOk);
        Assert.Equal(["game", "ui"], order);
    }

    [Fact]
    public void Shutdown_step_failure_does_not_block_later_steps()
    {
        var ui = false;
        var c = new ShutdownCoordinator(new ShutdownSteps
        {
            SaveSession = _ => throw new IOException("disk"),
            StopGameThread = () => throw new InvalidOperationException("boom"),
            CloseUi = () => ui = true,
        });
        var r = c.Run();
        Assert.True(ui);
        Assert.Equal(2, r.Failures.Count);
    }

    [Fact]
    public void Shutdown_total_timeout_forces_exit_as_last_resort()
    {
        var exit = new ManualResetEventSlim();
        int? code = null;
        var logs = new List<string>();
        var c = new ShutdownCoordinator(new ShutdownSteps
        {
            StopGameThread = () => Task.Delay(1500),
            Log = m => { lock (logs) logs.Add(m); },
            Exit = x => { code = x; exit.Set(); },
        }, new ShutdownOptions(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(150)));
        var r = c.Run();
        Assert.True(exit.Wait(2000, TestContext.Current.CancellationToken));
        Assert.Equal(0, code);
        Assert.True(r.ForcedExit);
        Assert.False(r.GameThreadOk);
        lock (logs) Assert.Contains(logs, m => m.Contains("forcing exit", StringComparison.Ordinal));
    }

    [Fact]
    public void Shutdown_runs_only_once()
    {
        var n = 0;
        var c = new ShutdownCoordinator(new ShutdownSteps { CloseUi = () => n++ });
        c.Run();
        c.Run();
        Assert.Equal(1, n);
        Assert.True(c.IsShuttingDown);
    }

    private CrashReporter Reporter(List<string> order, Func<byte[]?>? capture, out int?[] exit, Func<string, byte[], bool>? write = null)
    {
        var code = new int?[1];
        exit = code;
        return new CrashReporter(new CrashReporterOptions
        {
            LogsDirectory = Path.Combine(dir, "logs"),
            DataDirectory = dir,
            Now = () => new DateTime(2026, 11, 10, 13, 5, 9),
            CaptureSession = capture,
            WriteAtomic = write ?? ((p, b) => { Directory.CreateDirectory(dir); File.WriteAllBytes(p, b); return true; }),
            RemoveTrayIcon = () => order.Add("tray"),
            Exit = c => code[0] = c,
        });
    }

    [Fact]
    public void Crash_writes_txt_and_session_crash_never_session_json_then_tray_and_exit1()
    {
        Directory.CreateDirectory(dir);
        var session = Path.Combine(dir, "session.json");
        File.WriteAllText(session, "original");
        var order = new List<string>();
        var r = Reporter(order, () => "{\"a\":1}"u8.ToArray(), out var exit);
        Exception ex;
        try { throw new InvalidOperationException("kaboom"); } catch (Exception e) { ex = e; }

        Assert.True(r.HandleFatal("test", ex));

        var crash = Path.Combine(dir, "logs", "crash-20261110-130509.txt");
        Assert.Equal(crash, r.LastCrashFile);
        var text = File.ReadAllText(crash);
        Assert.Contains("kaboom", text);
        Assert.Contains("source: test", text);
        Assert.Equal("{\"a\":1}", File.ReadAllText(Path.Combine(dir, "session.crash.json")));
        Assert.Equal("original", File.ReadAllText(session));
        Assert.Equal(["tray"], order);
        Assert.Equal(1, exit[0]);
    }

    [Fact]
    public void Crash_steps_are_isolated_and_reentrancy_is_ignored()
    {
        var order = new List<string>();
        var r = Reporter(order, () => throw new InvalidOperationException("capture"), out var exit);
        Assert.True(r.HandleFatal("a", new InvalidOperationException("x")));
        Assert.False(r.HandleFatal("b", new InvalidOperationException("y")));
        Assert.Single(Directory.GetFiles(Path.Combine(dir, "logs"), "crash-*.txt"));
        Assert.False(File.Exists(Path.Combine(dir, "session.crash.json")));
        Assert.Equal(["tray"], order);
        Assert.Equal(1, exit[0]);
    }

    [Fact]
    public void Crash_same_second_gets_suffix_and_null_session_skips_emergency_save()
    {
        var order = new List<string>();
        var r1 = Reporter(order, null, out _);
        var r2 = Reporter(order, null, out _);
        r1.HandleFatal("a", new InvalidOperationException("x"), exit: false);
        r2.HandleFatal("b", new InvalidOperationException("y"), exit: false);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(dir, "logs"), "crash-*.txt").Length);
        Assert.False(File.Exists(Path.Combine(dir, "session.crash.json")));
    }

    [Fact]
    public void Pending_recovery_only_when_crash_session_is_newer()
    {
        Directory.CreateDirectory(dir);
        Assert.False(CrashReporter.HasPendingRecovery(dir));
        var crash = Path.Combine(dir, "session.crash.json");
        var session = Path.Combine(dir, "session.json");
        File.WriteAllText(crash, "{}");
        Assert.True(CrashReporter.HasPendingRecovery(dir));
        File.WriteAllText(session, "{}");
        File.SetLastWriteTimeUtc(session, DateTime.UtcNow.AddMinutes(1));
        Assert.False(CrashReporter.HasPendingRecovery(dir));
        File.SetLastWriteTimeUtc(session, DateTime.UtcNow.AddMinutes(-5));
        Assert.True(CrashReporter.HasPendingRecovery(dir));
    }
}
