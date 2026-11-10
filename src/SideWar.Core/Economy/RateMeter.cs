namespace SideWar.Core.Economy;

/// <summary>
/// Média móvel de créditos nos últimos 10 s de SimTime (SDD-27 RF-27.3): anel de 10 baldes de 1 s
/// (30 ticks cada). Só avança em <see cref="Tick"/>; simulação pausada = baldes parados.
/// </summary>
public sealed class RateMeter
{
    public const int BucketCount = 10;
    public const int TicksPerBucket = 30;

    private readonly double[] _shown = new double[BucketCount];
    private readonly double[] _income = new double[BucketCount];
    private int _current;
    private int _ticksInBucket;

    /// <summary>Créditos efetivamente ganhos por segundo (com multiplicador de presença): o "+N/s" da UI.</summary>
    public double DisplayedRate => Sum(_shown) / BucketCount;

    /// <summary>Créditos por segundo antes do multiplicador de presença, já com Reservas (base de "min de renda").</summary>
    public double IncomeRate => Sum(_income) / BucketCount;

    /// <summary>Registra ganhos no balde corrente. <paramref name="income"/> é o valor bruto antes do multiplicador de presença (em Ausente, antes do ×0,25).</summary>
    public void Record(double displayed, double income)
    {
        _shown[_current] += displayed;
        _income[_current] += income;
    }

    /// <summary>Avança um tick de simulação (1/30 s); a cada 30 ticks gira o anel e zera o balde mais antigo.</summary>
    public void Tick()
    {
        if (++_ticksInBucket < TicksPerBucket) return;
        _ticksInBucket = 0;
        _current = (_current + 1) % BucketCount;
        _shown[_current] = 0;
        _income[_current] = 0;
    }

    public void Reset()
    {
        Array.Clear(_shown);
        Array.Clear(_income);
        _current = 0;
        _ticksInBucket = 0;
    }

    private static double Sum(double[] buckets)
    {
        var total = 0.0;
        foreach (var b in buckets) total += b;
        return total;
    }
}
