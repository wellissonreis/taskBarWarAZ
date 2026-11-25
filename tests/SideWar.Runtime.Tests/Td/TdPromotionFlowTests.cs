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
    private sealed class Host(TdProfileRecorder recorder, bool minimized = true) : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
        public void RequestExit() { }
        public bool HudMinimized => minimized;
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

    [Fact]
    public void Core_hit_flows_to_demotion_card_with_lost_fusions_without_pausing_or_blocking_controls()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sidewar-demotion-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var content = TdContentLoader.LoadEmbedded();
            content = content with { Stages = content.Stages with { Stages = content.Stages.Stages.Select(s => s with { StartingCredits = 10_000_000 }).ToArray() } };
            // Primeiro inimigo resistente: chega à base com certeza, sem depender de quantas ondas a torre aguenta.
            content = content with { Enemies = content.Enemies with { Enemies = content.Enemies.Enemies.Select(e => e.Id == "grunt" ? e with { Hp = 10_000_000 } : e).ToArray() } };
            var recorder = new TdProfileRecorder(new TdProfileStore(RealFileSystem.Instance, dir, "0.1.0"),
                TdProfileData.Default with { Reputation = TdRank.Threshold(TdRankLevel.Senior) + 10 }, content.Meta, 6);
            var match = TdMatchState.Create(content, 1, Rng.Seed(7, 54), null, recorder.WithRunes(TdMatchOptions.Default));
            Assert.True(match.Apply(new SideWar.Core.Td.LayoutChanged(TdSanityBot.TypicalLayout())).IsOk);
            Assert.True(match.Apply(new SideWar.Core.Td.BuildTower("bolt", 100)).IsOk);
            foreach (var component in new[] { "frost", "cannon", "dns", "functions" })
                Assert.True(match.Apply(new SideWar.Core.Td.FuseTower(match.Towers[0].Id, component)).IsOk, component);
            var ui = new TdUiController(() => match, c => match.Apply(c), new Host(recorder, minimized: false), () => 0);
            ui.Sync();
            typeof(TdMatchState).GetMethod("BeginWave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(match, [1]);
            for (int i = 0; i < 100_000 && match.FusionLossCount == 0; i++)
            {
                match.ClearEvents(); // ficam só os eventos do passo do dano, que o host entrega ao gravador
                match.Step();
            }
            Assert.Equal(1, match.FusionLossCount);
            var game = new TdGameHost(new FakeClock(), new AppStateMachine(), match, recorder);
            game.OnWake(new FakeClock());
            Assert.Equal(TdRankLevel.Pleno, recorder.Rank.Current);
            ui.Sync();

            Assert.True(ui.DemotionCardOpen);
            Assert.False(match.Paused);
            Assert.Equal(0, ui.ModalCount);
            var card = ui.BuildPopover()!;
            Assert.Equal("Rebaixamento", card.Title);
            Assert.Contains(card.Items, i => i.Text.Contains("Fusões perdidas: 1"));
            long frame = match.Frame;
            match.Step();
            Assert.Equal(frame + 1, match.Frame);

            // Com o cartão aberto, pausar/retomar e abrir as torres continuam funcionando.
            TdUiItem Button(TdUiAction action) => ui.BuildHud().Items.First(i => i.Action == action);
            ui.Activate(Button(TdUiAction.PauseToggle));
            Assert.True(match.Paused);
            ui.Activate(Button(TdUiAction.PauseToggle));
            Assert.False(match.Paused);
            ui.Activate(Assert.Single(card.Items, i => i.Action == TdUiAction.DemotionContinue));
            Assert.False(ui.DemotionCardOpen);
            Assert.False(match.Paused);
            ui.Activate(Button(TdUiAction.QuickTowers));
            Assert.DoesNotContain(ui.BuildPopover()!.Items, i => i.Action == TdUiAction.DemotionContinue); // o cartão não volta por cima das torres
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
