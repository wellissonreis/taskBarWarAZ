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
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.8: fluxo de UI da partida TD (construção, inspetor, HUD, fim de partida) sobre o controlador puro.</summary>
public sealed class TdUiControllerTests
{
    private const int Width = 1000;
    private static readonly string[] Affordable = ["bolt", "frost", "cannon"];

    private sealed class FakeHost : ITdUiHost
    {
        public TdGameOptions GameOptions { get; private set; } = new();
        public void SetGameOptions(TdGameOptions options) => GameOptions = options;
        public bool Paused { get; set; }
        public float EndCardAvailableHeight { get; set; } = float.PositiveInfinity;
        public List<string> Calls { get; } = [];
        public void TogglePause() => Calls.Add("pause");
        public void OpenSettings() => Calls.Add("settings");
        public void RequestExit() => Calls.Add("exit");
        public bool HudMinimized { get; set; }
        public void SetHudMinimized(bool minimized) { HudMinimized = minimized; Calls.Add("hud:" + minimized); }
        public Action<int>? OnStartStage { get; set; }
        public void StartStage(int stageId) { Calls.Add($"stage{stageId}"); OnStartStage?.Invoke(stageId); }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => stageId <= 2;
        public int StageStars(int stageId) => stageId == 1 ? 2 : 0;
        public bool StageCrown(int stageId) => stageId == 1;
        public int StageMedals(int stageId) => stageId == 1 ? 5 : 0;
        public int StageBestScore(int stageId) => stageId == 1 ? 4123 : 0;
        public int Score { get; set; } = 7000;
        public SideWar.Core.Td.TdRankProgress Rank => SideWar.Core.Td.TdRank.Progress(Score);
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

    [Fact]
    public void Promotion_notice_only_appears_on_increase_and_expires()
    {
        var r = new Rig();
        r.Ui.Sync();
        Assert.False(r.Ui.NoticeOpen);
        r.Host.Score = 12000;
        r.Ui.Sync();
        Assert.Equal("Promovido a Pleno!", r.Ui.NoticeText);
        Assert.True(r.Ui.CornerPopover);
        r.Now += TdUiController.StageNoticeSeconds + 1;
        r.Ui.Sync();
        Assert.False(r.Ui.NoticeOpen);
        r.Host.Score = 5000;
        r.Ui.Sync();
        Assert.False(r.Ui.NoticeOpen);
    }

