using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>Cores do canvas novo sem token em SDD-20 (SDD-22 RF-22.5, DP-33.9).</summary>
public static class SceneColors
{
    public static readonly SKColor BarBorder = new(0x0b, 0x07, 0x16);
    public static readonly SKColor CoreBarTrack = new(0x2a, 0x1d, 0x4a);
    public static readonly SKColor CoreBarFill = new(0xc9, 0xa7, 0xff);
}
