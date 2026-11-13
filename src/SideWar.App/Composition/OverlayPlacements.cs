using SideWar.Platform.Windows.Threading;
using SideWar.Runtime.Geometry;

namespace SideWar.App.Composition;

/// <summary>Posição das janelas SDD-24 (RF-24.2) sobre a faixa atual do dock; compartilhada pelo card offline, pelo card do chefe e pelo painel.</summary>
internal static class OverlayPlacements
{
    /// <summary>
    /// Posição pela <see cref="PanelGeometry"/> e a faixa do dock. O card de pergunta mantém a base (<c>stripTop − 8</c>) e cresce
    /// para cima com <paramref name="heightDip"/> (explicação, RF-24.9). Falso se o dock ainda não tem layout.
    /// </summary>
    public static bool TryPlace(GameThread game, OverlayKind kind, float heightDip, out OverlayPlacement placement)
    {
        placement = default;
        if (game.Dock.Current is not { } current) return false;
        placement = PanelGeometry.Compute(kind, current.Monitor, current.Scene);
        if (kind == OverlayKind.QuestionCard && heightDip > 0)
        {
            double s = placement.Scale;
            int boxH = (int)Math.Round(heightDip * s, MidpointRounding.AwayFromZero);
            int bottom = placement.Bounds.Top + placement.Box.Height;
            int top = Math.Max(current.Monitor.WorkArea.Top, bottom - boxH);
            placement = placement with
            {
                Bounds = new PixelRect(placement.Bounds.Left, top, placement.Bounds.Right, bottom + placement.ShadowPx),
                Box = new PixelSize(placement.Box.Width, bottom - top),
            };
        }
        return true;
    }
}
