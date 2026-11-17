using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Td;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>
/// Faixa TD de 32 DIP (TD.7, SDD-28 RF-28.15 com o fallback Band de RF-28.18): trilho dos inimigos no topo (núcleo à esquerda, portal à
/// direita), torres embaixo. Lê o <see cref="TdMatchState"/> (só leitura) e desenha num <see cref="SKCanvas"/> transparente.
/// TD.9: usa as SPRITES do design (<see cref="TdArtMap"/>), pré-escaladas por DPI (<see cref="TdSpriteBaker"/>) e desenhadas em pixels
/// inteiros (nítidas a 100/150%): torres de 28 DIP, núcleo/portal de 42 DIP (1,5× a torre, passam do topo da faixa), inimigos de 11 a 28 DIP, caixa-bomba de 26 DIP.
/// Tudo apoiado no chão (base da faixa); inimigos à frente das torres. Legibilidade por cima das sprites: tipo de inimigo por <b>tinta +
/// pino de traço</b> (voador losango, blindado quadrado, regenera triângulo, drena cruz, chefe quadrado vermelho), barra de vida só para
/// chefe e elite danificado, alcance como barra no topo, nós do balanceador, caixa-bomba
/// com contagem e destaque de perigo, torre em risco marcada, torres SEM POSIÇÃO como chips tracejados. Paints/paths/imagens pré-criados;
/// sem alocação por frame em regime. Não thread-safe (T-Game).
/// </summary>
public sealed class TdBandRenderer : IDisposable
{
    private static readonly string[] Digits = BuildDigits();

    private const byte FlyingBit = 1, ArmoredBit = 2, RegenBit = 4, DrainBit = 8;

