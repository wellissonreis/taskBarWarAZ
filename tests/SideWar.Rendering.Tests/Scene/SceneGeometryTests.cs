using SideWar.Core.Simulation;
using SideWar.Rendering.Scene;
using Xunit;

namespace SideWar.Rendering.Tests.Scene;

/// <summary>CA-22.2: todos os valores da tabela de SDD-22 RF-22.5 para W = 1440 (TrayLeftX = 1240).</summary>
public sealed class SceneGeometryTests
{
    private const float TrayLeftX = 1240;

    [Fact]
    public void Core_AndBar()
    {
        Assert.Equal(new DipRect(10, 40, 84, 56), SceneGeometry.Core(10));
        Assert.Equal(new DipRect(22, 32, 60, 5), SceneGeometry.CoreBarContent(10));
        Assert.Equal(new DipRect(21, 31, 62, 7), SceneGeometry.CoreBarOuter(10));
        Assert.Equal(96, SceneGeometry.Core(10).Bottom);
    }

    [Fact]
    public void Core_FallbackAt428_BarFollows()
    {
        Assert.Equal(new DipRect(440, 32, 60, 5), SceneGeometry.CoreBarContent(428));
    }

    [Fact]
    public void Slots_EightCellsAt640Plus48i()
    {
        for (int i = 0; i < 8; i++)
        {
            float cell = 640 + 48 * i;
            float center = cell + 22;
            Assert.Equal(cell, SceneGeometry.CellLeft(center));
            Assert.Equal(new DipRect(cell + 1, 70, 42, 26), SceneGeometry.EmptySlot(cell));
            Assert.Equal(54, SceneGeometry.PlusBox(cell).Y);
            Assert.Equal(44, SceneGeometry.PlusBox(cell).W);
            Assert.Equal(38, SceneGeometry.TowerLabelBox(cell).Y);
            Assert.Equal(cell, SceneGeometry.TowerLabelBox(cell).X);
        }
    }

    [Theory]
    [InlineData(TowerType.Bolt, 0, 44)]
    [InlineData(TowerType.Cannon, 0, 45)]
    [InlineData(TowerType.Frost, 3, 39)]
    [InlineData(TowerType.ScaleSet, 0, 44)]
    public void Towers_XOffsetAndSize(TowerType type, float dx, float w)
    {
        var r = SceneGeometry.Tower(type, 640);
        Assert.Equal(new DipRect(640 + dx, 52, w, 44), r);
        Assert.Equal(96, r.Bottom);
    }

    [Fact]
    public void Portal_At1256With68x52()
    {
        Assert.Equal(new DipRect(TrayLeftX + 16, 44, 68, 52), SceneGeometry.Portal(TrayLeftX + 16));
        Assert.Equal(1256, SceneGeometry.Portal(TrayLeftX + 16).X);
    }

    [Theory]
    [InlineData(EnemyKind.Grunt, 23, 26, 70)]
    [InlineData(EnemyKind.Runner, 38, 22, 74)]
    [InlineData(EnemyKind.Swarm, 36, 22, 74)]
    [InlineData(EnemyKind.Armored, 32, 30, 66)]
    [InlineData(EnemyKind.Tank, 43, 34, 62)]
    [InlineData(EnemyKind.Boss, 68, 52, 44)]
    public void Enemies_SizeTopAndCenter(EnemyKind kind, float w, float h, float top)
    {
        var r = SceneGeometry.Enemy(kind, 500);
        Assert.Equal(new DipRect(500 - w / 2, top, w, h), r);
        Assert.Equal(96, r.Bottom);
        Assert.Equal(top - 2, SceneGeometry.Enemy(kind, 500, -2).Y);
    }

    [Fact]
    public void BossBar_Top36_Left4()
    {
        var boss = SceneGeometry.Enemy(EnemyKind.Boss, 800);
        var bar = SceneGeometry.BossBarContent(boss);
        Assert.Equal(36, bar.Y);
        Assert.Equal(boss.X + 4, bar.X);
        Assert.Equal(60, bar.W);
        Assert.Equal(5, bar.H);
        Assert.Equal(boss.Y - 8, bar.Y);
    }

    [Fact]
    public void BarFill_72PercentIs43Point2()
    {
        Assert.Equal(43.2f, SceneGeometry.BarFillWidth(72, 100), 3);
        Assert.Equal(60, SceneGeometry.BarFillWidth(500, 100));
        Assert.Equal(0, SceneGeometry.BarFillWidth(-5, 100));
        Assert.Equal(0, SceneGeometry.BarFillWidth(5, 0));
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
        Assert.Equal(32, SceneGeometry.FloatTop);
        Assert.Equal(16, SceneGeometry.FloatRise);
        Assert.Equal(1.4f, SceneGeometry.FloatSeconds);
        Assert.Equal(0.85f, SceneGeometry.EmptySlotOpacity);
    }
}
