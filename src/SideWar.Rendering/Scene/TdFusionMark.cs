using SideWar.Rendering.Panel;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Td;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>
/// TD.37b: selo da componente-módulo de uma torre fundida. Silhueta da base + pequeno selo vetorial do módulo (ícone + anel colorido: a identidade
/// não depende só da cor). Desenhado por código com a <see cref="Palette"/>; nada de bitmap novo.
/// </summary>
internal static class TdFusionMark
{
    public static TdIcon IconFor(string moduleId) => moduleId switch
    {
        "bolt" => TdIcon.Bolt, "frost" => TdIcon.Frost, "cannon" => TdIcon.Target, "balancer" => TdIcon.Firewall, "scaleset" => TdIcon.Scale,
        "functions" => TdIcon.Functions, "cosmos" => TdIcon.Cosmos, "sentinel" => TdIcon.Sentinel, "vault" => TdIcon.Vault, "backup" => TdIcon.Backup,
        "cdn" => TdIcon.Cdn, "queue" => TdIcon.Queue, "policy" => TdIcon.Policy, "aks" => TdIcon.Aks, "pipeline" => TdIcon.Pipeline,
        _ => TdIcon.Star,
    };

    public static SKColor ColorFor(string moduleId) => moduleId switch
    {
        "bolt" => Palette.Td.Bolt, "frost" => Palette.Td.Frost, "cannon" => Palette.Td.Cannon,
        "policy" or "backup" or "pipeline" => Palette.Menta,
        "vault" or "queue" => Palette.Ambar,
        _ => Palette.Td.TintViolet,
    };

    /// <summary>Selo circular de diâmetro <paramref name="size"/> (px) com o canto superior esquerdo em (x, y).</summary>
    public static void Draw(SKCanvas c, string moduleId, float x, float y, float size, byte alpha = 255)
    {
        if (size < 4f) return;
        var color = ColorFor(moduleId).WithAlpha(alpha);
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Palette.BgAbismo.WithAlpha(alpha) };
        using var ring = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1f, size / 10f), Color = color };
        float r = size / 2f;
        c.DrawCircle(x + r, y + r, r - ring.StrokeWidth / 2f, fill);
        c.DrawCircle(x + r, y + r, r - ring.StrokeWidth / 2f, ring);
        float inner = size * 0.62f;
        TdIconPainter.Draw(c, IconFor(moduleId), x + (size - inner) / 2f, y + (size - inner) / 2f, inner, color, Palette.BgAbismo);
    }
}
