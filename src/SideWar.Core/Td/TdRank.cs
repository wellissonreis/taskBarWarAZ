namespace SideWar.Core.Td;

/// <summary>Patente local (RF-28.21, ADR-014): definida pela soma dos recordes de pontuação por fase. Sem internet.</summary>
public enum TdRankLevel
{
    Estagiario = 0,
    Junior,
    Pleno,
    Senior,
    Especialista,
    Arquiteto,
}

/// <summary>Progresso até a próxima patente: <see cref="Next"/> nulo na patente máxima.</summary>
public readonly record struct TdRankProgress(TdRankLevel Current, TdRankLevel? Next, int Total, int CurrentThreshold, int NextThreshold)
{
    /// <summary>Fração 0..1 do caminho entre a patente atual e a próxima (1 na máxima).</summary>
    public double Fraction => Next is null ? 1.0 : Math.Clamp((Total - CurrentThreshold) / (double)(NextThreshold - CurrentThreshold), 0.0, 1.0);
}

/// <summary>
/// Limiares do INFINITO, estimados com XP de ondas perfeitas no ritmo do bot otimizado: Júnior ~30 min, Pleno ~1h30, Sênior ~4 h,
/// Especialista ~8 h, Arquiteto ~15 h. Quem vaza demora bem mais ou cai (ver REPORT INFINITO). O XP vem de <see cref="TdReputation"/>.
/// </summary>
public static class TdRank
{
    private static readonly int[] Thresholds = [0, 5_300, 27_600, 79_200, 167_400, 300_000];

    public static int Threshold(TdRankLevel level) => Thresholds[(int)level];

    /// <summary>Nome do sprite do brasão desta patente, incluído nos recursos de apresentação.</summary>
    public static string Brasao(TdRankLevel level) => level switch
    {
        TdRankLevel.Estagiario => "brasao_estagiario.png",
        TdRankLevel.Junior => "brasao_junior.png",
        TdRankLevel.Pleno => "brasao_pleno.png",
        TdRankLevel.Senior => "brasao_senior.png",
        TdRankLevel.Especialista => "brasao_especialista.png",
        TdRankLevel.Arquiteto => "brasao_arquiteto.png",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    public static string DisplayName(TdRankLevel level) => level switch
    {
        TdRankLevel.Estagiario => "Estagiário",
        TdRankLevel.Junior => "Júnior",
        TdRankLevel.Pleno => "Pleno",
        TdRankLevel.Senior => "Sênior",
        TdRankLevel.Especialista => "Especialista",
        TdRankLevel.Arquiteto => "Arquiteto",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
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
        if (cur == TdRankLevel.Arquiteto) return new(cur, null, totalScore, Thresholds[^1], Thresholds[^1]);
        var next = cur + 1;
        return new(cur, next, totalScore, Thresholds[(int)cur], Thresholds[(int)next]);
    }
}
