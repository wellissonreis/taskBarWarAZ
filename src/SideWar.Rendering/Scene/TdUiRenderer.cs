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
        RoundBox(c, new DipRect(0, hud.TopPad, hud.Width, hud.Height - hud.TopPad), Palette.Td.Border, Palette.Td.Surface, 5);
        for (int i = 0; i < hud.Items.Length; i++)
        {
            DrawHudItem(c, hud.Items[i], i == hover);
            if (hud.Items[i].Highlight) DrawHighlight(c, hud.Items[i].Rect);
        }
        if (hud.Floats is { Count: > 0 } floats)
            foreach (var it in hud.Items)
                if (it.Kind == TdUiKind.Money) { DrawMoneyFloats(c, it.Rect, floats); break; }
    }

    /// <summary>TD.33d: "+N" (11 DIP, âmbar translúcido) sobe 12 DIP acima do contador e some; só na faixa transparente do topo da janela do chip (nunca sobre a faixa de torres).</summary>
    private void DrawMoneyFloats(SKCanvas c, DipRect r, IReadOnlyList<TdMoneyFloat> floats)
    {
        foreach (var f in floats)
            _p.Text(c, f.Text, PanelFont.JetBrainsMono, 11f, new DipRect(r.X + 2, r.Y - TdHudView.FloatPad + (TdMoneyModel.FloatRiseDip - f.Rise), r.W - 6, 14), Palette.Td.Amber.WithAlpha(f.Alpha), TextAlign.Right);
    }

    /// <summary>
    /// TD.33d: contador de créditos. Fonte grande (<see cref="TdHudView.MoneyFontDip"/>), âmbar da Palette, alinhado à direita em fonte monoespaçada
    /// (largura fixa por dígito: o contador não dança). <c>Fill</c> = brilho de marco de patrimônio (0..1); <c>Sub</c> = renda "+X/s".
    /// </summary>
    private void DrawMoney(SKCanvas c, in TdUiItem it)
    {
        var r = it.Rect;
        bool glow = it.Fill > 0f;
        var border = glow ? Palette.Td.Amber.WithAlpha((byte)(90 + 165 * it.Fill)) : Palette.Td.BorderStrong;
        RoundBox(c, r, border, Palette.Td.Surface, 4);
        if (glow) { var rect = new DipRect(r.X + 1, r.Y + 1, r.W - 2, r.H - 2); RoundBox(c, rect, Palette.Td.Amber.WithAlpha((byte)(120 * it.Fill)), Palette.Td.Amber.WithAlpha((byte)(46 * it.Fill)), 4); }
        bool income = it.Sub is not null;
        float ty = income ? r.Y + 1f : r.Y + (r.H - 20f) / 2f;
        _p.Text(c, it.Text, PanelFont.JetBrainsMono, TdHudView.MoneyFontDip, new DipRect(r.X + 2, ty, r.W - 6, 20f), Palette.Td.Amber, TextAlign.Right);
        if (income) _p.Text(c, it.Sub!, PanelFont.JetBrainsMono, 8f, new DipRect(r.X + 2, r.Y + 18f, r.W - 6, 10f), Palette.Td.Muted, TextAlign.Right);
    }

    /// <summary>Brilho âmbar curto em volta do botão Torres quando os créditos cruzam o custo (RF-33.21).</summary>
    private void DrawBuyGlow(SKCanvas c, DipRect r, float intensity)
    {
        RoundBox(c, new DipRect(r.X - 1, r.Y - 1, r.W + 2, r.H + 2), Palette.Td.Amber.WithAlpha((byte)(60 + 195 * intensity)), Palette.Td.Amber.WithAlpha((byte)(60 * intensity)), 6);
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
        if (it.Kind == TdUiKind.Separator) { _p.Fill(c, r, Palette.Td.BorderStrong); return; }
        if (it.Kind == TdUiKind.Money) { DrawMoney(c, it); return; }
        if (it.Action == TdUiAction.SetSpeed)
        {
            // Botão de velocidade do chip: mostra 1x/2x/3x e alterna; âmbar quando acelerado.
            RoundBox(c, r, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, hover && it.Enabled ? Palette.Td.BorderStrong : Palette.Td.Button, 6);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 9.5f, new DipRect(r.X, r.Y + (r.H - 12) / 2f, r.W, 12), it.Selected ? Palette.Td.Amber : Palette.Td.Text, TextAlign.Center);
            return;
        }
        if (it.Action == TdUiAction.PauseToggle)
        {
            // Pausar/Retomar: neutro quando rodando; âmbar (borda + fundo translúcido) quando pausado.
            var pbord = it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong;
            var pfill = it.Selected ? Palette.Td.Amber.WithAlpha(36) : hover && it.Enabled ? Palette.Td.BorderStrong : Palette.Td.Button;
            RoundBox(c, r, pbord, pfill, 6);
            DrawIcon(c, it.Icon, r.X + (r.W - 12) / 2f, r.Y + (r.H - 12) / 2f, 12f, it.Selected ? Palette.Td.Amber : Palette.Td.Text);
            return;
        }
        if (it.Action == TdUiAction.RuneWindow)
        {
            // TD.30c: botão Runas (estrela). Discreto: sem animação; ponto âmbar pequeno só quando há fragmento e nó a despertar.
            RoundBox(c, r, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, hover && it.Enabled ? Palette.Td.BorderStrong : Palette.Td.Button, 6);
            DrawIcon(c, TdIcon.Star, r.X + (r.W - 14) / 2f, r.Y + (r.H - 14) / 2f, 14f, it.Selected ? Palette.Td.Amber : it.Enabled ? Palette.Td.Text : Palette.Td.Dim);
            if (it.Fill > 0f)
            {
                using var dot = new SKPaint { IsAntialias = true, Color = Palette.Td.Amber };
                c.DrawCircle(_p.Px(r.X + r.W - 6f), _p.Px(r.Y + 6f), Math.Max(1.5f, _p.Px(2.5f)), dot);
            }
            return;
        }
        if (it.Action == TdUiAction.QuickTowers)
        {
            if (it.Fill > 0f) DrawBuyGlow(c, r, it.Fill);
            RoundBox(c, r, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, hover && it.Enabled ? Palette.Td.BorderStrong : Palette.Td.Button, 6);
            DrawIcon(c, TdIcon.Castle, r.X + (r.W - 16) / 2f, r.Y + (r.H - 16) / 2f, 16f, it.Enabled ? Palette.Td.Text : Palette.Td.Dim);
            DrawArrowNE(c, r.X + r.W - 9f, r.Y + 2f, 7f, Palette.Td.Amber);
            return;
        }
        var color = ToneColor(it.Tone);
        float fs = it.Kind == TdUiKind.Button && r.W <= 80f ? 8.5f : 9f;
        bool isButton = it.Kind == TdUiKind.Button;
        bool enabled = it.Enabled && it.Action != TdUiAction.None;
        if (isButton)
        {
            var border = Palette.Td.BorderStrong;
            var fill = hover && enabled ? Palette.Td.BorderStrong : Palette.Td.Surface;
            RoundBox(c, r, border, fill, 4);
            if (it.Fill > 0f) _p.Fill(c, new DipRect(r.X + 1, r.Y + r.H - 3, (r.W - 2) * it.Fill, 2), Palette.Azul);
            if (!enabled && it.Tone != TdTone.Dim) color = Palette.TxtRotulo;
        }
        bool chip = it.Action == TdUiAction.HudRestore;
        if (chip) fs = 8f;
        float iconSize = chip ? 12f : Math.Min(15f, r.H - 8f);
        float x = r.X + (chip ? 4f : 5f);
        bool iconOnly = it.Text.Length == 0;
        if (it.Icon != TdIcon.None)
        {
            float ix = iconOnly ? r.X + (r.W - iconSize) / 2f : x;
            DrawIcon(c, it.Icon, ix, r.Y + (r.H - iconSize) / 2f, iconSize, color);
            x += iconSize + (chip ? 3f : 4f);
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

    /// <summary>Seta âmbar de 7 DIP apontando para cima/direita (marca do botão Torres: abre o painel).</summary>
    private void DrawArrowNE(SKCanvas c, float xDip, float yDip, float sizeDip, SKColor color)
    {
        float x = _p.Px(xDip), y = _p.Px(yDip), z = _p.Px(sizeDip);
        using var b = new SKPathBuilder();
        b.MoveTo(x + z * 0.15f, y + z * 0.15f); b.LineTo(x + z * 0.85f, y + z * 0.15f); b.LineTo(x + z * 0.85f, y + z * 0.85f);
        b.MoveTo(x + z * 0.85f, y + z * 0.15f); b.LineTo(x + z * 0.15f, y + z * 0.85f);
        using var path = b.Detach();
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = Math.Max(1f, _p.Px(1.4f)), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        c.DrawPath(path, paint);
    }

    private void DashedBox(SKCanvas c, DipRect r, SKColor border, float radius, SKColor? fill = null)
    {
        var rect = new SKRect(_p.Px(r.X) + 0.5f, _p.Px(r.Y) + 0.5f, _p.Px(r.X + r.W) - 0.5f, _p.Px(r.Y + r.H) - 0.5f);
        if (fill is { } f)
        {
            using var fp = new SKPaint { IsAntialias = true, Color = f };
            c.DrawRoundRect(rect, _p.Px(radius), _p.Px(radius), fp);
        }
        using var dash = SKPathEffect.CreateDash([_p.Px(3f), _p.Px(2.5f)], 0f);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1, _p.Px(1)), Color = border, PathEffect = dash };
        c.DrawRoundRect(rect, _p.Px(radius), _p.Px(radius), paint);
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
        if (it.Action == TdUiAction.SelectRune) { DrawRuneNode(c, it, hover); return; }
        if (it.Action == TdUiAction.RuneBranch) { DrawRuneChip(c, it, hover, true); return; }
        if (it.Action == TdUiAction.SetSpeed)
        {
            // Seletor segmentado completo (aba Jogo): selecionado âmbar com texto escuro.
            RoundBox(c, r, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, it.Selected ? Palette.Td.Amber : hover ? Palette.Td.BorderStrong : Palette.Td.Background, 5);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, new DipRect(r.X, r.Y + (r.H - 14) / 2f, r.W, 14), it.Selected ? Palette.Td.Background : Palette.Td.Muted, TextAlign.Center);
            return;
        }
        if (it.Action == TdUiAction.AwakenRune)
        {
            // Despertar runa: âmbar cheio; desabilitado ("Sem fragmentos"/"Teia completa") tracejado e apagado.
            if (it.Enabled) RoundBox(c, r, Palette.Td.Amber, hover ? Palette.AmbarHover : Palette.Td.Amber, 6);
            else DashedBox(c, r, Palette.Td.BorderStrong, 6);
            var fg = it.Enabled ? Palette.Td.Background : Palette.Td.Dim;
            if (it.Text.Length > 0) DrawIcon(c, TdIcon.Bolt, r.X + 12, r.Y + (r.H - 14) / 2f, 14f, fg);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, new DipRect(r.X + 32, r.Y + (r.H - 14) / 2f, r.W - 38, 14), fg);
            return;
        }
        if (it.SArg == "tutskip")
        {
            RoundBox(c, r, Palette.Td.BorderStrong, hover ? Palette.Td.BorderStrong : Palette.Td.Surface, 5);
            DrawIcon(c, TdIcon.Cross, r.X + 8, r.Y + (r.H - 10) / 2f, 10f, Palette.Td.Muted);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, new DipRect(r.X + 24, r.Y + (r.H - 12) / 2f, r.W - 28, 12), Palette.Td.Muted);
            return;
        }
        if (it.Action == TdUiAction.GameOption)
        {
            if (it.Arg == 0) RoundBox(c, new DipRect(r.X, r.Y, r.W, 9 * 31), Palette.Td.BorderStrong, Palette.Td.Surface, 8);
            if (hover) _p.Fill(c, new DipRect(r.X + 1, r.Y + 1, r.W - 2, r.H - 2), Palette.Td.BorderStrong.WithAlpha((byte)90));
            int colon = it.Text.IndexOf(':');
            string name = colon > 0 ? it.Text[..colon] : it.Text, val = colon > 0 ? it.Text[(colon + 1)..].Trim() : "";
            float dy = (r.H - 40) / 2f; // linhas de 31
            _p.Text(c, name, PanelFont.JetBrainsMono, 12, new DipRect(r.X + 12, r.Y + 12 + dy, 170, 16), Palette.Td.Text);
            if (it.Arg is 1 or 2 or 4 or 5 or 6 or 7)
            {
                bool on = val.StartsWith("lig", StringComparison.Ordinal);
                if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 9, new DipRect(r.X + r.W - 160, r.Y + 13 + dy, 100, 14), Palette.Td.Amber, TextAlign.Right);
                RoundBox(c, new DipRect(r.X + r.W - 52, r.Y + 11 + dy, 38, 18), on ? Palette.Td.Amber : Palette.Td.BorderStrong, on ? Palette.Td.Amber : Palette.Td.Background, 9);
                _p.Fill(c, new DipRect(r.X + r.W - (on ? 32 : 48), r.Y + 14 + dy, 12, 12), on ? Palette.Td.Background : Palette.Td.Muted);
            }
            else
            {
                if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 9, new DipRect(r.X + r.W - 160, r.Y + 13 + dy, 90, 14), Palette.Td.Amber, TextAlign.Right);
                _p.Text(c, val, PanelFont.JetBrainsMono, 12, new DipRect(r.X + r.W - 68, r.Y + 12 + dy, 56, 16), Palette.Td.Text, TextAlign.Right);
            }
            if (it.Arg < 8) _p.Fill(c, new DipRect(r.X + 1, r.Y + r.H - 1, r.W - 2, 1), Palette.Td.Border);
            return;
        }
        if (it.Action == TdUiAction.ChooseTower)
        {
            var col = it.SArg switch { "raio" or "lightning" or "bolt" => Palette.Td.Bolt, "gelo" or "ice" or "frost" => Palette.Td.Frost, "canhao" or "cannon" => Palette.Td.Cannon, _ => Palette.Td.Dim };
            RoundBox(c, r, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, hover && it.Enabled ? Palette.Td.BorderStrong : Palette.Td.Surface, 8);
            float size = Math.Min(44, r.H - 8);
            RoundBox(c, new DipRect(r.X + 7, r.Y + (r.H - size) / 2, size, size), Palette.Td.BorderStrong, col.WithAlpha(40), 6);
            // TD.28c: foto da torre (sprite do design) no tile, no lugar do ícone vetorial; apagada se bloqueada/sem crédito
            float photo = size - 8f;
            DrawTowerIcon(c, it.SArg ?? "", r.X + 7 + (size - photo) / 2, r.Y + (r.H - photo) / 2, photo, it.Enabled ? (byte)255 : (byte)110);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 14, new DipRect(r.X + 60, r.Y + 8, 150, 18), it.Enabled ? Palette.Td.Text : Palette.Td.Muted);
            if (it.Sub is not null) DrawWrapped(c, it.Sub, new DipRect(r.X + 210, r.Y + 9, Math.Max(20, r.W - 290), r.H - 12), Palette.Td.Muted);
            if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 14, new DipRect(r.X + r.W - 78, r.Y + 10, 66, 18), it.Enabled ? Palette.Td.Amber : Palette.Td.Dim, TextAlign.Right);
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
            if (it.Action == TdUiAction.OpenTower && it.SArg is not null && r.H >= 30f)
            {
                // TD.28c: aba Torres: foto da torre em campo à esquerda do nome
                float photo = Math.Min(r.H - 8f, 34f);
                RoundBox(c, new DipRect(r.X + 6, r.Y + (r.H - photo - 2) / 2, photo + 2, photo + 2), Palette.Td.BorderStrong, Palette.Td.Background, 5);
                DrawTowerIcon(c, it.SArg, r.X + 7, r.Y + (r.H - photo) / 2, photo, 255);
                tx = r.X + photo + 16;
            }
            else if (it.Icon != TdIcon.None) { DrawIcon(c, it.Icon, tx, r.Y + (r.H - 14) / 2, 14, fg); tx += 22; }
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
            case "webbg": return; // desenhada antes dos itens (DrawRuneWeb)
            case "optname": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 12, r, Palette.Td.Text); return;
            case "c11l": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 9, r, Palette.Td.Muted); return;
            case "secR": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, r, Palette.Td.Amber, TextAlign.Right); return;
            case "rfrag":
                _p.Text(c, "FRAGMENTOS", PanelFont.JetBrainsMono, 11, new DipRect(r.X, r.Y, 140, 14), Palette.Td.Muted);
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 18, new DipRect(r.X + 108, r.Y - 3, 90, 22), Palette.Td.Amber);
                return;
            case "rtot":
                _p.Text(c, "TEIA DE RUNAS", PanelFont.JetBrainsMono, 11, new DipRect(r.X, r.Y, r.W, 14), Palette.Td.Muted, TextAlign.Right);
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 13, new DipRect(r.X, r.Y + 17, r.W, 17), Palette.Td.Text, TextAlign.Right);
                return;
            case "rhint": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, r, Palette.Td.Dim); return;
            case "tut":
                if (it.Icon != TdIcon.None) DrawIcon(c, it.Icon, r.X, r.Y + 2, 14f, Palette.Td.Amber);
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, new DipRect(r.X + 22, r.Y + 3, r.W - 22, 14), Palette.Td.Amber);
                return;
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
        _p.Text(c, it.Text, PanelFont.JetBrainsMono, it.FontSize > 0 ? it.FontSize : 12, r, col);
    }

    // ---- Teia de runas (Main.dc.html): glifos por tipo na ordem do conteúdo (Alcance, Raio, Créditos, Gelo, Núcleo, Canhão, Recarga, Escudo).
    private static readonly string[] RuneGlyphs =
    [
        "M12 19V5M7 10l5-5 5 5", "M13 3L6 13h5l-1 8 7-10h-5z", "M12 5a7 7 0 1 0 .01 0M12 9v6", "M12 4v16M5 8l14 8M19 8L5 16",
        "M12 4l6 8-6 8-6-8z", "M12 3v18M3 12h18M12 8a4 4 0 1 0 .01 0", "M18 12a6 6 0 1 1-1.8-4.3M18 5v4h-4", "M15 4a8 8 0 1 0 5 13A7 7 0 0 1 15 4z",
    ];

    private static readonly SKPath[] RunePaths = [.. RuneGlyphs.Select(g => SKPath.ParseSvgPathData(g)!)];

    private static int UnpackType(int arg) => Math.Clamp(TdUiController.UnpackRune(arg).Type, 0, RunePaths.Length - 1);

    private void DrawRuneGlyph(SKCanvas c, int type, float xDip, float yDip, float sizeDip, SKColor color, float strokeUnits)
    {
        c.Save();
        c.Translate(_p.Px(xDip), _p.Px(yDip));
        float k = _p.Px(sizeDip) / 24f;
        c.Scale(k, k);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = strokeUnits, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        c.DrawPath(RunePaths[type], paint);
        c.Restore();
    }

    private static readonly SKPath StarPath = SKPath.ParseSvgPathData("M12 2.5l2.7 6 6.5.7-4.9 4.4 1.4 6.4L12 16.8 6.3 20l1.4-6.4-4.9-4.4 6.5-.7z")!;

    private void DrawStar(SKCanvas c, float cxDip, float cyDip, float sizeDip, float strokeUnits)
    {
        c.Save();
        c.Translate(_p.Px(cxDip) - _p.Px(sizeDip) / 2f, _p.Px(cyDip) - _p.Px(sizeDip) / 2f);
        float k = _p.Px(sizeDip) / 24f;
        c.Scale(k, k);
        using var sf = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Palette.Td.Amber.WithAlpha(46) };
        using var ss = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Palette.Td.Amber, StrokeWidth = strokeUnits, StrokeJoin = SKStrokeJoin.Round };
        c.DrawPath(StarPath, sf);
        c.DrawPath(StarPath, ss);
        c.Restore();
    }

    /// <summary>
    /// Fundo e fios da teia (TD.30c): painel escuro com brilho central (modal), arcos curvos entre setores do mesmo anel (controle puxado ao
    /// centro, como o design), raios do anel interno ao externo, estrela no centro. Aceso (âmbar, com brilho) quando as duas pontas estão
    /// despertadas; apagado caso contrário. Os nós são desenhados depois, por cima.
    /// </summary>
    private void DrawRuneWeb(SKCanvas c, TdUiItem[] items, bool big)
    {
        TdUiItem? bg = null;
        var nodes = new Dictionary<(int Ring, int Sector), TdUiItem>();
        foreach (var it in items)
        {
            if (it.SArg == "webbg" && it.Kind == TdUiKind.Label) bg = it;
            else if (it.Action == TdUiAction.SelectRune) { var (_, ring, sector) = TdUiController.UnpackRune(it.Arg); nodes[(ring, sector)] = it; }
        }
        if (bg is not { } b) return;
        var area = b.Rect;
        RoundBox(c, area, Palette.Td.Border, Palette.Td.Background, big ? 12 : 8);
        float cx = area.X + area.W / 2f, cy = area.Y + area.H / 2f;
        if (big)
        {
            using var glow = new SKPaint { IsAntialias = true, Shader = SKShader.CreateRadialGradient(new SKPoint(_p.Px(cx), _p.Px(cy)), _p.Px(150f), [Palette.Td.Amber.WithAlpha(26), Palette.Td.Amber.WithAlpha(0)], [0f, 1f], SKShaderTileMode.Clamp) };
            c.DrawCircle(_p.Px(cx), _p.Px(cy), _p.Px(150f), glow);
        }
        if (nodes.Count > 0)
        {
            float pull = big ? 0.9f : 0.88f;
            using var dim = new SKPathBuilder();
            using var lit = new SKPathBuilder();
            (float X, float Y) Mid(TdUiItem n) => (n.Rect.X + n.Rect.W / 2f, n.Rect.Y + n.Rect.H / 2f);
            void Line(SKPathBuilder pb, (float X, float Y) a, (float X, float Y) z) { pb.MoveTo(_p.Px(a.X), _p.Px(a.Y)); pb.LineTo(_p.Px(z.X), _p.Px(z.Y)); }
            foreach (var ((ring, sector), n) in nodes)
            {
                bool on = n.Fill >= TdUiController.RuneOn;
                var a = Mid(n);
                if (nodes.TryGetValue((ring, (sector + 1) % 8), out var side))
                {
                    var z = Mid(side);
                    float qx = cx + ((a.X + z.X) / 2f - cx) * pull, qy = cy + ((a.Y + z.Y) / 2f - cy) * pull;
                    var pb = on && side.Fill >= TdUiController.RuneOn ? lit : dim;
                    pb.MoveTo(_p.Px(a.X), _p.Px(a.Y));
                    pb.QuadTo(_p.Px(qx), _p.Px(qy), _p.Px(z.X), _p.Px(z.Y));
                }
                if (ring == 0) Line(on ? lit : dim, (cx, cy), a);
                else if (nodes.TryGetValue((ring - 1, sector), out var inner)) Line(on && inner.Fill >= TdUiController.RuneOn ? lit : dim, Mid(inner), a);
                if (ring == 3) Line(dim, a, (cx + (a.X - cx) * 1.05f, cy + (a.Y - cy) * 1.05f));
            }
            using var dimPath = dim.Detach();
            using var litPath = lit.Detach();
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
            paint.Color = Palette.Td.BorderStrong; paint.StrokeWidth = Math.Max(1f, _p.Px(1f));
            c.DrawPath(dimPath, paint);
            paint.Color = Palette.Td.Amber.WithAlpha(46); paint.StrokeWidth = Math.Max(2f, _p.Px(big ? 6f : 5f));
            c.DrawPath(litPath, paint);
            paint.Color = Palette.Td.Amber.WithAlpha(217); paint.StrokeWidth = Math.Max(1f, _p.Px(big ? 1.6f : 1.4f));
            c.DrawPath(litPath, paint);
        }
        float core = big ? 52f : 28f;
        using (var halo = new SKPaint { IsAntialias = true, Color = Palette.Td.Amber.WithAlpha((byte)(big ? 20 : 30)) })
            c.DrawCircle(_p.Px(cx), _p.Px(cy), _p.Px(core / 2f + (big ? 6f : 3f)), halo);
        RoundBox(c, new DipRect(cx - core / 2f, cy - core / 2f, core, core), Palette.Td.Amber, Palette.Td.Button, core / 2f);
        DrawStar(c, cx, cy, core / 2f, big ? 1.6f : 2f);
    }

    /// <summary>Cores do nó bloqueado: o glifo do ramo esmaecido continua legível (contraste ≥ 3:1 contra o fundo do nó; teste em TdRuneTypesVisibleRenderTests).</summary>
    public static readonly SKColor LockedGlyphColor = Palette.Td.Dim, LockedNodeFill = Palette.Td.Border;

    private void DrawRuneNode(SKCanvas c, in TdUiItem it, bool hovered)
    {
        var r = it.Rect;
        bool on = it.Fill >= TdUiController.RuneOn, av = !on && it.Fill > 0f;
        float cx = r.X + r.W / 2f, cy = r.Y + r.H / 2f;
        float vis = it.FontSize > 0f ? it.FontSize : r.W;
        if (hovered) vis *= 1.2f; // hover: o nó cresce 20%
        int type = UnpackType(it.Arg);
        float gs = vis * (vis >= 24f ? 0.52f : 0.62f), gx = cx - gs / 2f, gy = cy - gs / 2f;
        // legenda: nós do ramo em destaque (brilho leve), em qualquer estado
        if (it.Selected) DrawPebble(c, cx, cy, vis + 9f, Palette.Td.Amber.WithAlpha(70), default, true);
        if (on)
        {
            bool last = it.Right == "last";
            DrawPebble(c, cx, cy, vis + (last ? 12f : 8f), Palette.Td.Amber.WithAlpha((byte)(last ? 52 : 26)), default, true);
            if (last) DrawPebble(c, cx, cy, vis + 5f, Palette.Td.Amber.WithAlpha(120), default, false);
            DrawPebble(c, cx, cy, vis + 3f, Palette.Td.Amber.WithAlpha(40), default, true);
            DrawPebble(c, cx, cy, vis, Palette.Td.Amber.WithAlpha(190), Palette.Td.Button, false);
            DrawRuneGlyph(c, type, gx, gy, gs, Palette.Td.Amber, 2.2f);
        }
        else if (av)
        {
            if (hovered) DrawPebble(c, cx, cy, vis + 6f, Palette.Td.Amber.WithAlpha(34), default, true);
            DrawPebble(c, cx, cy, vis, Palette.Td.Amber, hovered ? Palette.Td.Button : Palette.Td.Background, false, dashed: true);
            DrawRuneGlyph(c, type, gx, gy, gs, Palette.Td.Amber, 2.2f);
        }
        else
        {
            if (hovered) DrawPebble(c, cx, cy, vis + 8f, Palette.Td.Amber.WithAlpha(34), default, true);
            using var f = new SKPaint { IsAntialias = true, Color = LockedNodeFill };
            using var l = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = hovered || it.Selected ? Palette.Td.Dim : Palette.Td.BorderStrong, StrokeWidth = Math.Max(1, _p.Px(1)) };
            c.DrawCircle(_p.Px(cx), _p.Px(cy), _p.Px(vis / 2f), f);
            c.DrawCircle(_p.Px(cx), _p.Px(cy), _p.Px(vis / 2f), l);
            DrawRuneGlyph(c, type, gx, gy, gs, it.Selected ? Palette.Td.Amber : LockedGlyphColor, 2.2f);
        }
    }

    /// <summary>
    /// Chip da legenda de ramos (TD.30e): glifo do ramo, nome e n/total. Modal (<paramref name="big"/>): glifo à esquerda, nome e contagem em duas linhas;
    /// HUD: glifo sobre a contagem (chip estreito). Passar o mouse acende a borda; <c>Selected</c> = ramo em destaque.
    /// </summary>
    private void DrawRuneChip(SKCanvas c, in TdUiItem it, bool hover, bool big)
    {
        var r = it.Rect;
        bool lit = it.Selected || hover || it.Highlight; // Highlight: o tutorial do 1º dano acende a legenda inteira
        RoundBox(c, r, lit ? Palette.Td.Amber : Palette.Td.BorderStrong, lit ? Palette.Td.Button : Palette.Td.Surface, 6);
        int type = Math.Clamp(it.Arg, 0, RunePaths.Length - 1);
        bool any = it.Right is { Length: > 0 } cnt && cnt[0] != '0';
        var glyph = any || lit ? Palette.Td.Amber : Palette.Td.Dim;
        if (big)
        {
            DrawRuneGlyph(c, type, r.X + 7f, r.Y + (r.H - 16f) / 2f, 16f, glyph, 2.2f);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10f, new DipRect(r.X + 27f, r.Y + 3f, r.W - 29f, 13f), Palette.Td.Text);
            _p.Text(c, it.Right ?? "", PanelFont.JetBrainsMono, 10f, new DipRect(r.X + 27f, r.Y + 17f, r.W - 29f, 13f), any ? Palette.Td.Amber : Palette.Td.Muted);
        }
        else
        {
            DrawRuneGlyph(c, type, r.X + (r.W - 12f) / 2f, r.Y + 3f, 12f, glyph, 2.2f);
            _p.Text(c, it.Right ?? "", PanelFont.JetBrainsMono, 9f, new DipRect(r.X, r.Y + 17f, r.W, 12f), any ? Palette.Td.Amber : Palette.Td.Muted, TextAlign.Center);
        }
    }

    // "Pebble" do design (46% 54% 50% 50% / 55% 48% 52% 45%): oval levemente irregular; halo/seleção sem fundo.
    private void DrawPebble(SKCanvas c, float cx, float cy, float sizeDip, SKColor edge, SKColor fill, bool halo, bool dashed = false)
    {
        float w = _p.Px(sizeDip), x = _p.Px(cx) - w / 2f, y = _p.Px(cy) - w / 2f;
        var rect = new SKRect(x, y, x + w, y + w);
        using var rr = new SKRoundRect();
        rr.SetRectRadii(rect, [new SKPoint(w * .46f, w * .55f), new SKPoint(w * .54f, w * .48f), new SKPoint(w * .50f, w * .52f), new SKPoint(w * .50f, w * .45f)]);
        if (fill != default) { using var f = new SKPaint { IsAntialias = true, Color = fill }; c.DrawRoundRect(rr, f); }
        using var paint = new SKPaint { IsAntialias = true, Style = halo ? SKPaintStyle.Fill : SKPaintStyle.Stroke, Color = edge, StrokeWidth = Math.Max(1, _p.Px(1)) };
        if (dashed) paint.PathEffect = SKPathEffect.CreateDash([_p.Px(3f), _p.Px(2.5f)], 0f);
        c.DrawRoundRect(rr, paint);
    }

    private List<string> WrapLines(string text, float sizeDip, float widthDip)
    {
        var lines = new List<string>(3);
        if (!_p.TextEnabled)
        {
            int per = Math.Max(8, (int)(widthDip / (sizeDip * 0.6f)));
            for (int i = 0; i < text.Length; i += per) lines.Add(text.Substring(i, Math.Min(per, text.Length - i)));
            return lines;
        }
        var font = _p.Font(PanelFont.JetBrainsMono, sizeDip);
        float max = _p.Px(widthDip);
        string line = "";
        foreach (var w in text.Split(' '))
        {
            var test = line.Length == 0 ? w : line + " " + w;
            if (line.Length > 0 && font.MeasureText(test) > max) { lines.Add(line); line = w; } else line = test;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    /// <summary>Medidas (DIP) do balão da runa: largura, altura e linhas do efeito. HUD: texto 12 DIP; modal: 13 DIP.</summary>
    public (float W, float H, List<string> Lines, List<string> Action) RuneTipSize(TdRuneTip tip, bool modal)
    {
        float fs = modal ? 13f : 12f, w = modal ? 290f : 272f, pad = modal ? 12f : 9f, line = fs + 4f;
        var lines = WrapLines(tip.Text, fs, w - 2 * pad);
        var action = tip.Action.Length > 0 ? WrapLines(tip.Action, fs, w - 2 * pad) : [];
        float top = modal ? 38f : line + 4f + line + 2f;
        float h = pad + top + 4f + lines.Count * line + (action.Count > 0 ? 4f + action.Count * line : 0f) + pad - 2f;
        return (w, h, lines, action);
    }

    /// <summary>
    /// Balão do nó sob o mouse (TD.30c): nome, etiqueta (DESPERTADA/DISPONÍVEL/BLOQUEADA), efeito e ação; sempre dentro dos limites da janela
    /// (flip vertical/horizontal por <see cref="TdRuneTipLayout"/>). Só desenho: o mouse passa por ele.
    /// </summary>
    private void DrawRuneTip(SKCanvas c, TdRuneTip tip, float boundsW, float boundsH, bool modal)
    {
        float fs = modal ? 13f : 12f, pad = modal ? 12f : 9f, line = fs + 4f;
        var (w, h, lines, action) = RuneTipSize(tip, modal);
        var (x, y, _) = TdRuneTipLayout.Place(tip.NodeX, tip.NodeY, tip.NodeSize * 1.2f, w, h, boundsW, boundsH, modal ? 8f : 4f);
        bool on = tip.State == TdRuneState.Awake, av = tip.State == TdRuneState.Available;
        _p.Fill(c, new DipRect(x + 2, y + 4, w, h), SKColors.Black.WithAlpha(90));
        RoundBox(c, new DipRect(x, y, w, h), on ? Palette.Td.Amber.WithAlpha(153) : Palette.Td.BorderStrong, Palette.Td.Surface, 8);
        float tx = x + pad, ty = y + pad;
        if (modal)
        {
            float cx = x + pad + 17, cy = y + pad + 17;
            if (on) DrawPebble(c, cx, cy, 34, Palette.Td.Amber.WithAlpha(180), Palette.Td.Button, false);
            else if (av) DrawPebble(c, cx, cy, 34, Palette.Td.Amber, Palette.Td.Background, false, dashed: true);
            else DrawPebble(c, cx, cy, 34, Palette.Td.BorderStrong, LockedNodeFill, false);
            DrawRuneGlyph(c, Math.Clamp(tip.TypeIndex, 0, RunePaths.Length - 1), cx - 9, cy - 9, 18f, on || av ? Palette.Td.Amber : LockedGlyphColor, 2f);
            tx += 46f;
        }
        _p.Text(c, tip.Name, PanelFont.JetBrainsMono, fs, new DipRect(tx, ty, w - (tx - x) - pad, line), Palette.Td.Text);
        float tagY = ty + line + (modal ? 3f : 4f);
        float tagW = (_p.TextEnabled ? _p.Font(PanelFont.JetBrainsMono, fs).MeasureText(tip.Tag) / (_p.Dpi / 96f) : tip.Tag.Length * fs * 0.6f) + 12f;
        var tagBox = new DipRect(tx, tagY, tagW, line + 2f);
        if (on) RoundBox(c, tagBox, Palette.Td.Amber, Palette.Td.Amber, 4);
        else DashedBox(c, tagBox, av ? Palette.Td.Amber : Palette.Td.BorderStrong, 4);
        _p.Text(c, tip.Tag, PanelFont.JetBrainsMono, fs, new DipRect(tagBox.X, tagBox.Y + 1f, tagBox.W, line), on ? Palette.Td.Background : av ? Palette.Td.Amber : Palette.Td.Muted, TextAlign.Center);
        float by = y + pad + (modal ? 38f : line + 4f + line + 2f) + 4f;
        for (int i = 0; i < lines.Count; i++)
            _p.Text(c, lines[i], PanelFont.JetBrainsMono, fs, new DipRect(x + pad, by + i * line, w - 2 * pad, line), Palette.Td.Text);
        var col = tip.ActionTone switch { TdTone.Good => Palette.Td.Green, TdTone.Accent => Palette.Td.Amber, TdTone.Bad => Palette.Td.Red, _ => Palette.Td.Muted };
        for (int i = 0; i < action.Count; i++)
            _p.Text(c, action[i], PanelFont.JetBrainsMono, fs, new DipRect(x + pad, by + lines.Count * line + 4f + i * line, w - 2 * pad, line), col);
    }

    /// <summary>Janela de runas ancorada no HUD (HUDRunas.dc.html): cabeçalho, mini-teia, dica, link "Teia completa" e a seta para o botão.</summary>
    private void DrawRuneWindow(SKCanvas c, TdPopoverView v, int hover)
    {
        float boxH = v.Height - TdPopoverView.ArrowH;
        var edge = Palette.Td.Amber.WithAlpha(115);
        // seta (triângulo) para o botão Runas, por baixo da caixa: a base é coberta pela caixa
        float ax = Math.Clamp(v.ArrowX, 14f, v.Width - 14f);
        using (var b = new SKPathBuilder())
        {
            b.MoveTo(_p.Px(ax - 7f), _p.Px(boxH - 1f)); b.LineTo(_p.Px(ax), _p.Px(boxH + TdPopoverView.ArrowH - 0.5f)); b.LineTo(_p.Px(ax + 7f), _p.Px(boxH - 1f));
            using var path = b.Detach();
            using var fill = new SKPaint { IsAntialias = true, Color = Palette.Td.Surface };
            using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = edge, StrokeWidth = Math.Max(1f, _p.Px(1f)), StrokeJoin = SKStrokeJoin.Miter };
            c.DrawPath(path, fill);
            c.DrawPath(path, line);
        }
        RoundBox(c, new DipRect(0, 0, v.Width, boxH), edge, Palette.Td.Surface, 10);
        _p.Fill(c, new DipRect(ax - 6f, boxH - 1f, 12f, 2f), Palette.Td.Surface);
        DrawStar(c, 19, 20, 14, 2f);
        _p.Text(c, v.Title, PanelFont.JetBrainsMono, 13, new DipRect(34, 12, 60, 16), Palette.Td.Text);
        DrawRuneWeb(c, v.Items, false);
        for (int i = 0; i < v.Items.Length; i++)
        {
            var it = v.Items[i];
            var r = it.Rect;
            if (it.Action == TdUiAction.SelectRune) { DrawRuneNode(c, it, i == hover); continue; }
            if (it.Action == TdUiAction.RuneBranch)
            {
                DrawRuneChip(c, it, i == hover, false);
                continue;
            }
            if (it.Action == TdUiAction.Close)
            {
                if (i == hover) RoundBox(c, r, Palette.Td.BorderStrong, Palette.Td.BorderStrong, 5);
                DrawIcon(c, TdIcon.Cross, r.X + 5, r.Y + 5, 14, Palette.Td.Muted);
                continue;
            }
            if (it.Action == TdUiAction.ModalTab)
            {
                var col = i == hover ? Palette.AmbarHover : Palette.Td.Amber;
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, new DipRect(r.X + 4, r.Y + 2, r.W - 18, 14), col, TextAlign.Right);
                DrawArrowNE(c, r.X + r.W - 10f, r.Y + 3f, 8f, col);
                continue;
            }
            switch (it.SArg)
            {
                case "rcount": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 12, new DipRect(r.X + 74, r.Y + 5, 70, 16), Palette.Td.Muted); break;
                case "rpill":
                    RoundBox(c, r, Palette.Td.Amber.WithAlpha(31), Palette.Td.Amber.WithAlpha(31), 11);
                    DrawRuneDiamond(c, r.X + 11, r.Y + r.H / 2f, 5f);
                    _p.Text(c, it.Text, PanelFont.JetBrainsMono, 12, new DipRect(r.X + 20, r.Y + 3, r.W - 26, 16), Palette.Td.Amber, TextAlign.Right);
                    break;
                case "rhintw":
                {
                    var lines = WrapLines(it.Text, 11f, r.W);
                    for (int l = 0; l < lines.Count && l < 2; l++)
                        _p.Text(c, lines[l], PanelFont.JetBrainsMono, 11, new DipRect(r.X, r.Y + 1 + l * 14, r.W, 14), Palette.Td.Dim);
                    break;
                }
            }
        }
        if (v.Tip is { } tip) DrawRuneTip(c, tip, v.Width, boxH, false);
    }

    private void DrawRuneDiamond(SKCanvas c, float cxDip, float cyDip, float rDip)
    {
        using var b = new SKPathBuilder();
        b.MoveTo(_p.Px(cxDip), _p.Px(cyDip - rDip)); b.LineTo(_p.Px(cxDip + rDip * 0.7f), _p.Px(cyDip)); b.LineTo(_p.Px(cxDip), _p.Px(cyDip + rDip)); b.LineTo(_p.Px(cxDip - rDip * 0.7f), _p.Px(cyDip));
        b.Close();
        using var path = b.Detach();
        using var paint = new SKPaint { IsAntialias = true, Color = Palette.Td.Amber };
        c.DrawPath(path, paint);
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
        if (v.Quick)
        {
            // Painel rápido (Partida.dc.html): cartão por torre com tile (sprite real), nome, descrição curta e preço âmbar.
            RoundBox(c, new DipRect(0, 0, v.Width, v.Height), Palette.Td.BorderStrong, Palette.Td.Background, 8);
            for (int i = 0; i < v.Items.Length; i++)
            {
                var it = v.Items[i];
                var r = it.Rect;
                if (it.Action == TdUiAction.TowerCategory)
                {
                    RoundBox(c, r, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, i == hover ? Palette.Td.Button : Palette.Td.Card, 4);
                    _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, r, it.Selected ? Palette.Td.Amber : Palette.Td.Text, TextAlign.Center);
                    continue;
                }
                var border = it.Arg switch { 1 => Palette.Td.Frost, 2 => Palette.Td.Green, 3 => Palette.Td.Amber, _ => Palette.Td.Cannon };
                RoundBox(c, r, border, i == hover && it.Enabled ? Palette.Td.Button : Palette.Td.Card, 8);
                DrawTowerIcon(c, it.SArg ?? "", r.X + 8, r.Y + 4, 28f, it.Enabled ? (byte)255 : (byte)110);
                if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, 14, new DipRect(r.X, r.Y + 38, r.W, 18), it.Enabled ? Palette.Td.Amber : Palette.Td.Dim, TextAlign.Center);
                if (it.Highlight) DrawHighlight(c, r);
            }
            return;
        }
        if (v.RuneWindow) { DrawRuneWindow(c, v, hover); return; }
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
            DrawRuneWeb(c, v.Items, true);
            for (int i = 0; i < v.Items.Length; i++)
            {
                DrawModalItem(c, v.Items[i], i == hover);
                if (v.Items[i].Highlight) DrawHighlight(c, v.Items[i].Rect);
            }
            if (v.Tip is { } tip) DrawRuneTip(c, tip, v.Width, v.Height, true);
            return;
        }
        if (v.HasArrow) DrawArrow(c, Math.Clamp(v.ArrowX, 8f, v.Width - 8f), boxH);
        float titleX = TdUiController.Pad + 2;
        if (v.IconTower is not null)
        {
            DrawTowerIcon(c, v.IconTower, 4, 2, HeaderIconDip, 255);
            titleX += 22;
        }
        float titleSize = v.Title == "CORRIDA ENCERRADA" ? 14f : 10.5f;
        _p.Text(c, v.Title, PanelFont.JetBrainsMono, titleSize, new DipRect(titleX, 3, v.Width - 40 - (titleX - TdUiController.Pad - 2), 18), Palette.TxtPrincipal);
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
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, it.FontSize > 0f ? it.FontSize : 9f, r, color);
                if (it.Right is not null) _p.Text(c, it.Right, PanelFont.JetBrainsMono, it.FontSize > 0f ? it.FontSize : 8f, r, Palette.TxtRotulo, TextAlign.Right);
                _p.Fill(c, new DipRect(r.X, r.Y + r.H - 3, r.W, 3), Palette.BgTrilho);
                if (it.Fill > 0f) _p.Fill(c, new DipRect(r.X, r.Y + r.H - 3, r.W * Math.Clamp(it.Fill, 0f, 1f), 3), Palette.Ambar);
                return;
            case TdUiKind.Label:
                if (it.Icon != TdIcon.None)
                {
                    DrawIcon(c, it.Icon, r.X, r.H >= 24f ? r.Y + 1f : r.Y + (r.H - 12f) / 2f, 12f, color);
                    r = new DipRect(r.X + 16f, r.Y, r.W - 16f, r.H);
                }
                if (it.FontSize > 0f)
                {
                    DrawWrappedSized(c, it.Text, r, color, it.FontSize);
                    return;
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
        if (it.FontSize > 0f) { size = it.FontSize; top = r.Y + (r.H - size * 1.3f) / 2f; }
        var box = new DipRect(r.X + 4 + shift, top, r.W - 8 - shift, it.FontSize > 0f ? it.FontSize * 1.3f : 12);
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
        var vectorIcon = towerId switch
        {
            "functions" => TdIcon.Functions, "cosmos" => TdIcon.Cosmos, "sentinel" => TdIcon.Sentinel,
            "vault" => TdIcon.Vault, "backup" => TdIcon.Backup, "cdn" => TdIcon.Cdn,
            "queue" => TdIcon.Queue, "policy" => TdIcon.Policy, "aks" => TdIcon.Aks,
            "pipeline" => TdIcon.Pipeline, _ => TdIcon.None,
        };
        if (vectorIcon != TdIcon.None)
        {
            TdIconPainter.Draw(c, vectorIcon, _p.Px(xDip), _p.Px(yDip), _p.Px(heightDip), Palette.Td.Text.WithAlpha(alpha), Palette.Td.Amber.WithAlpha(alpha));
            return;
        }
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

    /// <summary>TD.30b: texto legível (fonte em DIP, escalada pelo monitor) com quebra por palavra em tantas linhas quantas cabem na altura do item.</summary>
    private void DrawWrappedSized(SKCanvas c, string text, DipRect box, SKColor color, float size)
    {
        float lineH = size * TdUiController.QuizLineFactor;
        int maxLines = Math.Max(1, (int)MathF.Floor((box.H + 1f) / lineH));
        if (!_p.TextEnabled) return;
        var font = _p.Font(PanelFont.JetBrainsMono, size);
        float max = _p.Px(box.W);
        var line = "";
        int n = 0;
        foreach (var w in text.Split(' '))
        {
            var test = line.Length == 0 ? w : line + " " + w;
            if (line.Length > 0 && font.MeasureText(test) > max && n < maxLines - 1)
            {
                _p.Text(c, line, PanelFont.JetBrainsMono, size, new DipRect(box.X, box.Y + n * lineH, box.W, lineH), color);
                n++;
                line = w;
            }
            else line = test;
        }
        if (line.Length > 0) _p.Text(c, line, PanelFont.JetBrainsMono, size, new DipRect(box.X, box.Y + n * lineH, box.W, lineH), color);
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
