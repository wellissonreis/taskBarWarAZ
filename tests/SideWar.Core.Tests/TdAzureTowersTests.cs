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
    [Fact]
    public void CosmosChainsOncePerEnemyWithFalloff()
    {
        var s = Arena("grunt", 4);
        Build(s, "cosmos", 1100);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 2);
        Assert.Equal(new double[] { 10, 12, 16, 20 }, s.Enemies.Select(e => e.MaxHp - e.Hp).Order().ToArray());
    }

    [Fact]
    public void SentinelMarksBeforeAttacksRegardlessOfBuildOrder()
    {
        var s = Arena("grunt");
        Build(s, "bolt", 1060);
        Build(s, "sentinel", 1100);
        Assert.True(s.Apply(new SetTargeting(s.Towers[1].Id, TdTargeting.Strong)).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 1);
        Assert.Equal(30, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
        Assert.Equal(0.25, s.Enemies[0].VulnBonus);
        Assert.Equal(0, s.Enemies[0].SlowTicks);
    }

    [Fact]
    public void PipelineRampsAndQueueGrantsGrace()
    {
        var s = Arena("grunt");
        Build(s, "pipeline", 1100);
        Build(s, "queue", 1060);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 70);
        Assert.True(s.Towers[0].FocusHits >= 2);
        Assert.True(s.Enemies[0].PushGraceTicks > 0);
    }

    [Fact]
    public void NewTowersReplayDeterministically()
    {
        var a = Arena("grunt", 4); var b = Arena("grunt", 4);
        foreach (var s in new[] { a, b })
        {
            Build(s, "cosmos", 1100); Build(s, "sentinel", 1060);
            Build(s, "queue", 1020); Build(s, "pipeline", 980);
            Assert.True(s.Apply(new StartWave()).IsOk);
        }
        for (int i = 0; i < 250; i++) { a.Step(); b.Step(); Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash()); }
    }
    [Fact]
    public void QueueDoesNotTriggerFunctionsWhenItPushesIntoRange()
    {
        var s = Arena("grunt");
        Build(s, "functions", 1200);
        Build(s, "queue", 980);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 105);
        Assert.Equal(3, s.Towers[0].ShotCount);
        Steps(s, 15);
        Assert.True(s.Enemies[0].PushGraceTicks > 0);
        Assert.Equal(3, s.Towers[0].ShotCount);
    }

    [Fact]
    public void PolicyUsesPositionAfterQueuePush()
    {
        var s = Arena("armored", armor: 38);
        Build(s, "policy", 980);
        Build(s, "queue", 980);
        Build(s, "bolt", 1100);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 120);
        Assert.True(s.Enemies[0].PushGraceTicks > 0);
        Assert.True(s.Enemies[0].X > 1090);
        double hp = s.Enemies[0].Hp;
        Steps(s, 16);
        Assert.Equal(1, hp - s.Enemies[0].Hp);
    }

    [Fact]
    public void PipelineDamageRisesPerHit()
    {
        var s = Arena("grunt");
        Build(s, "pipeline", 1100);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 1);
        Assert.Equal(14, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
        Steps(s, 24);
        Assert.Equal(29, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
        Steps(s, 24);
        Assert.Equal(45, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
    }

    [Fact]
    public void SentinelMarksDoNotStack()
    {
        var s = Arena("grunt");
        Build(s, "sentinel", 1100);
        Build(s, "sentinel", 1080);
        Build(s, "bolt", 1060);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 1);
        Assert.Equal(30, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
        Assert.Equal(0.25, s.Enemies[0].VulnBonus);
    }

}
