using SideWar.Platform.Windows.Threading;

namespace SideWar.Platform.Windows.Windows;

/// <summary>
/// Absent-mode seal (SDD-22 RF-22.7, ADR-028, DP-22.12): own layered window at SourceConstantAlpha 255, click-through
/// (WS_EX_TRANSPARENT and HTTRANSPARENT, never clickable), owned by the scene. Drawn by Rendering.HotspotRenderer.DrawSeal.
/// </summary>
public sealed class SceneSealWindow : OverlayChildWindow
{
    internal SceneSealWindow(GameThread owner, SceneWindow scene)
        : base(owner, scene, "SideWar.SceneSeal", clickThrough: true)
    {
    }

    private protected override bool HitTransparent => true;
}
