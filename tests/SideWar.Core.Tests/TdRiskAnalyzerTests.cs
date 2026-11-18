using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class TdRiskAnalyzerTests
{
    [Fact]
    public void NextStep_sums_simultaneous_leaks_and_ignores_distant_or_dead_enemies()
    {
        TdEnemy[] enemies =
        [
            new() { X = 101, Speed = 30, Hp = 1, CoreDamage = 3 },
            new() { X = 100.5, Speed = 30, Hp = 1, CoreDamage = 4, ParalyzeTicks = 10 },
            new() { X = 102, Speed = 30, Hp = 1, CoreDamage = 8 },
            new() { X = 100, Speed = 30, Hp = 0, CoreDamage = 10 },
        ];
        Assert.Equal(7, TdRiskAnalyzer.NextStepLeakDamage(enemies, 100));
        Assert.Equal(7, TdRiskAnalyzer.NextStepLeakDamage(enemies, 100));
        Assert.Equal(0.5 / 30, TdRiskAnalyzer.SecondsToLeak(enemies, 100), 10);
    }

    [Fact]
    public void No_moving_threat_has_infinite_projected_time()
    {
        TdEnemy[] enemies = [new() { X = 20, Speed = 0, Hp = 1, CoreDamage = 1 }];
        Assert.Equal(double.PositiveInfinity, TdRiskAnalyzer.SecondsToLeak(enemies, 0));
        Assert.Equal(0, TdRiskAnalyzer.NextStepLeakDamage(enemies, 0));
    }
}
