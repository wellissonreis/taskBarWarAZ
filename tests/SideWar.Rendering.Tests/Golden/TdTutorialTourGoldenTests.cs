using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Golden;

public sealed class TdTutorialTourGoldenTests
{
    private sealed class Store : ITdOnboardingStore
    {
        public bool TutorialDone { get; private set; }
        public void SetTutorialDone(bool done) => TutorialDone = done;
        public bool TipSeen(string id) => false;
        public void MarkTipSeen(string id) { }
    }
    private sealed class Host : ITdUiHost
    {
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
        public bool PurchaseNode(string nodeId) => false;
    }

    private static void storePlacement(TdMatchState match)
    {
        match.Apply(new SideWar.Core.Td.LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        Assert.True(match.Apply(new SideWar.Core.Td.BuildTower("bolt", 500)).IsOk);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    public void Guided_page_matches_golden(int step)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia varia por sistema operacional");
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host(), () => 0, new Store());
        ui.Sync();
        ui.OpenStartupModal();
        if (step > 0) { storePlacement(match); ui.Sync(); }
        for (int i = 1; i < step; i++) ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.TutorialNext));
        var view = ui.BuildPopover()!;

        using var typo = new Typography();
        using var renderer = new TdUiRenderer(typo, 96) { TextEnabled = true };
        var (w, h) = renderer.PhysicalSize(view.Width, view.Height);
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        renderer.DrawPopover(canvas, view, -1);
        string? failure = GoldenImage.Verify($"td-tour-{step:00}", bitmap);
        Assert.True(failure is null, failure);
    }
    [Fact]
    public void First_tower_bar_matches_golden()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia varia por sistema operacional");
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host(), () => 0, new Store());
        ui.Sync();
        ui.OpenStartupModal();
        ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.QuickTowers));
        var view = ui.BuildPopover()!;
        Assert.Contains(view.Items, x => x.Action == TdUiAction.ChooseTower && x.Highlight);
        using var typo = new Typography();
        using var renderer = new TdUiRenderer(typo, 96) { TextEnabled = true };
        var (w, h) = renderer.PhysicalSize(view.Width, view.Height);
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        renderer.DrawPopover(canvas, view, -1);
        string? failure = GoldenImage.Verify("td-tour-first-tower-bar", bitmap);
        Assert.True(failure is null, failure);
    }

}
