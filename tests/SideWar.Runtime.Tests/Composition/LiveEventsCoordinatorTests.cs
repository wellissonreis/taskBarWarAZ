using SideWar.Core.Missions;
using SideWar.Core.Simulation;
using SideWar.Runtime.Composition;
using SideWar.Runtime.LiveEvents;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests.Composition;

/// <summary>T6.11b: eventos ao vivo + missões ligados ao laço (fakes; sem janelas).</summary>
public sealed class LiveEventsCoordinatorTests
{
    private sealed class FakeView : ILiveEventsPresenter
    {
        public readonly List<LiveEventsView> Views = [];
        public readonly List<QuestionCardModel?> Cards = [];
        public void ViewChanged(LiveEventsView view) => Views.Add(view);
        public void BossCardChanged(QuestionCardModel? card) => Cards.Add(card);
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly FakeView View = new();
        public readonly GameRuntime Runtime;
        public readonly MissionService Missions;
        public readonly LiveEventsCoordinator Coord;
        public DateOnly Today = new(2026, 11, 13);
        public int MissionChanges;

        public Rig(int wave = 1)
        {
            Runtime = new GameRuntime(new GameRuntimeOptions { Clock = Clock, UtcNow = () => new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc) });
            Missions = new MissionService(Runtime.Content, 777);
            Runtime.State.Stage = 1;
            Runtime.State.Wave = wave;
            Runtime.State.StartEvents(Runtime.Context);
            Coord = new LiveEventsCoordinator(Runtime, Missions, View, Clock, () => Today, missionsChanged: () => MissionChanges++);
            Runtime.Host.AddHook(Coord);
            Runtime.Dispatch(Runtime.App.LeaveHidden(HiddenReason.Starting)); // visível
            Coord.Start();
        }

        /// <summary>Um wake do laço na ordem do CompositeLoopHost: GameHost, depois o coordenador.</summary>
        public void Wake(double seconds = 1.0 / 30)
        {
            Clock.AdvanceTicks((long)(seconds * Clock.TicksPerSecond));
            Runtime.NotifyInteraction(); // jogador presente durante todo o teste
            Runtime.Host.OnWake(Clock);
            Coord.OnWake(Clock);
        }

        public void Run(double seconds) { for (int i = 0; i < (int)(seconds * 30); i++) Wake(); }

