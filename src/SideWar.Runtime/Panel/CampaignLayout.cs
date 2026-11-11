using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Panel;

public sealed record CampaignCardLayout(
    DipRect Rect, float LabelTop, float LabelHeight, float NameTop, float NameLineHeight, string[] NameLines,
    DipRect BarTrack, float FooterTop, float FooterLineHeight, float WaveX, float BossX, string[] BossLines);

public sealed record CampaignRowLayout(
    CampaignRowModel Model, DipRect Rect, float NumberX, float NumberTop, float TitleX, float TitleTop, float TitleLineHeight,
    string[] TitleLines, DipRect Stars, float StarsTextTop);

/// <summary>Layout puro da aba Campanha (SDD-24 RF-24.6): card da fase (padding 14, gap 8) e 10 linhas (padding 10 12, min 44).</summary>
public sealed class CampaignLayout
{
    public const float StarPitch = 11f;          // largura de cada estrela desenhada como SKPath (DP-20.2)
    public const float RowMinHeight = 44f, NumberWidth = 22f;
    public const float BarHeight = 8f;

    public CampaignModel Model { get; }
    public CampaignCardLayout Card { get; }
    public IReadOnlyList<CampaignRowLayout> Rows { get; }
    public float ContentHeight { get; }

    private CampaignLayout(CampaignModel m, CampaignCardLayout card, IReadOnlyList<CampaignRowLayout> rows, float h)
    {
        Model = m; Card = card; Rows = rows; ContentHeight = h;
    }

    public static CampaignLayout Compute(CampaignModel model, IPanelTextMetrics tm)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(tm);
        float x = ArsenalLayout.PadX, w = ArsenalLayout.ContentWidth;
        float y = ArsenalLayout.PadY;

        float inX = x + 1f + 14f, inW = w - 2f - 28f;
        float cy = y + 1f + 14f;
        float labelH = tm.LineHeight(PanelFont.JetBrainsMono, 10f);
        float labelTop = cy;
        cy += labelH + 8f;
        float nameLh = tm.LineHeight(PanelFont.Silkscreen, 15f);
        var nameLines = PanelTextWrap.Wrap(tm, PanelFont.Silkscreen, 15f, model.StageTitle, inW);
        float nameTop = cy;
        cy += nameLines.Length * nameLh + 8f;
        var bar = new DipRect(inX, cy, inW, BarHeight);
        cy += BarHeight + 8f;
        float footLh = tm.LineHeight(PanelFont.JetBrainsMono, 11f);
        float waveW = tm.Width(PanelFont.JetBrainsMono, 11f, model.WaveText);
        var bossLines = PanelTextWrap.Wrap(tm, PanelFont.JetBrainsMono, 11f, model.BossText, Math.Max(0f, inW - waveW - 8f));
        float bossW = 0f;
        foreach (var l in bossLines) bossW = Math.Max(bossW, tm.Width(PanelFont.JetBrainsMono, 11f, l));
        float footH = Math.Max(footLh, bossLines.Length * footLh);
        float cardBottom = cy + footH + 14f + 1f;
        var card = new CampaignCardLayout(new DipRect(x, y, w, cardBottom - y), labelTop, labelH, nameTop, nameLh, nameLines, bar,
            cy, footLh, inX, inX + inW - bossW, bossLines);
        y = cardBottom;

        var rows = new List<CampaignRowLayout>(model.Rows.Count);
        float titleLh = tm.LineHeight(PanelFont.JetBrainsMono, 12f);
        float starLh = tm.LineHeight(PanelFont.Silkscreen, 11f);
        foreach (var row in model.Rows)
        {
            y += ArsenalLayout.Gap;
            float starsW = row.Locked ? tm.Width(PanelFont.Silkscreen, 11f, "BLOQ.") : 3f * StarPitch;
            float rowIn = w - 2f - 24f;
            float titleW = Math.Max(0f, rowIn - NumberWidth - 12f - 12f - starsW);
            var lines = PanelTextWrap.Wrap(tm, PanelFont.JetBrainsMono, 12f, row.Title, titleW);
            float contentH = Math.Max(lines.Length * titleLh, starLh);
            float h = Math.Max(RowMinHeight, contentH + 20f + 2f);
            float mid = y + h / 2f;
            float rx = x + 1f + 12f;
            float titleX = rx + NumberWidth + 12f;
            var stars = new DipRect(x + w - 1f - 12f - starsW, mid - starLh / 2f, starsW, starLh);
            rows.Add(new CampaignRowLayout(row, new DipRect(x, y, w, h), rx, mid - starLh / 2f, titleX, mid - lines.Length * titleLh / 2f, titleLh,
                lines, stars, mid - starLh / 2f));
            y += h;
        }
        return new CampaignLayout(model, card, rows, y + ArsenalLayout.PadY);
    }
}
