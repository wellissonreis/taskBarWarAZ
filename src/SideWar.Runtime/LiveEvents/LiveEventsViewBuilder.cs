using SideWar.Core;
using SideWar.Core.Events;
using SideWar.Core.Simulation;
using SideWar.Runtime.Localization;

namespace SideWar.Runtime.LiveEvents;

/// <summary>
/// Monta a <see cref="LiveEventsView"/> a partir do <see cref="EventScheduler"/> e do encontro de chefe do Core
/// (SDD-26 RF-26.2/26.3/26.11/26.12) e traduz cliques em comandos. Leitura pura; roda entre ticks na T-Game.
/// </summary>
public static class LiveEventsViewBuilder
{
    public static LiveEventsView Build(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sched = state.Scheduler;
        if (sched is null || !state.Present) return LiveEventsView.Empty;

        LiveBannerView? banner = null;
        LiveChipView? chip = null;
        bool boss = state.Encounter is { IsActive: true };
        if (boss)
        {
            banner = new LiveBannerView(LiveBannerKind.Boss, Strings.BannerBoss, Ceil(state.Encounter!.Remaining), true);
            if (sched.BonusActive) chip = new LiveChipView("×3 " + Format(Ceil(sched.PeakRemaining)), Ceil(sched.PeakRemaining));
        }
        else if (sched.PeakOffered)
            banner = new LiveBannerView(LiveBannerKind.Peak, Strings.BannerPeak, Ceil(sched.PeakRemaining), true);
        else if (sched.BonusActive)
            banner = new LiveBannerView(LiveBannerKind.PeakScaled, Strings.BannerScaled, Ceil(sched.PeakRemaining), false);

        var crate = sched.CrateActive ? new LiveCrateView(sched.CrateX, Ceil(sched.CrateRemaining)) : null;
        return banner is null && crate is null && chip is null ? LiveEventsView.Empty : new LiveEventsView(banner, crate, chip);
    }

    /// <summary>Comando do clique (RF-26.15); null se o alvo não é clicável na visão atual (o Core ainda valida).</summary>
    public static IGameCommand? CommandFor(LiveEventsView view, LiveClickTarget target)
    {
        ArgumentNullException.ThrowIfNull(view);
        return target switch
        {
            LiveClickTarget.Crate => view.Crate is null ? null : new ClickSupplyCrate(),
            _ => view.Banner switch
            {
                { Clickable: true, Kind: LiveBannerKind.Peak } => new ClickPeakBanner(),
                { Clickable: true, Kind: LiveBannerKind.Boss } => new OpenBossQuestion(),
                _ => null,
            },
        };
    }

    private static int Ceil(double seconds) => (int)Math.Ceiling(Math.Max(0, seconds));
    private static string Format(int s) => $"{s / 60}:{s % 60:00}";
}
