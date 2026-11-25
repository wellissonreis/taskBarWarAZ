using System.Reflection;
using SideWar.App.Composition;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Persistence.IO;
using SideWar.Persistence.Profile;
using SideWar.Runtime.Td;
using SideWar.Runtime.Lifecycle;
using Xunit;

namespace SideWar.Runtime.Tests.Td;

public sealed class TdPromotionFlowTests
{
    private sealed class Host(TdProfileRecorder recorder) : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
        public void RequestExit() { }
        public bool HudMinimized => true;
        public void SetHudMinimized(bool minimized) { }
        public void StartStage(int stageId) { }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => recorder.StageUnlocked(stageId);
        public int StageStars(int stageId) => recorder.StageStars(stageId);
        public bool StageCrown(int stageId) => recorder.StageCrown(stageId);
        public int StageMedals(int stageId) => recorder.StageMedals(stageId);
        public int StageBestScore(int stageId) => recorder.StageBestScore(stageId);
        public TdRankProgress Rank => recorder.Rank;
        public TdRecordOutcome LastOutcome => recorder.LastOutcome;
        public int SealsAvailable => recorder.SealsAvailable;
        public bool NodePurchased(string nodeId) => recorder.NodePurchased(nodeId);
        public bool PurchaseNode(string nodeId) => recorder.Purchase(nodeId);
    }

    [Fact]
    public void Stage_xp_flows_through_recorder_to_visible_corner_promotion()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sidewar-promotion-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var content = TdContentLoader.LoadEmbedded();
            int initial = TdRank.Threshold(TdRankLevel.Junior) - 1;
            var recorder = new TdProfileRecorder(new TdProfileStore(RealFileSystem.Instance, dir, "0.1.0"),
                TdProfileData.Default with { Reputation = initial }, content.Meta, 6);
            var match = TdMatchState.Create(content, 1, Rng.Seed(7, 54), null, recorder.WithRunes(TdMatchOptions.Default));
            var ui = new TdUiController(() => match, c => match.Apply(c), new Host(recorder), () => 0);
            ui.Sync();
            ui.CollapseOutside();
            typeof(TdMatchState).GetMethod("ClearStageAndAdvance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(match, null);
            Assert.Contains(match.Events, e => e.Type == TdEventType.ReputationGained && e.A > 0);
            var game = new TdGameHost(new FakeClock(), new AppStateMachine(), match, recorder);
            game.OnWake(new FakeClock());
            Assert.Equal(TdRankLevel.Junior, recorder.Rank.Current);
            ui.Sync();
            Assert.True(ui.PromotionOpen);
            Assert.True(ui.CornerPopover);
            Assert.False(ui.ModalOpen);
            Assert.False(match.Paused);
            Assert.Equal("PROMOÇÃO!", ui.BuildPopover()!.Title);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
