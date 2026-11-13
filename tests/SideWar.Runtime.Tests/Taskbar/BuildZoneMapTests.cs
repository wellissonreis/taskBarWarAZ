using System.Text.Json;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Xunit;

namespace SideWar.Runtime.Tests.Taskbar;

/// <summary>T-21.1 (Blocked) and T-21.3 (BuildZoneMap), SDD-21 RF-21.5/21.7, ADR-034.</summary>
public sealed class BuildZoneMapTests
{
    private static TaskbarContext Context(int width, int barTop = 1032) => new(
        new("DISPLAY", new(0, 0, width, 1080), new(0, 0, width, barTop), 96, true, 0),
        new(0, barTop, width, 1080), TaskbarEdge.Bottom, false);

    private static UiaNode Node(int x, string id = "app", string cls = "Taskbar.TaskListButton", int w = 44) =>
        new(cls, "", id, new(x, 1032, x + w, 1080));

    private static UiaNode Tray(int x) => Node(x, "tray", "SystemTray.IconStack", 100);

    private static UiaNode[] CanvasNodes()
    {
        var path = Path.Combine(Repo(), "tests/SideWar.Runtime.Tests/Fixtures/Taskbar/canvas.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("Synthetic").GetBoolean());
        return doc.RootElement.GetProperty("Nodes").Deserialize<UiaNode[]>()!;
    }

    [Fact]
    public void CanvasFixtureProducesBlockedAndExactFreeZones()
    {
        var layout = TaskbarClassifier.Classify(CanvasNodes(), Context(1920));
        Assert.Equal(TaskbarSource.Uia, layout.Source);
        Assert.Equal(1712, layout.TrayLeftX);
        Assert.Equal(new BlockedSpan[]
        {
            new(0, 192, BlockReason.WindowsWidget),
            new(686, 1332, BlockReason.StartSearchApps),
            new(1696, 1920, BlockReason.TrayClock),
        }, layout.Blocked);
        var zones = BuildZoneMap.From(layout, 1920, new());
        Assert.Equal(TowerHost.Taskbar, zones.Host);
        Assert.Equal(new FreeZone[] { new(192, 686), new(1332, 1696) }, zones.Free);
        Assert.Equal(104, zones.HostTopDip);
        Assert.Equal(48, zones.HostHeightDip);
    }

    [Fact]
    public void BlockedWithoutWidgetStartsWithEdgeAndOrdersByX()
    {
        var layout = TaskbarClassifier.Classify(
            [Tray(1200), Node(700), Node(500, "StartButton"), Node(548)], Context(1440));
        Assert.Equal(new BlockedSpan[]
        {
            new(0, 4, BlockReason.Edge),
            new(496, 596, BlockReason.StartSearchApps),
            new(696, 748, BlockReason.StartSearchApps),
            new(1184, 1440, BlockReason.TrayClock),
        }, layout.Blocked);
    }

    [Fact]
    public void MarginsAreFourAndSixteenAndTouchingSpansMerge()
    {
        // 8 DIP gap between the two icons: 4 + 4 margins touch exactly and merge.
        var layout = TaskbarClassifier.Classify([Node(500, "StartButton"), Node(552), Tray(1200)], Context(1440));
        Assert.Contains(new BlockedSpan(496, 600, BlockReason.StartSearchApps), layout.Blocked);
        Assert.Equal(1184, layout.Blocked[^1].X0);
        Assert.Equal(1440, layout.Blocked[^1].X1);
        Assert.Equal(TaskbarClasses.IconMarginDip, 4);
        Assert.Equal(TaskbarClasses.TrayMarginDip, 16);
    }

    [Fact]
    public void WidgetAwayFromCornerIsOrdinaryStartSearchAppsBlock()
    {
        var layout = TaskbarClassifier.Classify(
            [Node(100, "WidgetsButton"), Node(500, "StartButton"), Node(548), Tray(1200)], Context(1440));
        Assert.Equal(BlockReason.Edge, layout.Blocked[0].Reason);
        Assert.Contains(new BlockedSpan(96, 148, BlockReason.StartSearchApps), layout.Blocked);
    }

    [Fact]
    public void MissingTrayUsesRightMinus200()
    {
        var layout = TaskbarClassifier.Classify([Node(500, "StartButton"), Node(548)], Context(1440));
        Assert.Equal(1240, layout.TrayLeftX);
        Assert.Equal(new BlockedSpan(1224, 1440, BlockReason.TrayClock), layout.Blocked[^1]);
    }

    [Fact]
    public void NarrowZonesAreDiscarded()
    {
        // Start ends at 544 (+4 = 548): a gap of 40 DIP is dropped, a gap of 44 is kept.
        var tight = TaskbarClassifier.Classify([Node(500, "StartButton"), Node(592), Tray(1200)], Context(1440));
        Assert.DoesNotContain(BuildZoneMap.From(tight, 1440, new()).Free, z => z.X0 == 548);
        var ok = TaskbarClassifier.Classify([Node(500, "StartButton"), Node(596), Tray(1200)], Context(1440));
        Assert.Contains(new FreeZone(548, 592), BuildZoneMap.From(ok, 1440, new()).Free);
    }

    [Fact]
    public void FractionalEdgesUseCeilAndFloor()
    {
        var layout = TaskbarClassifier.Classify([Node(500, "StartButton"), Node(548), Tray(1200)], Context(1440));
        var fractional = layout with { Blocked = [new(0, 100.2f, BlockReason.Edge), new(300.7f, 1440, BlockReason.TrayClock)] };
        Assert.Equal(new FreeZone[] { new(101, 300) }, BuildZoneMap.From(fractional, 1440, new()).Free);
    }

    [Fact]
    public void AtMostEightZonesKeepingTheLeftOnes()
    {
        var layout = TaskbarClassifier.Classify([Node(500, "StartButton"), Node(548), Tray(1200)], Context(1440));
        var spans = Enumerable.Range(0, 11).Select(i => new BlockedSpan(100 * i + 50, 100 * i + 55, BlockReason.StartSearchApps)).ToArray();
        var zones = BuildZoneMap.From(layout with { Blocked = spans }, 1440, new());
        Assert.Equal(8, zones.Free.Count);
        Assert.Equal(new FreeZone(0, 50), zones.Free[0]);
        Assert.Equal(new FreeZone(555, 650), zones.Free[6]);
    }

    [Theory]
    [InlineData(1280)] [InlineData(1440)] [InlineData(1920)] [InlineData(2560)]
    public void BandFallbackMatchesDp336(int width)
    {
        var zones = BuildZoneMap.From(TaskbarClassifier.Classify([], Context(width)), width, new());
        Assert.Equal(TowerHost.Band, zones.Host);
        Assert.Equal(2, zones.HostTopDip);
        Assert.Equal(44, zones.HostHeightDip);
        var expected = new List<FreeZone>();
        if (width / 2 - 240 - 96 >= 44) expected.Add(new(96, width / 2 - 240));
        if (width - 560 - (width / 2 + 240) >= 44) expected.Add(new(width / 2 + 240, width - 560));
        Assert.Equal(expected, zones.Free);
        if (width == 1920) Assert.Equal(new FreeZone[] { new(96, 720), new(1200, 1360) }, zones.Free);
        if (width == 1280) Assert.Equal(new FreeZone[] { new(96, 400) }, zones.Free);
    }

    public static TheoryData<string> FallbackConditions => new()
    { "edge", "autohide", "fallback", "nostart", "useuia", "towers" };

    [Theory]
    [MemberData(nameof(FallbackConditions))]
    public void EveryFallbackConditionYieldsBand(string condition)
    {
        var nodes = CanvasNodes();
        var context = Context(1920);
        var options = new BuildZoneOptions();
        switch (condition)
        {
            case "edge": context = context with { Edge = TaskbarEdge.Top }; break;
            case "autohide": context = context with { AutoHide = true }; break;
            case "fallback": nodes = []; break;
            case "nostart": nodes = nodes.Where(x => x.AutomationId != "StartButton").ToArray(); break;
            case "useuia": options = new() { UseUia = false }; context = context with { UseUia = false }; break;
            case "towers": options = new() { TowersInBar = false }; break;
        }
        var layout = TaskbarClassifier.Classify(nodes, context);
        var zones = BuildZoneMap.From(layout, 1920, options);
        Assert.Equal(TowerHost.Band, zones.Host);
        Assert.Equal(new FreeZone[] { new(96, 720), new(1200, 1360) }, zones.Free);
    }

    [Fact]
    public void UiaLayoutWithOptionsOffStillBand_AndSingleHostPerCall()
    {
        var layout = TaskbarClassifier.Classify(CanvasNodes(), Context(1920));
        Assert.Equal(TowerHost.Band, BuildZoneMap.From(layout, 1920, new() { UseUia = false }).Host);
        Assert.Equal(TowerHost.Band, BuildZoneMap.From(layout with { AutoHide = true }, 1920, new()).Host);
        Assert.Equal(TowerHost.Taskbar, BuildZoneMap.From(layout, 1920, new()).Host);
    }

    [Theory]
    [InlineData(1280)] [InlineData(1440)] [InlineData(1920)] [InlineData(2560)]
    public void TaskbarZonesAreIntegerAndDisjointAtSeveralWidths(int width)
    {
        var layout = TaskbarClassifier.Classify([Node(width / 2 - 100, "StartButton"), Node(width / 2 - 52), Tray(width - 200)],
            Context(width));
        var zones = BuildZoneMap.From(layout, width, new());
        Assert.Equal(TowerHost.Taskbar, zones.Host);
        Assert.All(zones.Free, z => Assert.True(z.X1 - z.X0 >= 44 && z.X0 >= 0 && z.X1 <= width));
        for (var i = 1; i < zones.Free.Count; i++) Assert.True(zones.Free[i].X0 >= zones.Free[i - 1].X1);
    }

    [Fact]
    public void DumpRoundTripCarriesBlockedAndBuildZones()
    {
        var nodes = CanvasNodes().Select(x => new DumpNode(x.ClassName, x.ParentClassName, x.AutomationId,
            "Button", x.Rect, x.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal))).ToArray();
        var ctx = Context(1920);
        var dump = TaskbarDump.Create(26200, ctx.Monitor, TaskbarEdge.Bottom, false, ctx.TaskbarRectPx, nodes);
        Assert.Equal(3, dump.Layout.Blocked!.Length);
        Assert.Equal(TowerHost.Taskbar, dump.BuildZones!.Host);
        Assert.Equal(new FreeZone[] { new(192, 686), new(1332, 1696) }, dump.BuildZones.Free);
        var back = TaskbarDump.FromJson(dump.ToJson());
        Assert.Equal(dump.BuildZones.Free, back.BuildZones!.Free);
        Assert.True(TaskbarLayoutComparison.Equivalent(dump.Layout.ToLayout(), back.Reclassify()));
        Assert.Equal(3, dump.Layout.ToLayout().Blocked.Count);
    }

    [Fact]
    public void ComparisonDetectsBlockedChanges()
    {
        var layout = TaskbarClassifier.Classify(CanvasNodes(), Context(1920));
        var moved = layout.Blocked.ToArray();
        moved[1] = moved[1] with { X1 = moved[1].X1 + 0.4f };
        Assert.True(TaskbarLayoutComparison.Equivalent(layout, layout with { Blocked = moved }));
        moved[1] = moved[1] with { X1 = moved[1].X1 + 1f };
        Assert.False(TaskbarLayoutComparison.Equivalent(layout, layout with { Blocked = moved }));
    }

    private static string Repo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }
}
