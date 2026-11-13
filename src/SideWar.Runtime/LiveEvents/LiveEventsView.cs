using SideWar.Core.Simulation;

namespace SideWar.Runtime.LiveEvents;

/// <summary>Banner único em exibição (SDD-26 RF-26.12): pico em oferta, defesa escalada ou chefe.</summary>
public enum LiveBannerKind { Peak, PeakScaled, Boss }

/// <summary>Banner: <see cref="ChipSeconds"/> = tempo restante arredondado para cima (<c>m:ss</c> no renderer); <see cref="Clickable"/> = false em "defesa escalada".</summary>
public sealed record LiveBannerView(LiveBannerKind Kind, string Text, int ChipSeconds, bool Clickable);

/// <summary>Caixa de suprimentos ativa: x em DIP (do estado, RF-26.11) e segundos restantes.</summary>
public sealed record LiveCrateView(int X, int RemainingSeconds);

/// <summary>Chip <c>×3 0:59</c> do widget quando o banner do chefe ocupa o slot com o bônus ativo (RF-26.3, DP-26.5).</summary>
public sealed record LiveChipView(string Text, int Seconds);

/// <summary>
/// Visão imutável dos eventos ao vivo. Igualdade por valor: o host só redesenha quando a visão muda (no máximo 1×/s
/// pelo chip). Todo <c>null</c> = nada a mostrar (Ausente/Offline ou sem evento).
/// </summary>
public sealed record LiveEventsView(LiveBannerView? Banner, LiveCrateView? Crate, LiveChipView? WidgetChip)
{
    public static LiveEventsView Empty { get; } = new(null, null, null);
}

/// <summary>Alvo de um clique em banner/caixa (<c>HotspotWindow.Clicked</c>).</summary>
public enum LiveClickTarget { Banner, Crate }
