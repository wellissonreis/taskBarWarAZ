using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.30e: cartão "runas no primeiro dano": uma vez por perfil, no cantinho, sem foco/pausa, respeita boss key, pausa e fila de cartões.</summary>
public sealed class TdRuneTutorialTests
{
    private sealed class Host : ITdUiHost
    {
        public bool Paused { get; set; }
        public void TogglePause() { }
        public void RequestExit() { }
        public bool HudMinimized { get; set; }
        public void SetHudMinimized(bool minimized) => HudMinimized = minimized;
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

    private sealed class Store(bool tutorialDone) : ITdOnboardingStore
    {
        public readonly HashSet<string> Seen = [];
        public bool Done = tutorialDone;
        public int Marks;
        public bool TutorialDone => Done;
        public void SetTutorialDone(bool done) => Done = done;
        public bool TipSeen(string id) => Seen.Contains(id);
        public void MarkTipSeen(string id) { Seen.Add(id); Marks++; }
        public void ClearTip(string id) => Seen.Remove(id);
        public bool BonusQuestions => true;
    }

    private sealed class Rig
    {
        public TdMatchState Match;
        public readonly Host Host = new();
        public readonly Store Store;
        public readonly TdUiController Ui;
        public double Now;
        public readonly List<IGameCommand> Sent = [];

        public Rig(bool tutorialDone = true, Func<double>? draw = null)
        {
            Store = new Store(tutorialDone);
            Match = Create(7);
            var picker = new TdQuestionPicker(TdQuestionBank.LoadEmbedded(), "", n => 0, _ => { });
            Ui = new TdUiController(() => Match, c => { Sent.Add(c); Match.Apply(c); }, Host, () => Now, Store, picker, draw ?? (() => 0.0));
            Ui.Sync();
        }

        public static TdMatchState Create(ulong seed)
        {
            var m = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(seed, 54), null, TdMatchOptions.Default);
            m.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
            return m;
        }

