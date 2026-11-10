namespace SideWar.Runtime.Taskbar;

public readonly record struct TowerSlot(int Index, float CenterXDip, float WidthDip, bool IsVirtual);

public static class SlotMap
{
    public static IReadOnlyList<TowerSlot> From(TaskbarLayout layout, float stripWidthDip)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stripWidthDip);
        if (!float.IsFinite(stripWidthDip))
            throw new ArgumentOutOfRangeException(nameof(stripWidthDip));
        if (layout.Source == TaskbarSource.Uia && layout.Edge == TaskbarEdge.Bottom &&
            !layout.AutoHide && layout.AppButtons.Count > 0)
            return Array.AsReadOnly(layout.AppButtons.Take(12).Select((b, i) =>
                new TowerSlot(i, b.Rect.X + b.Rect.W / 2, b.Rect.W, false)).ToArray());
        var left = (420 + (stripWidthDip - 200)) / 2 - 190;
        return Array.AsReadOnly(Enumerable.Range(0, 8).Select(i =>
            new TowerSlot(i, left + 22 + 48 * i, 44, true)).ToArray());
    }

    /// <summary>Null means SEM SLOT. Keep the ordinal and resolve again after layout changes;
    /// ownership, combat, Arsenal and persistence belong to their respective consumers.</summary>
    public static TowerSlot? Resolve(int slotIndex, IReadOnlyList<TowerSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        return slotIndex >= 0 && slotIndex < slots.Count ? slots[slotIndex] : null;
    }
}
