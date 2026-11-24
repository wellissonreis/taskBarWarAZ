using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using SideWar.Core.Simulation;
using System.Reflection;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class TdStageGateTests
{
    private static readonly TdStageDef[] Stages = TdContentLoader.LoadEmbedded().Stages.Stages;

    [Theory]
    [InlineData(1, TdRankLevel.Estagiario, 1)]
    [InlineData(1, TdRankLevel.Junior, 2)]
    [InlineData(2, TdRankLevel.Estagiario, 1)]
    [InlineData(8, TdRankLevel.Estagiario, 7)]
    [InlineData(7, TdRankLevel.Estagiario, 7)]
    [InlineData(12, TdRankLevel.Arquiteto, 13)]
    public void Next_respects_rank_and_cycle(int stage, TdRankLevel rank, int next) =>
        Assert.Equal(next, TdStageGate.Next(Stages, stage, rank));

    [Fact]
    public void Each_region_requires_its_corresponding_rank()
    {
        foreach (var stage in Stages)
        {
            Assert.Equal(stage.Id - 1, stage.MinRank);
            Assert.True(TdStageGate.Allowed(stage, (TdRankLevel)stage.MinRank));
            if (stage.MinRank > 0) Assert.False(TdStageGate.Allowed(stage, (TdRankLevel)(stage.MinRank - 1)));
        }
    }

    [Theory]
    [InlineData("brazilsouth", "ao Brazil South")]
    [InlineData("westeurope", "à West Europe")]
    [InlineData("australiaeast", "à Australia East")]
    public void Region_articles(string id, string text) => Assert.Equal(text, TdStageGate.RegionWithArticle(id));

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(1, 5300, 2)]
    [InlineData(8, 0, 7)]
    [InlineData(7, 0, 7)]
    [InlineData(12, 300000, 13)]
    public void Persistent_match_uses_current_reputation_on_stage_clear(int stage, int xp, int next)
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), stage, Rng.Seed(7, 54), null,
            TdMatchOptions.Default with { InitialReputation = xp, ContinuousWaves = false });
        typeof(TdMatchState).GetMethod("ClearStageAndAdvance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(match, null);
        Assert.Equal(next, match.StageNumber);
        Assert.Equal(stage, Assert.Single(match.StageResults).StageNumber);
    }

    [Fact]
    public void Stage_bonus_can_unlock_the_next_region_at_completion()
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54), null,
            TdMatchOptions.Default with { InitialReputation = 5000, ContinuousWaves = false });
        typeof(TdMatchState).GetMethod("ClearStageAndAdvance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(match, null);
        Assert.Equal(2, match.StageNumber);
        Assert.True(match.GateReputation >= TdRank.Threshold(TdRankLevel.Junior));
    }

    [Fact]
    public void Demotion_during_a_region_waits_for_its_end_before_returning()
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 2, Rng.Seed(7, 54), null,
            TdMatchOptions.Default with { InitialReputation = 5400, ContinuousWaves = false });
        typeof(TdMatchState).GetMethod("LoseReputation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(match, [10]);
        Assert.Equal(2, match.StageNumber);
        Assert.Equal(TdRankLevel.Estagiario, TdRank.For(match.GateReputation));
        typeof(TdMatchState).GetMethod("ClearStageAndAdvance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(match, null);
        Assert.Equal(1, match.StageNumber);
    }
}
