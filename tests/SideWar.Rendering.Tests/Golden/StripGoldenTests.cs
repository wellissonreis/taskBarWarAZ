using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>SDD-22 CA-22.7 / SDD-08 CA-08.3 / SDD-17 RF-17.3: goldens da faixa a 100% e 150% (atlas embutido; strip-fallback usa o PlaceholderAtlas).</summary>
public sealed class StripGoldenTests
{
    public static TheoryData<StripScene, int> Cases()
    {
        var d = new TheoryData<StripScene, int>();
        foreach (var scene in Enum.GetValues<StripScene>())
            foreach (int dpi in new[] { 96, 144 })
                d.Add(scene, dpi);
        return d;
    }

    public static TheoryData<StripScene, int> NoTextCases()
    {
        var d = new TheoryData<StripScene, int>();
        foreach (var scene in Enum.GetValues<StripScene>().Where(StripScenes.HasHotspotText))
            foreach (int dpi in new[] { 96, 144 })
                d.Add(scene, dpi);
        return d;
    }

    /// <summary>
    /// Cenas com texto de hotspot comparam só no SO de referência das goldens (Linux/WSL): a rasterização de texto do Skia
    /// difere entre SOs (SDD-17 RF-17.3). A comparação estrita entre SOs usa as variantes sem texto e as cenas sem hotspot de texto.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Strip_MatchesGolden(StripScene scene, int dpi)
    {
        if (StripScenes.HasHotspotText(scene))
            Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        using var bmp = StripScenes.Render(scene, dpi);
        Assert.Equal(StripScenes.WidthDip * dpi / 96, bmp.Width);
        string? failure = GoldenImage.Verify(StripScenes.GoldenName(scene, dpi), bmp);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [MemberData(nameof(NoTextCases))]
    public void Strip_NoText_MatchesGolden_OnAnyOs(StripScene scene, int dpi)
    {
        using var bmp = StripScenes.Render(scene, dpi, text: false);
        Assert.Equal(StripScenes.WidthDip * dpi / 96, bmp.Width);
        string? failure = GoldenImage.Verify(StripScenes.GoldenName(scene, dpi, text: false), bmp);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [MemberData(nameof(NoTextCases))]
    public void Strip_NoText_DiffersFromTextVariant(StripScene scene, int dpi)
    {
        using var with = StripScenes.Render(scene, dpi);
        using var without = StripScenes.Render(scene, dpi, text: false);
        Assert.False(with.Bytes.AsSpan().SequenceEqual(without.Bytes));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Strip_RenderIsDeterministic(StripScene scene, int dpi)
    {
        using var a = StripScenes.Render(scene, dpi);
        using var b = StripScenes.Render(scene, dpi);
        Assert.True(a.Bytes.AsSpan().SequenceEqual(b.Bytes));
    }

    [Fact]
    public void Scenes_AreDistinctFromEachOther()
    {
        using var present = StripScenes.Render(StripScene.Present, 96);
        foreach (var scene in new[] { StripScene.Away, StripScene.Peak, StripScene.Boss, StripScene.Fallback, StripScene.EmptySlots })
        {
            using var other = StripScenes.Render(scene, 96);
            Assert.False(present.Bytes.AsSpan().SequenceEqual(other.Bytes), scene.ToString());
        }
    }
}
