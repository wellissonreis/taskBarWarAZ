using SideWar.Rendering.Theme;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>Estado visual da linha de borda (SDD-22 RF-22.6). Pico usa a cor de presente.</summary>
public enum StripEdgeMode
{
    Present,
    Away,
    Boss,
}

/// <summary>Linha de borda de estado: 2 DIP no vão y ∈ [102, 104) (ADR-034), sob os sprites (RF-22.4 item 1).</summary>
public static class StripEdge
{
    public const float HeightDip = 2f;

    public static SKColor Color(StripEdgeMode mode) => mode switch
    {
        StripEdgeMode.Away => Palette.BordaAusente,
        StripEdgeMode.Boss => Palette.BordaChefe,
        _ => Palette.BordaPresente,
    };

    /// <summary>Linhas de pixel ocupadas: <c>[round(102 × scale), round(104 × scale))</c>.</summary>
    public static (int Top, int Bottom) Rows(float scale) =>
        ((int)MathF.Round(SceneGeometry.StatusLineTop * scale, MidpointRounding.AwayFromZero),
         (int)MathF.Round((SceneGeometry.StatusLineTop + SceneGeometry.StatusLineHeight) * scale, MidpointRounding.AwayFromZero));

    /// <summary>Desenha a linha de borda na largura <paramref name="widthPx"/>; não altera o estado do canvas.</summary>
    public static void Draw(SKCanvas canvas, SKPaint fill, float scale, int widthPx, StripEdgeMode mode)
    {
        var (top, bottom) = Rows(scale);
        fill.Color = Color(mode);
        canvas.DrawRect(0, top, widthPx, bottom - top, fill);
    }
}
