using SideWar.Rendering.Theme;
using SkiaSharp;

namespace SideWar.Rendering.Sprites;

/// <summary>
/// Atlas gerado em código com formas geométricas para todos os ids do catálogo (SDD-09 RF-09.6),
/// usado quando o recurso <c>atlas.png</c> não existe (D-03 pendente).
/// </summary>
public static class PlaceholderAtlas
{
    /// <summary>Resolução mestre do placeholder (px por DIP), igual ao masterScale provisório da SDD-09.</summary>
    public const float MasterScale = 3.25f;

    private const int Padding = 2;
    private const int MaxWidth = 1024;

    public static SpriteAtlas Create()
    {
        var sizes = new List<(SpriteInfo Info, int W, int H)>();
        foreach (var s in SpriteCatalog.All)
        {
            sizes.Add((s, (int)Math.Ceiling(s.WorldW * MasterScale), (int)Math.Ceiling(s.WorldH * MasterScale)));
        }

        var (positions, height) = ShelfLayout.Pack(sizes.ConvertAll(t => (t.W, t.H)), MaxWidth, Padding);
        var bitmap = new SKBitmap(new SKImageInfo(MaxWidth, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var rects = new Dictionary<SpriteId, SKRectI>();
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            for (var i = 0; i < sizes.Count; i++)
            {
                var (info, w, h) = sizes[i];
                var (x, y) = positions[i];
                var rect = new SKRectI(x, y, x + w, y + h);
                rects[info.Id] = rect;
                Draw(canvas, info.Id, rect);
            }

            return new SpriteAtlas(bitmap, rects, isPlaceholder: true);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static SKColor ColorOf(SpriteId id) => id switch
    {
        SpriteId.TowerBolt => Palette.Placeholder.TowerBolt,
        SpriteId.TowerFrost => Palette.Placeholder.TowerFrost,
        SpriteId.TowerCannon => Palette.Placeholder.TowerCannon,
        SpriteId.TowerScaleSet => Palette.Placeholder.TowerScaleSet,
        SpriteId.SlotEmpty => Palette.Placeholder.SlotEmpty,
        SpriteId.Core => Palette.Placeholder.Core,
        SpriteId.Portal => Palette.Placeholder.Portal,
        SpriteId.SupplyCrate => Palette.Placeholder.SupplyCrate,
        _ => Palette.Placeholder.Enemy, // inimigos
    };

    private static void Draw(SKCanvas canvas, SpriteId id, SKRectI r)
    {
        using var fill = new SKPaint { Color = ColorOf(id), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var edge = new SKPaint
        {
            Color = Palette.Placeholder.Edge,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(2f, r.Height / 24f),
        };
        var inset = edge.StrokeWidth;
        var box = new SKRect(r.Left + inset, r.Top + inset, r.Right - inset, r.Bottom - inset);

        switch (id)
        {
            case SpriteId.TowerBolt:
            case SpriteId.TowerFrost:
            case SpriteId.TowerCannon:
            case SpriteId.TowerScaleSet:
                {
                    using var builder = new SKPathBuilder();
                    builder.MoveTo(box.MidX, box.Top);
                    builder.LineTo(box.Right, box.Bottom);
                    builder.LineTo(box.Left, box.Bottom);
                    builder.Close();
                    using var path = builder.Detach();
                    canvas.DrawPath(path, fill);
                    canvas.DrawPath(path, edge);
                    break;
                }

            case SpriteId.SlotEmpty:
                canvas.DrawOval(box, fill);
                canvas.DrawOval(box, edge);
                break;
            case SpriteId.Core:
            case SpriteId.SupplyCrate:
                canvas.DrawRoundRect(box, box.Height / 6f, box.Height / 6f, fill);
                canvas.DrawRoundRect(box, box.Height / 6f, box.Height / 6f, edge);
                break;
            case SpriteId.Portal:
                canvas.DrawOval(box, fill);
                canvas.DrawOval(box, edge);
                break;
            default:
                canvas.DrawRect(box, fill);
                canvas.DrawRect(box, edge);
                break;
        }
    }
}
