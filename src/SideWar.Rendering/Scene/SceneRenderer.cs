using SideWar.Core.Simulation;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>
/// Camadas da cena (SDD-22 RF-22.4) desenhadas num <see cref="SKCanvas"/> a partir do <see cref="SimState"/>:
/// linha de estado, núcleo + barra, portal, inimigos (bob global) + barras de vida, números de dano, torres (na barra ou na faixa,
/// brilho pré-cozido, rótulo só número), chefe + barra (ADR-034, canvas 1920 × 152; feixes e clarões não, ADR-027).
/// A linha de borda de estado entra pelo parâmetro <c>edge</c> de Draw; selo e hotspots são do <see cref="HotspotRenderer"/> (T4.9). Pensado para a T-Game (não thread-safe);
/// paints/fonts pré-criados, sem alocação por frame em regime.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);

    private readonly ScaledSpriteCache _cache;
    private readonly SpriteAtlas _atlas;
    private readonly TowerSprites _towerSprites = new();
    private readonly Typography _typography;
    private readonly SKPaint _fill = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint _sprite = new() { IsAntialias = false };
    private readonly SKPaint _text = new() { IsAntialias = false };
    private readonly FloatingNumbers _numbers = new();
    private readonly TextCache _texts = new();
    private readonly Dictionary<(string Text, int Px), (SKTextBlob Blob, float Width)> _runs = [];
    private readonly int[] _order = new int[EnemyStore.CapacityConst];
    private readonly int[] _towerOrder = new int[64];
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
        _atlas = atlas;
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
        _towerSprites.Reset(_scale);
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
    /// (RF-22.6, <c>y ∈ [102, 104)</c>) é desenhada logo após o Clear, portanto SOB os sprites (RF-22.4 item 1); nulo = sem linha.
    /// <paramref name="barHeightDip"/> é a altura da barra (48 no padrão) e define o topo das torres.
    /// </summary>
    public void Draw(SKCanvas canvas, SimState state, double now, AnimationPolicy policy, StripEdgeMode? edge = null,
        float barHeightDip = SceneGeometry.DefaultBarHeight)
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
        DrawBar(canvas, SceneGeometry.CoreBarOuter(layout.CoreX), SceneGeometry.BarFillWidth(state.BaseHp, state.MaxBaseHp, SceneGeometry.CoreBarWidth),
            SceneColors.CoreBarTrack, SceneColors.CoreBarFill, SceneColors.BarBorder, 0);
        DrawSprite(canvas, SpriteId.Core, SceneGeometry.Core(layout.CoreX), 0, null);

        // 3. portal (camada estática no canvas: nasce por trás dele, antes dos inimigos)
        DrawSprite(canvas, SpriteId.Portal, SceneGeometry.Portal(layout.PortalX), 0, null);

        // 4. inimigos comuns, por nascimento (o mais novo por cima), cada um seguido da sua barra de vida
        var enemies = state.Enemies;
        int n = enemies.CopyIndicesByIdAscending(_order);
        int boss = -1;
        for (int k = 0; k < n; k++)
        {
            ref var e = ref enemies.At(_order[k]);
            if (e.Hp <= 0) continue; // morto, ainda no store até o DeathSystem
            if (e.Kind == EnemyKind.Boss || (e.Flags & EnemyFlags.Boss) != 0) { boss = _order[k]; continue; }
            DrawSprite(canvas, SceneGeometry.EnemySprite(e.Kind), SceneGeometry.Enemy(e.Kind, e.X), bobPx, null);
            DrawEnemyBar(canvas, SceneGeometry.EnemyBar(e.Kind, e.X), SceneGeometry.BarFillWidth(e.Hp, e.MaxHp, SceneGeometry.EnemyBar(e.Kind, e.X).W));
        }

        // 5. números de dano
        DrawNumbers(canvas, now, policy);

        // 6. torres posicionadas, na ordem de Id (as SEM POSIÇÃO não são desenhadas); sem slot vazio nem "+" (ADR-034)
        DrawTowers(canvas, state, barHeightDip);

        // 7. chefe e sua barra (68 × 5)
        if (boss >= 0)
        {
            ref var b = ref enemies.At(boss);
            var rect = SceneGeometry.Enemy(EnemyKind.Boss, b.X);
            DrawSprite(canvas, SpriteId.EnemyBoss, rect, bobPx, null);
            DrawBar(canvas, SceneGeometry.BossBarOuter(rect), SceneGeometry.BarFillWidth(b.Hp, b.MaxHp, SceneGeometry.BossBarWidth),
                Palette.PerigoTrilho, Palette.Perigo, SceneColors.BarBorder, bobPx);
        }
    }

    private void DrawTowers(SKCanvas canvas, SimState state, float barHeightDip)
    {
        var host = state.Layout.Host;
        int count = 0;
        for (int i = 0; i < state.Towers.Capacity; i++)
        {
            if (!state.Towers.IsAlive(i) || !state.Towers.At(i).Placed) continue;
            if (count < _towerOrder.Length) _towerOrder[count++] = i;
        }
        // ordem de Id crescente (inserção por seleção: ≤ 24 torres, sem alocação)
        for (int a = 1; a < count; a++)
        {
            int v = _towerOrder[a], k = a - 1;
            while (k >= 0 && state.Towers.At(_towerOrder[k]).Id > state.Towers.At(v).Id) { _towerOrder[k + 1] = _towerOrder[k]; k--; }
            _towerOrder[k + 1] = v;
        }
        for (int k = 0; k < count; k++)
        {
            ref var t = ref state.Towers.At(_towerOrder[k]);
            var rect = SceneGeometry.Tower(t.Type, host, state.TowerCenterX(in t), barHeightDip);
            if (_towerSprites.Get(_atlas, t.Type, host) is { } image)
            {
                float left = R(rect.X * _scale) - _towerSprites.PadPx;
                float top = R(rect.Y * _scale) - _towerSprites.PadPx;
                _sprite.Color = Palette.TxtBranco;
                canvas.DrawImage(image, left, top, Nearest, _sprite);
            }
            var (lx, ly) = SceneGeometry.TowerLabelOrigin(rect);
            DrawLabel(canvas, _texts.Number(t.Level), lx, ly);
        }
    }

    // base alinhada a round(base × scale); posição X por round(dip × scale); tamanho do cache (já round(dip × scale)).
    private void DrawSprite(SKCanvas canvas, SpriteId id, DipRect r, int yOffsetPx, SKColor? tint)
    {
        if (!_cache.TryGet(id, out var src, out _) || _cache.Image is not { } image) return;
        int left = (int)MathF.Round(r.X * _scale, MidpointRounding.AwayFromZero);
        int bottom = (int)MathF.Round(r.Bottom * _scale, MidpointRounding.AwayFromZero) + yOffsetPx;
        var dst = new SKRect(left, bottom - src.Height, left + src.Width, bottom);
        _sprite.Color = tint ?? Palette.TxtBranco;
        canvas.DrawImage(image, SKRect.Create(src.Left, src.Top, src.Width, src.Height), dst, Nearest, _sprite);
    }

    // barra com borda de 1 px (núcleo e chefe): borda, trilho, preenchimento.
    private void DrawBar(SKCanvas canvas, DipRect outer, float fillDip, SKColor track, SKColor fill, SKColor border, int yOffsetPx)
    {
        float s = _scale;
        float ox = R(outer.X * s), oy = R(outer.Y * s) + yOffsetPx, ow = R(outer.Right * s) - R(outer.X * s), oh = R(outer.Bottom * s) - R(outer.Y * s);
        float b = Math.Max(1, R(SceneGeometry.BarBorder * s));
        _fill.Color = border;
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

    // barra de vida do inimigo comum: 3 DIP, sem borda, trilho + preenchimento (HP%).
    private void DrawEnemyBar(SKCanvas canvas, DipRect bar, float fillDip)
    {
        float s = _scale;
        float x = R(bar.X * s), y = R(bar.Y * s), w = R(bar.Right * s) - x, h = Math.Max(1, R(bar.Bottom * s) - y);
        _fill.Color = Palette.PerigoTrilho;
        canvas.DrawRect(x, y, w, h, _fill);
        float fw = Math.Min(w, R(fillDip * s));
        if (fw > 0)
        {
            _fill.Color = Palette.Perigo;
            canvas.DrawRect(x, y, fw, h, _fill);
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
            DrawCentered(canvas, it.Text, it.X - SceneGeometry.TowerCellSize / 2, SceneGeometry.FloatTop - rise,
                SceneGeometry.TowerCellSize, SceneGeometry.FloatFontDip, color, alpha);
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

    // Rótulo de nível: Silkscreen 9, branco, alinhado à esquerda em (xDip, topDip), sombras 1px 1px 0 e −1px −1px 0 pretas.
    private void DrawLabel(SKCanvas canvas, string text, float xDip, float topDip)
    {
        var font = _typography.GetFont(FontRole.SilkscreenRegular, SceneGeometry.LabelFontDip, _dpi);
        var (blob, _) = GetRun(text, font, SceneGeometry.LabelFontDip);
        float x = R(xDip * _scale);
        float y = R(topDip * _scale) + R(-font.Metrics.Ascent);
        float sh = Math.Max(1, R(_scale));
        _text.Color = Palette.SombraTexto;
        canvas.DrawText(blob, x + sh, y + sh, _text);
        canvas.DrawText(blob, x - sh, y - sh, _text);
        _text.Color = Palette.TxtBranco;
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
        _towerSprites.Dispose();
        _fill.Dispose();
        _sprite.Dispose();
        _text.Dispose();
    }
}
