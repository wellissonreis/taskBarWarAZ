namespace SideWar.Runtime.Panel;

/// <summary>Quebra de linha gulosa e corte com reticências (SDD-24 RF-24.9 "texto longo"). Puro.</summary>
public static class TextFit
{
    public const string Ellipsis = "…";

    /// <summary>Corta <paramref name="text"/> para caber em <paramref name="maxWidth"/>, terminando em "…".</summary>
    public static string Ellipsize(string text, float maxWidth, PanelFont face, float size, IPanelTextMetrics m, float ls = 0f)
    {
        if (m.Width(face, size, text, ls) <= maxWidth) return text;
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (m.Width(face, size, text[..mid].TrimEnd() + Ellipsis, ls) <= maxWidth) lo = mid; else hi = mid - 1;
        }
        return text[..lo].TrimEnd() + Ellipsis;
    }

    /// <summary>
    /// Quebra por palavras em <paramref name="maxWidth"/>. Com <paramref name="maxLines"/> &gt; 0, o excedente é
    /// cortado e a última linha termina em "…".
    /// </summary>
    public static string[] Wrap(string text, float maxWidth, PanelFont face, float size, IPanelTextMetrics m, int maxLines = 0)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length == 0 || m.Width(face, size, candidate) <= maxWidth) { current = candidate; continue; }
            lines.Add(current);
            current = word;
        }
        if (current.Length > 0) lines.Add(current);
        if (lines.Count == 0) return [""];
        if (maxLines > 0 && lines.Count > maxLines)
        {
            var rest = string.Join(" ", lines.Skip(maxLines - 1));
            lines.RemoveRange(maxLines - 1, lines.Count - (maxLines - 1));
            lines.Add(Ellipsize(rest, maxWidth, face, size, m));
        }
        return [.. lines];
    }
}
