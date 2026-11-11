using System.Globalization;
using SideWar.App.Bench;
using SideWar.Runtime.Diagnostics;
using Xunit;

namespace SideWar.App.SmokeTests;

/// <summary>`--bench` end to end on real windows, without WPF and without global hotkeys/tray (T1.13, CA-16.1 partial).</summary>
[Trait("Env", "WindowsInteractive")]
public sealed class BenchSmokeTests
{
    private static async Task<(int Exit, string[] Lines, string Log)> Run(string scenario, int seconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sidewar-bench-test-{Guid.NewGuid():N}.csv");
        try
        {
            var log = new StringWriter();
            var exit = await BenchRunner.RunAsync(new BenchOptions(scenario, seconds, path, NoInput: true), log, withTray: false);
            return (exit, File.ReadAllLines(path), log.ToString());
        }
        finally { File.Delete(path); }
    }

    private static double Col(string line, int index) => double.Parse(line.Split(',')[index], CultureInfo.InvariantCulture);

    [Fact]
    public async Task E1SyntheticProducesCsvAtAbout30Fps()
    {
        var (exit, lines, log) = await Run("e1-synthetic", 4);
        Assert.True(exit == 0, log);
        Assert.Equal(BenchCsv.Header, lines[0]);
        Assert.Equal(5, lines.Length); // header + 4 rows
        foreach (var line in lines.Skip(1)) Assert.Equal(BenchCsv.ColumnCount, line.Split(',').Length);
        var fps = lines.Skip(2).Select(l => Col(l, 1)).ToArray();
        Assert.All(fps, f => Assert.InRange(f, 20, 40));
        Assert.True(Col(lines[^1], 13) > 0); // privateMB
        Assert.True(Col(lines[^1], 17) > 0); // gdiObjects
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("baseline-wpf")]
    public async Task HiddenAndBaselineRenderNothing(string scenario)
    {
        var (exit, lines, log) = await Run(scenario, 3);
        Assert.True(exit == 0, log);
        Assert.Equal(4, lines.Length);
        foreach (var line in lines.Skip(1))
        {
            Assert.Equal(0, Col(line, 1)); // fps
            Assert.InRange(Col(line, 8), 0, 5); // wakes per second stay near zero (CA-03.4 spirit)
        }
    }

    [Fact]
    public async Task IdleStaticDrawsOnceAndStaysQuiet()
    {
        var (exit, lines, log) = await Run("idle-static", 4);
        Assert.True(exit == 0, log);
        Assert.Equal(5, lines.Length);
        foreach (var line in lines.Skip(3)) Assert.Equal(0, Col(line, 1)); // no frames after the initial redraws
    }
}
