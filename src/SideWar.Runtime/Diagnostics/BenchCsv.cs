using System.Globalization;
using System.Text;

namespace SideWar.Runtime.Diagnostics;

/// <summary>One CSV line per second (SDD-16 RF-16.2 + RF-16.5 UIA columns). Times in ms, memory in MB.</summary>
public readonly record struct BenchRow(
    double T, double Fps, double ComposeP50, double ComposeP95, double PresentP50, double PresentP95,
    double TickMean, double TickP99, long Wakes, long AllocBytes, int Gen0, int Gen1, int Gen2,
    double PrivateMB, double WorkingSetMB, double ManagedHeapMB, int Handles, int GdiObjects, int UserObjects,
    long TaskbarQueries, long TaskbarUpdates, double TaskbarQueryMsP95, double? UiaRetainedMB);

public static class BenchCsv
{
    public const string Header =
        "t,fps,composeP50,composeP95,presentP50,presentP95,tickMean,tickP99,wakes,allocBytes,gen0,gen1,gen2," +
        "privateMB,workingSetMB,managedHeapMB,handles,gdiObjects,userObjects," +
        "taskbarQueries,taskbarUpdates,taskbarQueryMsP95,uiaRetainedMB";

    public const int ColumnCount = 23;

    /// <summary>Invariant culture, '.' decimals. `uiaRetainedMB` is empty when not measured (T-21.8).</summary>
    public static string Format(in BenchRow r)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(220);
        sb.Append(r.T.ToString("F1", c)).Append(',').Append(r.Fps.ToString("F2", c)).Append(',')
          .Append(r.ComposeP50.ToString("F3", c)).Append(',').Append(r.ComposeP95.ToString("F3", c)).Append(',')
          .Append(r.PresentP50.ToString("F3", c)).Append(',').Append(r.PresentP95.ToString("F3", c)).Append(',')
          .Append(r.TickMean.ToString("F4", c)).Append(',').Append(r.TickP99.ToString("F4", c)).Append(',')
          .Append(r.Wakes.ToString(c)).Append(',').Append(r.AllocBytes.ToString(c)).Append(',')
          .Append(r.Gen0.ToString(c)).Append(',').Append(r.Gen1.ToString(c)).Append(',').Append(r.Gen2.ToString(c)).Append(',')
          .Append(r.PrivateMB.ToString("F2", c)).Append(',').Append(r.WorkingSetMB.ToString("F2", c)).Append(',')
          .Append(r.ManagedHeapMB.ToString("F2", c)).Append(',')
          .Append(r.Handles.ToString(c)).Append(',').Append(r.GdiObjects.ToString(c)).Append(',').Append(r.UserObjects.ToString(c)).Append(',')
          .Append(r.TaskbarQueries.ToString(c)).Append(',').Append(r.TaskbarUpdates.ToString(c)).Append(',')
          .Append(r.TaskbarQueryMsP95.ToString("F2", c)).Append(',')
          .Append(r.UiaRetainedMB is { } u ? u.ToString("F2", c) : "");
        return sb.ToString();
    }
}
