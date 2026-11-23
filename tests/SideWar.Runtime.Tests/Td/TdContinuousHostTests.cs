using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Input;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;
using SellTower = SideWar.Core.Td.SellTower;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.28b: host da corrida contínua (onda seguinte sem Preparo, aviso discreto de fase, boss key, painel de torres fixo).</summary>
public sealed class TdContinuousHostTests
{
    private const int Width = 1000;
    private static readonly TdContent Base = TdContentLoader.LoadEmbedded();

    private static TdContent Tiny(int waves = 2)
    {
        var defs = Enumerable.Range(1, waves).Select(i => new TdWaveDef(i, false, 1.0, [new TdWaveGroupDef("grunt", 1, 0.0, 1.0)])).ToArray();
        var stages = Base.Stages.Stages.Select(s => s.Id == 1 ? s with { Waves = waves, StartingCredits = 5000 } : s).ToArray();
        var waveStages = Base.Waves.Stages.Select(s => s.Stage == 1 ? new TdStageWavesDef(1, defs) : s).ToArray();
        return Base with { Waves = new TdWavesDef(waveStages), Stages = Base.Stages with { Stages = stages } };
    }

    private sealed class FakeHost : ITdUiHost
    {
        public TdGameOptions GameOptions { get; private set; } = new();
        public void SetGameOptions(TdGameOptions options) => GameOptions = options;
        public int RunStartStage { get; set; } = 1;
        public List<string> Calls { get; } = [];
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
        public void RequestExit() { }
        public bool HudMinimized { get; set; } = true;
        public void SetHudMinimized(bool minimized) => HudMinimized = minimized;
        public void StartStage(int stageId) => Calls.Add($"stage{stageId}");
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => true;
        public int StageStars(int stageId) => 0;
        public bool StageCrown(int stageId) => false;
        public int StageMedals(int stageId) => 0;
        public int StageBestScore(int stageId) => 0;
        public TdRankProgress Rank => TdRank.Progress(0);
        public TdRecordOutcome LastOutcome { get; set; }
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => true;
    }

    private sealed class Store : ITdOnboardingStore
    {
        public bool TutorialDone => true;
        public void SetTutorialDone(bool done) { }
        public bool TipSeen(string id) => true;
        public void MarkTipSeen(string id) { }
        public bool BonusQuestions => true;
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly AppStateMachine App = new();
        public readonly TdFocusState Focus = new();
        public readonly FakeHost Host = new();
        public readonly TdMatchState Match;
        public readonly TdGameHost Game;
        public readonly TdUiController Ui;
        public readonly List<IGameCommand> Sent = [];
        public double Now;

        public Rig(TdMatchOptions? options = null, bool quiz = false, TdContent? content = null, double draw = 0.9)
        {
            Match = TdMatchState.Create(content ?? Tiny(), 1, Rng.Seed(42, 54), null, options ?? TdMatchOptions.Default);
            Match.Apply(new LayoutChanged(StripLayout.Create(Width, Width - 46, 88, 4, Width - 80, TowerHost.Band, [new FreeZone(32, Width - 32)])));
            App.LeaveHidden(HiddenReason.Starting);
            Game = new TdGameHost(Clock, App, Match, null, Focus);
            TdQuestionPicker? picker = quiz ? new TdQuestionPicker(TdQuestionBank.LoadEmbedded(), "", n => 0, _ => { }) : null;
            Ui = new TdUiController(() => Match, c => { Sent.Add(c); Game.OnCommand(c); }, Host, () => Now, quiz ? new Store() : null, picker, () => draw); // draw 0,9 ⇒ intervalo de 348 s
            Ui.Sync();
            Game.OnWake(Clock);
        }

        public void Run(double seconds)
        {
            int n = (int)Math.Round(seconds * 30);
            long step = (long)(Clock.TicksPerSecond / 30);
            for (int i = 0; i < n; i++)
            {
                Clock.AdvanceTicks(step);
                Now += 1.0 / 30;
                Game.OnWake(Clock);
                Ui.Sync();
            }
        }

