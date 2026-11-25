using System.Reflection;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class TdBossRewindTests
{
    private static TdMatchState Match(int stage = 1) => TdMatchState.Create(TdContentLoader.LoadEmbedded(), stage, Rng.Seed(42, 54), null,
        TdMatchOptions.Default with { ContinuousWaves = false, InitialReputation = 300000 });

    private static void Begin(TdMatchState match, int wave) => typeof(TdMatchState)
        .GetMethod("BeginWave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(match, [wave]);

    private static void Finish(TdMatchState match)
    {
        for (int i = 0; i < 100_000 && match.Phase == MatchPhase.Onda; i++) match.Step();
        Assert.NotEqual(MatchPhase.Onda, match.Phase);
    }

    [Fact]
    public void Final_wave_damage_rewinds_ten_without_losing_credits_and_keeps_xp_rules()
    {
        var match = Match();
        Assert.True(match.Apply(new SideWar.Core.Td.LayoutChanged(TdSanityBot.TypicalLayout())).IsOk);
        Assert.True(match.Apply(new SideWar.Core.Td.BuildTower("bolt", 600)).IsOk);
        var tower = Assert.Single(match.Towers);
        var towers = (List<TdTower>)typeof(TdMatchState).GetField("_towers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(match)!;
        var disabled = towers[0];
        disabled.DisabledTicks = 100_000;
        towers[0] = disabled;
        long credits = match.Credits;
        int xp = match.GateReputation;
        Begin(match, match.TotalWaves);
        Finish(match);
        Assert.Equal(match.TotalWaves - 11, match.WaveNumber); // próxima onda: final - 10
        Assert.Equal(1, match.BossRewinds);
        Assert.True(match.Credits >= credits);
        Assert.Equal(tower.Id, Assert.Single(match.Towers).Id);
        Assert.True(match.GateReputation < xp);
        Assert.Contains(match.Events, e => e.Type == TdEventType.BossRewind && e.A == match.TotalWaves - 10);
    }

    [Fact]
    public void Damage_before_final_wave_does_not_rewind()
    {
        var match = Match();
        Begin(match, match.TotalWaves - 1);
        Finish(match);
        Assert.Equal(match.TotalWaves - 1, match.WaveNumber);
        Assert.Equal(0, match.BossRewinds);
    }

    [Fact]
    public void Final_wave_without_boss_does_not_rewind_on_damage()
    {
        var content = TdContentLoader.LoadEmbedded();
        var stages = content.Waves.Stages.ToArray();
        var waves = stages[0].Waves.ToArray();
        var bossIds = content.Enemies.Bosses.Select(b => b.Id).ToHashSet();
        waves[^1] = waves[^1] with { Groups = waves[^1].Groups.Where(g => !bossIds.Contains(g.Enemy)).ToArray() };
        stages[0] = stages[0] with { Waves = waves };
        content = content with { Waves = content.Waves with { Stages = stages } };
        var match = TdMatchState.Create(content, 1, Rng.Seed(42, 54), null,
            TdMatchOptions.Default with { ContinuousWaves = false, PersistentRun = false, InitialReputation = 300000 });
        Begin(match, match.TotalWaves);
        Finish(match);
        Assert.True(match.ReputationLostTotal > 0);
        Assert.Equal(0, match.BossRewinds);
        Assert.Equal(MatchPhase.Vitoria, match.Phase);
    }

    [Fact]
    public void Final_boss_wave_without_damage_completes_without_rewind()
    {
        var content = TdContentLoader.LoadEmbedded();
        var towers = content.Towers.Select(t => t.Id == "bolt" ? t with { Damage = 1_000_000, Range = 2000, Interval = 0.1 } : t).ToArray();
        content = content with { Towers = towers };
        var match = TdMatchState.Create(content, 1, Rng.Seed(42, 54), null,
            TdMatchOptions.Default with { ContinuousWaves = false, PersistentRun = false, InitialReputation = 300000 });
        Assert.True(match.Apply(new SideWar.Core.Td.LayoutChanged(TdSanityBot.TypicalLayout())).IsOk);
        Assert.True(match.Apply(new SideWar.Core.Td.BuildTower("bolt", 600)).IsOk);
        Begin(match, match.TotalWaves);
        Finish(match);
        Assert.Equal(0, match.ReputationLostTotal);
        Assert.Equal(0, match.BossRewinds);
        Assert.Equal(MatchPhase.Vitoria, match.Phase);
    }

    [Fact]
    public void Rewind_never_goes_before_wave_one()
    {
        var content = TdContentLoader.LoadEmbedded();
        var stages = content.Waves.Stages.ToArray();
        stages[0] = stages[0] with { Waves = [stages[0].Waves[^1] with { Number = 1 }] };
        content = content with { Waves = content.Waves with { Stages = stages } };
        var match = TdMatchState.Create(content, 1, Rng.Seed(42, 54), null,
            TdMatchOptions.Default with { ContinuousWaves = false, PersistentRun = false, InitialReputation = 300000 });
        Begin(match, 1);
        Finish(match);
        Assert.Equal(0, match.WaveNumber);
        Assert.Contains(match.Events, e => e.Type == TdEventType.BossRewind && e.A == 1);
    }

    [Fact]
    public void Every_stage_ends_with_a_regional_boss()
    {
        var content = TdContentLoader.LoadEmbedded();
        var bosses = content.Enemies.Bosses.Select(b => b.Id).ToHashSet();
        foreach (var stage in content.Waves.Stages)
            Assert.Contains(stage.Waves[^1].Groups, group => bosses.Contains(group.Enemy));
    }

    [Fact]
    public void Clone_preserves_pending_failure_and_rewind_count_in_hash()
    {
        var match = Match();
        typeof(TdMatchState).GetField("_finalWaveFailed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(match, true);
        typeof(TdMatchState).GetProperty("BossRewinds")!.SetValue(match, 2);
        var clone = match.CloneForSimulation();
        Assert.Equal(2, clone.BossRewinds);
        Assert.Equal(match.ComputeStateHash(), clone.ComputeStateHash());
        typeof(TdMatchState).GetField("_finalWaveFailed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(clone, false);
        Assert.NotEqual(match.ComputeStateHash(), clone.ComputeStateHash());
    }
}
