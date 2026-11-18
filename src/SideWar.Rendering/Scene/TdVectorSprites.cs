using SideWar.Rendering.Panel;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Td;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>Silhuetas vetoriais cozidas uma vez por DPI, compartilhadas pelo mundo e pelos chips.</summary>
internal static class TdVectorSprites
{
    private static (string Path, TdIcon Icon, SKColor Color) Tower(string id) => id switch
    {
        "functions" => ("M5 27 L7 18 L2 15 L8 3 L13 8 L18 2 L23 15 L19 18 L22 27 Z", TdIcon.Functions, Palette.Ambar),
        "cdn" => ("M4 27 L7 23 L5 20 L2 15 L3 9 L7 4 L13 2 L19 4 L23 9 L24 15 L21 20 L19 23 L22 27 Z", TdIcon.Cdn, Palette.Gelo),
        "policy" => ("M4 27 L7 22 L4 18 L3 5 L13 1 L23 5 L22 18 L19 22 L22 27 Z", TdIcon.Policy, Palette.Menta),
        "cosmos" => ("M3 27 L7 23 L9 17 L3 11 L5 5 L10 7 L13 2 L16 7 L21 5 L23 11 L17 17 L19 23 L23 27 Z", TdIcon.Cosmos, Palette.Td.TintViolet),
        "sentinel" => ("M3 27 L7 23 L8 18 L2 12 L5 7 L11 5 L13 1 L15 5 L21 7 L24 12 L18 18 L19 23 L23 27 Z", TdIcon.Sentinel, Palette.Gelo),
        "queue" => ("M2 27 L4 23 L4 5 L8 5 L8 2 L20 2 L20 5 L22 5 L22 23 L24 27 Z", TdIcon.Queue, Palette.Ambar),
        "pipeline" => ("M2 27 L2 18 L7 18 L7 13 L12 13 L12 8 L17 8 L17 2 L24 2 L24 27 Z", TdIcon.Pipeline, Palette.Menta),
        "vault" => ("M3 27 L3 6 L6 3 L20 3 L23 6 L23 27 L19 27 L19 25 L7 25 L7 27 Z", TdIcon.Vault, Palette.Ambar),
        "backup" => ("M3 27 L6 22 L3 17 L3 9 L7 4 L13 2 L19 4 L22 9 L22 17 L19 22 L23 27 Z", TdIcon.Backup, Palette.Menta),
        "aks" => ("M2 27 L5 22 L3 17 L3 8 L13 2 L23 8 L23 17 L21 22 L24 27 Z", TdIcon.Aks, Palette.Gelo),
        _ => default,
    };

    public static bool IsTower(string id) => Tower(id).Path is not null;
    public static bool IsEnemy(string id) => id is "fork" or "trojan" or "phishing";

    public static SKImage Bake(string id, float height, bool enemy)
    {
        int h = Math.Max(1, (int)MathF.Round(height, MidpointRounding.AwayFromZero));
        int w = Math.Max(1, (int)MathF.Round(h * (enemy ? 1f : 26f / 28f), MidpointRounding.AwayFromZero));
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        c.Scale(w / 26f, h / 28f);
        using var fill = new SKPaint { IsAntialias = true, Color = Palette.BgAbismo };
        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, StrokeJoin = SKStrokeJoin.Round };
        string shape;
        TdIcon icon = TdIcon.None;
        SKColor color;
        if (enemy)
        {
            (shape, color) = id switch
            {
                "fork" => ("M2 23 L4 13 L1 9 L5 6 L9 9 L13 4 L17 9 L21 6 L25 9 L22 13 L24 23 L18 25 L13 21 L8 25 Z", Palette.Td.TintViolet),
                "trojan" => ("M3 9 L13 2 L23 9 L21 20 L13 27 L5 20 Z", Palette.Td.TintAmber),
                _ => ("M2 8 L5 4 L21 4 L24 8 L22 20 L17 23 L13 27 L9 23 L4 20 Z", Palette.Td.TintCyan),
            };
        }
        else (shape, icon, color) = Tower(id);
        using var path = SKPath.ParseSvgPathData(shape)!;
        c.DrawPath(path, fill);
        line.Color = color;
        c.DrawPath(path, line);
        if (!enemy)
        {
            TdIconPainter.Draw(c, icon, 5, 6, 16, color, Palette.BgAbismo);
            c.DrawLine(7, 25, 19, 25, line);
        }
        else
        {
            line.StrokeWidth = 2;
            if (id == "fork") { c.DrawLine(13, 18, 13, 11, line); c.DrawLine(13, 13, 8, 9, line); c.DrawLine(13, 13, 18, 9, line); }
            else if (id == "trojan") { c.DrawRect(8, 10, 10, 9, line); c.DrawLine(8, 10, 13, 15, line); c.DrawLine(13, 15, 18, 10, line); }
            else { c.DrawLine(6, 10, 13, 15, line); c.DrawLine(13, 15, 20, 10, line); c.DrawLine(6, 10, 20, 10, line); }
        }
        var image = SKImage.FromBitmap(bmp);
        var owned = image.ToRasterImage(ensurePixelData: true);
        if (!ReferenceEquals(image, owned)) image.Dispose();
        return owned;
    }
}
