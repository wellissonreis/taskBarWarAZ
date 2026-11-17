using SideWar.Core.Td;

namespace SideWar.Runtime.Td;

/// <summary>
/// Geometria da faixa TD (TD.7, fallback Band mínima de RF-28.18/DP-28.11 após TA.1 reprovar torres na barra): 32 DIP de altura logo
/// acima da barra, trilho dos inimigos no topo e torres embaixo. Origem no canto superior esquerdo da faixa; unidades em DIP.
/// O mundo lógico (1.200 unidades) é mapeado em toda a largura (<c>W_dip / 1200</c>). Puro, sem alocação.
/// </summary>
public static class TdBand
{
    /// <summary>
    /// Altura do ESPAÇO DE DESENHO da faixa (TD.14): toda a geometria abaixo (trilho, torres, chips, zonas, Core) é projetada para
    /// 32 DIP. A altura real da janela é <c>bandHeightDip</c> (32 a 96, padrão 64) e tudo escala por <see cref="Scale"/> (k = altura / 32):
    /// o Core enxerga a faixa com largura <c>W_dip / k</c> e as bordas (render, hit-test, janelas) convertem.
    /// </summary>
    public const int HeightDip = 32;

    /// <summary>
    /// TD.28c: folga extra (DIP do espaço de desenho) ACIMA do espaço de 32 para o portal e o núcleo (~42 de altura) e a barra de
    /// integridade passarem do topo da faixa. Escolha mais simples: a janela da cena fica (32 + 14)/32 mais alta em toda a largura, mas
    /// com alfa 0 na folga (o Windows deixa o clique passar); o desenho e o hit-test deslocam y por esta folga.
    /// </summary>
    public const float EdgeHeadroomDip = 14f;

    /// <summary>Altura real (DIP) da janela da cena = faixa + folga do portal/núcleo, escalada com a faixa.</summary>
    public static float SceneHeightDip(int bandHeightDip) => Math.Clamp(bandHeightDip, MinHeightDip, MaxHeightDip) * (HeightDip + EdgeHeadroomDip) / HeightDip;

    /// <summary>Folga (DIP reais) acima da faixa na janela da cena.</summary>
    public static float HeadroomRealDip(int bandHeightDip) => Math.Clamp(bandHeightDip, MinHeightDip, MaxHeightDip) * EdgeHeadroomDip / HeightDip;

    public const int MinHeightDip = 32, MaxHeightDip = 96, DefaultHeightDip = 64;

    /// <summary>Fator de escala do desenho da faixa: <c>altura real / 32</c> (limitada a 32..96).</summary>
    public static float Scale(int bandHeightDip) => Math.Clamp(bandHeightDip, MinHeightDip, MaxHeightDip) / (float)HeightDip;

    /// <summary>HUD é projetado para uma faixa de 64 DIP; abaixo disso encolhe até 0,75 (legibilidade), acima cresce até 1,5.</summary>
    public const float HudBaseWidthDip = 460f, HudBaseHeightDip = 64f;

    public static float HudScale(int bandHeightDip) => Math.Clamp(Math.Clamp(bandHeightDip, MinHeightDip, MaxHeightDip) / 64f, 0.75f, 1.5f);

    /// <summary>Largura real (DIP) da área à direita da faixa reservada ao HUD (TD.8/TD.14).</summary>
    public static int HudAreaDip(int bandHeightDip) => (int)MathF.Round(HudBaseWidthDip * HudScale(bandHeightDip));

    /// <summary>Altura real (DIP) da janela do HUD.</summary>
    public static int HudHeightDip(int bandHeightDip) => (int)MathF.Round(HudBaseHeightDip * HudScale(bandHeightDip));

    public const float RailTop = 2f, RailHeight = 10f;
    public const float TowerTop = 14f, TowerWidth = 20f, TowerHeight = 18f;

    /// <summary>Faixa livre às pontas (núcleo à esquerda, portal à direita): zona de torres = [EdgeClearDip, W − EdgeClearDip).</summary>
    public const int EdgeClearDip = 32;

    public const float TowerHitHalfWidth = 20f;
    public const float UnplacedChipLeft = 30f, UnplacedChipWidth = 14f, UnplacedChipGap = 2f, UnplacedChipTop = 16f, UnplacedChipHeight = 14f;

    /// <summary>Unidade lógica → DIP (largura da faixa em DIP).</summary>
    public static float ToDip(double logical, int widthDip) => (float)(logical * widthDip / TdWorld.PathLength);

    public static float TowerCenterDip(TdMatchState match, in TdTower tower, int widthDip) =>
        match.Layout is null ? ToDip(tower.X, widthDip) : match.TowerDip(tower);

    public static int WidthDip(TdMatchState match) => match.Layout?.Width ?? (int)TdWorld.PathLength;

    /// <summary>Centro X (DIP) do chip da torre SEM POSIÇÃO de ordem <paramref name="index"/> (0 = primeira sem posição).</summary>
    public static float UnplacedChipX(int index) => UnplacedChipLeft + index * (UnplacedChipWidth + UnplacedChipGap) + UnplacedChipWidth / 2f;
}
