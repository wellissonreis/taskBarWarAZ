using System.Text.Json;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Xunit;

namespace SideWar.Runtime.Tests.Taskbar;

public sealed class TaskbarTests
{
    private static TaskbarContext Context => new(new("DISPLAY", new(0, 0, 1440, 900),
        new(0, 0, 1440, 852), 96, true, 0), new(0, 852, 1440, 900), TaskbarEdge.Bottom, false);
    private static UiaNode Node(int x, string id = "app", string cls = "TaskListButton") =>
        new(cls, "", id, new(x, 852, x + 44, 900));
    private static UiaNode[] Nodes => [Node(500, "StartButton"), Node(548), Node(596)];

    [Theory]
    [InlineData("centro")]
    [InlineData("esquerda")]
    [InlineData("com-widgets")]
    [InlineData("muitos-apps")]
    [InlineData("pequena")]
    public void SyntheticFixtures(string name)
    {
        // Source fixtures intentionally remain synthetic; CA-21.1 still requires Windows captures.
        var path = Path.Combine(FindRepository(), "tests/SideWar.Runtime.Tests/Fixtures/Taskbar", name + ".json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        Assert.True(root.GetProperty("Synthetic").GetBoolean());
        var height = root.GetProperty("Height").GetInt32();
        var start = root.GetProperty("Start").GetInt32();
        var count = root.GetProperty("Count").GetInt32();
        var widgets = root.GetProperty("Widgets").GetBoolean();
        var nodes = root.GetProperty(nameof(Nodes)).Deserialize<UiaNode[]>()!;
        var layout = TaskbarClassifier.Classify(nodes, Context with { TaskbarRectPx = new(0, 900-height, 1440, 900) });
        Assert.Equal(TaskbarSource.Uia, layout.Source);
        Assert.Equal(count, layout.AppButtons.Count);
        Assert.Equal(height, layout.HeightDip);
        Assert.Equal(1240, layout.TrayLeftX);
        Assert.Equal(start == 0 ? TaskbarAlignment.Left : TaskbarAlignment.Center, layout.Alignment);
        Assert.Equal((widgets ? 44f : 0f, (float)start), layout.LeftFreeZone);
        Assert.Equal(widgets, layout.WidgetsButton.HasValue);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(i, layout.AppButtons[i].Order);
            Assert.Equal(start + 48 * (i + 1), layout.AppButtons[i].Rect.X);
        }
    }

    [Fact]
    public void RulesPrecedenceDiscardAndTieBreak()
    {
        UiaNode[] nodes = [Node(500, "StartButton"), Node(600) with { Rect = new(600, 860, 644, 900) },
            Node(600), Node(700, "SearchButton"), Node(800, cls: "SystemTray.TaskListButton"),
            Node(900) with { HasNotificationAreaAncestor = true },
            Node(1000, cls: "unknown") with { ParentClassName = "MSTaskListWClass" },
            Node(1100, cls: "unknown"), Node(-1), Node(1200) with { Rect = new(1200,852,1207,900) },
            Node(1250) with { Rect = new(1250,852,1294,859) }, Node(1300) with { Rect = default },
            Node(1400), Node(300) with { Rect = new(300,851,344,900) }];
        var layout = TaskbarClassifier.Classify(nodes, Context);
        Assert.Equal(3, layout.AppButtons.Count);
        Assert.Equal(852, layout.AppButtons[0].Rect.Y);
        Assert.Equal(860, layout.AppButtons[1].Rect.Y);
        Assert.Equal(1000, layout.AppButtons[2].Rect.X);
        Assert.Equal(800, layout.TrayLeftX);
    }

