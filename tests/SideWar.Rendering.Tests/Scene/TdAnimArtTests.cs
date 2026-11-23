using System.Reflection;
using System.Text.Json;
using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Tests.Golden;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Scene;

/// <summary>DESIGN-ANIM: tabela de animações × MANIFEST.json × recursos embutidos × conteúdo TD; a animação é por tempo de render.</summary>
public sealed class TdAnimArtTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();

    private static string[] EmbeddedAnim() => [.. typeof(TdAnimArt).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith("SideWar.Rendering.Assets.Anim.", StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.Ordinal))];

    private static string Manifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SideWar.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "assets", "source", "anim", "MANIFEST.json");
    }

    [Fact]
    public void MappedIds_ExistInContent()
    {
        foreach (var id in TdAnimArt.Towers) Assert.Contains(Content.Towers, t => t.Id == id);
        var all = Content.Enemies.Enemies.Select(e => e.Id).Concat(Content.Enemies.Bosses.Select(b => b.Id)).ToArray();
        foreach (var (id, _) in TdAnimArt.Enemies) Assert.Contains(id, all);
    }

    [Fact]
    public void EveryMappedSheet_IsEmbedded_AndMatchesManifestFramesAndFps()
    {
        var names = EmbeddedAnim().Select(n => n["SideWar.Rendering.Assets.Anim.".Length..]).ToHashSet();
        using var doc = JsonDocument.Parse(File.ReadAllText(Manifest()));
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToDictionary(e => e.GetProperty("file").GetString()!);
        Assert.Equal(entries.Keys.Order(), names.Order());

        void Check(string file, int frames, double fps)
        {
            var e = entries[file];
            Assert.Equal(frames, e.GetProperty("frames").GetInt32());
            Assert.InRange(e.GetProperty("fps").GetDouble(), fps - 0.05, fps + 0.05);
            var size = e.GetProperty("sheetPixels");
            Assert.Equal(frames, size[0].GetInt32() / e.GetProperty("framePixels")[0].GetInt32());
            using var s = typeof(TdAnimArt).Assembly.GetManifestResourceStream(TdAnimArt.Resource(file))!;
            using var bmp = SKBitmap.Decode(s);
            Assert.Equal(size[0].GetInt32(), bmp.Width);
            Assert.Equal(size[1].GetInt32(), bmp.Height);
        }

        foreach (var id in TdAnimArt.Towers)
        {
            Check(id + "-idle.png", TdAnimArt.IdleFrames, TdAnimArt.IdleFps);
            Check(id + "-attack.png", TdAnimArt.AttackFrames, TdAnimArt.AttackFps);
            Check(id + "-fx.png", TdAnimArt.FxFrames, id == "sentinel" ? 14 : TdAnimArt.FxFps);
        }
        foreach (var (id, fps) in TdAnimArt.Enemies) Check(id + "-walk.png", TdAnimArt.WalkFrames, fps);
    }

    [Fact]
    public void LoopFrame_FollowsFpsAndWraps()
    {
        Assert.Equal(0, TdAnimArt.LoopFrame(0, 8, 4));
        Assert.Equal(1, TdAnimArt.LoopFrame(0.125, 8, 4));
        Assert.Equal(3, TdAnimArt.LoopFrame(0.4999, 8, 4));
        Assert.Equal(0, TdAnimArt.LoopFrame(0.5, 8, 4));
        Assert.Equal(2, TdAnimArt.LoopFrame(0, 8, 4, 2));
    }

    [Fact]
    public void WaveScene_AnimatesWithRenderTime_AndIsDeterministicAtFixedTime()
    {
        var (match, view, now) = TdStripScenes.Build(TdStripScene.Wave);
        using var typo = new Typography();
        using var r = new TdBandRenderer(typo);
        r.SetDpi(96);
        var (w, h) = r.PhysicalSize(TdStripScenes.WidthDip);
        SKBitmap Draw(double t)
        {
            var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var c = new SKCanvas(bmp);
            r.Draw(c, match, t, view);
            return bmp;
        }
        using var a = Draw(now);
        using var a2 = Draw(now);
        using var b = Draw(now + 0.17);
        Assert.True(a.Bytes.AsSpan().SequenceEqual(a2.Bytes));
        Assert.False(a.Bytes.AsSpan().SequenceEqual(b.Bytes));
    }
    [Fact]
    public void DnsUsesTintedBoltSheets()
    {
        Assert.True(TdAnimArt.HasTower("dns"));
        Assert.Equal("bolt", TdAnimArt.TowerSheetId("dns"));
        Assert.True(TdArtMap.TryTower("dns", out var dns));
        Assert.True(dns.TintAlpha > 0);
    }

    [Fact]
    public void TowerFacesTargetOnEitherSide()
    {
        var (match, _, now) = TdStripScenes.Build(TdStripScene.PrepRange);
        var enemies = (List<TdEnemy>)typeof(TdMatchState).GetField("_enemies", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(match)!;
        using var typo = new Typography();
        using var renderer = new TdBandRenderer(typo);
        renderer.SetDpi(96);
        var (w, h) = renderer.PhysicalSize(TdStripScenes.WidthDip);
        SKBitmap Draw(int x)
        {
            enemies.Clear();
            enemies.Add(new TdEnemy { Id = 99, DefIndex = 0, X = x, Hp = 100, MaxHp = 100 });
            var bmp = new SKBitmap(w, h);
            using var c = new SKCanvas(bmp);
            renderer.Draw(c, match, now, TdView.None);
            return bmp;
        }
        using var left = Draw(270);
        using var right = Draw(390);
        int different = 0;
        for (int y = 0; y < h; y++)
        for (int x = 315; x < 345; x++)
        {
            if (left.GetPixel(x, y) != right.GetPixel(x, y)) different++;
        }
        Assert.True(different > 10, "a torre deve mudar de orientação");
    }

}
