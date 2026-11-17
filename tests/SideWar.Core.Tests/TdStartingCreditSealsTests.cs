using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class TdStartingCreditSealsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void StartingCreditSeals_StackOnceAtCreation_InEveryStage(int count)
    {
        var content = TdContentLoader.LoadEmbedded();
        var nodes = Enumerable.Range(1, count).Select(n => $"infrastructure.startingCredits{n}").ToArray();
        foreach (var stage in content.Stages.Stages)
        {
            var match = TdMatchState.Create(content, stage.Id, Rng.Seed(42, 54), [.. nodes, .. nodes, "unknown", "infrastructure.slots2"]);
            long expected = stage.StartingCredits + content.Runes.CreditSources.StartingCreditsBonus + 40 * count;
            Assert.Equal(expected, match.Credits);
            match.Step();
            Assert.Equal(expected, match.Credits);
            Assert.Equal(0, match.AwakenedCount);
            var again = TdMatchState.Create(content, stage.Id, Rng.Seed(42, 54), [.. nodes, .. nodes, "unknown", "infrastructure.slots2"]);
            again.Step();
            Assert.Equal(match.ComputeStateHash(), again.ComputeStateHash());
        }
    }

    [Fact]
    public void Meta_ReplacesObsoleteNodes_WithSameSealCosts_AndFortyCreditsEach()
    {
        var meta = TdContentLoader.LoadEmbedded().Meta;
        var nodes = meta.Branches.Single(b => b.Id == "infrastructure").Nodes;
        Assert.Equal(["infrastructure.credits", "infrastructure.startingCredits1", "infrastructure.startingCredits2", "infrastructure.startingCredits3"], nodes.Select(n => n.Id));
        Assert.Equal([1, 2, 3, 4], nodes.Select(n => n.Cost));
        Assert.Equal(["Créditos iniciais I", "Créditos iniciais II", "Créditos iniciais III"], nodes.Skip(1).Select(n => n.Name));
        Assert.All(nodes.Skip(1), n =>
        {
            var effect = Assert.Single(n.Effects);
            Assert.Equal("startingCredits", effect.Kind);
            Assert.Equal(40, effect.Value);
        });
    }
}
