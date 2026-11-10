namespace SideWar.Rendering.Animation;

/// <summary>Anel do pulso: largura do traço em DIP e alfa (0..1) da cor âmbar-anel.</summary>
public readonly record struct PulseRing(double SpreadDip, double Alpha);

/// <summary>adPulse: 1,2 s; (0 DIP, .7) → (6 DIP, 0) em 50 % e volta, com curva t²(3−2t) (SDD-20 nota 3).</summary>
public static class AdPulse
{
    public const double PeriodSeconds = 1.2;
    public const double MaxSpreadDip = 6.0;
    public const double StartAlpha = 0.7;

    /// <summary>Anel fixo sob reducedMotion/Saver (DP-20.4): 2 DIP, alfa .7.</summary>
    public static readonly PulseRing StaticRing = new(2.0, StartAlpha);

    public static PulseRing Ring(double t)
    {
        double phase = (t % PeriodSeconds + PeriodSeconds) % PeriodSeconds / PeriodSeconds;
        double u = phase < 0.5 ? phase * 2 : (1 - phase) * 2;
        double e = u * u * (3 - 2 * u);
        return new(MaxSpreadDip * e, StartAlpha * (1 - e));
    }
}
