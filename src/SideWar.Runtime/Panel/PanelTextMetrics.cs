namespace SideWar.Runtime.Panel;

/// <summary>Famílias de fonte do painel (SDD-20 RF-20.2), sem dependência de Skia.</summary>
public enum PanelFont { Silkscreen, JetBrainsMono }

/// <summary>
/// Medidas de texto em DIP para o layout puro (SDD-24 RF-24.3: "larguras de texto medidas"). Implementada no Rendering
/// por <c>SKFont</c>; os testes usam uma fonte fixa. <paramref name="letterSpacing"/> é somado após cada caractere.
/// </summary>
public interface IPanelTextMetrics
{
    float Width(PanelFont font, float sizeDip, string text, float letterSpacing = 0f);

    /// <summary>Altura de linha "normal" (ascent + descent + leading) do tamanho dado.</summary>
    float LineHeight(PanelFont font, float sizeDip);

    /// <summary>
    /// Distância do topo da caixa de linha até a linha de base (T5.5). Membro padrão para não quebrar implementações
    /// existentes (T5.4): aproxima por 80% da altura de linha.
    /// </summary>
    float Ascent(PanelFont font, float sizeDip) => LineHeight(font, sizeDip) * 0.8f;
}

/// <summary>Quebra de linha por palavras (largura em DIP), usada por descrição e títulos longos.</summary>
public static class PanelTextWrap
{
    public static string[] Wrap(IPanelTextMetrics metrics, PanelFont font, float sizeDip, string text, float maxWidth)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return [string.Empty];
        var words = text.Split(' ');
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length == 0 || metrics.Width(font, sizeDip, candidate) <= maxWidth) current = candidate;
            else
            {
                lines.Add(current);
                current = word;
            }
        }
        lines.Add(current);
        return [.. lines];
    }
}
