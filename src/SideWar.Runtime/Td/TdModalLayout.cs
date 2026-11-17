using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>Central modal placement in physical pixels on the band's monitor.</summary>
public static class TdModalLayout
{
    public static float Scale(PixelRect work, float dpiScale) =>
        Math.Min(dpiScale, Math.Min(work.Width * .9f / 720, work.Height * .9f / 480));

    public static PixelRect Bounds(PixelRect work, float dpiScale)
    {
        float scale = Scale(work, dpiScale);
        int width = (int)MathF.Round(720 * scale), height = (int)MathF.Round(480 * scale);
        int left = work.Left + (work.Width - width) / 2, top = work.Top + (work.Height - height) / 2;
        return new PixelRect(left, top, left + width, top + height);
    }
}