        public void RunUntil(Func<bool> cond, double maxSeconds = 1800)
        {
            for (int i = 0; i < (int)(maxSeconds * 30) && !cond(); i++) Wake();
            Assert.True(cond());
        }
    }

    [Fact(DisplayName = "T6.11b Start sorteia as 4 missões do dia e a virada coleta as concluídas via comando")]
    public void DaySyncAppliesGrantsAsCommands()
    {
        var r = new Rig();
        Assert.Equal(4, r.Missions.Missions.Count);
        Assert.Equal("2026-11-13", r.Missions.Date);
        var first = r.Missions.Missions[0];
        var def = r.Runtime.Content.Missions.Missions.First(m => m.Id == first.Id);
        r.Missions.Restore(new MissionServiceState("2026-11-13", [new MissionEntry(first.Id, def.Target, MissionStatus.Completed)], 0));
        long before = r.Runtime.State.MissionsCollected;
        r.Today = new DateOnly(2026, 11, 14);
        r.Run(31); // passa dos 30 s do teste de virada
        Assert.Equal("2026-11-14", r.Missions.Date);
        Assert.Equal(before + 1, r.Runtime.State.MissionsCollected); // GrantMissionReward aplicado entre ticks
        Assert.Equal(4, r.Missions.Missions.Count);
    }

    [Fact(DisplayName = "T6.11b Consume por tick conta mortes e AddPresentSeconds só em Presente")]
    public void PerTickConsumeAndPresentSeconds()
    {
        var r = new Rig();
        r.Run(2);
        double present = r.Missions.PresentSeconds;
        Assert.True(present > 1.5);
        r.Runtime.State.Events.Add(new SimEvent(SimEventType.CrateCollected, 0, 0, 0, 0));
        r.Coord.AfterStep(r.Runtime.State, default);
        // sem a missão no sorteio nada muda; o cursor avançou (não conta duas vezes)
        r.Runtime.State.Present = false;
        double before = r.Missions.PresentSeconds;
        r.Coord.AfterStep(r.Runtime.State, default);
        Assert.Equal(before, r.Missions.PresentSeconds);
    }

    [Fact(DisplayName = "T6.11b banner de pico: aparece uma vez, clique posta ClickPeakBanner ao host, vira DEFESA ESCALADA não clicável")]
    public void PeakBannerFlow()
    {
        var r = new Rig();
        r.RunUntil(() => r.Coord.Current.Banner is { Kind: LiveBannerKind.Peak });
        Assert.NotEmpty(r.View.Views);
        r.Coord.BannerClicked();
        r.Wake();
        r.Wake();
        Assert.Equal(LiveBannerKind.PeakScaled, r.Coord.Current.Banner!.Kind);
        Assert.False(r.Coord.Current.Banner.Clickable);
        int n = r.View.Views.Count;
        r.Coord.BannerClicked(); // não clicável: nenhum comando; a visão não muda por isso
        r.Wake();
        Assert.Equal(PresenceState.Presente, r.Runtime.Presence.State);
        Assert.True(r.View.Views.Count - n <= 1); // só o chip (1 Hz) pode mudar
    }

    [Fact(DisplayName = "T6.11b a visão só é notificada quando muda (chip a 1 Hz, nunca por wake)")]
    public void ViewNotifiedOnlyOnChange()
    {
        var r = new Rig();
        r.RunUntil(() => r.Coord.Current.Banner is not null);
        int n = r.View.Views.Count;
        for (int i = 0; i < 29; i++) r.Wake(); // < 1 s
        Assert.True(r.View.Views.Count - n <= 1);
    }

    [Fact(DisplayName = "T6.11b oculto: sem deadline e sem refresh")]
    public void HiddenHasNoDeadline()
    {
        var r = new Rig();
        r.Runtime.Dispatch(r.Runtime.App.EnterHidden(HiddenReason.User));
        Assert.Null(r.Coord.NextDeadline);
        int n = r.View.Views.Count;
        r.Coord.OnWake(r.Clock);
        Assert.Equal(n, r.View.Views.Count);
    }

    [Fact(DisplayName = "T6.11b clique no banner do chefe abre o card (Core) e o card some ao fechar sem cancelar o tempo")]
    public void BossBannerOpensCard()
    {
        var r = new Rig(wave: new Rig().Runtime.Content.Waves.BossEvery);
        r.RunUntil(() => r.Coord.Current.Banner is { Kind: LiveBannerKind.Boss }, 900);
        r.Coord.BannerClicked();
        r.Wake();
        Assert.NotNull(r.Coord.Boss.Card);
        Assert.NotEmpty(r.View.Cards);
        r.Coord.BossClosed();
        r.Wake();
        Assert.Null(r.Coord.Boss.Card);
        Assert.True(r.Runtime.State.Encounter!.IsActive); // fechar só esconde (RF-26.10)
    }

    [Fact(DisplayName = "T6.11b badge das missões notifica o widget quando muda")]
    public void BadgeChangeNotifies()
    {
        var r = new Rig();
        r.Wake();
        int n = r.MissionChanges;
        var first = r.Missions.Missions[0];
        var def = r.Runtime.Content.Missions.Missions.First(m => m.Id == first.Id);
        r.Missions.Restore(new MissionServiceState("2026-11-13", [new MissionEntry(first.Id, def.Target, MissionStatus.Completed)], 0));
        r.Coord.MarkDirty();
        r.Wake();
        Assert.Equal(1, r.Coord.MissionBadge);
        Assert.Equal(n + 1, r.MissionChanges);
    }
}
