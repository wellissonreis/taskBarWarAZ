using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Panel;

/// <summary>Retângulos de uma linha de missão (SDD-24 RF-24.7 item 7.3), em DIP da caixa do painel com offset 0.</summary>
public sealed record MissionRowLayout(
    DipRect Row, DipRect TitleBox, string Title, DipRect BarTrack, float BarFill, DipRect ProgressBox, DipRect? Collect);

/// <summary>
/// Layout puro da aba Missões (SDD-24 RF-24.7). Coordenadas em DIP da caixa visível do painel (400 × 548) com
/// offset de rolagem 0: o corpo começa em y = 55, o conteúdo em x = 18 (largura 364). O hit-test aplica o offset.
/// </summary>
public sealed record MissionsLayout(
    float ContentHeight,
    DipRect PresenceCard,
    DipRect TitleBox,
    DipRect MultiplierBox,
    DipRect ParagraphBox,
    string[] ParagraphLines,
    DipRect[] Bands,
    DipRect SectionBox,
    MissionRowLayout[] Rows)
{
    public const float X = 18f, Width = 364f, Pad = 14f, Gap = 10f, BarHeight = 5f;
    public const float ButtonMinWidth = 72f, ButtonHeight = 44f;

    public static MissionsLayout Compute(MissionsTabModel m, IPanelTextMetrics tm)
    {
        var y = PanelScroll.ViewportTopDip + Pad;

        // 7.1 card de presença (borda 1, padding 14, gap 8).
        var ix = X + 1 + Pad;
        var iw = Width - 2 - 2 * Pad;
        var cardTop = y;
        var cy = cardTop + 1 + Pad;
        var rowH = Math.Max(tm.LineHeight(PanelFont.Silkscreen, 16), tm.LineHeight(PanelFont.Silkscreen, 12));
        var baseline = cy + Math.Max(tm.Ascent(PanelFont.Silkscreen, 16), tm.Ascent(PanelFont.Silkscreen, 12));
        var titleBox = new DipRect(ix, baseline - tm.Ascent(PanelFont.Silkscreen, 12), iw, tm.LineHeight(PanelFont.Silkscreen, 12));
        var multBox = new DipRect(ix, baseline - tm.Ascent(PanelFont.Silkscreen, 16), iw, tm.LineHeight(PanelFont.Silkscreen, 16));
        cy += rowH + 8;
        var lines = TextFit.Wrap(m.PresenceText, iw, PanelFont.JetBrainsMono, 11, tm);
        var lh11 = tm.LineHeight(PanelFont.JetBrainsMono, 11);
        var paraBox = new DipRect(ix, cy, iw, lines.Length * lh11);
        cy += paraBox.H + 8;
        var lh10 = tm.LineHeight(PanelFont.JetBrainsMono, 10);
        var bandH = 12 + 2 * lh10;
        var bandW = (iw - 2 * 6) / 3f;
        var bands = new DipRect[3];
        for (var i = 0; i < 3; i++) bands[i] = new DipRect(ix + i * (bandW + 6), cy, bandW, bandH);
        cy += bandH;
        var card = new DipRect(X, cardTop, Width, cy + Pad + 1 - cardTop);
        y = card.Y + card.H + Gap;

        // 7.2 rótulo de seção (margem superior 4 + gap).
        y += 4;
        var section = new DipRect(X, y, Width, lh10);
        y += lh10 + Gap;

        // 7.3 linhas de missão.
        var rows = new MissionRowLayout[m.Missions.Count];
        var lh12 = tm.LineHeight(PanelFont.JetBrainsMono, 12);
        var buttonW = Math.Max(ButtonMinWidth, tm.Width(PanelFont.Silkscreen, 10, "COLETAR") + 12);
        for (var i = 0; i < rows.Length; i++)
        {
            var mission = m.Missions[i];
            var hasButton = mission.CanCollect;
            var colH = lh12 + 6 + BarHeight + 6 + lh10;
            var inner = Math.Max(colH, hasButton ? ButtonHeight : 0f);
            var rowRect = new DipRect(X, y, Width, 2 + 20 + inner);
            var colX = X + 1 + 12;
            var colW = Width - 2 - 24 - (hasButton ? buttonW + 12 : 0f);
            var colTop = y + 1 + 10 + (inner - colH) / 2f;
            var track = new DipRect(colX, colTop + lh12 + 6, colW, BarHeight);
            var fill = colW * Math.Clamp(mission.Progress, 0f, 1f);
            DipRect? collect = hasButton
                ? new DipRect(X + Width - 1 - 12 - buttonW, y + 1 + 10 + (inner - ButtonHeight) / 2f, buttonW, ButtonHeight)
                : null;
            rows[i] = new MissionRowLayout(rowRect,
                new DipRect(colX, colTop, colW, lh12),
                TextFit.Ellipsize(mission.Title, colW, PanelFont.JetBrainsMono, 12, tm),
                track, fill,
                new DipRect(colX, track.Y + BarHeight + 6, colW, lh10), collect);
            y += rowRect.H + Gap;
        }
        var end = rows.Length > 0 ? rows[^1].Row.Y + rows[^1].Row.H : section.Y + section.H;
        return new MissionsLayout(end + Pad - PanelScroll.ViewportTopDip, card, titleBox, multBox, paraBox, lines, bands, section, rows);
    }

    /// <summary>
    /// Alvo em (<paramref name="x"/>, <paramref name="y"/>) da caixa do painel com o offset de rolagem; recortado pelo
    /// viewport (RF-24.3, RF-24.11): fora de y ∈ [55, 55 + viewport) nada responde.
    /// </summary>
    public TabBodyHit Hit(float x, float y, float offset, float viewport = PanelScroll.ViewportDip)
    {
        if (y < PanelScroll.ViewportTopDip || y >= PanelScroll.ViewportTopDip + viewport) return TabBodyHit.None;
        var cy = y + offset;
        for (var i = 0; i < Rows.Length; i++)
            if (Rows[i].Collect is { } b && Contains(b, x, cy)) return new(TabBodyHitKind.MissionCollect, i);
        return TabBodyHit.None;
    }

    internal static bool Contains(DipRect r, float x, float y) => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H;
}
