using System.Diagnostics;
using SideWar.Runtime.Diagnostics;
using SideWar.Runtime.Taskbar;

namespace SideWar.App.Bench;

/// <summary>Measures each UIA query (call → completion) for `taskbarQueryMsP95` without changing the probe (RF-16.5).</summary>
internal sealed class TimingProbe(ITaskbarProbe inner, BenchStats stats) : ITaskbarProbe
{
    public Task<UiaNode[]> QueryAsync(nint taskbarHwnd)
    {
        long start = Stopwatch.GetTimestamp();
        var task = inner.QueryAsync(taskbarHwnd);
        task.ContinueWith(static (_, s) =>
        {
            var (stats, start) = ((BenchStats, long))s!;
            stats.RecordTaskbarQuery((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        }, (stats, start), TaskScheduler.Default);
        return task;
    }
}