    private readonly Typography _typography;
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
    private readonly SKPaint _text = new() { IsAntialias = false };
    private readonly SKPath _diamond = Poly(0, -4, 4, 0, 0, 4, -4, 0);
    private readonly SKPath _triangle = Poly(0, -4, 4, 3.5f, -4, 3.5f);
    private readonly SKPath _cross = Poly(-1.5f, -4, 1.5f, -4, 1.5f, -1.5f, 4, -1.5f, 4, 1.5f, 1.5f, 1.5f, 1.5f, 4, -1.5f, 4, -1.5f, 1.5f, -4, 1.5f, -4, -1.5f, -1.5f, -1.5f);
    private readonly SKPath _bolt = Poly(1.5f, -5, -3, 0.5f, 0, 0.5f, -1.5f, 5, 3, -0.5f, 0, -0.5f);
    private readonly SKPath _riskMark = Poly(-3, -1, 3, -1, 0, 3);
    private readonly SKPaint _img = new() { IsAntialias = false };
    private readonly SpriteAtlas _atlas;
    private readonly bool _ownsAtlas;
    private SKImage?[] _towerImg = [], _chipImg = [], _enemyImg = [];
    private SKImage? _coreImg, _portalImg, _crateImg;
    private bool _baked;
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest);
    private const float Ground = 32f, EnemyFoot = 30.5f, FlyFoot = 24f, ChipSpriteDip = 11f;
    private readonly SKPathEffect _dash = SKPathEffect.CreateDash([2f, 2f], 0);
    private TdContent? _content;
    private byte[] _traits = [];
    private int _enemyDefs;
    private int _dpi = 96;
    private float _s = 1f;   // escala efetiva do desenho: dpi/96 × k (k = altura da faixa / 32, TD.14)
    private float _k = 1f;

    /// <param name="typography">Fontes.</param>
    /// <param name="atlas">Atlas do design (compartilhado com a cena idle); nulo = carrega o embutido e o possui.</param>
    public TdBandRenderer(Typography typography, SpriteAtlas? atlas = null)
    {
        _typography = typography ?? throw new ArgumentNullException(nameof(typography));
        _atlas = atlas ?? SpriteAtlas.LoadEmbedded();
        _ownsAtlas = atlas is null;
    }

    public int Dpi => _dpi;

    /// <summary>DPI do monitor (muda o tamanho físico das fontes; recriar bitmaps/superfície fora daqui).</summary>
    public void SetDpi(int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        _dpi = dpi;
        _s = dpi / 96f * _k;
        _typography.Clear();
        ReleaseSprites();
    }

    /// <summary>Altura real da faixa em DIP (TD.14: 32 a 96): toda a arte do espaço de desenho de 32 DIP escala por <c>altura / 32</c>.</summary>
    public void SetBandHeight(int heightDip)
    {
        float k = TdBand.Scale(heightDip);
        if (k == _k) return;
        _k = k;
        _s = _dpi / 96f * _k;
        _typography.Clear();
        ReleaseSprites();
    }

    public float BandScale => _k;

    /// <summary>Tamanho físico da janela para uma largura de faixa em DIP.</summary>
    public (int Width, int Height) PhysicalSize(int widthDip) => ((int)MathF.Round(widthDip * _s), (int)MathF.Round((TdBand.HeightDip + TdBand.EdgeHeadroomDip) * _s));

    public void Draw(SKCanvas canvas, TdMatchState match, double now, in TdView view)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(match);
        EnsureContent(match.Content);
        if (!_baked) BakeSprites();
        canvas.Clear(SKColors.Transparent);
        int w = TdBand.WidthDip(match);
        canvas.Save();
        canvas.Scale(_s);
        canvas.Translate(0, TdBand.EdgeHeadroomDip); // folga acima da faixa: só o portal/núcleo/barra de integridade desenham lá (TD.28c)

        // Alfa 1/255 em toda a faixa: o Windows só entrega o mouse (modo Interativo, TD.8) a pixels com alfa > 0; invisível a olho nu.
        _fill.Color = Palette.Td.HitSurface;
        canvas.DrawRect(0, 0, w, TdBand.HeightDip, _fill);

        DrawRail(canvas, match, w);
        DrawSelectedRange(canvas, match, view, w);
        DrawZones(canvas, match, w);
        DrawNodes(canvas, match, w);
        DrawCore(canvas, match);
        DrawPortal(canvas, w);
        DrawTowers(canvas, match, view, w);
        DrawUnplaced(canvas, match, view);
        DrawEnemies(canvas, match, w);
        DrawGhost(canvas, match, view, w);
        DrawFire(canvas, match, w);
        DrawCrate(canvas, match, view, now, w);
        DrawHint(canvas, view, now, w);
        DrawPlacingMode(canvas, view, w);
        canvas.Restore();
    }

    private void EnsureContent(TdContent content)
    {
        if (ReferenceEquals(content, _content)) return;
        _content = content;
        var normal = content.Enemies.Enemies;
        var bosses = content.Enemies.Bosses;
        _enemyDefs = normal.Length + bosses.Length;
        _traits = new byte[_enemyDefs];
        for (int i = 0; i < normal.Length; i++) _traits[i] = TraitBits(normal[i].Traits);
        for (int i = 0; i < bosses.Length; i++) _traits[normal.Length + i] = TraitBits(bosses[i].Traits);
        ReleaseSprites();
    }

    // ------------------------------------------------------------------ sprites (TD.9)

    private void BakeSprites()
    {
        ReleaseSprites();
        var content = _content!;
        float px = _s;
        _towerImg = new SKImage?[content.Towers.Length];
        _chipImg = new SKImage?[content.Towers.Length];
        for (int i = 0; i < content.Towers.Length; i++)
        {
            var e = TdArtMap.TryTower(content.Towers[i].Id, out var found) ? found : TdArtMap.Towers[0];
            _towerImg[i] = TdSpriteBaker.ByHeight(_atlas, e.Sprite, e.HeightDip * px, e.Tint, e.TintAlpha);
            _chipImg[i] = TdSpriteBaker.ByHeight(_atlas, e.Sprite, ChipSpriteDip * px, e.Tint, e.TintAlpha);
        }
        var normal = content.Enemies.Enemies;
        var bosses = content.Enemies.Bosses;
        _enemyImg = new SKImage?[normal.Length + bosses.Length];
        for (int i = 0; i < _enemyImg.Length; i++)
        {
            string id = i < normal.Length ? normal[i].Id : bosses[i - normal.Length].Id;
            var e = TdArtMap.TryEnemy(id, out var found) ? found : (i < normal.Length ? TdArtMap.Enemies[0] : TdArtMap.Enemies[^3]);
            _enemyImg[i] = TdSpriteBaker.ByHeight(_atlas, e.Sprite, e.HeightDip * px, e.Tint, e.TintAlpha);
        }
        _coreImg = TdSpriteBaker.ByHeight(_atlas, SpriteId.Core, TdArtMap.EdgeHeightDip * px, default, 0);
        _portalImg = TdSpriteBaker.ByHeight(_atlas, SpriteId.Portal, TdArtMap.EdgeHeightDip * px, default, 0);
        _crateImg = TdSpriteBaker.ByWidth(_atlas, SpriteId.SupplyCrate, TdArtMap.CrateWidthDip * px);
        _baked = true;
    }

    private void ReleaseSprites()
    {
        foreach (var i in _towerImg) i?.Dispose();
        foreach (var i in _chipImg) i?.Dispose();
        foreach (var i in _enemyImg) i?.Dispose();
        _coreImg?.Dispose(); _portalImg?.Dispose(); _crateImg?.Dispose();
        _towerImg = []; _chipImg = []; _enemyImg = [];
        _coreImg = _portalImg = _crateImg = null;
        _baked = false;
    }

    /// <summary>Desenha a imagem pré-escalada com o centro X e a base em DIP, alinhada a pixels inteiros (sem reamostrar).</summary>
    private void Blit(SKCanvas c, SKImage? img, float cxDip, float bottomDip, byte alpha = 255)
    {
        if (img is null) return;
        c.Save();
        c.ResetMatrix();
        _img.Color = Palette.TxtBranco.WithAlpha(alpha);
        c.DrawImage(img, MathF.Round(cxDip * _s - img.Width / 2f), MathF.Round((bottomDip + TdBand.EdgeHeadroomDip) * _s - img.Height), Nearest, _img);
        c.Restore();
    }

    private float WidthDip(SKImage? img) => img is null ? 0f : img.Width / _s;

    private float HeightDip(SKImage? img) => img is null ? 0f : img.Height / _s;

    private static byte TraitBits(string[] traits)
    {
        byte b = 0;
        foreach (var t in traits)
        {
            if (t == "flying") b |= FlyingBit;
            else if (t == "armored") b |= ArmoredBit;
            else if (t == "regen") b |= RegenBit;
            else if (t == "drain") b |= DrainBit;
        }
        return b;
    }

    // ------------------------------------------------------------------ trilho, núcleo, portal

    private void DrawRail(SKCanvas c, TdMatchState m, int w)
    {
        // chão onde marcham os inimigos (base da faixa); em Onda a linha de topo fica sólida
        _fill.Color = Palette.Td.RailFill;
        c.DrawRect(0, Ground - 5f, w, 5f, _fill);
        _stroke.Color = m.Phase == MatchPhase.Onda ? Palette.BordaForte : Palette.BordaPainel;
        _stroke.PathEffect = m.Phase == MatchPhase.Onda ? null : _dash;
        c.DrawLine(0, Ground - 4.5f, w, Ground - 4.5f, _stroke);
        _stroke.PathEffect = null;
        if (m.Phase is MatchPhase.Vitoria or MatchPhase.Derrota)
        {
            _fill.Color = m.Phase == MatchPhase.Vitoria ? Palette.Menta : Palette.Perigo;
            c.DrawRect(0, TdBand.HeightDip - 1, w, 1, _fill);
        }
    }

    private void DrawCore(SKCanvas c, TdMatchState m)
    {
        float cx = Math.Max(TdArtMap.EdgeWidthDip, WidthDip(_coreImg)) / 2f;
        Blit(c, _coreImg, cx, Ground);
        // integridade legível por cima da sprite: barra acima do núcleo
        float frac = m.IntegrityMax > 0 ? Math.Clamp(m.Integrity / (float)m.IntegrityMax, 0f, 1f) : 0f;
        float top = Math.Max(1f - TdBand.EdgeHeadroomDip, Ground - HeightDip(_coreImg) - 3.5f);
        _fill.Color = Palette.Placeholder.Edge;
        c.DrawRect(1, top - 0.5f, 30, 3.5f, _fill);
        _fill.Color = frac > 0.5f ? Palette.Menta : frac > 0.25f ? Palette.Ambar : Palette.Perigo;
        c.DrawRect(1.5f, top, 29f * frac, 2.5f, _fill);
    }

    private void DrawPortal(SKCanvas c, int w) => Blit(c, _portalImg, w - Math.Max(TdArtMap.EdgeWidthDip, WidthDip(_portalImg)) / 2f, Ground);

    // ------------------------------------------------------------------ alcance, zonas, nós

    private void DrawSelectedRange(SKCanvas c, TdMatchState m, in TdView view, int w)
    {
        if (view.SelectedTowerId == 0) return;
        for (int i = 0; i < m.Towers.Count; i++)
        {
            var t = m.Towers[i];
            if (t.Id != view.SelectedTowerId || !t.Placed) continue;
            float cx = TdBand.TowerCenterDip(m, t, w);
            float r = TdBand.ToDip(_content!.Towers[t.DefIndex].Range, w);
            float x0 = MathF.Max(0, cx - r), x1 = MathF.Min(w, cx + r);
            var color = TowerColor(t.DefIndex);
            _fill.Color = color.WithAlpha(40);
            c.DrawRect(x0, 2, x1 - x0, Ground - 2, _fill);
            _fill.Color = color.WithAlpha(210);
            c.DrawRect(x0, 0, x1 - x0, 2, _fill);
        }
    }

    private void DrawZones(SKCanvas c, TdMatchState m, int w)
    {
        var zones = m.Zones;
        for (int i = 0; i < zones.Count; i++)
        {
            var z = zones[i];
            float x = TdBand.ToDip(z.X, w), r = TdBand.ToDip(z.Radius, w);
            _fill.Color = Palette.Perigo.WithAlpha(70);
            c.DrawRect(x - r, 2, 2 * r, Ground - 2, _fill);
        }
    }

    private void DrawNodes(SKCanvas c, TdMatchState m, int w)
    {
        var nodes = m.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            float x = TdBand.ToDip(nodes[i].X, w);
            _fill.Color = Palette.Menta;
            c.DrawRoundRect(x - 2.5f, Ground - 9f, 5, 7, 1, 1, _fill);
            _fill.Color = Palette.BgAbismo;
            c.DrawRect(x - 0.5f, Ground - 7.5f, 1, 4, _fill);
        }
    }

    // ------------------------------------------------------------------ inimigos

    private void DrawEnemies(SKCanvas c, TdMatchState m, int w)
    {
        var enemies = m.Enemies;
        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            float x = TdBand.ToDip(e.X, w);
            byte bits = e.DefIndex >= 0 && e.DefIndex < _enemyDefs ? _traits[e.DefIndex] : (byte)0;
            bool flying = e.Flying || (bits & FlyingBit) != 0;
            var img = e.DefIndex >= 0 && e.DefIndex < _enemyImg.Length ? _enemyImg[e.DefIndex] : null;
            float foot = e.IsBoss ? Ground - 0.5f : flying ? FlyFoot : EnemyFoot;
            float sw = WidthDip(img), sh = HeightDip(img);
            SKColor border = e.IsBoss ? Palette.Perigo : flying ? Palette.Gelo : (bits & ArmoredBit) != 0 ? Palette.Ambar
                : (bits & RegenBit) != 0 ? Palette.Menta : (bits & DrainBit) != 0 ? Palette.Td.EnemyDrain : Palette.Td.EnemySolo;
            Blit(c, img, x, foot);

            // pino de traço no canto superior direito (forma redundante com a tinta: não depende só de cor)
            float px = x + sw / 2f - 1f, py = foot - sh + 1.5f;
            if (e.IsBoss)
            {
                _fill.Color = Palette.BgAbismo;
                c.DrawRect(px - 3.5f, py - 3.5f, 7, 7, _fill);
                _fill.Color = border;
                c.DrawRect(px - 2.5f, py - 2.5f, 5, 5, _fill);
            }
            else if (flying) Pip(c, _diamond, px, py, border);
            else if ((bits & ArmoredBit) != 0)
            {
                _fill.Color = Palette.BgAbismo;
                c.DrawRect(px - 3, py - 3, 6, 6, _fill);
                _fill.Color = border;
                c.DrawRect(px - 2, py - 2, 4, 4, _fill);
            }
            else if ((bits & RegenBit) != 0) Pip(c, _triangle, px, py, border);
            else if ((bits & DrainBit) != 0) Pip(c, _cross, px, py, border);

            if (e.ParalyzeTicks > 0)
            {
                _stroke.Color = Palette.TxtBranco;
                c.DrawRoundRect(x - sw / 2f - 1f, foot - sh - 1f, sw + 2f, sh + 2f, 2, 2, _stroke);
            }
            else if (e.SlowTicks > 0 && e.SlowMult < 1.0)
            {
                _fill.Color = Palette.Gelo;
                c.DrawRect(x - MathF.Max(3f, sw / 2f), Ground - 1.5f, MathF.Max(6f, sw), 1.5f, _fill);
            }

            if (e.IsBoss || (e.Hp < e.MaxHp && e.MaxHp >= 90))
                DrawLifeBar(c, x, e.IsBoss ? 0f : foot - sh - 3.5f, e.IsBoss ? 24f : 10f, (float)(e.Hp / e.MaxHp), e.IsBoss);
        }
    }

    // Marcador de traço sobre fundo escuro (forma unitária de ±4 DIP reduzida a 75%).
    private void Pip(SKCanvas c, SKPath shape, float x, float y, SKColor color)
    {
        c.Save();
        c.Translate(x, y);
        c.Scale(0.75f);
        _fill.Color = Palette.BgAbismo;
        c.DrawCircle(0, 0, 5f, _fill);
        _fill.Color = color;
        c.DrawPath(shape, _fill);
        c.Restore();
    }

    private void DrawLifeBar(SKCanvas c, float cx, float top, float width, float frac, bool boss)
    {
        top = MathF.Max(0, top);
        float x = cx - width / 2f;
        _fill.Color = Palette.Placeholder.Edge;
        c.DrawRect(x - 0.5f, top, width + 1, 2.5f, _fill);
        _fill.Color = boss ? Palette.Perigo : Palette.Menta;
        c.DrawRect(x, top + 0.5f, width * Math.Clamp(frac, 0f, 1f), 1.5f, _fill);
    }

    // ------------------------------------------------------------------ torres

    private SKColor TowerColor(int defIndex)
    {
        string id = _content!.Towers[defIndex].Id;
        return id switch
        {
            "bolt" => Palette.Placeholder.TowerBolt,
            "frost" => Palette.Placeholder.TowerFrost,
            "cannon" => Palette.Placeholder.TowerCannon,
            "scaleset" => Palette.Placeholder.TowerScaleSet,
            _ => Palette.Menta, // balancer
        };
    }

    private void DrawTowers(SKCanvas c, TdMatchState m, in TdView view, int w)
    {
        int risk = m.CrateTargetTowerId;
        for (int i = 0; i < m.Towers.Count; i++)
        {
            var t = m.Towers[i];
            if (!t.Placed) continue;
            float cx = TdBand.TowerCenterDip(m, t, w);
            DrawTower(c, cx, t, view.SelectedTowerId == t.Id, view.HoverTowerId == t.Id, risk == t.Id);
        }
    }

    // Fantasma do modo construção (RF-28.20): caixa da torre no ponto (snap), barra de alcance no trilho; laranja se inválido.
    private void DrawGhost(SKCanvas c, TdMatchState m, in TdView view, int w)
    {
        if (view.GhostX < 0) return;
        float cx = m.Layout is null ? TdBand.ToDip(view.GhostX, w) : TdPlacement.ToDip(view.GhostX, w);
        var color = view.GhostValid ? Palette.Menta : Palette.Ambar;
        int def = view.GhostDef;
        float range = def >= 0 && def < _content!.Towers.Length ? TdBand.ToDip(_content.Towers[def].Range, w) : 0f;
        if (range > 0)
        {
            float x0 = MathF.Max(0, cx - range), x1 = MathF.Min(w, cx + range);
            _fill.Color = color.WithAlpha(40);
            c.DrawRect(x0, 2, x1 - x0, Ground - 2, _fill);
            _fill.Color = color.WithAlpha(210);
            c.DrawRect(x0, 0, x1 - x0, 2, _fill);
        }
        var gimg = def >= 0 && def < _towerImg.Length ? _towerImg[def] : null;
        float x = cx - TdBand.TowerWidth / 2f;
        if (gimg is not null)
        {
            Blit(c, gimg, cx, Ground, (byte)(view.GhostValid ? (view.ChosenX >= 0 ? 170 : 120) : 80));
            float gw = WidthDip(gimg), gh = HeightDip(gimg);
            _stroke.Color = color;
            _stroke.PathEffect = _dash;
            c.DrawRoundRect(cx - gw / 2f - 0.5f, Ground - gh - 0.5f, gw + 1, gh + 1, 2, 2, _stroke);
            _stroke.PathEffect = null;
            return;
        }
        _fill.Color = color.WithAlpha((byte)(view.ChosenX >= 0 ? 110 : 70));
        c.DrawRoundRect(x, TdBand.TowerTop, TdBand.TowerWidth, TdBand.TowerHeight, 3, 3, _fill);
        _stroke.Color = color;
        _stroke.PathEffect = _dash;
        c.DrawRoundRect(x + 0.5f, TdBand.TowerTop + 0.5f, TdBand.TowerWidth - 1, TdBand.TowerHeight - 1, 3, 3, _stroke);
        _stroke.PathEffect = null;
    }

    // Modo posicionar (TD.16b): a faixa passa a aceitar clique; borda menta em toda a faixa + véu leve, para o modo ser inequívoco.
    private void DrawPlacingMode(SKCanvas c, in TdView view, int w)
    {
        if (!view.PlacingMode) return;
        _fill.Color = Palette.Menta.WithAlpha(22);
        c.DrawRect(0, 0, w, TdBand.HeightDip, _fill);
        _stroke.Color = Palette.Menta.WithAlpha(235);
        _stroke.StrokeWidth = 2f;
        c.DrawRect(1f, 1f, w - 2f, TdBand.HeightDip - 2f, _stroke);
        _stroke.StrokeWidth = 1f;
    }

    // Destaque do tutorial (TD.15): contorno âmbar pulsante na zona de torres ou anel no alvo (torre/caixa). Alfa pela fase do relógio.
    private void DrawHint(SKCanvas c, in TdView view, double now, int w)
    {
        if (!view.BandHint && view.HintX < 0f) return;
        byte alpha = (byte)(150 + 100 * (0.5 + 0.5 * Math.Sin(now * 5.0)));
        _stroke.Color = Palette.Ambar.WithAlpha(alpha);
        _stroke.StrokeWidth = 1.5f;
        if (view.BandHint)
            c.DrawRoundRect(TdBand.EdgeClearDip, 1f, w - 2 * TdBand.EdgeClearDip, TdBand.HeightDip - 2f, 3, 3, _stroke);
        else
        {
            const float r = 13f;
            c.DrawRoundRect(view.HintX - r, 1f, 2 * r, TdBand.HeightDip - 2f, 4, 4, _stroke);
        }
        _stroke.StrokeWidth = 1f;
    }

    private void DrawTower(SKCanvas c, float cx, in TdTower t, bool selected, bool hover, bool atRisk)
    {
        var img = t.DefIndex >= 0 && t.DefIndex < _towerImg.Length ? _towerImg[t.DefIndex] : null;
        bool disabled = t.DisabledTicks > 0;
        float sw = MathF.Max(WidthDip(img), 12f), sh = MathF.Max(HeightDip(img), 12f);
        float left = cx - sw / 2f, top = Ground - sh;
        Blit(c, img, cx, Ground, disabled ? (byte)110 : (byte)255);

        if (selected || hover)
        {
            _stroke.Color = selected ? Palette.TxtBranco : Palette.AmbarHover;
            _stroke.StrokeWidth = selected ? 1.5f : 1f;
            c.DrawRoundRect(left - 0.5f, top - 0.5f, sw + 1, sh + 1, 2, 2, _stroke);
            _stroke.StrokeWidth = 1f;
        }

        if (disabled)
        {
            _stroke.Color = Palette.Perigo;
            c.DrawLine(left + 2, top + 2, left + sw - 2, Ground - 2, _stroke);
        }
        if (atRisk)
        {
            _stroke.Color = Palette.Perigo;
            _stroke.StrokeWidth = 2f;
            c.DrawRoundRect(left - 1f, top - 1f, sw + 2, sh + 2, 3, 3, _stroke);
            _stroke.StrokeWidth = 1f;
            _fill.Color = Palette.Perigo;
            Shape(c, _riskMark, cx, MathF.Max(2.5f, top - 3f), true, false);
        }
    }

    private void DrawUnplaced(SKCanvas c, TdMatchState m, in TdView view)
    {
        int index = 0;
        for (int i = 0; i < m.Towers.Count; i++)
        {
            var t = m.Towers[i];
            if (t.Placed) continue;
            float x = TdBand.UnplacedChipX(index++) - TdBand.UnplacedChipWidth / 2f;
            var color = TowerColor(t.DefIndex);
            _fill.Color = color.WithAlpha(60);
            c.DrawRoundRect(x, TdBand.UnplacedChipTop, TdBand.UnplacedChipWidth, TdBand.UnplacedChipHeight, 2, 2, _fill);
            _stroke.Color = view.SelectedTowerId == t.Id ? Palette.TxtBranco : color;
            _stroke.PathEffect = _dash;
            c.DrawRoundRect(x + 0.5f, TdBand.UnplacedChipTop + 0.5f, TdBand.UnplacedChipWidth - 1, TdBand.UnplacedChipHeight - 1, 2, 2, _stroke);
            _stroke.PathEffect = null;
            var chip = t.DefIndex >= 0 && t.DefIndex < _chipImg.Length ? _chipImg[t.DefIndex] : null;
            Blit(c, chip, x + TdBand.UnplacedChipWidth / 2f, TdBand.UnplacedChipTop + TdBand.UnplacedChipHeight - 1.5f, 200);
        }
    }

    // ------------------------------------------------------------------ projéteis / efeitos

    /// <summary>
    /// Efeito simples de disparo: a torre que acabou de atirar (recarga recém-reiniciada) traça um feixe até o inimigo mais próximo
    /// dentro do alcance. É só visual (o Core não expõe o alvo): o inimigo mostrado pode divergir do atingido em empates.
    /// </summary>
    private void DrawFire(SKCanvas c, TdMatchState m, int w)
    {
        for (int i = 0; i < m.Towers.Count; i++)
        {
            var t = m.Towers[i];
            if (!t.Placed || t.DisabledTicks > 0) continue;
            var def = _content!.Towers[t.DefIndex];
            if (def.Kind != "attack" || def.Interval <= 0) continue;
            int intervalTicks = Math.Max(1, (int)Math.Round(def.Interval * TdWorld.TicksPerSecond));
            if (t.CooldownTicks < intervalTicks - 2) continue;

            float cx = TdBand.TowerCenterDip(m, t, w);
            float range = (float)def.Range * w / 1200f;
            int best = -1;
            float bestD = float.MaxValue;
            for (int j = 0; j < m.Enemies.Count; j++)
            {
                var e = m.Enemies[j];
                bool both = def.Targets == "both";
                if (e.Flying && !both) continue;
                float ex = TdBand.ToDip(e.X, w);
                float d = MathF.Abs(ex - cx);
                if (d <= range && d < bestD) { bestD = d; best = j; }
            }
            if (best < 0) continue;
            var target = m.Enemies[best];
            float tx = TdBand.ToDip(target.X, w);
            float ty = target.Flying ? FlyFoot - 7f : EnemyFoot - 7f;
            _stroke.Color = TowerColor(t.DefIndex).WithAlpha(220);
            c.DrawLine(cx, Ground - 22f, tx, ty, _stroke);
            _fill.Color = Palette.TxtBranco;
            c.DrawCircle(tx, ty, 1.5f, _fill);
        }
    }

    // ------------------------------------------------------------------ caixa-bomba

    private void DrawCrate(SKCanvas c, TdMatchState m, in TdView view, double now, int w)
    {
        if (!m.CrateActive) return;
        float cx = TdBand.ToDip(m.CrateX, w);
        double left = m.CrateSecondsLeft;
        bool urgent = left <= 3.0;
        bool blink = urgent && ((int)(now * 4) & 1) == 0;
        // a torre em risco é marcada em DrawTower (CrateTargetTowerId)
        float ch = HeightDip(_crateImg), cw = WidthDip(_crateImg);
        float bottom = TdBand.CrateTop + ch + 1f;
        // destaque de perigo: halo vermelho (pisca nos últimos 3 s) atrás da caixa
        _fill.Color = (urgent ? Palette.Perigo : Palette.Ambar).WithAlpha(blink ? (byte)150 : (byte)70);
        c.DrawRoundRect(cx - cw / 2f - 2f, bottom - ch - 2f, cw + 4f, ch + 4f, 3, 3, _fill);
        Blit(c, _crateImg, cx, bottom);
        _stroke.Color = view.CrateHover ? Palette.TxtBranco : urgent ? Palette.Perigo : Palette.Ambar;
        _stroke.StrokeWidth = 1.5f;
        c.DrawRoundRect(cx - cw / 2f - 0.75f, bottom - ch - 0.75f, cw + 1.5f, ch + 1.5f, 2.5f, 2.5f, _stroke);
        _stroke.StrokeWidth = 1f;

        // contagem regressiva num selo escuro ao lado da caixa (física: fora da escala do canvas)
        float bx = cx + cw / 2f + 2f, by = bottom - ch;
        _fill.Color = Palette.BgAbismo.WithAlpha(235);
        c.DrawRoundRect(bx, by, 13f, 9f, 2, 2, _fill);
        int sec = Math.Clamp((int)Math.Ceiling(left), 0, Digits.Length - 1);
        c.Save();
        c.ResetMatrix();
        var font = _typography.GetFont(FontRole.SilkscreenBold, 8, (int)MathF.Round(_dpi * _k));
        _text.Color = urgent ? Palette.Perigo : Palette.Ambar;
        c.DrawText(Digits[sec], (bx + 6.5f) * _s, (by + 4.5f + TdBand.EdgeHeadroomDip) * _s + font.Size * 0.35f, SKTextAlign.Center, font, _text);
        c.Restore();
    }

    private static SKPath Poly(params float[] xy)
    {
        using var b = new SKPathBuilder();
        b.MoveTo(xy[0], xy[1]);
        for (int i = 2; i < xy.Length; i += 2) b.LineTo(xy[i], xy[i + 1]);
        b.Close();
        return b.Detach();
    }

    /// <summary>Desenha o contorno unitário <paramref name="shape"/> centrado em (x, y): preenchimento (se <paramref name="fillIt"/>) e borda.</summary>
    private void Shape(SKCanvas c, SKPath shape, float x, float y, bool fillIt, bool strokeIt)
    {
        c.Save();
        c.Translate(x, y);
        if (fillIt) c.DrawPath(shape, _fill);
        if (strokeIt) c.DrawPath(shape, _stroke);
        c.Restore();
    }

    private static string[] BuildDigits()
    {
        var d = new string[100];
        for (int i = 0; i < d.Length; i++) d[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return d;
    }

    public void Dispose()
    {
        ReleaseSprites();
        if (_ownsAtlas) _atlas.Dispose();
        _img.Dispose();
        _fill.Dispose();
        _stroke.Dispose();
        _text.Dispose();
        _diamond.Dispose(); _triangle.Dispose(); _cross.Dispose(); _bolt.Dispose(); _riskMark.Dispose();
        _dash.Dispose();
    }
}
