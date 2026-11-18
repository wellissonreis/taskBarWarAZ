using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.30c: botão Runas no HUD, janela de runas ancorada, hover com balão (modal e janela), tipo oculto até despertar.</summary>
public sealed class TdRuneUiTests
{
    private const int Width = 1000;

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
        public bool StageUnlocked(int stageId) => stageId <= 2;
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

    private sealed class Rig
    {
        public readonly TdMatchState Match;
        public readonly List<IGameCommand> Sent = [];
        public readonly TdUiController Ui;
        public int Changes;

        public Rig()
        {
            Match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54), null, TdMatchOptions.Default);
            Match.Apply(new LayoutChanged(StripLayout.Create(Width, Width - 46, 88, 4, Width - 80, TowerHost.Band, [new FreeZone(32, Width - 32)])));
            Ui = new TdUiController(() => Match, c => { Sent.Add(c); Match.Apply(c); }, new Host(), () => 0.0);
            Ui.Changed += () => Changes++;
            Ui.Sync();
        }

        public void Fragments(long n) => typeof(TdMatchState).GetProperty("Fragments")!.SetValue(Match, n);
        public TdUiItem Rune => Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.RuneWindow);
        public void Click(TdUiAction a, int arg = 0) => Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", a, arg));
        public TdUiItem Node(Func<TdUiItem, bool> pick) => Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.SelectRune && pick(i));
    }

    [Fact]
    public void Rune_button_sits_in_the_chip_and_keeps_the_rest_width_within_220()
    {
        var r = new Rig();
        var hud = r.Ui.BuildHud();
        Assert.True(TdHudView.RestWidth <= 220f, $"{TdHudView.RestWidth}");
        var b = hud.Items.Single(i => i.Action == TdUiAction.RuneWindow);
        Assert.Equal(TdIcon.Star, b.Icon);
        Assert.True(b.Rect.X + b.Rect.W <= hud.Width + 0.01f);
        Assert.True(hud.OverButton(b.Rect.X + b.Rect.W / 2f, 14f));
        // sem Iniciar (corrida contínua em andamento) a largura é exatamente a de repouso
        r.Match.Apply(new SideWar.Core.Td.BuildTower("bolt", 600));
        r.Match.Apply(new StartWave());
        Assert.Equal(TdHudView.RestWidth, r.Ui.BuildHud().Width);
    }

    [Fact]
    public void Amber_dot_only_with_fragment_and_a_node_to_awaken()
    {
        var r = new Rig();
        Assert.Equal(0f, r.Rune.Fill);          // sem fragmento: sem ponto
        r.Fragments(1);
        Assert.True(r.Rune.Fill > 0f);          // fragmento + nó disponível
        r.Fragments(0);
        Assert.Equal(0f, r.Rune.Fill);
    }

    [Fact]
    public void Window_opens_and_closes_by_click_without_pausing_and_never_by_itself()
    {
        var r = new Rig();
        Assert.False(r.Ui.RuneWindowOpen);
        Assert.Null(r.Ui.BuildPopover());
        r.Fragments(3); r.Ui.Sync();
        Assert.False(r.Ui.RuneWindowOpen);       // fragmento disponível não abre nada
        r.Ui.Activate(r.Rune);
        Assert.True(r.Ui.RuneWindowOpen);
        Assert.True(r.Ui.CornerPopover);
        Assert.False(r.Ui.ModalOpen);
        Assert.False(r.Match.Paused);
        var pop = r.Ui.BuildPopover()!;
        Assert.True(pop.RuneWindow);
        Assert.Equal(TdUiController.RuneWindowWidth, pop.Width);
        Assert.Equal("Runas", pop.Title);
        Assert.Equal(32, pop.Items.Count(i => i.Action == TdUiAction.SelectRune));
        Assert.Contains(pop.Items, i => i.SArg == "rhintw" && i.Text == "Passe o mouse para ver; clique para despertar.");
        Assert.Contains(pop.Items, i => i.SArg == "rlink" && i.Text == "Teia completa");
        Assert.True(r.Rune.Selected);
        r.Ui.Activate(r.Rune);
        Assert.False(r.Ui.RuneWindowOpen);
        Assert.Null(r.Ui.BuildPopover());
    }

    [Fact]
    public void Window_closes_by_escape_close_button_boss_key_and_scene_right_click()
    {
        var r = new Rig();
        r.Ui.Activate(r.Rune);
        Assert.True(r.Ui.PopoverOpen);            // ESC fica registrado
        r.Ui.Cancel();
        Assert.False(r.Ui.RuneWindowOpen);
        r.Ui.Activate(r.Rune);
        r.Ui.Activate(r.Ui.BuildPopover()!.Items.Single(i => i.Action == TdUiAction.Close));
        Assert.False(r.Ui.RuneWindowOpen);
        r.Ui.Activate(r.Rune);
        r.Ui.SetBossHidden(true);
        Assert.False(r.Ui.RuneWindowOpen);
        Assert.Null(r.Ui.BuildPopover());
        r.Ui.SetBossHidden(false);
        Assert.False(r.Ui.RuneWindowOpen);        // o atalho não reabre a janela
        r.Ui.Activate(r.Rune);
        r.Ui.SceneRightClick();
        Assert.False(r.Ui.RuneWindowOpen);
    }

    [Fact]
    public void Window_and_tower_panel_are_exclusive_and_the_full_web_link_opens_the_modal_on_runes()
    {
        var r = new Rig();
        r.Click(TdUiAction.QuickTowers);
        Assert.True(r.Ui.QuickTowersOpen);
        r.Ui.Activate(r.Rune);
        Assert.True(r.Ui.RuneWindowOpen);
        Assert.False(r.Ui.QuickTowersOpen);
        r.Click(TdUiAction.QuickTowers);
        Assert.False(r.Ui.RuneWindowOpen);
        Assert.True(r.Ui.QuickTowersOpen);
        r.Ui.Activate(r.Rune);
        var link = r.Ui.BuildPopover()!.Items.Single(i => i.SArg == "rlink");
        r.Ui.Activate(link);
        Assert.Equal(TdUiMode.Runes, r.Ui.Mode);
        Assert.True(r.Ui.ModalOpen);
        Assert.False(r.Ui.RuneWindowOpen);
        Assert.True(r.Match.Paused);
    }

    [Fact]
    public void Hover_shows_and_hides_the_tooltip_in_the_window_and_in_the_modal()
    {
        var r = new Rig();
        r.Ui.Activate(r.Rune);
        Assert.Null(r.Ui.BuildPopover()!.Tip);
        var node = r.Node(i => i.Fill == TdUiController.RuneAvailable);
        int before = r.Changes;
        r.Ui.Hover(node);
        Assert.Equal(node.SArg, r.Ui.HoverRune);
        Assert.True(r.Changes > before);
        Assert.Equal(node.SArg, r.Ui.BuildPopover()!.Tip!.NodeId);
        int again = r.Changes;
        r.Ui.Hover(node);                          // mesmo nó: sem redesenho
        Assert.Equal(again, r.Changes);
        r.Ui.Hover(null);
        Assert.Null(r.Ui.BuildPopover()!.Tip);
        // modal
        r.Click(TdUiAction.ModalTab, (int)TdUiMode.Runes);
        var m = r.Node(i => i.Fill == TdUiController.RuneLocked);
        r.Ui.Hover(m);
        var tip = r.Ui.BuildPopover()!.Tip!;
        Assert.Equal(TdRuneState.Locked, tip.State);
        Assert.Equal("BLOQUEADA", tip.Tag);
        r.Ui.Hover(null);
        Assert.Null(r.Ui.BuildPopover()!.Tip);
    }

    [Fact]
    public void Tooltip_texts_follow_the_design_for_each_state()
    {
        var r = new Rig();
        r.Click(TdUiAction.ModalTab, (int)TdUiMode.Runes);
        var avail = r.Node(i => i.Fill == TdUiController.RuneAvailable);
        r.Ui.Hover(avail);
        var t = r.Ui.BuildPopover()!.Tip!;
        Assert.Equal(("DISPONÍVEL", "Sem fragmentos"), (t.Tag, t.Action));
        Assert.StartsWith("Runa de ", t.Name);
        Assert.Equal(TdTone.Bad, t.ActionTone);
        r.Fragments(2);
        t = r.Ui.BuildPopover()!.Tip!;
        Assert.Equal("Clique para despertar · 1 fragmento", t.Action);
        Assert.Equal(TdTone.Accent, t.ActionTone);
        r.Ui.Activate(avail);
        Assert.Contains(r.Sent, c => c is AwakenRune a && a.NodeId == avail.SArg); // o nó clicado, não um sorteado
        var on = r.Node(i => i.Fill == TdUiController.RuneOn);
        r.Ui.Hover(on);
        t = r.Ui.BuildPopover()!.Tip!;
        Assert.Equal(("DESPERTADA", "Efeito ativo", TdTone.Good), (t.Tag, t.Action, t.ActionTone));
        Assert.StartsWith("Runa de ", t.Name);
    }

    [Fact]
    public void Rune_type_is_visible_in_every_state_in_the_window_and_in_the_modal()
    {
        var r = new Rig();
        var types = r.Match.Content.Runes.Types;
        r.Fragments(1);
        r.Ui.Activate(r.Rune);
        r.Ui.Activate(r.Node(i => i.Fill == TdUiController.RuneAvailable)); // um desperto, para ver os três estados
        foreach (var scene in new[] { "window", "modal" })
        {
            if (scene == "modal") r.Click(TdUiAction.ModalTab, (int)TdUiMode.Runes);
            foreach (var fill in new[] { TdUiController.RuneOn, TdUiController.RuneAvailable, TdUiController.RuneLocked })
            {
                var node = r.Node(i => i.Fill == fill);
                int ti = TdUiController.UnpackRune(node.Arg).Type;
                var type = types[ti];
                var def = r.Match.RuneWeb.Single(n => n.Id == node.SArg);
                Assert.Equal(def.Type, type.Id);                 // o item carrega o ramo REAL do nó, em qualquer estado
                Assert.StartsWith(type.Name + ", ", node.Text);
                r.Ui.Hover(node);
                var tip = r.Ui.BuildPopover()!.Tip!;
                Assert.Equal(ti, tip.TypeIndex);
                Assert.Equal("Runa de " + type.Name, tip.Name);
                Assert.Equal(type.Text, tip.Text);               // nome do ramo + efeito sempre
                r.Ui.Hover(null);
            }
        }
    }

    [Fact]
    public void Tooltip_has_state_tag_and_action_for_each_state_and_no_hidden_texts()
    {
        var r = new Rig();
        r.Click(TdUiAction.ModalTab, (int)TdUiMode.Runes);
        r.Ui.Hover(r.Node(i => i.Fill == TdUiController.RuneLocked));
        var t = r.Ui.BuildPopover()!.Tip!;
        Assert.Equal(("BLOQUEADA", "Desperte uma vizinha antes", TdTone.Dim), (t.Tag, t.Action, t.ActionTone));
        r.Ui.Hover(r.Node(i => i.Fill == TdUiController.RuneAvailable));
        t = r.Ui.BuildPopover()!.Tip!;
        Assert.Equal(("DISPONÍVEL", "Sem fragmentos"), (t.Tag, t.Action));
        r.Fragments(1);
        Assert.Equal("Clique para despertar · 1 fragmento", r.Ui.BuildPopover()!.Tip!.Action);
        foreach (var it in r.Ui.BuildPopover()!.Items)
        {
            Assert.DoesNotContain("oculta", it.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("revelado", it.Text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("Runa oculta", t.Name);
    }

    [Fact]
    public void Legend_lists_every_branch_with_awakened_over_total_in_the_modal_and_in_the_window()
    {
        var r = new Rig();
        var types = r.Match.Content.Runes.Types;
        r.Fragments(3);
        r.Ui.Activate(r.Rune);
        var avail = r.Node(i => i.Fill == TdUiController.RuneAvailable);
        r.Ui.Activate(avail);
        string awakeType = r.Match.RuneWeb.Single(n => n.Id == avail.SArg).Type;
        foreach (var scene in new[] { "window", "modal" })
        {
            if (scene == "modal") r.Click(TdUiAction.ModalTab, (int)TdUiMode.Runes);
            var pop = r.Ui.BuildPopover()!;
            var chips = pop.Items.Where(i => i.Action == TdUiAction.RuneBranch).ToArray();
            Assert.Equal(types.Length, chips.Length);
            foreach (var c in chips)
            {
                int total = r.Match.RuneWeb.Count(n => n.Type == c.SArg);
                int awake = c.SArg == awakeType ? 1 : 0;
                Assert.Equal($"{awake}/{total}", c.Right);
                Assert.Equal(types[c.Arg].Id, c.SArg);
                Assert.Equal(types[c.Arg].Name, c.Text);
                Assert.True(c.Rect.X >= 0 && c.Rect.X + c.Rect.W <= pop.Width && c.Rect.Y + c.Rect.H <= pop.Height, scene);
            }
            Assert.Equal(32, chips.Sum(c => int.Parse(c.Right!.Split((char)47)[1], System.Globalization.CultureInfo.InvariantCulture)));
            Assert.All(pop.Items.Where(i => i.Action != TdUiAction.RuneBranch).Select(i => i.Rect),
                rc => Assert.True(rc.X + rc.W <= pop.Width + 0.01f && rc.Y + rc.H <= pop.Height + 0.01f));
        }
    }

    [Fact]
    public void Hovering_a_legend_chip_highlights_only_that_branch_nodes_and_clears_on_leave()
    {
        var r = new Rig();
        foreach (var scene in new[] { "window", "modal" })
        {
            if (scene == "window") r.Ui.Activate(r.Rune); else r.Click(TdUiAction.ModalTab, (int)TdUiMode.Runes);
            var chip = r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.RuneBranch && i.SArg == "frost");
            Assert.All(r.Ui.BuildPopover()!.Items.Where(i => i.Action == TdUiAction.SelectRune), n => Assert.False(n.Selected));
            int before = r.Changes;
            r.Ui.Hover(chip);
            Assert.True(r.Changes > before);
            Assert.Equal("frost", r.Ui.HoverBranch);
            var pop = r.Ui.BuildPopover()!;
            var frostIds = r.Match.RuneWeb.Where(n => n.Type == "frost").Select(n => n.Id).ToHashSet();
            Assert.NotEmpty(frostIds);
            foreach (var n in pop.Items.Where(i => i.Action == TdUiAction.SelectRune)) Assert.Equal(frostIds.Contains(n.SArg!), n.Selected);
            Assert.True(pop.Items.Single(i => i.Action == TdUiAction.RuneBranch && i.SArg == "frost").Selected);
            r.Ui.Hover(null);
            Assert.Null(r.Ui.HoverBranch);
            Assert.All(r.Ui.BuildPopover()!.Items.Where(i => i.Action == TdUiAction.SelectRune), n => Assert.False(n.Selected));
        }
    }

    [Fact]
    public void Nodes_have_hit_areas_large_enough_to_hover_and_visual_size_in_fontsize()
    {
        var r = new Rig();
        r.Ui.Activate(r.Rune);
        var pop = r.Ui.BuildPopover()!;
        foreach (var n in pop.Items.Where(i => i.Action == TdUiAction.SelectRune))
        {
            Assert.True(n.Rect.W >= 16f && n.FontSize > 0f && n.Rect.W >= n.FontSize);
            Assert.Equal(pop.Items.ToList().IndexOf(n), pop.HitTest(n.Rect.X + n.Rect.W / 2f, n.Rect.Y + n.Rect.H / 2f));
            Assert.True(n.Rect.X >= 0 && n.Rect.X + n.Rect.W <= pop.Width);
        }
    }

    [Fact]
    public void Arrow_points_at_the_rune_button()
    {
        var r = new Rig();
        r.Ui.Activate(r.Rune);
        var pop = r.Ui.BuildPopover()!;
        var hud = r.Ui.BuildHud();
        var b = hud.Items.Single(i => i.Action == TdUiAction.RuneWindow);
        Assert.True(pop.HasArrow);
        Assert.Equal(pop.Width - (hud.Width - (b.Rect.X + b.Rect.W / 2f)), pop.ArrowX, 0.01f);
    }

    [Fact]
    public void Tooltip_layout_flips_vertically_and_clamps_horizontally_inside_the_window()
    {
        const float W = 288f, H = 250f, w = 224f, h = 100f;
        // nó no topo: abaixo; embaixo: acima
        var top = TdRuneTipLayout.Place(140, 50, 22, w, h, W, H, 4);
        Assert.Equal(TdTipSide.Below, top.Side);
        var bottom = TdRuneTipLayout.Place(140, 200, 22, w, h, W, H, 4);
        Assert.Equal(TdTipSide.Above, bottom.Side);
        // borda esquerda e direita: o balão fica dentro da janela
        foreach (float nx in new[] { 3f, 285f })
            foreach (float ny in new[] { 10f, 125f, 240f })
            {
                var p = TdRuneTipLayout.Place(nx, ny, 22, w, h, W, H, 4);
                Assert.InRange(p.X, 4f, W - w - 4f);
                Assert.InRange(p.Y, 4f, H - h - 4f);
            }
        // nó no meio-alto, mas sem espaço abaixo: vira para cima
        var flipUp = TdRuneTipLayout.Place(140, 120, 22, w, 140, W, 200, 4);
        Assert.Equal(TdTipSide.Above, flipUp.Side);
        // sem espaço vertical nenhum: lado direito ou esquerdo
        var side = TdRuneTipLayout.Place(20, 60, 22, 100, 110, 288, 120, 4);
        Assert.Equal(TdTipSide.Right, side.Side);
        var left = TdRuneTipLayout.Place(268, 60, 22, 100, 110, 288, 120, 4);
        Assert.Equal(TdTipSide.Left, left.Side);
    }

    [Fact]
    public void Hit_test_is_transparent_outside_the_buttons_with_the_rune_button()
    {
        var r = new Rig();
        var hud = r.Ui.BuildHud();
        var b = hud.Items.Single(i => i.Action == TdUiAction.RuneWindow);
        var neighbour = hud.Items.Single(i => i.Action == TdUiAction.QuickTowers);
        float gap = (b.Rect.X + neighbour.Rect.X + neighbour.Rect.W) / 2f;
        Assert.False(hud.OverButton(gap, 14f));
        Assert.False(hud.OverButton(b.Rect.X + 4f, -3f));
        Assert.True(hud.OverButton(b.Rect.X + 4f, 14f));
    }

    [Fact]
    public void A_new_match_closes_the_window_and_clears_the_hover()
    {
        var r = new Rig();
        r.Ui.Activate(r.Rune);
        r.Ui.Hover(r.Node(_ => true));
        var next = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(9, 1), null, TdMatchOptions.Default);
        next.Apply(new LayoutChanged(StripLayout.Create(Width, Width - 46, 88, 4, Width - 80, TowerHost.Band, [new FreeZone(32, Width - 32)])));
        var ui = new TdUiController(() => next, _ => { }, new Host(), () => 0.0);
        ui.Sync();
        Assert.False(ui.RuneWindowOpen);
        Assert.Null(ui.HoverRune);
    }
}