        public void RunUntil(Func<bool> done, double maxSeconds = 600)
        {
            for (double t = 0; t < maxSeconds && !done(); t += 0.5) Run(0.5);
            Assert.True(done());
        }

        /// <summary>O Core exige torre posicionada para iniciar: constrói uma provisória, inicia e vende em seguida.</summary>
        public void Start()
        {
            Game.OnCommand(new BuildTower("bolt", 900)); Game.OnWake(Clock);
            Game.OnCommand(new StartWave()); Game.OnWake(Clock);
            Game.OnCommand(new SellTower(Match.Towers[^1].Id)); Game.OnWake(Clock);
            Ui.Sync();
        }
    }

    [Fact]
    public void Next_wave_starts_without_preparo_and_start_button_only_exists_in_the_initial_preparo()
    {
        var r = new Rig();
        r.Ui.Activate(r.Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.HudExpand));
        Assert.Contains(r.Ui.BuildHud().Items, i => i.Action == TdUiAction.StartWave);
        Assert.Contains("Fase 1", r.Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.HudRestore).Tip);
        r.Start();
        Assert.DoesNotContain(r.Ui.BuildHud().Items, i => i.Action == TdUiAction.StartWave);
        r.RunUntil(() => r.Match.WaveNumber == 2);
        Assert.Equal(MatchPhase.Onda, r.Match.Phase); // nunca voltou ao Preparo
        Assert.DoesNotContain(r.Ui.BuildHud().Items, i => i.Action == TdUiAction.StartWave);
        Assert.Equal("2/2", r.Ui.BuildHud().Items.Single(i => i.SArg == "wave").Text);
    }

    [Fact]
    public void Classic_mode_keeps_the_start_button_and_preparo_between_waves()
    {
        var r = new Rig(TdMatchOptions.Classic);
        r.Ui.Activate(r.Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.HudExpand));
        r.Start();
        r.RunUntil(() => r.Match.Phase == MatchPhase.Preparo);
        Assert.Contains(r.Ui.BuildHud().Items, i => i.Action == TdUiAction.StartWave);
        Assert.DoesNotContain("Fase", r.Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.HudRestore).Text);
    }

    [Fact]
    public void Stage_cleared_shows_a_small_corner_notice_that_vanishes_by_itself_and_never_pauses()
    {
        var r = new Rig();
        r.Start();
        r.RunUntil(() => r.Match.StageResults.Count == 1);
        Assert.Equal(MatchPhase.Onda, r.Match.Phase); // a corrida segue na fase 2
        Assert.Equal(2, r.Match.StageNumber);
        Assert.True(r.Ui.NoticeOpen);
        Assert.True(r.Ui.CornerPopover);
        Assert.False(r.Ui.ModalOpen);
        var pop = r.Ui.BuildPopover()!;
        Assert.Contains(pop.Items, i => i.Kind == TdUiKind.Label && i.Text.StartsWith("Fase 1", StringComparison.Ordinal) && i.Icon == TdIcon.Check);
        Assert.True(pop.Height < 90f);
        Assert.Empty(r.Sent.OfType<Pause>());
        Assert.Contains("Fase 2", r.Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.HudRestore).Tip);
        r.Now += TdUiController.StageNoticeSeconds + 0.1;
        r.Ui.Sync();
        Assert.False(r.Ui.NoticeOpen);
        Assert.Null(r.Ui.BuildPopover());
    }

    [Fact]
    public void Notice_mentions_crown_and_record_from_the_profile_outcome()
    {
        var r = new Rig();
        r.Host.LastOutcome = new TdRecordOutcome(true, true, TdMedals.None, 0);
        r.Start();
        r.RunUntil(() => r.Match.StageResults.Count == 1);
        string text = r.Ui.NoticeText!;
        Assert.StartsWith("Fase 1", text);
        Assert.Equal(r.Match.StageResults[0].Crown, text.Contains("+coroa"));
        Assert.Contains("+recorde", text);
    }

    [Fact]
    public void Restart_action_in_a_persistent_run_always_uses_the_run_start_stage()
    {
        var r = new Rig();
        r.Host.RunStartStage = 4;
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Restart));
        Assert.Equal("stage4", Assert.Single(r.Host.Calls));
        var classic = new Rig(TdMatchOptions.Classic);
        classic.Host.RunStartStage = 4;
        classic.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Restart));
        Assert.Equal("stage1", Assert.Single(classic.Host.Calls));
    }

    [Fact]
    public void Bonus_question_appears_on_the_real_time_cadence_as_a_corner_card_without_pausing()
    {
        var r = new Rig(quiz: true); // sem Iniciar: o Preparo inicial conta (a cadência não depende da fase) e nada vaza
        r.Run(60);
        Assert.False(r.Ui.QuizActive); // cadência por tempo real (TD.30b): nada de pergunta a cada onda
        r.Run(290); // 350 s de relógio real no total: acima do intervalo sorteado (348 s)
        Assert.True(r.Ui.QuizActive);
        Assert.True(r.Ui.CornerPopover);
        Assert.False(r.Ui.ModalOpen);
        Assert.Empty(r.Sent.OfType<Pause>());
        long before = r.Match.Credits;
        var item = r.Ui.BuildPopover()!.Items.First(i => i.Action is TdUiAction.QuizTrue);
        r.Ui.Activate(item);
        r.Ui.Activate(r.Ui.BuildPopover()!.Items.FirstOrDefault(i => i.Action == TdUiAction.QuizClose));
        r.Run(0.2);
        Assert.Contains(r.Sent.OfType<AnswerBonus>(), _ => true);
        Assert.True(r.Match.BonusAnswered);
        Assert.True(r.Match.Credits >= before);
    }

    [Fact]
    public void Boss_key_hides_everything_keeps_the_match_running_and_marks_absent()
    {
        var r = new Rig();
        r.Start();
        var scene = new List<bool>();
        var ui = new List<bool>();
        int frames = 0;
        var boss = new TdBossKey(h => { ui.Add(h); r.Ui.SetBossHidden(h); }, scene.Add, r.Focus, () => r.App.IsVisible, () => frames++);
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu)); // modal aberta (pausa)
        Assert.True(r.Ui.ModalOpen);
        r.Focus.OnInteraction();
        Assert.True(boss.Toggle());
        Assert.False(r.Ui.ModalOpen);
        Assert.False(r.Ui.PopoverOpen);
        Assert.Null(r.Ui.BuildPopover());
        Assert.Equal([false], scene);
        Assert.False(r.Focus.IsPresent); // ausente
        Assert.True(r.App.SimulationRunning); // a partida continua
        r.Run(0.1); // processa Resume
        long frame = r.Match.Frame;
        r.Run(2);
        Assert.True(r.Match.Frame > frame);
        Assert.False(r.Match.Paused);
        Assert.False(r.Match.Present);
        Assert.False(boss.Toggle());
        Assert.Equal([false, true], scene);
        Assert.Equal(1, frames);
        Assert.True(r.Focus.IsPresent);
        Assert.Equal([true, false], ui);
        boss.Toggle();
        boss.Show();
        Assert.False(boss.Hidden);
    }

    [Fact]
    public void Boss_key_does_not_touch_a_scene_already_hidden_by_the_app_and_blocks_new_cards()
    {
        var r = new Rig(quiz: true);
        var scene = new List<bool>();
        var boss = new TdBossKey(r.Ui.SetBossHidden, scene.Add, r.Focus, () => false, () => { });
        boss.Toggle();
        Assert.Empty(scene);
        r.Run(TdUiController.QuizMaxIntervalSeconds + 10);
        Assert.False(r.Ui.QuizActive); // o relógio não conta com a janela escondida: a pergunta espera a volta
        boss.Toggle();
        r.Run(350);
        Assert.True(r.Ui.QuizActive);
    }

    [Fact]
    public void Pinned_tower_panel_stays_open_without_taking_esc_and_follows_the_setting()
    {
        var r = new Rig();
        r.Host.SetGameOptions(new TdGameOptions(PanelPinned: true));
        r.Ui.Sync();
        Assert.True(r.Ui.QuickTowersOpen);
        Assert.True(r.Ui.CornerPopover);
        Assert.False(r.Ui.PopoverOpen); // em repouso o ESC não é registrado
        Assert.False(r.Ui.ModalOpen);
        Assert.Empty(r.Sent.OfType<Pause>());
        // o botão ≡ não fecha o painel fixo
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.QuickTowers));
        Assert.True(r.Ui.QuickTowersOpen);
        // abrir o Menu troca o painel; fechar volta o painel
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        Assert.False(r.Ui.QuickTowersOpen);
        Assert.True(r.Ui.ModalOpen);
        r.Ui.Cancel();
        Assert.True(r.Ui.QuickTowersOpen);
        Assert.False(r.Ui.ModalOpen);
        // colocar uma torre: ESC cancela só a colocação e o painel continua
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ChooseTower, SArg: "bolt"));
        Assert.True(r.Ui.BuildActive);
        Assert.True(r.Ui.PopoverOpen);
        r.Ui.Cancel();
        Assert.False(r.Ui.BuildActive);
        Assert.True(r.Ui.QuickTowersOpen);
        // minimizar pelo atalho fecha; ao voltar reabre
        r.Ui.SetBossHidden(true);
        Assert.False(r.Ui.QuickTowersOpen);
        r.Ui.Sync();
        Assert.False(r.Ui.QuickTowersOpen);
        r.Ui.SetBossHidden(false);
        Assert.True(r.Ui.QuickTowersOpen);
        // desligar fecha
        r.Host.SetGameOptions(new TdGameOptions(PanelPinned: false));
        r.Ui.Sync();
        Assert.False(r.Ui.QuickTowersOpen);
        Assert.Null(r.Ui.BuildPopover());
    }

    [Fact]
    public void Unpinned_panel_is_closed_by_default_and_notice_goes_over_the_pinned_panel()
    {
        var r = new Rig();
        Assert.False(r.Ui.QuickTowersOpen);
        r.Host.SetGameOptions(new TdGameOptions(PanelPinned: true));
        r.Ui.Sync();
        r.Start();
        r.RunUntil(() => r.Match.StageResults.Count == 1);
        Assert.True(r.Ui.NoticeOpen);
        Assert.Equal("Fase concluida", r.Ui.BuildPopover()!.Title);
        r.Now += TdUiController.StageNoticeSeconds + 0.1;
        r.Ui.Sync();
        Assert.StartsWith("TORRES", r.Ui.BuildPopover()!.Title);
    }

    [Fact]
    public void Game_tab_offers_continuous_waves_and_pinned_panel_toggles()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        var pop = r.Ui.BuildPopover()!;
        Assert.Contains(pop.Items, i => i.Text.StartsWith("Ondas contínuas", StringComparison.Ordinal) && i.Arg == 6);
        Assert.Contains(pop.Items, i => i.Text.StartsWith("Painel de torres fixo", StringComparison.Ordinal) && i.Arg == 7);
        Assert.Contains(pop.Items, i => i.Text == "Reiniciar corrida");
        r.Ui.Activate(pop.Items.First(i => i.Arg == 6 && i.Action == TdUiAction.GameOption));
        Assert.False(r.Host.GameOptions.Continuous);
        r.Ui.Activate(r.Ui.BuildPopover()!.Items.First(i => i.Arg == 7 && i.Action == TdUiAction.GameOption));
        Assert.True(r.Host.GameOptions.PanelPinned);
    }

    [Fact]
    public void Chip_tooltip_and_help_show_the_boss_hotkey()
    {
        var r = new Rig();
        Assert.Contains("Ctrl+Alt+H", r.Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.HudRestore).Tip);
        r.Ui.OpenHowTo();
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Text.Contains("Ctrl+Alt+H"));
    }

    [Fact]
    public void Replay_script_carries_the_match_options()
    {
        var r = new Rig();
        r.Start();
        var script = r.Game.BuildReplay(1, 42, 54);
        Assert.Same(r.Match.Options, script.Options);
        Assert.True(script.Options!.ContinuousWaves);
        var classic = new Rig(TdMatchOptions.Classic);
        Assert.True(classic.Game.BuildReplay(1, 42, 54).Options!.IsClassic);
    }
}
