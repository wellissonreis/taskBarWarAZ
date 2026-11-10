using System.Text.Json;
using SkiaSharp;

namespace SideWar.Rendering.Sprites;

/// <summary>Atlas mestre (resolução mestre, premultiplicado) com o retângulo de cada sprite (SDD-09 RF-09.4).</summary>
public sealed class SpriteAtlas : IDisposable
{
    private readonly Dictionary<SpriteId, SKRectI> _rects;

    public SpriteAtlas(SKBitmap master, IReadOnlyDictionary<SpriteId, SKRectI> rects, bool isPlaceholder)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentNullException.ThrowIfNull(rects);
        if (master.AlphaType != SKAlphaType.Premul)
        {
            throw new ArgumentException("O atlas mestre deve ser Premul.", nameof(master));
        }

        foreach (var s in SpriteCatalog.All)
        {
            if (!rects.TryGetValue(s.Id, out var r) || r.Width <= 0 || r.Height <= 0
                || r.Left < 0 || r.Top < 0 || r.Right > master.Width || r.Bottom > master.Height)
            {
                throw new ArgumentException($"Retângulo ausente ou inválido para '{s.Key}'.", nameof(rects));
            }
        }

        Master = master;
        _rects = new Dictionary<SpriteId, SKRectI>(rects);
        IsPlaceholder = isPlaceholder;
    }

    public SKBitmap Master { get; }

    public bool IsPlaceholder { get; }

    public SKRectI GetRect(SpriteId id) => _rects[id];

    /// <summary>
    /// Carrega o atlas de <c>atlas.png</c> (não premultiplicado) e <c>atlas.json</c> (SDD-09 RF-09.3).
    /// Falha se algum id do catálogo faltar no JSON.
    /// </summary>
    public static SpriteAtlas Load(Stream png, Stream json)
    {
        ArgumentNullException.ThrowIfNull(png);
        ArgumentNullException.ThrowIfNull(json);
        using var raw = SKBitmap.Decode(png) ?? throw new InvalidDataException("atlas.png inválido.");
        var master = new SKBitmap(new SKImageInfo(raw.Width, raw.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(master))
        {
            canvas.Clear(SKColors.Transparent);
            using var img = SKImage.FromBitmap(raw);
            canvas.DrawImage(img, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var rects = new Dictionary<SpriteId, SKRectI>();
            foreach (var p in doc.RootElement.GetProperty("sprites").EnumerateObject())
            {
                if (!SpriteCatalog.TryGetByKey(p.Name, out var info))
                {
                    continue;
                }

                var v = p.Value;
                var x = v.GetProperty("x").GetInt32();
                var y = v.GetProperty("y").GetInt32();
                rects[info.Id] = new SKRectI(x, y, x + v.GetProperty("w").GetInt32(), y + v.GetProperty("h").GetInt32());
            }

            return new SpriteAtlas(master, rects, isPlaceholder: false);
        }
        catch
        {
            master.Dispose();
            throw;
        }
    }

    public void Dispose() => Master.Dispose();
}
