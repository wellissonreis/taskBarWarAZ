using SideWar.Core;
using SideWar.Core.Simulation;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Xunit;
using TowerHost = SideWar.Runtime.Taskbar.TowerHost;

namespace SideWar.Runtime.Tests.Taskbar;

/// <summary>TA.5: BuildZones -> StripLayout and LayoutChanged detection (SDD-22 RF-22.2, ADR-026/034).</summary>
public sealed class SceneLayoutWatcherTests
{
    private sealed class Sink : IGameCommandSink
    {
        public List<IGameCommand> Posted { get; } = [];
        public void Post(IGameCommand command) => Posted.Add(command);
    }

    private static MonitorInfo Monitor(int width = 1920, uint dpi = 96, string dev = "D1") =>
        new(dev, new(0, 0, width, 1080), new(0, 0, width, 1032), dpi, true, 0);

    private static TaskbarLayout Uia(bool autoHide = false) => new(
        "D1", TaskbarEdge.Bottom, autoHide, new DipRect(0, 1032, 1920, 48), 48,
        [new AppButton(0, new DipRect(948, 1032, 44, 48))], null, (192, 686),
        [new BlockedSpan(0, 192, BlockReason.WindowsWidget), new BlockedSpan(686, 1332, BlockReason.StartSearchApps),
         new BlockedSpan(1696, 1920, BlockReason.TrayClock)],
        1712, TaskbarAlignment.Center, TaskbarSource.Uia, 96);

    private static readonly BuildZoneOptions On = new() { TowersInBar = true };

    [Fact]
    public void Factory_UsesSpecConstantsAndCoreHost()
    {
        var zones = BuildZoneMap.From(Uia(), 1920, On);
        var layout = StripLayoutFactory.Create(zones, 1920);
        Assert.Equal(1920, layout.Width);
        Assert.Equal(1874f, layout.SpawnX);
        Assert.Equal(88f, layout.LeakX);
        Assert.Equal(4f, layout.CoreX);
        Assert.Equal(1840f, layout.PortalX);
        Assert.Equal(SideWar.Core.Simulation.TowerHost.Taskbar, layout.Host);
        Assert.Equal(new SideWar.Core.Simulation.FreeZone[] { new(192, 686), new(1332, 1696) }, layout.FreeZones.ToArray());
    }

    [Fact]
    public void Factory_ClipsOrdersAndCapsZones()
    {
        var free = new List<SideWar.Runtime.Taskbar.FreeZone> { new(-10, 50), new(40, 200), new(150, 140), new(300, 2000) };
        for (var i = 0; i < 10; i++) free.Add(new(2100 + i, 2200 + i));
        var layout = StripLayoutFactory.Create(new BuildZones(TowerHost.Band, free, 2, 44), 1000);
        Assert.Equal(new SideWar.Core.Simulation.FreeZone[] { new(0, 50), new(50, 200), new(300, 1000) }, layout.FreeZones.ToArray());
        Assert.Equal(SideWar.Core.Simulation.TowerHost.Band, layout.Host);
    }

    [Fact]
    public void Factory_RejectsTooNarrowStrip() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StripLayoutFactory.Create(new BuildZones(TowerHost.Band, [], 2, 44), StripLayoutFactory.MinWidthDip - 1));

    [Theory]
    [InlineData(1920, 96u, 1920)]
    [InlineData(1920, 144u, 1280)]
    [InlineData(2560, 120u, 2048)]
    public void WidthDip_IsRoundedDipOfWorkArea(int px, uint dpi, int dip) =>
        Assert.Equal(dip, StripLayoutFactory.WidthDip(px, dpi));

    [Fact]
    public void FirstObservationPostsThenIdenticalOnesAreSilent()
    {
        var sink = new Sink();
        var w = new SceneLayoutWatcher(sink);
        var u = w.Observe(Monitor(), Uia(), On);
        Assert.NotNull(u);
        Assert.True(u!.Value.LayoutPosted);
        Assert.Equal(new PixelRect(0, 928, 1920, 1080), u.Value.Scene);
        Assert.Single(sink.Posted);
        Assert.IsType<LayoutChanged>(sink.Posted[0]);
        Assert.Null(w.Observe(Monitor(), Uia(), On));
        Assert.Single(sink.Posted);
    }

    [Fact]
    public void SeededLayoutSuppressesRedundantCommand()
    {
        var layout = StripLayoutFactory.Create(BuildZoneMap.From(Uia(), 1920, On), 1920);
        var sink = new Sink();
        var u = new SceneLayoutWatcher(sink, layout).Observe(Monitor(), Uia(), On);
        Assert.NotNull(u);
        Assert.True(u!.Value.SceneChanged);
        Assert.False(u.Value.LayoutPosted);
        Assert.Empty(sink.Posted);
    }

    [Fact]
    public void BarMonitorAndDpiChangesPostLayoutChanged()
    {
        var sink = new Sink();
        var w = new SceneLayoutWatcher(sink);
        w.Observe(Monitor(), Uia(), On);
        // Auto-hide bar -> Band host.
        Assert.True(w.Observe(Monitor(), Uia(autoHide: true), On)!.Value.LayoutPosted);
        // Different monitor width.
        Assert.True(w.Observe(Monitor(2560), Uia(autoHide: true), On)!.Value.LayoutPosted);
        // DPI change alters width in DIP.
        Assert.True(w.Observe(Monitor(2560, 144), Uia(autoHide: true), On)!.Value.LayoutPosted);
        Assert.Equal(4, sink.Posted.Count);
        Assert.Equal(1707, w.CurrentLayout!.Width);
    }

    [Fact]
    public void ScaleOnlyChangeThatKeepsDipLayoutUpdatesSceneWithoutCommand()
    {
        var sink = new Sink();
        var w = new SceneLayoutWatcher(sink);
        w.Observe(Monitor(1920, 96), Uia(), new BuildZoneOptions());
        var u = w.Observe(Monitor(2880, 144), Uia(), new BuildZoneOptions());
        Assert.NotNull(u);
        Assert.True(u!.Value.SceneChanged);
        Assert.False(u.Value.LayoutPosted);
        Assert.Single(sink.Posted);
    }

    [Fact]
    public void TowersInBarOffYieldsBandHost()
    {
        var u = new SceneLayoutWatcher(new Sink()).Observe(Monitor(), Uia(), new BuildZoneOptions());
        Assert.Equal(TowerHost.Band, u!.Value.Host);
        Assert.Equal(new PixelRect(0, 928, 1920, 1032), u.Value.Scene);
    }
}
