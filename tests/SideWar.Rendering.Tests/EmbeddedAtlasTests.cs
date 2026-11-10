using SideWar.Rendering.Sprites;
using Xunit;

namespace SideWar.Rendering.Tests;

/// <summary>T4.3b: atlas real embutido com preenchimento por placeholder (SDD-09 RF-09.4/RF-09.6).</summary>
public sealed class EmbeddedAtlasTests
{
    [Fact]
    public void LoadEmbedded_NaoLancaECobreOs14Ids()
    {
        using var atlas = SpriteAtlas.LoadEmbedded();
        Assert.Equal(14, SpriteCatalog.All.Count);
        foreach (var s in SpriteCatalog.All)
        {
            var r = atlas.GetRect(s.Id);
            Assert.True(r.Width > 0 && r.Height > 0, s.Key);
            Assert.True(r.Right <= atlas.Master.Width && r.Bottom <= atlas.Master.Height, s.Key);
        }
    }

    [Fact]
    public void LoadEmbedded_ScaleSetVemDoPlaceholder_OsDemaisDoAtlasReal()
    {
        using var atlas = SpriteAtlas.LoadEmbedded();
        var asm = typeof(SpriteAtlas).Assembly;
        using var png = asm.GetManifestResourceStream("SideWar.Rendering.Assets.atlas.png")!;
        using var json = asm.GetManifestResourceStream("SideWar.Rendering.Assets.atlas.json")!;
        Assert.Throws<ArgumentException>(() => SpriteAtlas.Load(png, json));

        var scale = atlas.GetRect(SpriteId.TowerScaleSet);
        Assert.True(scale.Top >= 162, "Scale Set deve ficar abaixo da área do atlas real (162 px).");
        Assert.True(atlas.Master.GetPixel(scale.MidX, scale.Bottom - 6).Alpha > 0);
        Assert.True(atlas.GetRect(SpriteId.Core).Bottom <= 162);
    }

    public static TheoryData<float> Scales => new() { 1.0f, 1.25f, 1.5f, 2.0f };

    [Theory]
    [MemberData(nameof(Scales))]
    public void Bake_ComAtlasEmbutido_GeraOs14Ids(float scale)
    {
        using var atlas = SpriteAtlas.LoadEmbedded();
        using var cache = new ScaledSpriteCache(atlas);
        cache.Bake(scale);
        foreach (var s in SpriteCatalog.All)
        {
            Assert.True(cache.TryGet(s.Id, out var src, out var pivot), s.Key);
            Assert.InRange(src.Width, (s.WorldW * scale) - 1, (s.WorldW * scale) + 1);
            Assert.InRange(src.Height, (s.WorldH * scale) - 1, (s.WorldH * scale) + 1);
            Assert.Equal(src.Height, pivot.Y);
        }
    }
}
