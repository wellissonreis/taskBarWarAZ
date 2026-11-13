using SideWar.Core;
using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Runtime.Input;
using Xunit;

namespace SideWar.Runtime.Tests.Input;

internal static class BuildRig
{
    public static readonly GameContent Content = ContentLoader.LoadEmbedded();
    public static readonly SimContext Ctx = new(Content);

    /// <summary>Faixa 1000 DIP, zonas livres [100,500) e [600,800), torres opcionais por X.</summary>
    public static SimState State(params int[] towerX)
    {
        var layout = StripLayout.Create(1000, 960, 88, 4, 920, TowerHost.Taskbar, [new FreeZone(100, 500), new FreeZone(600, 800)]);
        var s = SimState.CreateInitial(Content, layout, Rng.Seed(7, 9));
        s.Credits = 1_000_000;
        foreach (var x in towerX) Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, x)).IsOk);
        return s;
    }

    public static BuildPointer P(float stripX, int sx = 0, int sy = 0, bool over = true) => new(sx, sy, stripX, over);
}

public sealed class BuildModeControllerTests
{
    private const long Tps = 1000;
    private static BuildModeController New() => new(Tps);
    private static readonly BuildOrigin Bolt = new(TowerType.Bolt);

    private static BuildModeController Drag(SimState s, out BuildOutcome last, float x = 300)
    {
        var c = New();
        c.Press(Bolt, BuildRig.P(0, 10, 10, false), 0);
        last = c.Move(BuildRig.P(x, 40, 40), s.Layout, s.Towers, 10);
        return c;
    }

    [Fact]
    public void PressStartsPressedWithoutOverlay()
    {
        var c = New();
        var o = c.Press(Bolt, BuildRig.P(0, 5, 5, false), 0);
        Assert.Equal(BuildPhase.Pressed, c.Phase);
        Assert.False(c.OverlayActive);
        Assert.False(o.OverlayEntered);
        Assert.Null(c.Ghost);
        Assert.Equal(Bolt, c.Origin);
    }

    [Fact]
    public void PressIgnoredWhenNotIdleOrNoneType()
    {
        var c = New();
        Assert.False(c.Press(new BuildOrigin(TowerType.None), BuildRig.P(0), 0).Changed);
        c.Press(Bolt, BuildRig.P(0), 0);
        Assert.False(c.Press(new BuildOrigin(TowerType.Frost), BuildRig.P(0), 1).Changed);
        Assert.Equal(TowerType.Bolt, c.Origin.Type);
    }

    [Theory]
    [InlineData(6, 0, BuildPhase.Pressed)]   // exatamente 6 px: ainda não
    [InlineData(4, 4, BuildPhase.Pressed)]   // ≈5,66
    [InlineData(7, 0, BuildPhase.Dragging)]
    [InlineData(0, -7, BuildPhase.Dragging)]
    [InlineData(5, 5, BuildPhase.Dragging)]  // ≈7,07
    public void ThresholdOfSixPixels(int dx, int dy, BuildPhase expected)
    {
        var s = BuildRig.State();
        var c = New();
        c.Press(Bolt, BuildRig.P(0, 100, 100, false), 0);
        var o = c.Move(BuildRig.P(300, 100 + dx, 100 + dy), s.Layout, s.Towers, 1);
        Assert.Equal(expected, c.Phase);
        Assert.Equal(expected == BuildPhase.Dragging, o.OverlayEntered);
    }

    [Fact]
    public void DraggingEntersOverlayAndGhostRoundsHalfUp()
    {
        var s = BuildRig.State();
        var c = Drag(s, out var o, 300.5f);
        Assert.True(o.OverlayEntered);
        Assert.True(c.OverlayActive);
        Assert.Equal(new BuildGhost(301, true, PlacementReason.None), c.Ghost);
    }

    [Fact]
    public void ReleaseOnValidSpotPostsBuildTowerAndGoesIdle()
    {
        var s = BuildRig.State();
        var c = Drag(s, out _);
        var o = c.Release(BuildRig.P(310), s.Layout, s.Towers, 20);
        Assert.Equal(new BuildTower(TowerType.Bolt, 310), o.Command);
        Assert.Equal(BuildPhase.Idle, c.Phase);
        Assert.True(o.OverlayExited);
        Assert.True(o.ReleaseCapture);
        Assert.Null(c.Ghost);
    }

