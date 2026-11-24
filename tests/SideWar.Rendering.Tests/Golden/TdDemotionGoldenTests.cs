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

public sealed class TdDemotionGoldenTests
{
    private sealed class Host : ITdUiHost
    {
        public TdGameOptions GameOptions => new(Discreet: true); // sem pulso: golden determinístico
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
        public int Score { get; set; } = 5800;
        public TdRankProgress Rank => TdRank.Progress(Score);
        public TdRecordOutcome LastOutcome => default;
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => true;
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public void DemotionCard_MatchesGolden(int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "golden com texto no Linux");
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
        var host = new Host();
        var ui = new TdUiController(() => match, _ => { }, host, () => 0);
        ui.Sync();
        host.Score = 4800;
        ui.Sync();
        var p = ui.BuildPopover()!;
        using var typo = new Typography();
        using var renderer = new TdUiRenderer(typo, dpi) { TextEnabled = true };
        var (width, height) = renderer.PhysicalSize(p.Width, p.Height);
        using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(0x1b, 0x22, 0x30));
        using var layer = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var lc = new SKCanvas(layer)) renderer.DrawPopover(lc, p, -1);
        GoldenImage.Blit(canvas, layer, 0, 0);
        Assert.Null(GoldenImage.Verify($"td-demotion-{dpi * 100 / 96}", bmp));
    }
}
