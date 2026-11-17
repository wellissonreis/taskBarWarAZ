using SideWar.Core.Td;
using SideWar.Runtime.Input;

namespace SideWar.Runtime.Td;

/// <summary>
/// Hit-test básico da faixa TD (TD.7; a interação completa é a TD.8): torre posicionada
/// (maior Id vence) e, por fim, o chip de torre SEM POSIÇÃO. Espaço livre não é alvo (o clique passa). Sem alocação.
/// </summary>
public static class TdHitTester
{
    public static HitTarget Test(TdMatchState match, float xDip, float yDip)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (!float.IsFinite(xDip) || !float.IsFinite(yDip) || yDip < 0 || yDip >= TdBand.HeightDip) return HitTarget.None;
        int width = TdBand.WidthDip(match);
        if (xDip < 0 || xDip >= width) return HitTarget.None;

        var towers = match.Towers;
        int best = -1;
        int unplacedIndex = 0, unplacedHit = -1;
        for (int i = 0; i < towers.Count; i++)
        {
            var t = towers[i];
            if (!t.Placed)
            {
                float chip = TdBand.UnplacedChipX(unplacedIndex++);
                if (yDip >= TdBand.UnplacedChipTop && yDip < TdBand.UnplacedChipTop + TdBand.UnplacedChipHeight &&
                    MathF.Abs(xDip - chip) <= TdBand.UnplacedChipWidth / 2f) unplacedHit = t.Id;
                continue;
            }
            float c = TdBand.TowerCenterDip(match, t, width);
            if (t.Id > best && xDip >= c - TdBand.TowerHitHalfWidth && xDip < c + TdBand.TowerHitHalfWidth) best = t.Id;
        }
        if (best >= 0) return HitTarget.TdTower(best);
        return unplacedHit >= 0 ? HitTarget.TdTower(unplacedHit) : HitTarget.None;
    }
}
