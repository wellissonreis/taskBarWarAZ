using SideWar.Core.Simulation;

namespace SideWar.Runtime.Hosting;

/// <summary>
/// Ponto de extensão por tick do <see cref="GameHost"/> (T6.6B: EventScheduler, BossEncounter). Chamado na T-Game,
/// fora de <c>BeginTick/EndTick</c>: pode usar <see cref="SimState.Apply"/> e ajustar fatores entre ticks.
/// </summary>
public interface IGameTickHook
{
    /// <summary>Antes de <see cref="SimState.Step"/>; <c>state.Tick</c> ainda é o tick que vai executar.</summary>
    void BeforeStep(SimState state, GameTickContext context);

    /// <summary>Depois de <see cref="SimState.Step"/>; <c>state.Tick</c> já foi incrementado.</summary>
    void AfterStep(SimState state, GameTickContext context);
}

/// <summary>Dados do tick para os hooks. <c>SimSeconds</c> = Tick / 30 do tick em execução.</summary>
public readonly record struct GameTickContext(long Tick, double SimSeconds, double WallSeconds);
