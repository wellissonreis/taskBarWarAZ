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
        "dns" => ("M3 27 L6 22 L5 8 L13 2 L21 8 L20 22 L23 27 Z", TdIcon.Cdn, Palette.Gelo),
        "frontdoor" => ("M2 27 L4 5 L9 2 L21 5 L24 27 L18 27 L18 12 L8 12 L8 27 Z", TdIcon.Policy, Palette.Menta),
        "shield" => ("M4 27 L7 22 L3 5 L13 1 L23 5 L19 22 L22 27 Z", TdIcon.Policy, Palette.Gelo),
        "waf" => ("M2 27 L5 20 L3 8 L13 2 L23 8 L21 20 L24 27 Z", TdIcon.Policy, Palette.Ambar),
        "eventgrid" => ("M2 27 L8 21 L4 13 L8 4 L18 4 L22 13 L18 21 L24 27 Z", TdIcon.Cosmos, Palette.Td.TintViolet),
        "bastion" => ("M2 27 L4 9 L7 9 L7 4 L10 4 L10 1 L16 1 L16 4 L19 4 L19 9 L22 9 L24 27 Z", TdIcon.Backup, Palette.Menta),
        "firewall" => ("M2 27 L3 9 L7 9 L7 5 L11 5 L11 2 L15 2 L15 5 L19 5 L19 9 L23 9 L24 27 Z", TdIcon.Pipeline, Palette.Ambar),
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
    public static bool IsEnemy(string id) => id is "fork" or "trojan" or "phishing" or "synflood" or "scanner" or "slowloris" or "fragment" or "dnstunnel" or "encrypted";

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
                "synflood" => ("M2 10 L8 3 L14 8 L21 4 L24 13 L19 22 L12 26 L5 22 Z", Palette.Td.TintCyan),
                "scanner" => ("M2 14 L8 6 L19 4 L24 14 L19 24 L8 22 Z", Palette.Td.TintAmber),
                "slowloris" => ("M3 10 L8 4 L18 4 L23 10 L23 20 L18 25 L8 25 L3 20 Z", Palette.Menta),
                "fragment" => ("M3 4 L12 3 L15 9 L23 7 L22 24 L14 26 L11 20 L3 23 Z", Palette.Td.TintViolet),
                "dnstunnel" => ("M3 6 L13 2 L23 6 L23 22 L13 26 L3 22 Z", Palette.Td.TintCyan),
                "encrypted" => ("M13 2 L23 7 L22 19 L13 27 L4 19 L3 7 Z", Palette.Td.TintAmber),
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
            if (id == "synflood") { c.DrawRect(6, 9, 5, 5, line); c.DrawRect(14, 14, 5, 5, line); }
            else if (id == "scanner") { c.DrawCircle(12, 14, 5, line); c.DrawLine(16, 18, 20, 22, line); }
            else if (id == "slowloris") { c.DrawCircle(13, 14, 7, line); c.DrawLine(13, 14, 13, 9, line); c.DrawLine(13, 14, 17, 16, line); }
            else if (id == "fragment") { c.DrawLine(13, 4, 9, 12, line); c.DrawLine(9, 12, 16, 15, line); c.DrawLine(16, 15, 12, 24, line); }
            else if (id == "dnstunnel") { c.DrawOval(new SKRect(7, 7, 19, 21), line); c.DrawLine(3, 14, 23, 14, line); }
            else if (id == "encrypted") { c.DrawRect(8, 13, 10, 8, line); c.DrawArc(new SKRect(10, 6, 16, 16), 180, 180, false, line); }
            else if (id == "fork") { c.DrawLine(13, 18, 13, 11, line); c.DrawLine(13, 13, 8, 9, line); c.DrawLine(13, 13, 18, 9, line); }
            else if (id == "trojan") { c.DrawRect(8, 10, 10, 9, line); c.DrawLine(8, 10, 13, 15, line); c.DrawLine(13, 15, 18, 10, line); }
            else { c.DrawLine(6, 10, 13, 15, line); c.DrawLine(13, 15, 20, 10, line); c.DrawLine(6, 10, 20, 10, line); }
        }
        var image = SKImage.FromBitmap(bmp);
        var owned = image.ToRasterImage(ensurePixelData: true);
        if (!ReferenceEquals(image, owned)) image.Dispose();
        return owned;
    }
}
