using SideWar.Rendering.Icons;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Core.Simulation;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Panel;

public sealed class OfflineCardRenderTests : IDisposable
{
    private readonly Typography typo = new();
    private readonly IconCatalog icons = new();

    public void Dispose()
    {
        icons.Dispose();
        typo.Dispose();
    }

    private static OfflineCardModel Model(bool warning = true)
    {
        var r = new OfflineReport(3 * 3600 + 12 * 60, 186_000, 22, 37, 0, 4100, 22, 23,
            [new(TowerType.Bolt, 12, 14), new(TowerType.Frost, 8, 9)], 6, 0, warning ? 4 : 0, warning ? 3 : 0, warning ? 1 : 0, false);
        return OfflineCardModel.From(new PendingRewards(1, r, 186_000, 4100), true);
    }

    private SKBitmap Render(int dpi, OfflineCardModel m, out OfflineCardLayout l)
    {
        l = OfflineCardLayout.Compute(m, new PanelTextMetricsSkia(typo));
        using var r = new OfflineCardRenderer(typo, icons, dpi);
        var (w, h) = r.WindowSize;
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        r.Draw(canvas, m, l);
        return bmp;
    }

    private static SKColor At(SKBitmap b, float x, float y, int dpi) => b.GetPixel((int)Math.Round(x * dpi / 96f), (int)Math.Round(y * dpi / 96f));

    [Theory(DisplayName = "CA-24.11 cores do card: borda âmbar, fundo, barra, tiles, aviso e botões (96 e 144 dpi)")]
    [InlineData(96)]
    [InlineData(144)]
    public void Colors(int dpi)
    {
        using var bmp = Render(dpi, Model(), out var l);
        Assert.Equal(new SKColor(0xf5, 0xb3, 0x35), At(bmp, 0.5f, 100, dpi));                // borda 2 DIP âmbar
        Assert.Equal(new SKColor(0x0b, 0x1a, 0x2e), At(bmp, 12, 300, dpi));                   // fundo bg-painel
        Assert.Equal(Palette.Azul, At(bmp, l.BarFill.X + 3, l.BarFill.Y + 4, dpi));            // preenchimento
        Assert.Equal(Palette.BgTrilho, At(bmp, l.BarTrack.X + l.BarTrack.W - 3, l.BarTrack.Y + 4, dpi));
        Assert.Equal(Palette.BordaPainel, At(bmp, l.Tiles[0].Box.X + 0.5f, l.Tiles[0].Box.Y + 40, dpi));
        Assert.Equal(Palette.BgItem, At(bmp, l.Tiles[0].Box.X + 5, l.Tiles[0].Box.Y + 5, dpi));
        Assert.Equal(Palette.PerigoBorda, At(bmp, l.WarningBox!.Value.X + 0.5f, l.WarningBox.Value.Y + 20, dpi));
        Assert.Equal(Palette.PerigoFundo, At(bmp, l.WarningBox.Value.X + 5, l.WarningBox.Value.Y + 5, dpi));
        Assert.Equal(Palette.Ambar, At(bmp, l.CollectX2.X + 4, l.CollectX2.Y + 4, dpi));
        Assert.Equal(Palette.BordaForte, At(bmp, l.Collect.X + 0.5f, l.Collect.Y + 20, dpi));
        Assert.Equal(Palette.BgPainel, At(bmp, l.Collect.X + 4, l.Collect.Y + 4, dpi));
        // sombra dura fora da caixa (+6,+6)
        Assert.Equal(Palette.SombraCard.Alpha, At(bmp, 472 + 3, 300, dpi).Alpha);
        Assert.Equal(0, At(bmp, 472 + 3, 2, dpi).Alpha);
    }

    [Fact(DisplayName = "Sem perdas: a região do aviso fica no fundo do card")]
    public void NoWarningDrawn()
    {
        var m = Model(warning: false);
        using var bmp = Render(96, m, out var l);
        Assert.Null(l.WarningBox);
        Assert.Equal(Palette.BgPainel, At(bmp, 30, l.ContentBottom + 6, 96));
    }

    [Fact(DisplayName = "Tamanho da janela: caixa + 6 DIP de sombra, arredondado uma vez")]
    public void WindowSize()
    {
        using var r96 = new OfflineCardRenderer(typo, icons, 96);
        using var r144 = new OfflineCardRenderer(typo, icons, 144);
        Assert.Equal((478, 618), r96.WindowSize);
        Assert.Equal((717, 927), r144.WindowSize);   // 708+9 × 918+9
    }

    [Fact(DisplayName = "Texto desenhado: tempo realçado, título âmbar e rótulos presentes")]
    public void TextInk()
    {
        using var bmp = Render(96, Model(), out var l);
        static bool HasInk(SKBitmap b, DipRectLike r, SKColor c)
        {
            for (var y = (int)r.Y; y < (int)(r.Y + r.H); y++)
                for (var x = (int)r.X; x < (int)(r.X + r.W); x++)
                    if (b.GetPixel(x, y) == c) return true;
            return false;
        }
        Assert.True(HasInk(bmp, new(l.TitleBox.X, l.TitleBox.Y, l.TitleBox.W, l.TitleBox.H), Palette.Ambar));
        Assert.True(HasInk(bmp, new(l.IntroBox.X, l.IntroBox.Y, l.IntroBox.W, l.IntroBox.H), Palette.TxtPrincipal));
        Assert.True(HasInk(bmp, new(l.Tiles[2].SubBox.X, l.Tiles[2].SubBox.Y, l.Tiles[2].SubBox.W, l.Tiles[2].SubBox.H), Palette.Menta));
        Assert.True(HasInk(bmp, new(l.CollectX2LabelBox.X, l.CollectX2LabelBox.Y, l.CollectX2LabelBox.W, l.CollectX2LabelBox.H), Palette.BgTrilhoFundo));
    }

    private readonly record struct DipRectLike(float X, float Y, float W, float H);

    [Fact(DisplayName = "Render é determinístico (mesmo frame, bytes iguais)")]
    public void Deterministic()
    {
        using var a = Render(96, Model(), out _);
        using var b = Render(96, Model(), out _);
        Assert.True(a.Bytes.AsSpan().SequenceEqual(b.Bytes));
    }

    [Fact(DisplayName = "Evidência visual: PNG do card (opcional, via SIDEWAR_PNG_DIR)")]
    public void WritesPngWhenAsked()
    {
        var dir = Environment.GetEnvironmentVariable("SIDEWAR_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        foreach (var dpi in new[] { 96, 144 })
        {
            using var bmp = Render(dpi, Model(), out _);
            using var img = SKImage.FromBitmap(bmp);
            using var d = img.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, $"offline-card-{dpi}.png"), d.ToArray());
        }
    }
}
