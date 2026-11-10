namespace SideWar.Runtime.Fullscreen;

/// <summary>Hide policy with hysteresis (SDD-06 RF-06.5): enter after 250 ms stable, exit after 600 ms stable.</summary>
public sealed class FullscreenPolicy
{
    public const int EnterDelayMs = 250;
    public const int ExitDelayMs = 600;

    private readonly IClock clock;
    private HideOnFullscreen mode;
    private string overlayMonitor;
    private IReadOnlySet<string> monitors = new HashSet<string>();
    private long? pendingSince;

    public FullscreenPolicy(IClock clock, HideOnFullscreen mode, string overlayMonitor)
    {
        this.clock = clock;
        this.mode = mode;
        this.overlayMonitor = overlayMonitor;
    }

    /// <summary>True while the overlay is hidden because of fullscreen (after hysteresis).</summary>
    public bool Hidden { get; private set; }

    /// <summary>Raw (pre-hysteresis) decision for the last input.</summary>
    public bool ShouldHide => mode switch
    {
        HideOnFullscreen.SameMonitor => monitors.Contains(overlayMonitor),
        HideOnFullscreen.AnyMonitor => monitors.Count > 0,
        _ => false,
    };

    /// <summary>Tick when the pending edge becomes effective, or null.</summary>
    public long? NextDeadline => pendingSince is { } since ? since + DelayTicks(ShouldHide) : null;

    public FullscreenTransition? Update(IReadOnlySet<string> fullscreenMonitors)
    {
        monitors = fullscreenMonitors;
        return Tick();
    }

    public FullscreenTransition? Reconfigure(HideOnFullscreen newMode, string newOverlayMonitor)
    {
        mode = newMode;
        overlayMonitor = newOverlayMonitor;
        return Tick();
    }

    /// <summary>Re-evaluates the stability timers against the clock.</summary>
    public FullscreenTransition? Tick()
    {
        var should = ShouldHide;
        if (should == Hidden) { pendingSince = null; return null; }
        var now = clock.NowTicks;
        pendingSince ??= now;
        if (now - pendingSince.Value < DelayTicks(should)) return null;
        Hidden = should;
        pendingSince = null;
        return should ? FullscreenTransition.Entered : FullscreenTransition.Exited;
    }

    private long DelayTicks(bool toHidden) =>
        (long)Math.Ceiling(clock.TicksPerSecond * (toHidden ? EnterDelayMs : ExitDelayMs) / 1000.0);
}
