namespace SideWar.Runtime.Td;

/// <summary>Estado de um nó da teia de runas (TD.30c): bloqueado, disponível (vizinho de um despertado) ou despertado.</summary>
public enum TdRuneState : byte { Locked, Available, Awake }

/// <summary>
/// Balão do nó sob o mouse (TD.30c, revisto na TD.30e), na modal e na janela de runas do HUD: nome do ramo, etiqueta, efeito e ação. O tipo é
/// SEMPRE visível (<see cref="TypeIndex"/> = índice em <c>Runes.Types</c>) em qualquer estado: o jogador precisa ver a árvore para montar estratégia.
/// A posição do nó (<see cref="NodeX"/>/<see cref="NodeY"/>, DIP do popover) ancora o balão.
/// </summary>
public sealed record TdRuneTip(string NodeId, TdRuneState State, int TypeIndex, string Name, string Tag, string Text, string Action, TdTone ActionTone,
    float NodeX, float NodeY, float NodeSize);

/// <summary>Posição do balão dentro dos limites da janela (flip vertical e horizontal). Puro e sem alocação.</summary>
public static class TdRuneTipLayout
{
    /// <summary>Diâmetro do nó acima do qual o balão se afasta, mais a folga até a borda do nó.</summary>
    public const float Gap = 6f;

    /// <summary>
    /// Devolve o canto superior esquerdo do balão <paramref name="w"/>×<paramref name="h"/> junto ao nó (centro, diâmetro) sempre dentro de
    /// <c>[margin, bounds - margin]</c>. Prefere abaixo se o nó está na metade de cima e acima se está na de baixo; vira para o outro lado
    /// quando não cabe; sem espaço vertical em nenhum dos lados, vai para a direita ou esquerda do nó.
    /// </summary>
    public static (float X, float Y, TdTipSide Side) Place(float nodeX, float nodeY, float nodeSize, float w, float h, float boundsW, float boundsH, float margin)
    {
        float half = nodeSize / 2f + Gap;
        float minX = margin, maxX = Math.Max(margin, boundsW - w - margin);
        float minY = margin, maxY = Math.Max(margin, boundsH - h - margin);
        float x = Math.Clamp(nodeX - w / 2f, minX, maxX);
        float below = nodeY + half, above = nodeY - half - h;
        bool belowFits = below >= minY && below <= maxY, aboveFits = above >= minY && above <= maxY;
        bool preferBelow = nodeY <= boundsH / 2f;
        if (preferBelow ? belowFits : !aboveFits && belowFits) return (x, below, TdTipSide.Below);
        if (aboveFits) return (x, above, TdTipSide.Above);
        if (belowFits) return (x, below, TdTipSide.Below);
        float right = nodeX + half, left = nodeX - half - w;
        float y = Math.Clamp(nodeY - h / 2f, minY, maxY);
        if (right >= minX && right <= maxX) return (right, y, TdTipSide.Right);
        if (left >= minX && left <= maxX) return (left, y, TdTipSide.Left);
        return (x, Math.Clamp(preferBelow ? below : above, minY, maxY), preferBelow ? TdTipSide.Below : TdTipSide.Above);
    }
}

public enum TdTipSide : byte { Below, Above, Right, Left }
