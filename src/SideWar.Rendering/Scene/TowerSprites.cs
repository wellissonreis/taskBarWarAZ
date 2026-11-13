using SideWar.Core.Simulation;
using SideWar.Rendering.Sprites;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>
/// Sprites de torre de 36 DIP (barra) ou 44 DIP (faixa) com o brilho do canvas
/// (<c>drop-shadow(0 0 3px rgba(76,194,255,.55))</c>) <b>pré-cozido</b> por escala e hospedeiro (DP-33.9): nenhum filtro por frame.
/// Cada imagem tem uma margem de <see cref="PadPx"/> pixels para o brilho.
/// </summary>
internal sealed class TowerSprites : IDisposable
{
    private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private static readonly SKColor GlowColor = new(76, 194, 255, 140); // .55 × 255

    private readonly SKImage?[] _images = new SKImage?[8]; // [host × 4 + tipo]
    private float _scale;

    public int PadPx { get; private set; }

    public void Reset(float scale)
    {
        Release();
        _scale = scale;
        // o blur de 3 px do CSS tem sigma 1,5; a margem cobre 3 sigmas
        PadPx = (int)MathF.Ceiling(SceneGeometry.GlowBlurDip * scale * 1.5f) + 1;
    }

    public SKImage? Get(SpriteAtlas atlas, TowerType type, TowerHost host)
    {
        int idx = (host == TowerHost.Band ? 4 : 0) + (int)TypeIndex(type);
        return _images[idx] ??= Bake(atlas, type, host);
    }

    private static int TypeIndex(TowerType type) => type switch
    {
        TowerType.Frost => 1,
        TowerType.Cannon => 2,
        TowerType.ScaleSet => 3,
        _ => 0,
    };

    private SKImage Bake(SpriteAtlas atlas, TowerType type, TowerHost host)
    {
        float w = SceneGeometry.TowerWidth(type, host), h = SceneGeometry.TowerHeight(host);
        int wp = Math.Max(1, (int)MathF.Round(w * _scale, MidpointRounding.AwayFromZero));
        int hp = Math.Max(1, (int)MathF.Round(h * _scale, MidpointRounding.AwayFromZero));
        using var bmp = new SKBitmap(new SKImageInfo(wp + 2 * PadPx, hp + 2 * PadPx, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            float sigma = SceneGeometry.GlowBlurDip * _scale / 2f;
            using var filter = SKImageFilter.CreateDropShadow(0, 0, sigma, sigma, GlowColor);
            using var paint = new SKPaint { IsAntialias = false, ImageFilter = filter };
            using var master = SKImage.FromBitmap(atlas.Master);
            var r = atlas.GetRect(SceneGeometry.TowerSprite(type));
            canvas.DrawImage(master, SKRect.Create(r.Left, r.Top, r.Width, r.Height),
                SKRect.Create(PadPx, PadPx, wp, hp), Linear, paint);
        }
        var image = SKImage.FromBitmap(bmp);
        // FromBitmap pode compartilhar os pixels do bitmap (que é descartado aqui): copiar para ser dono deles.
        var owned = image.ToRasterImage(ensurePixelData: true);
        if (!ReferenceEquals(owned, image)) image.Dispose();
        return owned;
    }

    public void Release()
    {
        for (int i = 0; i < _images.Length; i++)
        {
            _images[i]?.Dispose();
            _images[i] = null;
        }
    }

    public void Dispose() => Release();
}
