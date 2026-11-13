using SideWar.Core;
using SideWar.Core.Simulation;

namespace SideWar.Runtime.Input;

/// <summary>Fase do modo construção (SDD-15 RF-15.3).</summary>
public enum BuildPhase : byte { Idle, Pressed, Dragging, Armed }

/// <summary>Por que o modo foi cancelado (SDD-15 RF-15.3, DP-33.8).</summary>
public enum BuildCancelReason : byte { Escape, RightClick, CaptureLost, PanelClick, Hidden, Absent, Timeout }

/// <summary>De onde o modo nasceu: ícone de torre nova (<c>BuildTower</c>) ou <c>REPOSICIONAR</c> de uma torre SEM POSIÇÃO (<c>PlaceTower</c>, DP-33.1).</summary>
public readonly record struct BuildOrigin(TowerType Type, int? RepositionTowerId = null)
{
    public bool IsReposition => RepositionTowerId.HasValue;
}

/// <summary>
/// Amostra do ponteiro, já convertida pela plataforma (TA.8): <paramref name="ScreenX"/>/<paramref name="ScreenY"/> em px físicos
/// (só para o limiar de 6 px), <paramref name="StripXDip"/> em DIP da faixa (origem esquerda da janela de cena) e
/// <paramref name="OverStrip"/> = o cursor está sobre a janela de cena (cena + barra).
/// </summary>
public readonly record struct BuildPointer(int ScreenX, int ScreenY, float StripXDip, bool OverStrip);

/// <summary>Fantasma do modo construção: X inteiro em DIP e validade segundo o <see cref="PlacementValidator"/> (só visual).</summary>
public readonly record struct BuildGhost(int X, bool Valid, PlacementReason Reason);

/// <summary>Resultado de um evento: comando a postar (se houver) e a transição de fase para a plataforma reagir.</summary>
public readonly record struct BuildOutcome(IGameCommand? Command, BuildPhase From, BuildPhase To, BuildCancelReason? Cancelled)
{
    public static BuildOutcome None(BuildPhase phase) => new(null, phase, phase, null);

    public bool Changed => From != To;
    /// <summary>O overlay (e o Interativo forçado) acabou de ligar.</summary>
    public bool OverlayEntered => !BuildModeController.IsOverlay(From) && BuildModeController.IsOverlay(To);
    /// <summary>O overlay acabou de desligar (a plataforma volta ao modo anterior da faixa).</summary>
    public bool OverlayExited => BuildModeController.IsOverlay(From) && !BuildModeController.IsOverlay(To);
    /// <summary>A plataforma deve soltar a captura do painel (saiu de <c>Pressed</c>, ou o modo acabou).</summary>
    public bool ReleaseCapture => From == BuildPhase.Pressed && To != BuildPhase.Pressed || (From != BuildPhase.Idle && To == BuildPhase.Idle);
}

/// <summary>
/// Máquina do modo construção (SDD-15 RF-15.3, SDD-22 RF-22.11): pura, sem relógio nem Windows. O tempo é um contador monotônico
/// (como em <see cref="OverlayModeController"/>). Pré-condições do início (torre desbloqueada, créditos, limite de 24) são do Arsenal.
/// A posição é revalidada a cada chamada com o <see cref="PlacementValidator"/> e o estado atual (layout e torres);
/// a decisão final continua do comando aplicado entre ticks.
/// </summary>
public sealed class BuildModeController
{
    public const int DragThresholdPx = 6;
    public const double DefaultTimeoutSeconds = 30;

    private readonly long timeoutTicks;
    private BuildOrigin origin;
    private int pressX, pressY;
    private long lastMove;
    private BuildGhost? ghost;

