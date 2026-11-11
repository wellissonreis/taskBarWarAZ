using SkiaSharp;

namespace SideWar.App.Bench;

/// <summary>Placeholder widget content for the E1 frame (SDD-23 is a later phase): panel, border and one text line. Paints pre-created.</summary>
internal sealed class BenchWidgetRenderer : IDisposable
{
    private readonly SKPaint fill = new() { Color = new(0x10, 0x18, 0x30, 0xD0) };
    private readonly SKPaint border = new() { Color = new(0x4F, 0xC3, 0xF7, 0xFF), IsStroke = true, StrokeWidth = 1 };
    private readonly SKPaint text = new() { Color = new(0xFF, 0xFF, 0xFF, 0xFF), IsAntialias = true };
    private readonly SKFont font = new() { Size = 12 };
    private string line = "";
    private string? lastKey;

    public void Draw(SKCanvas canvas, int width, int height, float scale, string mode, string scenario, long frames)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.DrawRect(0, 0, width, height, fill);
        border.StrokeWidth = Math.Max(1, scale);
        canvas.DrawRect(0.5f, 0.5f, width - 1, height - 1, border);
        font.Size = 12 * scale;
        var key = mode + "|" + scenario + "|" + frames;
        if (key != lastKey) { lastKey = key; line = $"SideWar {scenario} · {mode} · {frames} quadros"; }
        canvas.DrawText(line, 10 * scale, height * 0.5f + 4 * scale, SKTextAlign.Left, font, text);
    }

    public void Dispose()
    {
        fill.Dispose(); border.Dispose(); text.Dispose(); font.Dispose();
    }
}
