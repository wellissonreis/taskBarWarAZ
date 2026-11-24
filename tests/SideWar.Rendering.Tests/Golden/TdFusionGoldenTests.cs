using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>TD.37b: cartão de fusão (escolha, preview habilitado, preview bloqueado, aviso e dica) a 100/150/200%: blocos sem sobreposição, fonte ≥ 14 DIP.</summary>
public sealed class TdFusionGoldenTests
{
    private static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);

    private sealed class Host : ITdUiHost
    {
        public TdGameOptions GameOptions => new(Discreet: true);
        public bool Paused => false;
        public void TogglePause() { }
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
        public bool TutorialDone => true;
        public void SetTutorialDone(bool done) { }
        public bool TipSeen(string id) => false;
        public void MarkTipSeen(string id) { }
    }

    public static readonly string[] Kinds = ["choose", "preview-ok", "preview-blocked", "preview-generic", "notice", "tip"];

    private static TdPopoverView Build(string kind)
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 6, Rng.Seed(7, 54));
        match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(match, 5000L);
        match.Apply(new BuildTower("frost", 500));
        int id = match.Towers[0].Id;
        double now = 0;
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host(), () => now, kind == "tip" ? new Store() : null);
        ui.Sync();
        if (kind == "tip")
        {
            now += 1; ui.Sync();
            Assert.True(ui.FusionTipOpen);
            return ui.BuildPopover()!;
        }
        ui.Activate(new TdUiItem(default, TdUiKind.Button, "T", TdUiAction.OpenTower, id));
        ui.Activate(ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.FuseOpen));
        string? module = kind switch { "choose" => null, "preview-generic" => "balancer", _ => "bolt" };
        if (kind == "preview-blocked") typeof(TdMatchState).GetProperty("Credits")!.SetValue(match, 10L);
        if (module is not null) ui.Activate(ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.FusePick && i.SArg == module));
        if (kind == "notice")
        {
            ui.Activate(ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.FuseConfirm));
            ui.Sync();
            Assert.True(ui.NoticeOpen);
        }
        return ui.BuildPopover()!;
    }

    public static TheoryData<string, int> Cases()
    {
        var d = new TheoryData<string, int>();
        foreach (var kind in Kinds)
            foreach (int dpi in new[] { 96, 144, 192 }) d.Add(kind, dpi);
        return d;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FusionCard_MatchesGolden(string kind, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        var p = Build(kind);
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi) { TextEnabled = true };
        var (pw, ph) = r.PhysicalSize(p.Width, p.Height);
        using var bmp = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(Backdrop);
        using var layer = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var lc = new SKCanvas(layer)) r.DrawPopover(lc, p, -1);
        GoldenImage.Blit(c, layer, 0, 0);
        string? failure = GoldenImage.Verify($"td-fusion-{kind}-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [InlineData("choose")]
    [InlineData("preview-ok")]
    [InlineData("preview-blocked")]
    [InlineData("preview-generic")]
    [InlineData("tip")]
    public void FusionCard_blocks_do_not_overlap_and_use_readable_fonts(string kind)
    {
        var p = Build(kind);
        var blocks = p.Items.Where(i => i.Text != "X").ToArray();
        Assert.NotEmpty(blocks);
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

    [Fact]
    public void Enabled_and_blocked_previews_differ()
    {
        var ok = Build("preview-ok");
        var blocked = Build("preview-blocked");
        Assert.True(ok.Items.First(i => i.Action == TdUiAction.FuseConfirm).Enabled);
        Assert.False(blocked.Items.First(i => i.Action == TdUiAction.FuseConfirm).Enabled);
        Assert.Contains(blocked.Items, i => i.Tone == TdTone.Bad && i.Text.StartsWith("Faltam", StringComparison.Ordinal));
    }
}
