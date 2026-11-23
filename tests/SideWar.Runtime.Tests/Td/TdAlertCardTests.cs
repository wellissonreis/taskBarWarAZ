using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Td;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using BuildTower = SideWar.Core.Td.BuildTower;
using SellTower = SideWar.Core.Td.SellTower;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.40c: cartão de alerta (crítico e aviso), clique-fora, boss key, silenciar e pulso (sem foco, sem mouse).</summary>
public sealed class TdAlertCardTests
{
    private static readonly TdContent Embedded = TdContentLoader.LoadEmbedded();

    private static readonly TdContent Content = Embedded;

    private sealed class Host(TdGameHost game, bool discreet = false) : ITdUiHost
    {
        public List<string> Calls { get; } = [];
        public int Mask;
        public TdGameOptions GameOptions => new(Discreet: discreet);
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
        public void RequestExit() { }
        public bool HudMinimized => false;
        public void SetHudMinimized(bool minimized) { }
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
        public TdAlertGuard? Alerts => game.Alerts;
        public void SaveTower() { Calls.Add("save"); game.SaveTower(); }
        public void LetTowerFall() { Calls.Add("fall"); game.LetTowerFall(); }
        public void AlertsMutedChanged(int mutedMask) { Mask = mutedMask; Calls.Add("mask"); }
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly AppStateMachine App = new();
        public readonly TdMatchState Match = TdMatchState.Create(Content, 1, Rng.Seed(42, 54));
        public readonly TdGameHost Game;
        public readonly Host Host;
        public readonly TdUiController Ui;
        public double Now;

