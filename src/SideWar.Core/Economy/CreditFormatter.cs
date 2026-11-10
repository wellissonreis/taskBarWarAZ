using System.Globalization;

namespace SideWar.Core.Economy;

/// <summary>Formatação numérica pt-BR compacta (SDD-27 RF-27.2, ADR-029). Cultura fixa, independente do Windows.</summary>
public static class CreditFormatter
{
    public const string Unreachable = "—";
    public const int MaxTier = 680;

    // Potências de 1000 (índice = tier); double só alcança o tier 102.
    private static readonly double[] Powers = BuildPowers();

    private static double[] BuildPowers()
    {
        var list = new List<double> { 1 };
        while (true)
        {
            var next = list[^1] * 1000;
            if (double.IsInfinity(next)) break;
            list.Add(next);
        }
        return [.. list];
    }

    public static string Format(long value)
    {
        if (value >= 0 && value < 1000) return value.ToString(CultureInfo.InvariantCulture);
        if (value == long.MinValue) return "-" + Format((double)long.MaxValue);
        return Format((double)value);
    }

    public static string Format(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return Unreachable;
        if (value < 0) return "-" + Format(-value);
        if (value < 1000)
        {
            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            return rounded >= 1000 ? "1K" : rounded.ToString("0", CultureInfo.InvariantCulture);
        }

        var tier = 1;
        while (tier + 1 < Powers.Length && value >= Powers[tier + 1]) tier++;

        var m = Round3((decimal)(value / Powers[tier]));
        if (m >= 1000)
        {
            tier++;
            m = 1m;
        }
        if (tier > MaxTier) return Unreachable;

        var text = m.ToString(m < 10 ? "0.##" : m < 100 ? "0.#" : "0", CultureInfo.InvariantCulture);
        return text.Replace('.', ',') + Suffix(tier);
    }

    /// <summary>Sufixo do tier: 1=K, 2=M, 3=B, 4=T, 5..680 = aa..zz.</summary>
    public static string Suffix(int tier)
    {
        if (tier is < 1 or > MaxTier) throw new ArgumentOutOfRangeException(nameof(tier));
        if (tier <= 4) return tier switch { 1 => "K", 2 => "M", 3 => "B", _ => "T" };
        var index = tier - 5;
        return string.Create(2, index, static (span, i) =>
        {
            span[0] = (char)('a' + i / 26);
            span[1] = (char)('a' + i % 26);
        });
    }

    private static decimal Round3(decimal m)
    {
        var decimals = m < 10 ? 2 : m < 100 ? 1 : 0;
        return Math.Round(m, decimals, MidpointRounding.AwayFromZero);
    }
}
