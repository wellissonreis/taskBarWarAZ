using SideWar.Core.Simulation;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>
/// Camadas da cena (SDD-22 RF-22.4) desenhadas num <see cref="SKCanvas"/> a partir do <see cref="SimState"/>:
/// núcleo + barra, slots/torres + rótulos, inimigos (bob global), números de dano, portal, chefe + barra.
/// A linha de borda de estado entra pelo parâmetro <c>edge</c> de Draw; selo e hotspots são do <see cref="HotspotRenderer"/> (T4.9). Pensado para a T-Game (não thread-safe);
/// paints/fonts pré-criados, sem alocação por frame em regime.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);

    private readonly ScaledSpriteCache _cache;
    private readonly Typography _typography;
    private readonly SKPaint _fill = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint _sprite = new() { IsAntialias = false };
    private readonly SKPaint _text = new() { IsAntialias = false };
    private readonly FloatingNumbers _numbers = new();
    private readonly TextCache _texts = new();
    private readonly Dictionary<(string Text, int Px), (SKTextBlob Blob, float Width)> _runs = [];
    private readonly int[] _order = new int[EnemyStore.CapacityConst];
    private readonly int[] _slowShown = new int[64];
    private int _slowHead;
    private long _cursor;
    private bool _cursorInit;
    private int _dpi;
    private float _scale = 1f;

    public SceneRenderer(SpriteAtlas atlas, Typography typography)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentNullException.ThrowIfNull(typography);
        _cache = new ScaledSpriteCache(atlas);
        _typography = typography;
    }

    public FloatingNumbers Numbers => _numbers;
    public int Dpi => _dpi;
    public float Scale => _scale;

    /// <summary>Define o DPI (reassa o cache de sprites; chamar também em WM_DPICHANGED, junto de <c>Typography.Clear()</c>).</summary>
    public void SetDpi(int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        if (dpi == _dpi && _cache.IsBaked) return;
        _dpi = dpi;
        ClearRuns();
        _scale = dpi / 96f;
        _cache.Bake(_scale);
    }

    /// <summary>
    /// Lê os eventos novos de <paramref name="state"/> e cria os números de dano (Hit; SuperHit é ignorado por repetir o dano do Hit; "LENTO" do Gelo só na 1ª lentidão).
    /// Chamar entre ticks. Com <paramref name="spawn"/> falso (modo ausente, DP-22.13) os eventos são consumidos sem criar números.
    /// </summary>
    public void ConsumeEvents(SimState state, double now, bool spawn = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!_cursorInit)
        {
            _cursor = state.Events.Written;
            _cursorInit = true;
            return;
        }
        while (state.Events.TryRead(ref _cursor, out var e))
        {
            if (!spawn || e.Type != SimEventType.Hit) continue; // SuperHit repete o dano do Hit (T3.6): só Hit gera número
            switch ((TowerType)e.B)
            {
                case TowerType.Frost:
                    if (FirstSlow(state, e.X)) _numbers.Add(e.X, now, "LENTO", FloatKind.Slow);
                    break;
                case TowerType.Cannon:
                    _numbers.Add(e.X, now, _texts.Damage(e.A), FloatKind.Cannon);
                    break;
                default:
                    _numbers.Add(e.X, now, _texts.Damage(e.A), FloatKind.Bolt);
                    break;
            }
        }
    }

    // O evento não carrega o Id do alvo: acha o inimigo mais próximo de X e lembra dos que já mostraram "LENTO".
    private bool FirstSlow(SimState state, float x)
    {
        var enemies = state.Enemies;
        int best = -1;
        float bestD = 8f; // DIP: tolerância (o inimigo pode ter andado entre o evento e a leitura)
        for (int i = 0; i < enemies.Capacity; i++)
        {
            if (!enemies.IsAlive(i)) continue;
            float d = MathF.Abs(enemies.At(i).X - x);
            if (d <= bestD) { bestD = d; best = i; }
        }
        if (best < 0) return false;
        int id = enemies.At(best).Id;
        foreach (int s in _slowShown) if (s == id) return false;
        _slowShown[_slowHead] = id;
        _slowHead = (_slowHead + 1) % _slowShown.Length;
        return true;
    }

    /// <summary>Atlas assado deste renderizador (compartilhado com o <see cref="HotspotRenderer"/> para não duplicar memória).</summary>
    public ScaledSpriteCache SpriteCache => _cache;

    /// <summary>
    /// Desenha a cena (limpa o canvas para transparente antes). Com <paramref name="edge"/> a linha de borda de estado
    /// (RF-22.6) é desenhada logo após o Clear, portanto SOB os sprites (RF-22.4 item 1); nulo = sem linha.
    /// </summary>
    public void Draw(SKCanvas canvas, SimState state, double now, AnimationPolicy policy, StripEdgeMode? edge = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(state);
        if (!_cache.IsBaked) throw new InvalidOperationException("Chame SetDpi antes de Draw.");
        _numbers.Expire(now);
        canvas.Clear(SKColors.Transparent);
        if (edge is { } mode) StripEdge.Draw(canvas, _fill, _scale, canvas.DeviceClipBounds.Width, mode);

        var layout = state.Layout;
        float s = _scale;
        float bob = (float)policy.Bob(now, 1.0); // DIP; convertido com round(offset × scale) abaixo
        int bobPx = (int)MathF.Round(bob * s, MidpointRounding.AwayFromZero);

        // 2. núcleo: barra e sprite
        var bar = SceneGeometry.CoreBarContent(layout.CoreX);
        DrawBar(canvas, bar, SceneGeometry.CoreBarOuter(layout.CoreX), SceneGeometry.BarFillWidth(state.BaseHp, state.MaxBaseHp),
            Palette.BgTrilho, Palette.Menta, 0);
        DrawSprite(canvas, SpriteId.Core, SceneGeometry.Core(layout.CoreX), 0, null);

        // 3. slots/torres
        for (int i = 0; i < layout.SlotCount; i++)
        {
            float cell = SceneGeometry.CellLeft(layout.SlotXAt(i));
            var h = state.FindTowerAtSlot(i);
            if (h.IsNone)
            {
                DrawSprite(canvas, SpriteId.SlotEmpty, SceneGeometry.EmptySlot(cell), 0, Palette.TxtBranco.WithAlpha(217));
                DrawCentered(canvas, "+", cell, SceneGeometry.PlusBox(cell).Y, SceneGeometry.CellWidth, SceneGeometry.PlusFontDip, Palette.TxtBranco);
            }
            else
            {
                ref var t = ref state.Towers.Get(h);
                DrawSprite(canvas, SceneGeometry.TowerSprite(t.Type), SceneGeometry.Tower(t.Type, cell), 0, null);
                DrawCentered(canvas, _texts.Level(t.Level), cell, SceneGeometry.TowerLabelBox(cell).Y, SceneGeometry.CellWidth, SceneGeometry.LabelFontDip, Palette.TxtBranco);
            }
        }

        // 4. inimigos comuns, por nascimento (o mais novo por cima)
        var enemies = state.Enemies;
        int n = enemies.CopyIndicesByIdAscending(_order);
        int boss = -1;
        for (int k = 0; k < n; k++)
        {
            ref var e = ref enemies.At(_order[k]);
            if (e.Hp <= 0) continue; // morto, ainda no store até o DeathSystem
            if (e.Kind == EnemyKind.Boss || (e.Flags & EnemyFlags.Boss) != 0) { boss = _order[k]; continue; }
            DrawSprite(canvas, SceneGeometry.EnemySprite(e.Kind), SceneGeometry.Enemy(e.Kind, e.X), bobPx, null);
        }

        // 5. números de dano
        DrawNumbers(canvas, now, policy);

        // 6. portal
        DrawSprite(canvas, SpriteId.Portal, SceneGeometry.Portal(layout.PortalX), 0, null);

        // 7. chefe e sua barra
        if (boss >= 0)
        {
            ref var b = ref enemies.At(boss);
            var rect = SceneGeometry.Enemy(EnemyKind.Boss, b.X);
            DrawSprite(canvas, SpriteId.EnemyBoss, rect, bobPx, null);
            DrawBar(canvas, SceneGeometry.BossBarContent(rect), SceneGeometry.BossBarOuter(rect),
                SceneGeometry.BarFillWidth(b.Hp, b.MaxHp), Palette.PerigoTrilho, Palette.Perigo, bobPx);
        }
    }

    // base alinhada a round(96 × scale); posição X por round(dip × scale); tamanho do cache (já round(dip × scale)).
    private void DrawSprite(SKCanvas canvas, SpriteId id, DipRect r, int yOffsetPx, SKColor? tint)
    {
        if (!_cache.TryGet(id, out var src, out _) || _cache.Image is not { } image) return;
        int left = (int)MathF.Round(r.X * _scale, MidpointRounding.AwayFromZero);
        int bottom = (int)MathF.Round(r.Bottom * _scale, MidpointRounding.AwayFromZero) + yOffsetPx;
        var dst = new SKRect(left, bottom - src.Height, left + src.Width, bottom);
        _sprite.Color = tint ?? Palette.TxtBranco;
        canvas.DrawImage(image, SKRect.Create(src.Left, src.Top, src.Width, src.Height), dst, Nearest, _sprite);
    }

    private void DrawBar(SKCanvas canvas, DipRect content, DipRect outer, float fillDip, SKColor track, SKColor fill, int yOffsetPx)
    {
        float s = _scale;
        float ox = R(outer.X * s), oy = R(outer.Y * s) + yOffsetPx, ow = R(outer.Right * s) - R(outer.X * s), oh = R(outer.Bottom * s) - R(outer.Y * s);
        float b = Math.Max(1, R(SceneGeometry.BarBorder * s));
        _fill.Color = Palette.BgTrilhoFundo;
        canvas.DrawRect(ox, oy, ow, oh, _fill);
        _fill.Color = track;
        canvas.DrawRect(ox + b, oy + b, ow - 2 * b, oh - 2 * b, _fill);
        float fw = Math.Min(ow - 2 * b, R(fillDip * s));
        if (fw > 0)
        {
            _fill.Color = fill;
            canvas.DrawRect(ox + b, oy + b, fw, oh - 2 * b, _fill);
        }
    }

    private void DrawNumbers(SKCanvas canvas, double now, AnimationPolicy policy)
    {
        var items = _numbers.Items;
        for (int i = 0; i < items.Length; i++)
        {
            ref readonly var it = ref items[i];
            if (!it.Active) continue;
            float p = FloatingNumbers.Progress(now, it.Start);
            float k = SceneGeometry.EaseOut(p);
            float rise = policy.ReducedMotion ? 0 : SceneGeometry.FloatRise * k;
            byte alpha = policy.ReducedMotion ? (byte)255 : (byte)Math.Round(255 * (1 - k));
            var color = it.Kind switch
            {
                FloatKind.Cannon => Palette.Ambar,
                FloatKind.Slow => Palette.Gelo,
                _ => Palette.AzulClaro,
            };
            DrawCentered(canvas, it.Text, it.X - SceneGeometry.CellWidth / 2, SceneGeometry.FloatTop - rise,
                SceneGeometry.CellWidth, SceneGeometry.FloatFontDip, color, alpha);
        }
    }

    private static float R(float v) => MathF.Round(v, MidpointRounding.AwayFromZero);

    // Texto Silkscreen (Alias, px inteiro) centrado na caixa [x, x+w] com topo em topDip; sombra 1px 1px 0 #000.
    private void DrawCentered(SKCanvas canvas, string text, float xDip, float topDip, float wDip, int sizeDip, SKColor color, byte alpha = 255)
    {
        var font = _typography.GetFont(FontRole.SilkscreenRegular, sizeDip, _dpi);
        float s = _scale;
        var (blob, width) = GetRun(text, font, sizeDip);
        float x = R(xDip * s + (wDip * s - width) / 2);
        float y = R(topDip * s) + R(-font.Metrics.Ascent);
        float sh = Math.Max(1, R(s));
        _text.Color = Palette.SombraTexto.WithAlpha(alpha);
        canvas.DrawText(blob, x + sh, y + sh, _text);
        _text.Color = color.WithAlpha(alpha);
        canvas.DrawText(blob, x, y, _text);
    }

    // DrawText(string) aloca (UTF-8) a cada chamada; SKTextBlob por (texto, tamanho físico) é criado uma vez e reutilizado.
    private (SKTextBlob Blob, float Width) GetRun(string text, SKFont font, int sizeDip)
    {
        var key = (text, Typography.PhysicalSize(sizeDip, _dpi));
        if (_runs.TryGetValue(key, out var run)) return run;
        run = (SKTextBlob.Create(text, font) ?? throw new InvalidOperationException("SKTextBlob.Create failed."), font.MeasureText(text));
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
        _cache.Dispose();
        _fill.Dispose();
        _sprite.Dispose();
        _text.Dispose();
    }
}
