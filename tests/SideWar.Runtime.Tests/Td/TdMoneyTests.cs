using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.33d (SDD-33 peça E): dinheiro visível. T-33.18 a T-33.23 (só apresentação: nada no Core nem no hash).</summary>
public sealed class TdMoneyTests
{
    private const double Dt = 1.0 / 30.0;

    // ---------------------------------------------------------------- modelo puro

    [Fact] // T-33.18
    public void Counting_is_monotonic_converges_exactly_within_600ms_and_never_overshoots()
    {
        var m = new TdMoneyModel();
        m.Reset(1000, 0, 0, 0.0);
        double now = 0.0; long frame = 0;
        m.Update(1500, ++frame, now, false, 0);
        Assert.Equal(1500, m.Real);
        long prev = m.Display;
        int intermediates = 0;
        double settledAt = -1;
        for (int i = 1; i <= 30; i++)
        {
            now += Dt;
            m.Update(1500, ++frame, now, false, 0);
            Assert.InRange(m.Display, prev, 1500);
            if (m.Display is > 1000 and < 1500) intermediates++;
            prev = m.Display;
            if (m.Display == 1500 && settledAt < 0) settledAt = now;
        }
        Assert.True(intermediates >= 3, $"intermediários: {intermediates}");
        Assert.InRange(settledAt, 0.01, 0.6 + 1e-9);
        Assert.Equal(1500, m.Display);
        Assert.False(m.Animating);
    }

    [Fact] // T-33.18
    public void A_purchase_drops_the_displayed_value_immediately()
    {
        var m = new TdMoneyModel();
        m.Reset(1000, 0, 0, 0.0);
        m.Update(1500, 1, 0.0, false, 0);
        m.Update(1500, 2, 0.1, false, 0);
        Assert.True(m.Display < 1500);
        m.Update(1100, 3, 0.1, false, 0);
        Assert.Equal(1100, m.Display);
        Assert.Equal(1100, m.Real);
    }

    [Fact] // T-33.19
    public void Ten_kills_in_300ms_make_one_floating_number_with_the_sum()
    {
        var m = new TdMoneyModel();
        m.Reset(0, 0, 0, 0.0);
        long credits = 0;
        for (int i = 0; i < 10; i++) { credits += 7; m.Update(credits, i + 1, i * 0.03, false, 0); }
        Assert.Single(m.Floats);
        Assert.Equal("+70", m.Floats[0].Text);
    }

    [Fact] // T-33.19
    public void Gains_apart_by_more_than_400ms_are_distinct_and_never_more_than_three_live()
    {
        var m = new TdMoneyModel();
        m.Reset(0, 0, 0, 0.0);
        m.Update(10, 1, 0.0, false, 0);
        for (int i = 1; i <= 5; i++) m.Update(10, 1 + i, i * 0.1, false, 0);
        m.Update(25, 7, 0.6, false, 0);
        Assert.Equal(2, m.Floats.Count);
        Assert.Equal("+10", m.Floats[0].Text);
        Assert.Equal("+15", m.Floats[1].Text);
        long credits = 25; double now = 0.5;
        for (int i = 0; i < 400; i++)
        {
            now += 0.1; credits += 3;
            m.Update(credits, 3 + i, now, false, 0);
            Assert.True(m.Floats.Count <= TdMoneyModel.MaxFloats);
        }
        // sobe até 12 DIP e some em 0,7 s
        m.Update(credits + 5, 999, now + 0.1, false, 0);
        Assert.All(m.Floats, f => Assert.InRange(f.Rise, 0f, TdMoneyModel.FloatRiseDip));
        for (int i = 1; i <= 10; i++) m.Update(credits + 5, 1000 + i, now + 0.1 + i * 0.1, false, 0);
        Assert.Empty(m.Floats);
    }

    [Fact] // T-33.20
    public void Income_is_the_ten_second_game_clock_average_and_purchases_do_not_count()
    {
        var m = new TdMoneyModel { ShowIncome = true };
        m.Reset(0, 0, 0, 0.0);
        long credits = 0, frame = 0; double now = 0;
        for (int i = 0; i < 300; i++) { credits++; now += Dt; m.Update(credits, ++frame, now, false, 0); }
        Assert.Equal("+30/s", m.IncomeText);
        credits -= 100; m.Update(credits, ++frame, now += Dt, false, 0); // compra
        Assert.Equal("+30/s", m.IncomeText);
        // a janela anda com o Frame (tempo de jogo), não com o relógio real: parado, o valor não muda
        m.Update(credits, frame, now + 60.0, false, 0);
        Assert.Equal("+30/s", m.IncomeText);
        for (int i = 0; i < 300; i++) m.Update(credits, ++frame, now += Dt, false, 0);
        Assert.Null(m.IncomeText);
    }

