using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>TD.7: goldens da faixa TD de 32 DIP a 100% e 150% (conteúdo de teste fixo; PNG em Golden/).</summary>
public sealed class TdStripGoldenTests
{
    public static TheoryData<TdStripScene, int> Cases()
    {
        var d = new TheoryData<TdStripScene, int>();
        foreach (var scene in Enum.GetValues<TdStripScene>())
            foreach (int dpi in new[] { 96, 144 })
                d.Add(scene, dpi);
        return d;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TdStrip_MatchesGolden(TdStripScene scene, int dpi)
    {
        using var bmp = TdStripScenes.Render(scene, dpi);
        Assert.Equal(TdStripScenes.WidthDip * dpi / 96, bmp.Width);
        string? failure = GoldenImage.Verify(TdStripScenes.GoldenName(scene, dpi), bmp);
        Assert.True(failure is null, failure);
    }

    /// <summary>TD.14: faixa a 40 (Pequeno), 64 (Médio, padrão) e 88 (Grande) DIP a 100%: arte, trilho e indicadores escalam juntos.</summary>
    public static TheoryData<TdStripScene, int> BandCases()
    {
        var d = new TheoryData<TdStripScene, int>();
        foreach (var scene in new[] { TdStripScene.Wave, TdStripScene.PrepRange, TdStripScene.Unpositioned })
            foreach (int h in new[] { 40, 64, 88 }) d.Add(scene, h);
        return d;
    }

    [Theory]
    [MemberData(nameof(BandCases))]
    public void TdBand_MatchesGoldenAtConfiguredHeights(TdStripScene scene, int bandHeightDip)
    {
        using var bmp = TdStripScenes.Render(scene, 96, bandHeightDip);
        Assert.Equal((int)Math.Round(TdStripScenes.WidthDip * bandHeightDip / 32.0), bmp.Width);
        Assert.Equal((int)Math.Round(bandHeightDip * (TdBand.HeightDip + TdBand.EdgeHeadroomDip) / 32.0) + TdStripScenes.BarDip, bmp.Height); // faixa + folga do portal/núcleo (TD.28c)
        string? failure = GoldenImage.Verify(TdStripScenes.BandGoldenName(scene, bandHeightDip), bmp);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TdStrip_RenderIsDeterministic(TdStripScene scene, int dpi)
    {
        using var a = TdStripScenes.Render(scene, dpi);
        using var b = TdStripScenes.Render(scene, dpi);
        Assert.True(a.Bytes.AsSpan().SequenceEqual(b.Bytes));
    }

    [Fact]
    public void TdScenes_AreDistinctFromEachOther()
    {
        var scenes = Enum.GetValues<TdStripScene>();
        var renders = scenes.Select(s => TdStripScenes.Render(s, 96)).ToArray();
        try
        {
            for (int i = 0; i < renders.Length; i++)
                for (int j = i + 1; j < renders.Length; j++)
                    Assert.False(renders[i].Bytes.AsSpan().SequenceEqual(renders[j].Bytes), $"{scenes[i]} == {scenes[j]}");
        }
        finally { foreach (var r in renders) r.Dispose(); }
    }

    [Fact]
    public void Band_IsTransparentAboveNothing_AndDrawsRailCoreAndPortal()
    {
        var (match, view, now) = TdStripScenes.Build(TdStripScene.PrepRange);
        using var typo = new SideWar.Rendering.Text.Typography();
        using var r = new SideWar.Rendering.Scene.TdBandRenderer(typo);
        r.SetDpi(96);
        using var bmp = new SKBitmap(new SKImageInfo(1200, 46, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        r.Draw(canvas, match, now, view);
        Assert.Equal(0, bmp.GetPixel(600, 5).Alpha); // folga acima da faixa (TD.28c): alfa 0, o clique passa
        Assert.Equal(1, bmp.GetPixel(600, 24).Alpha); // espaço livre: alfa 1/255 (invisível, mas recebe o mouse no modo Interativo; passivo = WS_EX_TRANSPARENT)
        Assert.NotEqual(0, bmp.GetPixel(600, 44).Alpha); // chão
        Assert.True(HasOpaque(bmp, 0, 4, 63, 46) && HasVisible(bmp, 0, 0, 63, 14), "núcleo (sprite core, 42 DIP: passa do topo da faixa)");
        Assert.True(HasOpaque(bmp, 1145, 4, 1200, 46) && HasVisible(bmp, 1145, 0, 1200, 14), "portal (sprite portal, 42 DIP)");
        Assert.True(HasOpaque(bmp, 318, 20, 342, 45), "torre selecionada (sprite tower-bolt)");
        // o alcance selecionado pinta a barra fina do topo sobre a torre selecionada (x = 330)
        Assert.NotEqual(0, bmp.GetPixel(330, 14).Alpha);
        Assert.Equal(1, bmp.GetPixel(700, 14).Alpha);
    }

    /// <summary>Pixel visível (alfa &gt; 8) na região: a folga acima da faixa só tem arte do portal/núcleo.</summary>
    private static bool HasVisible(SKBitmap bmp, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (bmp.GetPixel(x, y).Alpha > 8) return true;
        return false;
    }

    private static bool HasOpaque(SKBitmap bmp, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (bmp.GetPixel(x, y).Alpha == 255) return true;
        return false;
    }

    [Fact]
    public void ArtMap_CoversEveryTdContentId_AndMarksReusedArtAsProvisional()
    {
        var content = TdContentLoader.LoadEmbedded();
        // Ícones das torres Azure são implementados na TD.31d.
        foreach (var t in content.Towers.Where(t => t.Id is not ("functions" or "cdn" or "policy"))) Assert.True(SideWar.Rendering.Scene.TdArtMap.TryTower(t.Id, out _), t.Id);
        foreach (var e in content.Enemies.Enemies) Assert.True(SideWar.Rendering.Scene.TdArtMap.TryEnemy(e.Id, out _), e.Id);
        foreach (var b in content.Enemies.Bosses) Assert.True(SideWar.Rendering.Scene.TdArtMap.TryEnemy(b.Id, out _), b.Id);
        Assert.True(SideWar.Rendering.Scene.TdArtMap.TryTower("balancer", out var bal) && bal.Provisional);
        Assert.True(SideWar.Rendering.Scene.TdArtMap.TryTower("scaleset", out var sc) && sc.Provisional);
        // sem dois inimigos idênticos (sprite + tinta + altura): cada um é distinguível
        var all = SideWar.Rendering.Scene.TdArtMap.Enemies.Select(e => (e.Sprite, e.Tint, e.HeightDip)).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    [Fact]
    public void Draw_DoesNotAllocateInSteadyState()
    {
        var (match, view, now) = TdStripScenes.Build(TdStripScene.Wave);
        using var typo = new SideWar.Rendering.Text.Typography();
        using var r = new SideWar.Rendering.Scene.TdBandRenderer(typo);
        r.SetDpi(96);
        using var bmp = new SKBitmap(new SKImageInfo(1200, 46, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        for (int i = 0; i < 20; i++) r.Draw(canvas, match, now, view); // aquece caches
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) r.Draw(canvas, match, now, view);
        long perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 200;
        Assert.True(perFrame < 512, $"{perFrame} B/frame");
    }
}
