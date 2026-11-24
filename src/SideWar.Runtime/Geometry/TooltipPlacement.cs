namespace SideWar.Runtime.Geometry;

/// <summary>Places a tooltip by an element in screen pixels, flipping at monitor edges.</summary>
public static class TooltipPlacement
{
    public static PixelRect Beside(PixelRect work, PixelRect anchor, int width, int height, int gap = 12)
    {
        int x = anchor.Left + gap, y = anchor.Bottom + gap;
        if (x + width > work.Right) x = anchor.Right - width - gap;
        if (y + height > work.Bottom) y = anchor.Top - height - gap;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new PixelRect(x, y, x + width, y + height);
    }
}