    public BuildModeController(double ticksPerSecond, double timeoutSeconds = DefaultTimeoutSeconds)
    {
        if (!double.IsFinite(ticksPerSecond) || ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        if (!double.IsFinite(timeoutSeconds) || timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        timeoutTicks = (long)Math.Round(timeoutSeconds * ticksPerSecond);
    }

    public BuildPhase Phase { get; private set; } = BuildPhase.Idle;
    public BuildOrigin Origin => origin;
    /// <summary>Overlay visível e cena Interativa forçada (<c>Dragging</c> ou <c>Armed</c>).</summary>
    public bool OverlayActive => IsOverlay(Phase);
    public BuildGhost? Ghost => ghost;

    /// <summary>Tick em que o modo expira por falta de movimento; null fora do modo.</summary>
    public long? NextDeadline => Phase == BuildPhase.Idle ? null : lastMove + timeoutTicks;

    internal static bool IsOverlay(BuildPhase p) => p is BuildPhase.Dragging or BuildPhase.Armed;

    /// <summary><c>WM_LBUTTONDOWN</c> no ícone de torre (ou no botão REPOSICIONAR): <c>Idle → Pressed</c> (a plataforma faz <c>SetCapture</c>).</summary>
    public BuildOutcome Press(BuildOrigin from, BuildPointer cursor, long now)
    {
        if (Phase != BuildPhase.Idle || from.Type == TowerType.None) return BuildOutcome.None(Phase);
        origin = from;
        pressX = cursor.ScreenX; pressY = cursor.ScreenY;
        lastMove = now;
        ghost = null;
        return Transition(BuildPhase.Pressed);
    }

    /// <summary>Movimento do ponteiro: passa o limiar (<c>Pressed → Dragging</c>), move o fantasma e zera o timeout de 30 s.</summary>
    public BuildOutcome Move(BuildPointer cursor, StripLayout layout, TowerStore towers, long now)
    {
        if (Phase == BuildPhase.Idle) return BuildOutcome.None(Phase);
        lastMove = now;
        if (Phase == BuildPhase.Pressed)
        {
            long dx = cursor.ScreenX - pressX, dy = cursor.ScreenY - pressY;
            if (dx * dx + dy * dy <= (long)DragThresholdPx * DragThresholdPx) return BuildOutcome.None(Phase);
            UpdateGhost(cursor, layout, towers);
            return Transition(BuildPhase.Dragging);
        }
        UpdateGhost(cursor, layout, towers);
        return BuildOutcome.None(Phase);
    }

    /// <summary><c>WM_LBUTTONUP</c> do arraste: <c>Pressed → Armed</c>; <c>Dragging</c> → comando se válido e sobre a faixa, senão <c>Armed</c>.</summary>
    public BuildOutcome Release(BuildPointer cursor, StripLayout layout, TowerStore towers, long now)
    {
        switch (Phase)
        {
            case BuildPhase.Pressed:
                lastMove = now;
                UpdateGhost(cursor, layout, towers);
                return Transition(BuildPhase.Armed);
            case BuildPhase.Dragging:
                lastMove = now;
                UpdateGhost(cursor, layout, towers);
                if (cursor.OverStrip && ghost is { Valid: true } g) return Commit(g.X);
                return Transition(BuildPhase.Armed);
            default:
                return BuildOutcome.None(Phase);
        }
    }

    /// <summary>Clique esquerdo na cena (alvo <c>BuildOverlay</c>) em <c>Armed</c>: válido → comando e <c>Idle</c>; inválido → nada.</summary>
    public BuildOutcome Click(BuildPointer cursor, StripLayout layout, TowerStore towers, long now)
    {
        if (Phase != BuildPhase.Armed) return BuildOutcome.None(Phase);
        lastMove = now;
        UpdateGhost(cursor, layout, towers);
        return ghost is { Valid: true } g ? Commit(g.X) : BuildOutcome.None(Phase);
    }

    /// <summary>Revalida o fantasma contra o layout/torres atuais (entre ticks, ex.: <c>LayoutChanged</c> ou outra torre posicionada) sem mover o ponteiro.</summary>
    public bool Revalidate(StripLayout layout, TowerStore towers)
    {
        if (!OverlayActive || ghost is not { } g) return false;
        var fresh = Evaluate(g.X, layout, towers);
        if (fresh == g) return false;
        ghost = fresh;
        return true;
    }

    /// <summary>Cancelamento por ESC, clique direito, perda de captura, clique no painel, ocultação ou presença Ausente.</summary>
    public BuildOutcome Cancel(BuildCancelReason reason)
    {
        if (Phase == BuildPhase.Idle) return BuildOutcome.None(Phase);
        // WM_CAPTURECHANGED só cancela durante o arraste (Pressed/Dragging); em Armed a captura já foi solta de propósito.
        if (reason == BuildCancelReason.CaptureLost && Phase == BuildPhase.Armed) return BuildOutcome.None(Phase);
        return Exit(reason);
    }

    /// <summary>Verificação do prazo de 30 s sem movimento (chamar em <see cref="NextDeadline"/>).</summary>
    public BuildOutcome OnTick(long now) =>
        Phase != BuildPhase.Idle && now >= lastMove + timeoutTicks ? Exit(BuildCancelReason.Timeout) : BuildOutcome.None(Phase);

    private BuildOutcome Commit(int x)
    {
        IGameCommand cmd = origin.RepositionTowerId is { } id ? new PlaceTower(id, x) : new BuildTower(origin.Type, x);
        var from = Phase;
        Reset();
        return new(cmd, from, BuildPhase.Idle, null);
    }

    private BuildOutcome Exit(BuildCancelReason reason)
    {
        var from = Phase;
        Reset();
        return new(null, from, BuildPhase.Idle, reason);
    }

    private BuildOutcome Transition(BuildPhase to)
    {
        var from = Phase;
        Phase = to;
        return new(null, from, to, null);
    }

    private void Reset()
    {
        Phase = BuildPhase.Idle;
        ghost = null;
        origin = default;
    }

    private void UpdateGhost(BuildPointer cursor, StripLayout layout, TowerStore towers)
    {
        if (!float.IsFinite(cursor.StripXDip)) return;
        // Math.round do canvas (meio para cima), DIP inteiro.
        int x = (int)Math.Floor((double)cursor.StripXDip + 0.5);
        ghost = Evaluate(x, layout, towers);
    }

    private BuildGhost Evaluate(int x, StripLayout layout, TowerStore towers)
    {
        var reason = PlacementValidator.Validate(x, layout, towers, origin.RepositionTowerId);
        return new(x, reason == PlacementReason.None, reason);
    }
}
