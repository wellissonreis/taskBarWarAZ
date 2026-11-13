namespace SideWar.Core.Simulation;

/// <summary>Motivo de posição inválida (SDD-11 RF-11.9), na ordem de validação.</summary>
public enum PlacementReason : byte { None = 0, OutOfZone, Overlap }

/// <summary>
/// <c>PlacementValidator</c> 1D (SDD-11 RF-11.9, ADR-034): puro e sem alocação; usado pelos comandos e pelo overlay do modo construção.
/// Caixa de colisão <c>[x − 22, x + 22)</c> (44 DIP); encostar não é sobreposição. Só torres <c>Placed</c> contam.
/// </summary>
public static class PlacementValidator
{
    public const int BoxWidth = 44;
    public const int HalfBox = BoxWidth / 2;

    /// <summary>Centro X da torre pela âncora (SDD-21 RF-21.8).</summary>
    public static int CenterX(TowerAnchor anchor, int offsetDip, int width)
        => anchor == TowerAnchor.Start ? offsetDip : width - offsetDip;

    public static PlacementReason Validate(int x, StripLayout layout, TowerStore towers, int? ignoreId = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(towers);
        if (!FitsInZone(x, layout)) return PlacementReason.OutOfZone;
        for (int i = 0; i < towers.Capacity; i++)
        {
            if (!towers.IsAlive(i)) continue;
            ref readonly var t = ref towers.At(i);
            if (!t.Placed || (ignoreId is { } ig && t.Id == ig)) continue;
            int ox = CenterX(t.Anchor, t.OffsetDip, layout.Width);
            if (Math.Abs((long)x - ox) < BoxWidth) return PlacementReason.Overlap;
        }
        return PlacementReason.None;
    }

    public static bool FitsInZone(int x, StripLayout layout)
    {
        foreach (var z in layout.FreeZones)
            if (z.X0 <= x - HalfBox && x + HalfBox <= z.X1) return true;
        return false;
    }
}
