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
        for (int i = 0; i < hud.Items.Length; i++)
        {
            DrawHudItem(c, hud.Items[i], i == hover);
            if (hud.Items[i].Highlight) DrawHighlight(c, hud.Items[i].Rect);
        }
    }

    /// <summary>Destaque do alvo do tutorial (TD.15): moldura âmbar de 2 DIP em volta do item real.</summary>
    private void DrawHighlight(SKCanvas c, DipRect r)
    {
        const float t = 2f, o = 2f;
        _p.Fill(c, new DipRect(r.X - o, r.Y - o, r.W + 2 * o, t), Palette.Ambar);
        _p.Fill(c, new DipRect(r.X - o, r.Y + r.H + o - t, r.W + 2 * o, t), Palette.Ambar);
        _p.Fill(c, new DipRect(r.X - o, r.Y - o, t, r.H + 2 * o), Palette.Ambar);
        _p.Fill(c, new DipRect(r.X + r.W + o - t, r.Y - o, t, r.H + 2 * o), Palette.Ambar);
    }

    // Seta do balão do tutorial: triângulo âmbar abaixo da caixa, apontando para o alvo (HUD ou faixa).
    private void DrawArrow(SKCanvas c, float xDip, float topDip)
    {
        float x = _p.Px(xDip), y = _p.Px(topDip), h = _p.Px(TdPopoverView.ArrowH - 1f), hw = _p.Px(5f);
        using var b = new SKPathBuilder();
        b.MoveTo(x - hw, y);
        b.LineTo(x + hw, y);
        b.LineTo(x, y + h);
        b.Close();
        using var path = b.Detach();
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Palette.Ambar };
        c.DrawPath(path, fill);
    }

    /// <summary>Largura (DIP) da janela do tooltip; o texto quebra em linhas dentro dela.</summary>
    public const float TooltipWidthDip = 230f;
    private const float TipFont = 9f, TipLine = 12f;

    private List<string> TipLines(string text)
    {
        var lines = new List<string>(4);
        if (!_p.TextEnabled) { lines.Add(text.Length > 40 ? text[..40] : text); return lines; }
        var font = _p.Font(PanelFont.JetBrainsMono, TipFont);
        float max = _p.Px(TooltipWidthDip - 12f);
        string line = "";
        foreach (var w in text.Split(' '))
        {
            var test = line.Length == 0 ? w : line + " " + w;
            if (line.Length > 0 && font.MeasureText(test) > max) { lines.Add(line); line = w; } else line = test;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    /// <summary>Altura (DIP) da janela do tooltip para o texto.</summary>
    public float TooltipHeightDip(string text) => TipLines(text).Count * TipLine + 10f;

    public void DrawTooltip(SKCanvas c, string text, bool firstTimeTip = false)
    {
        c.Clear(SKColors.Transparent);
        var lines = TipLines(text);
        _p.Box(c, new DipRect(0, 0, TooltipWidthDip, lines.Count * TipLine + 10f), firstTimeTip ? Palette.Ambar : Palette.AzulClaro, Palette.BgPainel);
        for (int i = 0; i < lines.Count; i++)
            _p.Text(c, lines[i], PanelFont.JetBrainsMono, TipFont, new DipRect(6, 5 + i * TipLine, TooltipWidthDip - 12f, TipLine), Palette.TxtPrincipal);
    }

    private void DrawIcon(SKCanvas c, TdIcon icon, float xDip, float yDip, float sizeDip, SKColor color) =>
        TdIconPainter.Draw(c, icon, _p.Px(xDip), _p.Px(yDip), _p.Px(sizeDip), color, Palette.Ambar);

    /// <summary>Item do HUD (TD.14): rótulo/botão com ícone à esquerda, texto legível (JetBrains Mono), bônus com moeda e recarga em barra.</summary>
    private void DrawHudItem(SKCanvas c, in TdUiItem it, bool hover)
    {
        var r = it.Rect;
        var color = ToneColor(it.Tone);
        float fs = it.Kind == TdUiKind.Button && r.W <= 80f ? 8.5f : 9f;
        bool isButton = it.Kind == TdUiKind.Button;
        bool enabled = it.Enabled && it.Action != TdUiAction.None;
        if (isButton)
        {
            var border = it.Selected ? Palette.Ambar : enabled ? (hover ? Palette.AzulClaro : Palette.BordaForte) : Palette.BordaPainel;
            var fill = it.Selected || (hover && enabled) ? Palette.BgCard : Palette.BgItem;
            _p.Box(c, r, border, fill);
            if (it.Fill > 0f) _p.Fill(c, new DipRect(r.X + 1, r.Y + r.H - 3, (r.W - 2) * it.Fill, 2), Palette.Azul);
            if (!enabled && it.Tone != TdTone.Dim) color = Palette.TxtRotulo;
        }
        float iconSize = Math.Min(15f, r.H - 8f);
        float x = r.X + 5f;
        bool iconOnly = it.Text.Length == 0;
        if (it.Icon != TdIcon.None)
        {
            float ix = iconOnly ? r.X + (r.W - iconSize) / 2f : x;
            DrawIcon(c, it.Icon, ix, r.Y + (r.H - iconSize) / 2f, iconSize, color);
            x += iconSize + 4f;
        }
        if (iconOnly) return;
        float rightW = 0f;
        if (it.RightIcon != TdIcon.None)
        {
            DrawIcon(c, it.RightIcon, r.X + r.W - 17f, r.Y + (r.H - 12f) / 2f, 12f, enabled ? Palette.Ambar : color);
            rightW += 16f;
        }
        if (it.Right is not null)
        {
            rightW += (_p.TextEnabled ? _p.Font(PanelFont.JetBrainsMono, fs).MeasureText(it.Right) / (_p.Dpi / 96f) : 18f) + (it.RightCoin ? 12f : 0f) + 3f;
            float rx = r.X + r.W - rightW - 3f;
            _p.Text(c, it.Right, PanelFont.JetBrainsMono, fs, new DipRect(rx, r.Y + (r.H - 12f) / 2f, rightW - (it.RightCoin ? 12f : 0f), 12f), enabled ? Palette.Ambar : Palette.TxtRotulo);
            if (it.RightCoin) DrawIcon(c, TdIcon.Coin, r.X + r.W - 15f, r.Y + (r.H - 10f) / 2f, 10f, Palette.Ambar);
        }
        _p.Text(c, it.Text, PanelFont.JetBrainsMono, fs, new DipRect(x, r.Y + (r.H - 12f) / 2f, r.W - (x - r.X) - rightW - 2f, 12f), color);
    }

    public void DrawPopover(SKCanvas c, TdPopoverView v, int hover)
    {
        c.Clear(SKColors.Transparent);
        float boxH = v.HasArrow ? v.Height - TdPopoverView.ArrowH : v.Height;
        _p.Box(c, new DipRect(0, 0, v.Width, boxH), v.HasArrow ? Palette.Ambar : Palette.BordaForte, Palette.BgPainel);
        if (v.HasArrow) DrawArrow(c, Math.Clamp(v.ArrowX, 8f, v.Width - 8f), boxH);
        float titleX = TdUiController.Pad + 2;
        if (v.IconTower is not null)
        {
            DrawTowerIcon(c, v.IconTower, 4, 2, HeaderIconDip, 255);
            titleX += 22;
        }
        _p.Text(c, v.Title, PanelFont.JetBrainsMono, 10.5f, new DipRect(titleX, 5, v.Width - 40 - (titleX - TdUiController.Pad - 2), 14), Palette.TxtPrincipal);
        _p.Fill(c, new DipRect(1, TdUiController.HeaderH, v.Width - 2, 1), Palette.BordaPainel);
        for (int i = 0; i < v.Items.Length; i++)
        {
            DrawItem(c, v.Items[i], i == hover, v.Width, small: false);
            if (v.Items[i].Highlight) DrawHighlight(c, v.Items[i].Rect);
        }
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
                if (it.Icon != TdIcon.None)
                {
                    DrawIcon(c, it.Icon, r.X, r.H >= 24f ? r.Y + 1f : r.Y + (r.H - 12f) / 2f, 12f, color);
                    r = new DipRect(r.X + 16f, r.Y, r.W - 16f, r.H);
                }
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
        if (it.Icon != TdIcon.None && it.Action != TdUiAction.ChooseTower)
        {
            DrawIcon(c, it.Icon, r.X + 5f, r.Y + (r.H - 12f) / 2f, 12f, textColor);
            shift = 17f;
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
