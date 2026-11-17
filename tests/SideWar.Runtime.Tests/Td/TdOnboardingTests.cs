using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Input;
using SideWar.Runtime.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;
using UpgradeTower = SideWar.Core.Td.UpgradeTower;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.15: tutorial da 1ª partida, cartão "Como jogar" e dicas de 1ª vez (controlador puro; nada vai ao log nem ao hash).</summary>
public sealed class TdOnboardingTests
{
    private const int Width = 1000;

    private sealed class Host : ITdUiHost
    {
        public List<string> Calls { get; } = [];
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
        public void RequestExit() { }
        public bool HudMinimized { get; set; }
        public void SetHudMinimized(bool minimized) { HudMinimized = minimized; Calls.Add("hud:" + minimized); }
        public void StartStage(int stageId) => Calls.Add($"stage{stageId}");
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

    private sealed class Store : ITdOnboardingStore
    {
        public bool Done;
        public readonly HashSet<string> Tips = [];
        public int Saves;
        public bool TutorialDone => Done;
        public void SetTutorialDone(bool done) { Done = done; Saves++; }
        public bool TipSeen(string id) => Tips.Contains(id);
        public void MarkTipSeen(string id) { Tips.Add(id); Saves++; }
    }

    private sealed class Rig
    {
        public TdMatchState Match;
        public readonly List<IGameCommand> Sent = [];
        public readonly Host Host = new();
        public readonly Store Store;
        public double Now;
        public readonly TdUiController Ui;

        public Rig(int stage = 1, Store? store = null, bool tutorialDone = false)
        {
            Match = Create(stage);
            Store = store ?? new Store { Done = tutorialDone };
            Ui = new TdUiController(() => Match, Send, Host, () => Now, Store);
            Ui.Sync();
        }

        private void Send(IGameCommand c)
        {
            Sent.Add(c);
            Match.Apply(c is TdWaveRequest ? new StartWave() : c);
        }

        public void Click(TdUiAction a, string? sarg = null, int arg = 0) =>
            Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", a, arg, sarg));

        public TdUiItem Hud(TdUiAction a) => Ui.BuildHud().Items.First(i => i.Action == a);

        public void Credits(long v) => typeof(TdMatchState).GetProperty("Credits")!.SetValue(Match, v);

        public void BuildBolt()
        {
            Click(TdUiAction.BuildToggle);
            Ui.SceneLeftClick(300, 20, HitTarget.None);
            Click(TdUiAction.ChooseTower, "bolt");
        }

        public void StepWave(int ticks)
        {
            for (int i = 0; i < ticks && Match.Phase == MatchPhase.Onda; i++) Match.Step();
            Ui.Sync();
        }
    }

    private static TdMatchState Create(int stage)
    {
        var m = TdMatchState.Create(TdContentLoader.LoadEmbedded(), stage, Rng.Seed(7, 54));
        m.Apply(new LayoutChanged(StripLayout.Create(Width, Width - 46, 88, 4, Width - 80, TowerHost.Band, [new FreeZone(32, Width - 32)])));
        return m;
    }

    // ------------------------------------------------------------ tutorial

    [Fact]
    public void Steps_advance_by_real_actions_not_by_time()
    {
        var r = new Rig();
        Assert.Equal(TdTutorialStep.Build, r.Ui.Step);
        r.Now = 500; r.Ui.Sync(); r.Ui.Sync();
        Assert.Equal(TdTutorialStep.Build, r.Ui.Step); // ler sem pressa: o tempo não avança o passo
        r.Click(TdUiAction.BuildToggle);
        Assert.Equal(TdTutorialStep.Point, r.Ui.Step);
        r.Ui.SceneLeftClick(300, 20, HitTarget.None);
        Assert.Equal(TdTutorialStep.Tower, r.Ui.Step);
        r.Click(TdUiAction.ChooseTower, "bolt");
        Assert.Equal(TdTutorialStep.Wave, r.Ui.Step);
        r.Click(TdUiAction.StartWave);
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
        Assert.NotEqual(TdTutorialStep.Wave, r.Ui.Step);
    }

