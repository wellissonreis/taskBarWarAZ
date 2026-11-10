namespace SideWar.Runtime.Input;

/// <summary>Click target of the strip (SDD-22 RF-22.3; replaces SDD-04 RF-04.6).</summary>
public readonly record struct HitTarget(HitKind Kind, int SlotIndex)
{
    public static HitTarget None => new(HitKind.None, -1);
}

public enum HitKind { None, SceneTower, SceneEmptySlot }

/// <summary>A clickable rectangle of the strip, in DIP relative to the strip's top-left.</summary>
public readonly record struct HitRegion(HitKind Kind, int SlotIndex, float X, float Y, float Width, float Height)
{
    public bool Contains(float x, float y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>
/// Pure hit test of the strip by regions (towers and empty slots only). Towers win over empty slots;
/// ties go to the lower slot index. Anything else is <see cref="HitTarget.None"/>.
/// </summary>
public static class SceneHitTester
{
    public static HitTarget Test(IReadOnlyList<HitRegion> regions, float xDip, float yDip)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (!float.IsFinite(xDip) || !float.IsFinite(yDip)) return HitTarget.None;
        HitRegion? best = null;
        foreach (var region in regions)
        {
            if (region.Kind == HitKind.None || !region.Contains(xDip, yDip)) continue;
            if (best is not { } current || Better(region, current)) best = region;
        }
        return best is { } hit ? new HitTarget(hit.Kind, hit.SlotIndex) : HitTarget.None;
    }

    /// <summary>Physical client pixels to DIP (scale = dpi / 96).</summary>
    public static (float X, float Y) ToDip(int xPx, int yPx, float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        return (xPx / scale, yPx / scale);
    }

    private static bool Better(HitRegion candidate, HitRegion current) =>
        candidate.Kind != current.Kind
            ? candidate.Kind == HitKind.SceneTower
            : candidate.SlotIndex < current.SlotIndex;
}
