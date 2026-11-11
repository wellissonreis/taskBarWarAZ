using System.Diagnostics;

namespace SideWar.Runtime.Lifecycle;

/// <summary>Real steps of the normal shutdown (SDD-07 RF-07.10). Each is optional and isolated from failures of the others.</summary>
public sealed class ShutdownSteps
{
    /// <summary>Step 1: mark ShuttingDown and reject new commands.</summary>
    public Action? MarkShuttingDown { get; init; }
    /// <summary>Step 2: pause, serialize and write synchronously. Receives the save budget; returns false on failure.</summary>
    public Func<CancellationToken, bool>? SaveSession { get; init; }
    /// <summary>Step 3: stop T-Game (unhook, unregister, destroy windows, release surface; RF-03.7).</summary>
    public Func<Task>? StopGameThread { get; init; }
    /// <summary>Step 4: close WPF windows, NIM_DELETE, release the mutex, Application.Shutdown(0).</summary>
    public Action? CloseUi { get; init; }
    /// <summary>Step 5: final log line with flush.</summary>
    public Action<string>? Log { get; init; }
    /// <summary>Last resort when the total budget is exceeded (Environment.Exit in production).</summary>
    public Action<int>? Exit { get; init; }
}

public sealed record ShutdownOptions(TimeSpan SaveTimeout, TimeSpan TotalTimeout)
{
    public static ShutdownOptions Default { get; } = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
}

public sealed record ShutdownResult(bool SaveOk, bool SaveTimedOut, bool GameThreadOk, bool ForcedExit, TimeSpan Elapsed, IReadOnlyList<string> Failures);

/// <summary>
/// Normal shutdown in the mandatory order of RF-07.10, with a 2 s save timeout and a 3 s total watchdog;
/// <c>Exit(0)</c> is only the last resort. Runs once; later calls return the first result.
/// </summary>
public sealed class ShutdownCoordinator
{
    private readonly ShutdownSteps steps;
    private readonly ShutdownOptions options;
    private readonly object gate = new();
    private ShutdownResult? result;
    private bool started;

    public ShutdownCoordinator(ShutdownSteps steps, ShutdownOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        this.steps = steps;
        this.options = options ?? ShutdownOptions.Default;
    }

    public bool IsShuttingDown { get { lock (gate) return started; } }

    public ShutdownResult Run()
    {
        lock (gate)
        {
            if (started) return result ?? new ShutdownResult(false, false, false, false, TimeSpan.Zero, ["already shutting down"]);
            started = true;
        }
        var watch = Stopwatch.StartNew();
        var failures = new List<string>();
        var done = 0;
        var forced = 0;
        void Force()
        {
            if (Interlocked.Exchange(ref forced, 1) != 0) return;
            Try("log", () => steps.Log?.Invoke($"shutdown timeout after {watch.ElapsedMilliseconds} ms; forcing exit"), failures);
            steps.Exit?.Invoke(0);
        }
        using var watchdog = new Timer(_ =>
        {
            if (Volatile.Read(ref done) == 0) Force();
        }, null, options.TotalTimeout, Timeout.InfiniteTimeSpan);

        Try("MarkShuttingDown", () => steps.MarkShuttingDown?.Invoke(), failures);

        var saveOk = false;
        var saveTimedOut = false;
        if (steps.SaveSession is { } save)
        {
            using var cts = new CancellationTokenSource();
            var task = Task.Run(() => save(cts.Token));
            try
            {
                if (task.Wait(options.SaveTimeout)) saveOk = task.Result;
                else { saveTimedOut = true; cts.Cancel(); failures.Add("SaveSession: timeout"); }
            }
            catch (Exception ex) { failures.Add("SaveSession: " + ex.GetBaseException().Message); }
        }

        var gameOk = steps.StopGameThread is null;
        if (steps.StopGameThread is { } stop)
        {
            try
            {
                var remaining = options.TotalTimeout - watch.Elapsed;
                gameOk = Task.Run(stop).Wait(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                if (!gameOk) { failures.Add("StopGameThread: timeout"); Force(); }
            }
            catch (Exception ex) { failures.Add("StopGameThread: " + ex.GetBaseException().Message); }
        }

        Try("CloseUi", () => steps.CloseUi?.Invoke(), failures);
        Volatile.Write(ref done, 1);
        var elapsed = watch.Elapsed;
        Try("log", () => steps.Log?.Invoke($"shutdown ok in {(long)elapsed.TotalMilliseconds} ms"), failures);

        var res = new ShutdownResult(saveOk, saveTimedOut, gameOk, Volatile.Read(ref forced) != 0, elapsed, failures);
        lock (gate) result = res;
        return res;
    }

    private static void Try(string name, Action action, List<string> failures)
    {
        try { action(); }
        catch (Exception ex) { failures.Add($"{name}: {ex.GetBaseException().Message}"); }
    }
}
