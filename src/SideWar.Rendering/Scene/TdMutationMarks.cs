using SideWar.Rendering.Theme;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>
/// TD.37d (SDD-37 RF-37.7/37.11): marcas vetoriais discretas por traço de mutação, desenhadas sobre a silhueta do inimigo base (que não muda).
/// rápido = listras de velocidade atrás; tank = placas laterais de casco; lento = âncora/pesos; escudo = aura (some quando o escudo quebra);
/// blindado = placas sobre a cabeça; voador = asas. Cada traço tem forma E cor próprias (legível sem depender de cor). Desenho em DIP do espaço
/// da faixa (a tela já está escalada), sem alocação por frame (caminhos e pincéis pré-criados).
/// </summary>
internal sealed class TdMutationMarks : IDisposable
{
    public const string Fast = "fast", Durable = "durable", Flying = "flying", Slow = "slow", Shielded = "shielded", Armored = "armored";

    private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _wing;

    public TdMutationMarks()
    {
        // asa do lado esquerdo apontando para fora (x negativo); a direita espelha por Scale(-1, 1)
        using var b = new SKPathBuilder();
        b.MoveTo(0, 0);
        b.LineTo(-4.5f, -3.2f);
        b.LineTo(-3.2f, -0.2f);
        b.LineTo(-4.2f, 1.6f);
        b.LineTo(-1.4f, 1.2f);
        b.Close();
        _wing = b.Detach();
    }

    private void Stroke(SKCanvas c, float x1, float y1, float x2, float y2, SKColor color, float width)
    {
        _line.Color = Palette.BgAbismo.WithAlpha(190);
        _line.StrokeWidth = width + 1.2f;
        c.DrawLine(x1, y1, x2, y2, _line);
        _line.Color = color;
        _line.StrokeWidth = width;
        c.DrawLine(x1, y1, x2, y2, _line);
    }

    private void Plate(SKCanvas c, float x, float y, float w, float h, SKColor color)
    {
        _fill.Color = Palette.BgAbismo.WithAlpha(210);
        c.DrawRect(x - 0.6f, y - 0.6f, w + 1.2f, h + 1.2f, _fill);
        _fill.Color = color;
        c.DrawRect(x, y, w, h, _fill);
    }

    /// <summary>Desenha as marcas dos traços ligados em <paramref name="mask"/> (bit k = <paramref name="ids"/>[k]) sobre o corpo (centro X, base <paramref name="foot"/>, largura e altura em DIP).</summary>
    public void Draw(SKCanvas c, ushort mask, string[] ids, float cx, float foot, float sw, float sh, bool shieldUp)
    {
        float w = MathF.Min(MathF.Max(sw, 10f), 22f);
        float l = cx - w / 2f, r = cx + w / 2f, t = foot - sh, mid = foot - sh / 2f;
        for (int k = 0; k < ids.Length && k < 16; k++)
        {
            if ((mask & (1 << k)) == 0) continue;
            switch (ids[k])
            {
                case Fast: // três listras de velocidade atrás (lado do portal = direita)
                    Stroke(c, r + 0.8f, t + sh * 0.30f, r + 4.2f, t + sh * 0.30f, Palette.Td.MutFast, 1.2f);
                    Stroke(c, r + 0.8f, mid, r + 5.2f, mid, Palette.Td.MutFast, 1.2f);
                    Stroke(c, r + 0.8f, t + sh * 0.70f, r + 4.2f, t + sh * 0.70f, Palette.Td.MutFast, 1.2f);
                    break;
                case Durable: // casco: duas placas verticais nas laterais, na altura do corpo
                    Plate(c, l - 2.6f, t + sh * 0.30f, 2f, sh * 0.62f, Palette.Td.MutTank);
                    Plate(c, r + 0.6f, t + sh * 0.30f, 2f, sh * 0.62f, Palette.Td.MutTank);
                    break;
                case Slow: // pesos/âncora: haste com travessa e dois pesos pendurados à esquerda
                    Stroke(c, l - 2.2f, t + sh * 0.25f, l - 2.2f, foot - 2.2f, Palette.Td.MutSlow, 1.1f);
                    Stroke(c, l - 4f, t + sh * 0.25f, l - 0.4f, t + sh * 0.25f, Palette.Td.MutSlow, 1.1f);
                    Plate(c, l - 4.2f, foot - 2.6f, 2f, 2.2f, Palette.Td.MutSlow);
                    Plate(c, l - 1.6f, foot - 2.6f, 2f, 2.2f, Palette.Td.MutSlow);
                    break;
                case Shielded: // aura: arco sobre o corpo, só enquanto o escudo existe
                    if (!shieldUp) break;
                    _line.Color = Palette.Td.MutShield.WithAlpha(235);
                    _line.StrokeWidth = 1.3f;
                    c.DrawArc(new SKRect(l - 2.2f, t - 2.4f, r + 2.2f, foot + 1.2f), 195f, 150f, false, _line);
                    break;
                case Armored: // placas sobre a cabeça: duas barras empilhadas
                    Plate(c, l + 0.5f, t - 2.4f, w - 1f, 1.3f, Palette.Td.MutArmor);
                    Plate(c, l + 2f, t - 4.4f, w - 4f, 1.3f, Palette.Td.MutArmor);
                    break;
                case Flying: // asas nas laterais
                    DrawWing(c, l + 0.8f, mid - 0.5f, false);
                    DrawWing(c, r - 0.8f, mid - 0.5f, true);
                    break;
            }
        }
    }

    private void DrawWing(SKCanvas c, float x, float y, bool mirror)
    {
        c.Save();
        c.Translate(x, y);
        if (mirror) c.Scale(-1f, 1f);
        _fill.Color = Palette.BgAbismo.WithAlpha(210);
        c.Scale(1.25f);
        c.DrawPath(_wing, _fill);
        c.Scale(0.8f);
        _fill.Color = Palette.Td.MutFly;
        c.DrawPath(_wing, _fill);
        c.Restore();
    }

    public void Dispose()
    {
        _line.Dispose();
        _fill.Dispose();
        _wing.Dispose();
    }
}
