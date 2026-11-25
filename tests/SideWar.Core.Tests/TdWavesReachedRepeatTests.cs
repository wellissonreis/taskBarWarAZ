using System.Reflection;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class TdWavesReachedRepeatTests
{
    [Fact]
    public void Repeating_a_rank_locked_stage_counts_its_waves_without_throwing()
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(42, 54), null,
            TdMatchOptions.Default with { InitialReputation = 0 });
        var advance = typeof(TdMatchState).GetMethod("ClearStageAndAdvance", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var active = (System.Collections.IList)typeof(TdMatchState).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(match)!;
        for (int clear = 0; clear < 2; clear++) { active.Clear(); advance.Invoke(match, null); }
        Assert.Equal(1, match.StageNumber);
        Assert.Equal(2 * match.TotalWaves, match.WavesReached);
    }
}
