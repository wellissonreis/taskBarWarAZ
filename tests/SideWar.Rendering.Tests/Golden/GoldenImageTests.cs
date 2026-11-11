using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>Autoteste do harness (SDD-17 RF-17.3): ΔE76, limite de 0,5% e gravação de actual/diff em falha.</summary>
public sealed class GoldenImageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sidewar-golden-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static SKBitmap Solid(int w, int h, SKColor c)
    {
        var b = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var cv = new SKCanvas(b);
        cv.Clear(c);
        return b;
    }

    [Fact]
    public void DeltaE76_IdenticalIsZero_BlackWhiteIs100_SmallStepIsBelowThreshold()
    {
        Assert.Equal(0, GoldenImage.DeltaE76(new SKColor(10, 20, 30), new SKColor(10, 20, 30)), 6);
        Assert.Equal(100, GoldenImage.DeltaE76(SKColors.Black, SKColors.White), 1);
        Assert.True(GoldenImage.DeltaE76(new SKColor(100, 100, 100), new SKColor(102, 100, 100)) < GoldenImage.DeltaEThreshold);
        Assert.True(GoldenImage.DeltaE76(new SKColor(100, 100, 100), new SKColor(160, 100, 100)) > GoldenImage.DeltaEThreshold);
    }

    [Fact]
    public void Compare_UsesHalfPercentLimit()
    {
        using var a = Solid(100, 100, SKColors.Black);
        using var b = Solid(100, 100, SKColors.Black);
        for (int i = 0; i < 50; i++) b.SetPixel(i, 0, SKColors.White); // 50/10000 = 0,5%: passa
        var r = GoldenImage.Compare(a, b, out var d1);
        d1.Dispose();
        Assert.Equal(50, r.DifferentPixels);
        Assert.True(r.Passed(GoldenImage.MaxFraction));
        b.SetPixel(60, 0, SKColors.White); // 51 pixels: falha
        r = GoldenImage.Compare(a, b, out var d2);
        d2.Dispose();
        Assert.False(r.Passed(GoldenImage.MaxFraction));
    }

    [Fact]
    public void Compare_SizeMismatchFails()
    {
        using var a = Solid(10, 10, SKColors.Black);
        using var b = Solid(11, 10, SKColors.Black);
        var r = GoldenImage.Compare(a, b, out var d);
        d.Dispose();
        Assert.True(r.SizeMismatch);
        Assert.False(r.Passed(1.0));
    }

    [Fact]
    public void Verify_PassOnMatch_FailWritesActualAndDiff_MissingGoldenFails()
    {
        if (GoldenImage.UpdateMode) return; // em modo de atualização Verify grava a referência
        string golden = Path.Combine(_dir, "g"), art = Path.Combine(_dir, "a");
        using var black = Solid(40, 40, SKColors.Black);
        GoldenImage.Save(black, Path.Combine(golden, "x.png"));

        Assert.Null(GoldenImage.Verify("x", black, golden, art));
        Assert.False(File.Exists(Path.Combine(art, "x.diff.png")));

        using var white = Solid(40, 40, SKColors.White);
        string? msg = GoldenImage.Verify("x", white, golden, art);
        Assert.NotNull(msg);
        Assert.True(File.Exists(Path.Combine(art, "x.actual.png")));
        Assert.True(File.Exists(Path.Combine(art, "x.diff.png")));

        Assert.Contains("ausente", GoldenImage.Verify("nao-existe", black, golden, art));
    }
}
