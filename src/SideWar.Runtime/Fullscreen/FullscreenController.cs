namespace SideWar.Runtime.Fullscreen;

/// <summary>
/// Pure scheduling core of the FullscreenWatcher (SDD-06 RF-06.1 items 5-6, RF-06.4, RF-06.5): 100 ms coalescing,
/// safety polling (2 s visible / 1 s with a tracked window / off when hidden for another reason) and hysteresis.
/// All members run on T-Game; the host feeds <see cref="NextDeadline"/> and <see cref="OnWake"/>.
/// </summary>
public sealed class FullscreenController
{
    public const int CoalesceMs = 100;
    public const int VisiblePollMs = 2000;
    public const int TrackedPollMs = 1000;

    private readonly IClock clock;
    private readonly FullscreenTracker tracker;
    private readonly FullscreenPolicy policy;
    private long? evaluateAt;
    private long lastEvaluate;
    private bool hiddenForOtherReason;

    public FullscreenController(IClock clock, IWindowProbe probe, uint ownPid, HideOnFullscreen mode, string overlayMonitor)
    {
        this.clock = clock;
        tracker = new FullscreenTracker(probe, ownPid);
        policy = new FullscreenPolicy(clock, mode, overlayMonitor);
        lastEvaluate = clock.NowTicks;
    }

    /// <summary>Raised when the hidden-by-fullscreen state flips, after hysteresis.</summary>
    public event Action<FullscreenTransition>? Transition;

    public bool Hidden => policy.Hidden;
    public IReadOnlySet<string> FullscreenMonitors => tracker.FullscreenMonitors;
    public FullscreenTracker Tracker => tracker;

    /// <summary>The overlay is hidden for a non-fullscreen reason (SDD-07): polling stops.</summary>
    public bool HiddenForOtherReason
    {
        get => hiddenForOtherReason;
        set { hiddenForOtherReason = value; lastEvaluate = clock.NowTicks; }
    }

    public long? NextDeadline => Min(Min(evaluateAt, PollDeadline), policy.NextDeadline);

    private long? PollDeadline
    {
        get
        {
            if (hiddenForOtherReason) return null;
            return lastEvaluate + Ticks(tracker.HasTracked ? TrackedPollMs : VisiblePollMs);
        }
    }

    /// <summary>Any signal (hook, ABN_FULLSCREENAPP, settings change): evaluate in 100 ms; repeats in the window do not reschedule.</summary>
    public void Signal() => evaluateAt ??= clock.NowTicks + Ticks(CoalesceMs);

    public void Reconfigure(HideOnFullscreen mode, string overlayMonitor)
    {
        Emit(policy.Reconfigure(mode, overlayMonitor));
        Signal();
    }

    public void OnWake()
    {
        var now = clock.NowTicks;
        var poll = PollDeadline;
        if ((evaluateAt is { } at && now >= at) || (poll is { } p && now >= p))
        {
            evaluateAt = null;
            lastEvaluate = now;
            Emit(policy.Update(tracker.Evaluate()));
        }
        else Emit(policy.Tick());
    }

    private void Emit(FullscreenTransition? t) { if (t is { } v) Transition?.Invoke(v); }
    private long Ticks(int ms) => (long)Math.Ceiling(clock.TicksPerSecond * ms / 1000.0);
    private static long? Min(long? a, long? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
}
