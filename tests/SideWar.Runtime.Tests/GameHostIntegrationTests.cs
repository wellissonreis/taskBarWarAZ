using SideWar.Core;
using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Runtime.Hosting;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests;

/// <summary>TU.1: GameHost integrado a eventos (T6.6B), modo Ausente (T6.2) e regras compartilhadas com o offline (T6.3).</summary>
public sealed class GameHostIntegrationTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly AppStateMachine App = new();
        public readonly PresenceTracker Presence;
        public readonly SimState State;
        public readonly GameHost Host;

        public Rig(params TowerType[] towers)
        {
            Presence = new PresenceTracker(Clock);
            State = SimState.CreateInitial(Content,
                TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f }), Rng.Seed(42, 54));
            State.Credits = 1_000_000;
            for (int i = 0; i < towers.Length; i++) Assert.True(State.Apply(Ctx, new BuildTower(towers[i], TestLayouts.X(i))).IsOk);
            Host = new GameHost(Clock, App, Presence, State, Ctx);
            App.LeaveHidden(HiddenReason.Starting);
        }

        public void Run(double seconds, double step = 0.2)
        {
            long n = (long)Math.Round(seconds / step);
            long inc = (long)(step * Clock.TicksPerSecond);
            for (long i = 0; i < n; i++) { Clock.AdvanceTicks(inc); Host.OnWake(Clock); }
        }
    }

    [Fact(DisplayName = "TU.1 StartEvents só no início da presença; Present segue o estado; Ausente congela sem reiniciar")]
    public void Events_Start_With_Presence()
    {
        var r = new Rig();
        r.Host.OnWake(r.Clock);
        r.Run(1);
        Assert.False(r.State.EventsEnabled);   // nasce Ausente (sem interação)
        Assert.False(r.State.Present);
        r.Host.InteractionObserved();
        r.Run(1);
        Assert.True(r.State.EventsEnabled);
        Assert.True(r.State.Present);
        var sched = r.State.Scheduler;
        r.Run(310);   // 300 s sem interação → Ausente
        Assert.Equal(PresenceState.Ausente, r.Presence.State);
        Assert.False(r.State.Present);
        Assert.Same(sched, r.State.Scheduler);
    }

    [Fact(DisplayName = "TU.1 RunState != InStage: sem tick e comandos rejeitados (InvalidPhase); InStage volta ao normal")]
    public void RunState_Gates_Ticks_And_Commands()
    {
        var r = new Rig();
        r.Host.OnWake(r.Clock);
        r.Host.RunState = RunState.NoRun;
        long t = r.State.Tick;
        r.Host.OnCommand(new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        r.Run(2);
        Assert.Equal(t, r.State.Tick);
        Assert.True(r.Host.TryDequeueResult(out _, out var res));
        Assert.Equal(CommandReason.InvalidPhase, res.Reason);
        Assert.Equal(0, r.State.Towers.Count);
        r.Host.RunState = RunState.InStage;
        r.Host.OnWake(r.Clock);
        r.Run(1);
        Assert.True(r.State.Tick > t);
        r.Host.OnCommand(new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        r.Run(0.4);
        Assert.True(r.Host.TryDequeueResult(out _, out res));
        Assert.True(res.IsOk);
    }

    [Fact(DisplayName = "TU.1 IncomePerSecond vem do RateMeter alimentado pelas mortes")]
    public void Income_Comes_From_RateMeter()
    {
        var r = new Rig(TowerType.Bolt, TowerType.Bolt, TowerType.Bolt);
        r.Host.OnWake(r.Clock);
        r.Host.InteractionObserved();
        double max = 0;
        for (int i = 0; i < 120; i++)
        {
            r.Run(1);
            max = Math.Max(max, r.Host.Rate.IncomeRate);
            if (max > 0) break;
        }
        Assert.True(max > 0);
        r.Run(1);
        Assert.Equal(r.Host.Rate.IncomeRate, r.State.IncomePerSecond, 9);
        Assert.True(r.Host.Rate.DisplayedRate >= r.Host.Rate.IncomeRate);   // Presente ×1,5 ou mais
    }

    [Fact(DisplayName = "TU.1 Ausente: auto-escala a cada 900 s; Presente zera o contador e não gasta")]
    public void AutoScale_Every_900s_In_Absent()
    {
        var r = new Rig(TowerType.Bolt);
        r.Host.OnWake(r.Clock);
        r.Run(890, 0.25);
        Assert.Equal(0, r.Host.AutoScaleUpgradeCount);
        Assert.InRange(r.Host.AutoScaleAccumSeconds, 880, 900);
        long before = r.State.Credits;
        r.Run(25, 0.25);
        Assert.True(r.Host.AutoScaleUpgradeCount > 0);
        Assert.True(r.Host.AutoScaleSpent > 0);
        Assert.Equal(0, r.State.ManualUpgrades);
        Assert.True(r.State.Towers.Get(TestLayouts.TowerAt(r.State, 0)).Level > 1);
        Assert.True(r.State.Credits < before + 100_000);
        r.Host.InteractionObserved();
        r.Run(1);
        Assert.Equal(0, r.Host.AutoScaleAccumSeconds);
    }

    [Fact(DisplayName = "TU.1 toggle desligado: auto-escala não gasta")]
    public void AutoScale_Toggle_Off()
    {
        var r = new Rig(TowerType.Bolt) { };
        r.Host.AutoScaleEnabled = false;
        r.Host.OnWake(r.Clock);
        r.Run(920, 0.25);
        Assert.Equal(0, r.Host.AutoScaleUpgradeCount);
    }

    [Fact(DisplayName = "TU.1 Ausente com chefe em espera: host inicia o farm de w−1 (WaveToPlay)")]
    public void Host_Starts_Farm_For_Held_Boss()
    {
        var r = new Rig();
        r.State.Stage = 1;
        r.State.Wave = Content.Waves.BossEvery;
        r.State.StartEvents(Ctx);
        r.Host.OnWake(r.Clock);
        r.Run(1);
        Assert.True(r.State.BossHold);
        Assert.Equal(Content.Waves.BossEvery - 1, r.State.FarmWave);
        Assert.Equal(Content.Waves.BossEvery, r.State.Wave);
    }

    [Fact(DisplayName = "TU.1 offline e Ausente usam a mesma auto-escala (AutoScaler.Plan): mesmo gasto e mesmos níveis")]
    public void Offline_And_Sim_Share_AutoScaler()
    {
        var layout = TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f });
        var s = SimState.CreateInitial(Content, layout, Rng.Seed(42, 54));
        s.Credits = 1_000_000;
        var types = new[] { TowerType.Bolt, TowerType.Bolt, TowerType.Cannon };
        for (int i = 0; i < types.Length; i++) Assert.True(s.Apply(Ctx, new BuildTower(types[i], TestLayouts.X(i))).IsOk);
        long credits = 5000;
        s.Credits = credits;
        var towers = new List<OfflineTower>();
        for (int i = 0; i < types.Length; i++)
        {
            var t = s.Towers.Get(TestLayouts.TowerAt(s, i));
            towers.Add(new OfflineTower(t.Id, t.Type, i, t.Level));
        }

        var simResult = AutoScaler.Run(s, Ctx, true, PresenceState.Ausente);

        // Onda de chefe do fim da fase 1 (sem ganho analítico: chefes nunca são jogados) → só a rodada de auto-escala.
        var snap = new OfflineSnapshot(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, Content.Waves.BossEvery, Content.Waves.BossEvery,
            Content.Regions[0].Id, 0, 1, 0, credits, 0, towers, true, 0);
        var off = new OfflineCalculator(Content).Compute(snap, snap.SavedUtc.AddSeconds(AutoScaler.IntervalSeconds));

        Assert.Equal(simResult.Spent, off.Report.GastoAuto);
        Assert.Equal(simResult.Upgrades.Count, off.Report.NMelhorias);
        Assert.Equal(credits - simResult.Spent, off.State.Credits);
        for (int i = 0; i < types.Length; i++)
            Assert.Equal(s.Towers.Get(TestLayouts.TowerAt(s, i)).Level, off.State.Towers[i].Level);
    }
}
