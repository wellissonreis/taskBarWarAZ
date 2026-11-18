using System.Text.Json;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

public class TdLegacyTests
{
    [Theory]
    [InlineData(-1, 0)] [InlineData(4, 0)] [InlineData(5, 1)] [InlineData(12, 2)]
    [InlineData(29, 5)] [InlineData(30, 6)] [InlineData(int.MaxValue, 6)]
    public void FragmentsAreBounded(int waves, int expected) => Assert.Equal(expected, TdLegacy.Fragments(waves));

    [Theory]
    [InlineData(-1, 0)] [InlineData(5, 0)] [InlineData(6, 1)] [InlineData(12, 2)] [InlineData(18, 3)] [InlineData(int.MaxValue, 3)]
    public void StartingRunesAreBounded(int total, int expected) => Assert.Equal(expected, TdLegacy.StartingRunes(total));

    [Fact]
    public void InitialRunesAndReplayAreDeterministic()
    {
        var content = TdContentLoader.LoadEmbedded();
        var options = TdMatchOptions.Default with { LegacyRunes = 3 };
        var first = TdMatchState.Create(content, 1, Rng.Seed(42, 54), null, options);
        Assert.Equal(3, first.AwakenedCount);
        Assert.Equal(0, first.Fragments);
        Assert.Equal(0, first.WavesReached);
        for (int i = 0; i < 5; i++)
        {
            var next = TdMatchState.Create(content, 1, Rng.Seed(42, 54), null, options);
            Assert.Equal(first.ComputeStateHash(), next.ComputeStateHash());
            Assert.Equal(first.RuneWeb.Where(n => first.IsAwakened(n.Id)).Select(n => n.Id), next.RuneWeb.Where(n => next.IsAwakened(n.Id)).Select(n => n.Id));
        }
        var replay = new TdReplayScript(1, 42, 54, [], 0, options);
        Assert.Equal(first.ComputeStateHash(), TdReplayRunner.Run(content, replay).StateHash);
        Assert.Equal(0, JsonSerializer.Deserialize<TdMatchOptions>("{}")!.LegacyRunes);
        Assert.Throws<ArgumentOutOfRangeException>(() => TdMatchState.Create(content, 1, Rng.Seed(42, 54), null, options with { LegacyRunes = 4 }));
    }

    [Fact]
    public void AdvisorUsesDamageAndUnlockedContent()
    {
        var content = TdContentLoader.LoadEmbedded();
        var enemy = content.Enemies.Enemies.First(e => e.Traits.Contains("fast"));
        var suggestion = TdAdvisor.Suggest(content, [new TdLeakStat(enemy.Id, false, false, 2, 10)], 6);
        Assert.NotNull(suggestion);
        Assert.Contains(content.Towers, t => t.Id == suggestion.TowerId);
        Assert.Null(TdAdvisor.Suggest(content, [], 6));
    }
}