    [Fact]
    public void ReleaseOnInvalidSpotOrOutsideStripArms()
    {
        var s = BuildRig.State();
        var c = Drag(s, out _);
        var o = c.Release(BuildRig.P(550), s.Layout, s.Towers, 20); // vão entre zonas
        Assert.Null(o.Command);
        Assert.Equal(BuildPhase.Armed, c.Phase);
        Assert.True(c.OverlayActive);
        Assert.False(o.OverlayEntered);

        c = Drag(s, out _);
        o = c.Release(BuildRig.P(300, over: false), s.Layout, s.Towers, 20); // válido, mas fora da faixa
        Assert.Null(o.Command);
        Assert.Equal(BuildPhase.Armed, c.Phase);
    }

    [Fact]
    public void ReleaseWithoutPassingThresholdArmsAndReleasesCapture()
    {
        var s = BuildRig.State();
        var c = New();
        c.Press(Bolt, BuildRig.P(0, 10, 10, false), 0);
        var o = c.Release(BuildRig.P(300, 12, 11), s.Layout, s.Towers, 5);
        Assert.Equal(BuildPhase.Armed, c.Phase);
        Assert.True(o.OverlayEntered);
        Assert.True(o.ReleaseCapture);
        Assert.Equal(300, c.Ghost!.Value.X);
    }

    [Fact]
    public void ArmedClickValidBuildsInvalidDoesNothing()
    {
        var s = BuildRig.State();
        var c = New();
        c.Press(Bolt, BuildRig.P(0), 0);
        c.Release(BuildRig.P(300), s.Layout, s.Towers, 1);
        var bad = c.Click(BuildRig.P(550), s.Layout, s.Towers, 2);
        Assert.Null(bad.Command);
        Assert.Equal(BuildPhase.Armed, c.Phase);
        Assert.Equal(PlacementReason.OutOfZone, c.Ghost!.Value.Reason);
        var ok = c.Click(BuildRig.P(650), s.Layout, s.Towers, 3);
        Assert.Equal(new BuildTower(TowerType.Bolt, 650), ok.Command);
        Assert.Equal(BuildPhase.Idle, c.Phase);
    }

    [Fact]
    public void ClickOutsideArmedIsIgnored()
    {
        var s = BuildRig.State();
        var c = New();
        Assert.Null(c.Click(BuildRig.P(300), s.Layout, s.Towers, 0).Command);
        c.Press(Bolt, BuildRig.P(0), 0);
        Assert.Null(c.Click(BuildRig.P(300), s.Layout, s.Towers, 0).Command); // Pressed
        Assert.Equal(BuildPhase.Pressed, c.Phase);
    }

    [Fact]
    public void RepositionPostsPlaceTower()
    {
        var s = BuildRig.State();
        var c = New();
        c.Press(new BuildOrigin(TowerType.Frost, 12), BuildRig.P(0), 0);
        c.Release(BuildRig.P(300), s.Layout, s.Towers, 1);
        var o = c.Click(BuildRig.P(320), s.Layout, s.Towers, 2);
        Assert.Equal(new PlaceTower(12, 320), o.Command);
    }

    [Fact]
    public void OverlapAndEdgesUseThePlacementValidator()
    {
        var s = BuildRig.State(300);
        var c = New();
        c.Press(Bolt, BuildRig.P(0), 0);
        c.Release(BuildRig.P(330), s.Layout, s.Towers, 1);
        Assert.Equal(PlacementReason.Overlap, c.Ghost!.Value.Reason); // 30 < 44
        Assert.Null(c.Click(BuildRig.P(343), s.Layout, s.Towers, 2).Command);
        Assert.NotNull(c.Click(BuildRig.P(344), s.Layout, s.Towers, 3).Command); // encostar vale
    }

    [Fact]
    public void RevalidateBetweenTicksFlipsGhostWhenLayoutOrTowersChange()
    {
        var s = BuildRig.State();
        var c = New();
        c.Press(Bolt, BuildRig.P(0), 0);
        c.Release(BuildRig.P(300), s.Layout, s.Towers, 1);
        Assert.True(c.Ghost!.Value.Valid);
        Assert.False(c.Revalidate(s.Layout, s.Towers)); // nada mudou

        var other = BuildRig.State(300); // outra torre ocupou o lugar
        Assert.True(c.Revalidate(other.Layout, other.Towers));
        Assert.Equal(PlacementReason.Overlap, c.Ghost!.Value.Reason);

        var narrow = StripLayout.Create(1000, 960, 88, 4, 920, TowerHost.Taskbar, [new FreeZone(400, 500)]);
        Assert.True(c.Revalidate(narrow, s.Towers));
        Assert.Equal(PlacementReason.OutOfZone, c.Ghost!.Value.Reason);

        // um clique logo depois usa o estado atual, não o do último movimento
        Assert.Null(c.Click(BuildRig.P(300), narrow, s.Towers, 2).Command);
    }

