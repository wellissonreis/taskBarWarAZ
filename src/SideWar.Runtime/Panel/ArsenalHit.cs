using SideWar.Core;
using SideWar.Core.Simulation;

namespace SideWar.Runtime.Panel;

public enum ArsenalHitKind : byte { None, AutoScaleToggle, UpgradeTower, BuildTower }

/// <summary>Alvo clicável do corpo do Arsenal (RF-24.11). Botão bloqueado/SEM SLOT/linha e fundo = <see cref="None"/> (seta, sem hover).</summary>
public readonly record struct ArsenalHit(ArsenalHitKind Kind, int TowerId = 0, TowerType Type = TowerType.None, int Slot = -1)
{
    public static ArsenalHit None => default;
    public static ArsenalHit AutoScale => new(ArsenalHitKind.AutoScaleToggle);
    public static ArsenalHit Upgrade(int towerId) => new(ArsenalHitKind.UpgradeTower, towerId);
    public static ArsenalHit Build(TowerType type, int slot) => new(ArsenalHitKind.BuildTower, 0, type, slot);
    public bool IsNone => Kind == ArsenalHitKind.None;
}

public enum ArsenalIntentKind : byte { SetAutoScale, GameCommand }

/// <summary>
/// Intenção devolvida pelo clique (mouse down e up no mesmo alvo). <see cref="Command"/> é <c>BuildTower</c>/<c>UpgradeTower</c> (T3.7)
/// e deve ir para a T-Game e <c>state.Apply</c> entre ticks; <c>SetAutoScale</c> é de SDD-25 RF-25.5 (não é comando do Core).
/// O painel nunca aplica nada diretamente.
/// </summary>
public readonly record struct ArsenalIntent(ArsenalIntentKind Kind, bool AutoScaleOn, IGameCommand? Command)
{
    public static ArsenalIntent? FromHit(ArsenalHit hit, bool currentAutoScaleOn) => hit.Kind switch
    {
        ArsenalHitKind.AutoScaleToggle => new ArsenalIntent(ArsenalIntentKind.SetAutoScale, !currentAutoScaleOn, null),
        ArsenalHitKind.UpgradeTower => new ArsenalIntent(ArsenalIntentKind.GameCommand, currentAutoScaleOn, new UpgradeTower(hit.TowerId)),
        ArsenalHitKind.BuildTower => new ArsenalIntent(ArsenalIntentKind.GameCommand, currentAutoScaleOn, new BuildTower(hit.Type, hit.Slot)),
        _ => null,
    };
}
