using SideWar.Core.Simulation;

namespace SideWar.Runtime.Presence;

/// <summary>
/// Aplica o <see cref="OfflineResult"/> ao <see cref="SimState"/> no <c>Show</c>, antes do primeiro frame, entre ticks
/// (SDD-25 RF-25.10, SDD-10 RF-10.x: não é comando; D-11). Determinístico: depende só do resultado e do estado.
/// Ordem: níveis das torres (por Id), saldo e carry, onda/<c>ondaMax</c> (reinício sem inimigos), e, se
/// <c>!ShowCard</c> (&lt; 10 min, DP-25.2), os ganhos pendentes direto ao saldo e ao XP. Com card, créditos e XP
/// só entram ao coletar (<see cref="CollectOfflineRewards"/>).
/// </summary>
public static class OfflineResultApplier
{
    public static void Apply(SimState state, SimContext ctx, OfflineResult result)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(result);
        if (state.InTick) throw new InvalidOperationException("O resultado offline só é aplicado entre ticks.");
        var s = result.State;
        foreach (var t in s.Towers)
        {
            var h = state.FindTowerById(t.Id);
            if (h.IsNone) continue;
            ref var tower = ref state.Towers.Get(h);
            if (t.Level > tower.Level) tower.Level = t.Level;
        }
        state.Credits = s.Credits;
        state.Carry = s.Carry;
        if (!result.ShowCard)
        {
            state.AddCredits(result.PendingCredits);
            state.AddXp(result.PendingXp);
        }
        state.RestartAtWave(ctx, s.Wave, s.WaveMax);
    }
}
