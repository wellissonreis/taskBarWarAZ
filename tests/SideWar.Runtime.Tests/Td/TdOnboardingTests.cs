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
using AnswerBonus = SideWar.Core.Td.AnswerBonus;

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
        public bool Bonus;
        public bool BonusQuestions => Bonus;
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
        public double Now { get; set; }
        public readonly TdUiController Ui;

        public readonly TdQuestionPicker? Picker;
        public string Seen = "";

        public Rig(int stage = 1, Store? store = null, bool tutorialDone = false, bool intro = true, bool bonus = false, bool hudMin = false)
        {
            Host.HudMinimized = hudMin;
            Match = Create(stage);
            Store = store ?? new Store { Done = tutorialDone };
            Store.Bonus = bonus;
            Picker = new TdQuestionPicker(TdQuestionBank.LoadEmbedded(), "", n => 0, v => Seen = v);
            Ui = new TdUiController(() => Match, Send, Host, () => Now, Store, Picker);
            Ui.Sync();
            if (intro && !Store.Done && stage == 1) PassIntro();
        }

        /// <summary>HUD abre → recolhe → abre de novo; depois OK em núcleo, créditos e portal.</summary>
        public void PassIntro()
        {
            Click(TdUiAction.HudRestore); Ui.Sync();            // abriu
            Click(TdUiAction.HudMinimize); Ui.Sync();         // recolheu
            Click(TdUiAction.HudRestore); Ui.Sync();          // abriu de novo
            Click(TdUiAction.TutorialNext); Click(TdUiAction.TutorialNext); Click(TdUiAction.TutorialNext);
        }

        public void Ack() => Click(TdUiAction.TutorialNext);

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
            Ui.ClickPoint(300);
            Click(TdUiAction.ChooseTower, "bolt");
            Ui.SceneLeftClick(300, 20, HitTarget.None);
            Ui.Cancel();
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
    public void Every_tutorial_line_has_at_most_8_words_and_an_icon()
    {
        var r = new Rig(intro: false, hudMin: true);
        var seen = new List<TdUiItem>();
        void Grab() { if (r.Ui.BuildPopover() is { } p) seen.AddRange(p.Items.Where(i => i.Kind == TdUiKind.Label)); }
        Grab();
        r.Click(TdUiAction.HudRestore); r.Ui.Sync(); Grab();
        r.Click(TdUiAction.HudMinimize); r.Ui.Sync(); r.Click(TdUiAction.HudRestore); r.Ui.Sync(); Grab();
        for (int i = 0; i < 3; i++) { r.Ack(); Grab(); }
        Assert.NotEmpty(seen);
        foreach (var l in seen.Where(i => i.Tone == TdTone.Accent))
        {
            Assert.True(l.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8, l.Text);
            Assert.NotEqual(TdIcon.None, l.Icon);
        }
    }

    [Fact]
    public void Hurt_hint_is_only_for_the_first_match()
    {
        var r = new Rig(tutorialDone: true);
        r.BuildBolt();
        r.Click(TdUiAction.StartWave);
        typeof(TdMatchState).GetProperty("Integrity")!.SetValue(r.Match, r.Match.Integrity - 1);
        r.Ui.Sync();
        Assert.Equal(TdTutorialStep.None, r.Ui.Step);
    }

    [Fact]
    public void Tutorial_is_off_when_done_or_without_store_and_runs_on_any_start_stage()
    {
        Assert.Equal(TdTutorialStep.None, new Rig(tutorialDone: true).Ui.Step);
        // TD.29b: a corrida contínua pode começar em qualquer fase (fase sugerida); o tutorial não depende mais da fase 1.
        Assert.NotEqual(TdTutorialStep.None, new Rig(stage: 2).Ui.Step);
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
    public void Tutorial_does_not_change_the_simulation_hash_or_the_command_log()
    {
        ulong Run(bool withOnboarding)
        {
            var m = Create(1);
            var ui = withOnboarding ? new TdUiController(() => m, c => m.Apply(c is TdWaveRequest ? new StartWave() : c), new Host(), () => 0, new Store())
                : new TdUiController(() => m, c => m.Apply(c is TdWaveRequest ? new StartWave() : c), new Host(), () => 0);
            ui.Sync();
            ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.BuildToggle));
            ui.ClickPoint(300);
            ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ChooseTower, 0, "bolt"));
            ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.StartWave));
            for (int i = 0; i < 600; i++) { m.Step(); if (i % 30 == 0) { m.ClearEvents(); ui.Sync(); ui.BuildHud(); ui.BuildPopover(); } }
            return m.ComputeStateHash();
        }
        Assert.Equal(Run(false), Run(true));
    }

    // ------------------------------------------------------------ Como jogar

    [Fact]
    public void Redo_tutorial_preserves_the_current_match()
    {
        var r = new Rig(stage: 2, tutorialDone: true);
        r.BuildBolt();
        r.Click(TdUiAction.StartWave);
        r.StepWave(30);
        typeof(TdMatchState).GetProperty("Fragments")!.SetValue(r.Match, 4L);
        string rune = r.Match.Frontier[0];
        Assert.True(r.Match.Apply(new AwakenRune(rune)).IsOk);
        var match = r.Match;
        var hash = match.ComputeStateHash();
        var credits = match.Credits;
        var wave = match.WaveNumber;
        Assert.NotEmpty(match.Towers);
        Assert.True(wave > 0);
        r.Ui.OpenHowTo();
        Assert.Equal(TdUiMode.Help, r.Ui.Mode);
        r.Click(TdUiAction.RedoTutorial);
        Assert.False(r.Store.Done);
        Assert.DoesNotContain(r.Host.Calls, call => call.StartsWith("stage", StringComparison.Ordinal));
        Assert.Same(match, r.Match);
        Assert.Equal(hash, match.ComputeStateHash());
        Assert.Equal(2, match.StageId);
        Assert.Equal(credits, match.Credits);
        Assert.Equal(wave, match.WaveNumber);
        Assert.Equal(3, match.Fragments);
        Assert.True(match.IsAwakened(rune));
        Assert.NotEmpty(match.Towers);
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
        Assert.Equal(TdTutorialStep.Runes, r.Ui.Step);
        Assert.NotNull(r.Ui.BuildPopover());
    }

    // ------------------------------------------------------------ dicas de 1ª vez

    [Fact]
    public void Tutorial_teaches_chip_modal_close_without_changing_pause_between_tabs()
    {
        var r = new Rig(intro: false, hudMin: true);
        Assert.Equal(TdTutorialStep.HudOpen, r.Ui.Step);
        Assert.True(r.Ui.CurrentTip!.Split(' ').Length <= 8);
        Assert.Null(r.Ui.BuildPopover());
        r.Click(TdUiAction.HudRestore); r.Ui.Sync();
        Assert.Equal(TdTutorialStep.HudCollapse, r.Ui.Step);
        Assert.True(r.Match.Paused);
        int pauses = r.Sent.OfType<Pause>().Count();
        r.Click(TdUiAction.ModalTab, arg: (int)TdUiMode.Runes);
        Assert.Equal(pauses, r.Sent.OfType<Pause>().Count());
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.SArg == "rhint" && i.Text.StartsWith("Sem fragmentos", StringComparison.Ordinal));
        r.Ui.Cancel(); r.Ui.Sync();
        Assert.False(r.Match.Paused);
        Assert.Single(r.Sent.OfType<Resume>());
    }

    [Fact]
    public void First_wave_gate_survives_modal_and_real_placement()
    {
        var r = new Rig(intro: false);
        Assert.True(r.Ui.StartBlocked);
        r.Click(TdUiAction.HudRestore);
        Assert.False(r.Hud(TdUiAction.StartWave).Enabled);
        r.Click(TdUiAction.ChooseTower, "bolt");
        Assert.False(r.Match.Paused);
        r.Ui.SceneLeftClick(300, 20, HitTarget.None);
        r.Ui.Cancel();
        Assert.False(r.Ui.StartBlocked);
        Assert.True(r.Hud(TdUiAction.StartWave).Enabled);
    }

    [Fact]
    public void How_to_is_a_paused_tab_with_skippable_tutorial()
    {
        var r = new Rig(intro: false);
        r.Ui.OpenHowTo();
        Assert.True(r.Match.Paused);
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Action == TdUiAction.RedoTutorial);
        r.Click(TdUiAction.TutorialSkip);
        Assert.True(r.Store.Done);
        r.Ui.Cancel();
        Assert.False(r.Match.Paused);
    }
}
