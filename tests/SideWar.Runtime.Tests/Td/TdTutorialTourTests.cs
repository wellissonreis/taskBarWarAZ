using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using SideWar.Runtime.Input;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using BuildTower = SideWar.Core.Td.BuildTower;
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
        public void RequestExit() { }
        public bool HudMinimized { get; private set; }
        public void SetHudMinimized(bool minimized) => HudMinimized = minimized;
        public void StartStage(int stageId) { }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => true;
        public int StageStars(int stageId) => 0;
        public bool StageCrown(int stageId) => false;
        public int StageMedals(int stageId) => 0;
        public int StageBestScore(int stageId) => 0;
        public int Score { get; set; }
        public TdRankProgress Rank => TdRank.Progress(Score);
        public TdRecordOutcome LastOutcome => default;
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => false;
    }
    private static TdUiController Create(Store store, Host? host = null)
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
        match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        var ui = new TdUiController(() => match, c => match.Apply(c), host ?? new Host(), () => 0, store);
        ui.Sync();
        ui.OpenStartupModal();
        return ui;
    }
    private static void Click(TdUiController ui, TdUiAction action) =>
        ui.Activate(new TdUiItem(default, TdUiKind.Button, "", action));

    [Fact]
    public void Promotion_during_tutorial_is_a_corner_notice_without_pausing()
    {
        var host = new Host();
        var ui = Create(new Store(), host);
        Assert.Equal(TdTutorialStep.Tour, ui.Step);
        host.Score = TdRank.Threshold(TdRankLevel.Junior);
        ui.Sync();
        Assert.Equal("PROMOÇÃO!", ui.BuildPopover()!.Title);
        Assert.True(ui.CornerPopover);
        Assert.False(ui.ModalOpen);
    }

    [Fact]
    public void First_tower_tutorial_keeps_hud_and_band_accessible()
    {
        var host = new Host();
        host.SetHudMinimized(true);
        var ui = Create(new Store(), host);
        Assert.True(ui.CornerPopover);
        Assert.False(ui.ModalOpen);
        var hud = ui.BuildHud();
        var restore = hud.Items.First(i => i.Action == TdUiAction.HudRestore);
        Assert.Equal(restore, hud.Items[hud.HitTest(restore.Rect.X + 1, restore.Rect.Y + 1)]);
        ui.Activate(restore);
        Assert.False(host.HudMinimized);
        Assert.True(ui.CornerPopover);
        hud = ui.BuildHud();
        var towers = hud.Items.First(i => i.Action == TdUiAction.QuickTowers);
        Assert.Equal(towers, hud.Items[hud.HitTest(towers.Rect.X + 1, towers.Rect.Y + 1)]);
        ui.Activate(towers);
        var pop = ui.BuildPopover()!;
        var tower = pop.Items.First(i => i.Action == TdUiAction.ChooseTower && i.Enabled);
        Assert.Equal(tower, pop.Items[pop.HitTest(tower.Rect.X + 1, tower.Rect.Y + 1)]);
        ui.Activate(tower);
        Assert.True(ui.BuildActive);
        Assert.True(ui.CornerPopover);
        Assert.False(ui.ModalOpen);
        ui.SceneLeftClick(300, 20, HitTarget.None);
        ui.Sync();
        Assert.Contains(ui.BuildPopover()!.Items, i => i.Text.Contains("Inicie a onda"));
        Assert.True(ui.CornerPopover);
    }

    [Fact]
    public void Placement_is_required_and_invalid_points_explain_why()
    {
        var store = new Store();
        var ui = Create(store);
        Assert.Equal(TdTutorialStep.Tour, ui.Step);
        Click(ui, TdUiAction.TutorialNext);
        Assert.Contains(ui.BuildPopover()!.Items, x => x.Text.Contains("primeira torre"));
        Click(ui, TdUiAction.QuickTowers);
        var tower = ui.BuildPopover()!.Items.First(x => x.Action == TdUiAction.ChooseTower && x.Enabled);
        Assert.True(tower.Highlight);
        ui.Activate(tower);
        ui.SceneLeftClick(0, 20, HitTarget.None);
        Assert.NotNull(ui.Notice);
        Click(ui, TdUiAction.TutorialNext);
        Assert.Equal(TdTutorialStep.Tour, ui.Step);
        ui.SceneLeftClick(300, 20, HitTarget.None);
        ui.Sync();
        Assert.Contains(ui.BuildPopover()!.Items, x => x.Text.Contains("Inicie a onda"));
    }

    [Fact]
    public void Skip_and_help_restart_preserve_completion()
    {
        var store = new Store();
        var ui = Create(store);
        Click(ui, TdUiAction.TutorialSkip);
        Assert.True(store.TutorialDone);
        Assert.Equal(TdTutorialStep.None, Create(store).Step);
        ui.OpenHowTo();
        Assert.Contains(ui.BuildPopover()!.Items, x => x.Action == TdUiAction.RedoTutorial);
        Click(ui, TdUiAction.RedoTutorial);
        Assert.False(store.TutorialDone);
        Assert.Equal(TdTutorialStep.Tour, ui.Step);
    }

    [Fact]
    public void Tour_and_help_describe_current_rules()
    {
        string content = string.Join(" ", TdTutorial.Pages.Select(p => p.Text + " " + p.Detail));
        foreach (string term in new[] { "clique", "faixa", "Imediato", "arrastando", "inimigo", "patente", "Júnior", "XP", "velocidade", "sombra", "engrenagem" })
            Assert.Contains(term, content, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(6, titles.Count);
        Assert.Contains(titles, t => t.StartsWith("RECEITAS DE FUSÃO 0/", StringComparison.Ordinal));
        Assert.Contains("FASE / SELO", titles);
    }
}
