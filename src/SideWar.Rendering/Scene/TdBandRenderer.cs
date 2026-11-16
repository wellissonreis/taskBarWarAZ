using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Td;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>
/// Faixa TD de 32 DIP (TD.7, SDD-28 RF-28.15 com o fallback Band de RF-28.18): trilho dos inimigos no topo (núcleo à esquerda, portal à
/// direita), torres embaixo. Lê o <see cref="TdMatchState"/> (só leitura) e desenha num <see cref="SKCanvas"/> transparente.
/// Legibilidade a 32 DIP: tipo de inimigo por <b>forma + cor de borda</b> (solo cinza/círculo, voador ciano/losango elevado, blindado
/// âmbar/quadrado, regenera verde/triângulo, drena magenta/cruz, chefe vermelho/grande com barra), barra de vida só para chefe e elite
/// danificado, torre com pontos de nível e ponto de cor do ramo, alcance como barra sobre o trilho, nós do balanceador, caixa-bomba com
/// contagem e a torre em risco marcada, torres SEM POSIÇÃO como chips tracejados. Paints/paths pré-criados; sem alocação por frame em regime
/// (os números da contagem vêm de uma tabela). Não thread-safe (T-Game).
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
    private readonly SKPathEffect _dash = SKPathEffect.CreateDash([2f, 2f], 0);
    private TdContent? _content;
    private byte[] _traits = [];
    private int _enemyDefs;
    private int _dpi = 96;
    private float _s = 1f;

    public TdBandRenderer(Typography typography)
    {
        _typography = typography ?? throw new ArgumentNullException(nameof(typography));
    }

    public int Dpi => _dpi;

    /// <summary>DPI do monitor (muda o tamanho físico das fontes; recriar bitmaps/superfície fora daqui).</summary>
    public void SetDpi(int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        _dpi = dpi;
        _s = dpi / 96f;
        _typography.Clear();
    }

    /// <summary>Tamanho físico da janela para uma largura de faixa em DIP.</summary>
    public (int Width, int Height) PhysicalSize(int widthDip) => ((int)MathF.Round(widthDip * _s), (int)MathF.Round(TdBand.HeightDip * _s));

    public void Draw(SKCanvas canvas, TdMatchState match, double now, in TdView view)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(match);
        EnsureContent(match.Content);
        canvas.Clear(SKColors.Transparent);
        int w = TdBand.WidthDip(match);
        canvas.Save();
        canvas.Scale(_s);

        // Alfa 1/255 em toda a faixa: o Windows só entrega o mouse (modo Interativo, TD.8) a pixels com alfa > 0; invisível a olho nu.
        _fill.Color = new SKColor(0, 0, 0, 1);
        canvas.DrawRect(0, 0, w, TdBand.HeightDip, _fill);

        DrawRail(canvas, match, w);
        DrawSelectedRange(canvas, match, view, w);
        DrawZones(canvas, match, w);
        DrawCore(canvas, match);
        DrawPortal(canvas, w);
        DrawNodes(canvas, match, w);
        DrawEnemies(canvas, match, w);
        DrawTowers(canvas, match, view, w);
        DrawUnplaced(canvas, match, view);
        DrawGhost(canvas, match, view, w);
        DrawFire(canvas, match, w);
        DrawCrate(canvas, match, view, now, w);
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
    }

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
        _fill.Color = Palette.Td.RailFill;
        c.DrawRect(0, TdBand.RailTop, w, TdBand.RailHeight, _fill);
        _stroke.Color = m.Phase == MatchPhase.Onda ? Palette.BordaForte : Palette.BordaPainel;
        _stroke.PathEffect = m.Phase == MatchPhase.Onda ? null : _dash;
        c.DrawLine(0, TdBand.RailTop + 0.5f, w, TdBand.RailTop + 0.5f, _stroke);
        c.DrawLine(0, TdBand.RailTop + TdBand.RailHeight - 0.5f, w, TdBand.RailTop + TdBand.RailHeight - 0.5f, _stroke);
        _stroke.PathEffect = null;
        if (m.Phase is MatchPhase.Vitoria or MatchPhase.Derrota)
        {
            _fill.Color = m.Phase == MatchPhase.Vitoria ? Palette.Menta : Palette.Perigo;
            c.DrawRect(0, TdBand.HeightDip - 1, w, 1, _fill);
        }
    }

    private void DrawCore(SKCanvas c, TdMatchState m)
    {
        _fill.Color = Palette.BgPainel;
        c.DrawRoundRect(1, 1, 20, 28, 3, 3, _fill);
        _stroke.Color = Palette.Placeholder.Core;
        _stroke.StrokeWidth = 1.5f;
        c.DrawRoundRect(1.75f, 1.75f, 18.5f, 26.5f, 3, 3, _stroke);
        _stroke.StrokeWidth = 1f;
        float frac = m.IntegrityMax > 0 ? Math.Clamp(m.Integrity / (float)m.IntegrityMax, 0f, 1f) : 0f;
        _fill.Color = frac > 0.5f ? Palette.Menta : frac > 0.25f ? Palette.Ambar : Palette.Perigo;
        float h = 22f * frac;
        if (h > 0) c.DrawRect(5, 4 + (22f - h), 12, h, _fill);
    }

    private void DrawPortal(SKCanvas c, int w)
    {
        _fill.Color = Palette.Td.PortalGlow;
        c.DrawOval(w - 11f, 7f, 9f, 13f, _fill);
        _stroke.Color = Palette.Placeholder.Portal;
        _stroke.StrokeWidth = 1.5f;
        c.DrawOval(w - 11f, 7f, 8f, 12f, _stroke);
        _stroke.StrokeWidth = 1f;
    }

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
            _fill.Color = color.WithAlpha(48);
            c.DrawRect(x0, TdBand.RailTop, x1 - x0, TdBand.RailHeight, _fill);
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
            c.DrawRect(x - r, TdBand.RailTop, 2 * r, TdBand.RailHeight, _fill);
        }
    }

    private void DrawNodes(SKCanvas c, TdMatchState m, int w)
    {
        var nodes = m.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            float x = TdBand.ToDip(nodes[i].X, w);
            _fill.Color = Palette.Menta;
            c.DrawRoundRect(x - 2.5f, TdBand.RailTop + 1.5f, 5, 7, 1, 1, _fill);
            _fill.Color = Palette.BgAbismo;
            c.DrawRect(x - 0.5f, TdBand.RailTop + 3f, 1, 4, _fill);
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
            float cy = TdBand.RailTop + TdBand.RailHeight / 2f;
            bool flying = e.Flying || (bits & FlyingBit) != 0;
            if (flying) cy -= 2.5f;
            SKColor border = e.IsBoss ? Palette.Perigo : flying ? Palette.Gelo : (bits & ArmoredBit) != 0 ? Palette.Ambar
                : (bits & RegenBit) != 0 ? Palette.Menta : (bits & DrainBit) != 0 ? Palette.Td.EnemyDrain : Palette.Td.EnemySolo;

            _fill.Color = Palette.BgAbismo;
            _stroke.Color = border;
            _stroke.StrokeWidth = e.IsBoss ? 2f : 1.5f;
            if (e.IsBoss)
            {
                c.DrawRoundRect(x - 6, cy - 6, 12, 12, 3, 3, _fill);
                c.DrawRoundRect(x - 6, cy - 6, 12, 12, 3, 3, _stroke);
                _fill.Color = border;
                c.DrawCircle(x, cy, 2f, _fill);
            }
            else if (flying)
            {
                Shape(c, _diamond, x, cy, true, true);
            }
            else if ((bits & ArmoredBit) != 0)
            {
                c.DrawRect(x - 3.5f, cy - 3.5f, 7, 7, _fill);
                c.DrawRect(x - 3.5f, cy - 3.5f, 7, 7, _stroke);
            }
            else if ((bits & RegenBit) != 0)
            {
                Shape(c, _triangle, x, cy, true, true);
            }
            else if ((bits & DrainBit) != 0)
            {
                Shape(c, _cross, x, cy, true, true);
            }
            else
            {
                c.DrawCircle(x, cy, 3.5f, _fill);
                c.DrawCircle(x, cy, 3.5f, _stroke);
            }
            _stroke.StrokeWidth = 1f;

            if (e.ParalyzeTicks > 0)
            {
                _stroke.Color = Palette.TxtBranco;
                c.DrawCircle(x, cy, e.IsBoss ? 7.5f : 5f, _stroke);
            }
            else if (e.SlowTicks > 0 && e.SlowMult < 1.0)
            {
                _fill.Color = Palette.Gelo;
                c.DrawRect(x - 3, TdBand.RailTop + TdBand.RailHeight - 1.5f, 6, 1.5f, _fill);
            }

            if (e.IsBoss || (e.Hp < e.MaxHp && e.MaxHp >= 90))
                DrawLifeBar(c, x, e.IsBoss ? 0f : cy - 7f, e.IsBoss ? 14f : 8f, (float)(e.Hp / e.MaxHp), e.IsBoss);
        }
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
            DrawTowerPlate(c, cx, TdBand.TowerTop, t, view.SelectedTowerId == t.Id, view.HoverTowerId == t.Id, risk == t.Id, false);
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
            _fill.Color = color.WithAlpha(48);
            c.DrawRect(x0, TdBand.RailTop, x1 - x0, TdBand.RailHeight, _fill);
            _fill.Color = color.WithAlpha(210);
            c.DrawRect(x0, 0, x1 - x0, 2, _fill);
        }
        float x = cx - TdBand.TowerWidth / 2f;
        _fill.Color = color.WithAlpha((byte)(view.ChosenX >= 0 ? 110 : 70));
        c.DrawRoundRect(x, TdBand.TowerTop, TdBand.TowerWidth, TdBand.TowerHeight, 3, 3, _fill);
        _stroke.Color = color;
        _stroke.PathEffect = _dash;
        c.DrawRoundRect(x + 0.5f, TdBand.TowerTop + 0.5f, TdBand.TowerWidth - 1, TdBand.TowerHeight - 1, 3, 3, _stroke);
        _stroke.PathEffect = null;
    }

    private void DrawTowerPlate(SKCanvas c, float cx, float top, in TdTower t, bool selected, bool hover, bool atRisk, bool unplaced)
    {
        var color = TowerColor(t.DefIndex);
        float x = cx - TdBand.TowerWidth / 2f;
        bool disabled = t.DisabledTicks > 0;
        _fill.Color = disabled ? Palette.Td.Plate.WithAlpha(150) : Palette.Td.Plate;
        c.DrawRoundRect(x, top, TdBand.TowerWidth, TdBand.TowerHeight, 3, 3, _fill);
        _stroke.Color = selected ? Palette.TxtBranco : hover ? Palette.AmbarHover : t.Level >= 5 ? Palette.Ambar : color.WithAlpha(190);
        _stroke.StrokeWidth = selected ? 1.5f : 1f;
        c.DrawRoundRect(x + 0.5f, top + 0.5f, TdBand.TowerWidth - 1, TdBand.TowerHeight - 1, 3, 3, _stroke);
        _stroke.StrokeWidth = 1f;
        DrawTowerGlyph(c, cx, top + 7f, _content!.Towers[t.DefIndex].Id, disabled ? color.WithAlpha(90) : color);

        // pontos de nível (1 a 5) na base
        int level = Math.Clamp(t.Level, 1, 5);
        float px = cx - (level * 3f - 1f) / 2f;
        _fill.Color = Palette.TxtPrincipal;
        for (int i = 0; i < level; i++) c.DrawRect(px + i * 3f, top + TdBand.TowerHeight - 3.5f, 2f, 2f, _fill);

        // ponto de cor do ramo (A âmbar, B violeta)
        if (t.Branch != 0)
        {
            _fill.Color = t.Branch == 1 ? Palette.Ambar : Palette.Prestigio;
            c.DrawCircle(x + TdBand.TowerWidth - 3.5f, top + 3.5f, 2f, _fill);
        }
        if (disabled)
        {
            _stroke.Color = Palette.Perigo;
            c.DrawLine(x + 3, top + 3, x + TdBand.TowerWidth - 3, top + TdBand.TowerHeight - 3, _stroke);
        }
        if (atRisk)
        {
            _stroke.Color = Palette.Perigo;
            _stroke.StrokeWidth = 2f;
            c.DrawRoundRect(x - 1, top - 1, TdBand.TowerWidth + 2, TdBand.TowerHeight + 2, 4, 4, _stroke);
            _stroke.StrokeWidth = 1f;
        }
    }

    private void DrawTowerGlyph(SKCanvas c, float cx, float cy, string id, SKColor color)
    {
        _fill.Color = color;
        _stroke.Color = color;
        switch (id)
        {
            case "bolt":
                Shape(c, _bolt, cx, cy, true, false);
                break;
            case "frost":
                c.DrawLine(cx - 4.5f, cy, cx + 4.5f, cy, _stroke);
                c.DrawLine(cx - 2.5f, cy - 4, cx + 2.5f, cy + 4, _stroke);
                c.DrawLine(cx - 2.5f, cy + 4, cx + 2.5f, cy - 4, _stroke);
                break;
            case "cannon":
                c.DrawCircle(cx - 1, cy + 1.5f, 3.5f, _fill);
                c.DrawRect(cx, cy - 3.5f, 5, 2.5f, _fill);
                break;
            case "scaleset":
                c.DrawRect(cx - 4.5f, cy - 4, 9, 2, _fill);
                c.DrawRect(cx - 3.5f, cy - 0.5f, 7, 2, _fill);
                c.DrawRect(cx - 2.5f, cy + 3f, 5, 2, _fill);
                break;
            default: // balancer: dois nós ligados
                c.DrawCircle(cx - 3.5f, cy, 2f, _fill);
                c.DrawCircle(cx + 3.5f, cy, 2f, _fill);
                c.DrawLine(cx - 2, cy, cx + 2, cy, _stroke);
                break;
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
            DrawTowerGlyph(c, x + TdBand.UnplacedChipWidth / 2f, TdBand.UnplacedChipTop + TdBand.UnplacedChipHeight / 2f, _content!.Towers[t.DefIndex].Id, color.WithAlpha(150));
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
            double mult = 1.0;
            if (t.Branch is 1 or 2 && t.Branch <= def.Branches.Length &&
                def.Branches[t.Branch - 1].Stats.TryGetValue("intervalMultiplier", out var im)) mult = im;
            int intervalTicks = Math.Max(1, (int)Math.Round(def.Interval * mult * TdWorld.TicksPerSecond));
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
            float ty = TdBand.RailTop + TdBand.RailHeight / 2f - (target.Flying ? 2.5f : 0f);
            _stroke.Color = TowerColor(t.DefIndex).WithAlpha(220);
            c.DrawLine(cx, TdBand.TowerTop + 1, tx, ty, _stroke);
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
        float x = cx - TdBand.CrateSize / 2f;

        // laço até a torre em risco (a mais próxima, calculada pelo Core)
        int risk = m.CrateTargetTowerId;
        if (risk != 0)
        {
            for (int i = 0; i < m.Towers.Count; i++)
            {
                var t = m.Towers[i];
                if (t.Id != risk) continue;
                float tx = TdBand.TowerCenterDip(m, t, w);
                _fill.Color = Palette.Perigo;
                Shape(c, _riskMark, tx, TdBand.TowerTop, true, false);
                break;
            }
        }

        _fill.Color = blink ? Palette.Perigo : Palette.Placeholder.SupplyCrate;
        c.DrawRoundRect(x, TdBand.CrateTop, TdBand.CrateSize, TdBand.CrateSize, 3, 3, _fill);
        _stroke.Color = view.CrateHover ? Palette.TxtBranco : urgent ? Palette.Perigo : Palette.Ambar;
        _stroke.StrokeWidth = 1.5f;
        c.DrawRoundRect(x + 0.75f, TdBand.CrateTop + 0.75f, TdBand.CrateSize - 1.5f, TdBand.CrateSize - 1.5f, 3, 3, _stroke);
        _stroke.StrokeWidth = 1f;

        // contagem regressiva em texto (física: fora da escala do canvas)
        int sec = Math.Clamp((int)Math.Ceiling(left), 0, Digits.Length - 1);
        c.Save();
        c.ResetMatrix();
        var font = _typography.GetFont(FontRole.SilkscreenBold, 8, _dpi);
        _text.Color = Palette.Placeholder.Edge;
        c.DrawText(Digits[sec], cx * _s, (TdBand.CrateTop + TdBand.CrateSize / 2f) * _s + font.Size * 0.35f, SKTextAlign.Center, font, _text);
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
        _fill.Dispose();
        _stroke.Dispose();
        _text.Dispose();
        _diamond.Dispose(); _triangle.Dispose(); _cross.Dispose(); _bolt.Dispose(); _riskMark.Dispose();
        _dash.Dispose();
    }
}
