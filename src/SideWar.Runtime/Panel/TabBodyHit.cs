namespace SideWar.Runtime.Panel;

/// <summary>Alvos do corpo das abas Missões e Migrar (SDD-24 RF-24.11). Complementa <c>PanelHit.Body</c> sem alterá-lo.</summary>
public enum TabBodyHitKind { None, MissionCollect, RegionRow, Migrate }

/// <summary><see cref="Index"/> = linha da missão ou da região; −1 nos demais.</summary>
public readonly record struct TabBodyHit(TabBodyHitKind Kind, int Index = -1)
{
    public static TabBodyHit None => new(TabBodyHitKind.None);
}
