using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Panel;
using SkiaSharp;
using D = SideWar.Rendering.Panel.ArsenalCampaignDraw;

namespace SideWar.Rendering.Panel;

/// <summary>
/// Renderer Skia puro do corpo da aba Arsenal (SDD-24 RF-24.5, RF-24.12). O canvas tem origem no canto da caixa visível do painel,
/// em pixel físico; desenha só dentro do viewport (clip) e só as linhas visíveis. Plugar em <c>OverlayWindow.Renderer</c> depois
/// do cromo; o cabeçalho e a barra de rolagem vêm por cima (RF-24.13).
/// </summary>
public static class ArsenalTabRenderer
{
    public static void Draw(SKCanvas canvas, ArsenalLayout layout, ArsenalHit hover, float offsetDip, float viewportDip, ArsenalCampaignPaintContext ctx,
        BuildOrigin? building = null)
    {
        bool prev = D.TextSuppressed;
        D.TextSuppressed = ctx is { TextEnabled: false };
        try { DrawCore(canvas, layout, hover, offsetDip, viewportDip, ctx, building); }
        finally { D.TextSuppressed = prev; }
    }

    private static void DrawCore(SKCanvas canvas, ArsenalLayout layout, ArsenalHit hover, float offsetDip, float viewportDip, ArsenalCampaignPaintContext ctx,
        BuildOrigin? building)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(ctx);
        float s = ctx.Scale;
        canvas.Save();
        canvas.ClipRect(new SKRect(D.Px(2f, s), D.Px(PanelScroll.ViewportTopDip, s), D.Px(398f, s), D.Px(PanelScroll.ViewportTopDip + viewportDip, s)));
        DrawAutoCard(canvas, layout.AutoCard, offsetDip, s, ctx);

        DrawHelpCard(canvas, layout.HelpCard, offsetDip, s, ctx);

