using System.Text.Json;
using System.Text.Json.Serialization;
using SideWar.Runtime.Fullscreen;
using SideWar.Runtime.Geometry;
using Xunit;

namespace SideWar.Runtime.Tests.Fullscreen;

public sealed class FullscreenClassifierTests
{
    private const uint Own = 999;
    private static readonly PixelRect Mon = new(0, 0, 1920, 1080);
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);
    private const uint Caption = 0x16CF0000, NoCaption = 0x16010000, Popup = 0x96000000;

    private static WindowSnapshot Snap(PixelRect? frame = null, uint style = NoCaption, bool zoomed = false,
        string cls = "Chrome_WidgetWin_1", nint hwnd = 1, uint pid = 4242, bool iconic = false,
        bool visible = true, bool cloaked = false, PixelRect? mon = null) =>
        new(hwnd, pid, cls, style, 0, zoomed, iconic, visible, cloaked, frame ?? Mon, "D1", mon ?? Mon, Work);

    private static Verdict C(WindowSnapshot w, Quns q = Quns.AcceptsNotifications) =>
        FullscreenClassifier.Classify(w, q, Own);

    [Fact] public void R1_ZeroHwnd() => Assert.Equal(Verdict.NotFullscreen, C(Snap(hwnd: 0)));
    [Fact] public void R1_PrecedesAll() => Assert.Equal(Verdict.NotFullscreen, C(Snap(hwnd: 0, pid: Own, cloaked: true)));
    [Fact] public void R2_OwnProcess() => Assert.Equal(Verdict.Ignore, C(Snap(pid: Own)));
    [Fact] public void R2_PrecedesIconic() => Assert.Equal(Verdict.Ignore, C(Snap(pid: Own, iconic: true)));
    [Fact] public void R3_Cloaked() => Assert.Equal(Verdict.Ignore, C(Snap(cloaked: true)));
    [Fact] public void R3_Invisible() => Assert.Equal(Verdict.Ignore, C(Snap(visible: false)));
    [Fact] public void R3_PrecedesIconic() => Assert.Equal(Verdict.Ignore, C(Snap(cloaked: true, iconic: true)));
    [Fact] public void R4_Iconic() => Assert.Equal(Verdict.NotFullscreen, C(Snap(iconic: true)));
    [Fact] public void R4_IconicBeatsQuns() => Assert.Equal(Verdict.NotFullscreen, C(Snap(iconic: true), Quns.D3DFullscreen));

    [Theory]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("XamlExplorerHostIslandWindow")]
    [InlineData("MultitaskingViewFrame")]
    [InlineData("ForegroundStaging")]
    [InlineData("TaskListThumbnailWnd")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("NotifyIconOverflowWindow")]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    public void R5_ShellTransient(string cls) =>
        Assert.Equal(Verdict.Ignore, C(Snap(cls: cls, style: Popup), Quns.D3DFullscreen));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    public void R6_Desktop(string cls) =>
        Assert.Equal(Verdict.NotFullscreen, C(Snap(cls: cls, style: Popup), Quns.D3DFullscreen));

    [Fact] public void R5_IsCaseSensitive() => Assert.Equal(Verdict.Fullscreen, C(Snap(cls: "shell_traywnd")));
    [Fact] public void R8_NoCaptionCovering() => Assert.Equal(Verdict.Fullscreen, C(Snap()));
    [Fact] public void R8_PartialCaptionBitsIsNotCaption() => Assert.Equal(Verdict.Fullscreen, C(Snap(style: 0x00400000)));
    [Fact] public void R9_PopupWithCaptionBits() => Assert.Equal(Verdict.Fullscreen, C(Snap(style: Popup | 0x00C00000)));
    [Fact] public void R8_ZoomedNoCaptionIsNotFullscreen() => Assert.Equal(Verdict.NotFullscreen, C(Snap(zoomed: true)));
    [Fact] public void Maximized_Work() => Assert.Equal(Verdict.NotFullscreen, C(Snap(Work, Caption, true)));
    [Fact] public void Maximized_AutoHide_R11() => Assert.Equal(Verdict.NotFullscreen, C(Snap(Mon, Caption, true)));
    [Fact] public void CaptionNotZoomedCovering_R12() => Assert.Equal(Verdict.NotFullscreen, C(Snap(Mon, Caption)));
    [Fact] public void NotCovering_NoCaption() => Assert.Equal(Verdict.NotFullscreen, C(Snap(Work)));

    [Fact] public void R10_D3D_NotCovering() => Assert.Equal(Verdict.Fullscreen, C(Snap(Work, Caption, true), Quns.D3DFullscreen));
    [Fact] public void R10_Presentation() => Assert.Equal(Verdict.Fullscreen, C(Snap(Work, Caption), Quns.PresentationMode));
    [Theory]
    [InlineData(Quns.Busy)]
    [InlineData(Quns.Unknown)]
    [InlineData(Quns.NotPresent)]
    [InlineData(Quns.QuietTime)]
    [InlineData(Quns.App)]
    [InlineData(Quns.AcceptsNotifications)]
    public void OtherQunsAloneDoNotClassify(Quns q) => Assert.Equal(Verdict.NotFullscreen, C(Snap(Work, Caption, true), q));

    // Tolerance / edge cases (T-06.1)
    [Theory]
    [InlineData(1, 1, 1919, 1079, Verdict.Fullscreen)]    // exactly 1 px inside on each side
    [InlineData(2, 0, 1920, 1080, Verdict.NotFullscreen)]
    [InlineData(0, 2, 1920, 1080, Verdict.NotFullscreen)]
    [InlineData(0, 0, 1918, 1080, Verdict.NotFullscreen)]
    [InlineData(0, 0, 1920, 1078, Verdict.NotFullscreen)]
    [InlineData(-8, -8, 1928, 1088, Verdict.Fullscreen)]  // frame larger than monitor
    public void Tolerance(int l, int t, int r, int b, Verdict expected) =>
        Assert.Equal(expected, C(Snap(new PixelRect(l, t, r, b))));

    [Fact]
    public void NegativeOriginMonitor()
    {
        var mon = new PixelRect(-1920, -200, 0, 880);
        Assert.Equal(Verdict.Fullscreen, C(Snap(mon, mon: mon)));
        Assert.Equal(Verdict.NotFullscreen, C(Snap(new PixelRect(0, 0, 1920, 1080), mon: mon)));
        Assert.Equal(Verdict.Fullscreen, C(Snap(new PixelRect(-1919, -199, -1, 879), mon: mon)));
        Assert.Equal(Verdict.NotFullscreen, C(Snap(new PixelRect(-1918, -200, 0, 880), mon: mon)));
    }

    [Fact]
    public void ExtremeValuesDoNotOverflow() =>
        Assert.Equal(Verdict.Fullscreen, C(Snap(new PixelRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue),
            mon: new PixelRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue))));

    [Fact]
    public void NullClassNameIsTreatedAsEmpty() => Assert.Equal(Verdict.Fullscreen, C(Snap(cls: null!)));

    [Fact]
    public void ShellClassLists_AreTheSpecLists()
    {
        Assert.Equal(9, ShellClasses.ShellTransient.Count);
        Assert.Equal(2, ShellClasses.Desktop.Count);
    }

    // T-06.2 fixtures (synthetic until Windows captures exist; CA-06.1 needs real ones)
    private static readonly JsonSerializerOptions Opts = new() { Converters = { new JsonStringEnumConverter() } };

    private sealed record Fx(bool Synthetic, string Name, Quns Quns, Verdict Expected, long Hwnd, uint ProcessId,
        string ClassName, uint Style, uint ExStyle, bool IsZoomed, bool IsIconic, bool IsVisible, bool IsCloaked,
        PixelRect Frame, string MonitorDevice, PixelRect MonitorBounds, PixelRect MonitorWork);

    public static IEnumerable<object[]> FixtureFiles() =>
        Directory.GetFiles(Path.Combine(FindRepository(), "tests/SideWar.Runtime.Tests/Fixtures/windows"), "*.json")
            .Order(StringComparer.Ordinal).Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });

    [Fact]
    public void FixturesExist() => Assert.True(FixtureFiles().Count() >= 16);

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void Fixtures(string name)
    {
        var path = Path.Combine(FindRepository(), "tests/SideWar.Runtime.Tests/Fixtures/windows", name + ".json");
        var fx = JsonSerializer.Deserialize<Fx>(File.ReadAllText(path), Opts)!;
        Assert.True(fx.Synthetic);
        var w = new WindowSnapshot((nint)fx.Hwnd, fx.ProcessId, fx.ClassName, fx.Style, fx.ExStyle, fx.IsZoomed,
            fx.IsIconic, fx.IsVisible, fx.IsCloaked, fx.Frame, fx.MonitorDevice, fx.MonitorBounds, fx.MonitorWork);
        Assert.Equal(fx.Expected, FullscreenClassifier.Classify(w, fx.Quns, Own));
    }

    private static string FindRepository()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "specs/PLANO.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
