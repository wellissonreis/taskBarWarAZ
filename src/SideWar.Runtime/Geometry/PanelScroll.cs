namespace SideWar.Runtime.Geometry;

/// <summary>Pure scroll math of the panel body (SDD-24 RF-24.4). All values in DIP.</summary>
public static class PanelScroll
{
    public const float ViewportDip = 491f;
    public const float StepDip = 40f;
    public const float MinThumbDip = 44f;
    public const float TrackInsetDip = 4f; // track y = 59 .. viewport + 51 (483 tall at 491)
    public const float TrackXDip = 386f, TrackWidthDip = 8f;
    public const float HitXDip = 382f, HitWidthDip = 16f;
    public const float ViewportTopDip = 55f;
    public const float TrackTopDip = ViewportTopDip + TrackInsetDip;

    public static float TrackHeight(float viewport) => Math.Max(1f, viewport - 2 * TrackInsetDip);
    public static float MaxOffset(float content, float viewport) => Math.Max(0f, content - viewport);
    public static float Clamp(float offset, float content, float viewport) =>
        Math.Clamp(float.IsFinite(offset) ? offset : 0f, 0f, MaxOffset(content, viewport));

    /// <summary>One detent (120) = 40 DIP; positive wheel delta scrolls up.</summary>
    public static float Wheel(float offset, int wheelDelta, float content, float viewport) =>
        Clamp(offset - wheelDelta / 120f * StepDip, content, viewport);

    /// <summary>Thumb (top relative to the track, height); null when nothing scrolls.</summary>
    public static (float Top, float Height)? Thumb(float content, float viewport, float offset)
    {
        var max = MaxOffset(content, viewport);
        if (max <= 0f) return null;
        var track = TrackHeight(viewport);
        var height = Math.Min(track, Math.Max(MinThumbDip, track * viewport / content));
        var top = (track - height) * Clamp(offset, content, viewport) / max;
        return (top, height);
    }

    /// <summary>Offset for a thumb dragged so its top sits at <paramref name="pointerY"/> − <paramref name="grab"/> (track-relative).</summary>
    public static float OffsetFromDrag(float pointerY, float grab, float content, float viewport)
    {
        var thumb = Thumb(content, viewport, 0f);
        if (thumb is not { } t) return 0f;
        var range = TrackHeight(viewport) - t.Height;
        if (range <= 0f) return 0f;
        return Clamp((pointerY - grab) / range * MaxOffset(content, viewport), content, viewport);
    }

    /// <summary>Track click outside the thumb: one page (viewport − 40) toward the click.</summary>
    public static float TrackClick(float offset, float pointerY, float content, float viewport)
    {
        if (Thumb(content, viewport, offset) is not { } t) return 0f;
        var page = viewport - StepDip;
        return Clamp(pointerY < t.Top ? offset - page : offset + page, content, viewport);
    }
}
