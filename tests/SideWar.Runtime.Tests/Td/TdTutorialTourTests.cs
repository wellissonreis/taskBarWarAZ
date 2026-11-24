using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using Xunit;

namespace SideWar.Runtime.Tests.Td;

public sealed class TdTutorialTourTests
{
    private sealed class Store : ITdOnboardingStore
    {
        public bool TutorialDone { get; private set; }
        public void SetTutorialDone(bool done) => TutorialDone = done;
        public bool TipSeen(string id) => false;
        public void MarkTipSeen(string id) { }
    }
    private sealed class Host : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
        public void RequestExit() { }
        public bool HudMinimized => false;
        public void SetHudMinimized(bool minimized) { }
        public void StartStage(int stageId) { }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => true;
        public int StageStars(int stageId) => 0;
        public bool StageCrown(int stageId) => false;
        public int StageMedals(int stageId) => 0;
        public int StageBestScore(int stageId) => 0;
        public TdRankProgress Rank => TdRank.Progress(0);
        public TdRecordOutcome LastOutcome => default;
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => false;
    }
    private static TdUiController Create(Store store)
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host(), () => 0, store);
        ui.Sync();
        ui.OpenStartupModal();
        return ui;
    }
    private static void Click(TdUiController ui, TdUiAction action) =>
        ui.Activate(new TdUiItem(default, TdUiKind.Button, "", action));

    [Fact]
    public void Tour_covers_the_features_players_can_discover()
    {
        string content = string.Join(" ", TdTutorial.Pages.Select(p => p.Text + " " + p.Detail));
        foreach (string term in new[] { "base", "game over", "iniciar", "fraquezas", "mover", "fundir", "mira", "Cofre", "patente", "sequência", "runas", "zoom", "regiões", "selos", "ciclos", "chefões", "HUD", "avisos", "boss key", "Recomeçar", "bestiário", "Refazer tutorial" })
            Assert.Contains(term, content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void All_pages_are_reachable_in_order_without_towers_and_highlight_real_items()
    {
        var store = new Store();
        var ui = Create(store);
        for (int i = 0; i < TdTutorial.Pages.Count; i++)
        {
            var view = ui.BuildPopover()!;
            Assert.Contains(view.Items, x => x.SArg == "tut" && x.Text.Contains(TdTutorial.Pages[i].Text));
            Assert.Contains(view.Items, x => x.Highlight);
            Assert.Contains(view.Items, x => x.Action == TdUiAction.TutorialNext);
            Assert.Contains(view.Items, x => x.Action == TdUiAction.TutorialSkip);
            Assert.Equal(i > 0, view.Items.Single(x => x.Action == TdUiAction.TutorialPrevious).Enabled);
            if (TdTutorial.Pages[i].HudTarget != TdUiAction.None)
                Assert.Contains(ui.BuildHud().Items, x => x.Action == TdTutorial.Pages[i].HudTarget && x.Highlight);
            Click(ui, TdUiAction.TutorialNext);
        }
        Assert.True(store.TutorialDone);
        Assert.Equal(TdTutorialStep.None, ui.Step);
    }

    [Fact]
    public void Back_skip_restart_and_recreation_preserve_completion()
    {
        var store = new Store();
        var ui = Create(store);
        Click(ui, TdUiAction.TutorialNext);
        Click(ui, TdUiAction.TutorialNext);
        Click(ui, TdUiAction.TutorialPrevious);
        Assert.Contains(ui.BuildPopover()!.Items, x => x.SArg == "tut" && x.Text.Contains(TdTutorial.Pages[1].Text));
        Click(ui, TdUiAction.TutorialSkip);
        Assert.True(store.TutorialDone);
        Assert.Equal(TdTutorialStep.None, Create(store).Step);
        Click(ui, TdUiAction.RedoTutorial);
        Assert.False(store.TutorialDone);
        Assert.Contains(ui.BuildPopover()!.Items, x => x.Text.Contains(TdTutorial.Pages[0].Text));
        Assert.Equal(TdTutorialStep.Tour, Create(store).Step);
    }

    [Fact]
    public void Help_has_scrollable_sections_and_fixed_redo()
    {
        var store = new Store();
        store.SetTutorialDone(true);
        var ui = Create(store);
        ui.OpenHowTo();
        var titles = new HashSet<string>();
        do
        {
            var view = ui.BuildPopover()!;
            foreach (var item in view.Items.Where(x => x.SArg == "help-section")) titles.Add(item.Text);
            Assert.Contains(view.Items, x => x.Action == TdUiAction.RedoTutorial);
            var scroll = view.Scroll!.Value;
            if (scroll.Offset == scroll.Total - scroll.Visible) break;
            Assert.True(ui.ScrollBy(1));
        } while (true);
        Assert.Equal(5, titles.Count);
        Assert.Contains("FASE / SELO", titles);
    }
}
