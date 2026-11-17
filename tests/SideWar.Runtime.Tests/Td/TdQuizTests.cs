using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Input;
using SideWar.Runtime.Td;
using Xunit;
using AnswerBonus = SideWar.Core.Td.AnswerBonus;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.17 (RF-28.23, RF-28.24): pergunta-bônus V/F no Preparo, sorteio sem repetir e motivo da derrota.</summary>
public sealed class TdQuizTests
{
    private sealed class Host : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
        public void RequestExit() { }
        public bool HudMinimized { get; set; }
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

    private sealed class Store(bool bonus) : ITdOnboardingStore
    {
        public bool TutorialDone => true;
        public void SetTutorialDone(bool done) { }
        public bool TipSeen(string id) => true;
        public void MarkTipSeen(string id) { }
        public bool BonusQuestions => bonus;
    }

    private sealed class Rig
    {
        public readonly TdMatchState Match;
        public readonly List<IGameCommand> Sent = [];
        public double Now;
        public readonly TdUiController Ui;
        public string Seen = "";

        public Rig(bool bonus = true)
        {
            Match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
            Match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
            Match.Apply(new BuildTower("bolt", 500));
            var picker = new TdQuestionPicker(TdQuestionBank.LoadEmbedded(), "", n => 0, v => Seen = v);
            Ui = new TdUiController(() => Match, c => { Sent.Add(c); Match.Apply(c is TdWaveRequest ? new StartWave() : c); }, new Host(), () => Now, new Store(bonus), picker);
            Ui.Sync();
        }

        public void ToSecondPrep()
        {
            Match.Apply(new StartWave());
            for (int i = 0; i < 30 * 900 && Match.Phase == MatchPhase.Onda; i++) Match.Step();
            Assert.Equal(MatchPhase.Preparo, Match.Phase);
            Ui.Sync();
        }

        public TdUiItem Pop(TdUiAction a) => Ui.BuildPopover()!.Items.First(i => i.Action == a);
    }

