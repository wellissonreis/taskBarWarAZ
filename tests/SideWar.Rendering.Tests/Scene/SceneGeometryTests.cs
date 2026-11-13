using SideWar.Core.Simulation;
using SideWar.Rendering.Scene;
using Xunit;

namespace SideWar.Rendering.Tests.Scene;

/// <summary>CA-22.2 / T-22.2 (ADR-034): valores vigentes de SDD-22 RF-22.5 para W = 1920 e H = 48.</summary>
public sealed class SceneGeometryTests
{
    [Fact]
    public void Core_AndBar()
    {
        Assert.Equal(new DipRect(4, 46, 84, 56), SceneGeometry.Core(4));
        Assert.Equal(new DipRect(14, 38, 64, 5), SceneGeometry.CoreBarContent(4));
        Assert.Equal(new DipRect(13, 37, 66, 7), SceneGeometry.CoreBarOuter(4));
        Assert.Equal(102, SceneGeometry.Core(4).Bottom);
        Assert.Equal(88, SceneGeometry.Core(4).Right); // leakX
    }

    [Fact]
    public void Surface_Ground_StatusLine_AndBar()
    {
        Assert.Equal(102, SceneGeometry.GroundY);
        Assert.Equal(102, SceneGeometry.StatusLineTop);
        Assert.Equal(104, SceneGeometry.StatusLineTop + SceneGeometry.StatusLineHeight);
        Assert.Equal(104, SceneGeometry.BarTop);
        Assert.Equal(152, SceneGeometry.SurfaceHeight(48));
        Assert.Equal(144, SceneGeometry.SurfaceHeight(40));
        Assert.Equal(156, SceneGeometry.SurfaceHeight(52));
    }

    [Theory]
    [InlineData(TowerType.Bolt, 36, 18)]
    [InlineData(TowerType.Cannon, 37, 19)] // round(37/2) = 19 (meio para cima)
    [InlineData(TowerType.Frost, 32, 16)]
    [InlineData(TowerType.ScaleSet, 36, 18)]
    public void Towers_InBar_36DipAt110(TowerType type, float w, float leftOffset)
    {
        var r = SceneGeometry.Tower(type, TowerHost.Taskbar, 640, 48);
        Assert.Equal(new DipRect(640 - leftOffset, 110, w, 36), r);
        Assert.Equal(146, r.Bottom);
    }

    [Theory]
    [InlineData(40, 106)] // 104 + round(4/2)
    [InlineData(48, 110)]
    [InlineData(52, 112)] // 104 + 8
    public void TowerTop_FollowsBarHeight(float h, float top) =>
        Assert.Equal(top, SceneGeometry.TowerTop(TowerHost.Taskbar, h));

    [Theory]
    [InlineData(40, 102)]
    [InlineData(48, 106)]
    [InlineData(52, 108)]
    public void TowerCell_44x44(float h, float top)
    {
        var c = SceneGeometry.TowerCell(TowerHost.Taskbar, 300, h);
        Assert.Equal(new DipRect(278, top, 44, 44), c);
    }

    [Theory]
    [InlineData(TowerType.Bolt, 0, 44)]
    [InlineData(TowerType.Cannon, 0, 45)]
    [InlineData(TowerType.Frost, 3, 39)]
    [InlineData(TowerType.ScaleSet, 0, 44)]
    public void Towers_InBand_44DipAtTop2(TowerType type, float dx, float w)
    {
        var r = SceneGeometry.Tower(type, TowerHost.Band, 662, 48);
        Assert.Equal(new DipRect(640 + dx, 2, w, 44), r);
    }

    [Fact]
    public void TowerLabel_AtBottomRightCorner()
    {
        var r = SceneGeometry.Tower(TowerType.Bolt, TowerHost.Taskbar, 200, 48);
        Assert.Equal((r.X + 36 - 10, 110f + 36 - 10), SceneGeometry.TowerLabelOrigin(r));
        Assert.Equal((200f - 18 + 26, 136f), SceneGeometry.TowerLabelOrigin(r));
    }

