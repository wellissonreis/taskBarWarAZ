using SideWar.Runtime.Panel;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Td;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>Aba Jogo da modal (artboard Jogo.dc.html) e a barra de abas: itens com SArg "jg-*" (ver TdUiController.AddJogoTab).</summary>
public sealed partial class TdUiRenderer
{
    private float JogoTextWidth(string text, PanelFont face, float size) =>
        _p.TextEnabled ? _p.Font(face, size).MeasureText(text) / Math.Max(1f, _p.Px(1)) : text.Length * size * 0.6f;

    /// <summary>Desenha o item se ele pertence à aba Jogo ou à barra de abas; false = não é daqui.</summary>
    private bool DrawJogoItem(SKCanvas c, in TdUiItem it, bool hover)
    {
        var r = it.Rect;
        if (it.Action == TdUiAction.ModalTab && it.SArg is null) { DrawModalTabButton(c, it, hover); return true; }
        if (it.Kind == TdUiKind.Bar && it.SArg == "rank-card") { DrawPlayerCard(c, it); return true; }
        switch (it.SArg)
        {
            case "jg-stat": DrawJogoStat(c, it); return true;
            case "jg-warn": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 9, r, ModalToneColor(it.Tone)); return true;
            case "jg-tile": DrawJogoTile(c, it, hover); return true;
            case "jg-tile-speed":
                RoundBox(c, r, Palette.Td.EdgeSoft, Palette.Td.Card, 6);
                _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 10, new DipRect(r.X + 8, r.Y + 4, r.W - 16, 13), Palette.Td.Muted);
                return true;
        }
        if (it.Action == TdUiAction.ResumeAbility)
        {
            RoundBox(c, r, Palette.Td.EdgeSoft, hover && it.Enabled ? Palette.Td.BorderStrong : Palette.Td.Card, 6);
            var fg = it.Enabled ? Palette.Td.Text : Palette.Td.Muted;
            var icon = it.Text switch { "Escalar" => TdIcon.Scale, "Firewall" => TdIcon.Firewall, _ => TdIcon.Snapshot };
            float tw = JogoTextWidth(it.Text, PanelFont.SpaceGrotesk, 11), x = r.X + (r.W - (20 + tw)) / 2f;
            DrawIcon(c, icon, x, r.Y + (r.H - 14) / 2f, 14, fg);
            _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 11, new DipRect(x + 20, r.Y + (r.H - 14) / 2f, tw + 6, 14), fg);
            return true;
        }
        return false;
    }

    /// <summary>Aba da modal: selecionada = fundo Button com filete âmbar de 2 DIP embaixo; as demais só texto.</summary>
    private void DrawModalTabButton(SKCanvas c, in TdUiItem it, bool hover)
    {
        var r = it.Rect;
        if (it.Selected || hover)
        {
            c.Save();
            c.ClipRect(SKRect.Create(_p.Px(r.X), _p.Px(r.Y), _p.Px(r.W), _p.Px(r.H)));
            RoundBox(c, new DipRect(r.X, r.Y, r.W, r.H + 8), it.Selected ? Palette.Td.Button : Palette.Td.Surface, it.Selected ? Palette.Td.Button : Palette.Td.Surface, 6);
            c.Restore();
        }
        if (it.Selected) _p.Fill(c, new DipRect(r.X, r.Y + r.H - 2, r.W, 2), Palette.Td.Amber);
        _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 13, new DipRect(r.X + 4, r.Y + 9, r.W - 8, 18), it.Selected ? Palette.Td.BrightText : Palette.Td.Muted, TextAlign.Center);
    }

    /// <summary>Card do jogador (688×178): brasão 144, patente, escada de coroas, barra e pontos de XP.</summary>
    private void DrawPlayerCard(SKCanvas c, in TdUiItem it)
    {
        var r = it.Rect;
        RoundBox(c, r, Palette.Td.Amber.WithAlpha(140), Palette.Td.Card, 12);
        c.Save();
        c.ClipRect(SKRect.Create(_p.Px(r.X + 1), _p.Px(r.Y + 1), _p.Px(r.W - 2), _p.Px(r.H - 2)));
        using (var glow = SKShader.CreateRadialGradient(new SKPoint(_p.Px(r.X + 90), _p.Px(r.Y + 80)), _p.Px(110),
            [Palette.Td.Amber.WithAlpha(51), Palette.Td.Amber.WithAlpha(0)], [0f, 0.68f], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = glow, IsAntialias = true })
            c.DrawCircle(_p.Px(r.X + 90), _p.Px(r.Y + 80), _p.Px(110), paint);
        c.Restore();
        DrawBrasao(c, it.Arg, new DipRect(r.X + 14, r.Y + 17, 144, 144));
        float tx = r.X + 172, tw = 276;
        _p.Text(c, $"PATENTE ATUAL · NÍVEL {it.Arg + 1}/6", PanelFont.JetBrainsMono, 10, new DipRect(tx, r.Y + 20, tw, 14), Palette.Td.Muted);
        _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 30, new DipRect(tx, r.Y + 39, tw, 34), RankColor(it.Arg));
        if (it.Arg == 0) _p.Text(c, "Ainda no período de experiência.", PanelFont.SpaceGrotesk, 11, new DipRect(tx, r.Y + 76, tw, 14), Palette.Td.Text);
        for (int i = 0; i < 6; i++)
        {
            var cell = new DipRect(tx + i * 33, r.Y + 95, 28, 26);
            if (i == it.Arg) RoundBox(c, cell, Palette.Td.AmberLight, Palette.Td.PauseBadge, 5);
            else if (i == it.Arg + 1) DashedBox(c, cell, Palette.Td.Amber, 5, Palette.Td.Amber.WithAlpha(20));
            else RoundBox(c, cell, Palette.Td.EdgeSoft, Palette.Td.Deep, 5);
            DrawCrown(c, i, new DipRect(cell.X + 4, cell.Y + 3, 20, 20), i == it.Arg || i == it.Arg + 1 ? (byte)255 : (byte)89);
        }
        RoundBox(c, new DipRect(tx, r.Y + 129, tw, 10), Palette.Td.BorderStrong, Palette.Td.Deep, 5);
        if (it.Fill > 0) _p.Fill(c, new DipRect(tx + 1, r.Y + 130, (tw - 2) * Math.Clamp(it.Fill, 0f, 1f), 8), Palette.Td.Amber);
        var labels = (it.Right ?? "").Split('|');
        _p.Text(c, labels[0], PanelFont.JetBrainsMono, 11, new DipRect(tx, r.Y + 145, 130, 14), Palette.Td.BrightText);
        if (labels.Length > 1) _p.Text(c, labels[1], PanelFont.SpaceGrotesk, 11, new DipRect(tx + 120, r.Y + 145, tw - 120, 14), Palette.Td.Muted, TextAlign.Right);
    }

    /// <summary>Estatística do jogador (103×72): ícone + valor grande + rótulo; Coroas em destaque âmbar. <c>Fill</c> = patente atual (coroa).</summary>
    private void DrawJogoStat(SKCanvas c, in TdUiItem it)
    {
        var r = it.Rect;
        bool crown = it.Arg == 0;
        RoundBox(c, r, crown ? Palette.Td.Amber.WithAlpha(128) : Palette.Td.EdgeSoft, crown ? Palette.Td.Amber.WithAlpha(26) : Palette.Td.Surface, 8);
        float iy = r.Y + 9;
        switch (it.Arg)
        {
            case 0: DrawCrown(c, (int)it.Fill, new DipRect(r.X + 10, iy, 20, 20)); break;
            case 1: MapShape(c, MapStarPath, r.X + 10, iy + 1, 18, Palette.Td.Amber, null); break;
            case 2: DrawIcon(c, TdIcon.Portal, r.X + 10, iy + 1, 18, Palette.Td.Green); break;
            default: DrawIcon(c, TdIcon.Medal, r.X + 10, iy + 1, 18, Palette.Td.Bolt); break;
        }
        float vx = r.X + 36;
        _p.Text(c, it.Text, PanelFont.JetBrainsMono, 20, new DipRect(vx, r.Y + 8, r.W - 40, 24), crown ? Palette.Td.AmberLight : Palette.Td.BrightText);
        if (!string.IsNullOrEmpty(it.Right))
        {
            float w = JogoTextWidth(it.Text, PanelFont.JetBrainsMono, 20);
            _p.Text(c, it.Right, PanelFont.JetBrainsMono, 12, new DipRect(vx + w + 1, r.Y + 14, r.W - 40 - w, 16), crown ? Palette.Td.RankIntern : Palette.Td.Dim);
        }
        _p.Text(c, it.Sub ?? "", PanelFont.SpaceGrotesk, 11, new DipRect(r.X + 10, r.Y + 49, r.W - 16, 14), crown ? Palette.Td.Amber : Palette.Td.Muted);
    }

    /// <summary>Tile de configuração (121×40): rótulo, interruptor com estado, valor (Faixa) ou controle deslizante (Volume).</summary>
    private void DrawJogoTile(SKCanvas c, in TdUiItem it, bool hover)
    {
        var r = it.Rect;
        RoundBox(c, r, hover ? Palette.Td.BorderStrong : Palette.Td.EdgeSoft, Palette.Td.Card, 6);
        int colon = it.Text.IndexOf(':');
        string name = colon > 0 ? it.Text[..colon] : it.Text, val = colon > 0 ? it.Text[(colon + 1)..].Trim() : "";
        _p.Text(c, name, PanelFont.SpaceGrotesk, 10, new DipRect(r.X + 8, r.Y + 5, r.W - 16, 13), Palette.Td.Muted);
        if (it.Arg == 0) { _p.Text(c, val, PanelFont.JetBrainsMono, 12, new DipRect(r.X + 8, r.Y + 21, r.W - 16, 16), Palette.Td.BrightText); return; }
        if (it.Arg == 3)
        {
            _p.Text(c, val, PanelFont.JetBrainsMono, 10, new DipRect(r.X + 8, r.Y + 5, r.W - 16, 13), Palette.Td.BrightText, TextAlign.Right);
            float left = r.X + 8, trackW = r.W - 16, ty = r.Y + 27;
            RoundBox(c, new DipRect(left, ty, trackW, 4), Palette.Td.BorderStrong, Palette.Td.BorderStrong, 2);
            if (it.Fill > 0) _p.Fill(c, new DipRect(left, ty, trackW * Math.Clamp(it.Fill, 0f, 1f), 4), Palette.Td.Amber);
            RoundBox(c, new DipRect(left + trackW * Math.Clamp(it.Fill, 0f, 1f) - 4, ty - 2, 8, 8), Palette.Td.Amber, Palette.Td.Amber, 4);
            return;
        }
        bool on = val.StartsWith("lig", StringComparison.Ordinal);
        RoundBox(c, new DipRect(r.X + 8, r.Y + 21, 26, 14), on ? Palette.Td.Amber : Palette.Td.WavePending, on ? Palette.Td.Amber : Palette.Td.WavePending, 7);
        RoundBox(c, new DipRect(r.X + (on ? 22 : 10), r.Y + 23, 10, 10), on ? Palette.Td.Background : Palette.Td.Muted, on ? Palette.Td.Background : Palette.Td.Muted, 5);
        _p.Text(c, on ? "Ligado" : "Desligado", PanelFont.SpaceGrotesk, 10, new DipRect(r.X + 40, r.Y + 21, r.W - 44, 14), on ? Palette.Td.Amber : Palette.Td.Dim);
    }
}
