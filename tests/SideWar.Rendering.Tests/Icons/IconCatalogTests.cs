using SideWar.Rendering.Icons;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Icons;

public class IconCatalogTests
{
    private static readonly int[] Dpis = [96, 120, 144, 168, 192];

    [Fact]
    public void Catalog_ParsesAllSevenPaths()
    {
        using var c = new IconCatalog();
        Assert.Equal(7, Enum.GetValues<IconId>().Length);
        foreach (IconId id in Enum.GetValues<IconId>())
        {
            using SKPath p = SKPath.ParseSvgPathData(IconCatalog.GetSpec(id).PathData)!;
            Assert.False(p.Bounds.IsEmpty);
            Assert.True(p.Bounds.Left >= 0 && p.Bounds.Top >= 0 && p.Bounds.Right <= 16 && p.Bounds.Bottom <= 16);
        }
    }

    [Theory]
    [InlineData(IconId.Arsenal, 18, 1.5f)]
    [InlineData(IconId.Fases, 18, 1.5f)]
    [InlineData(IconId.Missoes, 18, 1.5f)]
    [InlineData(IconId.Migrar, 18, 1.5f)]
    [InlineData(IconId.Fechar, 16, 1.8f)]
    [InlineData(IconId.SetaPico, 16, 2f)]
    [InlineData(IconId.Alerta, 20, 1.6f)]
    public void Specs_MatchSdd(IconId id, float size, float stroke)
    {
        IconSpec s = IconCatalog.GetSpec(id);
        Assert.Equal(size, s.SizeDip);
        Assert.Equal(stroke, s.StrokeUnits);
    }

    [Fact]
    public void Masks_Alpha8_RoundedSize_NonEmpty_AllScales()
    {
        using var c = new IconCatalog();
        foreach (IconId id in Enum.GetValues<IconId>())
        foreach (int dpi in Dpis)
        {
            SKBitmap m = c.GetMask(id, dpi);
            int expected = (int)Math.Round(IconCatalog.GetSpec(id).SizeDip * dpi / 96.0, MidpointRounding.AwayFromZero);
            Assert.Equal(SKColorType.Alpha8, m.ColorType);
            Assert.Equal(expected, m.Width);
            Assert.Equal(expected, m.Height);
            Assert.Contains(m.Bytes, b => b > 0);
            Assert.Same(m, c.GetMask(id, dpi));
        }
    }

    [Fact]
    public void Fechar_DrawsBothDiagonals_CornersStayEmpty()
    {
        using var c = new IconCatalog();
        SKBitmap m = c.GetMask(IconId.Fechar, 96); // 16 px, k = 1
        Assert.True(m.GetPixelSpan()[8 * 16 + 8] > 0);          // cruzamento
        Assert.True(m.GetPixelSpan()[5 * 16 + 5] > 0);          // diagonal \
        Assert.True(m.GetPixelSpan()[5 * 16 + 10] > 0);         // diagonal /
        Assert.Equal(0, m.GetPixelSpan()[0]);                   // canto
        Assert.Equal(0, m.GetPixelSpan()[15 * 16 + 15]);
    }

    [Fact]
    public void DrawTinted_UsesPaintColor_AtIntegerPosition()
    {
        using var c = new IconCatalog();
        using var bmp = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(0xff, 0x9b, 0xb0) };
        c.DrawTinted(canvas, IconId.Alerta, 96, 20.4f, 20.4f, paint);
        SKColor px = bmp.GetPixel(20, 14); // aresta da seta do triangulo
        bool found = false;
        for (int y = 0; y < 40 && !found; y++)
        for (int x = 0; x < 40; x++)
        {
            SKColor p = bmp.GetPixel(x, y);
            if (p.Alpha == 255) { Assert.Equal(0xff, p.Red); Assert.Equal(0x9b, p.Green); Assert.Equal(0xb0, p.Blue); found = true; break; }
        }
        Assert.True(found);
        _ = px;
    }

    [Fact]
    public void Masks_AreDeterministic()
    {
        using var a = new IconCatalog();
        using var b = new IconCatalog();
        foreach (IconId id in Enum.GetValues<IconId>())
            Assert.True(a.GetMask(id, 144).Bytes.AsSpan().SequenceEqual(b.GetMask(id, 144).Bytes));
    }
}
