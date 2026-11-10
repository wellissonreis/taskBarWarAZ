namespace SideWar.Runtime.Presence;

/// <summary>Limites e multiplicadores de presença (SDD-25 RF-25.3; valores iniciais de presence.json).</summary>
public sealed record PresenceMultiplierConfig(
    double Tier2StartSeconds = 15 * 60,
    double Tier3StartSeconds = 60 * 60,
    double Tier1 = 1.5,
    double Tier2 = 2.0,
    double Tier3 = 2.5,
    double NotPresent = 0.25)
{
    public static PresenceMultiplierConfig Default { get; } = new();
}

/// <summary>Multiplicador de créditos e XP por presença (SDD-25 RF-25.3). Puro.</summary>
public static class PresenceMultiplier
{
    public static double Evaluate(PresenceState state, double presentSeconds, PresenceMultiplierConfig? config = null)
    {
        config ??= PresenceMultiplierConfig.Default;
        if (state != PresenceState.Presente) return config.NotPresent;
        if (presentSeconds >= config.Tier3StartSeconds) return config.Tier3;
        if (presentSeconds >= config.Tier2StartSeconds) return config.Tier2;
        return config.Tier1;
    }
}
