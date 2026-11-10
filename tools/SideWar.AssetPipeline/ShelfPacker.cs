namespace SideWar.AssetPipeline;

public readonly record struct PackItem(string Id, int Width, int Height);

public readonly record struct PackedRect(string Id, int X, int Y, int W, int H);

/// <summary>Empacotamento shelf por altura decrescente (SDD-09 RF-09.3 passo 4). Determinístico.</summary>
public static class ShelfPacker
{
    public const int Padding = 2;
    public const int MaxSize = 1024;

    public static (IReadOnlyList<PackedRect> Rects, int Width, int Height) Pack(IEnumerable<PackItem> items)
    {
        var sorted = items
            .OrderByDescending(i => i.Height)
            .ThenByDescending(i => i.Width)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();

        var rects = new List<PackedRect>(sorted.Count);
        int x = 0, y = 0, rowH = 0, usedW = 0;
        foreach (var it in sorted)
        {
            if (it.Width > MaxSize)
            {
                throw new InvalidOperationException($"Sprite '{it.Id}' mais largo que {MaxSize} px.");
            }

            if (x > 0 && x + it.Width > MaxSize)
            {
                x = 0;
                y += rowH + Padding;
                rowH = 0;
            }

            rects.Add(new PackedRect(it.Id, x, y, it.Width, it.Height));
            x += it.Width + Padding;
            usedW = Math.Max(usedW, x - Padding);
            rowH = Math.Max(rowH, it.Height);
        }

        var height = rects.Count == 0 ? 0 : y + rowH;
        if (height > MaxSize)
        {
            throw new InvalidOperationException($"Atlas {usedW}x{height} excede {MaxSize}x{MaxSize} px.");
        }

        return (rects, usedW, height);
    }
}
