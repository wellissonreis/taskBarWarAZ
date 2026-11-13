using Xunit;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>SDD-23 CA-23.4 / SDD-24 CA-24.13 / SDD-17 RF-17.3: goldens do widget, do painel (4 abas) e dos cards a 100% e 150%.</summary>
public sealed class HudGoldenTests
{
    public static TheoryData<HudScene, int> Cases()
    {
        var d = new TheoryData<HudScene, int>();
        foreach (var scene in Enum.GetValues<HudScene>())
            foreach (int dpi in new[] { 96, 144 })
                d.Add(scene, dpi);
        return d;
    }

    /// <summary>Com texto: só no SO de referência (Linux/WSL), pois a rasterização de texto do Skia difere entre SOs (RF-17.3).</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Hud_MatchesGolden_LinuxOnly(HudScene scene, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        using var bmp = HudScenes.Render(scene, dpi);
        string? failure = GoldenImage.Verify(HudScenes.GoldenName(scene, dpi), bmp);
        Assert.True(failure is null, failure);
    }

    /// <summary>Variantes com TextEnabled=false: layout idêntico, sem glifos; comparam nos dois SOs nas cenas de layout estável (NoTextIsCrossOs).</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Hud_NoText_MatchesGolden_OnAnyOs(HudScene scene, int dpi)
    {
        if (!HudScenes.NoTextIsCrossOs(scene))
            Assert.SkipUnless(OperatingSystem.IsLinux(), "layout depende da medição de texto do SO (1–3 px); ver HudScenes.NoTextIsCrossOs");
        using var bmp = HudScenes.Render(scene, dpi, text: false);
        string? failure = GoldenImage.Verify(HudScenes.GoldenName(scene, dpi, text: false), bmp);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Hud_NoText_DiffersFromTextVariant_SameSize(HudScene scene, int dpi)
    {
        using var with = HudScenes.Render(scene, dpi);
        using var without = HudScenes.Render(scene, dpi, text: false);
        Assert.Equal(with.Width, without.Width);
        Assert.Equal(with.Height, without.Height);
        Assert.False(with.Bytes.AsSpan().SequenceEqual(without.Bytes));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Hud_RenderIsDeterministic(HudScene scene, int dpi)
    {
        using var a = HudScenes.Render(scene, dpi, text: false);
        using var b = HudScenes.Render(scene, dpi, text: false);
        Assert.True(a.Bytes.AsSpan().SequenceEqual(b.Bytes));
    }

    [Fact]
    public void Scenes_AreDistinctFromEachOther()
    {
        var seen = new Dictionary<string, HudScene>();
        foreach (var scene in Enum.GetValues<HudScene>())
        {
            using var bmp = HudScenes.Render(scene, 96);
            string key = bmp.Width + "x" + bmp.Height + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bmp.Bytes));
            Assert.False(seen.TryGetValue(key, out var other), $"{scene} igual a {other}");
            seen[key] = scene;
        }
    }
}
