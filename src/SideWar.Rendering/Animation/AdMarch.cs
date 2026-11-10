namespace SideWar.Rendering.Animation;

/// <summary>adMarch: translateX 0 → −1180 DIP em 26 s, linear, infinita; delay = −i × 26/n.</summary>
public static class AdMarch
{
    public const double PeriodSeconds = 26.0;
    public const double TravelDip = 1180.0;

    public static double OffsetDip(double t, int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        double phase = (t + index * PeriodSeconds / count) / PeriodSeconds;
        phase -= Math.Floor(phase);
        return -TravelDip * phase;
    }
}
