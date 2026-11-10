using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SideWar.AssetPipeline;
using SideWar.Rendering.Sprites;
using Xunit;

namespace SideWar.Rendering.Tests;

/// <summary>CA-22.10, CA-09.6 e T-22.11: PNG do design, MANIFEST.json e atlas embutido (T4.1).</summary>
public sealed class DesignAssetsTests
{
    private static readonly (string Id, string File, int W, int H)[] Table =
    [
        ("7876da3e6e78fb4707a23e01e62f6bfe", "tower-bolt.png", 44, 44),
        ("a6621b80f8e0703e2b1589c2f29ffb9f", "tower-frost.png", 39, 44),
        ("dd161558f3ec662a35934c4b3ef2ba70", "tower-cannon.png", 45, 44),
        ("268ba6a35d360775ffe7c94341033a1d", "slot-empty.png", 42, 26),
        ("19329f44a48eaf461324b83ac27582d5", "core.png", 84, 56),
        ("b825e950b868cc0d3e51f91e73e38462", "portal.png", 68, 52),
        ("5208805dc38aec3f98f1e0af4861a263", "enemy-boss.png", 68, 52),
        ("ad2f6943f64c22b20bef7d6f3a7d8fac", "supply-crate.png", 45, 24),
        ("2a01dfce08f7cf4828fa9d3f09f82316", "enemy-scout.png", 23, 26),
        ("77ebb775d3628025a44ef8f79c2ff41e", "enemy-drone.png", 38, 22),
        ("b96d1508c27374dfbbe1f29736f2876d", "enemy-crawler.png", 36, 22),
        ("dc8cc7e7f1039d49e39b2ccb15bf7b26", "enemy-shield.png", 32, 30),
        ("2c41c2be426c5c2fb97dec204b4b3316", "enemy-brute.png", 43, 34),
    ];

    private static string DesignDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SideWar.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "assets", "source", "design");
    }

    private static JsonElement[] Entries(JsonDocument doc) => [.. doc.RootElement.GetProperty("entries").EnumerateArray()];

    [Fact]
    public void Manifest_Has13Entries_PlusScaleSetPlaceholder_AndHashesMatch()
    {
        var dir = DesignDir();
        var manifest = Path.Combine(dir, "MANIFEST.json");
        Assert.Empty(ManifestVerifier.Verify(manifest, dir));

        using var doc = JsonDocument.Parse(File.ReadAllBytes(manifest));
        var entries = Entries(doc);
        Assert.Equal(14, entries.Length);

        foreach (var (id, file, w, h) in Table)
        {
            var e = entries.Single(x => x.GetProperty("file").GetString() == file);
            Assert.Equal(id, e.GetProperty("id").GetString());
            Assert.Equal(new[] { w, h }, e.GetProperty("displayDip").EnumerateArray().Select(v => v.GetInt32()).ToArray());
            Assert.False(e.TryGetProperty("placeholder", out _));
            var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, file))));
            Assert.Equal(actual, e.GetProperty("sha256").GetString());
        }

        var scale = entries.Single(x => x.GetProperty("file").GetString() == "tower-scaleset.png");
        Assert.True(scale.GetProperty("placeholder").GetBoolean());
        Assert.False(File.Exists(Path.Combine(dir, "tower-scaleset.png")));
    }

    [Fact]
    public void Pixels_AreTwiceTheDisplayedDip()
    {
        var dir = DesignDir();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "MANIFEST.json")));
        foreach (var e in Entries(doc).Where(x => !x.TryGetProperty("placeholder", out _)))
        {
            var dip = e.GetProperty("displayDip").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var px = e.GetProperty("pixels").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var rgba = Rgba.Decode(File.ReadAllBytes(Path.Combine(dir, e.GetProperty("file").GetString()!)));
            Assert.Equal(px, new[] { rgba.Width, rgba.Height });
            Assert.InRange(px[0], (dip[0] * 2) - 1, (dip[0] * 2) + 1);
            Assert.Equal(dip[1] * 2, px[1]);
        }
    }

    [Fact]
    public void EmbeddedAtlas_IsPresent_AndSizesMatchCatalog()
    {
        var asm = typeof(SpriteAtlas).Assembly;
        using var png = asm.GetManifestResourceStream("SideWar.Rendering.Assets.atlas.png");
        using var json = asm.GetManifestResourceStream("SideWar.Rendering.Assets.atlas.json");
        Assert.NotNull(png);
        Assert.NotNull(json);
        Assert.InRange(png.Length, 1, 600 * 1024);

        // SpriteAtlas.Load exige os 14 ids e o atlas não tem tower.scaleset (D-13): pendência registrada, fora desta ficha.
        using var doc = JsonDocument.Parse(json);
        var sprites = doc.RootElement.GetProperty("sprites");
        Assert.False(sprites.TryGetProperty("tower.scaleset", out _));
        foreach (var info in SpriteCatalog.All.Where(i => i.Id != SpriteId.TowerScaleSet))
        {
            var s = sprites.GetProperty(info.Key);
            Assert.Equal(info.WorldW, s.GetProperty("worldW").GetSingle());
            // altura derivada da proporção do PNG (pixels ímpares): tolerância de 0,6 DIP
            Assert.InRange(s.GetProperty("worldH").GetSingle(), info.WorldH - 0.6f, info.WorldH + 0.6f);
        }
    }
}
