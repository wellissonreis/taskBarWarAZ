using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using Xunit;
using Rng = SideWar.Core.Simulation.Rng;

namespace SideWar.Core.Tests;

public sealed class TdDifficultyTests
{
    private static TdMatchState Match(TdDifficulty difficulty, bool fused = false, bool weakModule = false)
    {
        var c = TdContentLoader.LoadEmbedded();
        c = c with
        {
            Towers = c.Towers.Select(t => t.Id switch
            {
                "bolt" => t with { Damage = 100, Strong = [], Weak = ["basic"], Stats = new() },
                "dns" => t with { Damage = 100, Strong = [], Weak = weakModule ? ["basic"] : [], Stats = new() },
                _ => t,
            }).ToArray(),
            Enemies = c.Enemies with { Enemies = c.Enemies.Enemies.Select(e => e.Id == "grunt" ? e with { Hp = 1000000, Armor = 0, Speed = 1 } : e).ToArray() },
            Stages = c.Stages with { Stages = c.Stages.Stages.Select(s => s.Id == 6 ? s with { StartingCredits = 1000000, Waves = 1 } : s).ToArray() },
            Waves = new TdWavesDef([.. c.Waves.Stages.Take(5), new TdStageWavesDef(6, [new TdWaveDef(1, false, 1, [new TdWaveGroupDef("grunt", 1, 0, 1)])])]),
        };
        var s = TdMatchState.Create(c, 6, Rng.Seed(1, 54), null, TdMatchOptions.Classic with { Difficulty = difficulty, InitialReputation = TdRank.Threshold(TdRankLevel.Arquiteto) });
        Assert.True(s.Apply(new BuildTower("bolt", 1100)).IsOk);
        if (fused) Assert.True(s.Apply(new FuseTower(s.Towers[0].Id, "dns")).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        s.Step();
        var enemies = (List<TdEnemy>)s.Enemies;
        var enemy = enemies[0]; enemy.X = 1100; enemy.Speed = 0; enemies[0] = enemy;
        return s;
    }

    private static double Damage(TdMatchState s, int ticks = 400)
    {
        for (int i = 0; i < ticks; i++) s.Step();
        return s.Enemies[0].MaxHp - s.Enemies[0].Hp;
    }

    [Fact]
    public void Modes_reduce_damage_and_extreme_prevents_weak_tower_damage()
    {
        var casual = Match(TdDifficulty.Casual);
        var hard = Match(TdDifficulty.Hard);
        var extreme = Match(TdDifficulty.Extreme);
        Assert.NotEqual(casual.ComputeStateHash(), hard.ComputeStateHash());
        Assert.NotEqual(hard.ComputeStateHash(), extreme.ComputeStateHash());
        Assert.True(Damage(casual) > Damage(hard));
        Assert.True(Damage(hard) > 0);
        Assert.Equal(0, Damage(extreme));
    }

    [Fact]
    public void Extreme_fusion_keeps_normal_component_and_reduces_weak_component()
    {
        var mixed = Match(TdDifficulty.Extreme, fused: true);
        var weak = Match(TdDifficulty.Extreme, fused: true, weakModule: true);
        double weakDamage = Damage(weak);
        double mixedDamage = Damage(mixed);
        Assert.True(weakDamage > 0);
        Assert.True(mixedDamage > weakDamage);
    }
}
