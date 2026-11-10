namespace SideWar.Runtime.Input;

/// <summary>
/// Fade under the cursor in Passive (SDD-04 RF-04.5, DP-22.3: off by default for the strip). Polled at 8 Hz
/// only while enabled, Passive and visible; fades after 250 ms of continuous presence and restores after 150 ms
/// of absence. Only the opacity changes; nothing is redrawn (RNF-04.2).
/// </summary>
public sealed class PeekController
{
    public const double PollHz = 8;
    public const double EnterSeconds = 0.25;
    public const double ExitSeconds = 0.15;
    public const byte PeekAlpha = 60;

    private readonly long pollTicks;
    private readonly long enterTicks;
    private readonly long exitTicks;
    private bool active;
    private long? insideSince;
    private long? outsideSince;
    private long? nextPoll;

    public PeekController(double ticksPerSecond)
    {
        if (!double.IsFinite(ticksPerSecond) || ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        pollTicks = (long)Math.Round(ticksPerSecond / PollHz);
        enterTicks = (long)Math.Round(ticksPerSecond * EnterSeconds);
        exitTicks = (long)Math.Round(ticksPerSecond * ExitSeconds);
    }

    public bool Enabled { get; private set; }
    public bool Peeking { get; private set; }

    /// <summary>Next cursor poll; null when polling is off.</summary>
    public long? NextPoll => nextPoll;

    /// <summary>Enabled by settings AND Passive AND visible (also the absent power profile may turn it off).</summary>
    public bool Configure(bool enabled, bool passive, bool visible, long now)
    {
        Enabled = enabled;
        var run = enabled && passive && visible;
        if (run == active) return false;
        active = run;
        insideSince = outsideSince = null;
        nextPoll = run ? now : null;
        return Stop();
    }

    /// <summary>True when it is time to poll the cursor.</summary>
    public bool PollDue(long now) => nextPoll is { } due && now >= due;

    /// <summary>Feeds one cursor sample; returns true when <see cref="Peeking"/> changed.</summary>
    public bool OnSample(long now, bool cursorInside)
    {
        if (!active) return false;
        nextPoll = now + pollTicks;
        if (cursorInside)
        {
            outsideSince = null;
            insideSince ??= now;
            if (!Peeking && now - insideSince.Value >= enterTicks) { Peeking = true; return true; }
        }
        else
        {
            insideSince = null;
            outsideSince ??= now;
            if (Peeking && now - outsideSince.Value >= exitTicks) { Peeking = false; return true; }
        }
        return false;
    }

    private bool Stop()
    {
        if (!Peeking) return false;
        Peeking = false;
        return true;
    }
}
