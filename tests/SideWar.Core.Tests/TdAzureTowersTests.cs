using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using Xunit;
using Rng = SideWar.Core.Simulation.Rng;

namespace SideWar.Core.Tests;

public sealed class TdAzureTowersTests
{
    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 5)]
    [InlineData(2, 7)]
    [InlineData(3, 10)]
    [InlineData(4, 13)]
    [InlineData(5, 15)]
    public void TowerUnlockUsesProfileRatherThanStartingStage(int completed, int expected)
    {
        var content = TdContentLoader.LoadEmbedded();
        var options = TdMatchOptions.Classic with { HighestCompletedStage = completed };
        var early = TdMatchState.Create(content, 1, Rng.Seed(1, 54), null, options);
        var late = TdMatchState.Create(content, 6, Rng.Seed(1, 54), null, options);
        Assert.Equal(expected, content.Towers.Count(t => t.UnlockStage <= early.UnlockedTowerStage));
        Assert.Equal(early.UnlockedTowerStage, late.UnlockedTowerStage);
        if (completed < 5)
            Assert.Equal(SideWar.Core.Simulation.CommandReason.TowerLocked, late.Apply(new BuildTower("vault", 1100)).Reason);
    }

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

    [Fact]
    public void VaultLimitAndSellingFreeTheSlot()
    {
        var s = Arena("grunt");
        Build(s, "vault", 1000); Build(s, "vault", 1050);
        Assert.Equal(SideWar.Core.Simulation.CommandReason.TowerTypeLimit, s.Apply(new BuildTower("vault", 1100)).Reason);
        Assert.True(s.Apply(new SellTower(s.Towers[0].Id)).IsOk);
        Build(s, "vault", 1100);
    }

    [Fact]
    public void AksPodsAttackWithoutHoldingAndRequireThreeGroundEnemies()
    {
        var two = Arena("grunt", 2); Build(two, "aks", 1100);
        Assert.True(two.Apply(new StartWave()).IsOk); Steps(two, 40);
        Assert.Empty(two.Nodes);
        var three = Arena("grunt", 3); Build(three, "aks", 1100);
        Assert.True(three.Apply(new StartWave()).IsOk); Steps(three, 40);
        Assert.Equal(3, three.Nodes.Count);
        Assert.All(three.Nodes, n => { Assert.Equal(0, n.Holds); Assert.Equal(4, n.Flags); });
        double x = three.Enemies[0].X; double hp = three.Enemies.Sum(e => e.Hp);
        Steps(three, 30);
        Assert.True(three.Enemies[0].X < x);
        Assert.InRange(hp - three.Enemies.Sum(e => e.Hp), 23.9, 24.1);
    }

    [Fact]
    public void SentinelMarkExpiresBeforeNextApplication()
    {
        var s = Arena("grunt"); Build(s, "sentinel", 1100);
        Assert.True(s.Apply(new StartWave()).IsOk); Steps(s, 1);
        Assert.True(s.Enemies[0].VulnTicks > 0);
        Steps(s, 120);
        Assert.Equal(0, s.Enemies[0].VulnTicks);
        Assert.Equal(0, s.Enemies[0].VulnBonus);
    }

    [Fact]
    public void AzureReplayMatchesAcrossFiveRuns()
    {
        var matches = Enumerable.Range(0, 5).Select(_ => Arena("grunt", 4)).ToArray();
        foreach (var s in matches)
        {
            Build(s, "aks", 1100); Build(s, "backup", 1050); Build(s, "vault", 1000);
            Assert.True(s.Apply(new StartWave()).IsOk);
        }
        for (int i = 0; i < 400; i++)
        {
            foreach (var s in matches) s.Step();
            Assert.All(matches, s => Assert.Equal(matches[0].ComputeStateHash(), s.ComputeStateHash()));
        }
    }

    [Theory]
    [InlineData(false, 24)]
    [InlineData(true, 14)]
    public void VaultPaysOnWaveCompletion(bool leaks, long income)
    {
        var s = Arena("grunt"); Build(s, "vault", 1000);
        Assert.True(s.Apply(new StartWave()).IsOk); Steps(s, 1);
        var enemies = (List<TdEnemy>)s.Enemies;
        var e = enemies[0];
        if (leaks) e.X = 0; else e.Hp = 0;
        enemies[0] = e; Steps(s, 1);
        Assert.Equal(income, Assert.Single(s.Events, e => e.Type == TdEventType.VaultIncome).B);
    }

    [Fact]
    public void BackupRevivesAnotherTowerAndCannotReviveItself()
    {
        var s = Arena("grunt"); Build(s, "backup", 1000); Build(s, "bolt", 1050);
        Assert.True(s.Apply(new StartWave()).IsOk);
        var towers = (List<TdTower>)s.Towers;
        var bolt = towers[1]; bolt.DisabledTicks = 90; towers[1] = bolt;
        Steps(s, 1); Assert.Equal(0, s.Towers[1].DisabledTicks);
        var backup = towers[0]; backup.DisabledTicks = 90; towers[0] = backup;
        Steps(s, 1); Assert.Equal(89, s.Towers[0].DisabledTicks);
    }

    [Fact]
    public void BackupHealingDoesNotEraseAccumulatedLoss()
    {
        var s = Arena("grunt", 2); Build(s, "backup", 1000);
        Assert.True(s.Apply(new StartWave()).IsOk); Steps(s, 2);
        var enemies = (List<TdEnemy>)s.Enemies;
        var leaker = enemies[0]; leaker.X = 0; leaker.CoreDamage = 3; enemies[0] = leaker;
        var remaining = enemies[1]; remaining.Speed = 0; enemies[1] = remaining;
        Steps(s, 1); int lost = s.IntegrityLost; int damaged = s.Integrity;
        Assert.True(lost > 0); Steps(s, 1350);
        Assert.Equal(damaged + 1, s.Integrity);
        Assert.Equal(lost, s.IntegrityLost);
        var last = enemies[0]; last.Hp = 0; enemies[0] = last; Steps(s, 1);
        Assert.Equal(2, s.Stars);
        Assert.Equal(3, s.IntegrityLost);
    }

    [Fact]
    public void PipelineCapsDamageAndResetsOnTargetChange()
    {
        var s = Arena("grunt", 2); Build(s, "pipeline", 1100);
        Assert.True(s.Apply(new StartWave()).IsOk); Steps(s, 2);
        var enemies = (List<TdEnemy>)s.Enemies;
        for (int i = 0; i < enemies.Count; i++) { var e = enemies[i]; e.X = 1100 + i; e.Speed = 0; if (i == 1) e.Hp = 1000; enemies[i] = e; }
        Steps(s, 24 * 18);
        var focused = enemies.FindIndex(e => e.Id == s.Towers[0].FocusTargetId);
        double hp = enemies[focused].Hp; Steps(s, 24);
        Assert.Equal(30, hp - enemies[focused].Hp);
        var victim = enemies[focused]; victim.Hp = 0; enemies[focused] = victim;
        Steps(s, 24);
        Assert.Equal(1, s.Towers[0].FocusHits);
    }

    [Fact]
    public void QueuePushesBossHalfDistance()
    {
        var s = Arena("botnet"); Build(s, "queue", 1000);
        Assert.True(s.Apply(new StartWave()).IsOk); Steps(s, 1);
        var enemies = (List<TdEnemy>)s.Enemies;
        var boss = enemies[0]; boss.X = 1050; boss.Speed = 0; enemies[0] = boss;
        Steps(s, 1);
        Assert.Equal(1080, enemies[0].X);
    }

    [Fact]
    public void QueueDoesNotPushEnemyHeldByNode()
    {
        var s = Arena("grunt"); Build(s, "balancer", 1100);
        Assert.True(s.Apply(new StartWave()).IsOk); Steps(s, 1);
        var enemies = (List<TdEnemy>)s.Enemies;
        var e = enemies[0]; e.X = 1100; e.Speed = 0; enemies[0] = e;
        Steps(s, 1); Assert.NotEmpty(s.Nodes);
        Build(s, "queue", 1050); Steps(s, 1);
        Assert.Equal(1100, enemies[0].X);
        Assert.Equal(0, enemies[0].PushGraceTicks);
    }

    [Fact]
    public void NewTowersDoNotConsumeRng()
    {
        var baseline = Arena("grunt", 4); var towers = Arena("grunt", 4);
        Build(towers, "aks", 1100); Build(towers, "backup", 1050); Build(towers, "vault", 1000);
        Build(towers, "sentinel", 950); Build(towers, "queue", 900); Build(towers, "pipeline", 850); Build(towers, "cosmos", 800);
        Assert.True(baseline.Apply(new StartWave()).IsOk); Assert.True(towers.Apply(new StartWave()).IsOk);
        Steps(baseline, 300); Steps(towers, 300);
        var field = typeof(TdMatchState).GetField("_rng", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var a = (Rng)field.GetValue(baseline)!; var b = (Rng)field.GetValue(towers)!;
        Assert.Equal(a.State, b.State); Assert.Equal(a.Inc, b.Inc);
    }

    [Fact]
    public void DisabledVaultDoesNotPay()
    {
        var s = Arena("grunt"); Build(s, "vault", 1000);
        Assert.True(s.Apply(new StartWave()).IsOk); Steps(s, 1);
        var towers = (List<TdTower>)s.Towers; var t = towers[0]; t.DisabledTicks = 30; towers[0] = t;
        var enemies = (List<TdEnemy>)s.Enemies; var e = enemies[0]; e.Hp = 0; enemies[0] = e;
        Steps(s, 1); Assert.DoesNotContain(s.Events, e => e.Type == TdEventType.VaultIncome);
    }

    [Fact]
    public void PodsExpireAndDoNotTargetFlyingEnemies()
    {
        var flying = Arena("flyer", 3); Build(flying, "aks", 1100);
        Assert.True(flying.Apply(new StartWave()).IsOk); Steps(flying, 100);
        Assert.Empty(flying.Nodes); Assert.All(flying.Enemies, e => Assert.Equal(e.MaxHp, e.Hp));
        var s = Arena("grunt", 3); Build(s, "aks", 1100);
        Assert.True(s.Apply(new StartWave()).IsOk); Steps(s, 40);
        Assert.Equal(3, s.Nodes.Count); Steps(s, 360);
        Assert.Empty(s.Nodes);
    }

}
