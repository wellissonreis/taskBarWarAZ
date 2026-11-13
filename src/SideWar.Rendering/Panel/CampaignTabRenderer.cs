using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using SkiaSharp;
using D = SideWar.Rendering.Panel.ArsenalCampaignDraw;

namespace SideWar.Rendering.Panel;

/// <summary>Renderer Skia puro do corpo da aba Campanha (SDD-24 RF-24.6); mesma convenção de canvas do <see cref="ArsenalTabRenderer"/>. Sem alvos clicáveis.</summary>
public static class CampaignTabRenderer
{
    public static void Draw(SKCanvas canvas, CampaignLayout layout, float offsetDip, float viewportDip, ArsenalCampaignPaintContext ctx)
    {
        bool prev = D.TextSuppressed;
        D.TextSuppressed = ctx is { TextEnabled: false };
        try { DrawCore(canvas, layout, offsetDip, viewportDip, ctx); }
        finally { D.TextSuppressed = prev; }
    }

    private static void DrawCore(SKCanvas canvas, CampaignLayout layout, float offsetDip, float viewportDip, ArsenalCampaignPaintContext ctx)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(ctx);
        float s = ctx.Scale;
        int dpi = ctx.Dpi;
        SKFont Silk(float sz) => ctx.Fonts.GetFont(FontRole.SilkscreenRegular, sz, dpi);
        SKFont Mono(float sz) => ctx.Fonts.GetFont(FontRole.JetBrainsMonoRegular, sz, dpi);

        canvas.Save();
        canvas.ClipRect(new SKRect(D.Px(2f, s), D.Px(PanelScroll.ViewportTopDip, s), D.Px(398f, s), D.Px(PanelScroll.ViewportTopDip + viewportDip, s)));

        var k = layout.Card;
        D.Box(canvas, k.Rect, s, offsetDip, Palette.BgCard, Palette.Azul);
        float tx = k.Rect.X + 15f;
        D.TextTop(canvas, layout.Model.StageLabel, Mono(10f), tx, k.LabelTop, s, offsetDip, Palette.AzulClaro, 1f);
        for (int i = 0; i < k.NameLines.Length; i++)
            D.TextTop(canvas, k.NameLines[i], Silk(15f), tx, k.NameTop + i * k.NameLineHeight, s, offsetDip, Palette.TxtPrincipal);
        D.Solid(canvas, k.BarTrack, s, offsetDip, Palette.BgTrilho);
        var track = D.Rect(k.BarTrack, s, offsetDip);
        float fill = MathF.Round(layout.Model.Fraction * track.Width); // arredondado uma vez em px
        if (fill > 0f) D.SolidPx(canvas, new SKRect(track.Left, track.Top, track.Left + fill, track.Bottom), Palette.Azul);
        D.TextTop(canvas, layout.Model.WaveText, Mono(11f), k.WaveX, k.FooterTop, s, offsetDip, Palette.TxtSecundario);
        for (int i = 0; i < k.BossLines.Length; i++)
            D.TextTop(canvas, k.BossLines[i], Mono(11f), k.BossX, k.FooterTop + i * k.FooterLineHeight, s, offsetDip, Palette.TxtSecundario);

        var rows = layout.Rows;
        for (int ri = 0; ri < rows.Count; ri++)
        {
            var r = rows[ri];
            if (r.Rect.Y + r.Rect.H < offsetDip || r.Rect.Y > offsetDip + viewportDip) continue;
            D.BeginOpacity(canvas, r.Model.Opacity);
            D.Box(canvas, r.Rect, s, offsetDip, Palette.BgItem, Palette.BordaPainel);
            D.TextTop(canvas, r.Model.Number, Silk(11f), r.NumberX, r.NumberTop, s, offsetDip, Palette.TxtRotulo);
            for (int i = 0; i < r.TitleLines.Length; i++)
                D.TextTop(canvas, r.TitleLines[i], Mono(12f), r.TitleX, r.TitleTop + i * r.TitleLineHeight, s, offsetDip, Palette.TxtPrincipal);
            if (r.Model.Locked)
                D.TextTop(canvas, "BLOQ.", Silk(11f), r.Stars.X, r.StarsTextTop, s, offsetDip, Palette.Ambar);
            else
            {
                float d = D.Px(10f, s), stroke = MathF.Max(1f, MathF.Round(s));
                for (int i = 0; i < 3; i++)
                {
                    var (sx, sy) = D.Map(r.Stars.X + i * CampaignLayout.StarPitch + CampaignLayout.StarPitch / 2f, r.Stars.Y + r.Stars.H / 2f, offsetDip);
                    D.DrawStar(canvas, D.Px(sx, s), D.Px(sy, s), d, Palette.Ambar, i < r.Model.Stars, stroke);
                }
            }
            canvas.Restore();
        }
        canvas.Restore();
    }
}