    [Fact]
    public void Portal_AtWMinus80With68x52()
    {
        Assert.Equal(new DipRect(1840, 50, 68, 52), SceneGeometry.Portal(1920 - 80));
        Assert.Equal(102, SceneGeometry.Portal(1840).Bottom);
    }

    [Theory]
    [InlineData(EnemyKind.Grunt, 23, 26, 76)]
    [InlineData(EnemyKind.Runner, 38, 22, 60)]  // drone elevado em 20
    [InlineData(EnemyKind.Swarm, 36, 22, 80)]
    [InlineData(EnemyKind.Armored, 32, 30, 72)]
    [InlineData(EnemyKind.Tank, 43, 34, 68)]
    [InlineData(EnemyKind.Boss, 68, 52, 50)]
    public void Enemies_SizeTopAndCenter(EnemyKind kind, float w, float h, float top)
    {
        var r = SceneGeometry.Enemy(kind, 500);
        Assert.Equal(new DipRect(500 - w / 2, top, w, h), r);
        Assert.Equal(kind == EnemyKind.Runner ? 82 : 102, r.Bottom);
        Assert.Equal(top - 2, SceneGeometry.Enemy(kind, 500, -2).Y);
    }

    [Theory]
    [InlineData(EnemyKind.Grunt, 70)]
    [InlineData(EnemyKind.Runner, 54)]
    [InlineData(EnemyKind.Tank, 62)]
    public void EnemyBar_3DipAtTopMinus6_WidthOfSprite(EnemyKind kind, float top)
    {
        var bar = SceneGeometry.EnemyBar(kind, 500);
        var sprite = SceneGeometry.Enemy(kind, 500);
        Assert.Equal(new DipRect(sprite.X, top, sprite.W, 3), bar);
    }

    [Fact]
    public void BossBar_Top42_68x5()
    {
        var boss = SceneGeometry.Enemy(EnemyKind.Boss, 800);
        var bar = SceneGeometry.BossBarContent(boss);
        Assert.Equal(new DipRect(boss.X, 42, 68, 5), bar);
        Assert.Equal(boss.Y - 8, bar.Y);
    }

    [Fact]
    public void BarFill_72PercentOf64And68()
    {
        Assert.Equal(46.08f, SceneGeometry.BarFillWidth(72, 100, 64), 3);
        Assert.Equal(48.96f, SceneGeometry.BarFillWidth(72, 100, 68), 3);
        Assert.Equal(60, SceneGeometry.BarFillWidth(500, 100, 60));
        Assert.Equal(0, SceneGeometry.BarFillWidth(-5, 100, 60));
        Assert.Equal(0, SceneGeometry.BarFillWidth(5, 0, 60));
    }

    [Fact]
    public void RoundHalfUp_MatchesJsMathRound()
    {
        Assert.Equal(0, SceneGeometry.RoundHalfUp(-0.5f));
        Assert.Equal(3, SceneGeometry.RoundHalfUp(2.5f));
        Assert.Equal(-2, SceneGeometry.RoundHalfUp(-2.5f)); // Math.round(-2.5) = -2 em JS
    }

    [Fact]
    public void EaseOut_IsMonotonicWithFixedEnds()
    {
        Assert.Equal(0, SceneGeometry.EaseOut(0));
        Assert.Equal(1, SceneGeometry.EaseOut(1));
        float prev = 0;
        for (int i = 1; i <= 100; i++)
        {
            float v = SceneGeometry.EaseOut(i / 100f);
            Assert.True(v >= prev);
            prev = v;
        }
        Assert.True(SceneGeometry.EaseOut(0.5f) > 0.5f); // ease-out
    }

    [Fact]
    public void Constants_FloatSpec()
    {
        Assert.Equal(38, SceneGeometry.FloatTop);
        Assert.Equal(16, SceneGeometry.FloatRise);
        Assert.Equal(1.4f, SceneGeometry.FloatSeconds);
    }
}