    [Theory]
    [InlineData(96)] [InlineData(120)] [InlineData(144)] [InlineData(192)]
    public void PhysicalCoordinatesUseNegativeMonitorOrigin(uint dpi)
    {
        var scale = dpi / 96.0;
        PixelRect Px(int x, int y, int w, int h) => new(-3000 + (int)(x*scale), -2000 + (int)(y*scale),
            -3000 + (int)((x+w)*scale), -2000 + (int)((y+h)*scale));
        var context = new TaskbarContext(new("NEGATIVE", Px(0,0,1440,900), Px(0,0,1440,852), dpi,false,0),
            Px(0,852,1440,48), TaskbarEdge.Bottom,false);
        var layout = TaskbarClassifier.Classify([Node(0,"StartButton") with { Rect = Px(500,852,44,48) },
            Node(0) with { Rect = Px(548,852,44,48) }], context);
        Assert.Equal(new DipRect(548,852,44,48), layout.AppButtons[0].Rect);
        Assert.Equal(1240,layout.TrayLeftX);
    }

    [Theory]
    [InlineData(TaskbarEdge.Top,false,true)] [InlineData(TaskbarEdge.Left,false,true)]
    [InlineData(TaskbarEdge.Right,false,true)] [InlineData(TaskbarEdge.Unknown,false,true)]
    [InlineData(TaskbarEdge.Bottom,true,true)] [InlineData(TaskbarEdge.Bottom,false,false)]
    public void ContextFallback(TaskbarEdge edge, bool autoHide, bool useUia)
    {
        var layout = TaskbarClassifier.Classify(Nodes, Context with { Edge=edge, AutoHide=autoHide, UseUia=useUia });
        Assert.Equal(TaskbarSource.Fallback,layout.Source);
        Assert.Empty(layout.AppButtons);
        Assert.Empty(layout.Blocked);
        Assert.Equal(TowerHost.Band, BuildZoneMap.From(layout, 1440, new()).Host);
    }

    [Fact]
    public void MissingStartOrAppsFallsBack()
    {
        Assert.Equal(TaskbarSource.Fallback,TaskbarClassifier.Classify([Node(548)],Context).Source);
        Assert.Equal(TaskbarSource.Fallback,TaskbarClassifier.Classify([Node(500,"StartButton")],Context).Source);
    }

    [Fact]
    public void NotificationComparisonUsesToleranceAndContent()
    {
        var layout = TaskbarClassifier.Classify(Nodes,Context);
        Assert.True(TaskbarLayoutComparison.Equivalent(layout,TaskbarClassifier.Classify(Nodes,Context)));
        var buttons = layout.AppButtons.ToArray();
        buttons[0] = buttons[0] with { Rect = buttons[0].Rect with { X = buttons[0].Rect.X + 0.5f } };
        Assert.True(TaskbarLayoutComparison.Equivalent(layout,layout with { AppButtons=buttons }));
        buttons[0] = buttons[0] with { Rect = buttons[0].Rect with { X = buttons[0].Rect.X + 0.01f } };
        Assert.False(TaskbarLayoutComparison.Equivalent(layout,layout with { AppButtons=buttons }));
        Assert.False(TaskbarLayoutComparison.Equivalent(layout,layout with { Source=TaskbarSource.Fallback }));
    }

    [Theory]
    [InlineData(16, TaskbarAlignment.Left)]
    [InlineData(17, TaskbarAlignment.Center)]
    public void WidgetCornerAndAlignmentThreshold(int gap, TaskbarAlignment expected)
    {
        var layout = TaskbarClassifier.Classify([Node(0,"WidgetsButton"),
            Node(44+gap,"StartButton"),Node(200)],Context);
        Assert.Equal((44f,44f+gap),layout.LeftFreeZone);
        Assert.Equal(expected,layout.Alignment);
    }

    [Fact]
    public void WidgetAwayFromCornerParticipatesInFirstLeft()
    {
        var layout = TaskbarClassifier.Classify([Node(100,"WidgetsButton"),
            Node(500,"StartButton"),Node(548)],Context);
        Assert.Equal((0f,100f),layout.LeftFreeZone);
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,"CLAUDE.md"))) directory=directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }
}
