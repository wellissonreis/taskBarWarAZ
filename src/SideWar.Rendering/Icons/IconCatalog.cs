using SkiaSharp;

namespace SideWar.Rendering.Icons;

/// <summary>
/// Catalogo de icones vetoriais (SDD-20 RF-20.5): <c>ParseSvgPathData</c> uma vez na carga e mascara
/// <c>Alpha8</c> por (icone, DPI), rasterizada com antialias so no cache. Em render, desenhar a mascara
/// tingida com <see cref="DrawTinted"/>; nenhum <c>SKPath</c> e desenhado por frame. Uso na T-Game.
/// </summary>
public sealed class IconCatalog : IDisposable
{
    private const float ViewBox = 16f;

    private static readonly IconSpec[] Specs =
    [
        new("M5 14h6M6 14V7h4v7M4 7h8l-1-3H5zM8 1v3", 18, 1.5f),
        new("M4 15V2M4 2h8l-2 3 2 3H4", 18, 1.5f),
        new("M2 4l2 2 3-3M2 11l2 2 3-3M9 5h5M9 12h5", 18, 1.5f),
        new("M4.5 12H4a3 3 0 0 1-.3-6A4.5 4.5 0 0 1 12.4 6 3 3 0 0 1 12 12h-.5M8 14V8M5.5 10.5 8 8l2.5 2.5", 18, 1.5f),
        new("M3 3l10 10M13 3 3 13", 16, 1.8f),
        new("M8 13V3M3 8l5-5 5 5", 16, 2f),
        new("M8 2 1.5 14h13zM8 6.5v3.5M8 12v.5", 20, 1.6f),
    ];

    private readonly SKPath[] _paths;
    private readonly Dictionary<(IconId, int), SKBitmap> _masks = new();
    private bool _disposed;

    /// <summary>Faz o parse dos 7 paths; erro vira excecao de inicializacao (falha cedo).</summary>
    public IconCatalog()
    {
        _paths = new SKPath[Specs.Length];
        for (int i = 0; i < Specs.Length; i++)
        {
            _paths[i] = SKPath.ParseSvgPathData(Specs[i].PathData)
                ?? throw new InvalidOperationException($"Path data invalido do icone {(IconId)i}.");
        }
    }

    /// <summary>Especificacao do icone.</summary>
    public static IconSpec GetSpec(IconId id) => Specs[(int)id];

    /// <summary>Lado em px da mascara: <c>round(sizeDip * dpi / 96)</c>.</summary>
    public static int PhysicalSize(IconId id, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        return Math.Max(1, (int)Math.Round(Specs[(int)id].SizeDip * dpi / 96.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Mascara Alpha8 cacheada (nao alterar nem descartar). Cria na primeira chamada por (icone, DPI).</summary>
    public SKBitmap GetMask(IconId id, int dpi)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_masks.TryGetValue((id, dpi), out SKBitmap? mask)) return mask;

        IconSpec spec = Specs[(int)id];
        int size = PhysicalSize(id, dpi);
        float k = spec.SizeDip / ViewBox * dpi / 96f;
        mask = new SKBitmap(new SKImageInfo(size, size, SKColorType.Alpha8, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(mask))
        using (var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = spec.StrokeUnits * k,
            StrokeCap = SKStrokeCap.Butt,
            StrokeJoin = SKStrokeJoin.Miter,
            StrokeMiter = 4,
            IsAntialias = true,
            Color = SKColors.White,
        })
        {
            canvas.Clear(SKColors.Transparent);
            float offset = (size - ViewBox * k) / 2f;
            // Path ja escalado: o traco em px nao e escalado de novo.
            using var m = new SKPath();
            _paths[(int)id].Transform(SKMatrix.CreateScaleTranslation(k, k, offset, offset), m);
            canvas.DrawPath(m, paint);
        }
        _masks[(id, dpi)] = mask;
        return mask;
    }

    /// <summary>Desenha o icone centrado em (<paramref name="cx"/>, <paramref name="cy"/>), em pixel inteiro, tingido por <paramref name="paint"/>.</summary>
    public void DrawTinted(SKCanvas canvas, IconId id, int dpi, float cx, float cy, SKPaint paint)
    {
        SKBitmap mask = GetMask(id, dpi);
        float x = (float)Math.Round(cx - mask.Width / 2f);
        float y = (float)Math.Round(cy - mask.Height / 2f);
        canvas.DrawBitmap(mask, x, y, new SKSamplingOptions(SKFilterMode.Nearest), paint);
    }

    /// <summary>Descarta as mascaras (WM_DPICHANGED); os paths permanecem.</summary>
    public void ClearMasks()
    {
        foreach (SKBitmap b in _masks.Values) b.Dispose();
        _masks.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearMasks();
        foreach (SKPath p in _paths) p.Dispose();
    }
}
