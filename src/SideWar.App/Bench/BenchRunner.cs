using System.IO;
using System.Diagnostics;
using SideWar.Platform.Windows.Diagnostics;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Tray;
using SideWar.Runtime.Diagnostics;

namespace SideWar.App.Bench;

/// <summary>
/// `--bench` driver (SDD-16 RF-16.2): starts T-Game with <see cref="BenchHost"/>, samples one CSV row per second,
/// then performs the normal shutdown. Uses a temporary data folder deleted at the end; never touches the user's session.
/// Exit codes: 0 ok, 3 errors reported by T-Game (the CSV is still written).
/// </summary>
internal static class BenchRunner
{
    public static async Task<int> RunAsync(BenchOptions options, TextWriter output, bool withTray)
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "SideWar-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var errors = new List<Exception>();
        TrayIcon? tray = null;
        GameThread? game = null;
        try
        {
            var stats = new BenchStats();
            var host = new BenchHost(options.Scenario, stats, options.NoInput, m => output.WriteLine(m));
            game = new GameThread(host, e => { lock (errors) errors.Add(e); output.WriteLine(e.ToString()); }, m => output.WriteLine(m));
            host.Bind(game);
            await game.StartAsync();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            game.Post(() => { try { host.Setup(); ready.SetResult(); } catch (Exception e) { ready.SetException(e); } });
            await ready.Task;
            if (withTray && !options.NoInput && options.Scenario == "e1-synthetic")
            {
                tray = new TrayIcon("SideWar bench");
                var current = game;
                tray.ToggleVisibilityRequested += () => { try { current.Post(host.ToggleVisibility); } catch (InvalidOperationException) { } };
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(options.OutPath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await using var csv = new StreamWriter(options.OutPath, false, new System.Text.UTF8Encoding(false)) { NewLine = "\n" };
            await csv.WriteLineAsync(BenchCsv.Header);

            long lastWakes = game.WakeCount, lastAlloc = GC.GetTotalAllocatedBytes(false);
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            var watch = Stopwatch.StartNew();
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            long lastTicks = 0;
            for (int i = 0; i < options.Seconds && await timer.WaitForNextTickAsync(); i++)
            {
                var elapsed = watch.ElapsedTicks;
                double dt = Math.Max(1e-6, (elapsed - lastTicks) / (double)Stopwatch.Frequency);
                lastTicks = elapsed;
                var w = stats.Drain();
                var p = ProcessMetrics.Read();
                long wakes = game.WakeCount, alloc = GC.GetTotalAllocatedBytes(false);
                int c0 = GC.CollectionCount(0), c1 = GC.CollectionCount(1), c2 = GC.CollectionCount(2);
                var row = new BenchRow(watch.Elapsed.TotalSeconds, w.Frames / dt, w.ComposeP50, w.ComposeP95, w.PresentP50, w.PresentP95,
                    w.TickMean, w.TickP99, wakes - lastWakes, alloc - lastAlloc, c0 - g0, c1 - g1, c2 - g2,
                    p.PrivateMB, p.WorkingSetMB, GC.GetTotalMemory(false) / (1024.0 * 1024.0), p.Handles, p.GdiObjects, p.UserObjects,
                    host.TaskbarQueries, host.TaskbarUpdates, w.TaskbarQueryMsP95, null);
                await csv.WriteLineAsync(BenchCsv.Format(row));
                (lastWakes, lastAlloc, g0, g1, g2) = (wakes, alloc, c0, c1, c2);
            }
            await csv.FlushAsync();
        }
        catch (Exception e)
        {
            lock (errors) errors.Add(e);
            output.WriteLine(e.ToString());
        }
        finally
        {
            try { tray?.Dispose(); } catch (InvalidOperationException e) { output.WriteLine(e.Message); }
            if (game is not null)
                try { await game.StopAsync(); }
                catch (Exception e) { lock (errors) errors.Add(e); output.WriteLine(e.ToString()); }
            try { Directory.Delete(dataDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        lock (errors) return errors.Count == 0 ? 0 : 3;
    }
}
