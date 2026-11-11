using System.Diagnostics.Metrics;
using SideWar.Runtime.Diagnostics;
using Xunit;

namespace SideWar.Runtime.Tests.Diagnostics;

public sealed class BenchTests
{
    [Fact]
    public void ParseAcceptsImplementedScenarios()
    {
        foreach (var scenario in BenchOptions.Implemented)
        {
            var (o, e) = BenchOptions.Parse(["--bench", scenario, "--seconds", "5", "--out", "x.csv"]);
            Assert.Null(e);
            Assert.Equal((scenario, 5, "x.csv", false), (o!.Scenario, o.Seconds, o.OutPath, o.NoInput));
        }
    }

    [Fact]
    public void ParseDefaultsAndFlags()
    {
        var (o, _) = BenchOptions.Parse(["--bench", "hidden", "--no-input"]);
        Assert.Equal(60, o!.Seconds);
        Assert.Equal("bench-hidden.csv", o.OutPath);
        Assert.True(o.NoInput);
    }

    [Theory]
    [InlineData("--bench")]
    [InlineData("--bench", "--seconds", "5")]
    [InlineData("--bench", "nao-existe")]
    [InlineData("--bench", "soak")]
    [InlineData("--bench", "hidden", "--seconds", "0")]
    [InlineData("--bench", "hidden", "--seconds", "abc")]
    [InlineData("--bench", "hidden", "--seconds", "-3")]
    [InlineData("--bench", "hidden", "--seconds", "86401")]
    public void ParseRejectsInvalid(params string[] args)
    {
        var (o, e) = BenchOptions.Parse(args);
        Assert.Null(o);
        Assert.False(string.IsNullOrEmpty(e));
    }

    [Fact]
    public void PercentileIsNearestRank()
    {
        var v = Enumerable.Range(1, 100).Select(i => (double)i).Reverse().ToList();
        Assert.Equal(50, BenchStats.Percentile(v, 0.50));
        Assert.Equal(95, BenchStats.Percentile(v, 0.95));
        Assert.Equal(99, BenchStats.Percentile(v, 0.99));
        Assert.Equal(0, BenchStats.Percentile([], 0.5));
        Assert.Equal(7, BenchStats.Percentile([7], 0.99));
    }

    [Fact]
    public void DrainSummarizesAndClears()
    {
        var s = new BenchStats();
        for (int i = 1; i <= 10; i++) { s.RecordFrame(i, i * 2); s.RecordTick(1); }
        s.RecordTaskbarQuery(12);
        var w = s.Drain();
        Assert.Equal(10, w.Frames);
        Assert.Equal(5, w.ComposeP50);
        Assert.Equal(10, w.ComposeP95);
        Assert.Equal(20, w.PresentP95);
        Assert.Equal(1, w.TickMean);
        Assert.Equal(12, w.TaskbarQueryMsP95);
        Assert.Equal(0, s.Drain().Frames);
    }

    [Fact]
    public void CsvRowMatchesHeaderAndUsesInvariantCulture()
    {
        var row = new BenchRow(1, 29.97, 0.5, 0.9, 0.4, 0.8, 0.1, 0.3, 31, 1024, 0, 0, 0, 55.5, 60.25, 3.1, 200, 30, 12, 1, 1, 12.5, null);
        var line = BenchCsv.Format(row);
        Assert.Equal(BenchCsv.ColumnCount, BenchCsv.Header.Split(',').Length);
        Assert.Equal(BenchCsv.ColumnCount, line.Split(',').Length);
        Assert.StartsWith("1.0,29.97,0.500,", line);
        Assert.EndsWith(",12.50,", line); // uiaRetainedMB empty = not measured
        Assert.DoesNotContain(';', line);
    }

    [Fact]
    public void MetersExposeTheRf165Instruments()
    {
        var seen = new HashSet<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (i, l) => { if (i.Meter.Name == SideWarMetrics.MeterName) { seen.Add(i.Name); l.EnableMeasurementEvents(i); } },
        };
        listener.Start();
        var longs = 0L;
        var doubles = 0;
        listener.SetMeasurementEventCallback<long>((_, m, _, _) => longs += m);
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => doubles++);
        new BenchStats().RecordFrame(1, 1);
        new BenchStats().RecordTick(1);
        SideWarMetrics.RecordWake();
        SideWarMetrics.RecordSave();
        Assert.Superset(new HashSet<string>
        {
            "sidewar.frames", "sidewar.compose.ms", "sidewar.present.ms", "sidewar.tick.us", "sidewar.wakes", "sidewar.saves",
        }, seen);
        Assert.Equal(3, longs); // frame + wake + save
        Assert.Equal(3, doubles); // compose, present, tick
    }
}
