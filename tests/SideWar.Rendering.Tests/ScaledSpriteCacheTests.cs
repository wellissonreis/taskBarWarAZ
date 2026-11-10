using SideWar.Rendering.Sprites;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests;

public sealed class ScaledSpriteCacheTests
{
    private static SpriteAtlas Synthetic()
    {
        var rects = new Dictionary<SpriteId, SKRectI>();
        var bmp = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
        bmp.Erase(SKColors.Red);
        var i = 0;
        foreach (var s in SpriteCatalog.All)
        {
            rects[s.Id] = new SKRectI(i * 16, 0, (i * 16) + 12, 40);
            i++;
        }

        return new SpriteAtlas(bmp, rects, false);
    }

    public static TheoryData<float> Scales => new() { 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 3.0f };

    [Theory]
    [MemberData(nameof(Scales))]
    public void Bake_GeraTamanhoEsperadoParaTodosOsIds(float scale)
    {
        using var atlas = Synthetic();
        using var cache = new ScaledSpriteCache(atlas);
        cache.Bake(scale);
        foreach (var s in SpriteCatalog.All)
        {
            Assert.True(cache.TryGet(s.Id, out var src, out var pivot));
            Assert.InRange(src.Width, (s.WorldW * scale) - 1, (s.WorldW * scale) + 1);
            Assert.InRange(src.Height, (s.WorldH * scale) - 1, (s.WorldH * scale) + 1);
            Assert.Equal(src.Height, pivot.Y);
        }
    }

    [Fact]
    public void Bake_RetangulosDisjuntosEDentroDoAtlas()
    {
        using var atlas = Synthetic();
        using var cache = new ScaledSpriteCache(atlas);
        cache.Bake(3.0f);
        var rects = SpriteCatalog.All.Select(s => { cache.TryGet(s.Id, out var r, out _); return r; }).ToList();
        for (var a = 0; a < rects.Count; a++)
        {
            Assert.True(rects[a].Right <= cache.Image!.Width && rects[a].Bottom <= cache.Image.Height);
            for (var b = a + 1; b < rects.Count; b++)
            {
                Assert.False(rects[a].IntersectsWith(rects[b]));
            }
        }
    }

    [Fact]
    public void Bake_ReBakeEReleaseFuncionam()
    {
        using var atlas = Synthetic();
        using var cache = new ScaledSpriteCache(atlas);
        Assert.False(cache.TryGet(SpriteId.Core, out _, out _));
        cache.Bake(1f);
        cache.TryGet(SpriteId.Core, out var a, out _);
        cache.Bake(2f);
        cache.TryGet(SpriteId.Core, out var b, out _);
        Assert.Equal(84, a.Width);
        Assert.Equal(168, b.Width);
        Assert.Equal(2f, cache.RenderScale);
        cache.Release();
        Assert.False(cache.IsBaked);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void Bake_EscalaInvalidaLanca(float scale)
    {
        using var atlas = Synthetic();
        using var cache = new ScaledSpriteCache(atlas);
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Bake(scale));
    }

    [Fact]
    public void Bake_PreservaPixelOpacoDoMestre()
    {
        using var atlas = Synthetic();
        using var cache = new ScaledSpriteCache(atlas);
        cache.Bake(1f);
        cache.TryGet(SpriteId.TowerBolt, out var r, out _);
        using var bmp = SKBitmap.FromImage(cache.Image!);
        Assert.Equal(new SKColor(255, 0, 0, 255), bmp.GetPixel(r.MidX, r.MidY));
    }

    [Fact]
    public void Placeholder_CobreTodosOsIdsDoSprites_Json()
    {
        using var atlas = PlaceholderAtlas.Create();
        Assert.True(atlas.IsPlaceholder);
        var json = File.ReadAllText(FindRepoFile("assets/source/sprites.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var keys = doc.RootElement.GetProperty("sprites").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();
        Assert.Equal(keys.Count, SpriteCatalog.All.Count);
        foreach (var k in keys)
        {
            Assert.True(SpriteCatalog.TryGetByKey(k, out var info), k);
            Assert.True(atlas.GetRect(info.Id).Width > 0);
        }

        // alturas de SDD-22 RF-22.9 (exceto scaleset)
        var expected = new Dictionary<string, float>
        {
            ["tower.bolt"] = 44, ["tower.frost"] = 44, ["tower.cannon"] = 44, ["slot.empty"] = 26, ["core"] = 56,
            ["portal"] = 52, ["enemy.boss"] = 52, ["supply.crate"] = 24, ["enemy.grunt"] = 26, ["enemy.runner"] = 22,
            ["enemy.swarm"] = 22, ["enemy.armored"] = 30, ["enemy.tank"] = 34,
        };
        foreach (var (k, h) in expected)
        {
            SpriteCatalog.TryGetByKey(k, out var info);
            Assert.Equal(h, info.WorldH);
        }
    }

    [Fact]
    public void Placeholder_BakeFuncionaEmTodasAsEscalasEPintaPixels()
    {
        using var atlas = PlaceholderAtlas.Create();
        using var cache = new ScaledSpriteCache(atlas);
        foreach (var scale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f, 3f })
        {
            cache.Bake(scale);
            using var bmp = SKBitmap.FromImage(cache.Image!);
            foreach (var s in SpriteCatalog.All)
            {
                Assert.True(cache.TryGet(s.Id, out var r, out _));
                var exp = ScaledSpriteCache.ExpectedSize(s, scale);
                Assert.Equal(exp.Width, r.Width);
                Assert.Equal(exp.Height, r.Height);
                var any = false;
                for (var y = r.Top; y < r.Bottom && !any; y++)
                {
                    for (var x = r.Left; x < r.Right && !any; x++)
                    {
                        any = bmp.GetPixel(x, y).Alpha > 0;
                    }
                }

                Assert.True(any, s.Key);
            }
        }
    }

    [Fact]
    public void Load_LePngENaoPremultiplicadoEJson()
    {
        using var src = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        src.Erase(new SKColor(10, 200, 30, 255));
        using var png = new MemoryStream();
        src.Encode(png, SKEncodedImageFormat.Png, 100);
        png.Position = 0;
        var sprites = string.Join(",", SpriteCatalog.All.Select((s, i) => $"\"{s.Key}\":{{\"x\":{i * 4},\"y\":0,\"w\":4,\"h\":8}}"));
        using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"{{\"version\":1,\"sprites\":{{{sprites}}}}}"));
        using var atlas = SpriteAtlas.Load(png, json);
        Assert.False(atlas.IsPlaceholder);
        Assert.Equal(SKAlphaType.Premul, atlas.Master.AlphaType);
        Assert.Equal(new SKRectI(4, 0, 8, 8), atlas.GetRect(SpriteId.TowerFrost));
    }

    [Fact]
    public void Load_IdAusenteNoJsonFalha()
    {
        using var src = new SKBitmap(8, 8);
        using var png = new MemoryStream();
        src.Encode(png, SKEncodedImageFormat.Png, 100);
        png.Position = 0;
        using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"sprites\":{\"core\":{\"x\":0,\"y\":0,\"w\":4,\"h\":4}}}"));
        Assert.Throws<ArgumentException>(() => SpriteAtlas.Load(png, json));
    }

    private static string FindRepoFile(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, rel)))
        {
            dir = dir.Parent;
        }

        return dir is null ? throw new FileNotFoundException(rel) : Path.Combine(dir.FullName, rel);
    }
}
