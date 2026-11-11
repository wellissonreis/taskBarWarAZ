using SideWar.Platform.Windows.Threading;
using SideWar.Runtime.Geometry;

namespace SideWar.Platform.Windows.Windows;

/// <summary>
/// Hotspot window (banner or supply box; SDD-22 RF-22.8, ADR-021): always clickable (never WS_EX_TRANSPARENT), non-activating,
/// owned by the scene. A WM_LBUTTONUP inside the hit area posts <see cref="Clicked"/> to T-Game; behaviour (what the click does,
/// timing) belongs to SDD-26. The content is drawn by the host (Rendering.HotspotRenderer) into <see cref="OverlayChildWindow.Surface"/>.
/// </summary>
public sealed class HotspotWindow : OverlayChildWindow
{
    private HotspotPlacement placement;

    internal HotspotWindow(GameThread owner, SceneWindow scene, HotspotKind kind)
        : base(owner, scene, kind == HotspotKind.Banner ? "SideWar.HotspotBanner" : "SideWar.HotspotSupplyBox", clickThrough: false)
    {
        Kind = kind;
    }

    public HotspotKind Kind { get; }

    /// <summary>Opaque id given by SDD-26 (event/box id) and echoed in <see cref="Clicked"/>.</summary>
    public int Id { get; private set; }

    public HotspotPlacement Placement => placement;

    /// <summary>Raised on T-Game (posted, never inside the window procedure) with (kind, id) for a click in the hit area.</summary>
    public event Action<HotspotKind, int>? Clicked;

    /// <summary>Applies window and control rectangles from <see cref="HotspotGeometry"/>.</summary>
    public void Apply(HotspotPlacement value)
    {
        Apply(value.Window);
        placement = value;
    }

    /// <summary>Shows the hotspot for event/box <paramref name="id"/> (see <see cref="OverlayChildWindow.Show"/>).</summary>
    public void Show(int id)
    {
        CheckAccess();
        Id = id;
        Show();
    }

    private protected override bool UsesHandCursor => true;

    private protected override void OnLeftButtonUp(int x, int y)
    {
        if (!IsVisible || !HotspotGeometry.HitTest(Kind, placement, x, y)) return;
        var kind = Kind;
        var id = Id;
        Owner.Post(() => Clicked?.Invoke(kind, id));
    }
}
