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

    [Theory]
    [MemberData(nameof(Cases))]
    public void Strip_MatchesGolden(StripScene scene, int dpi)
    {
        using var bmp = StripScenes.Render(scene, dpi);
        Assert.Equal(StripScenes.WidthDip * dpi / 96, bmp.Width);
        string? failure = GoldenImage.Verify(StripScenes.GoldenName(scene, dpi), bmp);
        Assert.True(failure is null, failure);
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