        var rows = layout.Rows;
        for (int ri = 0; ri < rows.Count; ri++)
        {
            var row = rows[ri];
            if (row.Rect.Y + row.Rect.H < offsetDip || row.Rect.Y > offsetDip + viewportDip) continue;
            DrawRow(canvas, row, hover, building, offsetDip, s, ctx);
        }
        canvas.Restore();
    }

    private static SKFont Silk(ArsenalCampaignPaintContext ctx, float sz) => ctx.Fonts.GetFont(FontRole.SilkscreenRegular, sz, ctx.Dpi);
    private static SKFont Mono(ArsenalCampaignPaintContext ctx, float sz) => ctx.Fonts.GetFont(FontRole.JetBrainsMonoRegular, sz, ctx.Dpi);

    private static void DrawAutoCard(SKCanvas c, ArsenalAutoCardLayout a, float off, float s, ArsenalCampaignPaintContext ctx)
    {
        D.Box(c, a.Rect, s, off, Palette.BgCard, Palette.BordaForte);
        float tx = a.Rect.X + 13f;
        D.TextTop(c, ArsenalModel.AutoScaleTitle, Silk(ctx, 11f), tx, a.TitleTop, s, off, Palette.TxtPrincipal, 1f);
        for (int i = 0; i < a.DescriptionLines.Length; i++)
            D.TextTop(c, a.DescriptionLines[i], Mono(ctx, 11f), tx, a.DescriptionTop + i * a.DescriptionLineHeight, s, off, Palette.TxtTerciario);

        bool on = a.ToggleText == "ON";
        var box = D.Rect(a.Box, s, off);
        if (on)
        {
            D.SolidPx(c, box, Palette.Menta);
            D.DrawCheck(c, box, s, Palette.BgTrilhoFundo);
        }
        else
        {
            D.SolidPx(c, box, Palette.TxtRotulo);
            float b = MathF.Max(1f, MathF.Round(s));
            D.SolidPx(c, new SKRect(box.Left + b, box.Top + b, box.Right - b, box.Bottom - b), Palette.BgCard);
        }
        var f = Silk(ctx, 10f);
        float lh = D.LinePx(f);
        var (_, ty) = D.Map(0, a.Toggle.Y + a.Toggle.H / 2f, off);
        D.TextBaseline(c, a.ToggleText, f, D.Px(2f + a.ToggleTextX, s), D.Px(ty, s) - lh / 2f - f.Metrics.Ascent, on ? Palette.Menta : Palette.TxtRotulo, 0f, s);
    }

    private static void DrawHelpCard(SKCanvas c, ArsenalHelpCardLayout h, float off, float s, ArsenalCampaignPaintContext ctx)
    {
        D.Box(c, h.Rect, s, off, Palette.MentaFundo, Palette.MentaBorda);
        var f = Mono(ctx, 11f);
        for (int i = 0; i < h.Lines.Length; i++)
            D.TextTop(c, h.Lines[i], f, h.Rect.X + 13f, h.TextTop + i * h.LineHeight, s, off, Palette.MentaTexto);
    }

    private static void DrawRow(SKCanvas c, ArsenalRowLayout r, ArsenalHit hover, BuildOrigin? building, float off, float s, ArsenalCampaignPaintContext ctx)
    {
        var m = r.Model;
        D.BeginOpacity(c, m.Opacity);
        D.Box(c, r.Rect, s, off, Palette.BgItem, Palette.BordaPainel);
        // Ícone do tipo em construção (e da torre em reposicionamento): borda 2 DIP menta enquanto o modo dura.
        bool active = building is { } b && m.Kind is ArsenalRowKind.Tower or ArsenalRowKind.Available or ArsenalRowKind.NoPosition
            && b.Type == m.Type && (b.RepositionTowerId is { } rid ? rid == m.TowerId : m.Kind != ArsenalRowKind.NoPosition);
        if (active) D.Box(c, r.Slot, s, off, Palette.BgTrilhoFundo, Palette.Menta, 2f);
        else D.Solid(c, r.Slot, s, off, Palette.BgTrilhoFundo);
        D.DrawSprite(c, ctx.Sprites, D.SpriteFor(m.Type, m.Kind == ArsenalRowKind.Locked), D.Rect(r.Slot, s, off), ArsenalLayout.SpriteBox, s);

        var nf = Silk(ctx, 12f);
        var (_, ny) = D.Map(0, r.NameTop, off);
        float baseline = D.Px(ny, s) - nf.Metrics.Ascent;
        float x = D.Px(2f + r.TextX, s);
        float nameW = D.TextBaseline(c, m.Name, nf, x, baseline, Palette.TxtPrincipal, 0f, s);
        D.TextBaseline(c, m.LevelText, Mono(ctx, 10f), x + nameW + D.Px(8f, s), baseline, m.LevelIsSeal ? Palette.TxtWidgetRotulo : Palette.Ambar, 0f, s);
        D.TextTop(c, m.Azure, Mono(ctx, 10f), r.TextX, r.AzureTop, s, off, Palette.TxtRotulo);
        D.TextTop(c, m.Stats, Mono(ctx, 10f), r.TextX, r.StatsTop, s, off, Palette.TxtSecundario);

        bool locked = m.ButtonStyle == ArsenalButtonStyle.Locked;
        bool hot = !locked && !r.ButtonHit.IsNone && hover == r.ButtonHit;
        var bp = D.Rect(r.Button, s, off);
        D.SolidPx(c, bp, locked ? Palette.BgTrilho : hot ? Palette.AmbarHover : Palette.Ambar);
        SKColor fg = locked ? Palette.TxtRotulo : Palette.BgTrilhoFundo;
        var lf = Silk(ctx, 10f);
        var cf = Silk(ctx, 9f);
        bool hasCost = m.CostText.Length > 0;
        float lh = D.LinePx(lf), ch = D.LinePx(cf), gap = D.Px(2f, s);
        float total = lh + (hasCost ? gap + ch : 0f);
        float top = bp.MidY - total / 2f;
        D.TextBaseline(c, m.ButtonLabel, lf, bp.MidX - D.TextWidthPx(lf, m.ButtonLabel, 0f, s) / 2f, top - lf.Metrics.Ascent, fg, 0f, s);
        if (hasCost)
            D.TextBaseline(c, m.CostText, cf, bp.MidX - D.TextWidthPx(cf, m.CostText, 0f, s) / 2f, top + lh + gap - cf.Metrics.Ascent, fg, 0f, s);
        c.Restore();
    }
}
