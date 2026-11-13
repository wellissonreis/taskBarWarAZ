using SideWar.Core.Simulation;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Input;
using BlockReason = SideWar.Runtime.Taskbar.BlockReason;
using BlockedSpan = SideWar.Runtime.Taskbar.BlockedSpan;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>Textos do overlay do modo construção (SDD-15 RF-15.11 / <c>Strings</c>); padrão = pt-BR do canvas.</summary>
public sealed record BuildOverlayStrings
{
    public string Widget { get; init; } = "WIDGET";
    public string StartSearchApps { get; init; } = "INICIAR · PESQUISA · ÍCONES";
    public string TrayClock { get; init; } = "BANDEJA · RELÓGIO";
    public string Edge { get; init; } = "BORDA";
    public string AboveBar { get; init; } = "ACIMA DA BARRA: ROTA DOS INIMIGOS, BASE E PORTAL";
    public string HintDetail { get; init; } = "ao lado do ícone · clique para posicionar";
    public string Banner { get; init; } = "MODO CONSTRUÇÃO";
    public string Legend { get; init; } = "verde = livre · laranja = bloqueado";
    public string EscChip { get; init; } = "ESC cancela";

    public string Label(BlockReason reason) => reason switch
    {
        BlockReason.WindowsWidget => Widget,
        BlockReason.StartSearchApps => StartSearchApps,
        BlockReason.TrayClock => TrayClock,
        _ => Edge,
    };
}

/// <summary>Quadro do overlay: estado da simulação (layout e torres), trechos bloqueados da barra e fantasma (só visual).</summary>
public readonly record struct BuildOverlayFrame(
    SimState State,
    float BarHeightDip,
    IReadOnlyList<BlockedSpan> Blocked,
    BuildGhost? Ghost,
    TowerType GhostType,
    string HintTitle,
    double Now,
    bool ReducedMotion = false,
    BuildOverlayStrings? Strings = null);

