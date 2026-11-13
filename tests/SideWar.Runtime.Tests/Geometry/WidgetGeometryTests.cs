using SideWar.Runtime.Geometry;
using SideWar.Runtime.Widget;
using Xunit;

namespace SideWar.Runtime.Tests.Geometry;

public sealed class WidgetGeometryTests
{
    private static MonitorInfo Monitor(uint dpi, int left = 0, int top = 0, int w = 1920, int h = 1080) =>
        new("M", new PixelRect(left, top, left + w, top + h), new PixelRect(left, top, left + w, top + h), dpi, true, 0);

    private static PixelRect Scene(MonitorInfo m, int hDip = 104) =>
        new(m.Bounds.Left, m.Bounds.Bottom - (int)Math.Round(hDip * m.Dpi / 96.0), m.Bounds.Right, m.Bounds.Bottom);

    // CA-23.3: left = W − 548 (1372 em 1920) com a largura do canvas (≤ 540), 48 DIP de altura, 100–200 %.
    [Theory]
    [InlineData(96u)]
    [InlineData(120u)]
    [InlineData(144u)]
    [InlineData(168u)]
    [InlineData(192u)]
    public void CanvasWidth_LeftIsWMinus548_TopIsSceneTop_Height48(uint dpi)
    {
        var m = Monitor(dpi);
        var scene = Scene(m);
        var scale = dpi / 96.0;
        var p = WidgetGeometry.Compute(m, scene, 540f).Bounds;
        Assert.Equal(scene.Top, p.Top);
        Assert.Equal((int)Math.Round(48 * scale, MidpointRounding.AwayFromZero), p.Height);
        Assert.Equal((int)Math.Round(540 * scale, MidpointRounding.AwayFromZero), p.Width);
        Assert.Equal(scene.Width - (int)Math.Round(548 * scale, MidpointRounding.AwayFromZero), p.Left);
        if (dpi == 96) Assert.Equal(1372, p.Left);
        Assert.True(p.Right <= scene.Right - (int)Math.Round(8 * scale, MidpointRounding.AwayFromZero));
    }

    [Theory]
    [InlineData(96u, 560f)]
    [InlineData(144u, 600f)]
    [InlineData(192u, 700f)]
    public void WideWidget_IsPushedLeftToKeep8DipMargin(uint dpi, float widthDip)
    {
        var m = Monitor(dpi);
        var scene = Scene(m);
        var scale = dpi / 96.0;
        var p = WidgetGeometry.Compute(m, scene, widthDip).Bounds;
        Assert.Equal(scene.Right - (int)Math.Round(8 * scale, MidpointRounding.AwayFromZero), p.Right);
        Assert.True(p.Left < scene.Width - (int)Math.Round(548 * scale, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void SecondMonitor_IsRelativeToScene()
    {
        var m = Monitor(96, left: 1920, top: 100);
        var scene = Scene(m);
        var p = WidgetGeometry.Compute(m, scene, 480f).Bounds;
        Assert.Equal(1920 + 1372, p.Left);
        Assert.Equal(scene.Top, p.Top);
    }

    [Fact]
    public void SameBounds_NoMatterTheTaskbar_NoFallback()
    {
        var m = Monitor(96);
        Assert.Equal(WidgetGeometry.Compute(m, Scene(m), 480f), WidgetGeometry.Compute(m, Scene(m), 480f));
    }

    // CA-23.10: com o selo mais longo e o chip ×3 a largura cabe à esquerda de W − 8 em 1280, 1366 e 1920.
    [Theory]
    [InlineData(1280)]
    [InlineData(1366)]
    [InlineData(1920)]
    public void LongWidgetWithChip_FitsLeftOfWMinus8(int w)
    {
        var m = Monitor(96, w: w);
        float width = WidgetLayout.NaturalWidth(125f, 62f);
        var p = WidgetGeometry.Compute(m, Scene(m), width).Bounds;
        Assert.True(p.Left >= 0);
        Assert.True(p.Right <= w - 8);
    }

    [Fact]
    public void Throws_OnBadInput()
    {
        var m = Monitor(96);
        Assert.Throws<ArgumentOutOfRangeException>(() => WidgetGeometry.Compute(m with { Dpi = 0 }, Scene(m), 480f));
        Assert.Throws<ArgumentOutOfRangeException>(() => WidgetGeometry.Compute(m, Scene(m), 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => WidgetGeometry.Compute(m, Scene(m), float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => WidgetGeometry.Compute(m, new PixelRect(0, 0, 300, 104), 480f));
    }
}
