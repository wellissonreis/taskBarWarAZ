using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>
/// Folha de animação pré-escalada (uma vez por DPI/escala da faixa, nunca por quadro): <see cref="Frames"/> quadros lado a lado. A caixa opaca
/// (união dos quadros da folha de referência) ancora o desenho: base e centro horizontal da caixa vão ao ponto pedido.
/// Pixel-art: ampliação com vizinho mais próximo; redução com filtro linear + mipmap (vizinho mais próximo descartaria pixels).
/// </summary>
internal sealed class TdAnimStrip : IDisposable
{
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest);
    private static readonly SKSamplingOptions Down = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    private static readonly Dictionary<string, SKBitmap> Decoded = [];

    private readonly SKImage _image;

    private TdAnimStrip(SKImage image, int frames, int frameW, int frameH, int anchorX, int anchorY, int boxW, int boxH)
    {
        _image = image;
        Frames = frames;
        FrameW = frameW;
        FrameH = frameH;
        AnchorX = anchorX;
        AnchorY = anchorY;
        BoxW = boxW;
        BoxH = boxH;
    }

    public int Frames { get; }
    public int FrameW { get; }
    public int FrameH { get; }

    /// <summary>Centro horizontal e base da caixa opaca, em pixels físicos dentro do quadro.</summary>
    public int AnchorX { get; }
    public int AnchorY { get; }
    public int BoxW { get; }
    public int BoxH { get; }

    /// <summary>Decodifica (uma vez por processo) a folha embutida.</summary>
    public static SKBitmap Sheet(string file)
    {
        lock (Decoded)
        {
            if (Decoded.TryGetValue(file, out var cached)) return cached;
            using var s = typeof(TdAnimStrip).Assembly.GetManifestResourceStream(TdAnimArt.Resource(file))
                ?? throw new InvalidOperationException("Recurso de animação ausente: " + file);
            var bmp = SKBitmap.Decode(s) ?? throw new InvalidOperationException("PNG inválido: " + file);
            Decoded[file] = bmp;
            return bmp;
        }
    }

    /// <summary>Caixa opaca (alfa &gt; 0) da união dos quadros, em pixels da folha.</summary>
    public static SKRectI Bounds(string file, int frames)
    {
        var bmp = Sheet(file);
        int fw = bmp.Width / frames;
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y).Alpha == 0) continue;
                int fx = x % fw;
                if (fx < l) l = fx;
                if (fx > r) r = fx;
                if (y < t) t = y;
                if (y > b) b = y;
            }
        }
        return r < 0 ? new SKRectI(0, 0, fw, bmp.Height) : new SKRectI(l, t, r + 1, b + 1);
    }

    /// <summary>Escala (px físico por px da folha) que leva a caixa de <paramref name="boundsFile"/> à altura física dada.</summary>
    public static float ScaleForBoxHeight(string boundsFile, int frames, float heightPx) => heightPx / Bounds(boundsFile, frames).Height;

    /// <summary>Cozinha a folha na escala dada, ancorada pela caixa de <paramref name="boundsFile"/> (mesma escala/âncora para parada e ataque).</summary>
    public static TdAnimStrip Bake(string file, int frames, float scale, string boundsFile, int boundsFrames, bool centered = false)
    {
        var sheet = Sheet(file);
        int fw = sheet.Width / frames, fh = sheet.Height;
        int w = Math.Max(1, (int)MathF.Round(fw * scale, MidpointRounding.AwayFromZero));
        int h = Math.Max(1, (int)MathF.Round(fh * scale, MidpointRounding.AwayFromZero));
        var sampling = scale >= 1f ? Nearest : Down;
        using var bmp = new SKBitmap(new SKImageInfo(w * frames, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = false };
            for (int i = 0; i < frames; i++)
            {
                var sub = new SKBitmap();
                if (!sheet.ExtractSubset(sub, new SKRectI(i * fw, 0, (i + 1) * fw, fh))) throw new InvalidOperationException("Quadro fora da folha: " + file);
                using (sub)
                using (var img = SKImage.FromBitmap(sub))
                    canvas.DrawImage(img, SKRect.Create(i * w, 0, w, h), sampling, paint);
            }
        }
        var image = SKImage.FromBitmap(bmp);
        var owned = image.ToRasterImage(ensurePixelData: true);
        if (!ReferenceEquals(owned, image)) image.Dispose();

        int ax, ay, bw, bh;
        if (centered) { ax = w / 2; ay = h / 2; bw = w; bh = h; }
        else
        {
            var box = Bounds(boundsFile, boundsFrames);
            ax = (int)MathF.Round((box.Left + box.Right) / 2f * scale, MidpointRounding.AwayFromZero);
            ay = (int)MathF.Round(box.Bottom * scale, MidpointRounding.AwayFromZero);
            bw = Math.Max(1, (int)MathF.Round(box.Width * scale, MidpointRounding.AwayFromZero));
            bh = Math.Max(1, (int)MathF.Round(box.Height * scale, MidpointRounding.AwayFromZero));
        }
        return new TdAnimStrip(owned, frames, w, h, ax, ay, bw, bh);
    }

    /// <summary>
    /// Ícone estático da torre (quadro 0 da parada recortado na caixa opaca) com a caixa na altura física dada: chips do seletor, painel e fantasma de colocação.
    /// O chamador é dono da imagem.
    /// </summary>
    public static SKImage IdleIcon(string towerId, float heightPx)
    {
        string idle = towerId + "-idle.png";
        float scale = ScaleForBoxHeight(idle, TdAnimArt.IdleFrames, Math.Max(1f, heightPx));
        using var strip = Bake(idle, TdAnimArt.IdleFrames, scale, idle, TdAnimArt.IdleFrames);
        int left = Math.Clamp(strip.AnchorX - strip.BoxW / 2, 0, strip.FrameW - 1);
        int top = Math.Clamp(strip.AnchorY - strip.BoxH, 0, strip.FrameH - 1);
        int w = Math.Min(strip.BoxW, strip.FrameW - left), h = Math.Min(strip.BoxH, strip.FrameH - top);
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = false };
            canvas.DrawImage(strip._image, SKRect.Create(left, top, w, h), SKRect.Create(0, 0, w, h), Nearest, paint);
        }
        var img = SKImage.FromBitmap(bmp);
        var owned = img.ToRasterImage(ensurePixelData: true);
        if (!ReferenceEquals(owned, img)) img.Dispose();
        return owned;
    }

    /// <summary>Desenha o quadro com a âncora em (x, y) físicos; a matriz do canvas deve estar em pixels (identidade).</summary>
    public void Draw(SKCanvas canvas, int frame, float x, float y, SKPaint paint)
    {
        frame = Math.Clamp(frame, 0, Frames - 1);
        float left = MathF.Round(x - AnchorX), top = MathF.Round(y - AnchorY);
        canvas.DrawImage(_image, SKRect.Create(frame * FrameW, 0, FrameW, FrameH), SKRect.Create(left, top, FrameW, FrameH), Nearest, paint);
    }

    public void Dispose() => _image.Dispose();
}
