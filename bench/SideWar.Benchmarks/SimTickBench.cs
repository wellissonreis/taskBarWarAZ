using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace SideWar.Benchmarks;

/// <summary>
/// SDD-17 RF-17.5 / SDD-16 P-13: custo de um tick da simulação com ondas cheias (BenchmarkDotNet: média e alocado).
/// Cada operação = 1 tick (TopUp + Step); o p99 vem do harness <c>--p99</c> (<see cref="TickPercentiles"/>).
/// Resultados no Linux/WSL são INDICATIVOS.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, warmupCount: 3, iterationCount: 10)]
public class SimTickBench
{
    private const int TicksPerInvoke = 1000;
    private SimScenario _heavy = null!;
    private SimScenario _medium = null!;

    [GlobalSetup]
    public void Setup()
    {
        _heavy = SimScenario.WaveHeavy();
        _medium = SimScenario.WaveMedium();
    }

    /// <summary>44 inimigos, 12 torres LV5 (cenário presente-onda-pesada, ADR-027: sem projéteis).</summary>
    [Benchmark(OperationsPerInvoke = TicksPerInvoke)]
    public void WaveHeavy()
    {
        var s = _heavy;
        for (int i = 0; i < TicksPerInvoke; i++) s.Tick();
    }

    /// <summary>20 inimigos, 6 torres LV5 (presente-onda-media).</summary>
    [Benchmark(OperationsPerInvoke = TicksPerInvoke)]
    public void WaveMedium()
    {
        var s = _medium;
        for (int i = 0; i < TicksPerInvoke; i++) s.Tick();
    }
}
