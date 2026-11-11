using BenchmarkDotNet.Running;
using SideWar.Benchmarks;

// --p99 [--artifacts <dir>] [--reps <n>] [--ticks <n>] [--only <WaveHeavy|WaveMedium>]: harness de percentis (T3.12).
// Qualquer outro argumento vai para o BenchmarkDotNet (--filter, --artifacts, ...).
if (args.Contains("--p99"))
{
    string Opt(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
    return TickPercentiles.Run(Opt("--artifacts", "artifacts/bench/linux"), int.Parse(Opt("--reps", "3"), System.Globalization.CultureInfo.InvariantCulture),
        int.Parse(Opt("--ticks", "100000"), System.Globalization.CultureInfo.InvariantCulture), args.Contains("--only") ? Opt("--only", "") : null);
}
BenchmarkSwitcher.FromAssembly(typeof(SimTickBench).Assembly).Run(args);
return 0;
