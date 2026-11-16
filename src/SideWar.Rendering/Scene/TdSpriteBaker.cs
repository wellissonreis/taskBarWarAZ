using SideWar.Rendering.Sprites;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>
/// Pré-escala (uma vez por DPI/conteúdo, nunca por frame) as sprites do design do atlas mestre para o tamanho físico final, com a mesma
/// filtragem do <see cref="ScaledSpriteCache"/> (linear + mipmap) e tinta opcional cozida (SrcATop). Mantém a proporção.
/// </summary>
internal static class TdSpriteBaker
{
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>Altura física fixa; a largura segue a proporção do atlas.</summary>
    public static SKImage ByHeight(SpriteAtlas atlas, SpriteId id, float heightPx, SKColor tint = default, byte tintAlpha = 0)
    {
        var r = atlas.GetRect(id);
        int h = Math.Max(1, (int)MathF.Round(heightPx, MidpointRounding.AwayFromZero));
        int w = Math.Max(1, (int)MathF.Round(r.Width * h / (float)r.Height, MidpointRounding.AwayFromZero));
        return Bake(atlas, r, w, h, tint, tintAlpha);
    }

    /// <summary>Largura física fixa; a altura segue a proporção do atlas.</summary>
    public static SKImage ByWidth(SpriteAtlas atlas, SpriteId id, float widthPx)
    {
        var r = atlas.GetRect(id);
        int w = Math.Max(1, (int)MathF.Round(widthPx, MidpointRounding.AwayFromZero));
        int h = Math.Max(1, (int)MathF.Round(r.Height * w / (float)r.Width, MidpointRounding.AwayFromZero));
        return Bake(atlas, r, w, h, default, 0);
    }

    private static SKImage Bake(SpriteAtlas atlas, SKRectI src, int w, int h, SKColor tint, byte tintAlpha)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = false };
            using var filter = tintAlpha > 0 ? SKColorFilter.CreateBlendMode(tint.WithAlpha(tintAlpha), SKBlendMode.SrcATop) : null;
            paint.ColorFilter = filter;
            using var master = SKImage.FromBitmap(atlas.Master);
            canvas.DrawImage(master, SKRect.Create(src.Left, src.Top, src.Width, src.Height), SKRect.Create(0, 0, w, h), Sampling, paint);
        }
        var image = SKImage.FromBitmap(bmp);
        // FromBitmap pode compartilhar os pixels do bitmap (descartado aqui): copiar para ser dono deles.
        var owned = image.ToRasterImage(ensurePixelData: true);
        if (!ReferenceEquals(owned, image)) image.Dispose();
        return owned;
    }
}
