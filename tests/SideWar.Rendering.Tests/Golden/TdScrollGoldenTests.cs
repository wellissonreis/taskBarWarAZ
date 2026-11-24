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

/// <summary>Menu de pausa (modal): listas longas rolam por roda/teclado e mostram a barra fina (#22385A trilho, #F5B544 polegar), sem setas.</summary>
public sealed class TdScrollGoldenTests
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

    public static readonly string[] Scenes = ["towers-top", "towers-scrolled", "build-top", "build-scrolled"];

    private static TdPopoverView Build(string scene)
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 6, Rng.Seed(7, 54), null, TdMatchOptions.Classic with { InitialReputation = TdRank.Threshold(TdRankLevel.Arquiteto) });
        match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(match, 5000L);
        string[] ids = [.. match.Content.Towers.Where(t => t.MinRank <= match.CurrentRankId).Select(t => t.Id)];
        for (int x = 100, k = 0; match.Towers.Count < 9 && x < 960; x += 80, k++) match.Apply(new BuildTower(ids[k % ids.Length], x));
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host(), () => 0.0);
        ui.Sync();
        bool towers = scene.StartsWith("towers", StringComparison.Ordinal);
        ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)(towers ? TdUiMode.Towers : TdUiMode.Build)));
        if (scene.EndsWith("scrolled", StringComparison.Ordinal)) ui.ScrollBy(2);
        return ui.BuildPopover()!;
    }

    public static TheoryData<string, int> Cases()
    {
        var d = new TheoryData<string, int>();
        foreach (var s in Scenes)
            foreach (int dpi in new[] { 96, 144 }) d.Add(s, dpi);
        return d;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ModalScroll_MatchesGolden(string scene, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        var p = Build(scene);
        Assert.NotNull(p.Scroll);
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi) { TextEnabled = true };
        var (pw, ph) = r.PhysicalSize(p.Width, p.Height);
        using var bmp = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(Backdrop);
        using var layer = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var lc = new SKCanvas(layer)) r.DrawPopover(lc, p, -1);
        GoldenImage.Blit(c, layer, 0, 0);
        string? failure = GoldenImage.Verify($"td-modal-scroll-{scene}-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }

    [Fact]
    public void Scrollbar_thumb_moves_down_with_the_offset_and_uses_the_design_colors()
    {
        var top = Build("towers-top").Scroll!.Value;
        var down = Build("towers-scrolled").Scroll!.Value;
        Assert.True(down.Thumb().Y > top.Thumb().Y);
        Assert.Equal(0x22, SideWar.Rendering.Theme.Palette.Td.BorderStrong.Red);
        Assert.Equal(0xF5, SideWar.Rendering.Theme.Palette.Td.Amber.Red);
    }
}
