using System.Diagnostics;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Threading;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Power;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Windows;

/// <summary>
/// Applies the strip's Passive/Interactive mode (SDD-04 RF-04.1..04.5): toggles <c>WS_EX_TRANSPARENT</c> on the
/// scene window only (widget and other windows never get it), sets the global opacity (never 0, CA-23.1), tracks
/// the cursor for the 8 s auto-return and runs the optional peek. Pure logic lives in
/// <see cref="OverlayModeController"/>, <see cref="PeekController"/> and <see cref="SceneHitTester"/>.
/// All members require the owning T-Game. The trigger (hotkey, widget control, menu) is wired by the App
/// through <see cref="ToggleInteractive"/> / <see cref="SetMode"/>.
/// </summary>
public sealed class SceneModeController
{
    private readonly GameThread owner;
    private readonly OverlayModeController mode = new(Stopwatch.Frequency);
    private readonly PeekController peek = new(Stopwatch.Frequency);
    private SideWar.Core.Simulation.SimState? hitState;
    private float barHeightDip = SceneHitTester.DefaultBarHeight;
    private bool tracking;
    private bool peekEnabled;
    private PowerProfile powerProfile = PowerProfile.Normal;

    internal SceneModeController(GameThread owner) => this.owner = owner;

    public OverlayMode Mode => mode.Mode;
    public bool IsPeeking => peek.Peeking;
    public bool PeekEnabled => peekEnabled;

    /// <summary>Strip surface whose opacity is driven here; defaults to the one the dock resizes.</summary>
    public Rendering.LayeredSurface? Surface { get; set; }

    /// <summary>Overrides the DIP scale (dpi / 96); null = current dock monitor, else 1.</summary>
    public float? ScaleOverride { get; set; }

    /// <summary>Raised after the mode changed (also on automatic return and on hide): the App updates the widget indicator and the mode border (RF-04.7).</summary>
    public event Action<OverlayMode>? ModeChanged;

    /// <summary>A click on a tower or on the build overlay while Interactive (SDD-22 RF-22.3, RF-22.11). TA.8 maps the BuildOverlay click (position, right button) to <c>BuildModeController</c>.</summary>
    public event Action<HitTarget>? TargetClicked;

    /// <summary>The mode left Interactive with a drag in progress; the game must abort it (RF-04.2).</summary>
    public event Action? DragCancelled;

    /// <summary>Simulation state (layout + towers) and bar height used by the hit test (T-Game only; SDD-22 RF-22.3).</summary>
    public void SetScene(SideWar.Core.Simulation.SimState? state, float barHeight)
    {
        owner.VerifyAccess();
        hitState = state;
        barHeightDip = barHeight;
    }

    /// <summary>Build mode on/off (SDD-22 RF-22.11): forces Interactive, suspends the auto-return, returns to the previous mode on exit.</summary>
    public void SetBuildMode(bool active) => Apply(mode.SetBuildMode(active, Now));

    public bool BuildMode => mode.BuildActive;

    public HitTarget HitTest(int clientX, int clientY)
    {
        owner.VerifyAccess();
        var (x, y) = SceneHitTester.ToDip(clientX, clientY, Scale);
        return hitState is null ? HitTarget.None : SceneHitTester.Test(hitState, barHeightDip, mode.BuildActive, x, y);
    }

    /// <summary>Hotkey / widget control / menu trigger (RF-04.2).</summary>
    public void ToggleInteractive() => Apply(mode.Toggle(Now));

    public void SetMode(OverlayMode value) => Apply(mode.SetMode(value, Now));

    /// <summary>Present 255 / absent 128 (SDD-22 RF-22.7), same in both modes.</summary>
    public void SetAbsent(bool absent) => Apply(mode.SetAbsent(absent), modeChanged: false);

    /// <summary>The game reports an active drag (SDD-15) so the auto-return waits for it.</summary>
    public void SetDragActive(bool active)
    {
        owner.VerifyAccess();
        mode.SetDragActive(active, Now);
    }

    /// <summary>Peek is opt-in for the strip (DP-22.3); polling runs only while enabled, Passive and visible.</summary>
    public void SetPeekEnabled(bool enabled)
    {
        owner.VerifyAccess();
        peekEnabled = enabled;
        ConfigurePeek();
    }

    /// <summary>Power profile (SDD-07 RF-07.5): the peek poll runs only on Normal (SDD-16 RF-16.8).</summary>
    public void SetPowerProfile(PowerProfile profile)
    {
        owner.VerifyAccess();
        powerProfile = profile;
        ConfigurePeek();
    }

