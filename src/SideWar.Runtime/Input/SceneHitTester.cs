using SideWar.Core.Simulation;

namespace SideWar.Runtime.Input;

public enum HitKind : byte { None, SceneTower, BuildOverlay }

/// <summary>Click target of the strip (SDD-22 RF-22.3, ADR-034): <c>SceneTower(towerId) | BuildOverlay | None</c>.</summary>
public readonly record struct HitTarget(HitKind Kind, int TowerId)
{
    public static HitTarget None => new(HitKind.None, -1);
    public static HitTarget Overlay => new(HitKind.BuildOverlay, -1);
    public static HitTarget Tower(int towerId) => new(HitKind.SceneTower, towerId);
}

/// <summary>
/// Pure hit test of the scene window (SDD-22 RF-22.3). Tower cell: 44 × 44 <c>[x − 22, x + 22) × [barTop + round((H − 44)/2), +44)</c>
/// (host Taskbar) or <c>× [2, 46)</c> (host Band), placed towers only, highest Id first. Free space is not a target
/// (the click passes through). In build mode the overlay covers the whole window and wins over towers. No allocation.
/// </summary>
public static class SceneHitTester
{
    public const float BarTop = 104f;
    public const float Cell = PlacementValidator.BoxWidth;
    public const float BandCellTop = 2f;
    public const float DefaultBarHeight = 48f;

    public static HitTarget Test(SimState state, float barHeightDip, bool buildOverlay, float xDip, float yDip)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Test(state.Layout, state.Towers, barHeightDip, buildOverlay, xDip, yDip);
    }

    public static HitTarget Test(StripLayout layout, TowerStore towers, float barHeightDip, bool buildOverlay, float xDip, float yDip)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(towers);
        if (!float.IsFinite(xDip) || !float.IsFinite(yDip) || !float.IsFinite(barHeightDip)) return HitTarget.None;
        if (buildOverlay)
            return xDip >= 0 && xDip < layout.Width && yDip >= 0 && yDip < BarTop + barHeightDip ? HitTarget.Overlay : HitTarget.None;

        float top = layout.Host == TowerHost.Band ? BandCellTop : BarTop + MathF.Floor((barHeightDip - Cell) / 2 + 0.5f);
        if (yDip < top || yDip >= top + Cell) return HitTarget.None;
        int bestId = -1;
        for (int i = 0; i < towers.Capacity; i++)
        {
            if (!towers.IsAlive(i)) continue;
            ref readonly var t = ref towers.At(i);
            if (!t.Placed || t.Id <= bestId) continue;
            float cx = PlacementValidator.CenterX(t.Anchor, t.OffsetDip, layout.Width);
            if (xDip >= cx - Cell / 2 && xDip < cx + Cell / 2) bestId = t.Id;
        }
        return bestId >= 0 ? HitTarget.Tower(bestId) : HitTarget.None;
    }

    /// <summary>Physical client pixels to DIP (scale = dpi / 96).</summary>
    public static (float X, float Y) ToDip(int xPx, int yPx, float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        return (xPx / scale, yPx / scale);
    }
}
