namespace SideWar.Runtime.Geometry;

public enum PanelHit { None, Background, Close, ScrollThumb, ScrollTrack, Body }

/// <summary>Chrome-level hit-test; body content hit-testing arrives with T5.4/T5.5 (RF-24.11).</summary>
public static class PanelChromeHit
{
    public const float CloseX = 346f, CloseY = 6f, CloseSize = 44f;
    private const float BodyX = 2f, BodyW = 396f;

    /// <summary>Hit at (<paramref name="x"/>, <paramref name="y"/>) in DIP relative to the visible box's top-left.</summary>
    public static PanelHit Test(float x, float y, float boxHeightDip, float content, float viewport, float offset)
    {
        var boxW = PanelGeometry.PanelWidthDip;
        if (x < 0 || y < 0 || x >= boxW || y >= boxHeightDip) return PanelHit.Background; // shadow margin: consumed
        if (x >= CloseX && x < CloseX + CloseSize && y >= CloseY && y < CloseY + CloseSize) return PanelHit.Close;
        var bodyTop = PanelScroll.ViewportTopDip;
        if (y >= bodyTop && y < bodyTop + viewport)
        {
            if (PanelScroll.Thumb(content, viewport, offset) is { } t && x >= PanelScroll.HitXDip && x < PanelScroll.HitXDip + PanelScroll.HitWidthDip)
            {
                var ty = y - PanelScroll.TrackTopDip;
                if (ty >= 0 && ty < PanelScroll.TrackHeight(viewport))
                    return ty >= t.Top && ty < t.Top + t.Height ? PanelHit.ScrollThumb : PanelHit.ScrollTrack;
            }
            if (x >= BodyX && x < BodyX + BodyW) return PanelHit.Body;
        }
        return PanelHit.Background;
    }
}

/// <summary>
/// Mutable, T-Game-only view state of the panel: scroll offset, hover and a Version that rises only when
/// something shown changes (RF-24.12, RF-24.13). Sets are no-ops when the value is unchanged.
/// </summary>
public sealed class PanelViewState
{
    public long Version { get; private set; }
    public float Offset { get; private set; }
    public float Content { get; private set; }
    public float Viewport { get; private set; } = PanelScroll.ViewportDip;
    public PanelHit Hover { get; private set; }
    public bool Dragging { get; private set; }
    private float grab;

    public void SetContent(float content, float viewport)
    {
        if (content == Content && viewport == Viewport) return;
        Content = content; Viewport = viewport;
        SetOffset(Offset);
        Version++;
    }

    public void ResetOffset() => SetOffset(0f);

    public bool SetOffset(float offset)
    {
        var o = PanelScroll.Clamp(offset, Content, Viewport);
        if (o == Offset) return false;
        Offset = o; Version++;
        return true;
    }

    public bool Wheel(int delta) => SetOffset(PanelScroll.Wheel(Offset, delta, Content, Viewport));

    public bool SetHover(PanelHit hit)
    {
        if (hit == Hover) return false;
        Hover = hit; Version++;
        return true;
    }

    /// <summary><paramref name="trackY"/> = y relative to the track top, in DIP.</summary>
    public void BeginDrag(float trackY)
    {
        if (PanelScroll.Thumb(Content, Viewport, Offset) is not { } t) return;
        grab = trackY - t.Top;
        Dragging = true;
        Version++;
    }

    public bool Drag(float trackY) => Dragging && SetOffset(PanelScroll.OffsetFromDrag(trackY, grab, Content, Viewport));

    public void EndDrag()
    {
        if (!Dragging) return;
        Dragging = false; Version++;
    }

    public bool TrackClick(float trackY) => SetOffset(PanelScroll.TrackClick(Offset, trackY, Content, Viewport));
}
