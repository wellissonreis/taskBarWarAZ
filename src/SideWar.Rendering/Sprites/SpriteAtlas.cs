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
    public static SpriteAtlas Load(Stream png, Stream json) => Load(png, json, fillMissingWithPlaceholder: false);

    /// <summary>
    /// Carrega o atlas embutido em <c>SideWar.Rendering.Assets.atlas.png/json</c> (RF-09.4). Ids ausentes do atlas
    /// (ex.: <c>tower.scaleset</c>, placeholder D-13) são preenchidos pelo <see cref="PlaceholderAtlas"/> (RF-09.6);
    /// nunca lança por sprite ausente. Se os recursos não existirem, devolve o <see cref="PlaceholderAtlas"/> completo.
    /// </summary>
    public static SpriteAtlas LoadEmbedded()
    {
        var asm = typeof(SpriteAtlas).Assembly;
        using var png = asm.GetManifestResourceStream("SideWar.Rendering.Assets.atlas.png");
        using var json = asm.GetManifestResourceStream("SideWar.Rendering.Assets.atlas.json");
        if (png is null || json is null)
        {
            return PlaceholderAtlas.Create();
        }

        return Load(png, json, fillMissingWithPlaceholder: true);
    }

    private static SpriteAtlas Load(Stream png, Stream json, bool fillMissingWithPlaceholder)
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

            if (fillMissingWithPlaceholder)
            {
                var filled = FillMissing(master, rects);
                if (!ReferenceEquals(filled, master))
                {
                    master.Dispose();
                    master = filled;
                }
            }

            return new SpriteAtlas(master, rects, isPlaceholder: false);
        }
        catch
        {
            master.Dispose();
            throw;
        }
    }

    private static SKBitmap FillMissing(SKBitmap master, Dictionary<SpriteId, SKRectI> rects)
    {
        var missing = SpriteCatalog.All.Where(s => !rects.ContainsKey(s.Id)).ToList();
        if (missing.Count == 0)
        {
            return master;
        }

        using var ph = PlaceholderAtlas.Create();
        const int gap = 2;
        var width = Math.Max(master.Width, missing.Sum(m => ph.GetRect(m.Id).Width + gap));
        var bandH = missing.Max(m => ph.GetRect(m.Id).Height);
        var result = new SKBitmap(new SKImageInfo(width, master.Height + gap + bandH, SKColorType.Rgba8888, SKAlphaType.Premul));
        try
        {
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);
            using var masterImg = SKImage.FromBitmap(master);
            canvas.DrawImage(masterImg, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
            using var phImg = SKImage.FromBitmap(ph.Master);
            var x = 0;
            var y = master.Height + gap;
            foreach (var m in missing)
            {
                var src = ph.GetRect(m.Id);
                var dst = new SKRect(x, y, x + src.Width, y + src.Height);
                canvas.DrawImage(phImg, src, dst, new SKSamplingOptions(SKFilterMode.Nearest), null);
                rects[m.Id] = new SKRectI(x, y, x + src.Width, y + src.Height);
                x += src.Width + gap;
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public void Dispose() => Master.Dispose();
}
