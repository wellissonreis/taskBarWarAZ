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

/// <summary>TD.17/TD.30b (RF-28.23, RF-28.24): pergunta-bônus V/F por tempo real, sorteio sem repetir e motivo da derrota.</summary>
public sealed class TdQuizTests
{
    private sealed class Host : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
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

        public Rig(bool bonus = true, double draw = 0.0)
        {
            Match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
            Match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
            Match.Apply(new BuildTower("bolt", 500));
            var picker = new TdQuestionPicker(TdQuestionBank.LoadEmbedded(), "", n => 0, v => Seen = v);
            Ui = new TdUiController(() => Match, c => { Sent.Add(c); Match.Apply(c is TdWaveRequest ? new StartWave() : c); }, new Host(), () => Now, new Store(bonus), picker, () => draw);
            Ui.Sync();
        }

        /// <summary>Joga <paramref name="seconds"/> de relógio real (despertares de 1 s) com o jogo rodando.</summary>
        public void Run(double seconds)
        {
            for (double t = 0; t < seconds; t += 1.0) { Now += 1.0; Ui.Sync(); }
        }

        /// <summary>Relógio falso até o primeiro intervalo cheio (draw 0 ⇒ 240 s): a pergunta aparece.</summary>
        public void OfferQuestion()
        {
            Run(TdUiController.QuizMinIntervalSeconds + 1);
            Assert.True(Ui.QuizActive);
        }

