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
    public void PersistedRunesLoadAndReplayAreDeterministic()
    {
        var content = TdContentLoader.LoadEmbedded();
        var options = TdMatchOptions.Default with { InitialAwakened = 0b10111, InitialFragments = 7 };
        var first = TdMatchState.Create(content, 1, Rng.Seed(42, 54), null, options);
        Assert.Equal(4, first.AwakenedCount);
        Assert.Equal(7, first.Fragments);
        Assert.Equal((UInt128)0b10111, first.AwakenedBits);
        Assert.Equal(0, first.WavesReached);
        for (int i = 0; i < 3; i++)
            Assert.Equal(first.ComputeStateHash(), TdMatchState.Create(content, 1, Rng.Seed(42, 54), null, options).ComputeStateHash());
        var replay = new TdReplayScript(1, 42, 54, [], 0, options);
        Assert.Equal(first.ComputeStateHash(), TdReplayRunner.Run(content, replay).StateHash);
        Assert.NotEqual(first.ComputeStateHash(), TdMatchState.Create(content, 1, Rng.Seed(42, 54), null, TdMatchOptions.Default).ComputeStateHash());
        // Replay serializado (JSON) preserva a teia permanente.
        var back = JsonSerializer.Deserialize<TdMatchOptions>(JsonSerializer.Serialize(options))!;
        Assert.Equal(options.InitialAwakened, back.InitialAwakened);
        Assert.Equal(7, back.InitialFragments);
        // Modo clássico ignora a teia permanente.
        var classic = TdMatchState.Create(content, 1, Rng.Seed(42, 54), null, TdMatchOptions.Classic with { InitialAwakened = 1, InitialFragments = 5 });
        Assert.Equal(0, classic.AwakenedCount);
        Assert.Equal(0, classic.Fragments);
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
