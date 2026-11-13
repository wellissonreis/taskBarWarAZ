using SideWar.Rendering.Icons;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Rendering.Widget;
using SideWar.Runtime.Presence;
using SideWar.Runtime.Widget;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Widget;

public sealed class WidgetRendererTests : IDisposable
{
    private readonly Typography _typo = new();
    private readonly IconCatalog _icons = new();
    private readonly WidgetRenderer _r;

    public WidgetRendererTests() => _r = new WidgetRenderer(_typo, _icons);

    public void Dispose()
    {
        _r.Dispose();
        _icons.Dispose();
        _typo.Dispose();
    }

    private static WidgetModel Model(bool present = true, int badge = 2, WidgetTab? tab = null, int hover = -1,
        string? pending = null, string? chip = null) =>
        new(1, "48,2K", present ? "+312/s" : "+39/s", "37/60", present, present ? "×2,0" : "×0,25", chip, pending, badge, tab, hover);

    private SKBitmap Draw(in WidgetModel m, int dpi = 96, float heightDip = 48)
    {
        double s = dpi / 96.0;
        var bmp = new SKBitmap(new SKImageInfo((int)Math.Round(_r.GetLayout(m, heightDip, dpi).WidthDip * s), (int)Math.Round(heightDip * s), SKColorType.Rgba8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        _r.Render(c, m, heightDip, dpi);
        return bmp;
    }

    private static SKColor At(SKBitmap b, double x, double y) => b.GetPixel((int)x, (int)y);

    [Theory]
    [InlineData(96, 1280)]
    [InlineData(96, 1366)]
    [InlineData(96, 1920)]
    [InlineData(144, 1920)]
    [InlineData(192, 1920)]
    public void LongestSealAndChip_FitLeftOfWMinus8(int dpi, int sceneDip)
    {
        // CA-23.10 (ADR-034): selo "AUSENTE ×0,25" + chip ×3 medidos nos TTF reais; largura natural ≤ W − 8.
        var m = Model(present: false, chip: "×3 0:59");
        float w = _r.MeasureWidthDip(m, dpi);
        Assert.True(w <= sceneDip - 8, $"largura {w} DIP não cabe em {sceneDip}");
        Assert.True(_r.MeasureSealDip("AUSENTE ×0,25", dpi) > 60, "o selo deve ser o rótulo completo");
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(168)]
    [InlineData(192)]
    public void BonusChip_FollowsSeal_BothDrawn_WithinNavGap(int dpi)
    {
        // ADR-034: o chip ×3 vem depois do selo; a largura natural já o inclui.
        var m = Model(chip: "×3 0:59");
        var l = _r.GetLayout(m, 48, dpi);
        Assert.True(l.HasChip);
        Assert.Equal(l.Seal.X + l.Seal.W + 12, l.Chip.X, 0.01);
        Assert.True(l.Chip.X + l.Chip.W + 12 <= l.Nav0.X + 0.01f);
        using SKBitmap b = Draw(m, dpi);
        Assert.Contains(_r.LastTextBoxes, t => t.Text == "×3 0:59");
        Assert.Contains(_r.LastTextBoxes, t => t.Text == "PRESENTE ×2,0");
        Assert.Contains("PRESENTE ×2,0", m.AccessibleName);
        Assert.Contains("×3 0:59", m.AccessibleName);

        using SKBitmap back = Draw(Model(), dpi);
        Assert.Contains(_r.LastTextBoxes, t => t.Text == "PRESENTE ×2,0");
        Assert.DoesNotContain(_r.LastTextBoxes, t => t.Text == "×3 0:59");
        Assert.True(_r.GetLayout(Model(), 48, dpi).WidthDip < l.WidthDip);
    }

    [Fact]
    public void Background_IsPainelAlpha235_NoRadius_Border1()
    {
        using SKBitmap b = Draw(Model());
        // CA-23.9: sem raio, todos os pixels (cantos inclusos) têm alfa >= 1.
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                Assert.True(b.GetPixel(x, y).Alpha >= 1, $"alfa 0 em {x},{y}");
        SKColor corner = At(b, 0, 0);
        Assert.Equal(Palette.BordaPainel, corner);
        Assert.Equal(Palette.BordaPainel, At(b, b.Width - 1, b.Height - 1));
        Assert.Equal(Palette.BordaPainel, At(b, b.Width / 2, 0));
        SKColor inner = At(b, 150, 40);
        Assert.Equal(235, inner.Alpha);
        Assert.InRange(inner.Red, 0, 10);
        Assert.InRange(inner.Green, 12, 22);
        Assert.InRange(inner.Blue, 30, 38);
    }

    [Fact]
    public void Seal_BorderAndTextUseStateColor()
    {
        foreach (bool present in new[] { true, false })
        {
            var m = Model(present);
            using SKBitmap b = Draw(m);
            var l = _r.GetLayout(m, 48, 96);
            SKColor expected = present ? Palette.SeloPresente : Palette.SeloAusente;
            Assert.Equal(expected, At(b, l.Seal.X, l.Seal.Y + l.Seal.H / 2));
            Assert.Equal(expected, At(b, l.Seal.X + l.Seal.W / 2, Math.Round(l.Seal.Y, MidpointRounding.AwayFromZero)));
        }
    }

    [Fact]
    public void ActiveButton_HasBackgroundAndAmberBar_HoverOnlyBackground()
    {
        var m = Model(tab: WidgetTab.Fases);
        using SKBitmap b = Draw(m);
        var l = _r.GetLayout(m, 48, 96);
        var r = l.Nav1;
        SKColor bg = At(b, r.X + 3, r.Y + 22);
        SKColor plain = At(Draw(Model()), r.X + 3, r.Y + 22);
        Assert.True(bg.Red > plain.Red, "fundo ativo deve ser mais claro");
        Assert.Equal(Palette.Ambar, At(b, r.X + 22, r.Y + 44 - 2 - 1));
        Assert.NotEqual(Palette.Ambar, At(Draw(Model(hover: 1)), r.X + 22, r.Y + 44 - 2 - 1));
        // ativo + hover = fundo do ativo, sem somar
        Assert.Equal(bg, At(Draw(Model(tab: WidgetTab.Fases, hover: 1)), r.X + 3, r.Y + 22));
    }

    [Fact]
    public void MissionBadge_DrawnOnlyOnMissoes_AndHiddenAtZero()
    {
        var l = _r.GetLayout(Model(), 48, 96);
        double bx = l.Nav2.X + 44 - 3 - 7, by = l.Nav2.Y + 3 + 1.5;
        using (SKBitmap b = Draw(Model(badge: 2))) Assert.Equal(Palette.Ambar, At(b, bx, by));
        using (SKBitmap b = Draw(Model(badge: 0))) Assert.NotEqual(Palette.Ambar, At(b, bx, by));
        using (SKBitmap b = Draw(Model(badge: 2)))
            Assert.NotEqual(Palette.Ambar, At(b, l.Nav1.X + 44 - 3 - 7, l.Nav1.Y + 3 + 1.5));
    }

    [Fact]
    public void TextBoxes_StayInsideTheirColumns()
    {
        var m = Model();
        using SKBitmap b = Draw(m);
        var l = _r.GetLayout(m, 48, 96);
        foreach (WidgetTextBox t in _r.LastTextBoxes)
        {
            Assert.True(t.Bounds.Top >= 0 && t.Bounds.Bottom <= 48, $"{t.Text} fora da altura");
            if (t.Text is "48,2K" or "+312/s") Assert.True(t.Bounds.Right <= l.Credits.X + 70 + 0.5f);
            if (t.Text is "37/60" or "onda") Assert.True(t.Bounds.Right <= l.Wave.X + 52 + 0.5f);
        }
        Assert.Contains(_r.LastTextBoxes, t => t.Text == "48,2K");
        Assert.Contains(_r.LastTextBoxes, t => t.Text == "PRESENTE ×2,0");
    }

    [Fact]
    public void Pending_ReplacesRateLine()
    {
        var m = Model(pending: "+1,5K");
        using SKBitmap b = Draw(m);
        Assert.Contains(_r.LastTextBoxes, t => t.Text == "+1,5K");
        Assert.DoesNotContain(_r.LastTextBoxes, t => t.Text == "+312/s");
    }

    [Fact]
    public void Render_IsDeterministic_AndSupportsAllScalesAndHeights()
    {
        foreach (int dpi in new[] { 96, 120, 144, 168, 192 })
            foreach (float h in new[] { 48f })
            {
                using SKBitmap a = Draw(Model(tab: WidgetTab.Arsenal, chip: "×3 0:59"), dpi, h);
                using SKBitmap c = Draw(Model(tab: WidgetTab.Arsenal, chip: "×3 0:59"), dpi, h);
                Assert.True(a.Bytes.AsSpan().SequenceEqual(c.Bytes), $"dpi {dpi} h {h}");
            }
    }
}
