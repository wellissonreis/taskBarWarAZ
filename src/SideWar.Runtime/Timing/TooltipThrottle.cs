namespace SideWar.Runtime.Timing;

/// <summary>
/// Tray tooltip limiter (SDD-16 RF-16.8): at most one update per interval and only when the text changed. A text
/// held back by the interval is retained and exposed as <see cref="NextDeadline"/>, so the host arms a single
/// one-shot wait for it and calls <see cref="Flush"/> when due (no periodic timer). Clock is injected by the caller.
/// </summary>
public sealed class TooltipThrottle
{
    private readonly long intervalTicks;
    private string? pending;
    private long lastApplied;

    public TooltipThrottle(string initial, long intervalTicks, long now)
    {
        Current = initial ?? throw new ArgumentNullException(nameof(initial));
        ArgumentOutOfRangeException.ThrowIfNegative(intervalTicks);
        this.intervalTicks = intervalTicks;
        lastApplied = now;
    }

    public string Current { get; private set; }

    /// <summary>When the held-back text may be applied; null when nothing is pending.</summary>
    public long? NextDeadline => pending is null ? null : lastApplied + intervalTicks;

    /// <summary>True when <see cref="Current"/> changed now.</summary>
    public bool Offer(string text, long now)
    {
        if (text == Current) { pending = null; return false; }
        if (now - lastApplied < intervalTicks) { pending = text; return false; }
        Current = text;
        pending = null;
        lastApplied = now;
        return true;
    }

    /// <summary>Applies the held-back text if its deadline arrived.</summary>
    public bool Flush(long now) => pending is { } text && Offer(text, now);
}
