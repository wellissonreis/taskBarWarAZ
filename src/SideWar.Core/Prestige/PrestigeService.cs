using SideWar.Core.Content;
using SideWar.Core.Simulation;

namespace SideWar.Core.Prestige;

/// <summary>
/// "Migrar" e regiões (SDD-27 RF-27.10). Regras puras (disponibilidade, Reservas ganhas, desbloqueio de região) e a
/// validação do comando <see cref="MigrateTo"/>; a mutação do estado é de <c>SimState.ApplyMigration</c>.
/// </summary>
public static class PrestigeService
{
    /// <summary>Disponível quando a onda máxima da passagem atual chega a <c>prestige.minWave</c> (15).</summary>
    public static bool IsAvailable(EconomyDef eco, int waveMax)
    {
        ArgumentNullException.ThrowIfNull(eco);
        return waveMax >= eco.Prestige.MinWave;
    }

    /// <summary><c>floor(floor(ondaMax² / 170) × multReservasRegião)</c>; 0 se indisponível. Multiplicação em decimal (sem erro binário).</summary>
    public static long ReservesGain(EconomyDef eco, RegionDef region, int waveMax)
    {
        ArgumentNullException.ThrowIfNull(eco);
        ArgumentNullException.ThrowIfNull(region);
        if (!IsAvailable(eco, waveMax)) return 0;
        long baseGain = (long)waveMax * waveMax / eco.Prestige.Divisor;
        decimal gain = Math.Floor(baseGain * (decimal)region.ReservesMultiplier);
        return gain >= long.MaxValue ? long.MaxValue : (long)gain;
    }

    /// <summary>Região desbloqueada: <c>always</c>; <c>firstMigration</c> com ≥ 1 migração; <c>reserves</c> com Reservas ≥ <c>unlockReserves</c> (acumuladas: nunca são gastas).</summary>
    public static bool IsRegionUnlocked(RegionDef region, long reservas, int migrations)
    {
        ArgumentNullException.ThrowIfNull(region);
        return region.Unlock switch
        {
            "always" => true,
            "firstMigration" => migrations >= 1,
            "reserves" => reservas >= (region.UnlockReserves ?? long.MaxValue),
            _ => false,
        };
    }

    /// <summary>Índices das regiões desbloqueadas no estado atual.</summary>
    public static IReadOnlyList<int> UnlockedRegions(SimState state, GameContent content)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(content);
        var list = new List<int>(content.Regions.Length);
        for (int i = 0; i < content.Regions.Length; i++)
            if (IsRegionUnlocked(content.Regions[i], state.Reservas, state.Migrations)) list.Add(i);
        return list;
    }

    /// <summary>Reservas que Migrar daria agora, com a região **da partida que termina** (DP-27.12).</summary>
    public static long PreviewGain(SimState state, SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        return ReservesGain(ctx.Content.Economy, ctx.Content.Regions[state.RegionIndex], state.StageWaveMax);
    }

    /// <summary>
    /// Valida e aplica Migrar para a região escolhida. <c>NotAvailable</c> se ondaMax &lt; 15; <c>RegionLocked</c> se a região
    /// não existe ou está bloqueada. Só entre ticks.
    /// </summary>
    public static CommandResult Migrate(SimState state, SimContext ctx, int regionIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        if (state.InTick) throw new InvalidOperationException("Migrar só entre ticks.");
        var content = ctx.Content;
        if (!IsAvailable(content.Economy, state.StageWaveMax)) return CommandResult.Reject(CommandReason.NotAvailable);
        if ((uint)regionIndex >= (uint)content.Regions.Length
            || !IsRegionUnlocked(content.Regions[regionIndex], state.Reservas, state.Migrations))
            return CommandResult.Reject(CommandReason.RegionLocked);
        state.ApplyMigration(ctx, regionIndex, PreviewGain(state, ctx));
        return CommandResult.Ok;
    }
}
