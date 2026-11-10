using SkiaSharp;

namespace SideWar.Rendering.Text;

/// <summary>
/// <c>SKTypeface</c> por papel e <c>SKFont</c> pre-criado por (papel, tamanho DIP, DPI), SDD-20 RF-20.3.
/// Silkscreen: Edging Alias, Subpixel off, Hinting None. JetBrains Mono: Edging Antialias, Subpixel off,
/// Hinting Slight. Tamanho fisico sempre inteiro. Reconstruir (novo <see cref="Typography"/> ou
/// <see cref="Clear"/>) em WM_DPICHANGED. Nao e thread-safe: uso na T-Game.
/// </summary>
public sealed class Typography : IDisposable
{
    private readonly Dictionary<FontRole, SKTypeface> _faces = new();
    private readonly Dictionary<(FontRole, int, int), SKFont> _fonts = new();
    private bool _disposed;

    /// <summary>Tamanho fisico inteiro: <c>round(sizeDip * dpi / 96)</c>, minimo 1 (RF-20.3 item 2).</summary>
    public static int PhysicalSize(float sizeDip, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeDip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        return Math.Max(1, (int)Math.Round(sizeDip * dpi / 96.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>True para Silkscreen (sem antialias).</summary>
    public static bool IsSilkscreen(FontRole role) => role is FontRole.SilkscreenRegular or FontRole.SilkscreenBold;

    /// <summary>Typeface do papel (carregado uma vez; dono: esta instancia).</summary>
    public SKTypeface GetTypeface(FontRole role)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_faces.TryGetValue(role, out SKTypeface? face))
        {
            face = FontLoader.Load(role);
            _faces[role] = face;
        }
        return face;
    }

    /// <summary>
    /// Fonte para o papel, tamanho em DIP e DPI. Instancia compartilhada e cacheada: nao alterar nem descartar.
    /// </summary>
    public SKFont GetFont(FontRole role, float sizeDip, int dpi)
    {
        int px = PhysicalSize(sizeDip, dpi);
        var key = (role, px, 0);
        if (_fonts.TryGetValue(key, out SKFont? font)) return font;

        bool pixel = IsSilkscreen(role);
        font = new SKFont(GetTypeface(role), px)
        {
            Edging = pixel ? SKFontEdging.Alias : SKFontEdging.Antialias,
            Subpixel = false,
            Hinting = pixel ? SKFontHinting.None : SKFontHinting.Slight,
        };
        _fonts[key] = font;
        return font;
    }

    /// <summary>Descarta as fontes por tamanho (WM_DPICHANGED); os typefaces permanecem.</summary>
    public void Clear()
    {
        foreach (SKFont f in _fonts.Values) f.Dispose();
        _fonts.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
        foreach (SKTypeface t in _faces.Values) t.Dispose();
        _faces.Clear();
    }
}
