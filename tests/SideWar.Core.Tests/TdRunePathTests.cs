using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class TdRunePathTests
{
    private static readonly string[] ShortPath = ["d", "e"];
    private static readonly string[] LongPath = ["a", "b", "c"];
    private static readonly string[] BothStarts = ["a", "d"];
    private static readonly string[] FirstStart = ["a"];

    [Fact]
    public void Chooses_cheapest_path_then_fewest_runes_and_skips_awakened_targets()
    {
        TdRuneNodeDef[] nodes = [new("a", 0, 0, "x", 0, 0), new("b", 1, 0, "x", 0, 0),
            new("c", 2, 0, "goal", 0, 0), new("d", 0, 1, "x", 0, 0),
            new("e", 1, 1, "goal", 0, 0)];
        var costs = new Dictionary<string, int> { ["a"] = 1, ["b"] = 1, ["c"] = 1, ["d"] = 2, ["e"] = 1 };
        Assert.Equal(ShortPath, TdRunePath.Find(nodes, BothStarts, _ => false, id => costs[id], "goal"));
        costs["d"] = 3;
        Assert.Equal(LongPath, TdRunePath.Find(nodes, BothStarts, _ => false, id => costs[id], "goal"));
        Assert.Empty(TdRunePath.Find(nodes, FirstStart, id => id is "c" or "e", id => costs[id], "goal"));
    }
}
