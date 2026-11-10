using SkiaSharp;

namespace SideWar.Rendering.Frame;

/// <summary>
/// Animated test frame (SDD-08 RF-08.9): border line, design marks (core, 8 slots, portal), 24 moving
/// "enemies", 12 fixed "towers", 24 floating numbers, 1 banner, 1 box and a counters text. Translucent shapes
/// exercise premultiplied alpha (CA-08.1). Paints are pre-created; steady state allocates nothing.
/// </summary>
public sealed class TestSceneRenderer : IDisposable
{
    public const int EnemyCount = 24;
    public const int TowerCount = 12;
    public const int FloaterCount = 24;
    public const int SlotCount = 8;

    private readonly SKPaint background = Fill(0x40, 0x10, 0x18, 0x30);
    private readonly SKPaint border = Fill(0xFF, 0x4F, 0xC3, 0xF7);
    private readonly SKPaint mark = Fill(0xFF, 0xFF, 0xC1, 0x07);
    private readonly SKPaint enemy = Fill(0xB0, 0xEF, 0x53, 0x50);
    private readonly SKPaint tower = Fill(0xFF, 0x66, 0xBB, 0x6A);
    private readonly SKPaint banner = Fill(0xC0, 0x7E, 0x57, 0xC2);
    private readonly SKPaint box = Fill(0x60, 0x00, 0x00, 0x00);
    private readonly SKPaint text = new() { Color = new(0xFF, 0xFF, 0xFF, 0xFF), IsAntialias = true };
    private readonly SKFont font = new() { Size = 12 };
    private readonly SKPaint softCircle = new() { Color = new(0x90, 0xFF, 0xFF, 0xFF), IsAntialias = true };
    private string counters = "";
    private long countersFrame = long.MinValue;

    private static SKPaint Fill(byte a, byte r, byte g, byte b) => new() { Color = new(r, g, b, a), IsAntialias = false };

    /// <summary>Draws the full frame at <paramref name="seconds"/> of animation time.</summary>
    public void Draw(SKCanvas canvas, int width, int height, float scale, double seconds, long frame,
        double composeMs, double presentMs)
    {
        float s = scale;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawRect(0, 0, width, height, background);
        canvas.DrawRect(0, 0, width, Math.Max(1, s), border);

        // Marks: core (left), 8 slots, portal (right).
        float markY = height * 0.5f;
        float margin = 24 * s;
        canvas.DrawRect(margin - 8 * s, markY - 8 * s, 16 * s, 16 * s, mark);
        float span = Math.Max(1, width - 2 * margin - 96 * s);
        for (int i = 0; i < SlotCount; i++)
        {
            float x = margin + 48 * s + span * (i + 0.5f) / SlotCount;
            canvas.DrawRect(x - 4 * s, markY + 14 * s, 8 * s, 8 * s, mark);
        }
        canvas.DrawRect(width - margin - 8 * s, markY - 8 * s, 16 * s, 16 * s, mark);

        // Towers: fixed.
        for (int i = 0; i < TowerCount; i++)
        {
            float x = margin + 48 * s + span * (i + 0.5f) / TowerCount;
            canvas.DrawRect(x - 6 * s, 12 * s, 12 * s, 12 * s, tower);
        }

        // Enemies: horizontal motion, wrap-around.
        float lane = Math.Max(1, width - 2 * margin);
        for (int i = 0; i < EnemyCount; i++)
        {
            float speed = (40 + (i % 5) * 12) * s;
            float x = margin + (float)((i * 97 * s + seconds * speed) % lane);
            float y = markY - 12 * s + (i % 3) * 8 * s;
            canvas.DrawRect(x, y, 10 * s, 10 * s, enemy);
        }

        // Floating numbers (simulated as soft circles rising and fading).
        for (int i = 0; i < FloaterCount; i++)
        {
            double phase = (seconds * 0.8 + i * 0.137) % 1.0;
            float x = margin + (float)((i * 61 * s) % lane);
            float y = markY - (float)(phase * 28 * s);
            softCircle.Color = softCircle.Color.WithAlpha((byte)(160 * (1 - phase)));
            canvas.DrawCircle(x, y, 3 * s, softCircle);
        }

        // Banner and box.
        canvas.DrawRect(width * 0.5f - 90 * s, height - 22 * s, 180 * s, 16 * s, banner);
        canvas.DrawRect(width - margin - 120 * s, height - 40 * s, 110 * s, 30 * s, box);

        // Counters text, rebuilt only twice per second of frames.
        if (frame - countersFrame >= 15 || countersFrame == long.MinValue)
        {
            countersFrame = frame;
            counters = $"frame {frame}  compose {composeMs:F2} ms  present {presentMs:F2} ms";
        }
        font.Size = 12 * s;
        canvas.DrawText(counters, margin, height - 8 * s, SKTextAlign.Left, font, text);
    }

    public void Dispose()
    {
        background.Dispose(); border.Dispose(); mark.Dispose(); enemy.Dispose(); tower.Dispose();
        banner.Dispose(); box.Dispose(); text.Dispose(); font.Dispose(); softCircle.Dispose();
    }
}
