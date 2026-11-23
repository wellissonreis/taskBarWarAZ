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
    private OverlayModeController mode = new(Stopwatch.Frequency);
    private bool tdHover;
    private readonly TowerGrabGate grab = new();
    private long grabNext;
    private long hoverNext;
    private readonly PeekController peek = new(Stopwatch.Frequency);
    private SideWar.Core.Simulation.SimState? hitState;
    private float barHeightDip = SceneHitTester.DefaultBarHeight;
    private bool tracking;
    private bool peekEnabled;
    private byte peekAlpha = PeekController.PeekAlpha;
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

    /// <summary>TD mode (TD.7): hit test in DIP for the TD band (<c>TdHitTester</c>); null = the idle test.</summary>
    public Func<float, float, HitTarget>? TdHitTest { get; set; }

    /// <summary>
    /// Fusão por arrasto: há, neste ponto (DIP da janela), uma torre colocada que pode ser pegada? Só então a faixa deixa de ser click-through
    /// (<see cref="TowerGrabGate"/>); null desliga o recurso (a faixa segue 100% click-through fora do modo posicionar).
    /// </summary>
    public Func<float, float, bool>? TdGrabTest { get; set; }

    /// <summary>O arrasto de torre perdeu a captura do mouse sem soltar o botão (Alt+Tab, janela modal...): o jogo cancela o arrasto.</summary>
    public event Action? TdGrabCancelled;

    /// <summary>Uma torre está sob o cursor ou capturada (a faixa está recebendo o botão).</summary>
    public bool GrabHot => grab.Hot;

    /// <summary>
    /// TD (TD.16b): a faixa é CLICK-THROUGH por padrão (WS_EX_TRANSPARENT: cliques e roda vão para o app de baixo) e hover NÃO a torna
    /// interativa nem conta como presença. Só o modo posicionar (<see cref="SetBuildMode"/>) a torna Interativa. Liga o poll de 10 Hz que
    /// detecta clique/tecla em outro app (<see cref="TdExternalInput"/>).
    /// </summary>
    public void EnableTdPolling(double returnSeconds)
    {
        owner.VerifyAccess();
        mode = new OverlayModeController(Stopwatch.Frequency, returnSeconds);
        tdHover = true;
        hoverNext = Now;
    }

    private long suppressExternalUntil;

    /// <summary>TD.16b: ignora "entrada externa" por alguns segundos (o próprio atalho global do SideWar não é digitação em outro app).</summary>
    public void SuppressExternalInput(double seconds) => suppressExternalUntil = Now + (long)(seconds * Stopwatch.Frequency);

    /// <summary>TD pointer over the scene while Interactive: (xDip, yDip) move; click with its hit target; right click.</summary>
    public event Action<float, float>? TdPointerMoved;
    public event Action<float, float, HitTarget>? TdLeftClicked;
    /// <summary>Botão esquerdo solto (modo Interativo): termina o arrasto de torre (X em DIP).</summary>
    public event Action<float>? TdLeftReleased;
    public event Action? TdRightClicked;

    /// <summary>
    /// TD.16: clique de mouse com o cursor FORA de qualquer janela do SideWar, ou tecla digitada com o foreground em outro app (poll de
    /// 10 Hz). Janelas da faixa/HUD são no-activate, então voltar ao Chrome não gera mudança de foreground: o clique externo é a prova de
    /// que o jogador saiu. Poll de GetAsyncKeyState (bit alto = pressionado agora, bit baixo = pressionado desde a última leitura) em vez de
    /// hook de mouse global: sem DLL injetada/callback em cada movimento, custo desprezível a 10 Hz e sem exigir loop de mensagens dedicado.
    /// </summary>
    public event Action? TdExternalInput;

    private bool prevMouseDown;

    private byte tdFadeAlpha = 255;

    /// <summary>TD.14: opacidade máxima da faixa enquanto o jogador está ausente (255 = sem esmaecer); combina com o menor dos outros níveis.</summary>
    public void SetTdFade(byte alpha)
    {
        owner.VerifyAccess();
        if (tdFadeAlpha == alpha) return;
        tdFadeAlpha = alpha;
        ApplyOpacity();
    }

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
        if (TdHitTest is { } td) return td(x, y);
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

    /// <summary>Fade levels from settings: alpha while peeking and while absent (255 = never fades when absent).</summary>
    public void SetOpacityLevels(byte peek, byte absent)
    {
        owner.VerifyAccess();
        peekAlpha = peek;
        var fx = mode.SetAbsentAlpha(absent);
        if (fx is not null) Apply(fx, modeChanged: false); else ApplyOpacity();
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

    internal long? NextDeadline => Min(Min(Min(mode.NextDeadline, peek.NextPoll), tdHover && owner.Scene.IsVisible ? hoverNext : null),
        tdHover && TdGrabTest is not null && owner.Scene.IsVisible ? grabNext : null);

    internal void OnWake(long now)
    {
        Apply(mode.OnTick(now));
        if (tdHover && now >= hoverNext)
        {
            hoverNext = now + Stopwatch.Frequency / 10;
            if (TdExternalInput is not null && owner.Scene.IsVisible)
            {
                bool seen = ExternalInputSeen();
                if (seen && now >= suppressExternalUntil) TdExternalInput();
            }
        }
        if (tdHover && TdGrabTest is not null && now >= grabNext)
        {
            grabNext = now + Stopwatch.Frequency / 20; // 20 Hz: só GetCursorPos; sem trabalho enquanto o cursor está fora da faixa
            PollGrab();
        }
        if (peek.PollDue(now) && peek.OnSample(now, CursorInStrip())) ApplyOpacity();
    }

    internal void OnSceneShown() => ConfigurePeek();

    /// <summary>Hidden → always Passive (RF-04.2), polling off.</summary>
    internal void OnSceneHidden()
    {
        EndGrab(false);
        Apply(mode.OnHidden(Now));
        ConfigurePeek();
    }

    /// <summary>Called from the scene WndProc.</summary>
    internal void OnMessage(HWND hwnd, uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case PInvoke.WM_MOUSEMOVE when mode.Mode != OverlayMode.Interactive && grab.Hot:
                if (!tracking) tracking = TrackLeave(hwnd);
                if (grab.Captured)
                {
                    if (TdPointerMoved is { } dragMove)
                    {
                        var (gx, gy) = SceneHitTester.ToDip((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF), Scale);
                        dragMove(gx, gy);
                    }
                }
                else if (!GrabOver((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF))) UpdateGrab(false); // saiu da torre: volta a ser click-through
                break;
            case PInvoke.WM_MOUSEMOVE when mode.Mode == OverlayMode.Interactive:
                mode.OnMouseMove();
                if (!tracking) tracking = TrackLeave(hwnd);
                if (TdPointerMoved is not null)
                {
                    var (mx, my) = SceneHitTester.ToDip((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF), Scale);
                    TdPointerMoved(mx, my);
                }
                break;
            case PInvoke.WM_RBUTTONDOWN when mode.Mode == OverlayMode.Interactive:
                TdRightClicked?.Invoke();
                break;
            case PInvoke.WM_MOUSELEAVE:
                tracking = false;
                mode.OnMouseLeave(Now);
                if (!grab.Captured && grab.Hot) UpdateGrab(false);
                break;
            case PInvoke.WM_LBUTTONUP when grab.Captured:
            {
                short ux = (short)(lParam & 0xFFFF), uy = (short)((lParam >> 16) & 0xFFFF);
                EndGrab(GrabOver(ux, uy)); // solta a captura ANTES de o jogo reagir; reavalia pelo ponto final
                TdLeftReleased?.Invoke(SceneHitTester.ToDip(ux, uy, Scale).Item1);
                break;
            }
            case PInvoke.WM_CAPTURECHANGED when grab.Captured:
                EndGrab(false);
                TdGrabCancelled?.Invoke();
                break;
            case PInvoke.WM_LBUTTONUP when mode.Mode == OverlayMode.Interactive && TdLeftReleased is { } released:
                released(SceneHitTester.ToDip((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF), Scale).Item1);
                break;
            case PInvoke.WM_LBUTTONDOWN when mode.Mode != OverlayMode.Interactive && grab.BeginCapture():
            {
                // O botão chegou porque a torre estava sob o cursor: captura o mouse (sem foco) até soltar; o jogo decide se vira arrasto.
                PInvoke.SetCapture(hwnd);
                short dx = (short)(lParam & 0xFFFF), dy = (short)((lParam >> 16) & 0xFFFF);
                if (TdLeftClicked is { } grabClick)
                {
                    var (cx, cy) = SceneHitTester.ToDip(dx, dy, Scale);
                    grabClick(cx, cy, HitTest(dx, dy));
                }
                break;
            }
            case PInvoke.WM_LBUTTONDOWN when mode.Mode == OverlayMode.Interactive:
                var target = HitTest((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                if (TdLeftClicked is { } tdClick)
                {
                    var (cx, cy) = SceneHitTester.ToDip((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF), Scale);
                    tdClick(cx, cy, target);
                }
                else if (target.Kind != HitKind.None) TargetClicked?.Invoke(target);
                break;
        }
    }

    private static long Now => Stopwatch.GetTimestamp();

    private bool GrabOver(int clientX, int clientY)
    {
        if (TdGrabTest is not { } test) return false;
        var (x, y) = SceneHitTester.ToDip(clientX, clientY, Scale);
        return test(x, y);
    }

    /// <summary>Poll do cursor (20 Hz): torre sob o cursor deixa a faixa receber o botão; fora, volta a ser click-through. Não toca em modo nem foco.</summary>
    private void PollGrab()
    {
        if (grab.Captured || mode.Mode == OverlayMode.Interactive || !owner.Scene.IsVisible) return;
        bool over = false;
        if (PInvoke.GetCursorPos(out var p) && PInvoke.GetWindowRect(new HWND(owner.Scene.Handle), out var s) &&
            p.X >= s.left && p.X < s.right && p.Y >= s.top && p.Y < s.bottom)
            over = GrabOver(p.X - s.left, p.Y - s.top);
        UpdateGrab(over);
    }

    private void UpdateGrab(bool over)
    {
        if (grab.SetOver(over)) RefreshTransparency();
    }

    private void EndGrab(bool overAtEnd)
    {
        bool wasCaptured = grab.Captured;
        bool changed = grab.EndCapture(overAtEnd);
        if (wasCaptured) PInvoke.ReleaseCapture();
        if (changed) RefreshTransparency();
    }

    private void RefreshTransparency()
    {
        if (owner.Scene.Handle == 0) return;
        SetTransparent(new HWND(owner.Scene.Handle), grab.WantsTransparent(mode.Effects.Transparent));
        if (!grab.Hot) tracking = false;
    }

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
        SetTransparent(hwnd, grab.WantsTransparent(fx.Transparent));
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
        if (peek.Peeking) alpha = Math.Min(alpha, peekAlpha);
        if (tdFadeAlpha < 255) alpha = Math.Min(alpha, tdFadeAlpha);
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

    private static bool KeyHit(int vk) => (PInvoke.GetAsyncKeyState(vk) & 0x8001) != 0;

    private static bool OwnWindowAt(System.Drawing.Point p)
    {
        var h = PInvoke.WindowFromPoint(p);
        if (h.IsNull) return false;
        uint pid;
        unsafe { _ = PInvoke.GetWindowThreadProcessId(h, &pid); }
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>TD.16: clique (borda de descida) fora das janelas do SideWar, ou teclado com o foreground em outro app.</summary>
    private bool ExternalInputSeen()
    {
        bool down = KeyHit(0x01) || KeyHit(0x02) || KeyHit(0x04);
        bool edge = down && !prevMouseDown;
        prevMouseDown = down;
        if (edge && PInvoke.GetCursorPos(out var p) && !CursorInStrip() && !OwnWindowAt(p)) return true;
        var fg = PInvoke.GetForegroundWindow();
        if (fg.IsNull) return false;
        uint fpid;
        unsafe { _ = PInvoke.GetWindowThreadProcessId(fg, &fpid); }
        if (fpid == (uint)Environment.ProcessId) return false;
        for (int vk = 0x08; vk <= 0xFE; vk++)
        {
            if (vk is 0x10 or 0x11 or 0x12) continue; // modificadores sozinhos não contam
            if ((PInvoke.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        }
        return false;
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
