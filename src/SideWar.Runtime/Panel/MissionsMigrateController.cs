using SideWar.Core.Missions;
using SideWar.Core.Prestige;
using SideWar.Core.Simulation;

namespace SideWar.Runtime.Panel;

/// <summary>Resultado de um clique no corpo das abas Missões/Migrar.</summary>
public enum PanelBodyAction { None, MissionCollected, RegionChosen, MigrateArmed, Migrated, MigrateRejected }

/// <summary>
/// Cola das abas Missões e Migrar com os serviços reais (SDD-24 RF-24.7/24.8; T6.7). Cliques viram ação entre ticks:
/// COLETAR → <see cref="MissionService.Collect"/>; região → escolha da próxima migração; MIGRAR → segundo clique em 5 s
/// (CA-24.8) emite <see cref="MigrateTo"/> (via <c>Apply</c>, no CommandLog). Puro; só entre ticks da T-Game.
/// </summary>
public sealed class MissionsMigrateController(MissionService missions, MigrateArming arming)
{
    private int? _chosen;

    public MigrateArming Arming => arming;

    /// <summary>Região escolhida para a próxima migração; null = a atual.</summary>
    public int? ChosenRegion => _chosen;

    public PanelBodyAction Handle(TabBodyHit hit, SimState state, SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        if (hit.Kind != TabBodyHitKind.Migrate) arming.Cancel();   // qualquer outro clique desarma (RF-24.8)
        switch (hit.Kind)
        {
            case TabBodyHitKind.MissionCollect:
                return missions.Collect(hit.Index, state, ctx) ? PanelBodyAction.MissionCollected : PanelBodyAction.None;
            case TabBodyHitKind.RegionRow:
                var regions = ctx.Content.Regions;
                if ((uint)hit.Index >= (uint)regions.Length
                    || !PrestigeService.IsRegionUnlocked(regions[hit.Index], state.Reservas, state.Migrations)) return PanelBodyAction.None;
                _chosen = hit.Index;
                return PanelBodyAction.RegionChosen;
            case TabBodyHitKind.Migrate:
                var click = arming.Click(PrestigeService.IsAvailable(ctx.Content.Economy, state.StageWaveMax));
                if (click == MigrateClick.Armed) return PanelBodyAction.MigrateArmed;
                if (click != MigrateClick.Confirmed) return PanelBodyAction.None;
                var r = state.Apply(ctx, new MigrateTo(_chosen ?? state.RegionIndex));
                if (!r.IsOk) return PanelBodyAction.MigrateRejected;
                _chosen = null;
                return PanelBodyAction.Migrated;
            default:
                return PanelBodyAction.None;
        }
    }

    /// <summary>Troca de aba/fechar o painel: desarma o MIGRAR.</summary>
    public bool Cancel() => arming.Cancel();
}