        public Rig(bool autoPause, bool enabled, bool discreet = false)
        {
            Game = new TdGameHost(Clock, App, Match);
            Game.Alerts.Settings.AutoPause = autoPause;
            Game.Alerts.Settings.Enabled = enabled;
            Host = new Host(Game, discreet);
            Ui = new TdUiController(() => Match, c => Game.OnCommand(c), Host, () => Now);
            App.LeaveHidden(HiddenReason.Starting);
            Game.OnCommand(new LayoutChanged(StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 1200)])));
            // O Core exige uma torre posicionada para iniciar; usa uma provisória e vende em seguida (campo sem torres).
            Game.OnCommand(new BuildTower("bolt", 900));
            Game.OnWake(Clock);
            Game.OnCommand(new StartWave());
            Game.OnWake(Clock);
            Game.OnCommand(new SellTower(Match.Towers[0].Id));
            Game.OnWake(Clock);
        }

        /// <summary>Em Preparo, inicia a próxima onda com uma torre provisória vendida em seguida (o Core exige torre para iniciar).</summary>
        public void StartBareIfPreparo()
        {
            if (Match.Phase != MatchPhase.Preparo) return;
            Game.OnCommand(new BuildTower("bolt", 900)); Game.OnWake(Clock);
            Game.OnCommand(new StartWave()); Game.OnWake(Clock);
            Game.OnCommand(new SellTower(Match.Towers[^1].Id)); Game.OnWake(Clock);
        }

        public void Run(double seconds)
        {
            int n = (int)Math.Round(seconds * 30);
            long step = (long)(Clock.TicksPerSecond / 30);
            for (int i = 0; i < n; i++) { Clock.AdvanceTicks(step); Now += 1.0 / 30; Game.OnWake(Clock); Ui.Sync(); }
        }

        public void RunUntil(Func<bool> done, double max = 600) { for (double t = 0; t < max && !done(); t++) Run(1); }
    }

    private static Rig Held()
    {
        var r = new Rig(autoPause: true, enabled: true);
        r.RunUntil(() => r.Game.Alerts.Holding);
        Assert.True(r.Game.Alerts.Holding);
        return r;
    }

    [Fact]
    public void Critical_card_offers_save_and_fall_and_does_not_collapse_on_outside_click()
    {
        var r = Held();
        Assert.True(r.Ui.AlertCritical);
        Assert.True(r.Ui.CornerPopover);
        var p = r.Ui.BuildPopover()!;
        Assert.Equal(TdTone.Bad, p.AlertTone);
        Assert.Contains(p.Items, i => i.Action == TdUiAction.AlertSave && i.Text == "Salvar (voltar ao jogo)");
        Assert.Contains(p.Items, i => i.Action == TdUiAction.AlertFall && i.Text == "Deixar cair");
        Assert.DoesNotContain(p.Items, i => i.Action is TdUiAction.AlertDismiss or TdUiAction.AlertMute or TdUiAction.Close);
        r.Ui.CollapseOutside();
        r.Ui.Cancel(); // ESC também não decide pelo jogador
        Assert.NotNull(r.Ui.BuildPopover());
        Assert.True(r.Game.Alerts.Holding);
    }

    [Fact]
    public void Save_releases_hold_and_closes_the_card()
    {
        var r = Held();
        var save = r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.AlertSave);
        r.Ui.Activate(save);
        Assert.Contains("save", r.Host.Calls);
        Assert.False(r.Game.Alerts.Holding);
        Assert.False(r.Ui.AlertCardOpen);
    }

    [Fact]
    public void Fall_authorizes_defeat_and_closes_the_card()
    {
        var r = Held();
        r.Ui.Activate(r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.AlertFall));
        Assert.Contains("fall", r.Host.Calls);
        Assert.True(r.Game.Alerts.FallAuthorized);
        Assert.False(r.Ui.AlertCardOpen);
    }

    [Fact]
    public void Boss_key_hides_the_card_and_it_returns_afterwards()
    {
        var r = Held();
        r.Ui.SetBossHidden(true);
        Assert.False(r.Ui.AlertCardOpen);
        Assert.Null(r.Ui.BuildPopover());
        Assert.True(r.Game.Alerts.Holding); // a retenção continua: ausência nunca perde
        r.Ui.SetBossHidden(false);
        Assert.True(r.Ui.AlertCardOpen);
    }

    private static Rig Warning()
    {
        var r = new Rig(autoPause: false, enabled: true);
        r.RunUntil(() => r.Game.Alerts.Current is not null);
        r.Ui.Sync();
        Assert.NotNull(r.Game.Alerts.Current);
        return r;
    }

    [Fact]
    public void Warning_card_has_dismiss_and_mute_and_collapses_on_outside_click()
    {
        var r = Warning();
        Assert.True(r.Ui.AlertCardOpen);
        Assert.False(r.Ui.AlertCritical);
        var p = r.Ui.BuildPopover()!;
        Assert.Contains(p.Items, i => i.Action == TdUiAction.AlertDismiss && i.Text == "Entendi");
        Assert.Contains(p.Items, i => i.Action == TdUiAction.AlertMute);
        Assert.DoesNotContain(p.Items, i => i.Action is TdUiAction.AlertSave or TdUiAction.AlertFall);
        r.Ui.CollapseOutside();
        Assert.False(r.Ui.AlertCardOpen);
    }

    [Fact]
    public void Mute_silences_the_type_persists_mask_and_hides_the_card()
    {
        var r = Warning();
        var type = r.Game.Alerts.Current!.Value.Type;
        r.Ui.Activate(r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.AlertMute));
        Assert.True(r.Game.Alerts.Settings.IsMuted(type));
        Assert.Equal(r.Game.Alerts.Settings.MutedMask, r.Host.Mask);
        Assert.False(r.Ui.AlertCardOpen);
    }

    [Fact]
    public void Warning_card_expires_on_its_own()
    {
        var r = Warning();
        r.Now += TdUiController.AlertCardSeconds + 1;
        Assert.False(r.Ui.AlertCardOpen);
    }

    [Fact]
    public void New_alert_after_outside_click_calls_the_player_back_and_pulses()
    {
        var r = Warning();
        r.Ui.CollapseOutside();
        Assert.False(r.Ui.AlertCardOpen);
        int before = r.Game.Alerts.Raised;
        r.RunUntil(() => { r.StartBareIfPreparo(); return r.Game.Alerts.Raised > before; }, 300);
        r.Ui.Sync();
        Assert.True(r.Game.Alerts.Raised > before, "o cenário deve gerar um segundo alerta (cooldown de 60 s)");
        Assert.True(r.Ui.AlertCardOpen);
        Assert.True(r.Ui.AlertPulsing);
    }

    [Fact]
    public void Pulse_runs_only_at_first_and_never_in_discreet_mode()
    {
        var r = Warning();
        Assert.True(r.Ui.AlertPulsing);
        Assert.InRange(r.Ui.AlertPulse, 0f, 1f);
        r.Now += TdUiController.AlertPulseSeconds + 0.1;
        Assert.Equal(0f, r.Ui.AlertPulse);
        var d = new Rig(autoPause: false, enabled: true, discreet: true);
        d.RunUntil(() => d.Game.Alerts.Current is not null);
        d.Ui.Sync();
        Assert.True(d.Ui.AlertCardOpen);
        Assert.Equal(0f, d.Ui.AlertPulse);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Card_blocks_are_measured_without_overlap_and_fonts_are_at_least_14(bool critical)
    {
        var r = critical ? Held() : Warning();
        var p = r.Ui.BuildPopover()!;
        var blocks = p.Items.Where(i => i.Text != "X").ToArray();
        foreach (var b in blocks)
        {
            Assert.True(b.Rect.X >= 0 && b.Rect.Y >= 0 && b.Rect.X + b.Rect.W <= p.Width && b.Rect.Y + b.Rect.H <= p.Height, b.Text);
            Assert.True(b.FontSize >= 14f, b.Text);
            if (b.Kind == TdUiKind.Label)
                Assert.True(TdUiController.QuizLines(b.Text, b.Rect.W - 16f, b.FontSize) * b.FontSize * TdUiController.QuizLineFactor <= b.Rect.H, b.Text);
        }
        for (int i = 0; i < blocks.Length; i++)
            for (int j = i + 1; j < blocks.Length; j++)
            {
                var a = blocks[i].Rect; var b = blocks[j].Rect;
                Assert.False(a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H, $"{blocks[i].Text} x {blocks[j].Text}");
            }
    }
}
