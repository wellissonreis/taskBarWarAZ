using SideWar.Rendering.Theme;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Theme;

public class HardShadowTests
{
    [Theory]
    [InlineData(96, 6)]
    [InlineData(120, 8)]   // 7,5 -> 8
    [InlineData(144, 9)]
    [InlineData(168, 11)]  // 10,5 -> 11
    [InlineData(192, 12)]
    public void OffsetPx_IsRoundedSixDipScaled(int dpi, int expected) =>
        Assert.Equal(expected, HardShadow.OffsetPx(dpi));

    [Fact]
    public void ShadowRect_IsBodyShiftedBySameOffsetOnBothAxes()
    {
        var r = HardShadow.ShadowRect(new SKRect(10, 20, 110, 70), 144);
        Assert.Equal(new SKRect(19, 29, 119, 79), r);
    }

    [Fact]
    public void Draw_FillsExactlyTheShiftedRect_NoBlur()
    {
        using var bmp = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        HardShadow.Draw(canvas, new SKRect(4, 4, 20, 20), new SKColor(0, 0, 0, 255), 96);
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 40; x++)
            {
                bool inside = x >= 10 && x < 26 && y >= 10 && y < 26;
                Assert.Equal(inside ? (byte)255 : (byte)0, bmp.GetPixel(x, y).Alpha);
            }
    }

    [Fact]
    public void Draw_IsThreadSafe_ConcurrentCallersKeepTheirOwnColor()
    {
        // Regressao do flake: o SKPaint estatico compartilhado era mutado (Color) por varios renderers/testes em paralelo.
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
        {
            var color = new SKColor((byte)(30 * t + 10), (byte)(255 - 30 * t), 77, 255);
            using var bmp = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bmp);
            for (int i = 0; i < 3000; i++)
            {
                canvas.Clear(SKColors.Transparent);
                HardShadow.Draw(canvas, new SKRect(4, 4, 20, 20), color, 96);
                var px = bmp.GetPixel(15, 15);
                if (px != color) { failures.Add($"t{t} i{i}: esperado {color}, obtido {px}"); return; }
            }
        });
        Assert.True(failures.IsEmpty, string.Join("; ", failures));
    }

    [Fact]
    public void Source_UsesNoFilters()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "SideWar.Rendering"))) d = d.Parent;
        var src = File.ReadAllText(Path.Combine(d!.FullName, "src", "SideWar.Rendering", "Theme", "HardShadow.cs"));
        var code = string.Join('\n', src.Split('\n').Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));
        Assert.DoesNotContain("ImageFilter", code);
        Assert.DoesNotContain("MaskFilter", code);
    }
}
