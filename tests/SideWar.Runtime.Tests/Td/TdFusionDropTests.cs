using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Input;
using BuildTower = SideWar.Core.Td.BuildTower;
using SideWar.Runtime.Td;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>Fusão por soltar (escolher a torre na barra e soltar sobre outra), rolagem da modal e tutorial atualizado.</summary>
public sealed class TdFusionDropTests
{
    private sealed class Host : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
        public void RequestExit() { }
        public bool HudMinimized { get; set; } = true;
        public void SetHudMinimized(bool minimized) => HudMinimized = minimized;
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
        public bool PurchaseNode(string nodeId) => true;
    }

    private sealed class Store : ITdOnboardingStore
    {
        public readonly HashSet<string> Seen = [];
        public bool Done;
        public bool TutorialDone => Done;
        public void SetTutorialDone(bool done) => Done = done;
        public bool TipSeen(string id) => Seen.Contains(id);
        public void MarkTipSeen(string id) => Seen.Add(id);
        public void ClearTip(string id) => Seen.Remove(id);
    }

    private sealed class Rig
    {
        public readonly TdMatchState Match;
        public readonly List<IGameCommand> Sent = [];
        public readonly Store Store = new() { Done = true };
        public readonly Host Host = new();
        public readonly TdUiController Ui;
        public int Tower;

        public Rig(int stage = 6, long credits = 5000, bool onboarding = false)
        {
            Match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), stage, Rng.Seed(7, 54), null, TdMatchOptions.Classic with { InitialReputation = TdRank.Threshold((TdRankLevel)(stage - 1)) });
            Match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
            typeof(TdMatchState).GetProperty("Credits")!.SetValue(Match, credits);
            Ui = new TdUiController(() => Match, c => { Sent.Add(c); Match.Apply(c); }, Host, () => 0.0, onboarding ? Store : null);
            Assert.True(Match.Apply(new BuildTower("frost", 500)).IsOk);
            Tower = Match.Towers[0].Id;
            Ui.Sync();
        }

        public void Credits(long v) => typeof(TdMatchState).GetProperty("Credits")!.SetValue(Match, v);
        public float TowerX() => TdBand.TowerCenterDip(Match, Match.Towers.First(t => t.Id == Tower), TdBand.WidthDip(Match));

        public void Pick(string type)
        {
            if (!Ui.QuickTowersOpen) Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.QuickTowers));
            Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ChooseTower, SArg: type));
        }
    }

    [Fact]
    public void Dropping_a_picked_tower_on_a_valid_pair_fuses_it_and_charges_the_same_cost()
    {
        var r = new Rig();
        long before = r.Match.Credits;
        var check = r.Match.CheckFusion(r.Tower, "bolt");
        Assert.True(check.IsOk);
        r.Pick("bolt");
        r.Ui.PointerMoved(r.TowerX());
        Assert.Equal(r.Tower, r.Ui.View.FuseTargetId);
        Assert.True(r.Ui.View.FuseTargetOk);
        Assert.True(r.Ui.View.GhostValid);
        Assert.Equal("bolt", r.Ui.View.FuseModuleId);
        var title = r.Ui.BuildPopover()!.Title;
        Assert.Contains(check.Name, title);
        Assert.Contains($"${check.Cost}", title);

        Assert.True(r.Ui.SceneLeftClick(r.TowerX(), 20f, HitTarget.None));
        Assert.Contains(r.Sent, c => c is FuseTower f && f.BaseId == r.Tower && f.ComponentTypeId == "bolt");
        Assert.DoesNotContain(r.Sent, c => c is BuildTower b && b.TowerId == "bolt");
        Assert.True(r.Match.Towers[0].HasModule);
        Assert.Equal(before - check.Cost, r.Match.Credits);
        Assert.Equal(r.Tower, r.Ui.View.FuseFxId);
        Assert.True(r.Ui.View.FuseFxAge >= 0f);
    }

    [Fact]
    public void Dropping_on_an_invalid_pair_shows_a_short_reason_and_does_nothing()
    {
        var r = new Rig(stage: 1); // Estagiário funde 2: a terceira torre é recusada
        Assert.True(r.Match.Apply(new FuseTower(r.Tower, "bolt")).IsOk);
        long before = r.Match.Credits;
        int sent = r.Sent.Count;
        r.Pick("bolt");
        r.Ui.PointerMoved(r.TowerX());
        Assert.Equal(r.Tower, r.Ui.View.FuseTargetId);
        Assert.False(r.Ui.View.FuseTargetOk);
        Assert.False(r.Ui.View.GhostValid);
        Assert.Contains("Júnior", r.Ui.BuildPopover()!.Title);
        Assert.True(r.Ui.SceneLeftClick(r.TowerX(), 20f, HitTarget.None));
        Assert.Equal(sent, r.Sent.Count);
        Assert.Equal(before, r.Match.Credits);
        Assert.Equal(2, r.Match.Towers[0].ComponentTotal);
        Assert.True(r.Ui.BuildActive); // segue escolhendo
    }

    [Fact]
    public void Dropping_without_credits_is_refused_with_the_missing_amount()
    {
        var r = new Rig();
        r.Credits(1);
        int sent = r.Sent.Count;
        r.Pick("bolt");
        r.Ui.PointerMoved(r.TowerX());
        Assert.False(r.Ui.View.GhostValid);
        Assert.Contains("faltam", r.Ui.BuildPopover()!.Title);
        r.Ui.SceneLeftClick(r.TowerX(), 20f, HitTarget.None);
        Assert.Equal(sent, r.Sent.Count);
        Assert.False(r.Match.Towers[0].HasModule);
        Assert.Equal(1, r.Match.Credits);
    }

    [Fact]
    public void Moving_off_the_tower_returns_to_normal_placement_and_a_fused_tower_takes_a_third()
    {
        var r = new Rig();
        r.Pick("bolt");
        r.Ui.PointerMoved(r.TowerX() + 200f);
        Assert.Equal(0, r.Ui.View.FuseTargetId);
        Assert.True(r.Ui.View.GhostValid);
        r.Ui.PointerMoved(r.TowerX());
        r.Ui.SceneLeftClick(r.TowerX(), 20f, HitTarget.None);
        Assert.True(r.Match.Towers[0].HasModule);
        r.Pick("cannon");
        r.Ui.PointerMoved(r.TowerX());
        Assert.True(r.Ui.View.FuseTargetOk);
        Assert.Contains("3/6: Zona de Disponibilidade", r.Ui.BuildPopover()!.Title);
    }

    // ---------------------------------------------------------------- arrastar uma torre já colocada

    [Fact]
    public void Dragging_onto_a_hybrid_shows_recipe_and_count_then_adds_the_third_tower()
    {
        var r = new Rig(stage: 2); // Júnior: até 3 torres
        Assert.True(r.Match.Apply(new FuseTower(r.Tower, "bolt")).IsOk);
        var (id, x2) = AddSecond(r, "cannon");
        Assert.True(r.Ui.BeginTowerDrag(id, x2));
        r.Ui.TowerDragMoved(r.TowerX());
        Assert.True(r.Ui.View.FuseTargetOk);
        var card = r.Ui.BuildPopover()!;
        Assert.Equal("Soltar funde", card.Title);
        Assert.Contains(card.Items, i => i.Text == "Zona de Disponibilidade");
        Assert.Contains(card.Items, i => i.Text.Contains("3 de 3 torres", StringComparison.Ordinal));
        Assert.Contains(card.Items, i => i.Text.StartsWith("Receita:", StringComparison.Ordinal));
        r.Ui.TowerDragEnd(r.TowerX());
        var t = Assert.Single(r.Match.Towers);
        Assert.Equal(3, t.ComponentTotal);
        r.Ui.Sync();
        Assert.Equal("Torre fundida", r.Ui.BuildPopover()!.Title);
    }

    [Fact]
    public void Dragging_above_the_rank_limit_explains_the_needed_rank_and_changes_nothing()
    {
        var r = new Rig(stage: 1);
        Assert.True(r.Match.Apply(new FuseTower(r.Tower, "bolt")).IsOk);
        var (id, x2) = AddSecond(r, "dns");
        int sent = r.Sent.Count;
        Assert.True(r.Ui.BeginTowerDrag(id, x2));
        r.Ui.TowerDragMoved(r.TowerX());
        var card = r.Ui.BuildPopover()!;
        Assert.Equal("Fusão bloqueada", card.Title);
        Assert.Contains(card.Items, i => i.Text.Contains("exigem Júnior", StringComparison.Ordinal));
        r.Ui.TowerDragEnd(r.TowerX());
        Assert.Equal(sent, r.Sent.Count);
        Assert.Equal(2, r.Match.Towers.Count);
    }

    private static (int Id, float X) AddSecond(Rig r, string type = "bolt", int logical = 760)
    {
        Assert.True(r.Match.Apply(new BuildTower(type, logical)).IsOk);
        r.Ui.Sync();
        var t = r.Match.Towers[^1];
        return (t.Id, TdBand.TowerCenterDip(r.Match, t, TdBand.WidthDip(r.Match)));
    }

    [Fact]
    public void Dragging_a_placed_tower_onto_another_previews_and_fuses_for_free()
    {
        var r = new Rig();
        var (id, x2) = AddSecond(r);
        long credits = r.Match.Credits;
        Assert.True(r.Ui.BeginTowerDrag(id, x2));
        r.Ui.TowerDragMoved(r.TowerX());
        Assert.True(r.Ui.DraggingTower);
        Assert.Equal(r.Tower, r.Ui.View.FuseTargetId);
        Assert.True(r.Ui.View.GhostValid);
        Assert.True(r.Ui.View.FuseTargetOk);
        r.Ui.TowerDragEnd(r.TowerX());
        Assert.Contains(r.Sent, c => c is FuseTowers f && f.TargetId == r.Tower && f.SourceId == id);
        Assert.Single(r.Match.Towers);
        Assert.True(r.Match.Towers[0].HasModule);
        Assert.Equal(credits, r.Match.Credits);
        Assert.False(r.Ui.DraggingTower);
        Assert.True(r.Ui.View.FuseFxId > 0);
    }

    [Fact]
    public void Dropping_a_dragged_tower_outside_or_on_an_invalid_pair_leaves_everything_in_place()
    {
        var r = new Rig();
        var (id, x2) = AddSecond(r);
        r.Ui.BeginTowerDrag(id, x2);
        r.Ui.TowerDragMoved(x2 - 120f);
        Assert.Equal(0, r.Ui.View.FuseTargetId);
        r.Ui.TowerDragEnd(x2 - 120f);
        Assert.DoesNotContain(r.Sent, c => c is FuseTowers);
        Assert.Equal(2, r.Match.Towers.Count);

        var early = new Rig(stage: 1); // já no limite de 2 do Estagiário
        Assert.True(early.Match.Apply(new FuseTower(early.Tower, "bolt")).IsOk);
        var (id2, x3) = AddSecond(early);
        early.Ui.BeginTowerDrag(id2, x3);
        early.Ui.TowerDragMoved(early.TowerX());
        Assert.False(early.Ui.View.GhostValid);
        early.Ui.TowerDragEnd(early.TowerX());
        Assert.DoesNotContain(early.Sent, c => c is FuseTowers);
        Assert.Equal(2, early.Match.Towers.Count);
    }

    [Fact]
    public void Dragging_a_placed_tower_to_a_free_valid_spot_moves_it_and_to_an_invalid_one_keeps_it()
    {
        var r = new Rig();
        var (id, x2) = AddSecond(r);
        int fromX = r.Match.Towers.First(t => t.Id == id).X;
        long credits = r.Match.Credits;
        Assert.True(r.Ui.BeginTowerDrag(id, x2));
        r.Ui.TowerDragMoved(x2 - 120f);
        Assert.True(r.Ui.View.GhostValid);
        Assert.True(r.Ui.View.GhostX >= 0);
        r.Ui.TowerDragEnd(x2 - 120f);
        Assert.Contains(r.Sent, c => c is RelocateTower rt && rt.TowerId == id);
        Assert.Equal(2, r.Match.Towers.Count);
        Assert.NotEqual(fromX, r.Match.Towers.First(t => t.Id == id).X);
        Assert.Equal(credits, r.Match.Credits);

        // fora da zona livre: volta ao lugar, nada enviado
        int sent = r.Sent.Count;
        int x = r.Match.Towers.First(t => t.Id == id).X;
        r.Ui.BeginTowerDrag(id, x2);
        r.Ui.TowerDragMoved(5f);
        Assert.False(r.Ui.View.GhostValid);
        r.Ui.TowerDragEnd(5f);
        Assert.Equal(sent, r.Sent.Count);
        Assert.Equal(x, r.Match.Towers.First(t => t.Id == id).X);
    }

    [Fact]
    public void A_simple_press_and_release_on_a_tower_still_opens_the_inspector_and_esc_cancels_a_drag()
    {
        var r = new Rig();
        Assert.True(r.Ui.SceneLeftClick(r.TowerX(), 20f, HitTarget.None)); // pressionar começa o arrasto
        r.Ui.SceneLeftUp(r.TowerX());                                         // sem mover = clique
        Assert.Equal(TdUiMode.Inspector, r.Ui.Mode);
        Assert.Equal(r.Tower, r.Ui.SelectedTowerId);
        r.Ui.Cancel();

        var (id, x2) = AddSecond(r);
        r.Ui.BeginTowerDrag(id, x2);
        r.Ui.TowerDragMoved(r.TowerX());
        r.Ui.Cancel();
        Assert.False(r.Ui.DraggingTower);
        r.Ui.SceneLeftUp(r.TowerX());
        Assert.DoesNotContain(r.Sent, c => c is FuseTowers);
    }

    [Fact]
    public void CanGrabTowerAt_is_true_only_over_a_placed_tower_while_playing_and_not_placing()
    {
        var r = new Rig();
        float x = r.TowerX();
        Assert.True(r.Ui.CanGrabTowerAt(x, 20f));                 // sobre a torre colocada
        Assert.True(r.Ui.CanGrabTowerAt(x + 19f, 5f));            // borda da área de acerto
        Assert.False(r.Ui.CanGrabTowerAt(x + 21f, 20f));          // ao lado: o clique passa
        Assert.False(r.Ui.CanGrabTowerAt(100f, 20f));             // espaço livre
        Assert.False(r.Ui.CanGrabTowerAt(x, 40f));                // fora da faixa
        Assert.False(r.Ui.CanGrabTowerAt(float.NaN, 20f));
        r.Pick("bolt");                                           // modo posicionar: a cena já é interativa por outro caminho
        Assert.False(r.Ui.CanGrabTowerAt(x, 20f));
    }

    // ---------------------------------------------------------------- rolagem

    private static Rig WithTowers(int n)
    {
        var r = new Rig();
        string[] ids = [.. r.Match.Content.Towers.Where(t => t.MinRank <= r.Match.CurrentRankId).Select(t => t.Id)];
        for (int x = 100, k = 0; r.Match.Towers.Count < n && x < 960; x += 80, k++)
            r.Match.Apply(new BuildTower(ids[k % ids.Length], x));
        Assert.Equal(n, r.Match.Towers.Count);
        r.Ui.Sync();
        return r;
    }

    [Fact]
    public void Towers_tab_scrolls_by_wheel_and_keyboard_without_arrow_buttons()
    {
        var r = WithTowers(9);
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Towers));
        var v = r.Ui.BuildPopover()!;
        Assert.Equal(new TdScrollInfo(9, TdUiController.ModalTowerRows, 0, 132f, TdUiController.ModalTowerRows * 52f - 6f), v.Scroll);
        Assert.DoesNotContain(v.Items, i => i.Action is TdUiAction.TowerPage or TdUiAction.BuildCatalogPage);
        Assert.Equal(TdUiController.ModalTowerRows, v.Items.Count(i => i.Action == TdUiAction.OpenTower));

        Assert.True(r.Ui.ScrollBy(1));
        Assert.Equal(1, r.Ui.BuildPopover()!.Scroll!.Value.Offset);
        r.Ui.ScrollBy(100);
        Assert.Equal(3, r.Ui.BuildPopover()!.Scroll!.Value.Offset); // 9 - 6
        r.Ui.ScrollBy(-100);
        Assert.Equal(0, r.Ui.BuildPopover()!.Scroll!.Value.Offset);
        r.Ui.ScrollKey(1); // teclado: uma página, travada no fim
        Assert.Equal(3, r.Ui.BuildPopover()!.Scroll!.Value.Offset);
        r.Ui.ScrollKey(-1);
        Assert.Equal(0, r.Ui.BuildPopover()!.Scroll!.Value.Offset);
        var (y, h) = r.Ui.BuildPopover()!.Scroll!.Value.Thumb();
        Assert.Equal(132f, y);
        Assert.InRange(h, 16f, 52f * 6f);
    }

    [Fact]
    public void Help_scrolls_while_short_tower_list_does_not()
    {
        var r = new Rig(onboarding: true);
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Towers));
        Assert.Null(r.Ui.BuildPopover()!.Scroll);
        Assert.False(r.Ui.ScrollBy(1));
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Help));
        Assert.True(r.Ui.ScrollKey(1));
        Assert.True(r.Ui.BuildPopover()!.Scroll!.Value.Offset > 0);
        Assert.True(r.Ui.ScrollBy(100));
        var bottom = r.Ui.BuildPopover()!;
        Assert.Contains(bottom.Items, i => i.Text.Contains("Recomeçar do zero"));
        Assert.Contains(bottom.Items, i => i.Action == TdUiAction.RedoTutorial);
        Assert.True(r.Ui.ScrollBy(-100));
        Assert.Equal(0, r.Ui.BuildPopover()!.Scroll!.Value.Offset);
    }

    // ---------------------------------------------------------------- tutorial

    [Fact]
    public void Help_card_covers_hud_fusion_runes_and_ranks_and_offers_the_tutorial_again()
    {
        var r = new Rig(onboarding: true);
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Help));
        var texts = new List<string>();
        for (int i = 0; i < 30; i++)
        {
            var page = r.Ui.BuildPopover()!;
            texts.AddRange(page.Items.Select(item => item.Text));
            var scroll = page.Scroll!.Value;
            if (scroll.Offset == scroll.Total - scroll.Visible) break;
            r.Ui.ScrollBy(1);
        }
        Assert.Contains(texts, t => t.Contains("HUD"));
        Assert.Contains(texts, t => t.Contains("fusão"));
        Assert.Contains(texts, t => t.Contains("runas"));
        Assert.Contains(texts, t => t.Contains("Patentes"));
        var redo = r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.RedoTutorial);
        r.Ui.Activate(redo);
        Assert.False(r.Store.Done);
    }

    private sealed class Discovered : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
        public void RequestExit() { }
        public bool HudMinimized { get; set; } = true;
        public void SetHudMinimized(bool minimized) => HudMinimized = minimized;
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
        public bool PurchaseNode(string nodeId) => true;
        public int EncounterCount(string id) => id == "recipe:zona-disponibilidade" ? 1 : 0;
    }

    [Fact]
    public void Help_lists_only_the_discovered_recipes_and_counts_the_hidden_ones()
    {
        var r = new Rig();
        var ui = new TdUiController(() => r.Match, _ => { }, new Discovered(), () => 0.0, null);
        ui.OpenHowTo();
        var items = new List<TdUiItem>();
        for (int i = 0; i < 60; i++)
        {
            var page = ui.BuildPopover()!;
            items.AddRange(page.Items);
            if (!ui.ScrollBy(1)) break;
        }
        Assert.Contains(items, i => i.Text == "Zona de Disponibilidade" && i.Sub == "Raio+Gelo+Canhão");
        Assert.Contains(items, i => i.Text.StartsWith("??? ", StringComparison.Ordinal));
        Assert.DoesNotContain(items, i => i.Text == "Zero Trust");
    }

    [Fact]
    public void Tutorial_explains_rank_gate_for_fusion()
    {
        Assert.Contains(TdTutorial.Pages, p => p.Text.Contains("Patentes") && p.Detail.Contains("Júnior"));
    }

    [Fact]
    public void Fusion_tip_text_describes_the_drop_gesture()
    {
        Assert.Contains("solte sobre outra", TdUiController.FusionTipText);
        Assert.DoesNotContain("não junta", TdUiController.FusionTipText);
    }
}
