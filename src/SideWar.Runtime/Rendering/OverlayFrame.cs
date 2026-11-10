namespace SideWar.Runtime.Rendering;

/// <summary>
/// Raw top-down pixel buffer of an overlay surface (ADR-033). Format is always BGRA8888 with
/// premultiplied alpha. Valid only between <see cref="IOverlaySurface.BeginFrame"/> and
/// <see cref="IOverlaySurface.EndFrame"/>; previous content is preserved across frames.
/// </summary>
public readonly struct OverlayFrame(nint pixels, int width, int height, int rowBytes)
{
    public nint Pixels { get; } = pixels;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int RowBytes { get; } = rowBytes;
}
