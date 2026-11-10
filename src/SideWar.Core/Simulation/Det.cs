namespace SideWar.Core.Simulation;

/// <summary>Helpers de determinismo (SDD-11 RF-11.1).</summary>
public static class Det
{
    /// <summary>Quantização de valores de tabela e de layout: <c>MathF.Round(x * 1024f) / 1024f</c> (meio para par).</summary>
    public static float Quantize(float x) => MathF.Round(x * 1024f, MidpointRounding.ToEven) / 1024f;
}

/// <summary>Adição saturante de <c>long</c> (créditos, XP e Reservas; SDD-27 RF-27.1).</summary>
public static class Sat
{
    public static long Add(long a, long b)
    {
        long r = unchecked(a + b);
        if (b >= 0) return r < a ? long.MaxValue : r;
        return r > a ? long.MinValue : r;
    }
}