    [Fact]
    public void Score_change_refreshes_views_even_within_same_rank_or_on_decrease()
    {
        var r = new Rig();
        r.Ui.Sync();
        int changes = 0;
        r.Ui.Changed += () => changes++;
        r.Host.Score = 7100;
        r.Ui.Sync();
        Assert.True(changes > 0);
        changes = 0;
        r.Host.Score = 0;
        r.Ui.Sync();
        Assert.True(changes > 0);
        Assert.Null(r.Ui.NoticeText);
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.53f, 55)]
    [InlineData(1f, 100)]
    public void Volume_slider_writes_settings_in_five_percent_steps(float fraction, int expected)
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        var item = r.Ui.BuildPopover()!.Items.Single(i => i.Action == TdUiAction.GameOption && i.Arg == 3);
        float x = item.Rect.X + 88 + fraction * (item.Rect.W - 142);
        r.Ui.SetVolumeAt(item, x);
        Assert.Equal(expected, r.Host.GameOptions.Volume);
        Assert.Equal(expected / 100f, r.Ui.BuildPopover()!.Items.Single(i => i.Action == TdUiAction.GameOption && i.Arg == 3).Fill);
    }

    [Fact]
    public void Speed_buttons_in_pause_menu_update_settings()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        var button = r.Ui.BuildPopover()!.Items.Single(i => i.Action == TdUiAction.SetSpeed && i.Arg == 3);
        r.Ui.Activate(button);
        Assert.Equal(3, r.Host.GameOptions.GameSpeed);
        Assert.True(r.Ui.BuildPopover()!.Items.Single(i => i.Action == TdUiAction.SetSpeed && i.Arg == 3).Selected);
    }

    [Theory]
    [InlineData(0, "Brasão Estagiário", "0 / 5000 p.|faltam 5000 p. para Júnior")]
    [InlineData(4999, "Brasão Estagiário", "4999 / 5000 p.|faltam 1 p. para Júnior")]
    [InlineData(5000, "Brasão Júnior", "5000 / 12000 p.|faltam 7000 p. para Pleno")]
    [InlineData(34000, "Brasão Arquiteto", "34000 p.|Patente máxima")]
    public void Menu_rank_card_swaps_badge_name_and_bar_at_the_limits(int score, string tip, string bar)
    {
        var r = new Rig();
        r.Host.Score = score;
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        var card = r.Ui.BuildPopover()!.Items.Single(i => i.SArg == "rank-card");
        Assert.Equal(tip, card.Tip);
        Assert.Equal(bar, card.Right);
        Assert.Equal(SideWar.Core.Td.TdRank.Brasao((SideWar.Core.Td.TdRankLevel)card.Arg), SideWar.Core.Td.TdRank.Brasao(SideWar.Core.Td.TdRank.Progress(score).Current));
    }

    [Theory]
    [InlineData(0, 0, "Estagiário")]
    [InlineData(4999, 0, "Estagiário")]
    [InlineData(5000, 1, "Júnior")]
    [InlineData(34000, 5, "Arquiteto")]
    public void Menu_rank_and_header_follow_current_score(int score, int level, string name)
    {
        var r = new Rig();
        r.Host.Score = score;
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        var items = r.Ui.BuildPopover()!.Items;
        Assert.Contains(items, i => i.SArg == "rank-card" && i.Arg == level && i.Text == name);
        Assert.Contains(items, i => i.SArg == "rank-chip" && i.Arg == level && i.Text == name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Catalog_pages_cover_unlocked_towers_and_stay_inside_view(bool quick)
    {
        var r = new Rig { Match = Create(6) };
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", quick ? TdUiAction.QuickTowers : TdUiAction.BuildToggle));
        foreach (var category in new[] { "attack", "control", "support", "economy" })
        {
            r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.TowerCategory, SArg: category));
            var expected = r.Match.Content.Towers.Where(t => t.Category == category && t.UnlockStage <= r.Match.UnlockedTowerStage).Select(t => t.Id).ToArray();
            var actual = new List<string>();
            for (int page = 0; page < 20; page++)
            {
                var view = r.Ui.BuildPopover()!;
                foreach (var item in view.Items.Where(i => i.Action == TdUiAction.ChooseTower))
                {
                    Assert.True(item.Rect.X >= 0 && item.Rect.Y >= 0 && item.Rect.X + item.Rect.W <= view.Width && item.Rect.Y + item.Rect.H <= view.Height);
                    if (!actual.Contains(item.SArg!)) actual.Add(item.SArg!);
                    if (item.Clickable) Assert.Equal(item, view.Items[view.HitTest(item.Rect.X + 1, item.Rect.Y + 1)]);
                }
                if (!quick)
                {
                    // Modal: sem setas; a rolagem desce uma página por vez até o fim.
                    if (view.Scroll is not { } sc || sc.Offset + sc.Visible >= sc.Total) break;
                    Assert.DoesNotContain(view.Items, i => i.Action == TdUiAction.BuildCatalogPage);
                    r.Ui.ScrollBy(sc.Visible);
                    continue;
                }
                var next = view.Items.Single(i => i.Action == TdUiAction.BuildCatalogPage && i.Arg == 1);
                if (!next.Enabled) break;
                r.Ui.Activate(next);
            }
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Catalog_shows_next_unlock_without_locked_tower_buttons()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.QuickTowers));
        var view = r.Ui.BuildPopover()!;
        Assert.Contains(view.Items, i => i.Text.StartsWith("Próxima:", StringComparison.Ordinal));
        Assert.All(view.Items.Where(i => i.Action == TdUiAction.ChooseTower), item =>
            Assert.True(r.Match.Content.Towers.Single(t => t.Id == item.SArg).UnlockStage <= r.Match.UnlockedTowerStage));
    }

    [Fact]
    public void Outside_collapse_closes_build_and_runes_without_reopening_pinned_panel()
    {
        var r = new Rig();
        r.Host.SetGameOptions(new TdGameOptions(PanelPinned: true));
        r.Ui.Sync();
        Assert.True(r.Ui.QuickTowersOpen);
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ChooseTower, SArg: "bolt"));
        Assert.True(r.Ui.BuildActive);
        r.Ui.CollapseOutside();
        for (int i = 0; i < 5; i++) r.Ui.Sync();
        Assert.False(r.Ui.BuildActive);
        Assert.Null(r.Ui.BuildPopover());
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.RuneWindow));
        Assert.True(r.Ui.RuneWindowOpen);
        r.Ui.CollapseOutside();
        r.Ui.Sync();
        Assert.False(r.Ui.RuneWindowOpen);
        Assert.Null(r.Ui.BuildPopover());
    }

    [Fact]
    public void Hover_tooltip_does_not_intercept_pointer_and_tower_selection_stays_stable()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.QuickTowers));
        var tower = r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.ChooseTower && i.Enabled);
        for (int frame = 0; frame < 20; frame++)
        {
            r.Ui.Hover(tower);
            r.Ui.Sync();
            Assert.False(TdTooltipView.AcceptsPointer(firstTime: false));
            Assert.Equal(TdUiMode.Build, r.Ui.Mode);
            Assert.True(r.Ui.QuickTowersOpen);
            Assert.Equal(tower.SArg, r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.ChooseTower && i.SArg == tower.SArg).SArg);
        }
        Assert.True(TdTooltipView.AcceptsPointer(firstTime: true));
        r.Ui.Activate(tower);
        for (int frame = 0; frame < 20; frame++) { r.Ui.Sync(); Assert.True(r.Ui.BuildActive); }
    }

    [Fact]
    public void Persistent_defeat_countdown_restarts_at_eight_seconds_and_view_runes_cancels()
    {
        var r = new Rig();
        r.Match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54), null, TdMatchOptions.Default);
        r.Match.Apply(new LayoutChanged(StripLayout.Create(Width, Width - 46, 88, 4, Width - 80, TowerHost.Band, [new FreeZone(32, Width - 32)])));
        for (int i = 0; i < 30 * 3000 && r.Match.Phase is MatchPhase.Onda or MatchPhase.Preparo; i++)
        {
            if (r.Match.Phase == MatchPhase.Preparo) r.Match.Apply(new StartWave());
            r.Match.Step();
        }
        Assert.Equal(MatchPhase.Derrota, r.Match.Phase);
        r.Host.SetGameOptions(new TdGameOptions(AutoRestart: true));
        r.Ui.Sync();
        var card = r.Ui.BuildPopover()!;
        Assert.Equal("CORRIDA ENCERRADA", card.Title);
        Assert.Contains(card.Items, x => x.Text == "Recomecando em 8 s");
        r.Now = 2; r.Ui.Sync();
        r.Host.Paused = true;
        r.Now = 12; r.Ui.Sync();
        r.Host.Paused = false;
        r.Ui.SetBossHidden(true);
        r.Now = 22; r.Ui.Sync();
        r.Ui.SetBossHidden(false);
        r.Ui.Sync();
        r.Now = 27; r.Ui.Sync();
        Assert.DoesNotContain("stage1", r.Host.Calls);
        r.Now = 28; r.Ui.Sync();
        Assert.Contains("stage1", r.Host.Calls);

        var second = new Rig { Match = r.Match };
        second.Host.SetGameOptions(new TdGameOptions(AutoRestart: true));
        second.Ui.Sync();
        second.Ui.Activate(Find(second.Ui.BuildPopover()!, TdUiAction.ViewRunes));
        second.Now = 20; second.Ui.Sync();
        Assert.DoesNotContain("stage1", second.Host.Calls);
        Assert.Contains(second.Ui.BuildPopover()!.Items, x => x.Text.StartsWith("Legado", StringComparison.Ordinal));
    }

    [Fact]
    public void Persistent_defeat_card_sheds_rank_bar_then_leaks_when_space_is_limited()
    {
        var r = new Rig { Match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54), null, TdMatchOptions.Default) };
        r.Match.Apply(new LayoutChanged(StripLayout.Create(Width, Width - 46, 88, 4, Width - 80, TowerHost.Band, [new FreeZone(32, Width - 32)])));
        for (int i = 0; i < 30 * 3000 && r.Match.Phase is MatchPhase.Onda or MatchPhase.Preparo; i++)
        {
            if (r.Match.Phase == MatchPhase.Preparo) r.Match.Apply(new StartWave());
            r.Match.Step();
        }
        r.Ui.Sync();
        var full = r.Ui.BuildPopover()!;
        Assert.Contains(full.Items, x => x.Kind == TdUiKind.Bar);
        Assert.Contains(full.Items, x => x.Text.StartsWith("Vazou:", StringComparison.Ordinal));
        r.Host.EndCardAvailableHeight = full.Height - 1;
        var smaller = r.Ui.BuildPopover()!;
        Assert.DoesNotContain(smaller.Items, x => x.Kind == TdUiKind.Bar);
        Assert.Contains(smaller.Items, x => x.Text.StartsWith("Vazou:", StringComparison.Ordinal));
        r.Host.EndCardAvailableHeight = full.Height - 10;
        Assert.DoesNotContain(r.Ui.BuildPopover()!.Items, x => x.Text.StartsWith("Vazou:", StringComparison.Ordinal));
    }

    private static TdUiItem Find(TdPopoverView v, TdUiAction a, string? sarg = null) =>
        v.Items.First(i => i.Action == a && (sarg is null || i.SArg == sarg));

    [Fact]
    public void Categories_filter_horizontal_slots_and_recover_after_match_change()
    {
        var r = new Rig { Match = Create(6) };
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.QuickTowers));
        var panel = r.Ui.BuildPopover()!;
        var slots = panel.Items.Where(i => i.Action == TdUiAction.ChooseTower).ToArray();
        Assert.Equal(6, slots.Length);
        Assert.All(slots, i => { Assert.Equal(slots[0].Rect.Y, i.Rect.Y); Assert.Equal(44, i.Rect.W); Assert.NotNull(i.Tip); });
        r.Ui.Activate(Find(panel, TdUiAction.TowerCategory, "economy"));
        Assert.Single(r.Ui.BuildPopover()!.Items, i => i.Action == TdUiAction.ChooseTower);
        r.Match = Create(1);
        panel = r.Ui.BuildPopover()!;
        Assert.DoesNotContain(panel.Items, i => i.Action == TdUiAction.TowerCategory && i.SArg == "economy");
        Assert.Contains(panel.Items, i => i.Action == TdUiAction.TowerCategory && i.SArg == "attack" && i.Selected);
        Assert.Contains(panel.Items, i => i.Action == TdUiAction.ChooseTower && i.SArg == "bolt");
    }

    [Fact]
    public void Quick_towers_builds_without_pausing_and_stays_open()
    {
        var r = new Rig();
        r.Ui.Activate(Assert.Single(r.Ui.BuildHud().Items, i => i.Action == TdUiAction.QuickTowers));
        Assert.True(r.Ui.QuickTowersOpen);
        Assert.False(r.Ui.ModalOpen);
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.ChooseTower, "bolt"));
        Assert.True(r.Ui.BuildActive);
        Assert.True(r.Ui.SceneLeftClick(300, 20, HitTarget.None));
        Assert.Single(r.Match.Towers);
        Assert.True(r.Ui.QuickTowersOpen);
        Assert.False(r.Ui.BuildActive);
        Assert.DoesNotContain(r.Sent, c => c is Pause or Resume);
        r.Ui.Cancel();
        Assert.False(r.Ui.QuickTowersOpen);
    }

    [Fact]
    public void Ability_closes_and_resumes_before_sending_the_time_command()
    {
        var r = new Rig();
        r.Match.Apply(new StartWave());
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        var ability = r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.ResumeAbility && i.SArg == "scale");
        Assert.True(ability.Enabled);
        Assert.True(r.Match.Paused);
        r.Ui.Activate(ability);
        Assert.False(r.Ui.ModalOpen);
        Assert.False(r.Match.Paused);
        Assert.IsType<Resume>(r.Sent[^2]);
        Assert.IsType<UseAbility>(r.Sent[^1]);
    }

    [Fact]
    public void Game_tab_edits_options_without_resuming_or_sending_time_commands()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        Assert.Equal(8, r.Ui.BuildPopover()!.Items.Count(i => i.Action == TdUiAction.GameOption));
        for (int i = 0; i < 8; i++)
            r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.GameOption, i));
        Assert.Equal(new TdGameOptions(88, true, true, 80, false, false, false, true, 1), r.Host.GameOptions);
        Assert.Single(r.Sent.OfType<Pause>());
        Assert.Empty(r.Sent.OfType<Resume>());
        Assert.True(r.Match.Paused);
    }

    [Fact]
    public void Restart_in_game_tab_requires_confirmation_and_cancel_keeps_match()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.RestartPrompt));
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Text == "Reiniciar?");
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.RestartCancel));
        Assert.Empty(r.Host.Calls);
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.RestartPrompt));
        r.Host.OnStartStage = stage => r.Match = Create(stage);
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.Restart));
        r.Ui.Sync();
        Assert.Equal("stage1", Assert.Single(r.Host.Calls));
        Assert.False(r.Match.Paused);
        Assert.Equal(MatchPhase.Preparo, r.Match.Phase);
    }

    [Fact]
    public void Tabs_keep_one_pause_until_the_window_closes()
    {
        var r = new Rig();
        foreach (var mode in new[] { TdUiMode.Build, TdUiMode.Runes, TdUiMode.Towers, TdUiMode.Menu, TdUiMode.Help })
        {
            r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)mode));
            Assert.Equal(1, r.Ui.ModalCount);
            Assert.False(r.Ui.SceneLeftClick(300, 20, HitTarget.None));
        }
        Assert.Single(r.Sent.OfType<Pause>());
        r.Ui.Cancel();
        Assert.Single(r.Sent.OfType<Resume>());
    }

    [Fact]
    public void Click_on_free_point_opens_chooser_and_confirm_sends_BuildTower()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.HudRestore));
        Assert.True(r.Match.Paused);
        var pop = r.Ui.BuildPopover()!;
        Assert.Equal(720, pop.Width);
        Assert.Equal(480, pop.Height);
        r.Ui.Activate(Find(pop, TdUiAction.ChooseTower, "bolt"));
        Assert.True(r.Ui.BuildActive);
        Assert.False(r.Match.Paused);
        Assert.Null(r.Ui.BuildPopover());
        r.Ui.SceneLeftClick(300, 20, HitTarget.None);
        Assert.Single(r.Match.Towers);
        Assert.True(r.Match.Paused);
        Assert.Equal(TdUiMode.Build, r.Ui.Mode);
        Assert.Equal(1, r.Ui.ModalCount);
        Assert.Equal(new[] { typeof(Pause), typeof(Resume), typeof(BuildTower), typeof(Pause) }, r.Sent.Select(c => c.GetType()));
    }

    [Fact]
    public void Click_outside_zone_does_nothing_and_cancel_sends_no_command()
    {
        var r = new Rig();
        Assert.False(r.Ui.SceneLeftClick(300, 20, HitTarget.None));
        r.Ui.ClickPoint(300);
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.ChooseTower, "bolt"));
        r.Ui.PointerMoved(2);
        Assert.False(r.Ui.View.GhostValid);
        r.Ui.SceneLeftClick(2, 20, HitTarget.None);
        Assert.Empty(r.Match.Towers);
        Assert.True(r.Ui.BuildActive);
        r.Ui.Cancel();
        Assert.False(r.Match.Paused);
        Assert.False(r.Ui.BuildActive);
    }

    [Fact]
    public void Build_mode_times_out_after_15_seconds()
    {
        var r = new Rig();
        r.Ui.ClickPoint(300);
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.ChooseTower, "bolt"));
        r.Now = 10; r.Ui.Sync();
        Assert.True(r.Ui.BuildActive);
        r.Now = 16; r.Ui.Sync();
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
        Assert.False(r.Match.Paused);
    }

    [Fact]
    public void Chooser_shows_growing_price_per_tower_type()
    {
        var r = new Rig();
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(r.Match, 5000L);
        r.Match.Apply(new BuildTower("bolt", 100));
        r.Ui.ClickPoint(300);
        var pop = r.Ui.BuildPopover()!;
        Assert.Equal("$112", Find(pop, TdUiAction.ChooseTower, "bolt").Right);  // 2ª torre do tipo: 90 x 1,25 = 112,5 -> 112
        Assert.Contains("x1", Find(pop, TdUiAction.ChooseTower, "bolt").Text);
        r.Ui.Activate(Find(pop, TdUiAction.TowerCategory, "control"));
        Assert.Equal("$90", Find(r.Ui.BuildPopover()!, TdUiAction.ChooseTower, "frost").Right);
    }

    [Fact]
    public void Chooser_disables_unaffordable_and_revalidates_after_credits_drop()
    {
        var r = new Rig();
        r.Ui.ClickPoint(300);
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(r.Match, 0L);
        Assert.All(r.Ui.BuildPopover()!.Items.Where(i => i.Action == TdUiAction.ChooseTower), i => Assert.False(i.Enabled));
        Assert.True(r.Match.Paused);
    }

    [Fact]
    public void Inspector_has_target_and_sell_but_no_upgrade_or_branch()
    {
        var r = new Rig();
        r.Match.Apply(new BuildTower("bolt", 600));
        int id = r.Match.Towers[0].Id;
        r.Ui.OpenTowerFromList(id);
        Assert.Equal(TdUiMode.Inspector, r.Ui.Mode);
        Assert.Equal(id, r.Ui.View.SelectedTowerId);
        var v = r.Ui.BuildPopover()!;
        Assert.Contains(v.Items, i => i.Action == TdUiAction.Targeting && i.SArg == "Strong");
        Assert.DoesNotContain(v.Items, i => i.Text.Contains("Melhorar", StringComparison.OrdinalIgnoreCase) || i.Text.StartsWith("Ramo", StringComparison.Ordinal));
        r.Match.Apply(new SetPresence(false));
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.Targeting, "Weak"));
        Assert.Equal((byte)TdTargeting.Weak, r.Match.Towers[0].Targeting);
        long before = r.Match.Credits, refund = r.Match.SellValue(id);
        Assert.Equal(63, refund); // floor(90 x 0,7)
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.Sell));
        Assert.Empty(r.Match.Towers);
        Assert.Equal(before + refund, r.Match.Credits);
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
    }

    [Fact]
    public void Hud_start_button_sends_wave_request_and_ability_gates_on_wave()
    {
        var r = new Rig();
        var start = r.Ui.BuildHud().Items.First(i => i.Action == TdUiAction.StartWave);
        Assert.True(start.Enabled);
        r.Ui.Activate(start);
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        Assert.True(r.Match.Paused);
        Assert.All(r.Ui.BuildPopover()!.Items.Where(i => i.Action == TdUiAction.Ability), i => Assert.False(i.Enabled));
        Assert.False(r.Ui.BuildHud().Items.First(i => i.Action == TdUiAction.StartWave).Enabled);
    }

    [Fact]
    public void Hud_is_readable_every_item_has_a_tooltip_and_abilities_show_name_icon_and_lock()
    {
        var r = new Rig();
        var hud = r.Ui.BuildHud();
        Assert.False(hud.Minimized);
        Assert.Contains(hud.Items, i => i.Action == TdUiAction.HudRestore && i.Text.Contains("20/20") && !i.Text.Contains('¢'));
        Assert.Contains(hud.Items, i => i.Kind == TdUiKind.Money);
        Assert.DoesNotContain(hud.Items, i => i.Action == TdUiAction.Ability);
        Assert.All(hud.Items.Where(i => i.Kind != TdUiKind.Separator && i.SArg != "seg"), i => Assert.False(string.IsNullOrEmpty(i.Tip)));
        r.Ui.Activate(hud.Items.First(i => i.Action == TdUiAction.HudRestore));
        Assert.True(r.Ui.ModalOpen);
        Assert.Equal(5, r.Ui.BuildPopover()!.Items.Count(i => i.Action == TdUiAction.ModalTab));
    }

    [Fact]
    public void Start_button_becomes_call_next_with_the_bonus_during_the_wave()
    {
        var r = new Rig();
        r.Ui.Activate(r.Ui.BuildHud().Items.First(i => i.Action == TdUiAction.StartWave));
        for (int i = 0; i < 30 * 60 && r.Match.ActiveWaves.Any(a => !a.SpawnsDone); i++) r.Match.Step();
        var start = r.Ui.BuildHud().Items.First(i => i.Action == TdUiAction.StartWave);
        Assert.Equal("Próxima", start.Text);
        Assert.Equal(TdIcon.FastForward, start.Icon);
        Assert.True(start.RightCoin || start.Right is null);
    }

    [Fact]
    public void Minimize_collapses_the_hud_to_a_chip_with_core_and_wave_and_the_chip_restores()
    {
        var r = new Rig();
        r.Host.HudMinimized = true;
        var chip = r.Ui.BuildHud();
        Assert.True(chip.Minimized);
        Assert.Equal(22f, chip.Height);
        Assert.InRange(chip.Width, 190f, 195f);
        Assert.Contains(chip.Items, i => i.SArg == "rank-crown" && i.Tip == "Patente: Júnior");
        Assert.DoesNotContain(chip.Items, i => i.Kind == TdUiKind.Money || i.Action == TdUiAction.StartWave);
        var expand = chip.Items.Single(i => i.Action == TdUiAction.HudExpand);
        Assert.Equal(1f, expand.Fill); // Iniciar disponível continua sinalizado sem abrir o HUD.
        Assert.Equal(expand, chip.Items[chip.HitTest(expand.Rect.X + 1, expand.Rect.Y + 1)]);
        r.Ui.Activate(expand);
        Assert.False(r.Host.HudMinimized);
        var full = r.Ui.BuildHud();
        Assert.False(full.Minimized);
        Assert.Equal(28f, full.Height);
        Assert.Contains(full.Items, i => i.Kind == TdUiKind.Money);
        var collapse = full.Items.Single(i => i.Action == TdUiAction.HudMinimize);
        Assert.Equal(collapse, full.Items[full.HitTest(collapse.Rect.X + 1, collapse.Rect.Y + 1)]);
        r.Ui.Activate(collapse);
        Assert.True(r.Host.HudMinimized);
        Assert.True(r.Ui.BuildHud().Minimized);
        Assert.False(r.Ui.ModalOpen);
        Assert.False(r.Match.Paused);
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
        Assert.StartsWith("DERROTA", card.Title, StringComparison.Ordinal);
        Assert.DoesNotContain(card.Items, i => i.Action == TdUiAction.NextStage);
        Assert.Contains(card.Items, i => i.Action == TdUiAction.MapOpen);
        Assert.Contains(card.Items, i => i.Action == TdUiAction.Restart); // o card do cantinho mantém "Reiniciar fase"
        Assert.True(r.Ui.EndCardOpen);
        Assert.True(r.Ui.CornerPopover);
        Assert.False(r.Ui.ModalOpen); // automático: cantinho, sem modal central
        Assert.DoesNotContain(card.Items, i => i.Action == TdUiAction.ModalTab);
        r.Ui.Cancel();
        r.Ui.Activate(r.Ui.BuildHud().Items.First(i => i.Action == TdUiAction.HudRestore));
        Assert.Equal(TdUiMode.End, r.Ui.Mode);
        Assert.False(r.Ui.EndCardOpen);
        Assert.True(r.Ui.ModalOpen); // clique do usuário no chip abre a modal central
        card = r.Ui.BuildPopover()!;
        r.Host.OnStartStage = stage => r.Match = Create(stage);
        r.Ui.Activate(Find(card, TdUiAction.Restart));
        Assert.Equal("stage1", r.Host.Calls.Single());
        // nova partida: o card some
        r.Ui.Sync();
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
        Assert.Equal(MatchPhase.Preparo, r.Match.Phase);
        Assert.False(r.Match.Paused);
        r.Ui.Activate(r.Ui.BuildHud().Items.First(i => i.Action == TdUiAction.StartWave));
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
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
        Assert.Contains(card.Items, i => i.Action == TdUiAction.Restart && i.Text == "Jogar de novo");
        Assert.Contains(card.Items, i => i.Action == TdUiAction.MapOpen);
        r.Host.OnStartStage = stage => r.Match = Create(stage);
        r.Ui.Activate(Find(card, TdUiAction.NextStage));
        r.Ui.Sync();
        Assert.Equal(2, r.Match.StageId);
        Assert.Equal(MatchPhase.Preparo, r.Match.Phase);
        Assert.False(r.Match.Paused);
    }

    [Fact]
    public void Map_and_menu_show_per_stage_badges_record_and_rank()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "...", TdUiAction.Menu));
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Kind == TdUiKind.Bar && i.SArg == "rank-card" && i.Text == "Júnior");
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
        r.Ui.ClickPoint(300);
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(r.Match, 0L);
        var pop = r.Ui.BuildPopover()!;
        var disabled = pop.Items.First(i => i.SArg == "bolt");
        Assert.Equal(-1, pop.HitTest(disabled.Rect.X + 2, disabled.Rect.Y + 2));
        var ok = pop.Items.First(i => i.Action == TdUiAction.TowerCategory);
        Assert.True(pop.HitTest(ok.Rect.X + 2, ok.Rect.Y + 2) >= 0);
    }

    // ------------------------------------------------------------ TD.16b: faixa click-through, modo posicionar, caixa pelo HUD

    [Fact]
    public void Band_ignores_clicks_outside_the_placing_mode_and_only_the_placing_mode_accepts_them()
    {
        var r = new Rig();
        Assert.False(r.Ui.SceneLeftClick(300, 20, HitTarget.None));
        r.Ui.ClickPoint(300);
        Assert.False(r.Ui.BuildActive);
        Assert.False(r.Ui.SceneLeftClick(300, 20, HitTarget.None));
        r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.ChooseTower, "bolt"));
        Assert.True(r.Ui.View.PlacingMode);
        r.Ui.Cancel();
        Assert.False(r.Ui.View.PlacingMode);
        Assert.False(r.Ui.SceneLeftClick(300, 20, HitTarget.None));
    }

    [Fact]
    public void Placing_mode_exits_on_escape_right_click_and_timeout_and_ghost_follows_the_mouse_only_inside_it()
    {
        var r = new Rig();
        for (int i = 0; i < 3; i++)
        {
            r.Ui.ClickPoint(300);
            r.Ui.Activate(Find(r.Ui.BuildPopover()!, TdUiAction.ChooseTower, "bolt"));
            r.Ui.PointerMoved(300);
            Assert.True(r.Ui.View.GhostX >= 0);
            if (i == 0) r.Ui.Cancel();
            else if (i == 1) r.Ui.SceneRightClick();
            else { r.Now += 16; r.Ui.Sync(); }
            Assert.False(r.Ui.BuildActive);
            Assert.False(r.Match.Paused);
        }
    }

    [Fact]
    public void Towers_popover_lists_towers_with_sell_and_reposition_for_unplaced()
    {
        var r = new Rig();
        r.Match.Apply(new BuildTower("bolt", 600));
        int id = r.Match.Towers[0].Id;
        var menu = new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu);
        r.Ui.Activate(menu);
        var towersItem = Find(r.Ui.BuildPopover()!, TdUiAction.TowersOpen);
        r.Ui.Activate(towersItem);
        Assert.Equal(TdUiMode.Towers, r.Ui.Mode);
        var pop = r.Ui.BuildPopover()!;
        Assert.Contains(pop.Items, i => i.Action == TdUiAction.OpenTower && i.Arg == id);
        long before = r.Match.Credits;
        r.Ui.Activate(Find(pop, TdUiAction.Sell));
        Assert.IsType<SellTower>(r.Sent.Last());
        Assert.Empty(r.Match.Towers);
        Assert.True(r.Match.Credits > before);
        Assert.Equal(TdUiMode.Towers, r.Ui.Mode); // a lista continua aberta
        // abrir uma torre da lista mostra o inspetor completo
        r.Match.Apply(new BuildTower("bolt", 600));
        r.Ui.OpenTowerFromList(r.Match.Towers[0].Id);
        Assert.Equal(TdUiMode.Inspector, r.Ui.Mode);
    }
}
