using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using Xunit;
using Rng = SideWar.Core.Simulation.Rng;

namespace SideWar.Core.Tests;

public sealed class TdAzureTowersTests
{
    private static TdMatchState Arena(string enemy, int count = 1, double armor = 0)
    {
        var c = TdContentLoader.LoadEmbedded();
        c = c with
        {
            Enemies = c.Enemies with { Enemies = c.Enemies.Enemies.Select(e => e.Id == enemy ? e with { Hp = 10000, Armor = armor, Speed = 30 } : e).ToArray() },
            Stages = c.Stages with { Stages = c.Stages.Stages.Select(s => s.Id == 6 ? s with { StartingCredits = 1000000, Waves = 1 } : s).ToArray() },
            Waves = new TdWavesDef([.. c.Waves.Stages.Take(5), new TdStageWavesDef(6, [new TdWaveDef(1, false, 1, [new TdWaveGroupDef(enemy, count, 0, 0.01)])])])
        };
        return TdMatchState.Create(c, 6, Rng.Seed(1, 54));
    }

    private static void Build(TdMatchState s, string id, int x) => Assert.True(s.Apply(new BuildTower(id, x)).IsOk);
    private static void Steps(TdMatchState s, int n) { for (int i = 0; i < n; i++) s.Step(); }

    [Fact]
    public void FunctionsFiresOneBurstForSimultaneousEntrants()
    {
        var s = Arena("grunt", 2);
        Build(s, "functions", 1100);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 100);
        Assert.Equal(3, s.Towers[0].ShotCount);
        Assert.Single(s.Enemies, e => e.Hp < e.MaxHp);
        Assert.Equal(66, s.Enemies.Sum(e => e.MaxHp - e.Hp));
        Steps(s, 30);
        Assert.Equal(3, s.Towers[0].ShotCount);
    }

    [Fact]
    public void CdnHasMinimumRangeAndArmorFloor()
    {
        var s = Arena("armored", armor: 38);
        Build(s, "cdn", 1000);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 1);
        Assert.Equal(1, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
        Steps(s, 200);
        int shots = s.Towers[0].ShotCount;
        Steps(s, 60);
        Assert.Equal(shots, s.Towers[0].ShotCount);
    }

    [Fact]
    public void PolicyIgnoresArmorOnlyInsideItsAura()
    {
        var s = Arena("armored", armor: 38);
        Build(s, "bolt", 1060);
        Build(s, "policy", 1100);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 1);
        Assert.Equal(24, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
    }

    [Fact]
    public void ReplayIsDeterministicWithFunctions()
    {
        var a = Arena("grunt", 2); var b = Arena("grunt", 2);
        foreach (var s in new[] { a, b }) { Build(s, "functions", 1100); Assert.True(s.Apply(new StartWave()).IsOk); }
        for (int i = 0; i < 180; i++) { a.Step(); b.Step(); Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash()); }
    }
}