    [Fact]
    public void Quiz_and_tab_share_pause_until_the_last_modal_closes()
    {
        var r = new Rig();
        r.ToSecondPrep();
        Assert.Equal(1, r.Ui.ModalCount);
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Runes));
        Assert.Equal(2, r.Ui.ModalCount);
        Assert.Single(r.Sent.OfType<Pause>());
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.QuizClose));
        Assert.Equal(1, r.Ui.ModalCount);
        Assert.True(r.Match.Paused);
        Assert.Empty(r.Sent.OfType<Resume>());
        r.Ui.Cancel();
        Assert.Equal(0, r.Ui.ModalCount);
        Assert.Single(r.Sent.OfType<Resume>());
        Assert.False(r.Match.Paused);
    }

    [Fact]
    public void Question_appears_only_in_prep_after_the_first_wave_and_never_blocks_start()
    {
        var r = new Rig();
        Assert.False(r.Ui.QuizActive); // Preparo antes da onda 1: sem pergunta
        r.ToSecondPrep();
        Assert.True(r.Ui.QuizActive);
        var pop = r.Ui.BuildPopover()!;
        Assert.StartsWith("Pergunta-bônus", pop.Title, StringComparison.Ordinal);
        Assert.True(r.Ui.QuizQuestion!.Text.Length <= TdQuestionBank.MaxTextLength);
        Assert.Contains(pop.Items, i => i.Action == TdUiAction.QuizTrue && i.Rect.H >= 30f);
        Assert.Contains(pop.Items, i => i.Action == TdUiAction.QuizFalse && i.Rect.H >= 30f);
        Assert.Contains(pop.Items, i => i.Kind == TdUiKind.Bar && i.Right == "10 s");
        var start = r.Ui.BuildHud().Items.First(i => i.Action == TdUiAction.StartWave);
        Assert.False(start.Enabled); // modal pausa a simulação
    }

    [Fact]
    public void Disabled_setting_shows_no_question()
    {
        var r = new Rig(bonus: false);
        r.ToSecondPrep();
        Assert.False(r.Ui.QuizActive);
        Assert.Null(r.Ui.BuildPopover());
    }

    [Fact]
    public void Correct_answer_logs_AnswerBonus_pays_credits_and_shows_check()
    {
        var r = new Rig();
        r.ToSecondPrep();
        var q = r.Ui.QuizQuestion!;
        long before = r.Match.Credits;
        r.Ui.Activate(r.Pop(q.Answer ? TdUiAction.QuizTrue : TdUiAction.QuizFalse));
        var cmd = Assert.IsType<AnswerBonus>(Assert.Single(r.Sent.OfType<AnswerBonus>()));
        Assert.True(cmd.Correct);
        Assert.Equal(r.Match.BonusCreditsFor(2) , r.Match.Credits - before);
        var pop = r.Ui.BuildPopover()!;
        Assert.Contains(pop.Items, i => i.Icon == TdIcon.Check && i.Text.StartsWith("Acertou", StringComparison.Ordinal));
        Assert.True(r.Match.BonusAnswered);
        r.Now += TdUiController.QuizResultSeconds + 0.1; r.Ui.Sync();
        Assert.False(r.Ui.QuizActive);
    }

    [Fact]
    public void Wrong_answer_has_no_penalty_and_shows_the_right_answer_in_6_words_or_less()
    {
        var r = new Rig();
        r.ToSecondPrep();
        var q = r.Ui.QuizQuestion!;
        long before = r.Match.Credits;
        r.Ui.Activate(r.Pop(q.Answer ? TdUiAction.QuizFalse : TdUiAction.QuizTrue));
        Assert.False(Assert.IsType<AnswerBonus>(r.Sent.OfType<AnswerBonus>().Single()).Correct);
        Assert.Equal(before, r.Match.Credits);
        var line = r.Ui.BuildPopover()!.Items.First(i => i.Icon == TdIcon.Cross);
        Assert.Equal(q.Answer ? "Era VERDADEIRO." : "Era FALSO.", line.Text);
        Assert.True(line.Text.Split(' ').Length <= 6);
        Assert.True(r.Match.Phase == MatchPhase.Preparo); // jogo segue normal
    }

    [Fact]
    public void Ignored_or_expired_question_sends_nothing_and_shows_the_answer()
    {
        var r = new Rig();
        r.ToSecondPrep();
        r.Now += 10.1; r.Ui.Sync();
        Assert.Single(r.Sent.OfType<AnswerBonus>());
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Icon == TdIcon.Clock && i.Text.StartsWith("Tempo!", StringComparison.Ordinal));
        r.Now += TdUiController.QuizWrongSeconds + 0.1; r.Ui.Sync();
        Assert.False(r.Ui.QuizActive);
        Assert.True(r.Match.BonusAnswered);

        var r2 = new Rig();
        r2.ToSecondPrep();
        r2.Ui.Activate(r2.Pop(TdUiAction.QuizClose));
        Assert.False(r2.Ui.QuizActive);
        Assert.Single(r2.Sent.OfType<AnswerBonus>());
        Assert.False(r2.Match.Paused);
    }

    [Fact]
    public void Picker_never_repeats_until_the_bank_is_exhausted_then_restarts()
    {
        var bank = TdQuestionBank.LoadEmbedded();
        string saved = "";
        var p = new TdQuestionPicker(bank, "", n => n - 1, v => saved = v);
        var ids = new HashSet<string>();
        for (int i = 0; i < bank.Questions.Length; i++) Assert.True(ids.Add(p.Pick()!.Id));
        Assert.Equal(bank.Questions.Length, saved.Split(';').Length);
        Assert.Contains(p.Pick()!.Id, ids); // esgotou: recomeça
        Assert.Single(saved.Split(';'));
        var resumed = new TdQuestionPicker(bank, "q01;q02", n => 0, _ => { });
        Assert.Equal("q03", resumed.Pick()!.Id); // retoma do "visto" persistido
    }

    [Fact]
    public void Bank_has_60_plus_short_balanced_statements_with_short_explanations()
    {
        var bank = TdQuestionBank.LoadEmbedded();
        Assert.True(bank.Questions.Length >= 60);
        Assert.Empty(TdQuestionBank.Validate(bank));
        int t = bank.Questions.Count(q => q.Answer), f = bank.Questions.Length - t;
        Assert.InRange(t * 1.0 / f, 0.8, 1.25);
        Assert.All(bank.Questions, q => Assert.True(q.Text.Length <= 60 && q.Explanation.Split(' ').Length <= 8));
    }

    [Fact]
    public void AnswerBonus_in_the_log_replays_to_the_same_hash()
    {
        ulong Run()
        {
            var r = new Rig();
            r.ToSecondPrep();
            r.Ui.Activate(r.Pop(r.Ui.QuizQuestion!.Answer ? TdUiAction.QuizTrue : TdUiAction.QuizFalse));
            return r.Match.ComputeStateHash();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Defeat_card_shows_reason_and_tip_with_icons()
    {
        var r = new Rig();
        r.Match.Apply(new StartWave());
        for (int i = 0; i < 30 * 900 && r.Match.Phase != MatchPhase.Derrota; i++)
        {
            if (r.Match.Phase == MatchPhase.Preparo) r.Match.Apply(new StartWave());
            r.Match.Step();
        }
        Assert.Equal(MatchPhase.Derrota, r.Match.Phase);
        r.Ui.Sync();
        var card = r.Ui.BuildPopover()!;
        var labels = card.Items.Where(i => i.Kind == TdUiKind.Label && i.Icon != TdIcon.None).ToArray();
        Assert.Equal(2, labels.Length);
        Assert.Equal(TdIcon.Alert, labels[0].Icon);
        Assert.All(labels, l => Assert.True(l.Text.Split(' ').Length <= 8, l.Text));
    }
}
