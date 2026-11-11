using SideWar.Runtime.Rendering;

namespace SideWar.Runtime.Widget;

/// <summary>
/// Porta de desenho do widget (SDD-23 RF-23.9): <c>Platform.Windows</c> não referencia <c>Rendering</c>
/// (SDD-01), então o controlador desenha por esta porta; <c>Rendering</c> a implementa sobre o Skia.
/// Chamada só na T-Game.
/// </summary>
public interface IWidgetPainter
{
    /// <summary>Desenha o frame completo do modelo no buffer e devolve o layout desenhado (DIP).</summary>
    WidgetLayout Paint(in OverlayFrame frame, in WidgetModel model, float heightDip, int dpi);

    /// <summary>Descarta o canvas em cache (o buffer foi liberado ou recriado).</summary>
    void Invalidate();
}