        public void Damage(int n = 1) => typeof(TdMatchState).GetProperty("ReputationLostTotal")!.SetValue(Match, Match.ReputationLostTotal + 25 * n);
        public void Tick(double seconds = 0.5) { Now += seconds; Ui.Sync(); }
        public TdUiItem Card(TdUiAction a) => Ui.BuildPopover()!.Items.First(i => i.Action == a);
        public bool CardShown => Ui.RuneCardOpen && Ui.BuildPopover() is { } p && p.Items.Any(i => i.Action == TdUiAction.RuneTutorialOpen);
        public TdUiItem RuneBtn => Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.RuneWindow);
    }

    [Fact]
    public void No_card_without_damage_and_card_shows_on_first_damage_even_with_tutorial_done()
    {
        var r = new Rig(tutorialDone: true);
        r.Tick(); r.Tick();
        Assert.False(r.Ui.RuneCardOpen);
        Assert.False(r.Ui.RuneCardPending);
        Assert.Empty(r.Store.Seen);
        r.Damage();
        r.Tick();
        Assert.True(r.CardShown);
        Assert.True(r.Ui.CornerPopover);
        Assert.False(r.Ui.ModalOpen);
        Assert.False(r.Match.Paused);
        Assert.DoesNotContain(r.Sent, c => c is Pause);
        Assert.Contains(TdUiController.RuneTipKey, r.Store.Seen);
        var pop = r.Ui.BuildPopover()!;
        Assert.Contains(pop.Items, i => i.Kind == TdUiKind.Label && i.Text == TdUiController.RuneCardText && i.FontSize >= 14f);
        Assert.Contains(pop.Items, i => i.Text == "Abrir runas");
        Assert.Contains(pop.Items, i => i.Text == "Entendi");
        Assert.True(pop.HasArrow);
        Assert.StartsWith("Você levou dano.", TdUiController.RuneCardText);
    }

    [Fact]
    public void Card_fires_once_per_profile_and_does_not_repeat_after_more_damage_or_a_new_match()
    {
        var r = new Rig();
        r.Damage(); r.Tick();
        Assert.True(r.CardShown);
        r.Ui.Activate(r.Card(TdUiAction.RuneTutorialDismiss));
        Assert.False(r.Ui.RuneCardOpen);
        r.Damage(); r.Tick(); r.Damage(); r.Tick();
        Assert.False(r.Ui.RuneCardOpen);
        Assert.False(r.Ui.RuneCardPending);
        Assert.Equal(1, r.Store.Marks);
        r.Match = Rig.Create(9);
        r.Tick(); r.Damage(); r.Tick();
        Assert.False(r.Ui.RuneCardOpen);
        Assert.Equal(1, r.Store.Marks);
    }

    [Fact]
    public void Already_seen_profile_never_shows_it()
    {
        var r = new Rig();
        r.Store.Seen.Add(TdUiController.RuneTipKey);
        r.Damage(); r.Tick();
        Assert.False(r.Ui.RuneCardOpen);
        Assert.False(r.Ui.RuneCardPending);
    }

    [Fact]
    public void Redo_tutorial_reactivates_the_card()
    {
        var r = new Rig(tutorialDone: true);
        r.Damage(); r.Tick();
        Assert.Contains(TdUiController.RuneTipKey, r.Store.Seen);
        r.Ui.Activate(r.Card(TdUiAction.RuneTutorialDismiss));
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.RedoTutorial));
        Assert.DoesNotContain(TdUiController.RuneTipKey, r.Store.Seen);
        Assert.False(r.Store.Done);
        r.Match = Rig.Create(11);           // o host abre a fase 1 limpa
        r.Tick();
        r.Damage(); r.Tick();
        Assert.True(r.CardShown);
        Assert.Contains(TdUiController.RuneTipKey, r.Store.Seen);
    }

    [Fact]
    public void Open_runes_opens_the_hud_window_closes_the_card_and_lights_the_legend()
    {
        var r = new Rig();
        r.Damage(); r.Tick();
        r.Ui.Activate(r.Card(TdUiAction.RuneTutorialOpen));
        Assert.True(r.Ui.RuneWindowOpen);
        Assert.False(r.Ui.RuneCardOpen);
        Assert.True(r.Ui.LegendHighlight);
        var pop = r.Ui.BuildPopover()!;
        Assert.True(pop.RuneWindow);
        Assert.All(pop.Items.Where(i => i.Action == TdUiAction.RuneBranch), c => Assert.True(c.Highlight));
        r.Ui.Hover(pop.Items.First(i => i.Action == TdUiAction.RuneBranch));   // usar a legenda apaga o aviso
        Assert.False(r.Ui.LegendHighlight);
        // janela aberta pelo botão comum não acende a legenda
        r.Ui.Hover(null);
        r.Ui.Activate(r.RuneBtn);
        Assert.False(r.Ui.RuneWindowOpen);
        r.Ui.Activate(r.RuneBtn);
        Assert.True(r.Ui.RuneWindowOpen);
        Assert.False(r.Ui.LegendHighlight);
    }

    [Fact]
    public void Dismiss_buttons_and_close_x_hide_the_card_and_opening_the_window_by_hand_also_does()
    {
        var r = new Rig();
        r.Damage(); r.Tick();
        r.Ui.Activate(r.Card(TdUiAction.RuneTutorialDismiss));
        Assert.False(r.Ui.RuneCardOpen);
        Assert.Null(r.Ui.BuildPopover());
        var r2 = new Rig();
        r2.Damage(); r2.Tick();
        r2.Ui.Activate(r2.RuneBtn);
        r2.Tick();
        Assert.False(r2.Ui.RuneCardOpen);
        Assert.True(r2.Ui.RuneWindowOpen);
        Assert.False(r2.Ui.LegendHighlight);
    }

    [Fact]
    public void Card_closes_itself_after_about_twenty_seconds_of_exposure()
    {
        var r = new Rig();
        r.Damage(); r.Tick();
        r.Tick(TdUiController.RuneCardSeconds - 2);
        Assert.True(r.Ui.RuneCardOpen);
        r.Tick(3);
        Assert.False(r.Ui.RuneCardOpen);
        Assert.Null(r.Ui.BuildPopover());
    }

    [Fact]
    public void Boss_key_hides_and_cancels_the_card_and_it_does_not_come_back()
    {
        var r = new Rig();
        r.Damage(); r.Tick();
        r.Ui.SetBossHidden(true);
        Assert.False(r.Ui.RuneCardOpen);
        Assert.Null(r.Ui.BuildPopover());
        r.Ui.SetBossHidden(false);
        r.Tick();
        Assert.False(r.Ui.RuneCardOpen);
        // dano durante a boss key: espera, e só aparece depois
        var b = new Rig();
        b.Ui.SetBossHidden(true);
        b.Damage(); b.Tick();
        Assert.False(b.Ui.RuneCardOpen);
        Assert.True(b.Ui.RuneCardPending);
        Assert.Empty(b.Store.Seen);
        b.Ui.SetBossHidden(false);
        b.Tick();
        Assert.True(b.CardShown);
    }

    [Fact]
    public void Pause_hides_the_card_freezes_its_clock_and_a_pending_card_waits_for_resume()
    {
        var r = new Rig();
        r.Damage(); r.Tick();
        r.Tick(10);
        r.Host.Paused = true;
        r.Tick();
        Assert.False(r.Ui.RuneCardOpen);
        Assert.Null(r.Ui.BuildPopover());
        r.Tick(60);                              // pausado: não corre
        r.Host.Paused = false;
        r.Tick();
        Assert.True(r.Ui.RuneCardOpen);          // voltou, o tempo não foi gasto
        var p = new Rig();
        p.Host.Paused = true;
        p.Damage(); p.Tick();
        Assert.False(p.Ui.RuneCardOpen);
        Assert.True(p.Ui.RuneCardPending);
        p.Host.Paused = false;
        p.Tick();
        Assert.True(p.CardShown);
    }

    [Fact]
    public void Only_shows_when_the_rune_window_is_reachable_hud_open_and_match_running()
    {
        var r = new Rig();
        r.Host.HudMinimized = true;
        r.Damage(); r.Tick();
        Assert.False(r.Ui.RuneCardOpen);
        Assert.True(r.Ui.RuneCardPending);
        r.Host.HudMinimized = false;
        r.Tick();
        Assert.True(r.CardShown);
    }

    [Fact]
    public void Waits_its_turn_behind_the_question_card_and_never_stacks()
    {
        var r = new Rig();
        for (int i = 0; i < TdUiController.QuizMinIntervalSeconds + 1; i++) r.Tick(1.0);
        Assert.True(r.Ui.QuizActive);
        r.Damage(); r.Tick();
        Assert.False(r.Ui.RuneCardOpen);
        Assert.True(r.Ui.RuneCardPending);
        Assert.StartsWith("Pergunta-bônus", r.Ui.BuildPopover()!.Title);
        r.Ui.Activate(r.Card(TdUiAction.QuizClose));
        r.Tick();
        Assert.False(r.Ui.QuizActive);
        Assert.True(r.CardShown);
        Assert.Equal("Runas", r.Ui.BuildPopover()!.Title);
    }

    [Fact]
    public void Does_not_show_over_the_rune_window_or_a_modal_and_modal_dismisses_it()
    {
        var r = new Rig();
        r.Ui.Activate(r.RuneBtn);
        r.Damage(); r.Tick();
        Assert.False(r.Ui.RuneCardOpen);
        Assert.True(r.Ui.RuneCardPending);
        r.Ui.Activate(r.RuneBtn);                 // fecha a janela
        r.Tick();
        Assert.True(r.CardShown);
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Runes));
        r.Tick();
        Assert.False(r.Ui.RuneCardOpen);
        Assert.Equal(TdUiMode.Runes, r.Ui.Mode);
    }

    [Fact]
    public void Core_runes_raising_integrity_do_not_count_as_damage()
    {
        var r = new Rig();
        typeof(TdMatchState).GetProperty("Fragments")!.SetValue(r.Match, 1L);
        r.Tick();
        r.Match.Apply(new AwakenRune(r.Match.Frontier[0]));
        r.Tick();
        Assert.False(r.Ui.RuneCardPending);
        Assert.False(r.Ui.RuneCardOpen);
    }
}
