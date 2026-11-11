using SideWar.Rendering.Text;
using SideWar.Runtime.Panel;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>
/// <see cref="IPanelTextMetrics"/> sobre as fontes reais (SDD-20): mede em DIP usando a fonte a 96 dpi
/// (a escala fracionária do layout é refeita a cada mudança de escala). Uso na T-Game.
/// </summary>
public sealed class PanelTextMetricsSkia(Typography typography) : IPanelTextMetrics
{
    private readonly Typography _typography = typography ?? throw new ArgumentNullException(nameof(typography));

    public static FontRole Role(PanelFont font) => font == PanelFont.Silkscreen ? FontRole.SilkscreenRegular : FontRole.JetBrainsMonoRegular;

    public float Width(PanelFont font, float sizeDip, string text, float letterSpacing = 0f)
    {
        ArgumentNullException.ThrowIfNull(text);
        var f = _typography.GetFont(Role(font), sizeDip, 96);
        return f.MeasureText(text) + letterSpacing * text.Length;
    }

    public float LineHeight(PanelFont font, float sizeDip)
    {
        var f = _typography.GetFont(Role(font), sizeDip, 96);
        var m = f.Metrics;
        return m.Descent - m.Ascent + m.Leading;
    }

    public float Ascent(PanelFont font, float sizeDip) =>
        (float)Math.Round(-_typography.GetFont(Role(font), sizeDip, 96).Metrics.Ascent);
}
