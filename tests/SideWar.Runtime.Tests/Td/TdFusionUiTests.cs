using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.37b (SDD-37 RF-37.10): UI da fusão: inspetor, cartão no cantinho, preview, motivo, confirmação, aviso e dica de primeira vez.</summary>
public sealed class TdFusionUiTests
{
    private sealed class Host : ITdUiHost
    {
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
    }

    private sealed class Store : ITdOnboardingStore
    {
        public readonly HashSet<string> Seen = [];
        public bool TutorialDone => true;
        public void SetTutorialDone(bool done) { }
        public bool TipSeen(string id) => Seen.Contains(id);
        public void MarkTipSeen(string id) => Seen.Add(id);
        public void ClearTip(string id) => Seen.Remove(id);
    }

    private sealed class Rig
    {
        public readonly TdMatchState Match;
        public readonly List<IGameCommand> Sent = [];
        public readonly Store Store = new();
        public readonly TdUiController Ui;
        public double Now;
        public int Tower;

        public Rig(int stage = 6, bool onboarding = false, long credits = 5000)
        {
            Match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), stage, Rng.Seed(7, 54));
            Match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
            typeof(TdMatchState).GetProperty("Credits")!.SetValue(Match, credits);
            Ui = new TdUiController(() => Match, c => { Sent.Add(c); Match.Apply(c); }, new Host(), () => Now, onboarding ? Store : null);
            Assert.True(Match.Apply(new BuildTower("frost", 500)).IsOk);
            Tower = Match.Towers[0].Id;
            Ui.Sync();
        }

        public TdUiItem Find(TdUiAction a, string? sarg = null) => Ui.BuildPopover()!.Items.First(i => i.Action == a && (sarg is null || i.SArg == sarg));
        public void OpenCard() { Ui.Activate(new TdUiItem(default, TdUiKind.Button, "T", TdUiAction.OpenTower, Tower)); Ui.Activate(Find(TdUiAction.FuseOpen)); }
        public void Credits(long v) => typeof(TdMatchState).GetProperty("Credits")!.SetValue(Match, v);
    }

    [Fact]
    public void Inspector_offers_fuse_on_phase_2_and_explains_the_lock_before()
    {
        var late = new Rig(2);
        late.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "T", TdUiAction.OpenTower, late.Tower));
        Assert.True(late.Find(TdUiAction.FuseOpen).Enabled);
        var early = new Rig(1);
        early.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "T", TdUiAction.OpenTower, early.Tower));
        var locked = early.Find(TdUiAction.FuseOpen);
        Assert.False(locked.Enabled);
        Assert.Contains("fase 2", locked.Text);
        var check = early.Match.CheckFusion(early.Tower, "bolt");
        Assert.Contains("fase 2", TdUiController.FusionReasonText(early.Match, check));
    }

    [Fact]
    public void Fuse_flow_is_a_corner_card_without_pause_and_shows_name_cost_and_effect()
    {
        var r = new Rig();
        r.OpenCard();
        Assert.True(r.Ui.FuseCardOpen);
        Assert.True(r.Ui.CornerPopover);
        Assert.Equal(TdUiMode.None, r.Ui.Mode);
        Assert.False(r.Match.Paused);
        r.Ui.Activate(r.Find(TdUiAction.FusePick, "bolt"));
        var p = r.Ui.BuildPopover()!;
        long cost = r.Match.NextTowerCost(Array.FindIndex(r.Match.Content.Towers, t => t.Id == "bolt"));
        Assert.Equal("bolt", p.IconModule);
        Assert.Contains(p.Items, i => i.Text == "Gelo elétrico");
        Assert.Contains(p.Items, i => i.Text.Contains($"Custo: ${cost}", StringComparison.Ordinal));
        Assert.Contains(p.Items, i => i.Text.Contains("dano elétrico extra", StringComparison.Ordinal));
        Assert.Contains(p.Items, i => i.Action == TdUiAction.FuseConfirm && i.Enabled);
        Assert.DoesNotContain(r.Sent, c => c is FuseTower);
    }

    [Fact]
    public void Confirm_sends_the_command_charges_and_announces_the_hybrid_in_the_corner()
    {
        var r = new Rig();
        r.OpenCard();
        r.Ui.Activate(r.Find(TdUiAction.FusePick, "bolt"));
        long before = r.Match.Credits;
        long cost = r.Match.CheckFusion(r.Tower, "bolt").Cost;
        r.Ui.Activate(r.Find(TdUiAction.FuseConfirm));
        var cmd = Assert.IsType<FuseTower>(Assert.Single(r.Sent, c => c is FuseTower));
        Assert.Equal((r.Tower, "bolt"), (cmd.BaseId, cmd.ComponentTypeId));
        Assert.Equal(before - cost, r.Match.Credits);
        Assert.False(r.Ui.FuseCardOpen);
        r.Ui.Sync();
        Assert.True(r.Ui.NoticeOpen);
        var n = r.Ui.BuildPopover()!;
        Assert.Equal("Torre fundida", n.Title);
        Assert.Contains(n.Items, i => i.Text == "Gelo elétrico");
        // o inspetor passa a usar o nome composto e a venda somada
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "T", TdUiAction.OpenTower, r.Tower));
        var insp = r.Ui.BuildPopover()!;
        Assert.StartsWith("Gelo elétrico", insp.Title, StringComparison.Ordinal);
        Assert.Contains(insp.Items, i => i.Action == TdUiAction.Sell && i.Right == $"+${r.Match.SellValue(r.Tower)}");
        Assert.DoesNotContain(insp.Items, i => i.Action == TdUiAction.FuseOpen); // sem terceira componente
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "Torres", TdUiAction.TowersOpen));
        var row = r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.OpenTower);
        Assert.Equal("Gelo elétrico", row.Text);
        Assert.Equal("bolt", row.Sub);
    }

    [Fact]
    public void Blocked_preview_shows_the_reason_and_never_sends_the_command()
    {
        var r = new Rig();
        r.OpenCard();
        r.Credits(0);
        r.Ui.Activate(r.Find(TdUiAction.FusePick, "bolt"));
        var p = r.Ui.BuildPopover()!;
        var confirm = p.Items.First(i => i.Action == TdUiAction.FuseConfirm);
        Assert.False(confirm.Enabled);
        Assert.Contains(p.Items, i => i.Text.StartsWith("Faltam $", StringComparison.Ordinal) && i.Tone == TdTone.Bad);
        r.Ui.Activate(confirm);
        Assert.DoesNotContain(r.Sent, c => c is FuseTower);
        Assert.Equal(0, r.Match.Credits);
        r.Ui.Activate(r.Find(TdUiAction.FuseBack));
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.Action == TdUiAction.FusePick);
    }

    [Fact]
    public void State_change_before_confirm_updates_the_preview_and_core_rejection_is_explained()
    {
        var r = new Rig();
        r.OpenCard();
        r.Ui.Activate(r.Find(TdUiAction.FusePick, "bolt"));
        r.Credits(0); // créditos caem depois do preview aberto
        Assert.False(r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.FuseConfirm).Enabled);
        r.Credits(5000);
        Assert.True(r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.FuseConfirm).Enabled);
    }

    [Fact]
    public void Reasons_cover_phase_limit_and_unplaced()
    {
        var r = new Rig();
        var vault = r.Match.Content.Towers.First(t => t.Id == "vault");
        Assert.NotNull(TdUiController.FusionReasonText(r.Match, new TdFusionCheck(CommandReason.TowerTypeLimit, 0, 0, "", false, "")));
        Assert.Contains("Preparo", TdUiController.FusionReasonText(r.Match, new TdFusionCheck(CommandReason.InvalidPhase, 0, 0, "", false, "")));
        Assert.Contains("posição", TdUiController.FusionReasonText(r.Match, new TdFusionCheck(CommandReason.NotPlaced, 0, 0, "", false, "")));
        Assert.Contains("2 componentes", TdUiController.FusionReasonText(r.Match, new TdFusionCheck(CommandReason.AlreadyFused, 0, 0, "", false, "")));
        Assert.Null(TdUiController.FusionReasonText(r.Match, new TdFusionCheck(CommandReason.None, 0, 0, "", false, "")));
        Assert.NotNull(vault);
    }

    [Fact]
    public void Outside_click_collapses_the_fuse_card_and_selection()
    {
        var r = new Rig();
        r.OpenCard();
        r.Ui.CollapseOutside();
        Assert.False(r.Ui.FuseCardOpen);
        Assert.Null(r.Ui.BuildPopover());
        Assert.Equal(0, r.Ui.SelectedTowerId);
    }

    [Fact]
    public void Boss_key_hides_the_card_and_cancel_closes_it()
    {
        var r = new Rig();
        r.OpenCard();
        r.Ui.SetBossHidden(true);
        Assert.False(r.Ui.FuseCardOpen);
        Assert.Null(r.Ui.BuildPopover());
        r.Ui.SetBossHidden(false);
        Assert.False(r.Ui.FuseCardOpen); // a boss key fecha o cartão; nada reaparece sozinho
        r.OpenCard();
        Assert.True(r.Ui.FuseCardOpen);
        r.Ui.Cancel();
        Assert.False(r.Ui.FuseCardOpen);
    }

    [Fact]
    public void Generic_pair_is_labelled_and_described_by_role()
    {
        var r = new Rig();
        r.OpenCard();
        r.Ui.Activate(r.Find(TdUiAction.FusePick, "frost"));
        var p = r.Ui.BuildPopover()!;
        Assert.Contains(p.Items, i => i.Text.Contains("combinação genérica", StringComparison.Ordinal));
        Assert.Contains(p.Items, i => i.Text.Contains("a cada 3 s aplica lentidão", StringComparison.Ordinal));
    }

    [Fact]
    public void Card_blocks_use_readable_fonts_and_do_not_overlap_in_both_steps()
    {
        var r = new Rig();
        r.OpenCard();
        foreach (var step in new[] { "choose", "preview" })
        {
            if (step == "preview") r.Ui.Activate(r.Find(TdUiAction.FusePick, "bolt"));
            var p = r.Ui.BuildPopover()!;
            var blocks = p.Items.Where(i => i.Text != "X").ToArray();
            foreach (var b in blocks)
            {
                Assert.True(b.Rect.X >= 0 && b.Rect.Y >= 0 && b.Rect.X + b.Rect.W <= p.Width && b.Rect.Y + b.Rect.H <= p.Height, b.Text);
                Assert.True(b.FontSize >= 14f, b.Text);
            }
            for (int i = 0; i < blocks.Length; i++)
                for (int j = i + 1; j < blocks.Length; j++)
                {
                    var a = blocks[i].Rect; var b = blocks[j].Rect;
                    Assert.False(a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H, $"{blocks[i].Text} x {blocks[j].Text}");
                }
        }
    }

    [Fact]
    public void First_time_tip_shows_once_when_fusion_unlocks_and_never_repeats()
    {
        var r = new Rig(stage: 6, onboarding: true);
        for (int i = 0; i < 3; i++) { r.Now += 0.5; r.Ui.Sync(); }
        Assert.True(r.Ui.FusionTipOpen);
        var p = r.Ui.BuildPopover()!;
        Assert.Equal("Fusão", p.Title);
        Assert.Contains(p.Items, i => i.Text.Contains("Fundir", StringComparison.Ordinal));
        Assert.True(r.Store.TipSeen(TdUiController.FusionTipKey));
        r.Ui.Activate(r.Find(TdUiAction.FusionTipDismiss));
        Assert.False(r.Ui.FusionTipOpen);
        for (int i = 0; i < 5; i++) { r.Now += 0.5; r.Ui.Sync(); }
        Assert.False(r.Ui.FusionTipOpen);
        var before = new Rig(stage: 1, onboarding: true);
        for (int i = 0; i < 3; i++) { before.Now += 0.5; before.Ui.Sync(); }
        Assert.False(before.Ui.FusionTipOpen);
        Assert.False(before.Ui.FusionTipPending);
    }

    [Fact]
    public void Tip_expires_by_itself_and_waits_for_the_boss_key()
    {
        var r = new Rig(stage: 6, onboarding: true);
        r.Ui.SetBossHidden(true);
        for (int i = 0; i < 3; i++) { r.Now += 1; r.Ui.Sync(); }
        Assert.False(r.Ui.FusionTipOpen);
        r.Ui.SetBossHidden(false);
        r.Now += 1; r.Ui.Sync();
        Assert.True(r.Ui.FusionTipOpen);
        for (int i = 0; i < 25; i++) { r.Now += 1; r.Ui.Sync(); }
        Assert.False(r.Ui.FusionTipOpen);
    }
}