        public TdUiItem Pop(TdUiAction a) => Ui.BuildPopover()!.Items.First(i => i.Action == a);
    }

    [Fact]
    public void Quiz_is_a_corner_card_that_never_pauses_nor_opens_the_modal()
    {
        var r = new Rig();
        r.OfferQuestion();
        Assert.True(r.Ui.QuizActive);
        Assert.True(r.Ui.CornerPopover);
        Assert.False(r.Ui.ModalOpen);
        Assert.Equal(0, r.Ui.ModalCount);
        Assert.DoesNotContain(r.Sent, c => c is Pause);
        Assert.False(r.Match.Paused);
        Assert.DoesNotContain(r.Ui.BuildPopover()!.Items, i => i.Action == TdUiAction.ModalTab);
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Runes));
        Assert.Equal(1, r.Ui.ModalCount);
        r.Ui.Cancel();
        Assert.Equal(0, r.Ui.ModalCount);
        Assert.False(r.Match.Paused);
    }

    [Fact]
    public void Question_appears_only_after_a_full_real_time_interval_and_never_blocks_start()
    {
        var r = new Rig();
        Assert.False(r.Ui.QuizActive); // abrir o app não dispara na hora
        r.Run(TdUiController.QuizMinIntervalSeconds - 5);
        Assert.False(r.Ui.QuizActive);
        r.Run(5);
        Assert.True(r.Ui.QuizActive);
        var pop = r.Ui.BuildPopover()!;
        Assert.StartsWith("Pergunta-bônus", pop.Title, StringComparison.Ordinal);
        Assert.True(r.Ui.QuizQuestion!.Text.Length <= TdQuestionBank.MaxTextLength);
        Assert.Contains(pop.Items, i => i.Action == TdUiAction.QuizTrue && i.Rect.H >= 30f);
        Assert.Contains(pop.Items, i => i.Action == TdUiAction.QuizFalse && i.Rect.H >= 30f);
        Assert.Contains(pop.Items, i => i.Kind == TdUiKind.Bar && i.Right == "30 s");
        var start = r.Ui.BuildHud().Items.First(i => i.Action == TdUiAction.StartWave);
        Assert.True(start.Enabled); // cartão no cantinho não pausa nem bloqueia Iniciar
    }

    [Theory]
    [InlineData(0.0, 240.0)]
    [InlineData(0.5, 300.0)]
    [InlineData(0.999, 359.88)]
    public void Interval_is_drawn_between_4_and_6_minutes_independent_of_waves_and_speed(double draw, double expected)
    {
        var r = new Rig(draw: draw);
        Assert.Equal(expected, r.Ui.QuizIntervalSeconds, 2);
        // ondas e velocidade 3x do jogo não mexem no relógio: só o relógio real conta
        r.Match.Apply(new StartWave());
        for (int i = 0; i < 400; i++) r.Match.Step();
        r.Run(expected - 2);
        Assert.False(r.Ui.QuizActive);
        r.Run(3);
        Assert.True(r.Ui.QuizActive);
    }

    [Fact]
    public void Question_is_offered_during_a_wave_too_and_not_tied_to_wave_changes()
    {
        var r = new Rig();
        r.Match.Apply(new StartWave());
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
        r.OfferQuestion();
        for (int i = 0; i < 30 * 900 && r.Match.Phase == MatchPhase.Onda; i++) r.Match.Step(); // a onda acaba com o cartão aberto
        r.Ui.Sync();
        Assert.True(r.Ui.QuizActive); // trocar de onda/fase não repete nem some com a pergunta
        r.Ui.Activate(r.Pop(TdUiAction.QuizTrue));
        Assert.Single(r.Sent.OfType<AnswerBonus>());
    }

    [Fact]
    public void Paused_game_and_open_modal_freeze_the_clock()
    {
        var r = new Rig();
        r.Run(100);
        double before = r.Ui.QuizElapsedSeconds;
        r.Match.Apply(new Pause());
        r.Run(500);
        Assert.Equal(before, r.Ui.QuizElapsedSeconds);
        Assert.False(r.Ui.QuizActive);
        r.Match.Apply(new Resume());
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Runes));
        Assert.True(r.Ui.ModalOpen);
        r.Run(500);
        Assert.False(r.Ui.QuizActive);
        r.Ui.Cancel();
        r.Ui.SetBossHidden(true); // boss key: usuário ausente, nada conta
        r.Run(500);
        Assert.Equal(before, r.Ui.QuizElapsedSeconds);
        r.Ui.SetBossHidden(false);
        r.Run(10);
        Assert.Equal(before + 10, r.Ui.QuizElapsedSeconds, 3);
    }

    [Fact]
    public void Ignoring_the_question_restarts_the_clock_without_punishment_or_accumulation()
    {
        var r = new Rig();
        r.OfferQuestion();
        long credits = r.Match.Credits;
        r.Run(30.1); // expira pelo timer existente
        Assert.Equal(credits, r.Match.Credits);
        Assert.Equal(1, r.Match.BonusWrongCount);
        r.Run(TdUiController.QuizWrongSeconds + 1);
        Assert.False(r.Ui.QuizActive);
        // relógio reiniciado: o tempo passado exibindo/ignorando a pergunta não acumulou
        Assert.True(r.Ui.QuizElapsedSeconds < 20);
        r.Run(TdUiController.QuizMinIntervalSeconds - 25);
        Assert.False(r.Ui.QuizActive);
        r.Run(28);
        Assert.True(r.Ui.QuizActive);
    }

    [Fact]
    public void Only_one_question_at_a_time()
    {
        var r = new Rig();
        r.OfferQuestion();
        string id = r.Ui.QuizQuestion!.Id;
        r.Run(5);
        Assert.Equal(id, r.Ui.QuizQuestion!.Id);
        Assert.Empty(r.Sent.OfType<AnswerBonus>());
    }

    [Fact]
    public void Question_card_uses_the_larger_fonts_and_grows_in_height_within_the_corner_width()
    {
        var r = new Rig();
        r.OfferQuestion();
        var pop = r.Ui.BuildPopover()!;
        Assert.True(pop.Width <= 360f);
        var statement = pop.Items.First(i => i.Kind == TdUiKind.Label && i.Icon == TdIcon.Question);
        Assert.True(statement.FontSize >= 15f);
        Assert.Equal(r.Ui.QuizQuestion!.Text, statement.Text);
        Assert.True(statement.Rect.H >= statement.FontSize * 1.3f);
        var opts = pop.Items.Where(i => i.Action is TdUiAction.QuizTrue or TdUiAction.QuizFalse).ToArray();
        Assert.Equal(2, opts.Length);
        Assert.All(opts, o => Assert.True(o.FontSize >= 14f));
        Assert.All(pop.Items, i => Assert.True(i.Rect.X + i.Rect.W <= pop.Width + 0.01f));
        Assert.True(pop.Items.Max(i => i.Rect.Y + i.Rect.H) <= pop.Height);
        Assert.True(TdUiController.QuizLines("a b c d e f g h i j k l m n o p q r s t u v w x y z a b c d e f g h", 332f, 15f) > 1);
    }

    [Fact]
    public void Disabled_setting_shows_no_question()
    {
        var r = new Rig(bonus: false);
        r.Run(TdUiController.QuizMaxIntervalSeconds + 60);
        Assert.False(r.Ui.QuizActive);
        Assert.Null(r.Ui.BuildPopover());
    }

    [Fact]
    public void Correct_answer_logs_AnswerBonus_pays_credits_and_shows_check()
    {
        var r = new Rig();
        r.OfferQuestion();
        var q = r.Ui.QuizQuestion!;
        long before = r.Match.Credits;
        r.Ui.Activate(r.Pop(q.Answer ? TdUiAction.QuizTrue : TdUiAction.QuizFalse));
        var cmd = Assert.IsType<AnswerBonus>(Assert.Single(r.Sent.OfType<AnswerBonus>()));
        Assert.True(cmd.Correct);
        Assert.Equal(r.Match.BonusCreditsFor(1), r.Match.Credits - before);
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
        r.OfferQuestion();
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
        r.OfferQuestion();
        r.Now += 30.1; r.Ui.Sync();
        Assert.Single(r.Sent.OfType<AnswerBonus>());
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Icon == TdIcon.Clock && i.Text.StartsWith("Tempo!", StringComparison.Ordinal));
        r.Now += TdUiController.QuizWrongSeconds + 0.1; r.Ui.Sync();
        Assert.False(r.Ui.QuizActive);
        Assert.True(r.Match.BonusAnswered);

        var r2 = new Rig();
        r2.OfferQuestion();
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
            r.OfferQuestion();
            r.Ui.Activate(r.Pop(r.Ui.QuizQuestion!.Answer ? TdUiAction.QuizTrue : TdUiAction.QuizFalse));
            return r.Match.ComputeStateHash();
        }
        Assert.Equal(Run(), Run());
    }

}
