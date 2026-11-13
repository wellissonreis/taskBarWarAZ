using SideWar.Core.Simulation;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Runtime.Input;
using SkiaSharp;
using Xunit;
using BlockReason = SideWar.Runtime.Taskbar.BlockReason;
using BlockedSpan = SideWar.Runtime.Taskbar.BlockedSpan;

namespace SideWar.Rendering.Tests.Scene;

/// <summary>SDD-22 RF-22.11 / T-22.13: overlay do modo construção (lógica pura + pixels de borda, independentes de fonte).</summary>
public sealed class BuildOverlayRendererTests : IDisposable
{
    private readonly SpriteAtlas _atlas = SpriteAtlas.LoadEmbedded();
    private readonly Typography _typo = new();
    private readonly BuildOverlayRenderer _r;

    public BuildOverlayRendererTests() { _r = new BuildOverlayRenderer(_atlas, _typo); _r.SetDpi(96); }

    public void Dispose() { _r.Dispose(); _typo.Dispose(); _atlas.Dispose(); }

    private static SimState State(TowerHost host, int width, FreeZone[] zones, params int[] towerX)
    {
        var layout = StripLayout.Create(width, width - 40, 88, 4, width - 80, host, zones);
        var s = new SimState(layout);
        foreach (int x in towerX)
            s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Bolt, Anchor = TowerAnchor.Start, OffsetDip = x, Placed = true, Level = 1 });
        return s;
    }

    [Fact]
    public void FreeRunsSubtractTowerBoxesAndDropShortRuns()
    {
        // zona [100, 500): torre em 300 ocupa [278, 322) → sobram [100,278) e [322,500)
        var s = State(TowerHost.Taskbar, 1000, [new FreeZone(100, 500)], 300);
        Assert.Equal([(100, 278), (322, 500)], _r.FreeRuns(s));
        // torre em 120 (ocupa [98,142)) deixa [142,500); aparte < 44 some
        s = State(TowerHost.Taskbar, 1000, [new FreeZone(100, 500)], 120, 470);
        Assert.Equal([(142, 448)], _r.FreeRuns(s));
        s = State(TowerHost.Taskbar, 1000, [new FreeZone(100, 500)], 150, 200); // [128,172) e [178,222): vão 6 e sobras
        Assert.Equal([(222, 500)], _r.FreeRuns(s));
    }

    [Fact]
    public void FreeRunsOfSeveralZonesAndUnplacedAreIgnored()
    {
        var s = State(TowerHost.Taskbar, 1000, [new FreeZone(0, 100), new FreeZone(200, 243), new FreeZone(600, 800)], 700);
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Bolt, Placed = false, Level = 1, OffsetDip = 650 });
        Assert.Equal([(0, 100), (600, 678), (722, 800)], _r.FreeRuns(s)); // [200,243) tem 43 DIP: some
    }

    [Theory]
    [InlineData(1500, 1920, 1420)]   // célula.Left − 80 = 1478 − 80 = 1398 → mas caixa 1920 cabe
    [InlineData(10, 1920, 0)]        // limitada à esquerda
    [InlineData(1910, 1920, 1920 - 300)] // limitada à direita
    public void HintLeftClampsToScreen(int ghostX, float screen, float expectedApprox)
    {
        float left = BuildOverlayRenderer.HintLeft(ghostX, 300, screen);
        if (ghostX == 1500) Assert.Equal(1500 - 22 - 80, left);
        else Assert.Equal(expectedApprox, left);
    }

    [Fact]
    public void BlinkIsStepsOfTwoAndStopsWithReducedMotion()
    {
        Assert.Equal(255, BuildOverlayRenderer.Blink(0, false));
        Assert.Equal(255, BuildOverlayRenderer.Blink(0.54, false));
        Assert.Equal(115, BuildOverlayRenderer.Blink(0.56, false)); // .45 × 255
        Assert.Equal(255, BuildOverlayRenderer.Blink(1.1, false));
        Assert.Equal(255, BuildOverlayRenderer.Blink(0.8, true));
    }

    [Fact]
    public void CellTopFollowsHost()
    {
        Assert.Equal(106f, BuildOverlayRenderer.CellTop(TowerHost.Taskbar, 48));
        Assert.Equal(2f, BuildOverlayRenderer.CellTop(TowerHost.Band, 48));
    }

    private SKBitmap Render(SimState s, BuildGhost? ghost, bool reduced = true, IReadOnlyList<BlockedSpan>? blocked = null)
    {
        var bmp = new SKBitmap(new SKImageInfo(1920, 152, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        _r.Draw(canvas, new BuildOverlayFrame(s, 48, blocked ?? [new BlockedSpan(686, 1332, BlockReason.StartSearchApps)],
            ghost, TowerType.Bolt, "RAIO · 1,2K", 0, reduced));
        return bmp;
    }

    private static readonly SKColor Orange = new(0xff, 0x7a, 0x1a);
    private static readonly SKColor Green = new(0x5c, 0xf0, 0xb0);

    [Fact]
    public void BlockedBoxesHaveOrangeBordersAboveBarAndOnTheBar()
    {
        var s = State(TowerHost.Taskbar, 1920, [new FreeZone(1400, 1600)]);
        using var bmp = Render(s, null);
        Assert.Equal(Orange, bmp.GetPixel(0, 50));      // acima da barra [0,1920)×[0,102): borda esquerda
        Assert.Equal(Orange, bmp.GetPixel(1919, 50));
        Assert.Equal(Orange, bmp.GetPixel(900, 101));   // borda inferior da caixa
        Assert.Equal(Orange, bmp.GetPixel(686, 130));   // trecho bloqueado da barra
        Assert.Equal(Orange, bmp.GetPixel(1331, 130));
        Assert.Equal(Orange, bmp.GetPixel(900, 104));
        Assert.Equal(0, bmp.GetPixel(100, 130).Alpha); // sem span: nada
    }

    [Fact]
    public void StripesAlternateStrongAndWeakAlongTheDiagonal()
    {
        var s = State(TowerHost.Taskbar, 1920, [new FreeZone(1400, 1600)]);
        using var bmp = Render(s, null);
        var alphas = new HashSet<byte>();
        for (int x = 800; x < 840; x++) alphas.Add(bmp.GetPixel(x, 60).Alpha);
        Assert.Contains((byte)97, alphas);
        Assert.Contains((byte)20, alphas);
    }

    [Fact]
    public void FreeRunsAreGreen()
    {
        var s = State(TowerHost.Taskbar, 1920, [new FreeZone(1400, 1600)]);
        using var bmp = Render(s, null);
        var p = bmp.GetPixel(1500, 128);
        Assert.Equal(31, p.Alpha); // rgba(92,240,176,.12)
        Assert.True(p.Green > p.Red && p.Green > 200);
        Assert.Equal(Green, bmp.GetPixel(1403, 106)); // 1º traço do tracejado
        Assert.Equal(0, bmp.GetPixel(1500, 105).Alpha);
        Assert.Equal(0, bmp.GetPixel(1500, 150).Alpha);
    }

    [Fact]
    public void GhostValidIsGreenInvalidIsOrangeAndSitsAtTop106()
    {
        var s = State(TowerHost.Taskbar, 1920, [new FreeZone(1400, 1600)]);
        using (var ok = Render(s, new BuildGhost(1500, true, PlacementReason.None)))
        {
            Assert.Equal(Green, ok.GetPixel(1478, 106));   // canto sup. esq. [x−22, 106)
            Assert.Equal(Green, ok.GetPixel(1521, 149));   // canto inf. dir.
            Assert.Equal(Green, ok.GetPixel(1478, 130));
        }
        using (var bad = Render(s, new BuildGhost(1000, false, PlacementReason.OutOfZone)))
        {
            Assert.Equal(Orange, bad.GetPixel(978, 106));
            Assert.Equal(Orange, bad.GetPixel(1021, 149));
        }
    }

    [Fact]
    public void BannerIsCenteredAtTopAndHasGreenBorder()
    {
        var s = State(TowerHost.Taskbar, 1920, [new FreeZone(1400, 1600)]);
        using var bmp = Render(s, null);
        Assert.Equal(Green, bmp.GetPixel(960, 0));
        Assert.Equal(Green, bmp.GetPixel(960, 35));
        Assert.NotEqual(Green, bmp.GetPixel(960, 36));
    }

    [Fact]
    public void BandHostGhostSitsAtTop2()
    {
        var s = State(TowerHost.Band, 1920, [new FreeZone(300, 700)]);
        using var bmp = Render(s, new BuildGhost(500, true, PlacementReason.None), blocked: []);
        Assert.Equal(Green, bmp.GetPixel(478, 2));
        Assert.Equal(Green, bmp.GetPixel(521, 45));
    }

    [Fact]
    public void DrawRequiresSetDpi()
    {
        using var r = new BuildOverlayRenderer(_atlas, _typo);
        using var bmp = new SKBitmap(10, 10);
        using var canvas = new SKCanvas(bmp);
        var s = State(TowerHost.Taskbar, 1000, [new FreeZone(0, 100)]);
        Assert.Throws<InvalidOperationException>(() => r.Draw(canvas, new BuildOverlayFrame(s, 48, [], null, TowerType.Bolt, "", 0)));
    }

    [Fact]
    public void ScalesTo150Percent()
    {
        _r.SetDpi(144);
        var s = State(TowerHost.Taskbar, 1920, [new FreeZone(1400, 1600)]);
        using var bmp = new SKBitmap(new SKImageInfo(2880, 228, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        _r.Draw(canvas, new BuildOverlayFrame(s, 48, [], new BuildGhost(1500, true, PlacementReason.None), TowerType.Bolt, "RAIO", 0, true));
        Assert.Equal(Green, bmp.GetPixel(2217, 159)); // (1500−22)×1.5 = 2217, 106×1.5 = 159
        Assert.Equal(Orange, bmp.GetPixel(0, 75));
    }
}
