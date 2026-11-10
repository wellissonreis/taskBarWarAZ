using System.Runtime.InteropServices;
using SkiaSharp;

namespace SideWar.AssetPipeline;

/// <summary>Imagem RGBA8888 não premultiplicada em memória gerenciada.</summary>
public sealed class Rgba
{
    public Rgba(int width, int height, byte[] data)
    {
        if (width <= 0 || height <= 0 || data.Length != checked(width * height * 4))
        {
            throw new ArgumentException("Dimensões inconsistentes com o buffer RGBA.");
        }

        Width = width;
        Height = height;
        Data = data;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Data { get; }

    public static Rgba Decode(byte[] png)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(png)) ?? throw new InvalidDataException("PNG inválido.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var data = new byte[info.Width * info.Height * 4];
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var result = codec.GetPixels(info, handle.AddrOfPinnedObject());
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                throw new InvalidDataException($"Falha ao decodificar PNG: {result}.");
            }
        }
        finally
        {
            handle.Free();
        }

        return new Rgba(info.Width, info.Height, data);
    }

    public byte[] EncodePng()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        Marshal.Copy(Data, 0, bmp.GetPixels(), Data.Length);
        using var image = SKImage.FromBitmap(bmp);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("Falha ao codificar PNG.");
        return encoded.ToArray();
    }

    /// <summary>Reduz com Mitchell (SDD-09 RF-09.3 passo 3). Resample feito em premultiplicado.</summary>
    public Rgba ResizeMitchell(int width, int height)
    {
        var srcInfo = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var src = new SKBitmap(srcInfo);
        Marshal.Copy(Data, 0, src.GetPixels(), Data.Length);
        var premulInfo = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var premul = new SKBitmap(premulInfo);
        if (!src.CopyTo(premul, SKColorType.Rgba8888))
        {
            throw new InvalidOperationException("Falha ao premultiplicar.");
        }

        var dstInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var resized = premul.Resize(dstInfo, new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("Falha ao redimensionar.");
        using var unpremul = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (!resized.CopyTo(unpremul, SKColorType.Rgba8888))
        {
            throw new InvalidOperationException("Falha ao desmultiplicar.");
        }

        var outData = new byte[width * height * 4];
        Marshal.Copy(unpremul.GetPixels(), outData, 0, outData.Length);
        return new Rgba(width, height, outData);
    }

    /// <summary>Zera (RGBA = 0) todo pixel com alfa &lt; <paramref name="threshold"/> (contrato SDD-04 RF-04.4).</summary>
    public void ZeroLowAlpha(byte threshold)
    {
        for (var i = 0; i < Data.Length; i += 4)
        {
            if (Data[i + 3] < threshold)
            {
                Data[i] = 0;
                Data[i + 1] = 0;
                Data[i + 2] = 0;
                Data[i + 3] = 0;
            }
        }
    }

    /// <summary>Retângulo mínimo com alfa &gt;= <paramref name="threshold"/>; null se não houver pixel.</summary>
    public (int X, int Y, int W, int H)? OpaqueBounds(byte threshold)
    {
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (Data[((y * Width) + x) * 4 + 3] >= threshold)
                {
                    if (x < minX) { minX = x; }
                    if (x > maxX) { maxX = x; }
                    if (y < minY) { minY = y; }
                    if (y > maxY) { maxY = y; }
                }
            }
        }

        return maxX < 0 ? null : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public Rgba Crop(int x, int y, int w, int h)
    {
        var d = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
        {
            Buffer.BlockCopy(Data, (((y + row) * Width) + x) * 4, d, row * w * 4, w * 4);
        }

        return new Rgba(w, h, d);
    }

    public void Blit(Rgba src, int dx, int dy)
    {
        for (var row = 0; row < src.Height; row++)
        {
            Buffer.BlockCopy(src.Data, row * src.Width * 4, Data, (((dy + row) * Width) + dx) * 4, src.Width * 4);
        }
    }
}
