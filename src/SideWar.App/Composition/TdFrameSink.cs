using SideWar.Core.Td;
using SideWar.Platform.Windows.Rendering;
using SideWar.Rendering.Frame;
using SideWar.Rendering.Scene;
using SideWar.Runtime.Td;

namespace SideWar.App.Composition;

/// <summary>
/// Destino dos frames da faixa TD (TD.7): <see cref="TdBandRenderer"/> → DIB → <c>UpdateLayeredWindow</c>, na mesma superfície da cena.
/// Só na T-Game. Sem superfície, com a superfície vazia ou liberada não desenha (o próximo pedido redesenha tudo).
/// </summary>
internal sealed class TdFrameSink(LayeredSurface surface, TdBandRenderer renderer) : ITdFrameSink, IDisposable
{
    private readonly SkiaFrameCanvas canvas = new();

    /// <summary>Frames realmente compostos e apresentados.</summary>
    public long Presents { get; private set; }

    public TdBandRenderer Renderer => renderer;

    public void Present(TdMatchState match, double nowSeconds, in TdView view)
    {
        if (surface.Size.IsEmpty) return;
        var frame = surface.BeginFrame();
        renderer.Draw(canvas.Begin(frame), match, nowSeconds, view);
        canvas.End();
        surface.EndFrame(null);
        Presents++;
    }

    /// <summary>A superfície foi liberada ou redimensionada: o próximo BeginFrame recria o canvas.</summary>
    public void Invalidate() => canvas.Invalidate();

    public void Dispose() => canvas.Dispose();
}