/// <summary>
/// Overlay do modo construção (SDD-22 RF-22.11; estado "construir" de <c>Strip.dc.html</c>): zonas bloqueadas (laranja listrado),
/// trechos livres (verde tracejado), fantasma 44 × 44 piscando, dica, faixa «MODO CONSTRUÇÃO». Só desenho: frame completo por mudança,
/// zero custo fora do modo (ninguém o chama). Cores/medidas do canvas; o estado inválido do fantasma é proposta da spec (laranja).
/// </summary>
public sealed class BuildOverlayRenderer : IDisposable
{
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);

    // Cores do canvas (RF-22.11).
    public static readonly SKColor Orange = new(0xff, 0x7a, 0x1a);
    public static readonly SKColor OrangeStripeStrong = new(0xff, 0x7a, 0x1a, 97); // .38
    public static readonly SKColor OrangeStripeWeak = new(0xff, 0x7a, 0x1a, 20);   // .08
    public static readonly SKColor OrangeGhostFill = new(0xff, 0x7a, 0x1a, 56);    // .22
    public static readonly SKColor OrangeTagText = new(0x1a, 0x0b, 0x00);
    public static readonly SKColor Green = new(0x5c, 0xf0, 0xb0);
    public static readonly SKColor GreenFreeFill = new(0x5c, 0xf0, 0xb0, 31);      // .12
    public static readonly SKColor GreenGhostFill = new(0x5c, 0xf0, 0xb0, 56);     // .22
    public static readonly SKColor PanelFill = new(0x0b, 0x1a, 0x2e);
    public static readonly SKColor LegendText = new(0xc9, 0xd6, 0xea);
    public static readonly SKColor ChipBorder = new(0x2c, 0x44, 0x68);

    public const float StripePeriod = 14f, StripeStrong = 6f;
    public const float BorderDip = 2f;
    public const float MinFreeRun = PlacementValidator.BoxWidth;
    public const float HintTop = 62f, HintLeftOffset = 80f;
    public const float BannerHeight = 36f;
    public const double BlinkSeconds = 1.1;
    public const float BlinkLow = 0.45f;
    public const float AboveBarHeight = SceneGeometry.GroundY;

    private readonly SpriteAtlas _atlas;
    private readonly Typography _typography;
    private readonly TowerSprites _sprites = new();
    private readonly SKPaint _fill = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = false, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _text = new() { IsAntialias = false };
    private readonly SKPaint _sprite = new() { IsAntialias = false };
    private readonly SKPaint _stripes = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly Dictionary<(string Text, int Px, int Spacing, IntPtr Face), (SKTextBlob Blob, float Width)> _runs = [];
    private readonly List<(int A, int B)> _free = [];
    private readonly List<int> _centers = [];
    private SKShader? _stripeShader;
    private SKPathEffect? _dash;
    private int _dpi;
    private float _scale = 1f;

    public BuildOverlayRenderer(SpriteAtlas atlas, Typography typography)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(typography);
        _atlas = atlas;
        _typography = typography;
    }

    public int Dpi => _dpi;

    /// <summary>Desenha os glifos (padrão true); false mantém o layout idêntico sem texto (golden entre SOs, SDD-17 RF-17.3).</summary>
    public bool TextEnabled { get; set; } = true;

    private void DrawRun(SKCanvas canvas, SKTextBlob blob, float x, float y, SKPaint paint)
    {
        if (TextEnabled) canvas.DrawText(blob, x, y, paint);
    }

    /// <summary>Define o DPI (rebake do padrão listrado, do tracejado e dos sprites); chamar junto de <c>Typography.Clear()</c> em mudança de DPI.</summary>
    public void SetDpi(int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        if (dpi == _dpi && _stripeShader is not null) return;
        _dpi = dpi;
        _scale = dpi / 96f;
        ClearRuns();
        _sprites.Reset(_scale);
        _stripeShader?.Dispose();
        _dash?.Dispose();
        // repeating-linear-gradient(45deg, strong 0 6px, weak 6px 14px): a linha do gradiente sobe para a direita; origem no canto inferior esquerdo da caixa.
        float period = StripePeriod * _scale, k = MathF.Sqrt(0.5f);
        _stripeShader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(period * k, -period * k),
            [OrangeStripeStrong, OrangeStripeStrong, OrangeStripeWeak, OrangeStripeWeak],
            [0f, StripeStrong / StripePeriod, StripeStrong / StripePeriod, 1f], SKShaderTileMode.Repeat);
        _stripes.Shader = _stripeShader;
        _dash = SKPathEffect.CreateDash([6f * _scale, 4f * _scale], 0);
    }

    public void Draw(SKCanvas canvas, in BuildOverlayFrame f)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_stripeShader is null) throw new InvalidOperationException("Chame SetDpi antes de Draw.");
        var strings = f.Strings ?? DefaultStrings;
        var layout = f.State.Layout;
        float bar = f.BarHeightDip;

        var blocked = f.Blocked; // índice: foreach sobre IReadOnlyList aloca o enumerador (RF-08.8)
        for (int i = 0; i < blocked.Count; i++)
        {
            var span = blocked[i];
            DrawBlocked(canvas, span.X0, SceneGeometry.BarTop, span.X1 - span.X0, bar, strings.Label(span.Reason));
        }
        DrawBlocked(canvas, 0, 0, layout.Width, AboveBarHeight, strings.AboveBar);

        float cellTop = CellTop(layout.Host, bar);
        DrawFreeRuns(canvas, f.State, cellTop);

        if (f.Ghost is { } ghost)
        {
            DrawGhost(canvas, layout.Host, ghost, f.GhostType, cellTop, f.Now, f.ReducedMotion);
            DrawHint(canvas, layout.Width, ghost.X, f.HintTitle, strings.HintDetail);
        }
        DrawBanner(canvas, layout.Width, strings);
    }

    /// <summary>Topo da célula 44 × 44 (106 com H = 48; 2 no hospedeiro Band).</summary>
    public static float CellTop(TowerHost host, float barHeightDip) =>
        SceneGeometry.TowerCell(host, 0, barHeightDip).Y;

    /// <summary>
    /// Trechos livres: zona livre menos as caixas <c>[x − 22, x + 22)</c> das torres posicionadas, só trechos ≥ 44 DIP.
    /// Reaproveita as listas internas; o resultado vale até a próxima chamada.
    /// </summary>
    public IReadOnlyList<(int A, int B)> FreeRuns(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _centers.Clear();
        var layout = state.Layout;
        for (int i = 0; i < state.Towers.Capacity; i++)
        {
            if (!state.Towers.IsAlive(i)) continue;
            ref readonly var t = ref state.Towers.At(i);
            if (t.Placed) _centers.Add(PlacementValidator.CenterX(t.Anchor, t.OffsetDip, layout.Width));
        }
        _centers.Sort();
        _free.Clear();
        foreach (var zone in layout.FreeZones)
        {
            int cursor = zone.X0;
            foreach (int c in _centers)
            {
                int a = c - PlacementValidator.HalfBox, b = c + PlacementValidator.HalfBox;
                if (b <= cursor) continue;
                if (a >= zone.X1) break;
                if (a - cursor >= MinFreeRun) _free.Add((cursor, a));
                cursor = Math.Max(cursor, b);
            }
            if (zone.X1 - cursor >= MinFreeRun) _free.Add((cursor, zone.X1));
        }
        return _free;
    }

    private void DrawBlocked(SKCanvas canvas, float x, float y, float w, float h, string label)
    {
        if (w <= 0 || h <= 0) return;
        float s = _scale;
        float l = R(x * s), t = R(y * s), r = R((x + w) * s), b = R((y + h) * s);
        canvas.Save();
        canvas.ClipRect(new SKRect(l, t, r, b));
        canvas.Translate(l, b); // 0% do gradiente passa pelo canto inferior esquerdo
        canvas.DrawRect(0, -(b - t), r - l, b - t, _stripes);
        canvas.Restore();
        DrawBorder(canvas, l, t, r, b, BorderDip, Orange);

        // etiqueta: left 4, top 2, padding 1px 4px, Silkscreen 9, letter-spacing .5
        var font = _typography.GetFont(FontRole.SilkscreenRegular, 9, _dpi);
        var (blob, tw) = GetRun(label, font, 9, 0.5f);
        float line = LineHeight(font);
        float tl = l + R(4 * s), tt = t + R(2 * s), pad4 = R(4 * s), pad1 = Math.Max(1, R(s));
        float tr = Math.Min(r, tl + tw + 2 * pad4);
        _fill.Color = Orange;
        canvas.DrawRect(tl, tt, tr - tl, line + 2 * pad1, _fill);
        canvas.Save();
        canvas.ClipRect(new SKRect(tl, tt, tr, tt + line + 2 * pad1));
        _text.Color = OrangeTagText;
        DrawRun(canvas, blob, tl + pad4, tt + pad1 + R(-font.Metrics.Ascent), _text);
        canvas.Restore();
    }

    private void DrawFreeRuns(SKCanvas canvas, SimState state, float cellTop)
    {
        FreeRuns(state);
        var runs = _free; // List<T> concreta: enumerador struct (a interface devolvida por FreeRuns boxaria)
        float s = _scale, t = R(cellTop * s), b = R((cellTop + SceneGeometry.TowerCellSize) * s);
        float bw = Math.Max(1, R(BorderDip * s));
        foreach (var (a, c) in runs)
        {
            float l = R(a * s), r = R(c * s);
            _fill.Color = GreenFreeFill;
            canvas.DrawRect(l, t, r - l, b - t, _fill);
            _stroke.Color = Green;
            _stroke.StrokeWidth = bw;
            _stroke.PathEffect = _dash;
            canvas.DrawRect(l + bw / 2, t + bw / 2, r - l - bw, b - t - bw, _stroke);
            _stroke.PathEffect = null;
        }
    }

    private void DrawGhost(SKCanvas canvas, TowerHost host, BuildGhost ghost, TowerType type, float cellTop, double now, bool reduced)
    {
        float s = _scale;
        float l = R((ghost.X - PlacementValidator.HalfBox) * s), t = R(cellTop * s);
        float r = R((ghost.X + PlacementValidator.HalfBox) * s), b = R((cellTop + SceneGeometry.TowerCellSize) * s);
        _fill.Color = ghost.Valid ? GreenGhostFill : OrangeGhostFill;
        canvas.DrawRect(l, t, r - l, b - t, _fill);
        DrawBorder(canvas, l, t, r, b, BorderDip, ghost.Valid ? Green : Orange);

        if (_sprites.Get(_atlas, type, host) is not { } image) return;
        float w = SceneGeometry.TowerWidth(type, host), h = SceneGeometry.TowerHeight(host);
        float sl = R((ghost.X - w / 2) * s) , st = R((cellTop + (SceneGeometry.TowerCellSize - h) / 2) * s);
        _sprite.Color = SKColors.White.WithAlpha(Blink(now, reduced));
        canvas.DrawImage(image, sl - _sprites.PadPx, st - _sprites.PadPx, Nearest, _sprite);
    }

    /// <summary><c>adBlink</c> 1,1 s <c>steps(2)</c>: opacidade 1 na 1ª metade, 0,45 na 2ª; parado (1) com <c>reducedMotion</c>.</summary>
    public static byte Blink(double now, bool reducedMotion)
    {
        if (reducedMotion) return 255;
        double phase = (now % BlinkSeconds + BlinkSeconds) % BlinkSeconds / BlinkSeconds;
        return phase < 0.5 ? (byte)255 : (byte)MathF.Round(255 * BlinkLow);
    }

    /// <summary>Caixa da dica (DIP): <c>top 62</c>, <c>left = célula.Left − 80</c>, limitada a <c>[0, largura]</c>.</summary>
    public static float HintLeft(int ghostX, float boxWidth, float screenWidth) =>
        Math.Clamp(ghostX - PlacementValidator.HalfBox - HintLeftOffset, 0f, Math.Max(0f, screenWidth - boxWidth));

    private void DrawHint(SKCanvas canvas, float screenWidthDip, int ghostX, string title, string detail)
    {
        float s = _scale;
        var f1 = _typography.GetFont(FontRole.SilkscreenRegular, 11, _dpi);
        var f2 = _typography.GetFont(FontRole.JetBrainsMonoRegular, 11, _dpi);
        var (b1, w1) = GetRun(title, f1, 11, 0);
        var (b2, w2) = GetRun(detail, f2, 11, 0);
        float padX = R(10 * s), padY = R(6 * s), gap = R(8 * s), bw = Math.Max(1, R(s));
        float line = Math.Max(LineHeight(f1), LineHeight(f2));
        float width = w1 + w2 + gap + 2 * padX + 2 * bw, height = line + 2 * padY + 2 * bw;
        float left = R(HintLeft(ghostX, width / s, screenWidthDip) * s), top = R(HintTop * s);
        _fill.Color = Green;
        canvas.DrawRect(left, top, width, height, _fill);
        _fill.Color = PanelFill;
        canvas.DrawRect(left + bw, top + bw, width - 2 * bw, height - 2 * bw, _fill);
        float x = left + bw + padX, baseTop = top + bw + padY;
        _text.Color = Green;
        DrawRun(canvas, b1, x, baseTop + (line - LineHeight(f1)) / 2 + R(-f1.Metrics.Ascent), _text);
        _text.Color = LegendText;
        DrawRun(canvas, b2, x + w1 + gap, baseTop + (line - LineHeight(f2)) / 2 + R(-f2.Metrics.Ascent), _text);
    }

    private void DrawBanner(SKCanvas canvas, float screenWidthDip, BuildOverlayStrings st)
    {
        float s = _scale;
        var font = _typography.GetFont(FontRole.SilkscreenRegular, 11, _dpi);
        var (b1, w1) = GetRun(st.Banner, font, 11, 1);
        var (b2, w2) = GetRun(st.Legend, font, 11, 1);
        var (b3, w3) = GetRun(st.EscChip, font, 11, 1);
        float line = LineHeight(font);
        float bw = Math.Max(1, R(s)), padX = R(14 * s), gap = R(12 * s), chipX = R(6 * s), chipY = R(2 * s);
        float chipW = w3 + 2 * chipX + 2 * bw, chipH = line + 2 * chipY + 2 * bw;
        float width = w1 + w2 + chipW + 2 * gap + 2 * padX + 2 * bw, height = R(BannerHeight * s);
        float left = R((screenWidthDip * s - width) / 2), top = 0;
        _fill.Color = Green;
        canvas.DrawRect(left, top, width, height, _fill);
        _fill.Color = PanelFill;
        canvas.DrawRect(left + bw, top + bw, width - 2 * bw, height - 2 * bw, _fill);
        float baseline = top + (height - line) / 2 + R(-font.Metrics.Ascent);
        float x = left + bw + padX;
        _text.Color = Green;
        DrawRun(canvas, b1, x, baseline, _text);
        x += w1 + gap;
        _text.Color = LegendText;
        DrawRun(canvas, b2, x, baseline, _text);
        x += w2 + gap;
        float cy = top + (height - chipH) / 2;
        _fill.Color = ChipBorder;
        canvas.DrawRect(x, cy, chipW, chipH, _fill);
        _fill.Color = PanelFill;
        canvas.DrawRect(x + bw, cy + bw, chipW - 2 * bw, chipH - 2 * bw, _fill);
        DrawRun(canvas, b3, x + bw + chipX, cy + bw + chipY + R(-font.Metrics.Ascent), _text);
    }

    private void DrawBorder(SKCanvas canvas, float l, float t, float r, float b, float widthDip, SKColor color)
    {
        float w = Math.Max(1, R(widthDip * _scale));
        _fill.Color = color;
        canvas.DrawRect(l, t, r - l, w, _fill);
        canvas.DrawRect(l, b - w, r - l, w, _fill);
        canvas.DrawRect(l, t + w, w, b - t - 2 * w, _fill);
        canvas.DrawRect(r - w, t + w, w, b - t - 2 * w, _fill);
    }

    private static float LineHeight(SKFont font) => MathF.Round(font.Metrics.Descent - font.Metrics.Ascent, MidpointRounding.AwayFromZero);

    private static float R(float v) => MathF.Round(v, MidpointRounding.AwayFromZero);

    private static readonly BuildOverlayStrings DefaultStrings = new();

    // Texto com letter-spacing (CSS: espaço após cada glifo, inclusive o último). Blob por (texto, tamanho físico, espaçamento) cacheado.
    private (SKTextBlob Blob, float Width) GetRun(string text, SKFont font, int sizeDip, float spacingDip)
    {
        int spacingPx = (int)MathF.Round(spacingDip * _scale * 100);
        var key = (text, Typography.PhysicalSize(sizeDip, _dpi), spacingPx, font.Typeface.Handle);
        if (_runs.TryGetValue(key, out var run)) return run;
        if (spacingPx == 0)
            run = (SKTextBlob.Create(text, font) ?? throw new InvalidOperationException("SKTextBlob.Create failed."), font.MeasureText(text));
        else
        {
            var glyphs = font.GetGlyphs(text);
            var widths = font.GetGlyphWidths(glyphs);
            var points = new SKPoint[glyphs.Length];
            float x = 0, sp = spacingPx / 100f;
            for (int i = 0; i < glyphs.Length; i++) { points[i] = new SKPoint(x, 0); x += widths[i] + sp; }
            using var builder = new SKTextBlobBuilder();
            builder.AddPositionedRun(glyphs, font, points);
            run = (builder.Build() ?? throw new InvalidOperationException("SKTextBlob build failed."), x);
        }
        _runs[key] = run;
        return run;
    }

    private void ClearRuns()
    {
        foreach (var run in _runs.Values) run.Blob.Dispose();
        _runs.Clear();
    }

    public void Dispose()
    {
        ClearRuns();
        _stripeShader?.Dispose();
        _dash?.Dispose();
        _sprites.Dispose();
        _fill.Dispose();
        _stroke.Dispose();
        _text.Dispose();
        _sprite.Dispose();
        _stripes.Dispose();
    }
}
