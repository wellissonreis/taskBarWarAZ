using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>TD.40c: cartão de alerta (crítico e aviso, curto e longo) a 100/150/200%: blocos empilhados sem sobreposição, fonte ≥ 14 DIP.</summary>
public sealed class TdAlertGoldenTests
{
    private static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);

    private sealed class Host(TdAlertGuard guard) : ITdUiHost
    {
        public TdGameOptions GameOptions => new(Discreet: true); // sem pulso: golden determinístico
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
        public TdAlertGuard? Alerts => guard;
    }

    private static void Set(TdAlertGuard g, string name, object? value) =>
        typeof(TdAlertGuard).GetProperty(name)!.GetSetMethod(true)!.Invoke(g, [value]);

    private static TdUiController Setup(string kind)
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54), null, TdMatchOptions.Default);
        var g = new TdAlertGuard();
        TdAlertCatalog.Test(match, TdAlertType.PassedLastTower, out _); // só garante o catálogo carregado
        var type = kind switch
        {
            "critical-long" => TdAlertType.PassedLastTower,
            "warning-short" => TdAlertType.BossIncoming,
            _ => TdAlertType.PhishingNoSentinel,
        };
        var alert = MakeAlert(type);
        if (kind.StartsWith("critical", StringComparison.Ordinal)) { Set(g, "Holding", true); Set(g, "HoldAlert", alert); }
        Set(g, "Current", alert);
        Set(g, "Raised", 1);
        var ui = new TdUiController(() => match, _ => { }, new Host(g), () => 0.0);
        ui.Sync();
        Assert.True(ui.AlertCardOpen);
        return ui;
    }

    /// <summary>Alerta do catálogo (texto real) por tipo; o catálogo só expõe via Test, então basta uma partida que o dispare à força.</summary>
    private static TdAlert MakeAlert(TdAlertType type)
    {
        var m = typeof(TdAlertCatalog).GetMethod("Make", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (TdAlert)m.Invoke(null, [type])!;
    }

    public static TheoryData<string, int> Cases()
    {
        var d = new TheoryData<string, int>();
        foreach (var kind in new[] { "critical-long", "warning-short", "warning-long" })
            foreach (int dpi in new[] { 96, 144, 192 }) d.Add(kind, dpi);
        return d;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void AlertCard_MatchesGolden(string kind, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        var p = Setup(kind).BuildPopover()!;
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi) { TextEnabled = true };
        var (pw, ph) = r.PhysicalSize(p.Width, p.Height);
        using var bmp = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(Backdrop);
        using var layer = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var lc = new SKCanvas(layer)) r.DrawPopover(lc, p, -1);
        GoldenImage.Blit(c, layer, 0, 0);
        string? failure = GoldenImage.Verify($"td-alert-{kind}-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [InlineData("critical-long")]
    [InlineData("warning-short")]
    [InlineData("warning-long")]
    public void AlertCard_blocks_do_not_overlap_and_stay_inside(string kind)
    {
        var p = Setup(kind).BuildPopover()!;
        var blocks = p.Items.Where(i => i.Text != "X").ToArray();
        Assert.True(blocks.Length >= 4);
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
        bool critical = kind.StartsWith("critical", StringComparison.Ordinal);
        Assert.Equal(critical, blocks.Any(i => i.Action == TdUiAction.AlertSave));
        Assert.Equal(!critical, blocks.Any(i => i.Action == TdUiAction.AlertMute));
    }
}
