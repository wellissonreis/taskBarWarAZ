namespace SideWar.Runtime.Input;

/// <summary>Interaction mode of the strip only (SDD-04 RF-04.1; the widget and other windows are always clickable).</summary>
public enum OverlayMode { Passive, Interactive }

/// <summary>What the platform must apply after a transition (SDD-04 RF-04.2 / section 7).</summary>
public readonly record struct ModeEffects(bool Transparent, byte Alpha, bool CancelDrag);

/// <summary>
/// Pure Passive/Interactive state machine with the auto-return deadline (SDD-04 RF-04.2/04.3, SDD-22 RF-22.3/22.7).
/// Time is a monotonic tick count; the host sleeps until <see cref="NextDeadline"/>. The strip is never
/// made fully transparent: alpha is 255 (present) or 128 (absent), independent of the mode (CA-23.1).
/// </summary>
public sealed class OverlayModeController
{
    public const double DefaultAutoPassiveSeconds = 8;
    public const byte PresentAlpha = 255;
    public const byte AbsentAlpha = 128;

    private readonly long autoPassiveTicks;
    private bool cursorOver;
    private bool dragActive;
    private bool absent;
    private long? deadline;

    public OverlayModeController(double ticksPerSecond, double autoPassiveSeconds = DefaultAutoPassiveSeconds)
    {
        if (!double.IsFinite(ticksPerSecond) || ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        if (!double.IsFinite(autoPassiveSeconds) || autoPassiveSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(autoPassiveSeconds));
        autoPassiveTicks = (long)Math.Round(autoPassiveSeconds * ticksPerSecond);
    }

    public OverlayMode Mode { get; private set; } = OverlayMode.Passive;
    public bool CursorOver => cursorOver;
    public bool DragActive => dragActive;
    public bool Absent => absent;

    /// <summary>Tick at which Interactive returns to Passive; null when no countdown runs.</summary>
    public long? NextDeadline => deadline;

    /// <summary>Current effects (initial application at startup).</summary>
    public ModeEffects Effects => new(Mode == OverlayMode.Passive, absent ? AbsentAlpha : PresentAlpha, false);

    /// <summary>Hotkey / widget control / menu: Passive ⇄ Interactive.</summary>
    public ModeEffects? Toggle(long now) => SetMode(Mode == OverlayMode.Passive ? OverlayMode.Interactive : OverlayMode.Passive, now);

    public ModeEffects? SetMode(OverlayMode mode, long now)
    {
        if (mode == Mode) return null;
        var cancel = mode == OverlayMode.Passive && dragActive;
        Mode = mode;
        if (mode == OverlayMode.Interactive)
        {
            cursorOver = false; // unknown until the first WM_MOUSEMOVE
            dragActive = false;
            deadline = now + autoPassiveTicks;
        }
        else
        {
            cursorOver = false;
            dragActive = false;
            deadline = null;
        }
        return Effects with { CancelDrag = cancel };
    }

    /// <summary>Overlay hidden (SDD-07): always back to Passive, so the next appearance is Passive.</summary>
    public ModeEffects? OnHidden(long now) => SetMode(OverlayMode.Passive, now);

    /// <summary>WM_MOUSEMOVE over the strip: cancels the countdown.</summary>
    public void OnMouseMove()
    {
        if (Mode != OverlayMode.Interactive) return;
        cursorOver = true;
        deadline = null;
    }

    /// <summary>WM_MOUSELEAVE: starts the countdown (unless a drag is active).</summary>
    public void OnMouseLeave(long now)
    {
        if (Mode != OverlayMode.Interactive) return;
        cursorOver = false;
        Restart(now);
    }

    /// <summary>Drag start/end from the game (SDD-15); an active drag holds the mode, its end restarts the countdown.</summary>
    public void SetDragActive(bool active, long now)
    {
        if (Mode != OverlayMode.Interactive) { dragActive = false; return; }
        dragActive = active;
        Restart(now);
    }

    /// <summary>Present/absent opacity (SDD-22 RF-22.7), independent of the mode. Returns effects if alpha changed.</summary>
    public ModeEffects? SetAbsent(bool value)
    {
        if (absent == value) return null;
        absent = value;
        return Effects;
    }

    /// <summary>Called at or after <see cref="NextDeadline"/>; returns Passive effects when the deadline elapsed.</summary>
    public ModeEffects? OnTick(long now)
    {
        if (Mode != OverlayMode.Interactive || deadline is not { } due || now < due) return null;
        return SetMode(OverlayMode.Passive, now);
    }

    private void Restart(long now) =>
        deadline = cursorOver || dragActive ? null : now + autoPassiveTicks;
}
