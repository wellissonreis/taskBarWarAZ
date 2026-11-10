namespace SideWar.Rendering.Animation;

/// <summary>adBlink: 1,1 s, steps(2) por trecho → opacidades 1; 0,725; 0,45; 0,725 a cada 275 ms.</summary>
public static class AdBlink
{
    public const double PeriodSeconds = 1.1;
    public const double StateSeconds = PeriodSeconds / 4;
    private static readonly double[] States = [1.0, 0.725, 0.45, 0.725];

    public static int StateIndex(double t)
    {
        double phase = (t % PeriodSeconds + PeriodSeconds) % PeriodSeconds;
        return Math.Min((int)(phase / StateSeconds), 3);
    }

    public static double Opacity(double t) => States[StateIndex(t)];
}
