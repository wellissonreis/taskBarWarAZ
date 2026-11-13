using System.Globalization;

namespace SideWar.Runtime.Localization;

/// <summary>Textos das abas Missões/Migrar, banners e caixa (SDD-24 RF-24.7/24.8, SDD-26; RF-15.11). Parcial de <see cref="Strings"/>.</summary>
public static partial class Strings
{
    public const string MissionCollected = "COLETADA";
    public const string RewardRareChest = "+1 baú raro";
    public const string PresenceAbsent = "Você está ausente. Interaja para recuperar o bônus de presença.";
    public const string BannerPeak = "PICO DE DEMANDA · ESCALAR ×3";
    public const string BannerBoss = "CHEFE · RESPONDER PERGUNTA AZURE";
    public const string BannerScaled = "DEFESA ESCALADA ×3";

    public static string PresenceText(int minutes) =>
        $"Você está presente há {minutes} min. Interaja ao menos uma vez a cada 5 min para manter o bônus.";

    /// <summary>Recompensa de missão: <c>chestRare</c> → baú raro; <c>incomeMinutes</c> → min de renda (D-18); reserves; xp.</summary>
    public static string MissionReward(string kind, long amount) => kind switch
    {
        "chestRare" => RewardRareChest,
        "incomeMinutes" => $"+{amount} min de renda",
        "reserves" => $"+{amount} Reservas",
        "xp" => $"+{amount} XP",
        _ => string.Empty,
    };

    /// <summary>Progresso: <c>3/3</c>; a missão de presença exibe minutos (<c>42/60 min</c>).</summary>
    public static string MissionProgress(long progress, long target, bool minutes) =>
        minutes ? $"{progress}/{target} min" : $"{progress}/{target}";

    public static string RegionCurrent(string name) => $"{name} · atual";

    public static string RegionOpen(string name, double hpMultiplier, double reservesMultiplier) =>
        $"{name} · inimigos {Percent(hpMultiplier)} vida, Reservas ×{Dec(reservesMultiplier)}";

    public static string RegionLocked(string name, string unlock, long? reserves) =>
        unlock == "reserves" ? $"{name} · desbloqueia com {reserves ?? 0} Reservas" : $"{name} · desbloqueia na 1ª migração";

    private static string Percent(double mult)
    {
        int p = (int)Math.Round((mult - 1.0) * 100.0, MidpointRounding.AwayFromZero);
        return (p >= 0 ? "+" : "") + p.ToString(CultureInfo.InvariantCulture) + "%";
    }

    private static string Dec(double v) => v.ToString("0.0#", CultureInfo.InvariantCulture).Replace('.', ',');
}
