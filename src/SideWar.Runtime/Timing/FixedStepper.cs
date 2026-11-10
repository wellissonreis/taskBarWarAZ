namespace SideWar.Runtime.Timing;

/// <summary>Counts fixed simulation steps; the caller executes the returned ticks.</summary>
public sealed class FixedStepper
{
    private double lastNow;
    private bool wasRunning;

    public double AccumulatedSeconds { get; private set; }

    /// <summary>Establishes a time origin, including at pause/resume events. Times are seconds.</summary>
    public void Reset(double now, bool simRunning)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(now), true, nameof(now));
        lastNow = now;
        AccumulatedSeconds = 0;
        wasRunning = simRunning;
    }

    /// <summary>The first wake and the first wake after a pause establish a new origin.</summary>
    public int Advance(double now, bool simRunning)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(now), true, nameof(now));
        if (!simRunning || !wasRunning)
        {
            Reset(now, simRunning);
            return 0;
        }

        double elapsed = Math.Max(0, now - lastNow);
        lastNow = now;
        AccumulatedSeconds += Math.Min(elapsed, LoopConstants.MaxAccumulatedSeconds);
        // A tiny arithmetic tolerance prevents losing a boundary tick to subtraction rounding.
        int ticks = Math.Min(LoopConstants.MaxTicksPerWake,
            (int)Math.Floor((AccumulatedSeconds + 1e-12) / LoopConstants.TickSeconds));
        AccumulatedSeconds = elapsed > LoopConstants.MaxAccumulatedSeconds || ticks == LoopConstants.MaxTicksPerWake
            ? 0
            : Math.Max(0, AccumulatedSeconds - ticks * LoopConstants.TickSeconds);
        return ticks;
    }
}
