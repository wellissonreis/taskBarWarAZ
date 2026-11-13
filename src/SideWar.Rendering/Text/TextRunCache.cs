using SkiaSharp;

namespace SideWar.Rendering.Text;

/// <summary>
/// Cache de runs de texto (SDD-08 RF-08.8 item 1): <c>DrawText(string)</c> e <c>MeasureText(string)</c> alocam (UTF-8) a cada chamada;
/// aqui o <see cref="SKTextBlob"/> e a largura de cada (texto, fonte) são criados uma vez e reaproveitados (0 B/frame em regime).
/// Uma instância por renderer (ou por thread, no caso estático das abas): não é thread-safe. A chave usa typeface + tamanho físico + edging,
/// por isso sobrevive a <see cref="Typography.Clear"/> sem entradas incorretas. Passando de <see cref="Capacity"/> entradas o cache é esvaziado
/// (textos dinâmicos não crescem sem limite).
/// </summary>
internal sealed class TextRunCache : IDisposable
{
    public const int Capacity = 2048;

    private readonly Dictionary<(string Text, IntPtr Face, float Size, SKFontEdging Edging), Run> _runs = [];
    private readonly Dictionary<char, string> _oneChar = [];

    internal readonly record struct Run(SKTextBlob Blob, float Width);

    /// <summary>Run do texto na fonte (cria no primeiro uso). O blob pertence ao cache: não descartar.</summary>
    public Run Get(string text, SKFont font)
    {
        var key = (text, font.Typeface.Handle, font.Size, font.Edging);
        if (_runs.TryGetValue(key, out var run)) return run;
        if (_runs.Count >= Capacity) Clear();
        run = new Run(SKTextBlob.Create(text, font) ?? throw new InvalidOperationException("SKTextBlob.Create failed."), font.MeasureText(text));
        _runs[key] = run;
        return run;
    }

    /// <summary>Largura em px (mesmo valor de <c>font.MeasureText</c>), sem alocar em regime.</summary>
    public float Measure(string text, SKFont font) => Get(text, font).Width;

    /// <summary>String de 1 caractere interned (letter-spacing por caractere sem <c>ToString</c> por frame).</summary>
    public string Char(char ch)
    {
        if (!_oneChar.TryGetValue(ch, out var s)) _oneChar[ch] = s = ch.ToString();
        return s;
    }

    public void Clear()
    {
        foreach (var run in _runs.Values) run.Blob.Dispose();
        _runs.Clear();
    }

    public void Dispose() => Clear();
}
