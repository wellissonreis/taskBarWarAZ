using SkiaSharp;

namespace SideWar.Rendering.Text;

/// <summary>Cobertura de glifos pelo cmap (SDD-20 RF-20.3 item 7, T-20.4).</summary>
public static class GlyphCoverage
{
    /// <summary>Code points do texto sem glifo na face (espacos e controles ignorados).</summary>
    public static IReadOnlyList<int> Missing(SKTypeface face, string text)
    {
        using var font = new SKFont(face, 12);
        var missing = new List<int>();
        var seen = new HashSet<int>();
        for (int i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            int cp = char.ConvertToUtf32(text, i);
            if (cp <= 0x20 || !seen.Add(cp)) continue;
            if (font.GetGlyph(cp) == 0) missing.Add(cp);
        }
        return missing;
    }
}
