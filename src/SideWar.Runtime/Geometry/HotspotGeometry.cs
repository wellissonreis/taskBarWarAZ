namespace SideWar.Runtime.Geometry;

/// <summary>Tipo de janela hotspot (SDD-22 RF-22.8).</summary>
public enum HotspotKind
{
    /// <summary>Banner de pico ou de chefe (36 DIP de controle + 6 DIP de margem para o anel).</summary>
    Banner,

    /// <summary>Caixa de suprimentos (52 × 44 DIP, base em y = 96).</summary>
    SupplyBox,
}

/// <summary>
/// Janela (em pixels de tela) e retângulo do controle (em pixels do cliente da janela, origem no canto superior
/// esquerdo da janela) de um hotspot ou do selo.
/// </summary>
public readonly record struct HotspotPlacement(PixelRect Window, PixelRect Control);

/// <summary>
/// Geometria pura das janelas hotspot e do selo de modo ausente (SDD-22 RF-22.7 e RF-22.8; CA-22.5). Sem Win32 e sem
/// Skia: recebe a cena em pixels (<see cref="SceneLayout"/>) e o DPI inteiro; conversões <c>round(dip × dpi / 96)</c>.
/// </summary>
public static class HotspotGeometry
{
    public const float BannerControlHeightDip = 36f;
    public const float BannerRingMarginDip = 6f;
    public const float SupplyBoxWidthDip = 52f;
    public const float SupplyBoxHeightDip = 44f;
    public const float SupplySpriteWidthDip = 45f;
    public const float SupplySpriteHeightDip = 24f;
    public const float SealTopDip = 4f;

    public static int Px(double dip, uint dpi) => checked((int)Math.Round(dip * dpi / 96.0, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Banner centrado no topo da faixa. <paramref name="controlWidthPx"/> é a largura do conteúdo já medida em pixels.
    /// A janela é o controle com margem de 6 DIP em todos os lados e se estende acima da faixa.
    /// </summary>
    public static HotspotPlacement Banner(PixelRect scene, uint dpi, int controlWidthPx)
    {
        CheckDpi(dpi);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(controlWidthPx);
        int m = Px(BannerRingMarginDip, dpi);
        int h = Px(BannerControlHeightDip, dpi);
        int w = checked(controlWidthPx + 2 * m);
        int wh = checked(h + 2 * m);
        int left = scene.Left + (scene.Width - w) / 2;
        int top = checked(scene.Top - m);
        return new(new PixelRect(left, top, left + w, top + wh), new PixelRect(m, m, m + controlWidthPx, m + h));
    }

    /// <summary>
    /// Caixa de suprimentos: 52 × 44 DIP com a base na base da faixa. <paramref name="leftDip"/> é o X do controle
    /// relativo à esquerda da faixa (SDD-26); é mantido dentro da faixa.
    /// </summary>
    public static HotspotPlacement SupplyBox(PixelRect scene, uint dpi, float leftDip)
    {
        CheckDpi(dpi);
        if (!float.IsFinite(leftDip)) throw new ArgumentOutOfRangeException(nameof(leftDip));
        int w = Px(SupplyBoxWidthDip, dpi);
        int h = Px(SupplyBoxHeightDip, dpi);
        int max = Math.Max(scene.Left, scene.Right - w);
        int left = Math.Clamp(scene.Left + Px(leftDip, dpi), scene.Left, max);
        int top = scene.Bottom - h;
        return new(new PixelRect(left, top, left + w, top + h), new PixelRect(0, 0, w, h));
    }

    /// <summary>Posição do sprite (45 × 24 DIP) dentro da caixa: centrado na horizontal, base alinhada à do controle.</summary>
    public static PixelRect SupplySprite(PixelRect control, int spriteWidthPx, int spriteHeightPx)
    {
        int left = control.Left + (control.Width - spriteWidthPx) / 2;
        int top = control.Bottom - spriteHeightPx;
        return new(left, top, left + spriteWidthPx, top + spriteHeightPx);
    }

    /// <summary>Selo de modo ausente: centrado na faixa, 4 DIP abaixo do topo. Tamanho já medido em pixels.</summary>
    public static PixelRect Seal(PixelRect scene, uint dpi, int widthPx, int heightPx)
    {
        CheckDpi(dpi);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        int left = scene.Left + (scene.Width - widthPx) / 2;
        int top = scene.Top + Px(SealTopDip, dpi);
        return new(left, top, left + widthPx, top + heightPx);
    }

    /// <summary>
    /// Alvo de clique em coordenadas do cliente da janela. Banner: controle e anel (a janela inteira); caixa: o controle.
    /// Pixels de alfa 0 atravessam o clique para baixo por conta da própria janela layered.
    /// </summary>
    public static bool HitTest(HotspotKind kind, in HotspotPlacement placement, int x, int y)
    {
        var r = kind == HotspotKind.Banner
            ? new PixelRect(0, 0, placement.Window.Width, placement.Window.Height)
            : placement.Control;
        return x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
    }

    private static void CheckDpi(uint dpi)
    {
        if (dpi == 0 || dpi > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(dpi), "DPI must be positive.");
    }
}
