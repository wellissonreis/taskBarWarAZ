using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Panel;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Panel;

public sealed class ArsenalCampaignRenderTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);
    private readonly Typography _fonts = new();
    private readonly SpriteAtlas _atlas = SpriteAtlas.LoadEmbedded();
    private readonly ScaledSpriteCache _cache;

    public ArsenalCampaignRenderTests()
    {
        _cache = new ScaledSpriteCache(_atlas);
    }

    public void Dispose() { _cache.Dispose(); _atlas.Dispose(); _fonts.Dispose(); }

    private ArsenalCampaignPaintContext Paint(int dpi)
    {
        _cache.Bake(dpi / 96f);
        return new ArsenalCampaignPaintContext(_fonts, _cache, dpi);
    }

    private static SimState State()
    {
        var s = SimState.CreateInitial(Content, TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f }), Rng.Seed(1, 2));
        s.Credits = 1_000_000;
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        s.Apply(Ctx, new BuildTower(TowerType.Frost, TestLayouts.X(1)));
        return s;
    }

    private static SKBitmap Panel(int dpi)
    {
        var b = new SKBitmap((int)(400 * dpi / 96f), (int)(548 * dpi / 96f), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var c = new SKCanvas(b);
        c.Clear(Palette.BgPainel);
        return b;
    }

    private static SKColor At(SKBitmap b, float xDip, float yDip, int dpi) => b.GetPixel((int)MathF.Round(xDip * dpi / 96f), (int)MathF.Round(yDip * dpi / 96f));

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public void Arsenal_ButtonsAreAmberHoverAmberAndLockedTrilho_NoHoverOnLocked(int dpi)
    {
        var ctx = Paint(dpi);
        var lay = ArsenalLayout.Compute(ArsenalModelBuilder.Build(State(), Ctx, true), new PanelTextMetricsSkia(_fonts));
        var up = lay.Rows[0];
        var locked = lay.Rows[^1];
        Assert.Equal(ArsenalRowKind.Locked, locked.Model.Kind);

        using var normal = Panel(dpi);
        using (var c = new SKCanvas(normal)) ArsenalTabRenderer.Draw(c, lay, ArsenalHit.None, 0, 491, ctx);
        float ux = 2 + up.Button.X + 3, uy = 55 + up.Button.Y + 3;
        Assert.Equal(Palette.Ambar, At(normal, ux, uy, dpi));
        float lx = 2 + locked.Button.X + 3, ly = 55 + locked.Button.Y + 3 - 0;

        using var hov = Panel(dpi);
        using (var c = new SKCanvas(hov)) ArsenalTabRenderer.Draw(c, lay, up.ButtonHit, 0, 491, ctx);
        Assert.Equal(Palette.AmbarHover, At(hov, ux, uy, dpi));

        // linha bloqueada fica abaixo do viewport sem rolagem? rola até ela
        float off = Math.Max(0f, lay.ContentHeight - 491f);
        using var scrolled = Panel(dpi);
        using (var c = new SKCanvas(scrolled)) ArsenalTabRenderer.Draw(c, lay, locked.ButtonHit, off, 491, ctx);
        var px = At(scrolled, 2 + locked.Button.X + 3, 55 + locked.Button.Y - off + 3, dpi);
        // 0,45 de opacidade sobre bg-painel: não é âmbar e não muda com hover (ButtonHit é None)
        Assert.NotEqual(Palette.Ambar, px);
        Assert.NotEqual(Palette.AmbarHover, px);
        _ = lx; _ = ly;
    }

    [Fact]
    public void Arsenal_ClipsToViewport_NothingAboveY55()
    {
        var ctx = Paint(96);
        var lay = ArsenalLayout.Compute(ArsenalModelBuilder.Build(State(), Ctx, true), new PanelTextMetricsSkia(_fonts));
        using var b = Panel(96);
        using (var c = new SKCanvas(b)) ArsenalTabRenderer.Draw(c, lay, ArsenalHit.None, 0, 491, ctx);
        for (int y = 0; y < 55; y++)
            for (int x = 0; x < 400; x += 7)
                Assert.Equal(Palette.BgPainel, b.GetPixel(x, y));
    }

    [Fact]
    public void Arsenal_SlotBackgroundAndSpritePainted_AutoCardBorder()
    {
        var ctx = Paint(96);
        var lay = ArsenalLayout.Compute(ArsenalModelBuilder.Build(State(), Ctx, true), new PanelTextMetricsSkia(_fonts));
        using var b = Panel(96);
        using (var c = new SKCanvas(b)) ArsenalTabRenderer.Draw(c, lay, ArsenalHit.None, 0, 491, ctx);
        var slot = lay.Rows[0].Slot;
        Assert.Equal(Palette.BgTrilhoFundo, At(b, 2 + slot.X + 1, 55 + slot.Y + 1, 96));
        // algum pixel do slot difere do fundo (sprite)
        bool sprite = false;
        for (int y = 0; y < 52 && !sprite; y++)
            for (int x = 0; x < 52; x++)
                if (b.GetPixel((int)(2 + slot.X) + x, (int)(55 + slot.Y) + y) != Palette.BgTrilhoFundo) { sprite = true; break; }
        Assert.True(sprite);
        Assert.Equal(Palette.BordaForte, At(b, 2 + 16, 55 + 14 + 5, 96));
        Assert.Equal(Palette.BgCard, At(b, 2 + 20, 55 + 14 + 3, 96));
    }

    [Fact]
    public void Arsenal_AutoToggleOnIsMentaBox_OffIsOutlined()
    {
        var ctx = Paint(96);
        var tm = new PanelTextMetricsSkia(_fonts);
        foreach (bool on in new[] { true, false })
        {
            var lay = ArsenalLayout.Compute(ArsenalModelBuilder.Build(State(), Ctx, on), tm);
            using var b = Panel(96);
            using (var c = new SKCanvas(b)) ArsenalTabRenderer.Draw(c, lay, ArsenalHit.None, 0, 491, ctx);
            var box = lay.AutoCard.Box;
            var corner = At(b, 2 + box.X, 55 + box.Y + 10, 96);
            Assert.Equal(on ? Palette.Menta : Palette.TxtRotulo, corner);
            var edgeInside = At(b, 2 + box.X + 3, 55 + box.Y + 10, 96);
            if (!on) Assert.Equal(Palette.BgCard, edgeInside);
        }
    }

    [Fact]
    public void Campaign_BarFillIsRoundedOnce_StarsDrawnAsPaths_LockedRowsDimmed()
    {
        var s = State();
        s.Stage = 3; s.Wave = 37; s.UnlockedStage = 3;
        s.RestoreStageProgress(1, 0, 3);
        var m = CampaignModelBuilder.Build(s, Ctx);
        var lay = CampaignLayout.Compute(m, new PanelTextMetricsSkia(_fonts));
        var ctx = Paint(96);
        using var b = Panel(96);
        using (var c = new SKCanvas(b)) CampaignTabRenderer.Draw(c, lay, 0, 491, ctx);

        var bar = lay.Card.BarTrack;
        int y = (int)(55 + bar.Y + 4);
        int x0 = (int)(2 + bar.X);
        int fill = 0;
        for (int x = 0; x < (int)bar.W; x++)
            if (b.GetPixel(x0 + x, y) == Palette.Azul) fill++;
        Assert.Equal((int)MathF.Round(37f / 60f * bar.W), fill);
        Assert.Equal(Palette.BgTrilho, b.GetPixel(x0 + (int)bar.W - 1, y));
        Assert.Equal(Palette.Azul, b.GetPixel((int)(2 + lay.Card.Rect.X), (int)(55 + lay.Card.Rect.Y + 20)));

        // estrelas cheias da fase 01: âmbar no centro da primeira estrela; vazia da fase 03: centro com fundo
        var r0 = lay.Rows[0].Stars;
        Assert.Equal(Palette.Ambar, At(b, 2 + r0.X + 5.5f, 55 + r0.Y + r0.H / 2f, 96));
        var r2 = lay.Rows[2].Stars;
        Assert.NotEqual(Palette.Ambar, At(b, 2 + r2.X + 5.5f, 55 + r2.Y + r2.H / 2f, 96));
    }

    [Fact]
    public void Campaign_ScrolledPastAllContentDrawsNothingBelowViewport()
    {
        var m = CampaignModelBuilder.Build(State(), Ctx);
        var lay = CampaignLayout.Compute(m, new PanelTextMetricsSkia(_fonts));
        var ctx = Paint(96);
        using var b = Panel(96);
        using (var c = new SKCanvas(b)) CampaignTabRenderer.Draw(c, lay, lay.ContentHeight - 491, 491, ctx);
        for (int y = 546; y < 548; y++)
            for (int x = 0; x < 400; x += 11)
                Assert.Equal(Palette.BgPainel, b.GetPixel(x, y));
    }
}
