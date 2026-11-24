using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>Central modal placement in physical pixels on the band's monitor.</summary>
public static class TdModalLayout
{
    public static float Scale(PixelRect work, float dpiScale) =>
        Math.Min(dpiScale, Math.Min(work.Width * .9f / 720, work.Height * .9f / 480));

    /// <summary>Altura (DIP da modal) da faixa de arrasto: cabeçalho com patente, título e fechar; as abas começam em y=52.</summary>
    public const float DragHeaderDip = 48f;

    /// <summary>Modal centrada na área de trabalho, deslocada por (<paramref name="offsetX"/>, <paramref name="offsetY"/>) pixels e presa dentro dela.</summary>
    public static PixelRect Bounds(PixelRect work, float dpiScale, int offsetX = 0, int offsetY = 0)
    {
        float scale = Scale(work, dpiScale);
        int width = (int)MathF.Round(720 * scale), height = (int)MathF.Round(480 * scale);
        int left = work.Left + (work.Width - width) / 2, top = work.Top + (work.Height - height) / 2;
        (offsetX, offsetY) = ClampOffset(work, width, height, offsetX, offsetY);
        left += offsetX; top += offsetY;
        return new PixelRect(left, top, left + width, top + height);
    }

    /// <summary>Limita o deslocamento em relação ao centro para a modal (<paramref name="width"/>×<paramref name="height"/>) ficar toda dentro da área de trabalho; fora da tela (ex.: monitor menor) volta ao possível.</summary>
    public static (int X, int Y) ClampOffset(PixelRect work, int width, int height, int offsetX, int offsetY)
    {
        int maxX = Math.Max(0, (work.Width - width) / 2), maxY = Math.Max(0, (work.Height - height) / 2);
        return (Math.Clamp(offsetX, -maxX, maxX), Math.Clamp(offsetY, -maxY, maxY));
    }

    /// <summary>Ponto (pixels do cliente da modal) na faixa de arrasto do cabeçalho.</summary>
    public static bool InDragHeader(float xDip, float yDip) => yDip >= 0 && yDip < DragHeaderDip && xDip >= 0 && xDip < 720;
}
