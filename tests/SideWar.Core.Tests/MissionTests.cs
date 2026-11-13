using SideWar.Core.Content;
using SideWar.Core.Missions;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>T6.7 / T-27.10 / CA-27.9: missões diárias, badge, coleta e recompensas (D-18).</summary>
public sealed class MissionTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);
    private static readonly DateOnly D1 = new(2026, 11, 10);

    private static SimState NewState(double income = 10)
    {
        var s = SimState.CreateInitial(Content, TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f }), Rng.Seed(42, 54));
        s.IncomePerSecond = income;
        return s;
    }

    private static MissionService With(params string[] ids)
    {
        var svc = new MissionService(Content, 777);
        svc.Restore(new MissionServiceState("2026-11-10", ids.Select(i => new MissionEntry(i, 0, MissionStatus.InProgress)).ToArray(), 0));
        return svc;
    }

    /// <summary>Coleta no serviço e aplica o comando devolvido pelo host (entre ticks).</summary>
    private static bool Pay(MissionService m, int index, SimState s)
    {
        var g = m.Collect(index);
        return g is not null && s.Apply(Ctx, g).IsOk;
    }

    private static void Obs(MissionService m, SimEventType t, int n = 1)
    {
        for (int i = 0; i < n; i++) m.Observe(new SimEvent(t, 0, 0, 0, 0));
    }

    [Fact]
    public void Draw_IsDeterministic_FourDistinctFromPool()
    {
        var a = MissionService.Draw(Content.Missions, 12345, D1);
        var b = MissionService.Draw(Content.Missions, 12345, D1);
        Assert.Equal(a, b);
        Assert.Equal(4, a.Length);
        Assert.Equal(4, a.Distinct().Count());
        Assert.All(a, id => Assert.Contains(Content.Missions.Missions, d => d.Id == id));
    }

    [Fact]
    public void Draw_VariesByDateAndSeed()
    {
        var days = Enumerable.Range(0, 14).Select(i => string.Join(",", MissionService.Draw(Content.Missions, 1, D1.AddDays(i)))).Distinct().Count();
        Assert.True(days > 3);
        var seeds = Enumerable.Range(0, 14).Select(i => string.Join(",", MissionService.Draw(Content.Missions, (ulong)i, D1))).Distinct().Count();
        Assert.True(seeds > 3);
    }

    [Fact]
    public void Sync_DoesNotConsumeMainRng_AndIsIdempotentWithinDay()
    {
        var s = NewState();
        ulong rng = s.Rng.State, hash = s.ComputeStateHash();
        var m = new MissionService(Content, 9);
        Assert.Equal(0, m.Sync(D1, new List<GrantMissionReward>()));
        Assert.Equal(rng, s.Rng.State);
        Assert.Equal(hash, s.ComputeStateHash());
        Assert.Equal("2026-11-10", m.Date);
        Assert.Equal(4, m.Missions.Count);
        var before = m.Missions.ToArray();
        m.Sync(D1, new List<GrantMissionReward>());
        Assert.Equal(before, m.Missions.ToArray());
    }

    [Fact]
    public void Sync_SameSeedAndDate_SameMissionsOnAnotherInstance()
    {
        var s = NewState();
        var a = new MissionService(Content, 9); a.Sync(D1, new List<GrantMissionReward>());
        var b = new MissionService(Content, 9); b.Sync(D1, new List<GrantMissionReward>());
        Assert.Equal(a.Missions.Select(x => x.Id), b.Missions.Select(x => x.Id));
    }

    [Fact]
    public void Observe_CountsPerSdd_TimeoutAndLeakDoNotCount()
    {
        var m = With("answer-boss", "scale-spikes", "collect-crates", "defeat-enemies");
        Obs(m, SimEventType.BossAnswered, 2);
        Obs(m, SimEventType.BossTimeout, 5);
        Obs(m, SimEventType.Leak, 5);
        Obs(m, SimEventType.PeakScaled, 4);
        Obs(m, SimEventType.PeakIgnored, 3);
        Obs(m, SimEventType.CrateCollected, 9);
        Obs(m, SimEventType.CrateExpired, 3);
        Obs(m, SimEventType.Kill, 199);
        Assert.Equal(new long[] { 2, 4, 9, 199 }, m.Missions.Select(x => x.Progress).ToArray());
        Assert.Equal(0, m.Badge);
        Obs(m, SimEventType.BossAnswered); Obs(m, SimEventType.PeakScaled); Obs(m, SimEventType.CrateCollected); Obs(m, SimEventType.Kill);
        Assert.All(m.Missions, x => Assert.Equal(MissionStatus.Completed, x.Status));
        Assert.Equal(4, m.Badge);
        Obs(m, SimEventType.Kill, 10);   // além da meta: progresso limitado
        Assert.Equal(200, m.Missions[3].Progress);
    }

    [Fact]
    public void ManualUpgrade_Counts_AutoUpgradeDoesNot()
    {
        var s = NewState();
        s.Credits = 100000;
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        var m = With("upgrade-towers");
        long cur = s.Events.Written;
        for (int i = 0; i < 3; i++) Assert.True(s.Apply(Ctx, new UpgradeTower(1)).IsOk);
        m.Consume(s, ref cur);
        Assert.Equal(3, m.Missions[0].Progress);
        s.Towers.At(0).Level += 5;   // melhoria sem Apply (auto-escala): sem evento
        m.Consume(s, ref cur);
        Assert.Equal(3, m.Missions[0].Progress);
    }

    [Fact]
    public void PresentTime_AccumulatesNonContiguously_60Minutes()
    {
        var m = With("stay-present");
        m.AddPresentSeconds(59 * 60 + 59);   // 59 min
        Assert.Equal(59, m.Missions[0].Progress);
        Assert.Equal(MissionStatus.InProgress, m.Missions[0].Status);
        m.AddPresentSeconds(0.5);
        Assert.Equal(59, m.Missions[0].Progress);
        m.AddPresentSeconds(0.5);   // total 3600 s, em pedaços
        Assert.Equal(60, m.Missions[0].Progress);
        Assert.Equal(MissionStatus.Completed, m.Missions[0].Status);
    }

    [Fact]
    public void Collect_OnlyWhenCompleted_PaysOnce()
    {
        var s = NewState();
        var m = With("stay-present");
        Assert.Null(m.Collect(0));
        m.AddPresentSeconds(3600);
        Assert.True(Pay(m, 0, s));
        Assert.Equal(2, s.Reservas);
        Assert.Equal(MissionStatus.Collected, m.Missions[0].Status);
        Assert.Null(m.Collect(0));
        Assert.Equal(2, s.Reservas);
        Assert.Null(m.Collect(5));
        Assert.Equal(0, m.Badge);
    }

    [Fact]
    public void Rewards_IncomeRelative_D18_AndFixedKinds()
    {
        var s = NewState(income: 10);   // 10 créditos/s
        s.Credits = 0;
        var m = With("scale-spikes", "defeat-enemies", "collect-crates", "upgrade-towers", "answer-boss", "stay-present");
        Obs(m, SimEventType.PeakScaled, 5); Obs(m, SimEventType.Kill, 200); Obs(m, SimEventType.CrateCollected, 10);
        Obs(m, SimEventType.TowerUpgraded, 10); Obs(m, SimEventType.BossAnswered, 3); m.AddPresentSeconds(3600);
        Assert.Equal(6, m.Badge);

        Assert.True(Pay(m, 0, s));                    // 10 min de renda = 10 × 60 × 10
        Assert.Equal(6000, s.Credits);
        Assert.True(Pay(m, 1, s));                    // 5 min de renda
        Assert.Equal(9000, s.Credits);
        Assert.True(Pay(m, 2, s));                    // 500 XP
        Assert.Equal(500, s.Xp);
        Assert.True(Pay(m, 3, s));                    // 300 XP
        Assert.Equal(800, s.Xp);
        Assert.True(Pay(m, 4, s));                    // baú raro: 15 min de renda + 1 Reserva
        Assert.Equal(18000, s.Credits);
        Assert.Equal(1, s.Reservas);
        Assert.True(Pay(m, 5, s));                    // 2 Reservas
        Assert.Equal(3, s.Reservas);
        Assert.Equal(0, m.Badge);
    }

    [Fact]
    public void Rewards_IgnorePresenceMultiplier()
    {
        var s = NewState(income: 10);
        s.Credits = 0;
        s.PresenceMultiplier = 2.5;
        var m = With("scale-spikes");
        Obs(m, SimEventType.PeakScaled, 5);
        Pay(m, 0, s);
        Assert.Equal(6000, s.Credits);
    }

    [Fact]
    public void Reward_FromMission_ReservesBoostProductionImmediately()
    {
        var s = NewState();
        var m = With("stay-present");
        m.AddPresentSeconds(3600);
        Pay(m, 0, s);
        Assert.Equal(2, s.Reservas);   // entra no multiplicador (1 + 0,01 × Reservas) na hora (DeathSystem lê Reservas)
    }

    [Fact]
    public void Rollover_AutoCollectsCompleted_ResetsProgressAndDrawsNewDay()
    {
        var s = NewState(income: 10);
        s.Credits = 0;
        var m = With("scale-spikes", "defeat-enemies", "collect-crates", "upgrade-towers");
        Obs(m, SimEventType.PeakScaled, 5);              // concluída, não coletada
        Obs(m, SimEventType.Kill, 50);                   // em andamento
        m.AddPresentSeconds(120);
        var grants = new List<GrantMissionReward>();
        int auto = m.Sync(new DateOnly(2026, 11, 11), grants);   // meia-noite local
        Assert.Equal(1, auto);
        Assert.Equal(0, s.Credits);                              // o serviço não entrega: devolve o comando
        foreach (var g in grants) Assert.True(s.Apply(Ctx, g).IsOk);
        Assert.Equal(1, s.MissionsCollected);
        Assert.Equal(6000, s.Credits);
        Assert.Equal("2026-11-11", m.Date);
        Assert.Equal(4, m.Missions.Count);
        Assert.All(m.Missions, x => { Assert.Equal(0, x.Progress); Assert.Equal(MissionStatus.InProgress, x.Status); });
        Assert.Equal(0, m.PresentSeconds);
        Assert.Equal(MissionService.Draw(Content.Missions, 777, new DateOnly(2026, 11, 11)), m.Missions.Select(x => x.Id));
    }

    [Fact]
    public void Collected_DoesNotPayAgainOnRollover()
    {
        var s = NewState(income: 10);
        var m = With("scale-spikes");
        Obs(m, SimEventType.PeakScaled, 5);
        Pay(m, 0, s);
        long credits = s.Credits;
        var gr = new List<GrantMissionReward>();
        Assert.Equal(0, m.Sync(D1.AddDays(1), gr));
        Assert.Empty(gr);
        Assert.Equal(credits, s.Credits);
    }

    [Fact]
    public void SnapshotRestore_RoundTrip()
    {
        var s = NewState();
        var m = new MissionService(Content, 5);
        m.Sync(D1, new List<GrantMissionReward>());
        Obs(m, SimEventType.Kill, 7); Obs(m, SimEventType.BossAnswered, 3); m.AddPresentSeconds(150);
        Pay(m, 0, s);
        var snap = m.Snapshot();
        var n = new MissionService(Content, 5);
        n.Restore(snap);
        Assert.Equal(snap.Date, n.Date);
        Assert.Equal(m.Missions.ToArray(), n.Missions.ToArray());
        Assert.Equal(m.PresentSeconds, n.PresentSeconds);
        Assert.Equal(m.Badge, n.Badge);
    }

    [Fact]
    public void GrantMissionReward_IsCommand_UpdatesHashedAccumulator_AndReplayReproducesHash()
    {
        var a = NewState(); var b = NewState();
        var cmds = new[]
        {
            new GrantMissionReward("scale-spikes", "incomeMinutes", 10),
            new GrantMissionReward("stay-present", "reserves", 2),
            new GrantMissionReward("defeat-enemies", "xp", 500),
        };
        ulong h0 = a.ComputeStateHash();
        foreach (var c in cmds) Assert.True(a.Apply(Ctx, c).IsOk);
        Assert.Equal(3, a.MissionsCollected);
        Assert.NotEqual(0, a.MissionsCollectedXor);
        Assert.NotEqual(h0, a.ComputeStateHash());
        foreach (var c in cmds) Assert.True(b.Apply(Ctx, c).IsOk);
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        Assert.False(a.Apply(Ctx, new GrantMissionReward("x", "bogus", 1)).IsOk);
        Assert.False(a.Apply(Ctx, new GrantMissionReward("x", "xp", 0)).IsOk);
        Assert.Equal(3, a.MissionsCollected);
    }
}