    [Fact]
    public void RevalidateOnlyInsideOverlay()
    {
        var s = BuildRig.State();
        var c = New();
        Assert.False(c.Revalidate(s.Layout, s.Towers));
        c.Press(Bolt, BuildRig.P(0), 0);
        Assert.False(c.Revalidate(s.Layout, s.Towers));
    }

    [Theory]
    [InlineData(BuildCancelReason.Escape)]
    [InlineData(BuildCancelReason.RightClick)]
    [InlineData(BuildCancelReason.PanelClick)]
    [InlineData(BuildCancelReason.Hidden)]
    [InlineData(BuildCancelReason.Absent)]
    public void CancelFromEveryPhase(BuildCancelReason reason)
    {
        var s = BuildRig.State();
        foreach (var phase in new[] { BuildPhase.Pressed, BuildPhase.Dragging, BuildPhase.Armed })
        {
            var c = New();
            c.Press(Bolt, BuildRig.P(0, 0, 0), 0);
            if (phase == BuildPhase.Dragging) c.Move(BuildRig.P(300, 50, 0), s.Layout, s.Towers, 1);
            if (phase == BuildPhase.Armed) c.Release(BuildRig.P(300), s.Layout, s.Towers, 1);
            Assert.Equal(phase, c.Phase);
            var o = c.Cancel(reason);
            Assert.Equal(BuildPhase.Idle, c.Phase);
            Assert.Equal(reason, o.Cancelled);
            Assert.Null(o.Command);
            Assert.True(o.ReleaseCapture);
            Assert.Equal(phase != BuildPhase.Pressed, o.OverlayExited);
        }
    }

    [Fact]
    public void CancelOnIdleIsNoop() => Assert.False(New().Cancel(BuildCancelReason.Escape).Changed);

    [Fact]
    public void CaptureLostCancelsDuringDragButNotWhenArmed()
    {
        var s = BuildRig.State();
        var c = Drag(s, out _);
        Assert.Equal(BuildPhase.Idle, c.Cancel(BuildCancelReason.CaptureLost).To);

        c = New();
        c.Press(Bolt, BuildRig.P(0), 0);
        Assert.Equal(BuildPhase.Idle, c.Cancel(BuildCancelReason.CaptureLost).To); // Pressed

        c = New();
        c.Press(Bolt, BuildRig.P(0), 0);
        c.Release(BuildRig.P(300), s.Layout, s.Towers, 1); // Armed: soltar a captura é normal
        Assert.False(c.Cancel(BuildCancelReason.CaptureLost).Changed);
        Assert.Equal(BuildPhase.Armed, c.Phase);
    }

    [Fact]
    public void ThirtySecondsWithoutMovementCancels()
    {
        var s = BuildRig.State();
        var c = New();
        Assert.Null(c.NextDeadline);
        c.Press(Bolt, BuildRig.P(0), 0);
        c.Release(BuildRig.P(300), s.Layout, s.Towers, 0);
        Assert.Equal(30 * Tps, c.NextDeadline);
        Assert.False(c.OnTick(29_999).Changed);
        c.Move(BuildRig.P(310), s.Layout, s.Towers, 20_000); // movimento zera
        Assert.Equal(50 * Tps, c.NextDeadline);
        Assert.False(c.OnTick(49_999).Changed);
        var o = c.OnTick(50_000);
        Assert.Equal(BuildCancelReason.Timeout, o.Cancelled);
        Assert.Equal(BuildPhase.Idle, c.Phase);
        Assert.Null(c.NextDeadline);
    }

    [Fact]
    public void ReusableAfterExitAndNonFinitePointerKeepsGhost()
    {
        var s = BuildRig.State();
        var c = New();
        c.Press(Bolt, BuildRig.P(0), 0);
        c.Release(BuildRig.P(300), s.Layout, s.Towers, 0);
        c.Move(BuildRig.P(float.NaN), s.Layout, s.Towers, 1);
        Assert.Equal(300, c.Ghost!.Value.X);
        c.Cancel(BuildCancelReason.Escape);
        c.Press(new BuildOrigin(TowerType.Cannon), BuildRig.P(0), 5);
        Assert.Equal(BuildPhase.Pressed, c.Phase);
        Assert.Equal(TowerType.Cannon, c.Origin.Type);
    }