    [Fact]
    public void Clicking_a_band_point_directly_skips_to_tower_step()
    {
        var r = new Rig();
        r.Ui.SceneLeftClick(300, 20, HitTarget.None);
        Assert.Equal(TdTutorialStep.Tower, r.Ui.Step);
    }

    [Fact]
    public void Build_step_shows_callout_with_arrow_to_the_construir_button_and_skip_button()
    {
        var r = new Rig();
        var pop = r.Ui.BuildPopover()!;
        Assert.StartsWith("Tutorial 1/7", pop.Title);
        Assert.True(pop.HasArrow);
        Assert.True(pop.TargetHudX > 0);
        Assert.Contains(pop.Items, i => i.Kind == TdUiKind.Label && i.Text.Contains("Construir"));
        Assert.Contains(pop.Items, i => i.Action == TdUiAction.TutorialSkip && i.Clickable);
        Assert.True(r.Hud(TdUiAction.BuildToggle).Highlight);
        Assert.False(r.Hud(TdUiAction.StartWave).Highlight);
    }

    [Fact]
    public void Point_and_tower_steps_decorate_the_chooser_and_always_keep_skip()
    {
        var r = new Rig();
        r.Click(TdUiAction.BuildToggle);
        var point = r.Ui.BuildPopover()!;
        Assert.Contains(point.Items, i => i.Text.Contains("ponto da faixa"));
        Assert.Contains(point.Items, i => i.Action == TdUiAction.TutorialSkip);
        Assert.True(r.Ui.View.BandHint);
        r.Ui.SceneLeftClick(300, 20, HitTarget.None);
        var tower = r.Ui.BuildPopover()!;
        Assert.False(r.Ui.View.BandHint);
        var hi = Assert.Single(tower.Items, i => i.Highlight);
        Assert.Equal(TdUiAction.ChooseTower, hi.Action);
        Assert.Contains(tower.Items, i => i.Action == TdUiAction.TutorialSkip);
    }

    [Fact]
    public void Wave_step_highlights_start_button()
    {
        var r = new Rig();
        r.BuildBolt();
        Assert.Equal(TdTutorialStep.Wave, r.Ui.Step);
        Assert.True(r.Hud(TdUiAction.StartWave).Highlight);
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Text == "Clique em Iniciar onda");
    }

