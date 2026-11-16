using TdArtMap = SideWar.Rendering.Scene.TdArtMap;
using TdSpriteBaker = SideWar.Rendering.Scene.TdSpriteBaker;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Td;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>
/// Desenha o HUD compacto e o popover da partida TD (TD.8) a partir dos modelos puros de layout (<see cref="TdHudView"/>,
/// <see cref="TdPopoverView"/>). Sem lógica de jogo: só paleta, caixas e texto (JetBrains Mono para texto com acento, Silkscreen para rótulos ASCII).
/// </summary>
public sealed class TdUiRenderer(Typography typography, int dpi, SpriteAtlas? atlas = null) : IDisposable
{
    private const float ChooserIconDip = 30f, HeaderIconDip = 18f;
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest);
    private readonly PanelPainter _p = new(typography, dpi);
    private readonly SpriteAtlas _atlas = atlas ?? SpriteAtlas.LoadEmbedded();
    private readonly bool _ownsAtlas = atlas is null;
    private readonly Dictionary<(string Id, int Px), SKImage> _icons = [];

    /// <summary>Falso = layout idêntico sem glifos (goldens comparáveis entre SOs).</summary>
    public bool TextEnabled { get => _p.TextEnabled; set => _p.TextEnabled = value; }

    public (int W, int H) PhysicalSize(float wDip, float hDip) => ((int)_p.Px(wDip), (int)_p.Px(hDip));

    public void DrawHud(SKCanvas c, TdHudView hud, int hover)
    {
        c.Clear(SKColors.Transparent);
        _p.Box(c, new DipRect(0, 0, hud.Width, hud.Height), Palette.BordaForte, Palette.Td.Plate);
        for (int i = 0; i < hud.Items.Length; i++) DrawItem(c, hud.Items[i], i == hover, hud.Width, small: true);
    }

    public void DrawPopover(SKCanvas c, TdPopoverView v, int hover)
    {
        c.Clear(SKColors.Transparent);
        _p.Box(c, new DipRect(0, 0, v.Width, v.Height), Palette.BordaForte, Palette.BgPainel);
        float titleX = TdUiController.Pad + 2;
        if (v.IconTower is not null)
        {
            DrawTowerIcon(c, v.IconTower, 4, 2, HeaderIconDip, 255);
            titleX += 22;
        }
        _p.Text(c, v.Title, PanelFont.JetBrainsMono, 10.5f, new DipRect(titleX, 5, v.Width - 40 - (titleX - TdUiController.Pad - 2), 14), Palette.TxtPrincipal);
        _p.Fill(c, new DipRect(1, TdUiController.HeaderH, v.Width - 2, 1), Palette.BordaPainel);
        for (int i = 0; i < v.Items.Length; i++) DrawItem(c, v.Items[i], i == hover, v.Width, small: false);
    }

    private static SKColor ToneColor(TdTone t) => t switch
    {
        TdTone.Good => Palette.Menta, TdTone.Warn => Palette.Ambar, TdTone.Bad => Palette.Perigo, TdTone.Accent => Palette.Ambar,
        TdTone.Dim => Palette.TxtRotulo, _ => Palette.TxtPrincipal,
    };

    private void DrawItem(SKCanvas c, in TdUiItem it, bool hover, float width, bool small)
    {
        var r = it.Rect;
        float size = small ? 9f : 9.5f;
        var color = ToneColor(it.Tone);
        switch (it.Kind)
        {
            case TdUiKind.Separator:
                _p.Fill(c, new DipRect(r.X, r.Y, r.W, 1), Palette.BordaPainel);
                return;
            case TdUiKind.Header:
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 9f, r, Palette.TxtRotulo);
                return;
            case TdUiKind.Badges:
                DrawBadges(c, it);
                return;
            case TdUiKind.Bar:
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 9f, r, color);
                if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 8f, r, Palette.TxtRotulo, TextAlign.Right);
                _p.Fill(c, new DipRect(r.X, r.Y + r.H - 3, r.W, 3), Palette.BgTrilho);
                if (it.Fill > 0f) _p.Fill(c, new DipRect(r.X, r.Y + r.H - 3, r.W * Math.Clamp(it.Fill, 0f, 1f), 3), Palette.Ambar);
                return;
            case TdUiKind.Label:
                if (!small && r.H >= 24f && _p.TextEnabled && _p.Font(PanelFont.JetBrainsMono, size).MeasureText(it.Text) > _p.Px(r.W))
                {
                    DrawWrapped(c, it.Text, r, color);
                    return;
                }
                _p.Text(c, it.Text, small ? PanelFont.Silkscreen : PanelFont.JetBrainsMono, small ? 9f : size, r, color);
                if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 8.5f, r, Palette.TxtRotulo, TextAlign.Right);
                return;
        }
        bool enabled = it.Enabled && it.Action != TdUiAction.None;
        var border = it.Selected ? Palette.Ambar : enabled ? (hover ? Palette.AzulClaro : Palette.BordaForte) : Palette.BordaPainel;
        var fill = it.Selected ? Palette.BgCard : hover && enabled ? Palette.BgCard : Palette.BgItem;
        _p.Box(c, r, border, fill);
        if (it.Fill > 0f) _p.Fill(c, new DipRect(r.X + 1, r.Y + r.H - 3, (r.W - 2) * it.Fill, 2), Palette.Azul);
        var textColor = enabled ? color : Palette.TxtRotulo;
        float top = it.Sub is not null && !small ? r.Y + 3 : r.Y + (r.H - 12) / 2f;
        // TD.9: torre do design à esquerda do botão de construção (apagada se bloqueada/sem créditos)
        float shift = 0f;
        if (it.Action == TdUiAction.ChooseTower && it.SArg is not null)
        {
            DrawTowerIcon(c, it.SArg, r.X + 4, r.Y + (r.H - ChooserIconDip) / 2f, ChooserIconDip, enabled ? (byte)255 : (byte)110);
            shift = 36f;
        }
        var box = new DipRect(r.X + 4 + shift, top, r.W - 8 - shift, 12);
        _p.Text(c, it.Text, small ? PanelFont.Silkscreen : PanelFont.JetBrainsMono, small ? 8.5f : size, box, textColor, small && it.Right is null ? TextAlign.Center : TextAlign.Left);
        if (it.Right is not null) _p.Text(c, it.Right, small ? PanelFont.Silkscreen : PanelFont.JetBrainsMono, small ? 8.5f : size, box, enabled ? Palette.Ambar : Palette.TxtRotulo, TextAlign.Right);
        if (it.Sub is not null && !small) DrawWrapped(c, it.Sub, new DipRect(r.X + 4 + shift, r.Y + 16, r.W - 8 - shift, r.H - 17), Palette.TxtTerciario);
    }

    private static readonly (int Bit, string Glyph)[] MedalGlyphs = [(1, "V"), (2, "H"), (4, "P"), (8, "D")];

    private static SKColor MedalColor(int bit) => bit switch { 1 => Palette.Menta, 2 => Palette.AzulClaro, 4 => Palette.Prestigio, _ => Palette.Gelo };

    /// <summary>Coroa dourada (acesa se <c>Selected</c>) e 4 medalhas com letra (V sem vender, H sem habilidades, P pressa, D desafio): ganhas coloridas, demais apagadas.</summary>
    private void DrawBadges(SKCanvas c, in TdUiItem it)
    {
        var r = it.Rect;
        DrawCrown(c, r.X + 2, r.Y + 1, 16f, 11f, it.Selected);
        float x = r.X + 24;
        foreach (var (bit, glyph) in MedalGlyphs)
        {
            bool got = (it.Arg & bit) != 0;
            DrawMedal(c, x, r.Y + 1, 12f, MedalColor(bit), got, glyph);
            x += 15f;
        }
        if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 8.5f, r, Palette.TxtRotulo, TextAlign.Right);
    }

    private void DrawCrown(SKCanvas c, float xDip, float yDip, float wDip, float hDip, bool on)
    {
        float x = _p.Px(xDip), y = _p.Px(yDip), w = _p.Px(wDip), h = _p.Px(hDip);
        using var b = new SKPathBuilder();
        b.MoveTo(x, y + h);
        b.LineTo(x, y + h * 0.25f);
        b.LineTo(x + w * 0.25f, y + h * 0.6f);
        b.LineTo(x + w * 0.5f, y);
        b.LineTo(x + w * 0.75f, y + h * 0.6f);
        b.LineTo(x + w, y + h * 0.25f);
        b.LineTo(x + w, y + h);
        b.Close();
        using var path = b.Detach();
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = on ? Palette.Ambar : Palette.BgTrilho };
        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1f, _p.Px(1f)), Color = on ? Palette.AmbarHover : Palette.BordaForte };
        c.DrawPath(path, fill);
        c.DrawPath(path, line);
    }

    private void DrawMedal(SKCanvas c, float xDip, float yDip, float sizeDip, SKColor color, bool on, string glyph)
    {
        float s = _p.Px(sizeDip);
        float cx = _p.Px(xDip) + s / 2f, cy = _p.Px(yDip) + s / 2f;
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = on ? color : Palette.BgTrilho };
        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1f, _p.Px(1f)), Color = on ? Palette.TxtBranco : Palette.BordaForte };
        c.DrawCircle(cx, cy, s / 2f - 0.5f, fill);
        c.DrawCircle(cx, cy, s / 2f - 0.5f, line);
        _p.Text(c, glyph, PanelFont.Silkscreen, 7f, new DipRect(xDip, yDip + 1.5f, sizeDip, sizeDip - 2f), on ? Palette.BgAbismo : Palette.TxtRotulo, TextAlign.Center);
    }

    // Ícone da torre (sprite do design, TdArtMap) pré-escalado por altura física e cacheado: desenhado em pixels inteiros.
    private void DrawTowerIcon(SKCanvas c, string towerId, float xDip, float yDip, float heightDip, byte alpha)
    {
        if (!TdArtMap.TryTower(towerId, out var e)) return;
        int hpx = Math.Max(1, (int)MathF.Round(_p.Px(heightDip)));
        if (!_icons.TryGetValue((towerId, hpx), out var img))
        {
            img = TdSpriteBaker.ByHeight(_atlas, e.Sprite, hpx, e.Tint, e.TintAlpha);
            _icons[(towerId, hpx)] = img;
        }
        using var paint = new SKPaint { IsAntialias = false, Color = Palette.TxtBranco.WithAlpha(alpha) };
        float x = MathF.Round(_p.Px(xDip)) + MathF.Max(0f, MathF.Round((_p.Px(heightDip) - img.Width) / 2f));
        c.DrawImage(img, x, MathF.Round(_p.Px(yDip)), Nearest, paint);
    }

    // Duas linhas no máximo (quebra por palavra; reticências no excesso): RNF-28.3 pede a fraqueza completa legível.
    private void DrawWrapped(SKCanvas c, string text, DipRect box, SKColor color)
    {
        if (!_p.TextEnabled) return;
        var font = _p.Font(PanelFont.JetBrainsMono, 8f);
        float max = _p.Px(box.W);
        var words = text.Split(' ');
        var line = "";
        int n = 0;
        foreach (var w in words)
        {
            var test = line.Length == 0 ? w : line + " " + w;
            if (line.Length > 0 && font.MeasureText(test) > max)
            {
                _p.Text(c, line, PanelFont.JetBrainsMono, 8f, new DipRect(box.X, box.Y + n * 9.5f, box.W, 9), color);
                line = w;
                if (++n == 1) continue;
            }
            else line = test;
        }
        if (n < 2 && line.Length > 0) _p.Text(c, line, PanelFont.JetBrainsMono, 8f, new DipRect(box.X, box.Y + n * 9.5f, box.W, 9), color);
    }

    public void Dispose()
    {
        foreach (var i in _icons.Values) i.Dispose();
        _icons.Clear();
        _p.Dispose();
        if (_ownsAtlas) _atlas.Dispose();
    }
}
