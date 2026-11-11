using SideWar.Runtime.Power;

namespace SideWar.Platform.Windows.Session;

/// <summary>Pure resolution of <see cref="PowerProfile"/> from raw power facts (SDD-07 RF-07.5).</summary>
public static class PowerProfileResolver
{
    public static PowerProfile Resolve(bool powerSaving, uint energySaver, bool onBattery, bool saverOnBattery, bool forceSaver)
        => powerSaving || energySaver >= 1 || (onBattery && saverOnBattery) || forceSaver
            ? PowerProfile.Saver
            : PowerProfile.Normal;
}
