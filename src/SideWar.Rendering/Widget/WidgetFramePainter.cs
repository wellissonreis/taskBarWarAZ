using SideWar.Rendering.Frame;
using SideWar.Runtime.Rendering;
using SideWar.Runtime.Widget;

namespace SideWar.Rendering.Widget;

/// <summary>Adaptador <see cref="IWidgetPainter"/>: <see cref="WidgetRenderer"/> sobre o buffer do <see cref="OverlayFrame"/>.</summary>
public sealed class WidgetFramePainter : IWidgetPainter, IDisposable
{
    private readonly WidgetRenderer _renderer;
    private readonly SkiaFrameCanvas _canvas = new();

    public WidgetFramePainter(WidgetRenderer renderer) => _renderer = renderer;

    public WidgetLayout Paint(in OverlayFrame frame, in WidgetModel model, float heightDip, int dpi)
    {
        _renderer.Render(_canvas.Begin(frame), model, heightDip, dpi);
        _canvas.End();
        return _renderer.GetLayout(model, heightDip, dpi);
    }

    public void Invalidate() => _canvas.Invalidate();

    public void Dispose() => _canvas.Dispose();
}
