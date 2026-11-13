using SideWar.Core;
using SideWar.Core.Content;
using SideWar.Core.Missions;
using SideWar.Core.Simulation;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests.Panel;

/// <summary>T6.11: abas Missões e Migrar ligadas a MissionService/PrestigeService reais (CA-24.7, CA-24.8).</summary>
public sealed class MissionsMigrateBindingTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static SimState NewState() =>
        SimState.CreateInitial(Content, TestLayouts.Default(), Rng.Seed(42, 54));

    private static MissionService Svc(params (string Id, long P, MissionStatus S)[] items)
    {
        var svc = new MissionService(Content, 777);
        svc.Restore(new MissionServiceState("2026-11-10", items.Select(i => new MissionEntry(i.Id, i.P, i.S)).ToArray(), 0));
        return svc;
    }

    [Fact(DisplayName = "CA-24.7 modelo de Missões: progresso, recompensa D-18, concluída/coletada e presença")]
    public void Missions_Model()
    {
        var svc = Svc(("answer-boss", 3, MissionStatus.Completed), ("scale-spikes", 2, MissionStatus.InProgress),
            ("stay-present", 42, MissionStatus.InProgress), ("collect-crates", 10, MissionStatus.Collected));
        var m = MissionsMigrateModelBuilder.BuildMissions(svc, Content.Missions, PresenceState.Presente, 42 * 60);
        Assert.Equal("×2,0", m.Multiplier);
        Assert.False(m.MultiplierMuted);
        Assert.Contains("42 min", m.PresenceText);
        Assert.True(m.Bands[1].Current);
        Assert.Equal("3/3 · +1 baú raro", m.Missions[0].ProgressText);
        Assert.True(m.Missions[0].CanCollect);
        Assert.Equal("2/5 · +10 min de renda", m.Missions[1].ProgressText);
        Assert.Equal(0.4f, m.Missions[1].Progress, 3);
        Assert.Equal("42/60 min · +2 Reservas", m.Missions[2].ProgressText);
        Assert.Equal("COLETADA", m.Missions[3].ProgressText);
        Assert.False(m.Missions[3].CanCollect);
        Assert.Equal(1f, m.Missions[3].Progress);
    }

    [Fact(DisplayName = "DP-24.8 Ausente: nenhuma faixa, ×0,25 esmaecido")]
    public void Missions_Absent()
    {
        var m = MissionsMigrateModelBuilder.BuildMissions(Svc(("answer-boss", 0, MissionStatus.InProgress)), Content.Missions, PresenceState.Ausente, 1000);
        Assert.Equal("×0,25", m.Multiplier);
        Assert.True(m.MultiplierMuted);
        Assert.All(m.Bands, b => Assert.False(b.Current));
    }

    [Fact(DisplayName = "COLETAR entrega a recompensa real e marca Coletada; sem duplicar")]
    public void Collect_Delivers_Once()
    {
        var s = NewState();
        var svc = Svc(("stay-present", 60, MissionStatus.Completed));
        var ctl = new MissionsMigrateController(svc, new MigrateArming(new FakeClock()));
        Assert.Equal(PanelBodyAction.MissionCollected, ctl.Handle(new TabBodyHit(TabBodyHitKind.MissionCollect, 0), s, Ctx));
        Assert.Equal(2, s.Reservas);
        Assert.Equal(MissionStatus.Collected, svc.Missions[0].Status);
        Assert.Equal(PanelBodyAction.None, ctl.Handle(new TabBodyHit(TabBodyHitKind.MissionCollect, 0), s, Ctx));
        Assert.Equal(2, s.Reservas);
    }

    [Fact(DisplayName = "CA-24.8 modelo de Migrar: ganho, regiões bloqueadas/atual, indisponível < onda 15")]
    public void Migrate_Model()
    {
        var s = NewState();
        s.StageWaveMax = 14;
        var m = MissionsMigrateModelBuilder.BuildMigrate(s, Ctx, null, false);
        Assert.Equal(MigrateButtonState.Unavailable, m.Button);
        Assert.Equal("Brazil South · atual", m.Regions[0].Text);
        Assert.False(m.Regions[1].Selectable);
        Assert.Contains("1ª migração", m.Regions[1].Text);
        Assert.Contains("30 Reservas", m.Regions[2].Text);

        s.StageWaveMax = 40;
        s.Reservas = 30;
        s.GrantReward(0, 0, 0);
        var m2 = MissionsMigrateModelBuilder.BuildMigrate(s, Ctx, 2, false);
        Assert.Equal("+9", m2.GainNow);   // floor(40²/170) = 9
        Assert.Equal("30", m2.CurrentReserves);
        Assert.True(m2.Regions[2].Selectable);
        Assert.StartsWith("→ ", m2.Regions[2].Text);
        Assert.Equal(RegionTone.Principal, m2.Regions[2].Tone);
        Assert.Equal(MigrateButtonState.Normal, m2.Button);
        Assert.Equal(MigrateButtonState.Armed, MissionsMigrateModelBuilder.BuildMigrate(s, Ctx, 2, true).Button);
    }

    [Fact(DisplayName = "CA-24.8 MIGRAR: 1º clique arma sem migrar; 2º em ≤ 5 s emite MigrateTo; 5 s expira; outro clique desarma")]
    public void Migrate_Arming_Flow()
    {
        var s = NewState();
        s.StageWaveMax = 20;
        s.Reservas = 30;
        var clock = new FakeClock();
        var ctl = new MissionsMigrateController(Svc(), new MigrateArming(clock));
        var migrate = new TabBodyHit(TabBodyHitKind.Migrate);

        Assert.Equal(PanelBodyAction.RegionChosen, ctl.Handle(new TabBodyHit(TabBodyHitKind.RegionRow, 2), s, Ctx));
        Assert.Equal(PanelBodyAction.MigrateArmed, ctl.Handle(migrate, s, Ctx));
        Assert.Equal(0, s.Migrations);
        Assert.True(ctl.Arming.Armed);
        clock.AdvanceTicks(6 * 30_000_000L);   // expira
        Assert.Equal(PanelBodyAction.MigrateArmed, ctl.Handle(migrate, s, Ctx));
        Assert.Equal(0, s.Migrations);
        Assert.Equal(PanelBodyAction.None, ctl.Handle(new TabBodyHit(TabBodyHitKind.None), s, Ctx));   // outro clique desarma
        Assert.False(ctl.Arming.Armed);

        ctl.Handle(migrate, s, Ctx);
        clock.AdvanceTicks(2 * 30_000_000L);
        Assert.Equal(PanelBodyAction.Migrated, ctl.Handle(migrate, s, Ctx));
        Assert.Equal(1, s.Migrations);
        Assert.Equal(2, s.RegionIndex);
        Assert.Equal(30 + 2, s.Reservas);   // floor(400/170)=2 (região atual: ×1,0)
        Assert.Null(ctl.ChosenRegion);
    }

    [Fact(DisplayName = "MIGRAR indisponível (< onda 15) é ignorado; região bloqueada não é escolhida")]
    public void Migrate_Unavailable_And_Locked()
    {
        var s = NewState();
        s.StageWaveMax = 10;
        var ctl = new MissionsMigrateController(Svc(), new MigrateArming(new FakeClock()));
        Assert.Equal(PanelBodyAction.None, ctl.Handle(new TabBodyHit(TabBodyHitKind.Migrate), s, Ctx));
        Assert.False(ctl.Arming.Armed);
        Assert.Equal(PanelBodyAction.None, ctl.Handle(new TabBodyHit(TabBodyHitKind.RegionRow, 2), s, Ctx));
        Assert.Null(ctl.ChosenRegion);
    }

    [Fact(DisplayName = "Missões contam pelos SimEvents reais (Consume) e o badge reflete as concluídas")]
    public void Missions_Count_From_Events()
    {
        var s = NewState();
        var svc = Svc(("scale-spikes", 4, MissionStatus.InProgress));
        s.Events.Add(new SimEvent(SimEventType.PeakScaled, 0, 0, 0, 0));
        long cursor = 0;
        svc.Consume(s, ref cursor);
        Assert.Equal(MissionStatus.Completed, svc.Missions[0].Status);
        Assert.Equal(1, svc.Badge);
        Assert.True(MissionsMigrateModelBuilder.BuildMissions(svc, Content.Missions, PresenceState.Presente, 0).Missions[0].CanCollect);
    }
}
