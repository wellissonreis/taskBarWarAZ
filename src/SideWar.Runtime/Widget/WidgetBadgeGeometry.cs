using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Widget;

/// <summary>
/// Área clicável do badge "+X" (SDD-23 RF-23.7): pílula de 14 DIP que substitui a linha da taxa, sob os créditos
/// (Silkscreen 14, linha 1,1). Cobre a largura da coluna de créditos (a pílula é menor e fica à esquerda; área generosa).
/// </summary>
public static class WidgetBadgeGeometry
{
    public const float PillHeight = 14f, CreditsLine = 14f * 1.1f;

    public static DipRect Rect(DipRect credits, float heightDip)
    {
        float top = (heightDip - (CreditsLine + PillHeight)) / 2f + CreditsLine;
        return new DipRect(credits.X, top, credits.W, PillHeight);
    }

    public static bool Contains(this DipRect r, float x, float y) => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H;
}
