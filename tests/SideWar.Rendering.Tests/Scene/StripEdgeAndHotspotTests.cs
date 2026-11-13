using SideWar.Core.Simulation;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Power;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Scene;

public sealed class StripEdgeAndHotspotTests : IDisposable
{
    private static readonly AnimationPolicy Normal = new(false, PowerProfile.Normal);
    private static readonly AnimationPolicy Reduced = new(true, PowerProfile.Normal);
    private readonly SpriteAtlas _atlas = SpriteAtlas.LoadEmbedded();
    private readonly Typography _typo = new();
    private readonly IconCatalog _icons = new();

    public void Dispose()
    {
        _icons.Dispose();
        _typo.Dispose();
        _atlas.Dispose();
    }

    private static SimState NewState()
    {
        var slots = new float[8];
        for (int i = 0; i < 8; i++) slots[i] = 640 + 48 * i + 22;
        var s = new SimState(TestLayouts.FromCenters(1290, 94, 10, 1256, slots)) { BaseHp = 50, MaxBaseHp = 100 };
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Bolt, Anchor = TowerAnchor.Start, OffsetDip = TestLayouts.SceneX(0), Placed = true, Level = 14 });
        return s;
    }

    private static SKBitmap Bitmap(int w, int h) => new(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));

    private (SceneRenderer Scene, HotspotRenderer Hot) Renderers(int dpi)
    {
        var scene = new SceneRenderer(_atlas, _typo);
        scene.SetDpi(dpi);
        var hot = new HotspotRenderer(scene.SpriteCache, _typo, _icons);
        hot.SetDpi(dpi);
        return (scene, hot);
    }

    // ---------- linha de borda (RF-22.6) ----------

    [Fact]
    public void EdgeColors_PerMode()
    {
        Assert.Equal(Palette.BordaPresente, StripEdge.Color(StripEdgeMode.Present));
        Assert.Equal(Palette.BordaAusente, StripEdge.Color(StripEdgeMode.Away));
        Assert.Equal(Palette.BordaChefe, StripEdge.Color(StripEdgeMode.Boss));
    }

    [Theory]
    [InlineData(96, 94, 96)]
    [InlineData(120, 118, 120)]
    [InlineData(144, 141, 144)]
    [InlineData(192, 188, 192)]
    public void EdgeRows_AreTheLastTwoDipOfTheStrip(int dpi, int top, int bottom) =>
        Assert.Equal((top, bottom), StripEdge.Rows(dpi / 96f));

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public void Draw_Edge_IsUnderTheSprites_AndDefaultHasNoEdge(int dpi)
    {
        var (r, hot) = Renderers(dpi);
        using var _ = r; using var __ = hot;
        var state = NewState();
        int w = 1440 * dpi / 96, h = dpi;
        using var plain = Bitmap(w, h);
        using var edged = Bitmap(w, h);
        using (var c = new SKCanvas(plain)) r.Draw(c, state, 0, Normal);
        using (var c = new SKCanvas(edged)) r.Draw(c, state, 0, Normal, StripEdgeMode.Present);
        var (top, bottom) = StripEdge.Rows(dpi / 96f);
        Assert.Equal(0, plain.GetPixel(5, h - 1).Alpha);
        int sprite = 0, partial = 0, edge = 0;
        for (int y = top; y < bottom; y++)
            for (int x = 0; x < w; x++)
            {
                var p = plain.GetPixel(x, y);
                var e = edged.GetPixel(x, y);
                if (p.Alpha == 255) { Assert.Equal(p, e); sprite++; }          // sprite opaco por cima, intacto
                else if (p.Alpha != 0) { partial++; Assert.NotEqual(Palette.BordaPresente, e); } // translúcido: mistura sobre a linha
                else if (p.Alpha == 0) { Assert.Equal(Palette.BordaPresente, e); edge++; } // sem sprite: a linha
            }
        Assert.True(sprite + partial > 0, $"há sprite sobre a linha (torre/slot/núcleo); opacos={sprite} parciais={partial}");
        Assert.True(edge > w, "a linha aparece onde não há sprite");
        Assert.Equal(0, edged.GetPixel(5, top - 1).Alpha); // só 2 DIP
    }

    [Fact]
    public void Draw_Edge_AwayAndBossColors()
    {
        var (r, hot) = Renderers(96);
        using var _ = r; using var __ = hot;
        using var bmp = Bitmap(1440, 96);
        using var c = new SKCanvas(bmp);
        r.Draw(c, NewState(), 0, Normal, StripEdgeMode.Away);
        Assert.Equal(Palette.BordaAusente, bmp.GetPixel(5, 95));
        r.Draw(c, NewState(), 0, Normal, StripEdgeMode.Boss);
        Assert.Equal(Palette.BordaChefe, bmp.GetPixel(5, 94));
    }

    // ---------- textos ----------

    [Theory]
    [InlineData(24, "0:24")]
    [InlineData(45, "0:45")]
    [InlineData(60, "1:00")]
    [InlineData(-3, "0:00")]
    public void FormatChip(int seconds, string expected) => Assert.Equal(expected, HotspotRenderer.FormatChip(seconds));

    [Theory]
    [InlineData(108, "ausente há 1h 48min")]
    [InlineData(5, "ausente há 5min")]
    [InlineData(60, "ausente há 1h 0min")]
    [InlineData(0, "ausente há 0min")]
    public void FormatAway(int minutes, string expected) =>
        Assert.Equal(expected, HotspotRenderer.FormatAway(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void SilkscreenHasGlyphsForTheBannerAndSealTexts()
    {
        var face = _typo.GetTypeface(FontRole.SilkscreenRegular);
        foreach (var t in new[] { HotspotRenderer.PeakText, HotspotRenderer.BossText, HotspotRenderer.SealText, "ausente há 1h 48min", "0:24" })
            Assert.Empty(GlyphCoverage.Missing(face, t));
    }

    // ---------- banner (RF-22.8) ----------

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public void Banner_Peak_DrawsAmberControlAndPulseRing(int dpi)
    {
        var (r, hot) = Renderers(dpi);
        using var _ = r; using var __ = hot;
        int cw = hot.MeasureBannerWidth(BannerVariant.Peak, "0:24");
        Assert.True(cw > 200);
        var place = HotspotGeometry.Banner(new PixelRect(0, 0, 1440 * dpi / 96, dpi), (uint)dpi, cw);
        using var bmp = Bitmap(place.Window.Width, place.Window.Height);
        using var canvas = new SKCanvas(bmp);
        int m = place.Control.Left, midY = place.Window.Height / 2;

        hot.DrawBanner(canvas, BannerVariant.Peak, "0:24", 0.0, Normal); // fase 0: anel de 0 DIP
        Assert.Equal(Palette.Ambar, bmp.GetPixel(m + 1, m + 1));
        Assert.Equal(0, bmp.GetPixel(m - 1, midY).Alpha);
        Assert.Equal(0, bmp.GetPixel(0, 0).Alpha);

        hot.DrawBanner(canvas, BannerVariant.Peak, "0:24", 0.3, Normal); // fase 0,25: spread 3 DIP, alfa ~0,35
        Assert.InRange(bmp.GetPixel(m - 1, midY).Alpha, 60, 120);
        Assert.Equal(0, bmp.GetPixel(m - (int)(5 * dpi / 96f), midY).Alpha);
        Assert.Equal(Palette.Ambar, bmp.GetPixel(m + 1, m + 1)); // anel não cobre o controle

        // texto escuro (#071323) e chip existem dentro do controle
        Assert.True(Count(bmp, Palette.BgTrilhoFundo) > 50);
    }

    [Fact]
    public void Banner_Reduced_StaticRingTwoDip_AndBossColors()
    {
        var (r, hot) = Renderers(96);
        using var _ = r; using var __ = hot;
        int cw = hot.MeasureBannerWidth(BannerVariant.Boss, "0:45");
        using var bmp = Bitmap(cw + 12, 48);
        using var canvas = new SKCanvas(bmp);
        hot.DrawBanner(canvas, BannerVariant.Boss, "0:45", 0.3, Reduced);
        Assert.InRange(bmp.GetPixel(5, 24).Alpha, 177, 179);      // 2 DIP fixo, rgba(245,179,53,.7)
        Assert.InRange(bmp.GetPixel(4, 24).Alpha, 177, 179);
        Assert.Equal(0, bmp.GetPixel(3, 24).Alpha);
        Assert.Equal(Palette.Perigo, bmp.GetPixel(7, 7));         // fundo do chefe
        Assert.True(Count(bmp, Palette.PerigoTinta) > 50);
        Assert.True(Count(bmp, Palette.PerigoClaro) > 5);         // texto do chip
    }

    [Fact]
    public void Banner_ChipChangesWidthOnlyByItsText()
    {
        var (r, hot) = Renderers(96);
        using var _ = r; using var __ = hot;
        int boss = hot.MeasureBannerWidth(BannerVariant.Boss, "0:45");
        int peak = hot.MeasureBannerWidth(BannerVariant.Peak, "0:24");
        Assert.True(boss > 0 && peak > 0);
        Assert.Equal(peak, hot.MeasureBannerWidth(BannerVariant.Peak, "1:59")); // dígitos de largura igual na Silkscreen
    }

    // ---------- caixa (RF-22.8) ----------

    [Fact]
    public void SupplyBox_BlinksBetween1And045_AndReducedMotionDoesNot()
    {
        var (r, hot) = Renderers(96);
        using var _ = r; using var __ = hot;
        using var bmp = Bitmap(52, 44);
        using var canvas = new SKCanvas(bmp);
        hot.DrawSupplyBox(canvas, 0.0, Normal);
        int full = MaxAlpha(bmp);
        Assert.InRange(full, 250, 255);
        Assert.Equal(0, bmp.GetPixel(0, 0).Alpha);                // fora do sprite 45 × 24 (x 3..48, y 20..44)
        hot.DrawSupplyBox(canvas, 0.55, Normal);                  // estado 2: 0,45
        Assert.InRange(MaxAlpha(bmp), (int)(full * 0.45) - 2, (int)(full * 0.45) + 2);
        hot.DrawSupplyBox(canvas, 0.55, Reduced);
        Assert.Equal(full, MaxAlpha(bmp));
        Assert.Equal(0, bmp.GetPixel(1, 10).Alpha);               // sprite só nas linhas 20..44
        Assert.True(HasAlpha(bmp, 3, 20, 45, 24));
    }

    // ---------- selo (RF-22.7) ----------

    [Fact]
    public void Seal_DrawsBorderBackgroundMarkAndStaysOpaque()
    {
        var (r, hot) = Renderers(96);
        using var _ = r; using var __ = hot;
        var size = hot.MeasureSeal("ausente há 1h 48min");
        Assert.InRange(size.Height, 30, 40); // 2 + 14 + linha
        Assert.True(size.Width > 400);
        using var bmp = Bitmap(size.Width, size.Height);
        using var canvas = new SKCanvas(bmp);
        hot.DrawSeal(canvas, "ausente há 1h 48min");
        Assert.Equal(Palette.BordaForte, bmp.GetPixel(0, 0));
        Assert.Equal(Palette.BordaForte, bmp.GetPixel(size.Width - 1, size.Height - 1));
        Assert.Equal(Palette.BgAusente, bmp.GetPixel(3, 3));
        Assert.Equal(Palette.TxtRotulo, bmp.GetPixel(13 + 3, size.Height / 2)); // quadrado 8 × 8 em x = 1 + 12
        Assert.True(Count(bmp, Palette.TxtSecundario) > 50);
        for (int x = 0; x < size.Width; x += 7)
            for (int y = 0; y < size.Height; y += 3) Assert.Equal(255, bmp.GetPixel(x, y).Alpha);
    }

    [Fact]
    public void Draw_BeforeSetDpi_Throws()
    {
        using var scene = new SceneRenderer(_atlas, _typo);
        scene.SetDpi(96);
        using var hot = new HotspotRenderer(scene.SpriteCache, _typo, _icons);
        using var bmp = Bitmap(10, 10);
        using var canvas = new SKCanvas(bmp);
        Assert.Throws<InvalidOperationException>(() => hot.DrawSupplyBox(canvas, 0, Normal));
    }

    private static int Count(SKBitmap b, SKColor c)
    {
        int n = 0;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y) == c) n++;
        return n;
    }

    private static int MaxAlpha(SKBitmap b)
    {
        int m = 0;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++) m = Math.Max(m, b.GetPixel(x, y).Alpha);
        return m;
    }

    private static bool HasAlpha(SKBitmap b, int x0, int y0, int w, int h)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
                if (b.GetPixel(x, y).Alpha != 0) return true;
        return false;
    }
}
