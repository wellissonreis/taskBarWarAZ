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

public sealed class TdBestiaryGoldenTests
{
    [Theory]
    [InlineData("ransomware")]
    [InlineData("botnet")]
    public void Legacy_bosses_have_enraged_sheets(string id)
    {
        Assert.True(TdAnimArt.TryEnragedFps(id, out var fps));
        Assert.True(fps > 0);
        using var stream = typeof(TdAnimArt).Assembly.GetManifestResourceStream(TdAnimArt.Resource(id + "_enf-walk.png"));
        Assert.NotNull(stream);
        using var bitmap = SKBitmap.Decode(stream);
        Assert.Equal(4 * bitmap.Height, bitmap.Width);
    }

    [Fact]
    public void Every_design_entry_and_variant_has_an_embedded_four_frame_sheet()
    {
        foreach (var entry in TdBestiary.Entries)
        foreach (var art in new[] { entry.Art }.Concat(entry.Variants.Select(v => v.Art)))
        {
            using var stream = typeof(TdUiRenderer).Assembly.GetManifestResourceStream("SideWar.Rendering.Assets.Bestiary." + art + ".png");
            Assert.NotNull(stream);
            using var bitmap = SKBitmap.Decode(stream);
            Assert.Equal(128, bitmap.Width);
            Assert.Equal(32, bitmap.Height);
        }
    }

    private sealed class Host : ITdUiHost
    {
        public readonly Dictionary<string, int> Seen = [];
        public int EncounterCount(string id) => Seen.GetValueOrDefault(id);
        public TdGameOptions GameOptions => new();
        public void SetGameOptions(TdGameOptions options) { }
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

    [Theory]
    [InlineData(96, false)] [InlineData(144, false)]
    [InlineData(96, true)] [InlineData(144, true)]
    public void Modal_matches_artwork(int dpi, bool known)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "golden Linux");
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
        var host = new Host();
        if (known) { host.Seen["swarm"] = 12; host.Seen["swarm:elite"] = 1; }
        var ui = new TdUiController(() => match, c => match.Apply(c), host);
        ui.Activate(new(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Bestiary));
        if (known) ui.Hover(ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.BestiarySelect));
        var view = ui.BuildPopover()!;
        using var typography = new Typography();
        using var renderer = new TdUiRenderer(typography, dpi);
        var (w, h) = renderer.PhysicalSize(view.Width, view.Height);
        using var image = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(image)) renderer.DrawPopover(canvas, view, -1);
        var failure = GoldenImage.Verify($"td-bestiary-{(known ? "known" : "locked")}-{dpi * 100 / 96}", image);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [InlineData(96)] [InlineData(144)]
    public void Construir_modal_economia(int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "golden Linux");
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 6, Rng.Seed(7, 54));
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host());
        ui.Activate(new(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Build));
        var category = ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.TowerCategory && i.SArg == "economy");
        ui.Activate(category);
        var view = ui.BuildPopover()!;
        Assert.Contains(view.Items, i => i.Text == "Todas as torres liberadas");
        using var typography = new Typography();
        using var renderer = new TdUiRenderer(typography, dpi);
        var (w, h) = renderer.PhysicalSize(view.Width, view.Height);
        using var image = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(image)) renderer.DrawPopover(canvas, view, -1);
        var failure = GoldenImage.Verify($"td-construir-economia-{dpi * 100 / 96}", image);
        Assert.True(failure is null, failure);
    }
}
