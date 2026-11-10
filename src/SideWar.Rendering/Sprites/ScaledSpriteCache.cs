using SkiaSharp;

namespace SideWar.Rendering.Sprites;

/// <summary>
/// Cache pré-escalado (SDD-09 RF-09.5): <see cref="Bake"/> reduz o atlas mestre para
/// <c>round(worldW·renderScale) × round(worldH·renderScale)</c> e empacota num atlas de runtime.
/// Uso na T-Game; não é thread-safe.
/// </summary>
public sealed class ScaledSpriteCache : IDisposable
{
    private const int Padding = 2;
    private const int MaxWidth = 2048;
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly SpriteAtlas _atlas;
    private readonly Dictionary<SpriteId, (SKRectI Src, SKPointI Pivot)> _entries = [];
    private SKImage? _image;

    public ScaledSpriteCache(SpriteAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        _atlas = atlas;
    }

    public float RenderScale { get; private set; }

    /// <summary>Atlas de runtime (raster); nulo antes do primeiro bake.</summary>
    public SKImage? Image => _image;

    public bool IsBaked => _image is not null;

    public static SKSizeI ExpectedSize(SpriteInfo s, float renderScale) =>
        new(Math.Max(1, (int)MathF.Round(s.WorldW * renderScale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)MathF.Round(s.WorldH * renderScale, MidpointRounding.AwayFromZero)));

    public void Bake(float renderScale)
    {
        if (!float.IsFinite(renderScale) || renderScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(renderScale));
        }

        var sizes = new List<(int W, int H)>();
        foreach (var s in SpriteCatalog.All)
        {
            var d = ExpectedSize(s, renderScale);
            sizes.Add((d.Width, d.Height));
        }

        var (positions, height) = ShelfLayout.Pack(sizes, MaxWidth, Padding);
        using var master = SKImage.FromBitmap(_atlas.Master);
        using var target = new SKBitmap(new SKImageInfo(MaxWidth, Math.Max(1, height), SKColorType.Rgba8888, SKAlphaType.Premul));
        var entries = new Dictionary<SpriteId, (SKRectI, SKPointI)>();
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = false };
            for (var i = 0; i < sizes.Count; i++)
            {
                var info = SpriteCatalog.All[i];
                var (w, h) = sizes[i];
                var (x, y) = positions[i];
                var dest = new SKRectI(x, y, x + w, y + h);
                canvas.DrawImage(master, SKRect.Create(_atlas.GetRect(info.Id).Left, _atlas.GetRect(info.Id).Top, _atlas.GetRect(info.Id).Width, _atlas.GetRect(info.Id).Height), SKRect.Create(dest.Left, dest.Top, dest.Width, dest.Height), Sampling, paint);
                entries[info.Id] = (dest, new SKPointI(
                    (int)MathF.Round(info.PivotX * w, MidpointRounding.AwayFromZero),
                    (int)MathF.Round(info.PivotY * h, MidpointRounding.AwayFromZero)));
            }
        }

        var image = SKImage.FromBitmap(target);
        // FromBitmap compartilha pixels com o bitmap quando imutável; copiar para ser dono dos pixels.
        var owned = image.ToRasterImage(ensurePixelData: true);
        if (!ReferenceEquals(owned, image))
        {
            image.Dispose();
        }

        _image?.Dispose();
        _image = owned;
        _entries.Clear();
        foreach (var kv in entries)
        {
            _entries[kv.Key] = kv.Value;
        }

        RenderScale = renderScale;
    }

    public bool TryGet(SpriteId id, out SKRectI src, out SKPointI pivotPx)
    {
        if (_image is not null && _entries.TryGetValue(id, out var e))
        {
            src = e.Src;
            pivotPx = e.Pivot;
            return true;
        }

        src = default;
        pivotPx = default;
        return false;
    }

    /// <summary>Descarta o atlas de runtime (SDD-07 ReleaseCaches); um novo Bake o recria.</summary>
    public void Release()
    {
        _image?.Dispose();
        _image = null;
        _entries.Clear();
    }

    public void Dispose() => Release();
}
