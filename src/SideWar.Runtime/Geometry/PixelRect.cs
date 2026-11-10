namespace SideWar.Runtime.Geometry;

/// <summary>Physical screen pixels; right and bottom are exclusive.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => checked(Right - Left);
    public int Height => checked(Bottom - Top);
}
