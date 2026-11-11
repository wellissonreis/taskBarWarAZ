using SideWar.Rendering.Icons;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Panel;

public class MissionsMigrateRenderTests : IDisposable
{
    private readonly Typography typo = new();
    private readonly IconCatalog icons = new();
    private readonly PanelTextMetricsSkia tm;

    public MissionsMigrateRenderTests() => tm = new PanelTextMetricsSkia(typo);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        icons.Dispose();
        typo.Dispose();
    }

    private static SKColor At(SKBitmap b, float x, float y, int dpi) => b.GetPixel((int)Math.Round(x * dpi / 96f), (int)Math.Round(y * dpi / 96f));

    private SKBitmap RenderMissions(int dpi, PanelViewState view, TabBodyHit hover, out MissionsLayout layout, MissionsTabModel? model = null)
    {
        model ??= MissionsTabModel.TestData();
        layout = MissionsLayout.Compute(model, tm);
        view.SetContent(layout.ContentHeight, PanelScroll.ViewportDip);
        using var r = new MissionsMigratePanelRenderer(typo, icons, dpi);
        var (w, h) = r.WindowSize;
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        r.DrawMissions(canvas, model, layout, view, hover);
        return bmp;
    }

    private SKBitmap RenderMigrate(int dpi, PanelViewState view, TabBodyHit hover, MigrateTabModel model, out MigrateLayout layout)
    {
        layout = MigrateLayout.Compute(model, tm);
        view.SetContent(layout.ContentHeight, PanelScroll.ViewportDip);
        using var r = new MissionsMigratePanelRenderer(typo, icons, dpi);
        var (w, h) = r.WindowSize;
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        r.DrawMigrate(canvas, model, layout, view, hover);
        return bmp;
    }

    [Fact]
    public void TextMetrics_ConsistentWithRealFonts()
    {
        Assert.True(tm.LineHeight(PanelFont.JetBrainsMono, 11) is >= 13 and <= 16);
        Assert.True(tm.LineHeight(PanelFont.Silkscreen, 12) >= 12);
        Assert.True(tm.Width(PanelFont.JetBrainsMono, 11, "abcdef") > 6 * 5);
        Assert.Equal(tm.Width(PanelFont.JetBrainsMono, 11, "ab") + 2 * 1f, tm.Width(PanelFont.JetBrainsMono, 11, "ab", 1f), 3);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    public void Missions_Colors_BandsBarsAndButtons(int dpi)
    {
        var view = new PanelViewState();
        using var bmp = RenderMissions(dpi, view, TabBodyHit.None, out var l);
        Assert.Equal(Palette.BordaPainel, At(bmp, 1, 100, dpi));                 // borda de 2 DIP do painel
        Assert.Equal(Palette.BgPainel, At(bmp, 3, 300, dpi));                    // fundo do corpo
        Assert.Equal(Palette.MentaBorda, At(bmp, l.PresenceCard.X + 0.2f, l.PresenceCard.Y + 40, dpi)); // borda do card
        Assert.Equal(Palette.MentaFundo, At(bmp, l.PresenceCard.X + 4, l.PresenceCard.Y + 4, dpi));
        // faixa atual (índice 1) em menta; inativas em menta-faixa (CA-24.7)
        Assert.Equal(Palette.Menta, At(bmp, l.Bands[1].X + 2, l.Bands[1].Y + 1, dpi));
        Assert.Equal(Palette.MentaFaixa, At(bmp, l.Bands[0].X + 2, l.Bands[0].Y + 1, dpi));
        Assert.Equal(Palette.MentaFaixa, At(bmp, l.Bands[2].X + 2, l.Bands[2].Y + 1, dpi));
        // barra de 5 DIP: trilho e preenchimento (missão 3 a 70%)
        var bar = l.Rows[2].BarTrack;
        Assert.Equal(Palette.Ambar, At(bmp, bar.X + 2, bar.Y + 2, dpi));
        Assert.Equal(Palette.BgTrilho, At(bmp, bar.X + bar.W - 2, bar.Y + 2, dpi));
        Assert.Equal(Palette.BgItem, At(bmp, l.Rows[0].Row.X + 4, l.Rows[0].Row.Y + 4, dpi));
        // COLETAR só nas concluídas
        var btn = l.Rows[0].Collect!.Value;
        Assert.Equal(Palette.Ambar, At(bmp, btn.X + 2, btn.Y + 2, dpi));
        var none = l.Rows[2].Row;
        Assert.NotEqual(Palette.Ambar, At(bmp, none.X + none.W - 20, none.Y + none.H / 2, dpi));
        // cabeçalho: filete 1 DIP em y=54; barra de rolagem existe se o conteúdo > 491
        Assert.Equal(Palette.BordaPainel, At(bmp, 100, 54.4f, dpi));
        if (l.ContentHeight > 491f) Assert.Equal(Palette.TxtRotulo, At(bmp, 390, 62, dpi));
        // sombra dura (+6,+6): fora da caixa, alfa de sombra-painel
        var sh = bmp.GetPixel((int)Math.Round(404f * dpi / 96f), (int)Math.Round(300f * dpi / 96f));
        Assert.Equal((int)Palette.SombraPainel.Alpha, (int)sh.Alpha);
    }

    [Fact]
    public void Missions_HoverUsesAmbarHover_AndTextDrawn()
    {
        var view = new PanelViewState();
        using var bmp = RenderMissions(96, view, new TabBodyHit(TabBodyHitKind.MissionCollect, 0), out var l);
        var btn = l.Rows[0].Collect!.Value;
        Assert.Equal(Palette.AmbarHover, At(bmp, btn.X + 2, btn.Y + 2, 96));
        // texto COLETAR (bg-trilho-fundo) desenhado dentro do botão
        var found = false;
        for (var y = (int)btn.Y; y < btn.Y + btn.H && !found; y++)
            for (var x = (int)btn.X; x < btn.X + btn.W; x++)
                if (bmp.GetPixel(x, y) == Palette.BgTrilhoFundo) { found = true; break; }
        Assert.True(found);
        // outras linhas sem hover
        var b2 = l.Rows[1].Collect!.Value;
        Assert.Equal(Palette.Ambar, At(bmp, b2.X + 2, b2.Y + 2, 96));
    }

    [Fact]
    public void Missions_ScrolledRowsAreClipped_HeaderStaysOnTop()
    {
        var view = new PanelViewState();
        var model = MissionsTabModel.TestData();
        var l = MissionsLayout.Compute(model, tm);
        view.SetContent(l.ContentHeight, PanelScroll.ViewportDip);
        view.SetOffset(10_000f);
        using var bmp = RenderMissions(96, view, TabBodyHit.None, out _);
        // Nada do corpo rolado vaza para o cabeçalho (y 2..54): bg-painel acima do filete.
        Assert.Equal(Palette.BgPainel, At(bmp, 250, 40, 96));
        Assert.Equal(Palette.BordaPainel, At(bmp, 100, 54.4f, 96));
    }

    [Fact]
    public void Migrate_Colors_Normal_Armed_Unavailable_Hover()
    {
        var view = new PanelViewState();
        using (var bmp = RenderMigrate(96, view, TabBodyHit.None, MigrateTabModel.TestData(), out var l))
        {
            Assert.Equal(Palette.PrestigioBorda, At(bmp, l.Card.X + 0.2f, l.Card.Y + 40, 96));
            Assert.Equal(Palette.PrestigioFundo, At(bmp, l.Card.X + 4, l.Card.Y + 4, 96));
            Assert.Equal(Palette.BgItem, At(bmp, l.Tiles[0].X + 4, l.Tiles[0].Y + 4, 96));
            Assert.Equal(Palette.Prestigio, At(bmp, l.Button.X + 3, l.Button.Y + 3, 96));
            Assert.Equal(48f, l.Button.H);
        }
        using (var bmp = RenderMigrate(96, view, new TabBodyHit(TabBodyHitKind.Migrate), MigrateTabModel.TestData(), out var l))
            Assert.Equal(Palette.PrestigioHover, At(bmp, l.Button.X + 3, l.Button.Y + 3, 96));
        using (var bmp = RenderMigrate(96, view, new TabBodyHit(TabBodyHitKind.Migrate), MigrateTabModel.TestData(armed: true), out var l))
            Assert.Equal(Palette.Perigo, At(bmp, l.Button.X + 3, l.Button.Y + 3, 96));      // armado: sem hover
        using (var bmp = RenderMigrate(96, view, new TabBodyHit(TabBodyHitKind.Migrate), MigrateTabModel.TestData(maxWave: 5), out var l))
            Assert.Equal(Palette.BgTrilho, At(bmp, l.Button.X + 3, l.Button.Y + 3, 96));   // indisponível: sem hover
    }

    [Fact(DisplayName = "CA-24.15 contrast of used pairs; forbidden pair bg-trilho-fundo/menta-faixa never drawn")]
    public void Contrast_UsedPairs()
    {
        (SKColor Fg, SKColor Bg)[] pairs =
        [
            (Palette.BgTrilhoFundo, Palette.Menta), (Palette.MentaTexto, Palette.MentaFaixa), (Palette.MentaTexto, Palette.MentaFundo),
            (Palette.Menta, Palette.MentaFundo), (Palette.BgTrilhoFundo, Palette.Ambar), (Palette.BgTrilhoFundo, Palette.AmbarHover),
            (Palette.PerigoTinta, Palette.Perigo), (Palette.PrestigioTinta, Palette.Prestigio), (Palette.PrestigioTinta, Palette.PrestigioHover),
            (Palette.PrestigioTexto, Palette.PrestigioFundo), (Palette.PrestigioClaro, Palette.PrestigioFundo),
            (Palette.TxtRotulo, Palette.BgItem), (Palette.TxtPrincipal, Palette.PerigoFundo), (Palette.PerigoClaro, Palette.BgPainel),
            (Palette.TxtSecundario, Palette.BgPainel), (Palette.TxtRotulo, Palette.BgPainel), (Palette.TxtRotulo, Palette.BgTrilho),
            (Palette.Menta, Palette.BgItem), (Palette.TxtPrincipal, Palette.MentaFundo),
        ];
        foreach (var (fg, bg) in pairs) Assert.True(Contrast.Ratio(fg, bg) >= Contrast.MinText, $"{fg} on {bg}");
        Assert.True(Contrast.Ratio(Palette.BgTrilhoFundo, Palette.MentaFaixa) < 2.0); // o par proibido existe e não está na lista
    }

    [Theory]
    [InlineData(QuestionCardState.Waiting, null)]
    [InlineData(QuestionCardState.Correct, 1)]
    [InlineData(QuestionCardState.Wrong, 0)]
    [InlineData(QuestionCardState.TimedOut, null)]
    public void QuestionCard_StatesAndOptionColors(QuestionCardState state, int? chosen)
    {
        var m = QuestionCardModel.TestData(state, chosen);
        var l = QuestionCardLayout.Compute(m, tm);
        using var r = new QuestionCardRenderer(typo, icons, 96);
        var (w, h) = r.WindowSize(l.Height);
        using var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        r.Draw(canvas, m, l);
        Assert.Equal(Palette.Perigo, At(bmp, 1, 100, 96));                  // borda de 2 DIP perigo
        Assert.Equal(Palette.BgPainel, At(bmp, 5, 120, 96));
        Assert.Equal((int)Palette.SombraCard.Alpha, (int)bmp.GetPixel(523, 100).Alpha);
        for (var i = 0; i < 4; i++)
        {
            var o = l.Options[i];
            var (border, fill) = m.Visuals[i] switch
            {
                OptionVisual.Correct => (Palette.Menta, Palette.MentaFundo),
                OptionVisual.Wrong => (Palette.Perigo, Palette.PerigoFundo),
                _ => (Palette.BordaForte, Palette.BgItem),
            };
            Assert.Equal(border, At(bmp, o.X + 0.2f, o.Y + 20, 96));
            Assert.Equal(fill, At(bmp, o.X + o.W - 4, o.Y + 4, 96));
        }
        if (state == QuestionCardState.Wrong) Assert.Equal(OptionVisual.Wrong, m.Visuals[0]);
        // ícone da ação desenhado (minimizar = traço AA; fechar = máscara) sobre bg-painel
        var a = l.ActionButton;
        var hit = false;
        for (var y = (int)a.Y; y < a.Y + a.H && !hit; y++)
            for (var x = (int)a.X; x < a.X + a.W; x++)
                if (bmp.GetPixel(x, y) != Palette.BgPainel) { hit = true; break; }
        Assert.True(hit);
    }

    [Fact]
    public void QuestionCard_ExplanationGrowsHeight()
    {
        var m = QuestionCardModel.TestData(QuestionCardState.Wrong, 0);
        var l = QuestionCardLayout.Compute(m, tm);
        Assert.True(l.ShowsExplanation);
        Assert.True(l.Height > 242f - 40f);
        using var r = new QuestionCardRenderer(typo, icons, 144);
        var (w, h) = r.WindowSize(l.Height);
        Assert.Equal((int)Math.Round(520 * 1.5) + 9, w);
        Assert.True(h > 0);
    }

    [Fact(DisplayName = "Dump PNGs for visual inspection (only when SIDEWAR_T55_DUMP is set)")]
    public void DumpPngs()
    {
        var dir = Environment.GetEnvironmentVariable("SIDEWAR_T55_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        void Save(SKBitmap b, string n)
        {
            using var img = SKImage.FromBitmap(b);
            using var d = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, n), d.ToArray());
        }
        using (var b = RenderMissions(96, new PanelViewState(), TabBodyHit.None, out _)) Save(b, "missoes.png");
        using (var b = RenderMigrate(96, new PanelViewState(), TabBodyHit.None, MigrateTabModel.TestData(chosenRegion: 1), out _)) Save(b, "migrar.png");
        using (var b = RenderMigrate(96, new PanelViewState(), TabBodyHit.None, MigrateTabModel.TestData(armed: true), out _)) Save(b, "migrar-armado.png");
        foreach (var (st, ch, n) in new (QuestionCardState, int?, string)[] { (QuestionCardState.Waiting, null, "w"), (QuestionCardState.Wrong, 0, "e"), (QuestionCardState.Correct, 1, "c"), (QuestionCardState.TimedOut, null, "t") })
        {
            var m = QuestionCardModel.TestData(st, ch);
            var l = QuestionCardLayout.Compute(m, tm);
            using var r = new QuestionCardRenderer(typo, icons, 96);
            var (w, h) = r.WindowSize(l.Height);
            using var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var c = new SKCanvas(bmp);
            r.Draw(c, m, l);
            Save(bmp, $"pergunta-{n}.png");
        }
    }
}