    /// <summary>Applies the current effects, e.g. after the surface was created (initial Passive).</summary>
    public void Reapply() => Apply(mode.Effects, modeChanged: false);

    internal long? NextDeadline => Min(mode.NextDeadline, peek.NextPoll);

    internal void OnWake(long now)
    {
        Apply(mode.OnTick(now));
        if (peek.PollDue(now) && peek.OnSample(now, CursorInStrip())) ApplyOpacity();
    }

    internal void OnSceneShown() => ConfigurePeek();

    /// <summary>Hidden → always Passive (RF-04.2), polling off.</summary>
    internal void OnSceneHidden()
    {
        Apply(mode.OnHidden(Now));
        ConfigurePeek();
    }

    /// <summary>Called from the scene WndProc.</summary>
    internal void OnMessage(HWND hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case PInvoke.WM_MOUSEMOVE when mode.Mode == OverlayMode.Interactive:
                mode.OnMouseMove();
                if (!tracking) tracking = TrackLeave(hwnd);
                break;
            case PInvoke.WM_MOUSELEAVE:
                tracking = false;
                mode.OnMouseLeave(Now);
                break;
            case PInvoke.WM_LBUTTONDOWN when mode.Mode == OverlayMode.Interactive:
                var target = HitTest((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                if (target.Kind != HitKind.None) TargetClicked?.Invoke(target);
                break;
        }
    }

    private static long Now => Stopwatch.GetTimestamp();

    private float Scale => ScaleOverride ?? (owner.Dock.Current is { } c ? c.Monitor.Dpi / 96f : 1f);

    private Rendering.LayeredSurface? ActiveSurface => Surface ?? owner.Dock.SceneSurface;

    private static unsafe bool TrackLeave(HWND hwnd)
    {
        var track = new TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(TRACKMOUSEEVENT),
            dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE,
            hwndTrack = hwnd,
        };
        return PInvoke.TrackMouseEvent(ref track);
    }

    private void ConfigurePeek()
    {
        var changed = peek.Configure(peekEnabled, mode.Mode == OverlayMode.Passive, owner.Scene.IsVisible, powerProfile, Now);
        if (changed) ApplyOpacity();
    }

    // RF-04.2: style + FRAMECHANGED, then opacity (a single present with only the new blend).
    private void Apply(ModeEffects? effects, bool modeChanged = true)
    {
        owner.VerifyAccess();
        if (effects is not { } fx) return;
        var hwnd = new HWND(owner.Scene.Handle);
        SetTransparent(hwnd, fx.Transparent);
        if (fx.CancelDrag)
        {
            PInvoke.ReleaseCapture();
            DragCancelled?.Invoke();
        }
        if (modeChanged) tracking = false;
        ConfigurePeek();
        ApplyOpacity();
        if (modeChanged) ModeChanged?.Invoke(mode.Mode);
    }

    private void ApplyOpacity()
    {
        var alpha = mode.Effects.Alpha;
        if (peek.Peeking) alpha = Math.Min(alpha, PeekController.PeekAlpha);
        ActiveSurface?.SetOpacity(alpha);
    }

    private static void SetTransparent(HWND hwnd, bool transparent)
    {
        var ex = (WINDOW_EX_STYLE)(nuint)WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var wanted = transparent ? ex | WINDOW_EX_STYLE.WS_EX_TRANSPARENT : ex & ~WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        if (wanted == ex) return;
        WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)(uint)wanted);
        if (!PInvoke.SetWindowPos(hwnd, default, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER))
            throw Win32Error.LastError("SetWindowPos(frame)");
    }

    // Cursor inside the strip and not over the always-clickable widget (RF-04.5).
    private bool CursorInStrip()
    {
        if (!PInvoke.GetCursorPos(out var p)) return false;
        if (!PInvoke.GetWindowRect(new HWND(owner.Scene.Handle), out var s) ||
            p.X < s.left || p.X >= s.right || p.Y < s.top || p.Y >= s.bottom) return false;
        if (owner.Scene.Widget is { IsVisible: true } w)
        {
            var b = w.Bounds;
            if (p.X >= b.Left && p.X < b.Right && p.Y >= b.Top && p.Y < b.Bottom) return false;
        }
        return true;
    }

    private static long? Min(long? a, long? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
}