    [Fact]
    public void Upgrade_step_when_credits_allow_and_ends_on_the_real_upgrade_click()
    {
        var r = new Rig();
        r.BuildBolt();
        r.Click(TdUiAction.StartWave);
        r.Credits(500);
        r.Ui.Sync();
        Assert.Equal(TdTutorialStep.Upgrade, r.Ui.Step);
        Assert.True(r.Ui.View.HintX > 0);
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Text.Contains("torre para melhorar"));
        r.Ui.SceneLeftClick(0, 0, HitTarget.TdTower(r.Match.Towers[0].Id));
        var inspector = r.Ui.BuildPopover()!;
        Assert.Equal(TdUiAction.Upgrade, Assert.Single(inspector.Items, i => i.Highlight).Action);
        r.Click(TdUiAction.Upgrade, arg: r.Match.Towers[0].Id);
        Assert.NotEqual(TdTutorialStep.Upgrade, r.Ui.Step);
        Assert.IsType<UpgradeTower>(r.Sent.Last());
    }

    [Fact]
    public void Upgrade_step_waits_while_credits_are_short()
    {
        var r = new Rig();
        r.BuildBolt();
        r.Click(TdUiAction.StartWave);
        r.Credits(0);
        Assert.NotEqual(TdTutorialStep.Upgrade, r.Ui.Step);
    }

    [Fact]
    public void Ability_step_appears_when_ready_and_ends_when_used()
    {
        var r = new Rig();
        r.BuildBolt();
        r.Click(TdUiAction.StartWave);
        r.Credits(0);
        r.Ui.Sync();
        Assert.Equal(TdTutorialStep.Ability, r.Ui.Step);
        var hi = Assert.Single(r.Ui.BuildHud().Items, i => i.Highlight);
        Assert.Equal(TdUiAction.Ability, hi.Action);
        r.Click(TdUiAction.Ability, "scale");
        Assert.Equal(TdTutorialStep.None, r.Ui.Step);
    }

    [Fact]
    public void Crate_step_on_first_crate_and_a_collected_or_vanished_crate_completes_it()
    {
        var r = new Rig();
        r.BuildBolt();
        r.Click(TdUiAction.StartWave);
        r.Credits(0);
        r.Click(TdUiAction.Ability, "scale");
        typeof(TdMatchState).GetProperty("CrateActive")!.SetValue(r.Match, true);
        typeof(TdMatchState).GetProperty("CrateX")!.SetValue(r.Match, 600);
        r.Ui.Sync();
        Assert.Equal(TdTutorialStep.Crate, r.Ui.Step);
        Assert.True(r.Ui.View.HintX > 0);
        var pop = r.Ui.BuildPopover()!;
        Assert.Contains(pop.Items, i => i.Text == "Clique na caixa antes que exploda");
        Assert.True(pop.AnchorX > 0);
        typeof(TdMatchState).GetProperty("CrateActive")!.SetValue(r.Match, false);
        r.Ui.Sync();
        Assert.NotEqual(TdTutorialStep.Crate, r.Ui.Step);
    }

    [Fact]
    public void Skip_marks_done_persists_and_hides_everything()
    {
        var r = new Rig();
        var skip = r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.TutorialSkip);
        r.Ui.Activate(skip);
        Assert.True(r.Store.Done);
        Assert.Equal(TdTutorialStep.None, r.Ui.Step);
        Assert.Null(r.Ui.BuildPopover());
        Assert.DoesNotContain(r.Ui.BuildHud().Items, i => i.Highlight);
        Assert.Empty(r.Sent); // nada vai ao log da simulação
    }

    [Fact]
    public void Tutorial_is_off_when_done_or_outside_stage_1_or_without_store()
    {
        Assert.Equal(TdTutorialStep.None, new Rig(tutorialDone: true).Ui.Step);
        Assert.Equal(TdTutorialStep.None, new Rig(stage: 2).Ui.Step);
        var m = Create(1);
        var ui = new TdUiController(() => m, _ => { }, new Host(), () => 0);
        ui.Sync();
        Assert.Equal(TdTutorialStep.None, ui.Step);
        Assert.Null(ui.BuildPopover());
    }

    [Fact]
    public void Tutorial_finishes_when_the_match_ends_after_the_wave_started()
    {
        var r = new Rig();
        r.BuildBolt();
        r.Click(TdUiAction.StartWave);
        Assert.False(r.Store.Done);
        for (int i = 0; i < 30 * 3000 && r.Match.Phase is MatchPhase.Preparo or MatchPhase.Onda; i++)
        {
            if (r.Match.Phase == MatchPhase.Preparo) r.Match.Apply(new StartWave());
            r.Match.Step();
        }
        Assert.True(r.Match.Phase is MatchPhase.Vitoria or MatchPhase.Derrota);
        r.Ui.Sync();
        Assert.True(r.Store.Done);
    }

    [Fact]
    public void Minimized_hud_is_expanded_once_when_the_tutorial_starts()
    {
        var r = new Rig();
        r.Host.HudMinimized = true;
        r.Ui.Sync();
        Assert.False(r.Host.HudMinimized);
        r.Host.HudMinimized = true; // depois disso o jogador manda
        r.Ui.Sync();
        Assert.True(r.Host.HudMinimized);
    }

    [Fact]
    public void Tutorial_does_not_change_the_simulation_hash_or_the_command_log()
    {
        ulong Run(bool withOnboarding)
        {
            var m = Create(1);
            var ui = withOnboarding ? new TdUiController(() => m, c => m.Apply(c is TdWaveRequest ? new StartWave() : c), new Host(), () => 0, new Store())
                : new TdUiController(() => m, c => m.Apply(c is TdWaveRequest ? new StartWave() : c), new Host(), () => 0);
            ui.Sync();
            ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.BuildToggle));
            ui.SceneLeftClick(300, 20, HitTarget.None);
            ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ChooseTower, 0, "bolt"));
            ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.StartWave));
            for (int i = 0; i < 600; i++) { m.Step(); if (i % 30 == 0) { m.ClearEvents(); ui.Sync(); ui.BuildHud(); ui.BuildPopover(); } }
            return m.ComputeStateHash();
        }
        Assert.Equal(Run(false), Run(true));
    }

    // ------------------------------------------------------------ Como jogar

    [Fact]
    public void Menu_has_how_to_play_that_opens_the_card_with_redo()
    {
        var r = new Rig(tutorialDone: true);
        r.Click(TdUiAction.Menu);
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Action == TdUiAction.HowTo);
        r.Click(TdUiAction.HowTo);
        Assert.Equal(TdUiMode.Help, r.Ui.Mode);
        var card = r.Ui.BuildPopover()!;
        Assert.Equal("Como jogar", card.Title);
        foreach (var topic in new[] { "Objetivo", "Torres", "Ondas", "Habilidades", "Caixa-bomba", "Coroa, medalhas e patente" })
            Assert.Contains(card.Items, i => i.Kind == TdUiKind.Header && i.Text == topic);
        Assert.Contains(card.Items, i => i.Action == TdUiAction.RedoTutorial);
    }

    [Fact]
    public void OpenHowTo_from_tray_and_redo_restarts_stage_1_with_tutorial_on()
    {
        var r = new Rig(tutorialDone: true);
        r.Ui.OpenHowTo();
        Assert.Equal(TdUiMode.Help, r.Ui.Mode);
        r.Click(TdUiAction.RedoTutorial);
        Assert.False(r.Store.Done);
        Assert.Contains("stage1", r.Host.Calls);
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
        Assert.Equal(TdTutorialStep.Build, r.Ui.Step);
    }

    // ------------------------------------------------------------ dicas de 1ª vez

    [Fact]
    public void Tower_tip_shows_once_for_each_type_then_never_again()
    {
        var store = new Store { Done = true };
        var r = new Rig(store: store);
        r.BuildBolt();
        r.Ui.Sync();
        Assert.StartsWith("Dica: Raio:", r.Ui.CurrentTip);
        Assert.Contains("tower:bolt", store.Tips);
        r.Now = 7; r.Ui.Sync();
        Assert.Null(r.Ui.CurrentTip); // some sozinha em ~6 s

        var again = new Rig(store: store); // novo controlador, mesmo perfil: não repete
        again.BuildBolt();
        again.Ui.Sync();
        Assert.Null(again.Ui.CurrentTip);
    }

    [Fact]
    public void Tip_disappears_on_click_and_never_blocks_the_game()
    {
        var r = new Rig(tutorialDone: true);
        r.BuildBolt();
        r.Ui.Sync();
        Assert.NotNull(r.Ui.CurrentTip);
        r.Click(TdUiAction.StartWave); // o jogo segue: clicar dispensa a dica
        Assert.Null(r.Ui.CurrentTip);
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
        Assert.Single(r.Sent.OfType<BuildTower>());
    }

    [Fact]
    public void Enemy_tip_names_the_enemy_and_the_counter_tower_once()
    {
        var r = new Rig(tutorialDone: true);
        r.BuildBolt();
        r.Ui.DismissTip();
        r.Ui.Sync();
        r.Click(TdUiAction.StartWave);
        r.Now = 100;
        for (int i = 0; i < 30 * 60 && r.Match.Enemies.Count == 0; i++) r.Match.Step();
        Assert.NotEmpty(r.Match.Enemies);
        r.Ui.Sync();
        Assert.Contains("enemy:grunt", r.Store.Tips);
        Assert.Contains("Requisição", r.Ui.CurrentTip);
        Assert.Contains("Use ", r.Ui.CurrentTip);
    }

    [Fact]
    public void Tips_wait_while_the_tutorial_callout_is_visible()
    {
        var r = new Rig();
        r.BuildBolt(); // passo Wave: balão visível
        r.Ui.Sync();
        Assert.Null(r.Ui.CurrentTip);
        Assert.DoesNotContain("tower:bolt", r.Store.Tips);
        r.Ui.Activate(r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.TutorialSkip));
        r.Ui.Sync();
        Assert.NotNull(r.Ui.CurrentTip);
    }
}
