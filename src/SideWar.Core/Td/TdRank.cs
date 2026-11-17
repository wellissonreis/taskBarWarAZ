namespace SideWar.Core.Td;

/// <summary>Patente local (RF-28.21, ADR-014): definida pela soma dos recordes de pontuação por fase. Sem internet.</summary>
public enum TdRankLevel
{
    Estagiario = 0,
    Junior,
    Pleno,
    Senior,
    Arquiteto,
    Mvp,
}

/// <summary>Progresso até a próxima patente: <see cref="Next"/> nulo na patente máxima.</summary>
public readonly record struct TdRankProgress(TdRankLevel Current, TdRankLevel? Next, int Total, int CurrentThreshold, int NextThreshold)
{
    /// <summary>Fração 0..1 do caminho entre a patente atual e a próxima (1 na máxima).</summary>
    public double Fraction => Next is null ? 1.0 : Math.Clamp((Total - CurrentThreshold) / (double)(NextThreshold - CurrentThreshold), 0.0, 1.0);
}

/// <summary>
/// Limiares recalibrados com 20 seeds por fase no TdSanityBot (TD.22): Médio Sênior,
/// Otimizado MVP e SemContra Estagiário. Evidência em specs/evidencias/TD.22.md.
/// </summary>
public static class TdRank
{
    private static readonly int[] Thresholds = [0, 5_000, 12_000, 22_000, 29_000, 34_000];

    public static int Threshold(TdRankLevel level) => Thresholds[(int)level];

    public static string DisplayName(TdRankLevel level) => level switch
    {
        TdRankLevel.Estagiario => "Estagiário",
        TdRankLevel.Junior => "Júnior",
        TdRankLevel.Pleno => "Pleno",
        TdRankLevel.Senior => "Sênior",
        TdRankLevel.Arquiteto => "Arquiteto",
        _ => "MVP",
    };

    /// <summary>Nome sem acento (HUD em Silkscreen, só ASCII).</summary>
    public static string AsciiName(TdRankLevel level) => level switch
    {
        TdRankLevel.Estagiario => "Estagiario",
        TdRankLevel.Junior => "Junior",
        TdRankLevel.Senior => "Senior",
        _ => DisplayName(level),
    };

    public static TdRankLevel For(int totalScore)
    {
        var r = TdRankLevel.Estagiario;
        for (int i = 1; i < Thresholds.Length; i++)
            if (totalScore >= Thresholds[i]) r = (TdRankLevel)i;
        return r;
    }

    public static TdRankProgress Progress(int totalScore)
    {
        totalScore = Math.Max(0, totalScore);
        var cur = For(totalScore);
        if (cur == TdRankLevel.Mvp) return new(cur, null, totalScore, Thresholds[^1], Thresholds[^1]);
        var next = cur + 1;
        return new(cur, next, totalScore, Thresholds[(int)cur], Thresholds[(int)next]);
    }
}
