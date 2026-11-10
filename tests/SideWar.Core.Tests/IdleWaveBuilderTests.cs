using SideWar.Core.Content;
using SideWar.Core.Waves;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class IdleWaveBuilderTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly IdleWaveBuilder Builder = new(Content);

    private static Dictionary<string, int> Mix(WavePlan plan)
        => plan.Spawns.GroupBy(s => s.Enemy.Id).ToDictionary(g => g.Key, g => g.Count());

    // Valores esperados calculados fora do código (planilha/python) a partir de SDD-27 RF-27.7.
    [Theory(DisplayName = "T-12.2 / CA-27.4 Wave count, interval and mix")]
    [InlineData(1, 60, 6, 0.85, false, 6, 0, 0, 0, 0, 0)]
    [InlineData(20, 60, 8, 0.6889830508474576, true, 4, 2, 1, 0, 0, 1)]
    [InlineData(60, 60, 17, 0.35, true, 1, 5, 3, 5, 2, 1)]
    [InlineData(72, 80, 30, 0.40063291139240503, false, 1, 9, 6, 9, 5, 0)]
    [InlineData(100, 100, 19, 0.35, true, 1, 5, 4, 5, 3, 1)]
    public void Build_Table(int w, int total, int count, double interval, bool boss,
        int grunt, int runner, int tank, int swarm, int armored, int bossCount)
    {
        var plan = Builder.Build(w, total, 0);
        Assert.Equal(count, plan.Count);
        Assert.Equal(count, plan.Spawns.Length);
        Assert.Equal(interval, plan.Interval, 12);
        Assert.Equal(boss, plan.IsBoss);
        var mix = Mix(plan);
        int Get(string k) => mix.GetValueOrDefault(k);
        Assert.Equal(grunt, Get("grunt"));
        Assert.Equal(runner, Get("runner"));
        Assert.Equal(tank, Get("tank"));
        Assert.Equal(swarm, Get("swarm"));
        Assert.Equal(armored, Get("armored"));
        Assert.Equal(bossCount, Get("boss"));
        for (var i = 0; i < plan.Spawns.Length; i++)
            Assert.Equal(i * interval, plan.Spawns[i].Time, 12);
    }

    [Fact(DisplayName = "CA-27.4 Boss sits at floor(len/2) and only on 20/40/60/80/100")]
    public void Boss_Placement_And_Waves()
    {
        for (var w = 1; w <= 100; w++)
        {
            var plan = Builder.Build(w, 100, 0);
            var expected = w % 20 == 0;
            Assert.Equal(expected, plan.IsBoss);
            Assert.Equal(expected ? 1 : 0, plan.Spawns.Count(s => s.Enemy.Id == "boss"));
            if (expected)
                Assert.Equal("boss", plan.Spawns[(plan.Count - 1) / 2].Enemy.Id);
        }
    }

    [Fact(DisplayName = "CA-27.4 N(w) grows to 30 from wave 72 and never exceeds 30 regular")]
    public void Count_Caps()
    {
        Assert.Equal(6, Builder.BaseCount(1));
        Assert.Equal(29, Builder.BaseCount(69));
        Assert.Equal(30, Builder.BaseCount(72));
        Assert.Equal(30, Builder.BaseCount(100));
    }

    [Theory(DisplayName = "CA-27.3 HP, gold and XP per wave, stage and region")]
    [InlineData(1, 0, 1.0, 42.0, 9.0, 5.0)]
    [InlineData(20, 0, 1.0, 215.94977270338404, 32.548747815304345, 10.534245879968369)]
    [InlineData(60, 9, 2.0, 19670.28565207193, 487.3998518792554, 50.575131769484884)]
    [InlineData(100, 9, 1.4, 432482.585239521, 7298.5485328016375, 242.81225088591145)]
    public void Scaling_Grunt(int w, int stage, double region, double hp, double gold, double xp)
    {
        var plan = Builder.Build(w, 100, stage, region);
        var g = plan.Spawns.First(s => s.Enemy.Id == "grunt");
        Assert.Equal(hp, g.Hp, hp * 1e-9);
        Assert.Equal(gold, g.Gold, gold * 1e-9);
        Assert.Equal(xp, g.Xp, xp * 1e-9);
    }

    [Fact(DisplayName = "CA-27.3 Boss uses boss base values and speed does not grow")]
    public void Boss_Values()
    {
        var b = Builder.Build(20, 60, 0).Spawns.Single(s => s.Enemy.Id == "boss");
        Assert.Equal(620 * 1.09 * 1.09 * 1.09 * 1.09 * 1.09 * 1.09 * 1.09 * 1.09 * 1.09 * 1.09 * 1.09 * 1.09 * 1.09
            * 1.09 * 1.09 * 1.09 * 1.09 * 1.09 * 1.09, b.Hp, 6);
        Assert.Equal(25, b.Enemy.Speed);
    }

    [Fact(DisplayName = "Build rejects invalid wave")]
    public void Invalid_Wave() => Assert.Throws<ArgumentOutOfRangeException>(() => Builder.Build(0, 60, 0));
}
