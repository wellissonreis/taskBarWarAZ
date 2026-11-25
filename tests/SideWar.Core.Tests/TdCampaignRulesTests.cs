using System.Reflection;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using FuseTower = SideWar.Core.Td.FuseTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Core.Tests;

/// <summary>BAL-1: escala de campanha, chefe da última onda, teia obrigatória no fim, meia barra de XP e perda de fusões.</summary>
public sealed class TdCampaignRulesTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();

    private static TdContent Rich => Content with { Stages = Content.Stages with { Stages = Content.Stages.Stages.Select(s => s with { StartingCredits = 10_000_000 }).ToArray() } };

    private static TdMatchState Match(int stage = 1, int reputation = 0, TdDifficulty difficulty = TdDifficulty.Casual, UInt128 runes = default, TdContent? content = null)
    {
        var m = TdMatchState.Create(content ?? Rich, stage, Rng.Seed(42, 54), null,
            TdMatchOptions.Default with { InitialReputation = reputation, Difficulty = difficulty, InitialAwakened = runes });
        Assert.True(m.Apply(new LayoutChanged(TdSanityBot.TypicalLayout())).IsOk);
        return m;
    }

    private static void Begin(TdMatchState m, int wave) => typeof(TdMatchState)
        .GetMethod("BeginWave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(m, [wave]);

    private static void Until(TdMatchState m, Func<bool> done, int max = 200_000)
    {
        for (int i = 0; i < max && !done(); i++) m.Step();
        Assert.True(done());
    }

    private static List<TdEnemy> Spawn(TdMatchState m, int wave)
    {
        Begin(m, wave);
        m.Step();
        var list = (List<TdEnemy>)m.Enemies;
        Until(m, () => list.Count > 0);
        return list;
    }

    [Fact]
    public void Hp_scale_grows_every_wave_without_plateau_and_by_difficulty()
    {
        for (int stage = 1; stage <= 6; stage++)
        {
            var m = Match(stage);
            for (int wave = 2; wave <= m.TotalWaves; wave++)
                Assert.True(m.CampaignHpFactor(wave) > m.CampaignHpFactor(wave - 1), $"fase {stage}, onda {wave}");
            var hard = Match(stage, difficulty: TdDifficulty.Hard);
            var extreme = Match(stage, difficulty: TdDifficulty.Extreme);
            Assert.True(hard.CampaignHpFactor(1) > m.CampaignHpFactor(1));
            Assert.True(extreme.CampaignHpFactor(1) > hard.CampaignHpFactor(1));
        }
        var classic = TdMatchState.Create(Content, 6, Rng.Seed(1, 54), null, TdMatchOptions.Classic);
        Assert.Equal(1.0, classic.CampaignHpFactor(classic.TotalWaves));
    }

    [Fact]
    public void Last_wave_regional_boss_is_far_above_the_curve()
    {
        var m = Match();
        Begin(m, m.TotalWaves);
        var bossIds = Content.Enemies.Bosses.Select(b => b.Id).ToArray();
        int regional = Content.Enemies.Enemies.Length + Array.IndexOf(bossIds, "chefe_botnet");
        Until(m, () => m.Enemies.Any(e => e.DefIndex == regional));
        var boss = m.Enemies.First(e => e.DefIndex == regional);
        var def = Content.Enemies.Bosses.Single(b => b.Id == "chefe_botnet");
        var rules = Content.Stages.Rules;
        double curve = def.Hp * (1 + (rules.HpWaveGrowth - 1) * (m.TotalWaves - 1));
        Assert.Equal(curve * Content.Stages.Stages[0].BossHp, boss.MaxHp, 6);
        Assert.True(Content.Stages.Stages.All(s => s.BossHp > 1));
        Assert.Equal(boss.MaxHp * TdMatchState.FinalBossShieldFraction, boss.ShieldHp, 6);
        Assert.True(boss.Armor >= TdMatchState.FinalBossArmorBonus);
    }

    [Theory]
    [InlineData(6, false, true)]
    [InlineData(6, true, false)]
    [InlineData(5, false, false)]
    public void Final_region_boss_only_takes_damage_with_the_complete_web(int stage, bool complete, bool immune)
    {
        var m = Match(stage, TdRank.Threshold(TdRankLevel.Arquiteto), runes: complete ? UInt128.MaxValue : UInt128.MaxValue >> 1);
        Assert.Equal(immune, m.FinalBossImmune);
        Assert.True(m.Apply(new BuildTower("bolt", 1100)).IsOk);
        var enemies = Spawn(m, m.TotalWaves);
        var ids = Content.Enemies.Bosses.Select((b, i) => (b.Id, Index: Content.Enemies.Enemies.Length + i)).Where(b => b.Id.StartsWith("chefe_", StringComparison.Ordinal)).Select(b => b.Index).ToHashSet();
        Until(m, () => enemies.Any(e => ids.Contains(e.DefIndex)));
        var boss = enemies.First(e => ids.Contains(e.DefIndex));
        boss.X = 1100; boss.Speed = 0;
        enemies.Clear(); enemies.Add(boss);
        double before = boss.Hp + boss.ShieldHp;
        for (int i = 0; i < 120; i++) m.Step();
        var after = enemies.First(e => e.Id == boss.Id);
        Assert.Equal(immune, after.Hp + after.ShieldHp >= before);
    }

    [Fact]
    public void Boss_breach_rewinds_pauses_and_only_manual_resume_continues()
    {
        var m = Match();
        Assert.True(m.Apply(new BuildTower("vault", 600)).IsOk || m.Apply(new BuildTower("bolt", 100)).IsOk);
        int towers = m.Towers.Count;
        long credits = m.Credits;
        Begin(m, m.TotalWaves);
        Until(m, () => m.BossDefeats == 1);
        Assert.True(m.Paused);
        Assert.Equal(1, m.BossRewinds);
        Assert.Equal(m.TotalWaves - 10, m.WaveNumber);
        Assert.Equal(towers, m.Towers.Count);
        Assert.True(m.Credits >= credits);
        long frame = m.Frame;
        for (int i = 0; i < 100; i++) m.Step();
        Assert.Equal(frame, m.Frame);
        Assert.True(m.Apply(new BuildTower("bolt", 900)).IsOk); // reconfigurar é permitido na pausa
        Assert.True(m.Apply(new Resume()).IsOk);
        m.Step();
        Assert.Equal(frame + 1, m.Frame);
    }

    [Fact]
    public void Leaks_before_the_final_wave_never_pause()
    {
        var m = Match();
        Begin(m, m.TotalWaves - 1);
        Until(m, () => m.WaveNumber == m.TotalWaves);
        Assert.True(m.ReputationLostTotal > 0);
        Assert.False(m.Paused);
        Assert.Equal(0, m.BossDefeats);
    }

    private static long FirstLeakLoss(int reputation)
    {
        var m = Match(1, reputation);
        Begin(m, 1);
        Until(m, () => m.ReputationLostTotal > 0);
        return m.ReputationLostTotal;
    }

    [Fact]
    public void From_senior_each_core_hit_costs_half_of_the_rank_bar()
    {
        Assert.Equal(TdReputation.XpPerCoreDamage, FirstLeakLoss(TdRank.Threshold(TdRankLevel.Senior) - 1)); // abaixo: regra antiga
        foreach (var rank in new[] { TdRankLevel.Senior, TdRankLevel.Especialista, TdRankLevel.Arquiteto })
            Assert.Equal(TdReputation.RankBar(rank) / 2, FirstLeakLoss(TdRank.Threshold(rank) + 10));
        Assert.Equal(TdReputation.RankBar(TdRankLevel.Especialista), TdReputation.RankBar(TdRankLevel.Arquiteto));
    }

    private static TdMatchState FusedAtSenior()
    {
        var m = Match(1, TdRank.Threshold(TdRankLevel.Senior) + 10);
        Assert.True(m.Apply(new BuildTower("bolt", 100)).IsOk);
        int id = m.Towers[0].Id;
        foreach (var component in new[] { "frost", "cannon", "dns", "functions" })
            Assert.True(m.Apply(new FuseTower(id, component)).IsOk, component);
        Assert.Equal(5, m.Towers[0].ComponentTotal);
        return m;
    }

    private static int Def(string id) => Array.FindIndex(Content.Towers, t => t.Id == id);

    [Fact]
    public void Demotion_destroys_randomly_chosen_fusions_down_to_the_new_limit()
    {
        var m = FusedAtSenior();
        var asked = new List<int>();
        m.DemotionPick = n => { asked.Add(n); return 1; };
        long credits = m.Credits;
        var tower = m.Towers[0];
        // a torre não alcança o spawn: o primeiro vazamento rebaixa
        Begin(m, 1);
        Until(m, () => m.ReputationLostTotal > 0);
        Assert.Equal((int)TdRankLevel.Pleno, m.CurrentRankId);
        Assert.Equal([4], asked);
        Assert.Equal(4, m.Towers[0].ComponentTotal);
        Assert.Equal(tower.DefIndex, m.Towers[0].DefIndex);
        Assert.Equal([Def("cannon")], m.LastFusionLoss);
        Assert.Equal(new[] { Def("bolt"), Def("frost"), Def("dns"), Def("functions") }, m.Towers[0].Components());
        Assert.Equal(1, m.FusionLossCount);
        Assert.Contains(m.Events, e => e.Type == TdEventType.FusionLost && e.B == Def("cannon"));
        Assert.True(m.Credits >= credits); // sem cobrança e sem reembolso
    }

    [Fact]
    public void Default_demotion_pick_is_deterministic_for_the_same_seed()
    {
        TdMatchState Run()
        {
            var m = FusedAtSenior();
            Begin(m, 1);
            Until(m, () => m.FusionLossCount == 1);
            return m;
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a.LastFusionLoss, b.LastFusionLoss);
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        Assert.Equal(a.ComputeStateHash(), a.CloneForSimulation().ComputeStateHash());
    }

    [Theory]
    [InlineData(TdRankLevel.Pleno, 100)]
    [InlineData(TdRankLevel.Senior, TdReputation.SeniorXpGainPercent)]
    [InlineData(TdRankLevel.Especialista, TdReputation.SpecialistXpGainPercent)]
    public void High_ranks_earn_less_xp(TdRankLevel rank, int percent)
    {
        var m = Match(1, TdRank.Threshold(rank));
        typeof(TdMatchState).GetMethod("GainReputation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(m, [1000, 1, true]);
        Assert.Equal(10 * percent, m.ReputationGainedTotal);
    }

    [Fact]
    public void Queues_and_blockers_cannot_trap_the_immune_final_boss()
    {
        var m = Match(6, TdRank.Threshold(TdRankLevel.Arquiteto)); // teia incompleta: chefe imune
        Assert.True(m.FinalBossImmune);
        foreach (var (tower, x) in new[] { ("queue", 1100), ("queue", 960), ("queue", 640), ("queue", 450), ("balancer", 300), ("frontdoor", 200) })
        {
            int at = x;
            while (!m.Apply(new BuildTower(tower, at)).IsOk) at -= 11;
        }
        Begin(m, m.TotalWaves);
        Until(m, () => m.BossDefeats == 1); // a onda termina em derrota em vez de ficar presa
        Assert.True(m.Paused);
    }

    [Fact]
    public void Many_queues_cannot_push_a_common_enemy_back_forever()
    {
        // Filas em toda a faixa contra um inimigo indestrutível no meio da fase: ele precisa chegar à base.
        var content = Content with
        {
            Enemies = Content.Enemies with { Enemies = Content.Enemies.Enemies.Select(e => e.Id == "grunt" ? e with { Hp = 100_000_000 } : e).ToArray() },
            Stages = Content.Stages with { Stages = Content.Stages.Stages.Select(s => s with { StartingCredits = 100_000_000 }).ToArray() },
        };
        var m = Match(6, TdRank.Threshold(TdRankLevel.Arquiteto), content: content);
        foreach (int x in new[] { 1100, 1000, 900, 780, 640, 500, 400, 300, 200, 100 })
        {
            int at = x;
            while (!m.Apply(new BuildTower("queue", at)).IsOk) at -= 11;
        }
        Begin(m, 1);
        Until(m, () => m.ReputationLostTotal > 0);
    }

    private static double FinalBossDamage(string tower)
    {
        var content = Content with
        {
            Towers = Content.Towers.Select(t => t.Id is "bolt" or "cannon" ? t with { Damage = 100, Interval = 1, Range = 400, Stats = new() } : t).ToArray(),
            Stages = Content.Stages with { Stages = Content.Stages.Stages.Select(s => s with { StartingCredits = 100_000 }).ToArray() },
        };
        var m = Match(1, content: content);
        Assert.True(m.Apply(new BuildTower(tower, 960)).IsOk);
        var enemies = Spawn(m, m.TotalWaves);
        int regional = Content.Enemies.Enemies.Length + Array.FindIndex(Content.Enemies.Bosses, b => b.Id == "chefe_botnet");
        Until(m, () => enemies.Any(e => e.DefIndex == regional));
        var boss = enemies.First(e => e.DefIndex == regional);
        boss.X = 960; boss.Speed = 0;
        enemies.Clear(); enemies.Add(boss);
        double before = boss.Hp + boss.ShieldHp;
        for (int i = 0; i < 300; i++) m.Step();
        var after = enemies.First(e => e.Id == boss.Id);
        return before - after.Hp - after.ShieldHp;
    }

    [Fact]
    public void Final_boss_resists_towers_without_advantage_against_it()
    {
        double bolt = FinalBossDamage("bolt");      // vantagem contra "durable": x1,6
        double cannon = FinalBossDamage("cannon");  // sem vantagem: bossOffStrategy (25% nas fases 1 a 3)
        Assert.True(cannon > 0);
        Assert.True(bolt > cannon * 4, $"{bolt} vs {cannon}");
        Assert.All(Content.Stages.Stages.Take(3), s => Assert.Equal(0.25, s.BossOffStrategy));
        Assert.All(Content.Stages.Stages.Skip(3), s => Assert.Equal(1.0, s.BossOffStrategy));
    }
}
