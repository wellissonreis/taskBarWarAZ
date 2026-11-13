using SideWar.Core;
using SideWar.Core.Simulation;
using SideWar.Runtime.Input;

namespace SideWar.Runtime.Panel;

public enum ArsenalHitKind : byte { None, AutoScaleToggle, UpgradeTower, TowerIcon, Reposition }

/// <summary>
/// Alvo clicável do corpo do Arsenal (RF-24.11). Botão bloqueado, ícone bloqueado/sem créditos/no limite, linha e fundo = <see cref="None"/>
/// (seta, sem hover). <see cref="TowerIcon"/> e <see cref="Reposition"/> são origens do modo construção (ADR-034).
/// </summary>
public readonly record struct ArsenalHit(ArsenalHitKind Kind, int TowerId = 0, TowerType Type = TowerType.None, string HintTitle = "")
{
    public static ArsenalHit None => default;
    public static ArsenalHit AutoScale => new(ArsenalHitKind.AutoScaleToggle);
    public static ArsenalHit Upgrade(int towerId) => new(ArsenalHitKind.UpgradeTower, towerId);
    public static ArsenalHit Icon(TowerType type, string hintTitle = "") => new(ArsenalHitKind.TowerIcon, 0, type, hintTitle);
    public static ArsenalHit Reposition(int towerId, TowerType type, string hintTitle = "") => new(ArsenalHitKind.Reposition, towerId, type, hintTitle);
    public bool IsNone => Kind == ArsenalHitKind.None;
}

public enum ArsenalIntentKind : byte { SetAutoScale, GameCommand, BeginBuildMode }

/// <summary>
/// Intenção devolvida pelo clique (mouse down e up no mesmo alvo; no ícone e no REPOSICIONAR, só o mouse down).
/// <see cref="Command"/> é <c>UpgradeTower</c> e vai para a T-Game e <c>state.Apply</c> entre ticks; <c>SetAutoScale</c> é de SDD-25 RF-25.5;
/// <see cref="BeginBuildMode"/> leva <see cref="Origin"/> para <c>BuildModeController.Press</c> (a captura real do mouse é da TA.8) e
/// <see cref="HintTitle"/> para <c>BuildOverlayFrame.HintTitle</c>. O painel nunca aplica nada diretamente.
/// </summary>
public readonly record struct ArsenalIntent(ArsenalIntentKind Kind, bool AutoScaleOn, IGameCommand? Command, BuildOrigin? Origin = null, string HintTitle = "")
{
    public static ArsenalIntent? FromHit(ArsenalHit hit, bool currentAutoScaleOn) => hit.Kind switch
    {
        ArsenalHitKind.AutoScaleToggle => new ArsenalIntent(ArsenalIntentKind.SetAutoScale, !currentAutoScaleOn, null),
        ArsenalHitKind.UpgradeTower => new ArsenalIntent(ArsenalIntentKind.GameCommand, currentAutoScaleOn, new UpgradeTower(hit.TowerId)),
        ArsenalHitKind.TowerIcon => new ArsenalIntent(ArsenalIntentKind.BeginBuildMode, currentAutoScaleOn, null, new BuildOrigin(hit.Type), hit.HintTitle),
        ArsenalHitKind.Reposition => new ArsenalIntent(ArsenalIntentKind.BeginBuildMode, currentAutoScaleOn, null, new BuildOrigin(hit.Type, hit.TowerId), hit.HintTitle),
        _ => null,
    };

    /// <summary>Ícone/REPOSICIONAR: inicia o modo construção já no <c>mouse down</c> (limiar de 6 px decide arrastar × armar).</summary>
    public static bool StartsOnPress(ArsenalHit hit) => hit.Kind is ArsenalHitKind.TowerIcon or ArsenalHitKind.Reposition;

    /// <summary>Liga a intenção ao controlador (nível puro); a plataforma só entrega o ponteiro e o relógio.</summary>
    public BuildOutcome Press(BuildModeController controller, BuildPointer cursor, long now)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return Origin is { } o ? controller.Press(o, cursor, now) : BuildOutcome.None(controller.Phase);
    }
}
