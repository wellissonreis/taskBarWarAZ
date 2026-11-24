using TdAnimArt = SideWar.Rendering.Scene.TdAnimArt;
using TdAnimStrip = SideWar.Rendering.Scene.TdAnimStrip;
using TdVectorSprites = SideWar.Rendering.Scene.TdVectorSprites;
using TdArtMap = SideWar.Rendering.Scene.TdArtMap;
using TdSpriteBaker = SideWar.Rendering.Scene.TdSpriteBaker;
using TdFusionMark = SideWar.Rendering.Scene.TdFusionMark;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Core.Td;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Td;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>Mapa de fases + Selos (artboard Fases.dc.html): rota das regiões, painel de detalhe, banner e árvore de melhorias. Itens com SArg "map*" (ver TdUiController.AddMapModal).</summary>
public sealed partial class TdUiRenderer
{
    private static readonly SKPath MapStarPath = SKPath.ParseSvgPathData("M12 2.5l2.7 6 6.5.7-4.9 4.4 1.4 6.4L12 16.8 6.3 20l1.4-6.4-4.9-4.4 6.5-.7z")!;
    private static readonly SKPath MapHexPath = SKPath.ParseSvgPathData("M12 2l8.5 5v10L12 22l-8.5-5V7z")!;
    private static readonly SKPath MapLockPath = SKPath.ParseSvgPathData("M7 11V8a5 5 0 0 1 10 0v3h1v10H6V11zm2 0h6V8a3 3 0 0 0-6 0z")!;
    private static readonly SKPath MapPlayPath = SKPath.ParseSvgPathData("M7 4l13 8-13 8z")!;

    private static SKColor BranchColor(int branch) => branch switch { 0 => Palette.Td.Bolt, 1 => Palette.Td.TintOrange, _ => Palette.Td.Green };

