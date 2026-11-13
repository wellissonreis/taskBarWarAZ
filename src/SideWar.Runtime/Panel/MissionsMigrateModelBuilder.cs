using SideWar.Core.Content;
using SideWar.Core.Missions;
using SideWar.Core.Prestige;
using SideWar.Core.Simulation;
using SideWar.Runtime.Localization;
using SideWar.Runtime.Presence;

namespace SideWar.Runtime.Panel;

/// <summary>
/// Constrói os modelos das abas Missões e Migrar a partir do estado real (SDD-24 RF-24.7/24.8; T6.7 serviços).
/// Leitura pura: não altera o estado nem aplica comandos. Roda na T-Game por mudança de dados, não por frame.
/// </summary>
public static class MissionsMigrateModelBuilder
{
    public static MissionsTabModel BuildMissions(MissionService missions, MissionsDef defs, PresenceState presence, double presentSeconds,
        PresenceMultiplierConfig? config = null, long version = 0)
    {
        ArgumentNullException.ThrowIfNull(missions);
        config ??= PresenceMultiplierConfig.Default;
        ArgumentNullException.ThrowIfNull(defs);
        var rows = new MissionRowView[missions.Missions.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            var m = missions.Missions[i];
            var d = Find(defs, m.Id);
            long target = Math.Max(1, d.Target);
            bool collected = m.Status == MissionStatus.Collected;
            string progress = collected
                ? Strings.MissionCollected
                : Strings.MissionProgress(m.Progress, target, d.Id == "stay-present") + " · " + Strings.MissionReward(d.Reward.Kind, d.Reward.Amount);
            rows[i] = new MissionRowView(m.Id, d.Title, collected ? 1f : (float)Math.Min(1.0, (double)m.Progress / target),
                progress, m.Status != MissionStatus.InProgress, collected);
        }

        bool present = presence == PresenceState.Presente;
        double mult = PresenceMultiplier.Evaluate(presence, presentSeconds, config);
        int band = !present ? -1 : presentSeconds >= config.Tier3StartSeconds ? 2 : presentSeconds >= config.Tier2StartSeconds ? 1 : 0;
        string text = present ? Strings.PresenceText((int)(presentSeconds / 60.0)) : Strings.PresenceAbsent;
        return new MissionsTabModel(MissionsTabModel.BonusTitle, MissionsTabModel.FormatMultiplier(mult), !present, text,
            MissionsTabModel.DefaultBands(band), MissionsTabModel.SectionText, rows, version);
    }

    private static MissionDef Find(MissionsDef defs, string id)
    {
        foreach (var d in defs.Missions) if (d.Id == id) return d;
        throw new InvalidOperationException($"Missão desconhecida: {id}");
    }

    /// <summary>
    /// Aba Migrar (RF-24.8). <paramref name="chosenRegion"/>: região da próxima migração (índice; null = nenhuma marcada).
    /// O ganho exibido usa a região da partida que termina (<see cref="PrestigeService.PreviewGain"/>, DP-27.12).
    /// </summary>
    public static MigrateTabModel BuildMigrate(SimState state, SimContext ctx, int? chosenRegion, bool armed, long version = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        var regions = ctx.Content.Regions;
        var rows = new RegionRowView[regions.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            var r = regions[i];
            bool unlocked = PrestigeService.IsRegionUnlocked(r, state.Reservas, state.Migrations);
            if (i == state.RegionIndex)
                rows[i] = MigrateTabModel.Row(i, Strings.RegionCurrent(r.Name), true, RegionTone.Secundario, chosenRegion);
            else if (unlocked)
                rows[i] = MigrateTabModel.Row(i, Strings.RegionOpen(r.Name, r.HpMultiplier, r.ReservesMultiplier), true, RegionTone.Rotulo, chosenRegion);
            else
                rows[i] = MigrateTabModel.Row(i, Strings.RegionLocked(r.Name, r.Unlock, r.UnlockReserves), false, RegionTone.Rotulo, chosenRegion);
        }
        long gain = PrestigeService.PreviewGain(state, ctx);
        return MigrateTabModel.Create(state.Reservas, gain, rows, state.StageWaveMax, armed) with { Version = version };
    }
}
