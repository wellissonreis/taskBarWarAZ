namespace SideWar.Rendering.Animation;

/// <summary>adBob: 0,5 s, steps(2) por trecho → 0, −1, −2, −1 DIP a cada 125 ms; global (sem fase por inimigo).</summary>
public static class AdBob
{
    public const double PeriodSeconds = 0.5;
    public const double StateSeconds = PeriodSeconds / 4;
    private static readonly int[] StatesDip = [0, -1, -2, -1];

    public static int StateIndex(double t)
    {
        double phase = (t % PeriodSeconds + PeriodSeconds) % PeriodSeconds;
        return Math.Min((int)(phase / StateSeconds), 3);
    }

    public static int OffsetDip(double t) => StatesDip[StateIndex(t)];

    /// <summary>Deslocamento em px físicos: round(offset × scale).</summary>
    public static double OffsetPx(double t, double scale) =>
        Math.Round(OffsetDip(t) * scale, MidpointRounding.AwayFromZero);
}
