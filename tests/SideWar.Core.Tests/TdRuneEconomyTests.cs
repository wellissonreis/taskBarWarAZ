using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;

namespace SideWar.Core.Tests;

public sealed class TdRuneEconomyTests
{
    [Fact]
    public void Path_is_shortest_stable_and_each_step_is_buyable()
    {
        var c = TdContentLoader.LoadEmbedded();
        var graph = new TdRuneGraph(c.Runes);
        foreach (var target in c.Runes.Nodes)
        {
            var s = TdMatchState.Create(Rich(c), 6, Rng.Seed(1, 54));
            var path = graph.PathTo(target.Id, s.OwnedRunes);
            foreach (string id in path) Assert.True(s.Apply(new BuyRune(id)).IsOk, id);
            Assert.True(s.OwnsRune(target.Id));
            Assert.Empty(graph.PathTo(target.Id, s.OwnedRunes));
        }
        Assert.Empty(graph.PathTo("unknown", []));
        Assert.Equal(["border.bolt-frost"], graph.PathTo("border.bolt-frost", ["frost.n1"]));
        Assert.Equal(["bolt.s1", "bolt.s4", "bolt.n2"], graph.PathTo("bolt.n2", []));
    }

    private static TdContent Rich(TdContent c) => c with { Stages = c.Stages with { Stages = c.Stages.Stages.Select(s => s with { StartingCredits = 2_000_000_000 }).ToArray() } };

    [Fact]
    public void Discount_refund_sell_sweep_never_returns_more_than_paid()
    {
        var c = Rich(TdContentLoader.LoadEmbedded());
        foreach (var kind in c.Towers)
        foreach (bool autoBudget in new[] { false, true })
        foreach (bool saleRunes in new[] { false, true })
        {
            var variant = c with { Stages = c.Stages with { Rules = c.Stages.Rules with { SellRefund = saleRunes ? 0.75 : 0.7 } } };
            var s = TdMatchState.Create(variant, 6, Rng.Seed(1, 54));
            long initial = s.Credits;
            var graph = new TdRuneGraph(c.Runes);
            void Buy(string target) { foreach (var id in graph.PathTo(target, s.OwnedRunes)) Assert.True(s.Apply(new BuyRune(id)).IsOk); }
            if (saleRunes) Buy("general.s5");
            Buy("general.n1");
            if (autoBudget) Buy("border.scaleset-general");
            for (int n = 0; n < 48; n++)
                Assert.True(s.Apply(new BuildTower(kind.Id, 10 + n * 24)).IsOk);
            Assert.True(s.Apply(new RefundRune("general.s1")).IsOk);
            if (saleRunes) Buy("general.s5"); // rebuild sale bonuses without restoring Cota folgada
            foreach (var t in s.Towers.ToArray().Reverse())
            {
                long before = s.Credits;
                long refund = s.SellValue(t.Id);
                Assert.InRange(refund, 0, t.PurchasePrice);
                Assert.True(s.Apply(new SellTower(t.Id)).IsOk);
                Assert.Equal(before + refund, s.Credits);
            }
            if (saleRunes) Assert.True(s.Apply(new RefundRune("general.s1")).IsOk);
            Assert.True(s.Credits <= initial);
        }
    }
}
