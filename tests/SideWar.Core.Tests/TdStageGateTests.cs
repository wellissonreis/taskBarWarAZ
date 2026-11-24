using SideWar.Core.Content.Td;
using SideWar.Core.Td;
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
}