    [Fact] // T-33.20
    public void Income_off_neither_draws_nor_accumulates()
    {
        var m = new TdMoneyModel { ShowIncome = false };
        m.Reset(0, 0, 0, 0.0);
        for (int i = 1; i <= 100; i++) m.Update(i * 5, i, i * Dt, false, 0);
        Assert.Null(m.IncomeText);
    }

    [Fact] // T-33.21
    public void Buy_glow_fires_once_per_upward_crossing_with_hysteresis()
    {
        var m = new TdMoneyModel();
        m.Reset(99, 0, 100, 0.0);
        double now = 0; long f = 0;
        m.Update(100, ++f, now, false, 100);
        Assert.True(m.BuyGlow > 0f);
        for (int i = 0; i < 40; i++) m.Update(100, ++f, now += Dt, false, 100);
        Assert.Equal(0f, m.BuyGlow);                       // ≈ 800 ms depois apagou
        m.Update(130, ++f, now += Dt, false, 100);
        Assert.Equal(0f, m.BuyGlow);                       // ficar acima não repete
        m.Update(80, ++f, now += Dt, false, 100);
        m.Update(100, ++f, now += Dt, false, 100);
        Assert.True(m.BuyGlow > 0f);                       // caiu e voltou: outro brilho
    }

    [Fact] // T-33.21
    public void Changing_the_selected_tower_threshold_does_not_fire_the_glow()
    {
        var m = new TdMoneyModel();
        m.Reset(60, 0, 100, 0.0);
        m.Update(60, 1, 0.1, false, 50);
        Assert.Equal(0f, m.BuyGlow);
    }

    [Fact] // T-33.21
    public void Wealth_milestones_glow_once_per_run()
    {
        var m = new TdMoneyModel();
        m.Reset(999, 0, 0, 0.0);
        double now = 0; long f = 0;
        m.Update(1000, ++f, now, false, 0);
        Assert.True(m.MilestoneGlow > 0f);
        for (int i = 0; i < 40; i++) m.Update(1000, ++f, now += Dt, false, 0);
        Assert.Equal(0f, m.MilestoneGlow);
        m.Update(400, ++f, now += Dt, false, 0);
        m.Update(1200, ++f, now += Dt, false, 0);
        Assert.Equal(0f, m.MilestoneGlow);                 // o marco de 1.000 já foi
        m.Update(10_050, ++f, now += Dt, false, 0);
        Assert.True(m.MilestoneGlow > 0f);                 // 10.000: novo marco
    }

    [Fact] // T-33.21
    public void Discreet_mode_jumps_the_value_has_no_glow_and_keeps_only_big_floats()
    {
        var m = new TdMoneyModel { Discreet = true };
        m.Reset(999, 0, 100, 0.0);
        m.Update(1000, 1, 0.0, false, 100);
        Assert.Equal(1000, m.Display);
        Assert.Equal(0f, m.MilestoneGlow);
        Assert.Equal(0f, m.BuyGlow);
        Assert.Empty(m.Floats);                            // +1 é pequeno
        Assert.False(m.Animating);
        for (int i = 1; i <= 8; i++) m.Update(1000, 1 + i, 0.0 + i * 0.125, false, 100);
        m.Update(1100, 10, 1.125, false, 100);
        Assert.Single(m.Floats);                           // +100 ≥ 50
        Assert.Equal("+100", m.Floats[0].Text);
        Assert.Equal(1100, m.Display);
    }

    [Fact] // RF-33.22
    public void Frozen_clock_stops_the_animation_and_schedules_nothing()
    {
        var m = new TdMoneyModel();
        m.Reset(0, 0, 0, 0.0);
        m.Update(1000, 1, 0.0, false, 0);
        m.Update(1000, 2, 0.1, false, 0);
        long mid = m.Display;
        Assert.InRange(mid, 1, 999);
        Assert.True(m.Animating);
        m.Update(1000, 2, 5.0, true, 0);                   // pausa: o tempo não anda
        m.Update(1000, 2, 9.0, true, 0);
        Assert.Equal(mid, m.Display);
        Assert.False(m.Animating);
        m.Update(1000, 2, 9.5, false, 0);                  // retoma de onde parou
        Assert.True(m.Display >= mid && m.Display < 1000 || m.Display == 1000);
    }

    [Fact]
    public void A_refund_is_not_an_income_or_a_floating_gain()
    {
        var m = new TdMoneyModel { ShowIncome = true };
        m.Reset(100, 0, 0, 0.0);
        m.NoteRefund();
        m.Update(150, 1, 0.1, false, 0);
        Assert.Empty(m.Floats);
        Assert.Null(m.IncomeText);
        m.Update(160, 2, 0.2, false, 0);
        Assert.Single(m.Floats);
    }

