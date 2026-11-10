using SideWar.Runtime.Rendering;
using SkiaSharp;

namespace SideWar.Rendering.Frame;

/// <summary>
/// Wraps an <see cref="OverlayFrame"/> (BGRA8888 premultiplied, ADR-033) in a Skia raster surface drawing straight
/// into the surface memory. The SKSurface is cached per buffer, so steady-state frames allocate nothing.
/// </summary>
public sealed class SkiaFrameCanvas : IDisposable
{
    private SKSurface? surface;
    private nint pixels;
    private int width;
    private int height;
    private int rowBytes;

    /// <summary>Returns the canvas for this frame; pixel content from previous frames is preserved.</summary>
    public SKCanvas Begin(in OverlayFrame frame)
    {
        if (surface is null || frame.Pixels != pixels || frame.Width != width || frame.Height != height || frame.RowBytes != rowBytes)
        {
            surface?.Dispose();
            surface = SKSurface.Create(new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul),
                frame.Pixels, frame.RowBytes)
                ?? throw new InvalidOperationException("SKSurface.Create failed for the overlay frame.");
            pixels = frame.Pixels;
            width = frame.Width;
            height = frame.Height;
            rowBytes = frame.RowBytes;
        }
        return surface.Canvas;
    }

    /// <summary>Flushes Skia into the buffer; call before <see cref="IOverlaySurface.EndFrame"/>.</summary>
    public void End() => surface?.Canvas.Flush();

    /// <summary>Drops the cached surface (call when the buffer is released).</summary>
    public void Invalidate()
    {
        surface?.Dispose();
        surface = null;
        pixels = 0;
    }

    public void Dispose() => Invalidate();
}