    [Fact]
    public void ConstructorValidates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BuildModeController(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BuildModeController(1, double.NaN));
    }
}

public sealed class BuildModeOverlayModeTests
{
    private const long Tps = 1000;

    [Fact]
    public void BuildForcesInteractiveFromPassiveAndRestoresIt()
    {
        var m = new OverlayModeController(Tps);
        var on = m.SetBuildMode(true, 0);
        Assert.Equal(OverlayMode.Interactive, m.Mode);
        Assert.False(on!.Value.Transparent);
        Assert.Null(m.NextDeadline);
        Assert.True(m.BuildActive);
        var off = m.SetBuildMode(false, 100);
        Assert.Equal(OverlayMode.Passive, m.Mode);
        Assert.True(off!.Value.Transparent);
        Assert.Null(m.NextDeadline);
    }

    [Fact]
    public void BuildSuspendsAutoReturnAndBlocksPassive()
    {
        var m = new OverlayModeController(Tps);
        m.SetBuildMode(true, 0);
        m.OnMouseMove();
        m.OnMouseLeave(10);
        Assert.Null(m.NextDeadline);
        Assert.Null(m.OnTick(1_000_000));
        Assert.Null(m.SetMode(OverlayMode.Passive, 20));
        Assert.Null(m.Toggle(20));
        Assert.Equal(OverlayMode.Interactive, m.Mode);
        m.SetDragActive(true, 30);
        m.SetDragActive(false, 40);
        Assert.Null(m.NextDeadline);
    }

    [Fact]
    public void ExitFromInteractiveKeepsInteractiveAndRestartsCountdown()
    {
        var m = new OverlayModeController(Tps);
        m.SetMode(OverlayMode.Interactive, 0);
        m.SetBuildMode(true, 100);
        Assert.Null(m.NextDeadline);
        Assert.Null(m.SetBuildMode(false, 500));
        Assert.Equal(OverlayMode.Interactive, m.Mode);
        Assert.Equal(500 + 8 * Tps, m.NextDeadline);
        Assert.NotNull(m.OnTick(500 + 8 * Tps));
        Assert.Equal(OverlayMode.Passive, m.Mode);
    }

    [Fact]
    public void ExitWhileCursorOverKeepsCountdownStopped()
    {
        var m = new OverlayModeController(Tps);
        m.SetMode(OverlayMode.Interactive, 0);
        m.SetBuildMode(true, 1);
        m.OnMouseMove(); // cursor sobre a faixa
        m.SetBuildMode(false, 2);
        Assert.Null(m.NextDeadline);
    }

    [Fact]
    public void HiddenEndsBuildAndGoesPassive()
    {
        var m = new OverlayModeController(Tps);
        m.SetBuildMode(true, 0);
        var fx = m.OnHidden(5);
        Assert.False(m.BuildActive);
        Assert.Equal(OverlayMode.Passive, m.Mode);
        Assert.True(fx!.Value.Transparent);
    }

    [Fact]
    public void BuildModeIdempotentAndAlphaIndependent()
    {
        var m = new OverlayModeController(Tps);
        Assert.Null(m.SetBuildMode(false, 0));
        m.SetBuildMode(true, 0);
        Assert.Null(m.SetBuildMode(true, 1));
        Assert.Equal(OverlayModeController.AbsentAlpha, m.SetAbsent(true)!.Value.Alpha);
    }
}

public sealed class SceneHitTesterTests
{
    private const float H = 48;
    private static HitTarget T(SimState s, float x, float y, bool build = false) => SceneHitTester.Test(s, H, build, x, y);

    [Fact]
    public void TowerCellIs44x44AtTop106()
    {
        var s = BuildRig.State(300);
        int id = s.Towers.At(0).Id;
        Assert.Equal(HitTarget.Tower(id), T(s, 300, 106));
        Assert.Equal(HitTarget.Tower(id), T(s, 278, 149.9f));   // canto esquerdo inclusivo
        Assert.Equal(HitTarget.None, T(s, 322, 120));           // direita exclusiva
        Assert.Equal(HitTarget.None, T(s, 277.9f, 120));
        Assert.Equal(HitTarget.None, T(s, 300, 105.9f));        // acima da célula
        Assert.Equal(HitTarget.None, T(s, 300, 150));           // abaixo
    }

