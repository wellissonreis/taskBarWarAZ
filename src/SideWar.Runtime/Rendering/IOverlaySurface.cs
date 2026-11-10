using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Rendering;

/// <summary>Port implemented by Platform.Windows (SDD-01 RF-01.6, SDD-08 RF-08.1, ADR-033). T-Game only.</summary>
public interface IOverlaySurface
{
    PixelSize Size { get; }
    float Scale { get; }
    OverlayFrame BeginFrame();
    /// <summary>Presents the frame; <paramref name="dirty"/> limits the update rectangle (RF-08.7).</summary>
    void EndFrame(PixelRect? dirty);
    /// <summary>Changes SourceConstantAlpha and re-presents without redrawing.</summary>
    void SetOpacity(byte constantAlpha);
}
