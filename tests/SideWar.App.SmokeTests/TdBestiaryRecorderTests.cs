using System.Reflection;
using SideWar.App.Composition;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Persistence.IO;
using SideWar.Persistence.Profile;
using Xunit;

namespace SideWar.App.SmokeTests;

public sealed class TdBestiaryRecorderTests
{
    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(5000, 2, true)]
    public void Stage_gate_persists_actual_next_stage_and_reputation(int xp, int nextStage, bool eastUnlocked)
    {
        string dir = Path.Combine(Path.GetTempPath(), "sidewar-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var content = TdContentLoader.LoadEmbedded();
            var store = new TdProfileStore(RealFileSystem.Instance, dir, "0.1.0");
            var recorder = new TdProfileRecorder(store, TdProfileData.Default with { Reputation = xp }, content.Meta, 6);
            var match = TdMatchState.Create(content, 1, Rng.Seed(7, 54), null,
                recorder.WithRunes(TdMatchOptions.Default) with { ContinuousWaves = false });
            typeof(TdMatchState).GetMethod("ClearStageAndAdvance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(match, null);
            recorder.OnEvents(match, match.Events);
            Assert.Equal(nextStage, match.StageNumber);
            Assert.Equal(match.GateReputation, recorder.Profile.Reputation);
            Assert.Equal(nextStage, store.Load().Profile.RunStageNumber);
            Assert.Equal(eastUnlocked, recorder.StageUnlocked(2));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Encounters_are_saved_immediately_and_reset_with_the_profile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sidewar-bestiary-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var content = TdContentLoader.LoadEmbedded();
            var store = new TdProfileStore(RealFileSystem.Instance, dir, "0.1.0");
            var recorder = new TdProfileRecorder(store, TdProfileData.Default, content.Meta, 6);
            var match = TdMatchState.Create(content, 1, Rng.Seed(7, 54));
            int index = Array.FindIndex(content.Enemies.Enemies, e => e.Id == "swarm");
            var make = typeof(TdMatchState).GetMethod("MakeEnemy", BindingFlags.Instance | BindingFlags.NonPublic)!;
            make.Invoke(match, [index, 1, 100d, (ushort)1]);
            recorder.OnEvents(match, match.Events);
            var profile = store.Load().Profile;
            Assert.Equal(1, profile.Encounters.Single(e => e.Id == "swarm").Count);
            Assert.Equal(1, profile.Encounters.Single(e => e.Id == "swarm:elite").Count);
            recorder.ResetProgress();
            Assert.Empty(store.Load().Profile.Encounters);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
