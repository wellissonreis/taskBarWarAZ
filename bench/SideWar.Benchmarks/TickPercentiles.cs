using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace SideWar.Benchmarks;

/// <summary>
/// Harness de percentis (média, p50, p99, p99,9, máx) e alocação por tick (GC.GetAllocatedBytesForCurrentThread).
/// Mede só <c>SimState.Step</c> (o TopUp da onda sintética fica fora do cronômetro). Várias repetições para mostrar
/// a variação (máquina compartilhada). Alvos P-13: média ≤ 60 µs, p99 ≤ 150 µs, 0 B/tick. Resultado INDICATIVO (WSL).
/// </summary>
public static class TickPercentiles
{
    public sealed record Row(string Scenario, int Rep, int Ticks, double MeanUs, double P50Us, double P99Us, double P999Us, double MaxUs,
        long AllocBytes, double AvgEnemies, double LoadStart, double LoadEnd);

    public static int Run(string outDir, int reps, int ticks, string? only)
    {
        Directory.CreateDirectory(outDir);
        var rows = new List<Row>();
        foreach (var (name, make) in new (string, Func<SimScenario>)[]
                 { ("WaveHeavy", SimScenario.WaveHeavy), ("WaveMedium", SimScenario.WaveMedium) })
        {
            if (only is not null && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;
            for (int rep = 1; rep <= reps; rep++)
                rows.Add(Measure(name, rep, ticks, make()));
        }

        var sb = new StringBuilder();
        sb.AppendLine("cenario,rep,ticks,media_us,p50_us,p99_us,p99_9_us,max_us,alocado_bytes,inimigos_medio,load1_inicio,load1_fim");
        foreach (var r in rows)
            sb.AppendLine(string.Join(',', r.Scenario, r.Rep, r.Ticks, F(r.MeanUs), F(r.P50Us), F(r.P99Us), F(r.P999Us), F(r.MaxUs),
                r.AllocBytes, F(r.AvgEnemies), F(r.LoadStart), F(r.LoadEnd)));
        File.WriteAllText(Path.Combine(outDir, "SimTick-percentis.csv"), sb.ToString());

        Console.WriteLine("indicativo (WSL) — percentis do tick (só Step); alvos P-13: média ≤ 60 µs, p99 ≤ 150 µs, 0 B");
        Console.WriteLine($"CPUs lógicas: {Environment.ProcessorCount}");
        foreach (var r in rows)
            Console.WriteLine($"{r.Scenario,-10} rep{r.Rep} n={r.Ticks} média={F(r.MeanUs)}µs p50={F(r.P50Us)}µs p99={F(r.P99Us)}µs " +
                              $"p99,9={F(r.P999Us)}µs máx={F(r.MaxUs)}µs alocado={r.AllocBytes}B inimigos≈{F(r.AvgEnemies)} load1={F(r.LoadStart)}→{F(r.LoadEnd)}");
        return 0;
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static double Load1()
    {
        try { return double.Parse(File.ReadAllText("/proc/loadavg").Split(' ')[0], CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException) { return double.NaN; }
    }

    private static Row Measure(string name, int rep, int ticks, SimScenario sc)
    {
        double load0 = Load1();
        var samples = new long[ticks];
        long enemiesSum = 0;
        for (int i = 0; i < 2000; i++) sc.Tick(); // aquece JIT/caches (tiered PGO)
        GC.Collect(); GC.WaitForPendingFinalizers();
        long allocInStep = 0;
        for (int i = 0; i < ticks; i++)
        {
            sc.TopUp();
            enemiesSum += sc.EnemyCount;
            long a = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            sc.Step();
            long t1 = Stopwatch.GetTimestamp();
            allocInStep += GC.GetAllocatedBytesForCurrentThread() - a;
            samples[i] = t1 - t0;
        }
        Array.Sort(samples);
        double toUs = 1_000_000.0 / Stopwatch.Frequency;
        double mean = samples.Average() * toUs;
        return new Row(name, rep, ticks, mean, Pct(samples, 0.50) * toUs, Pct(samples, 0.99) * toUs, Pct(samples, 0.999) * toUs,
            samples[^1] * toUs, allocInStep, (double)enemiesSum / ticks, load0, Load1());
    }

    private static long Pct(long[] sorted, double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
}
