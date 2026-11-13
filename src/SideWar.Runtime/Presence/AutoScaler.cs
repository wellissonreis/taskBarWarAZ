using SideWar.Core.Content;
using SideWar.Core.Simulation;

namespace SideWar.Runtime.Presence;

/// <summary>Uma melhoria feita pela auto-escala, passo a passo (o card agrega por torre em <see cref="AutoUpgrade"/>).</summary>
public readonly record struct AutoUpgradeStep(int TowerId, TowerType Type, int FromLevel, int ToLevel, long Cost);

public sealed record AutoScaleResult(long Spent, IReadOnlyList<AutoUpgradeStep> Upgrades)
{
    public static AutoScaleResult None { get; } = new(0, Array.Empty<AutoUpgradeStep>());
}

/// <summary>Torre candidata da auto-escala: só entram torres posicionadas. <c>Level</c> é atualizado por <see cref="AutoScaler.Plan"/>.</summary>
public struct AutoScaleCandidate(int id, TowerType type, int level, long baseCost)
{
    public int Id { get; } = id;
    public TowerType Type { get; } = type;
    public int Level { get; set; } = level;
    public long BaseCost { get; } = baseCost;
}

/// <summary>
/// Auto-escala (SDD-25 RF-25.5). Só em Ausente/Offline e com o toggle ligado. Gasta até 50% do saldo no início da rodada
/// melhorando repetidamente a torre de menor custo de melhoria (empate: menor Id), recalculando o custo a cada melhoria.
/// Não passa por <c>Apply</c>: não conta em <c>ManualUpgrades</c>. Chamar entre ticks.
/// <see cref="Plan"/> é a única implementação da regra (simulação em <see cref="Run"/> e offline em <c>OfflineCalculator</c>).
/// </summary>
public static class AutoScaler
{
    public const double IntervalSeconds = 15 * 60;
    public const double BudgetFraction = 0.5;

    public static bool Applies(PresenceState presence) => presence != PresenceState.Presente;

    /// <summary>Orçamento da rodada: 50% do saldo (floor), nunca negativo.</summary>
    public static long Budget(long credits) => credits <= 0 ? 0 : credits / 2;

    /// <summary>Orçamento com fração configurável (<c>OfflineConfig.AutoScaleBudgetFraction</c>); 0,5 equivale a <see cref="Budget(long)"/>.</summary>
    public static long Budget(long credits, double fraction)
    {
        if (fraction == BudgetFraction) return Budget(credits);
        double v = Math.Floor(credits * fraction);
        return v <= 0 ? 0 : v >= long.MaxValue ? long.MaxValue : (long)v;
    }

    /// <summary>
    /// Regra pura da rodada: melhora repetidamente a candidata de menor custo (empate: menor Id) enquanto o custo couber no
    /// <paramref name="budget"/> restante; atualiza <c>Level</c> das candidatas e devolve os passos e o gasto.
    /// </summary>
    public static AutoScaleResult Plan(Span<AutoScaleCandidate> towers, EconomyDef econ, long budget)
    {
        ArgumentNullException.ThrowIfNull(econ);
        long spent = 0;
        List<AutoUpgradeStep>? list = null;
        while (true)
        {
            int best = -1;
            long bestCost = long.MaxValue;
            for (int i = 0; i < towers.Length; i++)
            {
                ref var t = ref towers[i];
                if (t.Level >= econ.MaxTowerLevel) continue;
                if (!TowerCosts.TryUpgradeCost(t.BaseCost, t.Level, econ.UpgradeCostFactor, econ.UpgradeCostGrowth, out long c)) continue;
                if (best < 0 || c < bestCost || (c == bestCost && t.Id < towers[best].Id)) { best = i; bestCost = c; }
            }
            if (best < 0 || bestCost > budget - spent) break;
            ref var tower = ref towers[best];
            int from = tower.Level++;
            spent += bestCost;
            (list ??= []).Add(new AutoUpgradeStep(tower.Id, tower.Type, from, tower.Level, bestCost));
        }
        return list is null ? AutoScaleResult.None : new AutoScaleResult(spent, list);
    }

    public static AutoScaleResult Run(SimState state, SimContext ctx, bool enabled, PresenceState presence)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        if (!enabled || !Applies(presence) || state.Towers.Count == 0) return AutoScaleResult.None;

        var cands = new List<AutoScaleCandidate>(state.Towers.Count);
        var slots = new List<int>(state.Towers.Count);
        for (int i = 0; i < state.Towers.Capacity; i++)
        {
            if (!state.Towers.IsAlive(i)) continue;
            ref var t = ref state.Towers.At(i);
            if (!t.Placed) continue; // SEM POSIÇÃO não recebe melhoria (ADR-034, SDD-25)
            cands.Add(new AutoScaleCandidate(t.Id, t.Type, t.Level, ctx.Tower(t.Type).Cost));
            slots.Add(i);
        }
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cands);
        var result = Plan(span, ctx.Content.Economy, Budget(state.Credits));
        if (result.Spent == 0) return result;
        for (int k = 0; k < span.Length; k++) state.Towers.At(slots[k]).Level = span[k].Level;
        state.Credits -= result.Spent;
        return result;
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

    /// <summary>Restaura o contador persistido (SDD-14).</summary>
    public void Restore(double accumSeconds) => AccumSeconds = Math.Max(0, accumSeconds);
}
