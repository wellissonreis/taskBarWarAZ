using System.Security.Cryptography;
using System.Text.Json;
using SideWar.AssetPipeline;
using Xunit;

namespace SideWar.Rendering.Tests;

public sealed class AssetPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sidewar-t42-" + Guid.NewGuid().ToString("N"));

    public AssetPipelineTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Rgba Make(int w, int h, Func<int, int, (byte R, byte G, byte B, byte A)> px)
    {
        var d = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (r, g, b, a) = px(x, y);
                var i = ((y * w) + x) * 4;
                d[i] = r; d[i + 1] = g; d[i + 2] = b; d[i + 3] = a;
            }
        }

        return new Rgba(w, h, d);
    }

    // Gradiente com alfas baixos (1..7) e altos, para exercitar o limiar.
    private static byte[] NoisyPng(int w, int h, int seed) =>
        Make(w, h, (x, y) => ((byte)((x * 7) + seed), (byte)((y * 5) + seed), (byte)(x + y), (byte)(((x * 3) + (y * 11) + seed) % 24 == 0 ? 0 : ((x + y + seed) % 256)))).EncodePng();

    private static SpriteDefinition Def(string id, double? ww = 20, double? wh = null, bool trim = false, bool placeholder = false) =>
        new(id, $"design/{id}.png", ww, wh, 0.5, 1.0, trim, placeholder);

    private static List<SpriteSource> Sources() =>
    [
        new(Def("a", 30), NoisyPng(100, 80, 1)),
        new(Def("b", 20), NoisyPng(40, 60, 2)),
        new(Def("c", 12), NoisyPng(24, 24, 3)),
        new(Def("d", null, 18), NoisyPng(35, 70, 4)),
    ];

    // CA-09.1 / T-09.1
    [Fact]
    public void Build_IsByteIdenticalAcrossRuns()
    {
        var r1 = AtlasBuilder.Build(Sources());
        var r2 = AtlasBuilder.Build(Sources());
        Assert.Equal(r1.Png, r2.Png);
        Assert.Equal(r1.Json, r2.Json);
        Assert.Equal(r1.InputsHash, r2.InputsHash);
        Assert.Equal(r1.PixelsHash, r2.PixelsHash);
    }

    [Fact]
    public void Build_IsIndependentOfInputOrder()
    {
        var s = Sources();
        var r1 = AtlasBuilder.Build(s);
        s.Reverse();
        var r2 = AtlasBuilder.Build(s);
        Assert.Equal(r1.Png, r2.Png);
        Assert.Equal(r1.Json, r2.Json);
    }

    [Fact]
    public void InputsHash_ChangesWithPixelsAndMasterScale()
    {
        var s = Sources();
        var h = AtlasBuilder.ComputeInputsHash(s, 3.25);
        Assert.NotEqual(h, AtlasBuilder.ComputeInputsHash(s, 3.0));
        s[0] = new SpriteSource(s[0].Definition, NoisyPng(100, 80, 99));
        Assert.NotEqual(h, AtlasBuilder.ComputeInputsHash(s, 3.25));
    }

    [Fact]
    public void Json_DeclaresHashesAndVersion()
    {
        var r = AtlasBuilder.Build(Sources());
        using var doc = JsonDocument.Parse(r.Json);
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(3.25, doc.RootElement.GetProperty("masterScale").GetDouble());
        Assert.Equal(r.InputsHash, doc.RootElement.GetProperty("inputsHash").GetString());
        Assert.Equal(r.PixelsHash, doc.RootElement.GetProperty("pixelsHash").GetString());
        Assert.Equal(4, doc.RootElement.GetProperty("sprites").EnumerateObject().Count());
    }

    // CA-09.2 / T-09.2
    [Fact]
    public void Atlas_HasNoPixelWithAlphaBetween1And7()
    {
        var r = AtlasBuilder.Build(Sources());
        var img = Rgba.Decode(r.Png);
        var sawLow = false;
        for (var i = 3; i < img.Data.Length; i += 4)
        {
            Assert.False(img.Data[i] is > 0 and < 8, $"alfa {img.Data[i]} no byte {i}");
            sawLow |= img.Data[i] == 0;
        }

        Assert.True(sawLow); // o fixture de fato tinha alfa baixo zerado
    }

    [Fact]
    public void Atlas_ZerosColorOfTransparentPixels_AndKeepsUnpremultipliedColor()
    {
        var png = Make(4, 4, (x, y) => x == 0 ? ((byte)200, (byte)10, (byte)20, (byte)5) : ((byte)200, (byte)100, (byte)50, (byte)128)).EncodePng();
        var r = AtlasBuilder.Build([new SpriteSource(Def("s", 4), png)]);
        var img = Rgba.Decode(r.Png);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, img.Data[..4]);
        var p = img.Data[4..8];
        Assert.InRange(p[0], 198, 202); // não premultiplicado no arquivo
        Assert.InRange(p[1], 98, 102);
        Assert.InRange(p[2], 48, 52);
        Assert.Equal(128, p[3]);
    }

    [Fact]
    public void Trim_CropsOnlyWhenRequested()
    {
        Rgba Padded() => Make(10, 10, (x, y) => x is >= 3 and < 7 && y is >= 2 and < 8 ? ((byte)9, (byte)9, (byte)9, (byte)255) : default);
        var png = Padded().EncodePng();

        var noTrim = AtlasBuilder.Build([new SpriteSource(Def("s", 10, null, trim: false), png)]);
        Assert.Equal((10, 10), (noTrim.Rects[0].W, noTrim.Rects[0].H));

        var trim = AtlasBuilder.Build([new SpriteSource(Def("s", 4, null, trim: true), png)]);
        Assert.Equal((4, 6), (trim.Rects[0].W, trim.Rects[0].H));
        using var doc = JsonDocument.Parse(trim.Json);
        Assert.Equal(6.0, doc.RootElement.GetProperty("sprites").GetProperty("s").GetProperty("worldH").GetDouble());
    }

    [Fact]
    public void Master_NeverUpscales_AndDownscalesToCeilDipTimesScale()
    {
        // nativo 24 < ceil(12*3.25)=39 -> mantém 24; nativo 100 > ceil(30*3.25)=98 -> reduz a 98 (altura proporcional 78,4 -> 78).
        var r = AtlasBuilder.Build(Sources());
        var c = r.Rects.Single(x => x.Id == "c");
        var a = r.Rects.Single(x => x.Id == "a");
        Assert.Equal((24, 24), (c.W, c.H));
        Assert.Equal((98, 78), (a.W, a.H));
        var d = r.Rects.Single(x => x.Id == "d"); // eixo de altura: ceil(18*3.25)=59 < 70
        Assert.Equal(59, d.H);
    }

    // T-09.3
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Packer_RectsAreDisjointWithPaddingAndInsideBounds(int seed)
    {
        var rng = new Random(seed);
        var items = Enumerable.Range(0, 60).Select(i => new PackItem($"s{i:D2}", rng.Next(5, 120), rng.Next(5, 90))).ToList();
        var (rects, w, h) = ShelfPacker.Pack(items);
        Assert.Equal(items.Count, rects.Count);
        Assert.True(w <= 1024 && h <= 1024);
        foreach (var r in rects)
        {
            Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.W <= w && r.Y + r.H <= h);
        }

        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                var a = rects[i];
                var b = rects[j];
                var apart = a.X + a.W + ShelfPacker.Padding <= b.X || b.X + b.W + ShelfPacker.Padding <= a.X
                         || a.Y + a.H + ShelfPacker.Padding <= b.Y || b.Y + b.H + ShelfPacker.Padding <= a.Y;
                Assert.True(apart, $"{a.Id} colide com {b.Id}");
            }
        }
    }

    [Fact]
    public void Packer_FailsWhenAtlasExceeds1024()
    {
        var items = Enumerable.Range(0, 30).Select(i => new PackItem($"s{i}", 500, 100));
        Assert.Throws<InvalidOperationException>(() => ShelfPacker.Pack(items));
    }

    [Fact]
    public void Packer_FailsWhenSpriteWiderThan1024()
    {
        Assert.Throws<InvalidOperationException>(() => ShelfPacker.Pack([new PackItem("x", 1025, 10)]));
    }

    [Fact]
    public void SpritesFile_ParsesSpecAndRejectsBadEntries()
    {
        var list = SpritesFile.Parse(File.ReadAllBytes(FindRepoFile("assets/source/sprites.json")));
        Assert.Equal(14, list.Count);
        Assert.Single(list, s => s.Placeholder);
        Assert.All(list, s => Assert.False(s.Trim));

        static byte[] J(string s) => System.Text.Encoding.UTF8.GetBytes(s);
        Assert.Throws<InvalidDataException>(() => SpritesFile.Parse(J("""{"version":1,"sprites":[{"id":"a","file":"x.png"}]}""")));
        Assert.Throws<InvalidDataException>(() => SpritesFile.Parse(J("""{"version":1,"sprites":[{"id":"a","file":"x","worldWidth":1,"worldHeight":1}]}""")));
        Assert.Throws<InvalidDataException>(() => SpritesFile.Parse(J("""{"version":1,"sprites":[{"id":"a","file":"x","worldWidth":1},{"id":"a","file":"y","worldWidth":1}]}""")));
    }

    [Fact]
    public void Runner_WritesThenSkipsWhenUnchanged_AndRewritesOnChange()
    {
        var inDir = Path.Combine(_root, "in");
        var outDir = Path.Combine(_root, "out");
        Directory.CreateDirectory(Path.Combine(inDir, "design"));
        File.WriteAllText(Path.Combine(inDir, "sprites.json"), """
            {"version":1,"sprites":[
              {"id":"x","file":"design/x.png","worldWidth":20,"trim":false},
              {"id":"ph","file":"design/ph.png","worldWidth":20,"trim":false,"placeholder":true}]}
            """);
        File.WriteAllBytes(Path.Combine(inDir, "design/x.png"), NoisyPng(30, 30, 5));

        Assert.Equal(RunOutcome.Written, PipelineRunner.Run(inDir, outDir));
        var pngPath = Path.Combine(outDir, "atlas.png");
        var first = File.ReadAllBytes(pngPath);
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(pngPath, stamp);

        Assert.Equal(RunOutcome.UpToDate, PipelineRunner.Run(inDir, outDir));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(pngPath)); // não regravou
        Assert.Equal(first, File.ReadAllBytes(pngPath));

        File.WriteAllBytes(Path.Combine(inDir, "design/x.png"), NoisyPng(30, 30, 6));
        Assert.Equal(RunOutcome.Written, PipelineRunner.Run(inDir, outDir));
        Assert.NotEqual(first, File.ReadAllBytes(pngPath));
    }

    [Fact]
    public void Runner_FailsOnMissingNonPlaceholderPng()
    {
        var inDir = Path.Combine(_root, "in2");
        Directory.CreateDirectory(inDir);
        File.WriteAllText(Path.Combine(inDir, "sprites.json"), """{"version":1,"sprites":[{"id":"x","file":"design/x.png","worldWidth":20}]}""");
        Assert.Throws<FileNotFoundException>(() => PipelineRunner.Run(inDir, Path.Combine(_root, "o")));
    }

    [Fact]
    public void Manifest_ConsumedWhenPresent_DetectsHashMismatchAndMissingFile()
    {
        var inDir = Path.Combine(_root, "in3");
        var design = Path.Combine(inDir, "design");
        Directory.CreateDirectory(design);
        var png = NoisyPng(30, 30, 7);
        File.WriteAllBytes(Path.Combine(design, "x.png"), png);
        File.WriteAllText(Path.Combine(inDir, "sprites.json"), """{"version":1,"sprites":[{"id":"x","file":"design/x.png","worldWidth":20}]}""");

        var good = Convert.ToHexStringLower(SHA256.HashData(png));
        var manifest = Path.Combine(design, "MANIFEST.json");
        File.WriteAllText(manifest, $$"""[{"id":"x","file":"x.png","sha256":"{{good}}"},{"id":"ph","file":"nope.png","sha256":"","placeholder":true}]""");
        Assert.Empty(ManifestVerifier.Verify(manifest, design));
        Assert.Equal(RunOutcome.Written, PipelineRunner.Run(inDir, Path.Combine(_root, "o3")));

        File.WriteAllText(manifest, """{"entries":[{"id":"x","file":"x.png","sha256":"00"},{"id":"y","file":"y.png","sha256":"00"}]}""");
        Assert.Equal(2, ManifestVerifier.Verify(manifest, design).Count);
        Assert.Throws<InvalidDataException>(() => PipelineRunner.Run(inDir, Path.Combine(_root, "o4")));
    }

    private static string FindRepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "SideWar.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(dir ?? throw new FileNotFoundException("SideWar.slnx"), relative);
    }
}
