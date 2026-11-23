using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>Posiciona a janela de runas junto ao botão, mantendo-a na área útil quando possível.</summary>
public static class TdRunePopoverLayout
{
    public static (PixelRect Bounds, float ArrowX, bool ArrowTop) Place(PixelRect work, PixelRect hud, float buttonCenter, float scale, float width, float height)
    {
        int w = (int)MathF.Round(width * scale), h = (int)MathF.Round(height * scale);
        int target = hud.Left + (int)MathF.Round(buttonCenter * scale);
        int left = Math.Clamp(target - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        bool below = hud.Bottom + 2 + h <= work.Bottom;
        int top = below ? hud.Bottom + 2 : Math.Max(work.Top, hud.Top - h - 2);
        return (new PixelRect(left, top, left + w, top + h), Math.Clamp((target - left) / scale, 14f, width - 14f), below);
    }
}
