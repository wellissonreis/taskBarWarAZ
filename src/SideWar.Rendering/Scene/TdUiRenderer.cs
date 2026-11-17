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
        RoundBox(c, new DipRect(0, 0, hud.Width, hud.Height), Palette.Td.Border, Palette.Td.Surface, 5);
        for (int i = 0; i < hud.Items.Length; i++)
        {
            DrawHudItem(c, hud.Items[i], i == hover);
            if (i == 0 && hud.TotalWaves > 0)
            {
                int bars = Math.Min(hud.TotalWaves, 20);
                for (int b = 0; b < bars; b++)
                    _p.Fill(c, new DipRect(104 + b * 5, 23, 3, 2), b < hud.Wave ? Palette.Td.Amber : Palette.Td.BorderStrong);
            }
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
            var border = enabled && it.Action == TdUiAction.CollectCrate ? Palette.Td.Amber : Palette.Td.BorderStrong;
            var fill = enabled && it.Action == TdUiAction.CollectCrate ? Palette.Td.Amber : hover && enabled ? Palette.Td.BorderStrong : Palette.Td.Surface;
            RoundBox(c, r, border, fill, 4);
            if (it.Action == TdUiAction.CollectCrate && enabled) color = Palette.Td.Background;
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

    private void RoundBox(SKCanvas c, DipRect r, SKColor border, SKColor fill, float radius)
    {
        var rect = new SKRect(_p.Px(r.X), _p.Px(r.Y), _p.Px(r.X + r.W), _p.Px(r.Y + r.H));
        using var paint = new SKPaint { IsAntialias = true, Color = fill };
        c.DrawRoundRect(rect, _p.Px(radius), _p.Px(radius), paint);
        paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = Math.Max(1, _p.Px(1)); paint.Color = border;
        c.DrawRoundRect(rect, _p.Px(radius), _p.Px(radius), paint);
    }

    private void DrawModalItem(SKCanvas c, in TdUiItem it, bool hover)
    {
        var r = it.Rect;
        if (it.Action == TdUiAction.ModalTab)
        {
            if (it.Selected) RoundBox(c, r, Palette.Td.BorderStrong, Palette.Td.BorderStrong, 5);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, new DipRect(r.X + 8, r.Y + 11, r.W - 16, 16), it.Selected ? Palette.Td.Text : Palette.Td.Muted, TextAlign.Center);
            return;
        }
        if (it.Action == TdUiAction.Close)
        {
            DrawIcon(c, TdIcon.Cross, r.X + 10, r.Y + 10, 16, Palette.Td.Muted);
            return;
        }
        if (it.Kind == TdUiKind.Label) { DrawModalLabel(c, it); return; }
        if (it.Kind == TdUiKind.Bar) { DrawRankCard(c, it); return; }
        if (it.Action == TdUiAction.ResumeAbility)
        {
            RoundBox(c, r, Palette.Td.BorderStrong, hover && it.Enabled ? Palette.Td.BorderStrong : Palette.Td.Surface, 6);
            var fg = it.Enabled ? Palette.Td.Text : Palette.Td.Muted;
            DrawIcon(c, it.Text switch { "Escalar" => TdIcon.Scale, "Firewall" => TdIcon.Firewall, _ => TdIcon.Snapshot }, r.X + r.W / 2 - 7, r.Y + 8, 14, fg);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, new DipRect(r.X + 2, r.Y + 26, r.W - 4, 14), fg, TextAlign.Center);
            _p.Text(c, it.Right ?? "", PanelFont.JetBrainsMono, 9, new DipRect(r.X + 2, r.Y + 40, r.W - 4, 12), it.Enabled ? Palette.Td.Amber : Palette.Td.Muted, TextAlign.Center);
            return;
        }
        if (it.Action == TdUiAction.GameOption)
        {
            if (it.Arg == 0) RoundBox(c, new DipRect(r.X, r.Y, r.W, 240), Palette.Td.BorderStrong, Palette.Td.Surface, 8);
            if (hover) _p.Fill(c, new DipRect(r.X + 1, r.Y + 1, r.W - 2, r.H - 2), Palette.Td.BorderStrong.WithAlpha((byte)90));
            int colon = it.Text.IndexOf(':');
            string name = colon > 0 ? it.Text[..colon] : it.Text, val = colon > 0 ? it.Text[(colon + 1)..].Trim() : "";
            _p.Text(c, name, PanelFont.JetBrainsMono, 12, new DipRect(r.X + 12, r.Y + 12, 150, 16), Palette.Td.Text);
            if (it.Arg is 1 or 2 or 4 or 5)
            {
                bool on = val.StartsWith("lig", StringComparison.Ordinal);
                RoundBox(c, new DipRect(r.X + r.W - 52, r.Y + 11, 38, 18), on ? Palette.Td.Amber : Palette.Td.BorderStrong, on ? Palette.Td.Amber : Palette.Td.Background, 9);
                _p.Fill(c, new DipRect(r.X + r.W - (on ? 32 : 48), r.Y + 14, 12, 12), on ? Palette.Td.Background : Palette.Td.Muted);
            }
            else
            {
                if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 9, new DipRect(r.X + r.W - 160, r.Y + 13, 90, 14), Palette.Td.Amber, TextAlign.Right);
                _p.Text(c, val, PanelFont.JetBrainsMono, 12, new DipRect(r.X + r.W - 68, r.Y + 12, 56, 16), Palette.Td.Text, TextAlign.Right);
            }
            if (it.Arg < 5) _p.Fill(c, new DipRect(r.X + 1, r.Y + r.H - 1, r.W - 2, 1), Palette.Td.Border);
            return;
        }
        if (it.Action == TdUiAction.ChooseTower)
        {
            var col = it.SArg switch { "raio" or "lightning" or "bolt" => Palette.Td.Bolt, "gelo" or "ice" or "frost" => Palette.Td.Frost, "canhao" or "cannon" => Palette.Td.Cannon, _ => Palette.Td.Dim };
            RoundBox(c, r, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, hover && it.Enabled ? Palette.Td.BorderStrong : Palette.Td.Surface, 8);
            float size = Math.Min(44, r.H - 8);
            RoundBox(c, new DipRect(r.X + 7, r.Y + (r.H - size) / 2, size, size), Palette.Td.BorderStrong, col.WithAlpha(40), 6);
            DrawIcon(c, it.Enabled ? it.SArg switch { "bolt" => TdIcon.Bolt, "frost" => TdIcon.Frost, _ => TdIcon.Target } : TdIcon.Lock, r.X + 7 + (size - 22) / 2, r.Y + (r.H - 22) / 2, 22, col);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 12, new DipRect(r.X + 60, r.Y + 8, 150, 16), it.Enabled ? Palette.Td.Text : Palette.Td.Muted);
            if (it.Sub is not null) DrawWrapped(c, it.Sub, new DipRect(r.X + 210, r.Y + 9, Math.Max(20, r.W - 290), r.H - 12), Palette.Td.Muted);
            if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 12, new DipRect(r.X + r.W - 78, r.Y + 10, 66, 16), it.Enabled ? Palette.Td.Amber : Palette.Td.Dim, TextAlign.Right);
            return;
        }
        if (it.Kind == TdUiKind.Button)
        {
            var tint = it.SArg == "primary" || it.Action == TdUiAction.Preview ? Palette.Td.Amber : it.Tone == TdTone.Bad ? Palette.Td.Red : it.Tone == TdTone.Good ? Palette.Td.Green : default;
            var border = it.Selected ? Palette.Td.Amber : tint == default ? Palette.Td.BorderStrong : tint.WithAlpha(115);
            var back = it.SArg == "primary" ? Palette.Td.Amber : tint != default ? tint.WithAlpha((byte)(hover ? 45 : 28)) : hover && it.Enabled ? Palette.Td.BorderStrong : Palette.Td.Surface;
            RoundBox(c, r, border, back, 6);
            var fg = !it.Enabled ? Palette.Td.Dim : it.SArg == "primary" ? Palette.Td.Background : tint != default ? tint : Palette.Td.Text;
            float tx = r.X + 10;
            if (it.Icon != TdIcon.None) { DrawIcon(c, it.Icon, tx, r.Y + (r.H - 14) / 2, 14, fg); tx += 22; }
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, r.H >= 34 ? 12 : 10, new DipRect(tx, r.Y + Math.Max(2, (r.H - 13) / 2), r.W - (tx - r.X) - 10, 13), fg);
            if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 10, new DipRect(r.X + 10, r.Y + Math.Max(2, (r.H - 13) / 2), r.W - 20, 13), Palette.Td.Amber, TextAlign.Right);
            return;
        }
        DrawItem(c, it, hover, 720, false);
    }

    private void DrawModalLabel(SKCanvas c, in TdUiItem it)
    {
        var r = it.Rect;
        switch (it.SArg)
        {
            case "sec": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, r, Palette.Td.Muted); return;
            case "hdr": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 12, r, Palette.Td.Muted); return;
            case "hdrR": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, r, Palette.Td.Muted, TextAlign.Right); return;
            case "empty": RoundBox(c, r, Palette.Td.BorderStrong, Palette.Td.Surface, 10); return;
            case "badge":
                RoundBox(c, r, Palette.Td.Amber.WithAlpha(30), Palette.Td.Amber.WithAlpha(30), 12);
                _p.Text(c, it.Text.ToUpperInvariant(), PanelFont.JetBrainsMono, 10, new DipRect(r.X, r.Y + 5, r.W, 14), Palette.Td.Amber, TextAlign.Center);
                return;
            case "c18":
                RoundBox(c, new DipRect(r.X + r.W / 2 - 32, r.Y - 84, 64, 64), Palette.Td.BorderStrong, Palette.Td.Border, 12);
                if (it.Icon != TdIcon.None) DrawIcon(c, it.Icon, r.X + r.W / 2 - 14, r.Y - 66, 28, it.Icon == TdIcon.Lock ? Palette.Td.Amber : Palette.Td.Muted);
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 16, r, Palette.Td.Text, TextAlign.Center);
                return;
            case "c13": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, r, Palette.Td.Muted, TextAlign.Center); return;
        }
        if (it.Icon != TdIcon.None)
        {
            var accent = it.Icon == TdIcon.Heart ? Palette.Td.Red : it.Icon is TdIcon.Coin or TdIcon.Crown or TdIcon.Medal ? Palette.Td.Amber : Palette.Td.Muted;
            float ty = r.Y + (r.H - 26) / 2;
            RoundBox(c, new DipRect(r.X, ty, 26, 26), accent == Palette.Td.Muted ? Palette.Td.BorderStrong : accent.WithAlpha(36), accent == Palette.Td.Muted ? Palette.Td.Surface : accent.WithAlpha(36), 6);
            DrawIcon(c, it.Icon, r.X + 6, ty + 6, 14, accent);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, new DipRect(r.X + 36, r.Y + (r.H - 14) / 2, r.W - 36, 14), Palette.Td.Text);
            return;
        }
        var col = it.Tone switch { TdTone.Warn or TdTone.Accent => Palette.Td.Amber, TdTone.Bad => Palette.Td.Red, TdTone.Good => Palette.Td.Green, TdTone.Dim => Palette.Td.Muted, _ => Palette.Td.Text };
        _p.Text(c, it.Text, PanelFont.JetBrainsMono, 12, r, col);
    }

    private void DrawRankCard(SKCanvas c, in TdUiItem it)
    {
        var r = it.Rect;
        RoundBox(c, r, Palette.Td.BorderStrong, Palette.Td.Surface, 8);
        RoundBox(c, new DipRect(r.X + 12, r.Y + 12, 32, 32), Palette.Td.Amber.WithAlpha(36), Palette.Td.Amber.WithAlpha(36), 6);
        DrawIcon(c, TdIcon.Medal, r.X + 20, r.Y + 20, 16, Palette.Td.Amber);
        int colon = it.Text.IndexOf(':');
        _p.Text(c, "Patente", PanelFont.JetBrainsMono, 9, new DipRect(r.X + 54, r.Y + 11, 120, 12), Palette.Td.Muted);
        _p.Text(c, colon > 0 ? it.Text[(colon + 1)..].Trim() : it.Text, PanelFont.JetBrainsMono, 13, new DipRect(r.X + 54, r.Y + 25, 150, 18), Palette.Td.Amber);
        if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 9, new DipRect(r.X + r.W - 190, r.Y + 17, 178, 12), Palette.Td.Muted, TextAlign.Right);
        _p.Fill(c, new DipRect(r.X + 12, r.Y + 50, r.W - 24, 6), Palette.Td.Background);
        _p.Fill(c, new DipRect(r.X + 12, r.Y + 50, Math.Max(2, (r.W - 24) * it.Fill), 6), Palette.Td.Amber);
    }

    public void DrawPopover(SKCanvas c, TdPopoverView v, int hover)
    {
        c.Clear(SKColors.Transparent);
        float boxH = v.HasArrow ? v.Height - TdPopoverView.ArrowH : v.Height;
        if (v.Width == 416 && v.Height == 136)
        {
            RoundBox(c, new DipRect(0, 0, v.Width, v.Height), Palette.Td.BorderStrong, Palette.Td.Background, 8);
            _p.Text(c, v.Title, PanelFont.JetBrainsMono, 10, new DipRect(10, 12, 390, 16), Palette.Td.Muted);
            foreach (var it in v.Items)
            {
                var r = it.Rect;
                var col = it.SArg switch { "bolt" => Palette.Td.Bolt, "frost" => Palette.Td.Frost, "cannon" => Palette.Td.Cannon, _ => Palette.Td.Dim };
                if (!it.Enabled) col = col.WithAlpha(115);
                RoundBox(c, r, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, Palette.Td.Surface, 6);
                RoundBox(c, new DipRect(r.X + 22, r.Y + 6, 32, 32), Palette.Td.BorderStrong, col.WithAlpha(35), 5);
                DrawIcon(c, it.SArg switch { "bolt" => TdIcon.Bolt, "frost" => TdIcon.Frost, _ => TdIcon.Target }, r.X + 29, r.Y + 13, 18, col);
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 8, new DipRect(r.X + 2, r.Y + 46, r.W - 4, 12), it.Enabled ? Palette.Td.Text : Palette.Td.Dim, TextAlign.Center);
                _p.Text(c, it.Right ?? "", PanelFont.JetBrainsMono, 10, new DipRect(r.X + 2, r.Y + 64, r.W - 4, 12), it.Enabled ? Palette.Td.Amber : Palette.Td.Dim, TextAlign.Center);
            }
            return;
        }
        bool modal = v.Width == 720 && v.Height == 480 && !v.HasArrow;
        _p.Box(c, new DipRect(0, 0, v.Width, boxH), modal ? Palette.Td.Border : v.HasArrow ? Palette.Ambar : Palette.BordaForte, modal ? Palette.Td.Background : Palette.BgPainel);
        if (modal)
        {
            _p.Fill(c, new DipRect(1, 1, v.Width - 2, 43), Palette.Td.Surface);
            _p.Fill(c, new DipRect(1, 44, v.Width - 2, 1), Palette.Td.Border);
            _p.Fill(c, new DipRect(1, 88, v.Width - 2, 1), Palette.Td.Border);
            RoundBox(c, new DipRect(16, 10, 86, 24), Palette.Td.Surface, Palette.Td.PauseBadge, 4);
            _p.Text(c, "Ⅱ PAUSADO", PanelFont.JetBrainsMono, 10, new DipRect(21, 15, 80, 14), Palette.Td.Amber);
            _p.Text(c, v.Title, PanelFont.JetBrainsMono, 11, new DipRect(112, 15, 550, 16), Palette.Td.Muted);
            for (int i = 0; i < v.Items.Length; i++)
            {
                DrawModalItem(c, v.Items[i], i == hover);
                if (v.Items[i].Highlight) DrawHighlight(c, v.Items[i].Rect);
            }
            return;
        }
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