    [Fact]
    public void FreeSpaceAndWorldAreNotTargets()
    {
        var s = BuildRig.State(300);
        Assert.Equal(HitTarget.None, T(s, 700, 120)); // espaço livre na barra
        Assert.Equal(HitTarget.None, T(s, 300, 50));  // cena
        Assert.Equal(HitTarget.None, T(s, float.NaN, 120));
        Assert.Equal(HitTarget.None, T(s, 300, float.PositiveInfinity));
    }

    [Fact]
    public void UnplacedTowersAreIgnored()
    {
        var s = BuildRig.State(300);
        Assert.Equal(HitTarget.Tower(s.Towers.At(0).Id), T(s, 300, 120));
        var narrow = StripLayout.Create(1000, 960, 88, 4, 920, TowerHost.Taskbar, [new FreeZone(0, 10)]);
        s.ApplyLayout(narrow); // torre vira SEM POSIÇÃO
        Assert.False(s.Towers.At(0).Placed);
        Assert.Equal(HitTarget.None, T(s, 300, 120));
    }

    [Fact]
    public void EndAnchoredTowerFollowsWidth()
    {
        var s = BuildRig.State(750); // x ≥ W/2 → âncora End, offset = 250
        Assert.Equal(TowerAnchor.End, s.Towers.At(0).Anchor);
        int id = s.Towers.At(0).Id;
        Assert.Equal(HitTarget.Tower(id), T(s, 750, 130));
        var wider = StripLayout.Create(1100, 1060, 88, 4, 1020, TowerHost.Taskbar, [new FreeZone(0, 1100)]);
        s.ApplyLayout(wider);
        Assert.Equal(HitTarget.Tower(id), T(s, 850, 130));
        Assert.Equal(HitTarget.None, T(s, 750, 130));
    }

    [Fact]
    public void BandHostUsesTopTwoToFortySix()
    {
        var layout = StripLayout.Create(1000, 960, 88, 4, 920, TowerHost.Band, [new FreeZone(100, 500)]);
        var s = SimState.CreateInitial(BuildRig.Content, layout, Rng.Seed(1, 1));
        s.Credits = 1_000_000;
        Assert.True(s.Apply(BuildRig.Ctx, new BuildTower(TowerType.Bolt, 300)).IsOk);
        int id = s.Towers.At(0).Id;
        Assert.Equal(HitTarget.Tower(id), T(s, 300, 2));
        Assert.Equal(HitTarget.Tower(id), T(s, 300, 45.9f));
        Assert.Equal(HitTarget.None, T(s, 300, 1.9f));
        Assert.Equal(HitTarget.None, T(s, 300, 46));
        Assert.Equal(HitTarget.None, T(s, 300, 120));
    }

    [Fact]
    public void BuildOverlayCoversWholeWindowAndWinsOverTowers()
    {
        var s = BuildRig.State(300);
        Assert.Equal(HitTarget.Overlay, T(s, 300, 120, build: true));
        Assert.Equal(HitTarget.Overlay, T(s, 0, 0, build: true));
        Assert.Equal(HitTarget.Overlay, T(s, 999.9f, 151.9f, build: true));
        Assert.Equal(HitTarget.None, T(s, 1000, 10, build: true));
        Assert.Equal(HitTarget.None, T(s, 10, 152, build: true));
        Assert.Equal(HitTarget.None, T(s, -1, 10, build: true));
    }

    [Fact]
    public void TallerBarMovesCellAndWindow()
    {
        var s = BuildRig.State(300);
        int id = s.Towers.At(0).Id;
        // H = 60 → top = 104 + round(8) = 112
        Assert.Equal(HitTarget.Tower(id), SceneHitTester.Test(s, 60, false, 300, 112));
        Assert.Equal(HitTarget.None, SceneHitTester.Test(s, 60, false, 300, 111.9f));
        Assert.Equal(HitTarget.Overlay, SceneHitTester.Test(s, 60, true, 10, 163.9f));
        Assert.Equal(HitTarget.None, SceneHitTester.Test(s, 60, true, 10, 164));
    }

    [Theory]
    [InlineData(150, 75, 1.5f, 100f, 50f)]
    [InlineData(96, 96, 1f, 96f, 96f)]
    [InlineData(384, 192, 2f, 192f, 96f)]
    public void PixelsToDip(int x, int y, float scale, float ex, float ey)
    {
        Assert.Equal((ex, ey), SceneHitTester.ToDip(x, y, scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneHitTester.ToDip(1, 1, 0));
    }
}