    private void MapShape(SKCanvas c, SKPath path, float xDip, float yDip, float sizeDip, SKColor? fill, SKColor? stroke, float strokeUnits = 1.4f)
    {
        c.Save();
        c.Translate(_p.Px(xDip), _p.Px(yDip));
        float k = _p.Px(sizeDip) / 24f;
        c.Scale(k, k);
        if (fill is { } f) { using var fp = new SKPaint { IsAntialias = true, Color = f }; c.DrawPath(path, fp); }
        if (stroke is { } s) { using var sp = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = s, StrokeWidth = strokeUnits, StrokeJoin = SKStrokeJoin.Round }; c.DrawPath(path, sp); }
        c.Restore();
    }

    private void MapDashedCircle(SKCanvas c, float cx, float cy, float d, SKColor color, float widthDip, float dash, float gap)
    {
        using var fx = SKPathEffect.CreateDash([_p.Px(dash), _p.Px(gap)], 0f);
        using var pt = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = Math.Max(1, _p.Px(widthDip)), PathEffect = fx };
        c.DrawCircle(_p.Px(cx), _p.Px(cy), _p.Px(d / 2f) - 0.5f, pt);
    }

    /// <summary>Fundo do mapa (grade de pontos, título e rota entre as regiões). Chamado antes dos itens.</summary>
    private void DrawMapBack(SKCanvas c, TdUiItem[] items)
    {
        TdUiItem? bg = null;
        var nodes = new List<TdUiItem>(6);
        foreach (var it in items)
        {
            if (it.SArg == "mapbg") bg = it;
            else if (it.SArg == "mapnode") nodes.Add(it);
        }
        if (bg is not { } b) return;
        var r = b.Rect;
        RoundBox(c, r, Palette.Td.MapBorder, Palette.Td.Deep, 10);
        c.Save();
        c.ClipRect(new SKRect(_p.Px(r.X + 1), _p.Px(r.Y + 1), _p.Px(r.X + r.W - 1), _p.Px(r.Y + r.H - 1)));
        using (var dot = new SKPaint { Color = Palette.Td.MapDot, IsAntialias = false })
            for (float y = r.Y + 5; y < r.Y + r.H; y += 10)
                for (float x = r.X + 5; x < r.X + r.W; x += 10)
                    c.DrawRect(_p.Px(x), _p.Px(y), Math.Max(1, _p.Px(1)), Math.Max(1, _p.Px(1)), dot);
        _p.Text(c, b.Text, PanelFont.JetBrainsMono, 10, new DipRect(r.X + 12, r.Y + 10, 200, 14), Palette.Td.Dim);
        using var done = new SKPathBuilder();
        using var open = new SKPathBuilder();
        using var locked = new SKPathBuilder();
        for (int i = 0; i + 1 < nodes.Count; i++)
        {
            var a = nodes[i].Rect; var z = nodes[i + 1].Rect;
            float ax = a.X + a.W / 2f, ay = a.Y + a.H / 2f, zx = z.X + z.W / 2f, zy = z.Y + z.H / 2f;
            var pb = nodes[i + 1].Fill <= 0f ? locked : nodes[i + 1].Fill >= 1f ? done : open;
            pb.MoveTo(_p.Px(ax), _p.Px(ay));
            pb.QuadTo(_p.Px((ax + zx) / 2f), _p.Px((ay + zy) / 2f - 26f), _p.Px(zx), _p.Px(zy));
        }
        void Stroke(SKPathBuilder pb, SKColor col, float w, float dash, float gap)
        {
            using var path = pb.Detach();
            using var fx = dash > 0 ? SKPathEffect.CreateDash([_p.Px(dash), _p.Px(gap)], 0f) : null;
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, Color = col, StrokeWidth = Math.Max(1, _p.Px(w)), PathEffect = fx };
            c.DrawPath(path, paint);
        }
        Stroke(locked, Palette.Td.WavePending, 2f, 3f, 6f);
        Stroke(open, Palette.Td.Amber.WithAlpha(153), 2f, 5f, 5f);
        Stroke(done, Palette.Td.Amber.WithAlpha(230), 2.5f, 0f, 0f);
        c.Restore();
    }

    /// <summary>Estrelas pequenas (preenchidas/vazadas) centradas em <paramref name="cx"/>.</summary>
    private void MapStarRow(SKCanvas c, float cx, float y, float size, int on, float gap, SKColor onFill, SKColor onStroke, SKColor offStroke)
    {
        float x = cx - (3 * size + 2 * gap) / 2f;
        for (int i = 0; i < 3; i++, x += size + gap)
            MapShape(c, MapStarPath, x, y, size, i < on ? onFill : null, i < on ? onStroke : offStroke, 1.6f);
    }

    /// <summary>Desenha um item do mapa/selos; devolve false se o item não é do mapa.</summary>
    private bool DrawMapItem(SKCanvas c, in TdUiItem it, bool hover)
    {
        var r = it.Rect;
        switch (it.SArg)
        {
            case "mapbg": return true; // DrawMapBack
            case "mapsubtab":
            {
                if (it.Arg == 0) RoundBox(c, new DipRect(r.X - 3, r.Y - 3, 72 * 2 + 3 + 6, r.H + 6), Palette.Td.Border, Palette.Td.Deep, 8);
                if (it.Selected) RoundBox(c, r, Palette.Td.Amber, Palette.Td.Amber, 6);
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 12, new DipRect(r.X, r.Y + 6, r.W, 16), it.Selected ? Palette.Td.Background : hover ? Palette.Td.Text : Palette.Td.Muted, TextAlign.Center);
                return true;
            }
            case "mapsum":
            {
                float x = r.X;
                if (it.Arg == 0) MapShape(c, MapStarPath, x, r.Y + 2, 14, Palette.Td.Amber, null);
                else if (it.Arg == 1) DrawCrown(c, x, r.Y + 3, 16f, 11f, true);
                else MapShape(c, MapHexPath, x, r.Y + 2, 14, Palette.Td.Bolt.WithAlpha(51), Palette.Td.Bolt, 2f);
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 12, new DipRect(x + 19, r.Y + 1, r.W - 19, 16), Palette.Td.BrightText);
                if (it.Right is not null)
                {
                    float tw = _p.TextEnabled ? _p.Font(PanelFont.JetBrainsMono, 12).MeasureText(it.Text) / Math.Max(1f, _p.Px(1)) : it.Text.Length * 7f;
                    _p.Text(c, it.Right, PanelFont.JetBrainsMono, 12, new DipRect(x + 19 + tw, r.Y + 1, 40, 16), Palette.Td.Dim);
                }
                return true;
            }
            case "mapnode": DrawMapNode(c, it, hover); return true;
            case "mapdetail": DrawMapDetail(c, it); return true;
            case "mapplay": DrawMapButton(c, it, hover, primary: true); return true;
            case "mapcancel": DrawMapButton(c, it, hover, primary: false); return true;
            case "mapwarn":
                MapShape(c, SKPath.ParseSvgPathData("M12 4l9 16H3z")!, r.X, r.Y + 1, 12, null, Palette.Td.Pink, 2.2f);
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, new DipRect(r.X + 18, r.Y, r.W - 18, 14), Palette.Td.Pink);
                return true;
            case "mapbanner":
            {
                RoundBox(c, r, Palette.Td.Bolt.WithAlpha(89), Palette.Td.Bolt.WithAlpha(20), 8);
                MapShape(c, MapHexPath, r.X + 12, r.Y + 12, 22, Palette.Td.Bolt.WithAlpha(51), Palette.Td.Bolt, 1.8f);
                MapShape(c, MapStarPath, r.X + 17, r.Y + 17, 12, Palette.Td.Bolt, null);
                _p.Text(c, "Selos disponíveis:", PanelFont.JetBrainsMono, 12, new DipRect(r.X + 44, r.Y + 6, 170, 16), Palette.Td.Text);
                _p.Text(c, it.Arg.ToString(System.Globalization.CultureInfo.InvariantCulture), PanelFont.JetBrainsMono, 16, new DipRect(r.X + 44 + 140, r.Y + 4, 60, 20), Palette.Td.Bolt);
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, new DipRect(r.X + 44, r.Y + 26, r.W - 150, 14), Palette.Td.Muted);
                _p.Text(c, it.Right ?? "", PanelFont.JetBrainsMono, 11, new DipRect(r.X + r.W - 160, r.Y + 15, 148, 16), Palette.Td.Muted, TextAlign.Right);
                return true;
            }
            case "mapbranch":
            {
                var col = BranchColor(it.Arg);
                float cy = r.Y + r.H / 2f;
                c.Save();
                c.Translate(_p.Px(r.X + 5), _p.Px(cy));
                c.RotateDegrees(45);
                using (var dp = new SKPaint { IsAntialias = true, Color = col }) c.DrawRect(-_p.Px(5) * 0.85f, -_p.Px(5) * 0.85f, _p.Px(10) * 0.85f, _p.Px(10) * 0.85f, dp);
                c.Restore();
                string name = it.Text.ToUpperInvariant();
                _p.Text(c, name, PanelFont.JetBrainsMono, 11, new DipRect(r.X + 18, cy - 8, 140, 16), col);
                float nameW = _p.TextEnabled ? _p.Font(PanelFont.JetBrainsMono, 11).MeasureText(name) / Math.Max(1f, _p.Px(1)) : name.Length * 7f;
                float lx = r.X + 18 + nameW + 8, rx = r.X + r.W - 28;
                if (rx > lx) _p.Fill(c, new DipRect(lx, cy, rx - lx - 6, 1), Palette.Td.Border);
                _p.Text(c, it.Right ?? "", PanelFont.JetBrainsMono, 10, new DipRect(rx, cy - 7, 28, 14), Palette.Td.Dim, TextAlign.Right);
                return true;
            }
            case "maplink":
                _p.Fill(c, r, it.Fill >= 1f ? BranchColor(it.Arg) : Palette.Td.EdgeSoft);
                return true;
        }
        if (it.Action == TdUiAction.BuyNode) { DrawMapBuy(c, it, hover); return true; }
        return false;
    }

    private void DrawMapNode(SKCanvas c, in TdUiItem it, bool hover)
    {
        var r = it.Rect;
        float cx = r.X + r.W / 2f, cy = r.Y + r.H / 2f;
        bool locked = it.Fill <= 0f, cleared = it.Fill >= 1f;
        if (it.Selected)
        {
            using var halo = new SKPaint { IsAntialias = true, Color = Palette.Td.Amber.WithAlpha(64) };
            c.DrawCircle(_p.Px(cx), _p.Px(cy), _p.Px(r.W / 2f + 4f), halo);
            using var glow = new SKPaint { IsAntialias = true, Color = Palette.Td.Amber.WithAlpha(72), ImageFilter = SKImageFilter.CreateBlur(_p.Px(9), _p.Px(9)) };
            c.DrawCircle(_p.Px(cx), _p.Px(cy), _p.Px(r.W / 2f), glow);
        }
        using (var f = new SKPaint { IsAntialias = true, Color = locked ? Palette.Td.Surface : cleared ? Palette.Td.Amber : Palette.Td.NodeOpen })
            c.DrawCircle(_p.Px(cx), _p.Px(cy), _p.Px(r.W / 2f), f);
        if (locked) MapDashedCircle(c, cx, cy, r.W, Palette.Td.WavePending, 1f, 3f, 2.5f);
        else if (cleared)
        {
            using var l = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = Palette.Td.AmberLight, StrokeWidth = Math.Max(2, _p.Px(2)) };
            c.DrawCircle(_p.Px(cx), _p.Px(cy), _p.Px(r.W / 2f) - _p.Px(1), l);
        }
        else MapDashedCircle(c, cx, cy, r.W - 2, hover ? Palette.Td.AmberLight : Palette.Td.Amber, 2f, 4f, 3f);
        if (!locked) _p.Text(c, it.Right ?? "", PanelFont.JetBrainsMono, 13, new DipRect(r.X, cy - 8, r.W, 16), cleared ? Palette.Td.Background : Palette.Td.Amber, TextAlign.Center);
        float ny = r.Y + r.H + 3f;
        _p.Text(c, it.Text, PanelFont.JetBrainsMono, 11, new DipRect(cx - 60, ny, 120, 14), locked ? Palette.Td.Faint : it.Selected ? Palette.Td.BrightText : hover ? Palette.Td.BrightText : Palette.Td.Text, TextAlign.Center);
        if (locked) _p.Text(c, "bloqueada", PanelFont.JetBrainsMono, 10, new DipRect(cx - 60, ny + 15, 120, 13), Palette.Td.Faint, TextAlign.Center);
        else
        {
            int stars = int.TryParse(it.Sub, out var s) ? s : 0;
            MapStarRow(c, cx, ny + 16, 10, stars, 1f, Palette.Td.Amber, Palette.Td.Amber, Palette.Td.Amber);
        }
    }

    private void DrawMapDetail(SKCanvas c, in TdUiItem it)
    {
        var r = it.Rect;
        bool locked = !it.Enabled;
        int stars = it.Arg & 3, medals = (it.Arg >> 3) & 15, num = it.Arg >> 8;
        bool crown = (it.Arg & 4) != 0;
        RoundBox(c, r, locked ? Palette.Td.EdgeSoft : Palette.Td.Amber.WithAlpha(115), Palette.Td.Card, 10);
        float x = r.X + 14, w = r.W - 28, y = r.Y + 14;
        _p.Text(c, $"FASE {num} · {it.Sub}", PanelFont.JetBrainsMono, 10, new DipRect(x, y, w, 14), Palette.Td.Muted);
        _p.Text(c, it.Text, PanelFont.JetBrainsMono, 18, new DipRect(x, y + 14, w, 24), Palette.Td.BrightText);
        y += 48;
        for (int i = 0; i < 3; i++)
            MapShape(c, MapStarPath, x + i * 30, y, 26, i < stars ? Palette.Td.Amber : null, i < stars ? Palette.Td.AmberLight : Palette.Td.StarOff);
        y += 36;
        var box = new DipRect(x, y, w, 42);
        RoundBox(c, box, crown ? Palette.Td.Amber.WithAlpha(153) : Palette.Td.EdgeSoft, crown ? Palette.Td.Amber.WithAlpha(31) : Palette.Td.Surface, 8);
        DrawCrown(c, x + 10, y + 11, 24f, 17f, crown);
        _p.Text(c, crown ? "Coroa conquistada" : "Coroa pendente", PanelFont.JetBrainsMono, 12, new DipRect(x + 44, y + 6, w - 48, 16), crown ? Palette.Td.AmberLight : Palette.Td.Muted);
        _p.Text(c, "Vença sem perder núcleo.", PanelFont.JetBrainsMono, 10, new DipRect(x + 44, y + 23, w - 48, 14), Palette.Td.Muted);
        y += 52;
        _p.Text(c, "SELOS DA FASE", PanelFont.JetBrainsMono, 10, new DipRect(x, y, w, 14), Palette.Td.Muted);
        y += 20;
        for (int i = 0; i < MedalGlyphs.Length; i++)
        {
            bool got = (medals & MedalGlyphs[i].Bit) != 0;
            var col = MedalColor(MedalGlyphs[i].Bit);
            var cell = new DipRect(x + i * 36, y, 30, 30);
            if (got)
            {
                using var gl = new SKPaint { IsAntialias = true, Color = col.WithAlpha(60), ImageFilter = SKImageFilter.CreateBlur(_p.Px(4), _p.Px(4)) };
                c.DrawCircle(_p.Px(cell.X + 15), _p.Px(cell.Y + 15), _p.Px(15), gl);
                using var fp = new SKPaint { IsAntialias = true, Color = col };
                c.DrawCircle(_p.Px(cell.X + 15), _p.Px(cell.Y + 15), _p.Px(15), fp);
            }
            else MapDashedCircle(c, cell.X + 15, cell.Y + 15, 30, Palette.Td.WavePending, 1f, 3f, 2.5f);
            _p.Text(c, MedalGlyphs[i].Glyph, PanelFont.JetBrainsMono, 12, new DipRect(cell.X, cell.Y + 7, cell.W, 16), got ? Palette.Td.Background : Palette.Td.Faint, TextAlign.Center);
        }
        float by = r.Y + r.H - 14 - 24;
        _p.Text(c, "Recorde", PanelFont.JetBrainsMono, 11, new DipRect(x, by + 8, 80, 14), Palette.Td.Muted);
        _p.Text(c, it.Right ?? "—", PanelFont.JetBrainsMono, 16, new DipRect(x + 80, by + 2, w - 80, 22), it.Right is "—" ? Palette.Td.Faint : Palette.Td.BrightText, TextAlign.Right);
    }

    private void DrawMapButton(SKCanvas c, in TdUiItem it, bool hover, bool primary)
    {
        var r = it.Rect;
        bool disabled = !it.Enabled;
        if (disabled) DashedBox(c, r, Palette.Td.WavePending, 8);
        else if (primary) RoundBox(c, r, Palette.Td.AmberLight, hover ? Palette.AmbarHover : Palette.Td.Amber, 8);
        else RoundBox(c, r, Palette.Td.BorderStrong, hover ? Palette.Td.BorderStrong : Palette.Td.Button, 8);
        var fg = disabled ? Palette.Td.Dim : primary ? Palette.Td.Background : Palette.Td.Text;
        float tw = _p.TextEnabled ? _p.Font(PanelFont.JetBrainsMono, 13).MeasureText(it.Text) / Math.Max(1f, _p.Px(1)) : it.Text.Length * 8f;
        float total = tw + (primary ? 22f : 0f), x = r.X + (r.W - total) / 2f;
        if (primary) MapShape(c, disabled ? MapLockPath : MapPlayPath, x, r.Y + (r.H - 14) / 2f, 14, fg, null);
        _p.Text(c, it.Text, PanelFont.JetBrainsMono, 13, new DipRect(primary ? x + 22 : r.X, r.Y + (r.H - 16) / 2f, primary ? tw + 4 : r.W, 16), fg, primary ? TextAlign.Left : TextAlign.Center);
    }

    private void DrawMapBuy(SKCanvas c, in TdUiItem it, bool hover)
    {
        var r = it.Rect;
        var col = BranchColor(it.Arg / 16);
        int tier = it.Arg % 16;
        bool have = it.Fill >= 1f, avail = !have && it.Fill > 0f, afford = it.Enabled;
        if (afford)
        {
            using var halo = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = col.WithAlpha((byte)(hover ? 70 : 34)), StrokeWidth = _p.Px(3) };
            c.DrawRoundRect(new SKRect(_p.Px(r.X) - 1.5f, _p.Px(r.Y) - 1.5f, _p.Px(r.X + r.W) + 1.5f, _p.Px(r.Y + r.H) + 1.5f), _p.Px(9), _p.Px(9), halo);
        }
        if (have) RoundBox(c, r, col.WithAlpha(136), Palette.Td.TierOwned, 8);
        else if (avail) RoundBox(c, r, col, Palette.Td.Card, 8);
        else DashedBox(c, r, Palette.Td.EdgeSoft, 8, Palette.Td.Surface);
        var tier24 = new DipRect(r.X + 8, r.Y + (r.H - 24) / 2f, 24, 24);
        if (have) RoundBox(c, tier24, col, col, 6);
        else if (avail) RoundBox(c, tier24, col, Palette.Td.Card, 6);
        else DashedBox(c, tier24, Palette.Td.WavePending, 6);
        string[] roman = ["I", "II", "III", "IV"];
        _p.Text(c, roman[Math.Clamp(tier, 0, 3)], PanelFont.JetBrainsMono, 10, new DipRect(tier24.X, tier24.Y + 5, 24, 14), have ? Palette.Td.Background : avail ? col : Palette.Td.Faint, TextAlign.Center);
        string cost = it.Right ?? "";
        float cw = (_p.TextEnabled ? _p.Font(PanelFont.JetBrainsMono, 10).MeasureText(cost) / Math.Max(1f, _p.Px(1)) : cost.Length * 6f) + 12f;
        var costBox = new DipRect(r.X + r.W - 8 - cw, r.Y + (r.H - 18) / 2f, cw, 18);
        var costCol = have ? Palette.Td.Green : afford ? Palette.Td.Bolt : avail ? Palette.Td.Pink : Palette.Td.Faint;
        if (have) RoundBox(c, costBox, Palette.Td.Green.WithAlpha(31), Palette.Td.Green.WithAlpha(31), 4);
        _p.Text(c, cost, PanelFont.JetBrainsMono, 10, new DipRect(costBox.X, costBox.Y + 2, costBox.W, 14), costCol, TextAlign.Center);
        float nx = r.X + 40, nw = costBox.X - nx - 6;
        var lines = WrapLines(it.Text, 11f, nw);
        float ty = r.Y + (r.H - Math.Min(2, lines.Count) * 14f) / 2f;
        for (int i = 0; i < lines.Count && i < 2; i++)
            _p.Text(c, lines[i], PanelFont.JetBrainsMono, 11, new DipRect(nx, ty + i * 14f, nw, 14), have ? Palette.Td.Text : avail ? Palette.Td.BrightText : Palette.Td.Faint);
    }
}
