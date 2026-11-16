using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using BuildTower = SideWar.Core.Td.BuildTower;
using FreeZone = SideWar.Runtime.Taskbar.FreeZone;
using TowerHost = SideWar.Runtime.Taskbar.TowerHost;
using SideWar.Core.Td;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Taskbar;
using SideWar.Runtime.Td;
using Xunit;
using LayoutChangedTd = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.7: faixa Band de 32 DIP (largura da barra menos a área do HUD), LayoutChanged ao TdGameHost, hit-test e fps do render.</summary>
public sealed class TdBandTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();
    private static readonly BuildZoneOptions Td = new() { TdBand = true };

    private static MonitorInfo Monitor(int width = 1920, int height = 1032, uint dpi = 96) =>
        new("DISPLAY1", new PixelRect(0, 0, width, height), new PixelRect(0, 0, width, height), dpi, true, 0);

    private static TaskbarLayout Bar(bool autoHide = false) =>
        new("DISPLAY1", TaskbarEdge.Bottom, autoHide, default, 48, Array.Empty<AppButton>(), null,
            default, [], 0, TaskbarAlignment.Center, TaskbarSource.Uia, 96);

    private sealed class FakeClockSink : IGameCommandSink
    {
        public readonly List<IGameCommand> Posted = [];
        public void Post(IGameCommand command) => Posted.Add(command);
    }

    [Theory]
    [InlineData(1920, 96, 32, 1920 - 160)]
    [InlineData(1920, 144, 48, 1920 - 240)]
    [InlineData(2560, 192, 64, 2560 - 320)]
    public void ComputeTdBand_is_32_dip_above_the_bar_and_leaves_the_hud_area(int width, uint dpi, int heightPx, int rightPx)
    {
        var m = Monitor(width, 1032, dpi);
        var r = SceneLayout.ComputeTdBand(m, Bar());
        Assert.Equal(new PixelRect(0, 1032 - heightPx, rightPx, 1032), r);
    }

    [Fact]
    public void ComputeTdBand_raises_2px_with_autohide_and_rejects_tiny_work_areas()
    {
        Assert.Equal(1030, SceneLayout.ComputeTdBand(Monitor(), Bar(autoHide: true)).Bottom);
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneLayout.ComputeTdBand(Monitor(200, 1032), Bar()));
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneLayout.ComputeTdBand(Monitor(1920, 20), Bar()));
    }

    [Fact]
    public void BuildZoneMap_in_td_mode_is_one_band_zone_between_core_and_portal_regardless_of_uia()
    {
        var zones = BuildZoneMap.From(default, 1760, Td);
        Assert.Equal(TowerHost.Band, zones.Host);
        Assert.Equal(TdBand.HeightDip, zones.HostHeightDip);
        var z = Assert.Single(zones.Free);
        Assert.Equal(new FreeZone(TdBand.EdgeClearDip, 1760 - TdBand.EdgeClearDip), z);
        // TowersInBar não muda nada em TD (TA.1 reprovou)
        Assert.Equal(zones.Free, BuildZoneMap.From(default, 1760, Td with { TowersInBar = true }).Free);
    }

    [Fact]
    public void Watcher_posts_the_td_layout_to_the_td_host_at_start_and_on_dpi_or_monitor_change()
    {
        var sink = new FakeClockSink();
        var w = new SceneLayoutWatcher(sink, null, l => new LayoutChangedTd(l));
        var first = w.Observe(Monitor(), Bar(), Td);
        Assert.NotNull(first);
        Assert.True(first!.Value.LayoutPosted);
        Assert.Equal(1920 - 160, first.Value.Layout.Width);
        Assert.Equal(TdBand.HeightDip, first.Value.Scene.Height);
        Assert.IsType<LayoutChangedTd>(Assert.Single(sink.Posted));

        Assert.Null(w.Observe(Monitor(), Bar(), Td)); // nada mudou
        var dpi = w.Observe(Monitor(2880, 1548, 144), Bar(), Td)!.Value; // 150%: janela maior, mesma faixa lógica em DIP
        Assert.True(dpi.SceneChanged);
        Assert.False(dpi.LayoutPosted);
        Assert.True(w.Observe(Monitor(2560, 1392), Bar(), Td)!.Value.LayoutPosted); // outro monitor
        Assert.Equal(2, sink.Posted.Count); // o DPI não mudou a faixa lógica: sem novo LayoutChanged
    }

    [Fact]
    public void Layout_reaches_the_match_through_the_host_before_the_first_step()
    {
        var clock = new FakeClock();
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        var match = TdMatchState.Create(Content, 1, Rng.Seed(42, 54));
        var host = new TdGameHost(clock, app, match);
        var w = new SceneLayoutWatcher(host, null, l => new LayoutChangedTd(l));
        w.Observe(Monitor(), Bar(), Td);
        Assert.Null(match.Layout);
        host.OnWake(clock);
        Assert.NotNull(match.Layout);
        Assert.Equal(1760, match.Layout!.Width);
        Assert.Contains(host.CommandLog, e => e.Command is LayoutChangedTd);
    }

    private static (TdMatchState Match, TdGameHost Host, FakeClock Clock, AppStateMachine App) Rig()
    {
        var clock = new FakeClock();
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        var match = TdMatchState.Create(Content, 1, Rng.Seed(42, 54));
        var host = new TdGameHost(clock, app, match);
        host.OnCommand(new LayoutChangedTd(StripLayoutFactory.Create(BuildZoneMap.TdBand(1200), 1200)));
        host.OnWake(clock);
        return (match, host, clock, app);
    }

    [Fact]
    public void HitTester_prefers_the_crate_then_the_highest_placed_tower_and_ignores_free_space()
    {
        var (match, host, clock, _) = Rig();
        host.OnCommand(new BuildTower("bolt", 600));
        host.OnWake(clock);
        var tower = match.Towers[0];
        float cx = TdBand.TowerCenterDip(match, tower, 1200);
        Assert.Equal(HitTarget.TdTower(tower.Id), TdHitTester.Test(match, cx, 20));
        Assert.Equal(HitTarget.TdTower(tower.Id), TdHitTester.Test(match, cx + 19, 5));
        Assert.Equal(HitKind.None, TdHitTester.Test(match, cx + 21, 20).Kind);
        Assert.Equal(HitKind.None, TdHitTester.Test(match, 100, 20).Kind);
        Assert.Equal(HitKind.None, TdHitTester.Test(match, cx, 33).Kind);
        Assert.Equal(HitKind.None, TdHitTester.Test(match, float.NaN, 5).Kind);
    }

    [Fact]
    public void HitTester_exposes_unplaced_chips()
    {
        var (match, host, clock, _) = Rig();
        host.OnCommand(new BuildTower("bolt", 600));
        host.OnWake(clock);
        // a faixa encolhe e a zona some: a torre fica SEM POSIÇÃO
        host.OnCommand(new LayoutChangedTd(StripLayout.Create(1200, 1154, 88, 4, 1120, SideWar.Core.Simulation.TowerHost.Band, [new SideWar.Core.Simulation.FreeZone(0, 10)])));
        host.OnWake(clock);
        Assert.Equal(1, match.UnplacedCount);
        Assert.Equal(HitTarget.TdTower(match.Towers[0].Id), TdHitTester.Test(match, TdBand.UnplacedChipX(0), 23));
    }

    private sealed class CountingSink : ITdFrameSink
    {
        public int Frames;
        public void Present(TdMatchState match, double nowSeconds, in TdView view) => Frames++;
    }

    [Fact]
    public void Driver_is_event_driven_zero_fps_in_preparo_and_paused_30_fps_in_wave()
    {
        var (match, host, clock, app) = Rig();
        var sink = new CountingSink();
        var driver = new TdRenderDriver(host, app, clock, sink);
        long step = (long)(clock.TicksPerSecond / 30);
        void Tick(int n)
        {
            for (int i = 0; i < n; i++) { clock.AdvanceTicks(step); host.OnWake(clock); driver.OnWake(clock); }
        }

        driver.OnWake(clock); // primeiro frame (sujo)
        Assert.Equal(1, sink.Frames);
        Assert.Null(driver.NextDeadline);
        Tick(90); // 3 s em Preparo
        Assert.Equal(1, sink.Frames); // 0 fps

        host.OnCommand(new BuildTower("bolt", 600)); // comando em Preparo: um frame
        Tick(1);
        Assert.Equal(2, sink.Frames);
        Tick(30);
        Assert.Equal(2, sink.Frames);

        driver.SetSelected(match.Towers[0].Id); // interação: um frame
        Assert.NotNull(driver.NextDeadline);
        Tick(1);
        Assert.Equal(3, sink.Frames);

        host.OnCommand(new StartWave());
        Tick(31); // ~1 s de Onda
        Assert.InRange(sink.Frames - 3, 29, 33); // 30 fps

        app.SetPause(SideWar.Runtime.Timing.PauseReason.User); // pausa: 0 fps
        int before = sink.Frames;
        Tick(60);
        Assert.Equal(before, sink.Frames);

        app.EnterHidden(HiddenReason.User); // oculto: nada desenha, nem pedido sujo
        driver.MarkDirty();
        Assert.Null(driver.NextDeadline);
        Tick(3);
        Assert.Equal(before, sink.Frames);
    }
}
