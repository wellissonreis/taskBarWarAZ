namespace SideWar.Runtime.Diagnostics;

/// <summary>One-second window of samples (milliseconds). Percentiles are nearest-rank.</summary>
public readonly record struct BenchWindow(
    long Frames, double ComposeP50, double ComposeP95, double PresentP50, double PresentP95,
    double TickMean, double TickP99, long Ticks, double TaskbarQueryMsP95);

/// <summary>
/// Thread-safe sample collector: T-Game records, the sampler drains once per second. Lists are reused, so
/// steady state allocates nothing beyond list growth in the first window.
/// </summary>
public sealed class BenchStats
{
    private readonly object gate = new();
    private readonly List<double> compose = new(64), present = new(64), tick = new(256), query = new(8);

    public void RecordFrame(double composeMs, double presentMs)
    {
        lock (gate) { compose.Add(composeMs); present.Add(presentMs); }
        SideWarMetrics.RecordFrame(composeMs, presentMs);
    }

    public void RecordTick(double ms)
    {
        lock (gate) tick.Add(ms);
        SideWarMetrics.RecordTick(ms * 1000.0);
    }

    public void RecordTaskbarQuery(double ms)
    {
        lock (gate) query.Add(ms);
    }

    /// <summary>Returns the window since the previous drain and clears it.</summary>
    public BenchWindow Drain()
    {
        lock (gate)
        {
            double tickSum = 0;
            foreach (var t in tick) tickSum += t;
            var result = new BenchWindow(compose.Count, Percentile(compose, 0.50), Percentile(compose, 0.95),
                Percentile(present, 0.50), Percentile(present, 0.95),
                tick.Count == 0 ? 0 : tickSum / tick.Count, Percentile(tick, 0.99), tick.Count, Percentile(query, 0.95));
            compose.Clear(); present.Clear(); tick.Clear(); query.Clear();
            return result;
        }
    }

    /// <summary>Nearest-rank percentile (sorts the list in place); 0 for an empty list.</summary>
    public static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var rank = (int)Math.Ceiling(p * values.Count);
        return values[Math.Clamp(rank - 1, 0, values.Count - 1)];
    }
}
