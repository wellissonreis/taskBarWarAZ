using System.Diagnostics.Metrics;

namespace SideWar.Runtime.Diagnostics;

/// <summary>SDD-16 RF-16.5: `Meter("SideWar")`. With no listener every call is a cheap no-op (no allocation).</summary>
public static class SideWarMetrics
{
    public const string MeterName = "SideWar";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Frames = Meter.CreateCounter<long>("sidewar.frames");
    private static readonly Histogram<double> ComposeMs = Meter.CreateHistogram<double>("sidewar.compose.ms", "ms");
    private static readonly Histogram<double> PresentMs = Meter.CreateHistogram<double>("sidewar.present.ms", "ms");
    private static readonly Histogram<double> TickUs = Meter.CreateHistogram<double>("sidewar.tick.us", "us");
    private static readonly Counter<long> Wakes = Meter.CreateCounter<long>("sidewar.wakes");
    private static readonly Counter<long> Saves = Meter.CreateCounter<long>("sidewar.saves");

    public static void RecordFrame(double composeMs, double presentMs)
    {
        Frames.Add(1);
        ComposeMs.Record(composeMs);
        PresentMs.Record(presentMs);
    }

    public static void RecordTick(double microseconds) => TickUs.Record(microseconds);
    public static void RecordWake() => Wakes.Add(1);
    public static void RecordSave() => Saves.Add(1);
}
