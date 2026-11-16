using SideWar.Core.Td;

namespace SideWar.Runtime.Td;

/// <summary>
/// Geometria da faixa TD (TD.7, fallback Band mínima de RF-28.18/DP-28.11 após TA.1 reprovar torres na barra): 32 DIP de altura logo
/// acima da barra, trilho dos inimigos no topo e torres embaixo. Origem no canto superior esquerdo da faixa; unidades em DIP.
/// O mundo lógico (1.200 unidades) é mapeado em toda a largura (<c>W_dip / 1200</c>). Puro, sem alocação.
/// </summary>
public static class TdBand
{
    /// <summary>Altura da janela de cena em modo TD.</summary>
    public const int HeightDip = 32;

    /// <summary>Largura à direita da faixa reservada ao HUD compacto (TD.8): a cena TD é a largura da barra menos esta área.</summary>
    public const int HudAreaDip = 160;

    public const float RailTop = 2f, RailHeight = 10f;
    public const float TowerTop = 14f, TowerWidth = 20f, TowerHeight = 18f;

    /// <summary>Faixa livre às pontas (núcleo à esquerda, portal à direita): zona de torres = [EdgeClearDip, W − EdgeClearDip).</summary>
    public const int EdgeClearDip = 32;

    public const float CrateSize = 16f, CrateTop = 3f;
    public const float TowerHitHalfWidth = 20f, CrateHitHalfWidth = 12f;
    public const float UnplacedChipLeft = 30f, UnplacedChipWidth = 14f, UnplacedChipGap = 2f, UnplacedChipTop = 16f, UnplacedChipHeight = 14f;

    /// <summary>Unidade lógica → DIP (largura da faixa em DIP).</summary>
    public static float ToDip(double logical, int widthDip) => (float)(logical * widthDip / TdWorld.PathLength);

    public static float TowerCenterDip(TdMatchState match, in TdTower tower, int widthDip) =>
        match.Layout is null ? ToDip(tower.X, widthDip) : match.TowerDip(tower);

    public static int WidthDip(TdMatchState match) => match.Layout?.Width ?? (int)TdWorld.PathLength;

    /// <summary>Centro X (DIP) do chip da torre SEM POSIÇÃO de ordem <paramref name="index"/> (0 = primeira sem posição).</summary>
    public static float UnplacedChipX(int index) => UnplacedChipLeft + index * (UnplacedChipWidth + UnplacedChipGap) + UnplacedChipWidth / 2f;
}
