namespace SideWar.Rendering.Sprites;

/// <summary>Empacotamento shelf por altura decrescente, estável; retorna posições na ordem de entrada.</summary>
internal static class ShelfLayout
{
    public static (List<(int X, int Y)> Positions, int Height) Pack(IReadOnlyList<(int W, int H)> sizes, int maxWidth, int padding)
    {
        var order = Enumerable.Range(0, sizes.Count)
            .OrderByDescending(i => sizes[i].H).ThenBy(i => i).ToArray();
        var pos = new (int X, int Y)[sizes.Count];
        int x = 0, y = 0, shelfH = 0;
        foreach (var i in order)
        {
            var (w, h) = sizes[i];
            if (w > maxWidth)
            {
                throw new InvalidOperationException($"Sprite com {w} px excede a largura máxima {maxWidth}.");
            }

            if (x > 0 && x + w > maxWidth)
            {
                y += shelfH + padding;
                x = 0;
                shelfH = 0;
            }

            pos[i] = (x, y);
            x += w + padding;
            shelfH = Math.Max(shelfH, h);
        }

        return ([.. pos], y + shelfH);
    }
}
