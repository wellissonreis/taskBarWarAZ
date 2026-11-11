using SideWar.Runtime.Panel;

namespace SideWar.Runtime.Tests.Panel;

/// <summary>Métricas falsas: Mono avança 0,6 em; Silkscreen 0,75 em; linha = 1,32 em; ascendente = 1,02 em.</summary>
internal sealed class FakeTextMetrics : IPanelTextMetrics
{
    public float LineHeight(PanelFont face, float sizeDip) => MathF.Round(sizeDip * 1.32f);
    public float Ascent(PanelFont face, float sizeDip) => MathF.Round(sizeDip * 1.02f);
    public float Width(PanelFont face, float sizeDip, string text, float letterSpacing = 0f) =>
        text.Length * (sizeDip * (face == PanelFont.JetBrainsMono ? 0.6f : 0.75f) + letterSpacing);
}
