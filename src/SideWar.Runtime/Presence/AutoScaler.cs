using SideWar.Core.Simulation;

namespace SideWar.Runtime.Presence;

/// <summary>Uma melhoria feita pela auto-escala (entra na lista do card, RF-25.9).</summary>
public readonly record struct AutoUpgrade(int TowerId, TowerType Type, int FromLevel, int ToLevel, long Cost);

public sealed record AutoScaleResult(long Spent, IReadOnlyList<AutoUpgrade> Upgrades)
{
    public static AutoScaleResult None { get; } = new(0, Array.Empty<AutoUpgrade>());
}

/// <summary>
/// Auto-escala (SDD-25 RF-25.5). Só em Ausente/Offline e com o toggle ligado. Gasta até 50% do saldo no início da rodada
/// melhorando repetidamente a torre de menor custo de melhoria (empate: menor Id), recalculando o custo a cada melhoria.
/// Não passa por <c>Apply</c>: não conta em <c>ManualUpgrades</c>. Chamar entre ticks.
/// </summary>
public static class AutoScaler
{
    public const double IntervalSeconds = 15 * 60;
    public const double BudgetFraction = 0.5;

    public static bool Applies(PresenceState presence) => presence != PresenceState.Presente;

    /// <summary>Orçamento da rodada: 50% do saldo (floor), nunca negativo.</summary>
    public static long Budget(long credits) => credits <= 0 ? 0 : credits / 2;

    public static AutoScaleResult Run(SimState state, SimContext ctx, bool enabled, PresenceState presence)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        if (!enabled || !Applies(presence) || state.Towers.Count == 0) return AutoScaleResult.None;

        var econ = ctx.Content.Economy;
        long budget = Budget(state.Credits);
        long spent = 0;
        var list = new List<AutoUpgrade>();
        while (true)
        {
            int bestIdx = -1;
            long bestCost = long.MaxValue;
            int bestId = int.MaxValue;
            for (int i = 0; i < state.Towers.Capacity; i++)
            {
                if (!state.Towers.IsAlive(i)) continue;
                ref var t = ref state.Towers.At(i);
                if (t.Level >= econ.MaxTowerLevel) continue;
                if (!TowerCosts.TryUpgradeCost(ctx.Tower(t.Type).Cost, t.Level, econ.UpgradeCostFactor, econ.UpgradeCostGrowth, out long c))
                    continue;
                if (c < bestCost || (c == bestCost && t.Id < bestId)) { bestIdx = i; bestCost = c; bestId = t.Id; }
            }
            if (bestIdx < 0 || bestCost > budget - spent || bestCost > state.Credits) break;
            ref var tower = ref state.Towers.At(bestIdx);
            int from = tower.Level++;
            state.Credits -= bestCost;
            spent += bestCost;
            list.Add(new AutoUpgrade(tower.Id, tower.Type, from, tower.Level, bestCost));
        }
        return list.Count == 0 ? AutoScaleResult.None : new AutoScaleResult(spent, list);
    }
}

/// <summary>Contador <c>autoScaleAccumSeconds</c> (persistido): acumula em Ausente/Offline e zera em Presente (RF-25.5).</summary>
public sealed class AutoScaleAccumulator
{
    public double AccumSeconds { get; private set; }

    public AutoScaleAccumulator(double accumSeconds = 0) => AccumSeconds = Math.Max(0, accumSeconds);

    /// <summary>Avança <paramref name="seconds"/> no estado dado e devolve quantas rodadas de 15 min venceram.</summary>
    public int Advance(PresenceState presence, double seconds)
    {
        if (!AutoScaler.Applies(presence)) { AccumSeconds = 0; return 0; }
        if (seconds <= 0) return 0;
        AccumSeconds += seconds;
        int rounds = (int)Math.Floor(AccumSeconds / AutoScaler.IntervalSeconds);
        AccumSeconds -= rounds * AutoScaler.IntervalSeconds;
        return rounds;
    }

    public void ReturnedToPresent() => AccumSeconds = 0;
}
