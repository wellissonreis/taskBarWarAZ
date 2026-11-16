using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Input;
using SideWar.Runtime.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using SetTargeting = SideWar.Core.Td.SetTargeting;
using UpgradeTower = SideWar.Core.Td.UpgradeTower;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.8: fluxo de UI da partida TD (construção, inspetor, HUD, fim de partida) sobre o controlador puro.</summary>
public sealed class TdUiControllerTests
{
    private const int Width = 1000;
    private static readonly string[] Affordable = ["bolt", "frost", "cannon"];

    private sealed class FakeHost : ITdUiHost
    {
        public bool Paused { get; set; }
        public List<string> Calls { get; } = [];
        public void TogglePause() => Calls.Add("pause");
        public void OpenSettings() => Calls.Add("settings");
        public void RequestExit() => Calls.Add("exit");
        public void StartStage(int stageId) => Calls.Add($"stage{stageId}");
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => stageId <= 2;
        public int StageStars(int stageId) => stageId == 1 ? 2 : 0;
        public bool StageCrown(int stageId) => stageId == 1;
        public int StageMedals(int stageId) => stageId == 1 ? 5 : 0;
        public int StageBestScore(int stageId) => stageId == 1 ? 4123 : 0;
        public SideWar.Core.Td.TdRankProgress Rank => SideWar.Core.Td.TdRank.Progress(7000);
        public SideWar.Core.Td.TdRecordOutcome LastOutcome { get; set; }
        public int SealsAvailable { get; set; } = 3;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) { Calls.Add("buy:" + nodeId); return true; }
    }

    private sealed class Rig
    {
        public TdMatchState Match = Create(1);
        public readonly List<IGameCommand> Sent = [];
        public readonly FakeHost Host = new();
        public double Now;
        public TdUiController Ui;

        public Rig()
        {
            Ui = new TdUiController(() => Match, Send, Host, () => Now);
        }

        private void Send(IGameCommand c)
        {
            Sent.Add(c);
            if (c is TdWaveRequest) Match.Apply(Match.Phase == MatchPhase.Preparo ? new StartWave() : new CallNextWave());
            else Match.Apply(c);
        }
    }

    private static TdMatchState Create(int stage)
    {
        var m = TdMatchState.Create(TdContentLoader.LoadEmbedded(), stage, Rng.Seed(7, 54));
        m.Apply(new LayoutChanged(StripLayout.Create(Width, Width - 46, 88, 4, Width - 80, TowerHost.Band, [new FreeZone(32, Width - 32)])));
        return m;
    }

    private static TdUiItem Find(TdPopoverView v, TdUiAction a, string? sarg = null) =>
        v.Items.First(i => i.Action == a && (sarg is null || i.SArg == sarg));

    [Fact]
    public void Click_on_free_point_opens_chooser_and_confirm_sends_BuildTower()
    {
        var r = new Rig();
        r.Ui.SceneLeftClick(300, 20, HitTarget.None);
        Assert.Equal(TdUiMode.Build, r.Ui.Mode);
        var pop = r.Ui.BuildPopover()!;
        Assert.Contains(pop.Items, i => i.Action == TdUiAction.ChooseTower && i.SArg == "bolt" && i.Enabled);
        Assert.Contains(pop.Items, i => i.SArg == "balancer" && !i.Enabled); // fase 2
        r.Ui.Activate(Find(pop, TdUiAction.ChooseTower, "bolt"));
        var build = Assert.IsType<BuildTower>(r.Sent.Single());
        Assert.Equal("bolt", build.TowerId);
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
        Assert.Single(r.Match.Towers);
    }

    [Fact]
    public void Click_outside_zone_does_nothing_and_cancel_sends_no_command()
    {
        var r = new Rig();
        r.Ui.SceneLeftClick(2, 20, HitTarget.None); // fora da zona livre (32..968)
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "TORRE", TdUiAction.BuildToggle));
        Assert.True(r.Ui.ChoosingPoint);
        r.Ui.PointerMoved(2);
        Assert.False(r.Ui.View.GhostX < 0);
        Assert.False(r.Ui.View.GhostValid);
        r.Ui.SceneLeftClick(2, 20, HitTarget.None);
        Assert.True(r.Ui.ChoosingPoint); // clique inválido não avança
        r.Ui.Cancel();
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
        Assert.Empty(r.Sent);
    }

    [Fact]
    public void Build_mode_times_out_after_30_seconds()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "TORRE", TdUiAction.BuildToggle));
        r.Now = 10;
        r.Ui.Sync();
        Assert.True(r.Ui.BuildActive);
        r.Now = 31;
        r.Ui.Sync();
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
    }

    [Fact]
    public void Tower_limit_makes_the_ghost_invalid()
    {
        var r = new Rig();
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(r.Match, 5000L);
        for (int i = 0; i < r.Match.MaxTowers; i++) r.Match.Apply(new BuildTower("bolt", 100 + i * 140));
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "TORRE", TdUiAction.BuildToggle));
        r.Ui.PointerMoved(950);
        Assert.False(r.Ui.View.GhostValid);
        Assert.Equal("Limite de torres atingido", r.Ui.Notice);
    }

    [Fact]
    public void Chooser_disables_unaffordable_and_revalidates_after_credits_drop()
    {
        var r = new Rig();
        r.Ui.SceneLeftClick(300, 20, HitTarget.None);
        var pop = r.Ui.BuildPopover()!;
        Assert.All(pop.Items.Where(i => i.Action == TdUiAction.ChooseTower && i.Enabled), i => Assert.Contains(i.SArg, Affordable));
        // outra torre ocupa o ponto: volta ao passo do ponto com aviso
        r.Match.Apply(new BuildTower("bolt", r.Ui.View.ChosenX));
        r.Ui.Sync();
        Assert.True(r.Ui.ChoosingPoint);
        Assert.NotNull(r.Ui.Notice);
    }

    [Fact]
    public void Inspector_upgrade_branch_target_and_sell()
    {
        var r = new Rig();
        r.Match.Apply(new BuildTower("bolt", 600));
        int id = r.Match.Towers[0].Id;
        r.Ui.SceneLeftClick(500, 20, HitTarget.TdTower(id));
        Assert.Equal(TdUiMode.Inspector, r.Ui.Mode);
        Assert.Equal(id, r.Ui.View.SelectedTowerId);
        var v = r.Ui.BuildPopover()!;
        Assert.Contains(v.Items, i => i.Action == TdUiAction.Targeting && i.SArg == "Strong");
        r.Match.Apply(new SetPresence(false));
        // dinheiro para dois níveis
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(r.Match, 5000L);
        r.Ui.Activate(Find(v, TdUiAction.Upgrade));
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.Upgrade));
        Assert.Equal(3, r.Match.Towers[0].Level);
        var branches = r.Ui.BuildPopover()!;
        Assert.Equal(2, branches.Items.Count(i => i.Action == TdUiAction.Branch));
        r.Ui.Activate(Find(branches, TdUiAction.Branch, "A"));
        Assert.Equal(4, r.Match.Towers[0].Level);
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.Targeting, "Weak"));
        Assert.Equal((byte)TdTargeting.Weak, r.Match.Towers[0].Targeting);
        long before = r.Match.Credits;
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.Sell));
        Assert.Empty(r.Match.Towers);
        Assert.True(r.Match.Credits > before);
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
    }

    [Fact]
    public void Hud_start_button_sends_wave_request_and_ability_gates_on_wave()
    {
        var r = new Rig();
        var hud = r.Ui.BuildHud();
        var start = hud.Items.First(i => i.Action == TdUiAction.StartWave);
        Assert.True(start.Enabled);
        Assert.Contains("INICIAR", start.Text);
        var ability = hud.Items.First(i => i.Action == TdUiAction.Ability);
        Assert.False(ability.Enabled); // Preparo
        r.Ui.Activate(start);
        Assert.IsType<TdWaveRequest>(r.Sent.Single());
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
        hud = r.Ui.BuildHud();
        Assert.True(hud.Items.First(i => i.Action == TdUiAction.Ability && i.SArg == "scale").Enabled);
        Assert.Contains(hud.Items, i => i.Text.StartsWith("ONDA 1/", StringComparison.Ordinal));
        r.Ui.Activate(hud.Items.First(i => i.SArg == "scale"));
        Assert.IsType<UseAbility>(r.Sent[^1]);
    }

    [Fact]
    public void Menu_preview_and_map_flow()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "...", TdUiAction.Menu));
        var menu = r.Ui.BuildPopover()!;
        r.Ui.Activate(Find(menu, TdUiAction.Preview));
        var preview = r.Ui.BuildPopover()!;
        Assert.StartsWith("Onda 1/", preview.Title, StringComparison.Ordinal);
        Assert.Contains(preview.Items, i => i.Text.Contains('x'));
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "...", TdUiAction.Menu));
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.MapOpen));
        var map = r.Ui.BuildPopover()!;
        Assert.Equal(2, map.Items.Count(i => i.Action == TdUiAction.SelectStage && i.Enabled));
        r.Ui.Activate(Find(map, TdUiAction.MapTab));
        r.Ui.Activate(map.Items.First(i => i is { Action: TdUiAction.MapTab, Arg: 1 }));
        var tree = r.Ui.BuildPopover()!;
        Assert.Equal(12, tree.Items.Count(i => i.Action == TdUiAction.BuyNode));
        r.Ui.Activate(tree.Items.First(i => i.Action == TdUiAction.BuyNode && i.Enabled));
        Assert.StartsWith("buy:", r.Host.Calls.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void End_card_opens_on_defeat_and_actions_reach_the_host()
    {
        var r = new Rig();
        for (int i = 0; i < 30 * 3000 && r.Match.Phase is MatchPhase.Onda or MatchPhase.Preparo; i++)
        {
            if (r.Match.Phase == MatchPhase.Preparo) r.Match.Apply(new StartWave());
            r.Match.Step();
        }
        Assert.Equal(MatchPhase.Derrota, r.Match.Phase);
        r.Ui.Sync();
        Assert.Equal(TdUiMode.End, r.Ui.Mode);
        var card = r.Ui.BuildPopover()!;
        Assert.Equal("DERROTA", card.Title);
        Assert.DoesNotContain(card.Items, i => i.Action == TdUiAction.NextStage);
        r.Ui.Activate(Find(card, TdUiAction.Restart));
        Assert.Equal("stage1", r.Host.Calls.Single());
        // nova partida: o card some
        r.Match = Create(1);
        r.Ui.Sync();
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
    }

    [Fact]
    public void Victory_card_shows_crown_medals_score_record_and_rank()
    {
        var content = TdContentLoader.LoadEmbedded();
        var waves = new[] { new TdWaveDef(1, false, 1.0, [new TdWaveGroupDef("grunt", 0, 0.0, 1.1)]) };
        content = content with
        {
            Waves = new TdWavesDef([new TdStageWavesDef(1, waves), .. content.Waves.Stages.Skip(1)]),
            Stages = content.Stages with { Stages = [content.Stages.Stages[0] with { Waves = 1 }, .. content.Stages.Stages.Skip(1)] },
        };
        var r = new Rig { Match = TdMatchState.Create(content, 1, Rng.Seed(7, 54)) };
        r.Match.Apply(new StartWave());
        for (int i = 0; i < 3000 && r.Match.Phase == MatchPhase.Onda; i++) r.Match.Step();
        Assert.Equal(MatchPhase.Vitoria, r.Match.Phase);
        r.Host.LastOutcome = new TdRecordOutcome(true, true, TdMedals.SemVender, 3);
        r.Ui.Sync();
        var card = r.Ui.BuildPopover()!;
        Assert.StartsWith("VITORIA", card.Title, StringComparison.Ordinal);
        var badges = card.Items.Single(i => i.Kind == TdUiKind.Badges);
        Assert.True(badges.Selected);                                   // coroa
        Assert.True(((TdMedals)badges.Arg).HasFlag(TdMedals.SemVender)); // medalhas ganhas
        Assert.Contains(card.Items, i => i.Text.StartsWith("Pontuacao: ", StringComparison.Ordinal) && i.Right == "NOVO RECORDE");
        Assert.Contains(card.Items, i => i.Text == "Selos bonus: +3");
        Assert.Contains(card.Items, i => i.Kind == TdUiKind.Bar && i.Text.StartsWith("Patente: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Map_and_menu_show_per_stage_badges_record_and_rank()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "...", TdUiAction.Menu));
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Kind == TdUiKind.Bar && i.Text == "Patente: Júnior");
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.MapOpen));
        var map = r.Ui.BuildPopover()!;
        var badges = map.Items.Where(i => i.Kind == TdUiKind.Badges).ToArray();
        Assert.Equal(2, badges.Length); // só as fases abertas
        Assert.True(badges[0].Selected);
        Assert.Equal(5, badges[0].Arg);
        Assert.Equal("Recorde 4123", badges[0].Right);
        Assert.False(badges[1].Selected);
        Assert.Equal("sem recorde", badges[1].Right);
    }

    [Fact]
    public void Popover_hit_test_ignores_labels_and_disabled_items()
    {
        var r = new Rig();
        r.Ui.SceneLeftClick(300, 20, HitTarget.None);
        var pop = r.Ui.BuildPopover()!;
        var disabled = pop.Items.First(i => i.SArg == "balancer");
        Assert.Equal(-1, pop.HitTest(disabled.Rect.X + 2, disabled.Rect.Y + 2));
        var ok = pop.Items.First(i => i.SArg == "bolt");
        Assert.True(pop.HitTest(ok.Rect.X + 2, ok.Rect.Y + 2) >= 0);
    }
}
