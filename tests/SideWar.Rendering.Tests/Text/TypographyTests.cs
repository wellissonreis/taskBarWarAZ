using SideWar.Rendering.Text;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Text;

public class TypographyTests
{
    private static readonly int[] Dpis = [96, 120, 144, 168, 192];
    private static readonly int[] SilkMissing = [0x2192, 0x2605, 0x2606];
    private static readonly int[] JbMissing = [0x2605, 0x2606];
    private static readonly float[] Sizes = [9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 24];

    [Theory]
    [InlineData(9, 96, 9)]
    [InlineData(9, 120, 11)]
    [InlineData(10, 120, 13)]
    [InlineData(11, 168, 19)]
    [InlineData(24, 192, 48)]
    public void PhysicalSize_RoundsToInteger(float dip, int dpi, int expected)
        => Assert.Equal(expected, Typography.PhysicalSize(dip, dpi));

    [Fact]
    public void SilkscreenFonts_AliasNoSubpixelNoHintingIntegerSize_AllScales()
    {
        using var t = new Typography();
        foreach (FontRole role in new[] { FontRole.SilkscreenRegular, FontRole.SilkscreenBold })
        foreach (int dpi in Dpis)
        foreach (float size in Sizes)
        {
            SKFont f = t.GetFont(role, size, dpi);
            Assert.Equal(SKFontEdging.Alias, f.Edging);
            Assert.False(f.Subpixel);
            Assert.Equal(SKFontHinting.None, f.Hinting);
            Assert.Equal(MathF.Floor(f.Size), f.Size);
            Assert.Equal(Typography.PhysicalSize(size, dpi), (int)f.Size);
        }
    }

    [Fact]
    public void JetBrainsMonoFonts_GrayscaleAntialiasSlightHinting()
    {
        using var t = new Typography();
        foreach (FontRole role in new[] { FontRole.JetBrainsMonoRegular, FontRole.JetBrainsMonoSemiBold })
        {
            SKFont f = t.GetFont(role, 11, 120);
            Assert.Equal(SKFontEdging.Antialias, f.Edging);
            Assert.False(f.Subpixel);
            Assert.Equal(SKFontHinting.Slight, f.Hinting);
            Assert.Equal(14f, f.Size);
        }
    }

    [Fact]
    public void GetFont_IsCached()
    {
        using var t = new Typography();
        Assert.Same(t.GetFont(FontRole.SilkscreenRegular, 10, 96), t.GetFont(FontRole.SilkscreenRegular, 10, 96));
        Assert.NotSame(t.GetFont(FontRole.SilkscreenRegular, 10, 96), t.GetFont(FontRole.SilkscreenBold, 10, 96));
    }

    [Theory]
    [InlineData(FontRole.SilkscreenRegular, "Silkscreen")]
    [InlineData(FontRole.SilkscreenBold, "Silkscreen")]
    [InlineData(FontRole.JetBrainsMonoRegular, "JetBrains Mono")]
    [InlineData(FontRole.JetBrainsMonoSemiBold, "JetBrains Mono")]
    public void Typefaces_LoadFromEmbeddedResources(FontRole role, string family)
    {
        using var t = new Typography();
        SKTypeface face = t.GetTypeface(role);
        Assert.StartsWith(family, face.FamilyName, StringComparison.Ordinal);
    }

    [Fact]
    public void Silkscreen_DrawsHardPixels_NoIntermediateAlpha()
    {
        using var t = new Typography();
        SKFont font = t.GetFont(FontRole.SilkscreenRegular, 20, 96);
        using var bmp = new SKBitmap(new SKImageInfo(120, 40, SKColorType.Alpha8, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        using (var paint = new SKPaint { IsAntialias = false, Color = SKColors.White })
        {
            c.Clear(SKColors.Transparent);
            c.DrawText("AZ 12", 4, 28, SKTextAlign.Left, font, paint);
        }
        bool any = false;
        foreach (byte b in bmp.Bytes)
        {
            Assert.True(b == 0 || b == 255, $"alfa intermediario {b}");
            any |= b == 255;
        }
        Assert.True(any);
    }

    [Fact]
    public void Glyphs_DesignCharacters_CoverageReport()
    {
        // T-20.4 parcial: Strings.cs ainda nao existe; usa os caracteres do design (RF-20.3 item 7).
        const string text = "×·→★☆ãéêçóÃÉÊÇÓáíúâôõ ABCXYZ abcxyz 0123456789 +-/,.:%";
        using var t = new Typography();
        foreach (FontRole role in Enum.GetValues<FontRole>())
        {
            IReadOnlyList<int> missing = GlyphCoverage.Missing(t.GetTypeface(role), text);
            if (role is FontRole.JetBrainsMonoRegular or FontRole.JetBrainsMonoSemiBold)
            {
                Assert.True(missing.Count == 0 || missing.All(cp => cp is 0x2606 or 0x2605), string.Join(',', missing));
            }
        }
        // O que falta na Silkscreen deve ter fallback na JetBrains Mono ou virar DP-20.2.
        IReadOnlyList<int> silk = GlyphCoverage.Missing(t.GetTypeface(FontRole.SilkscreenRegular), text);
        IReadOnlyList<int> jb = GlyphCoverage.Missing(t.GetTypeface(FontRole.JetBrainsMonoRegular), text);
        // Achado medido: Silkscreen sem U+2192 U+2605 U+2606; JetBrains Mono sem U+2605 U+2606 (estrelas: DP-20.2).
        Assert.Equal(SilkMissing, silk.ToArray());
        Assert.Equal(JbMissing, jb.ToArray());
    }
}