    // ---------------------------------------------------------------- controlador / HUD

    private sealed class Host : ITdUiHost
    {
        public TdGameOptions GameOptions { get; set; } = new();
        public void SetGameOptions(TdGameOptions options) => GameOptions = options;
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
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

    private sealed class Rig
    {
        public readonly TdMatchState Match;
        public readonly TdUiController Ui;
        public readonly Host Host = new();
        public double Now;

        public Rig(TdGameOptions? opts = null)
        {
            Match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54), null, TdMatchOptions.Default);
            Match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
            if (opts is not null) Host.GameOptions = opts;
            Ui = new TdUiController(() => Match, c => Match.Apply(c), Host, () => Now);
            Ui.Sync();
        }
    }

    [Fact] // T-33.18
    public void Money_item_is_big_tabular_amber_and_replaces_the_credits_in_the_chip()
    {
        var r = new Rig();
        var hud = r.Ui.BuildHud();
        var money = hud.Items.Single(i => i.Kind == TdUiKind.Money);
        Assert.True(TdHudView.MoneyFontDip >= 16f);
        Assert.Equal(r.Match.Credits.ToString(System.Globalization.CultureInfo.InvariantCulture), money.Text);
        var chip = hud.Items.Single(i => i.Action == TdUiAction.HudRestore);
        Assert.DoesNotContain('¢', chip.Text);
        Assert.Contains("20/20", chip.Text);
        Assert.Equal(TdHudView.MoneyWidth(4), TdHudView.MoneyWidth(1));   // tabular: o contador não dança até 4 dígitos
        Assert.True(TdHudView.MoneyWidth(5) > TdHudView.MoneyWidth(4));
        Assert.False(hud.OverButton(money.Rect.X + money.Rect.W / 2f, 14f)); // não clicável: o clique passa
        Assert.False(string.IsNullOrEmpty(money.Tip));
    }

    [Fact]
    public void Hud_has_space_for_credit_counter_and_controls()
    {
        var r = new Rig();
        r.Match.Apply(new SideWar.Core.Td.BuildTower("bolt", 600));
        r.Match.Apply(new StartWave());
        var hud = r.Ui.BuildHud();
        var money = hud.Items.Single(i => i.Kind == TdUiKind.Money);
        var rune = hud.Items.Single(i => i.Action == TdUiAction.RuneWindow);
        Assert.True(money.Rect.X + money.Rect.W <= rune.Rect.X);
        Assert.All(hud.Items, i => Assert.True(i.Rect.X + i.Rect.W <= hud.Width + 0.01f));
    }

    [Fact]
    public void Long_credit_value_expands_without_overlapping_rune_button()
    {
        var r = new Rig();
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(r.Match, 123456789L);
        var hud = r.Ui.BuildHud();
        var money = hud.Items.Single(i => i.Kind == TdUiKind.Money);
        var rune = hud.Items.Single(i => i.Action == TdUiAction.RuneWindow);
        Assert.True(hud.Width > 720);
        Assert.Equal("123456789", money.Text);
        Assert.True(money.Rect.X + money.Rect.W < rune.Rect.X);
        Assert.Equal(TdUiAction.RuneWindow, hud.Items[hud.HitTest(rune.Rect.X + 14, rune.Rect.Y + 14)].Action);
        foreach (var speed in hud.Items.Where(i => i.Action == TdUiAction.SetSpeed))
            Assert.Equal(speed.Arg, hud.Items[hud.HitTest(speed.Rect.X + 15, speed.Rect.Y + 11)].Arg);
    }

    [Fact] // T-33.21
    public void Towers_button_glows_only_on_upward_crossings_of_the_cheapest_tower_cost()
    {
        var r = new Rig();
        long Cost() => Enumerable.Range(0, r.Match.Content.Towers.Length).Where(i => r.Match.StageId >= r.Match.Content.Towers[i].UnlockStage).Min(i => r.Match.NextTowerCost(i));
        Assert.Equal(0f, r.Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.QuickTowers).Fill); // já podia comprar ao abrir: sem brilho
        string Cheapest() => r.Match.Content.Towers.Where((t, i) => r.Match.StageId >= t.UnlockStage).MinBy(t => r.Match.NextTowerCost(Array.IndexOf(r.Match.Content.Towers, t)))!.Id;
        for (int x = 100; r.Match.Credits >= Cost() && x < 900; x += 90) r.Match.Apply(new SideWar.Core.Td.BuildTower(Cheapest(), x));
        Assert.True(r.Match.Credits < Cost(), $"credits {r.Match.Credits} custo {Cost()} torres {r.Match.Towers.Count}");
        r.Now += Dt; r.Ui.BuildHud();
        r.Match.Apply(new StartWave());
        int edges = 0; bool lit = false;
        for (int i = 0; i < 20000 && edges < 2; i++)
        {
            r.Match.Step(); r.Now += Dt;
            bool on = r.Ui.BuildHud().Items.Single(it => it.Action == TdUiAction.QuickTowers).Fill > 0f;
            if (on && !lit) { edges++; Assert.True(r.Match.Credits >= Cost(), "brilho só com créditos suficientes"); }
            lit = on;
        }
        Assert.True(edges >= 1, "esperava ao menos um cruzamento");
    }

    [Fact] // T-33.19
    public void Floating_numbers_get_a_transparent_top_strip_only_while_visible_and_never_take_clicks()
    {
        var r = new Rig();
        var rest = r.Ui.BuildHud();
        Assert.Equal(0f, rest.TopPad);
        Assert.Equal(40f, rest.Height);
        r.Match.Apply(new SideWar.Core.Td.BuildTower("bolt", 300));
        r.Match.Apply(new StartWave());
        for (int i = 0; i < 4000 && r.Ui.Money.Floats.Count == 0; i++) { r.Match.Step(); r.Now += Dt; r.Ui.BuildHud(); }
        var hud = r.Ui.BuildHud();
        Assert.NotEmpty(hud.Floats!);
        Assert.Equal(TdHudView.FloatPad, hud.TopPad);
        Assert.Equal(40f + TdHudView.FloatPad, hud.Height);
        Assert.All(hud.Items, it => Assert.True(it.Rect.Y >= hud.TopPad));
        Assert.False(hud.OverButton(5f, 4f));                       // a faixa do topo é transparente ao mouse
        var money = hud.Items.Single(i => i.Kind == TdUiKind.Money);
        Assert.False(hud.OverButton(money.Rect.X + 4, money.Rect.Y + 4));
    }

    [Fact] // T-33.22
    public void Boss_key_stops_all_money_animation_and_comes_back_on_the_real_value()
    {
        var r = new Rig();
        r.Ui.BuildHud();
        r.Ui.SetBossHidden(true);
        Assert.False(r.Ui.MoneyAnimating);
        for (int i = 0; i < 90; i++) r.Match.Step();
        r.Now += 5;
        r.Ui.BuildHud();                                   // oculto: não atualiza nada
        Assert.False(r.Ui.MoneyAnimating);
        r.Ui.SetBossHidden(false);
        var hud = r.Ui.BuildHud();
        Assert.Equal(r.Match.Credits.ToString(System.Globalization.CultureInfo.InvariantCulture), hud.Items.Single(i => i.Kind == TdUiKind.Money).Text);
        Assert.False(r.Ui.MoneyAnimating);
        Assert.Empty(hud.Floats ?? []);
    }

    [Fact] // T-33.22
    public void Idle_money_schedules_no_animation()
    {
        var r = new Rig();
        for (int i = 0; i < 5; i++) { r.Now += 1; r.Ui.BuildHud(); }
        Assert.False(r.Ui.MoneyAnimating);
    }

    [Fact] // T-33.21
    public void Discreet_option_reaches_the_model_and_show_income_option_too()
    {
        var r = new Rig(new TdGameOptions(ShowIncome: true, Discreet: true));
        r.Ui.BuildHud();
        Assert.True(r.Ui.Money.Discreet);
        Assert.True(r.Ui.Money.ShowIncome);
        r.Host.SetGameOptions(new TdGameOptions());
        r.Ui.BuildHud();
        Assert.False(r.Ui.Money.Discreet);
        Assert.False(r.Ui.Money.ShowIncome);
    }

    [Fact] // T-33.23
    public void State_hash_is_identical_with_the_money_piece_on_off_or_discreet()
    {
        ulong Run(TdGameOptions? opts, bool observe)
        {
            var r = new Rig(opts);
            r.Match.Apply(new SideWar.Core.Td.BuildTower("bolt", 600));
            r.Match.Apply(new StartWave());
            for (int i = 0; i < 600; i++)
            {
                r.Match.Step();
                if (observe) { r.Now += Dt; r.Ui.BuildHud(); }
            }
            return r.Match.ComputeStateHash();
        }
        ulong off = Run(null, false);
        Assert.Equal(off, Run(new TdGameOptions(), true));
        Assert.Equal(off, Run(new TdGameOptions(ShowIncome: true), true));
        Assert.Equal(off, Run(new TdGameOptions(Discreet: true), true));
    }
}
