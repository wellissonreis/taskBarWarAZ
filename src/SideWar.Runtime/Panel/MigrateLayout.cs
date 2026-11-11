using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Panel;

/// <summary>Linha de região: retângulo clicável (altura de clique 44 nas selecionáveis, ADR-031) e caixa do texto.</summary>
public sealed record RegionRowLayout(DipRect Row, DipRect TextBox, string Text, bool Selectable);

/// <summary>
/// Layout puro da aba Migrar (SDD-24 RF-24.8, gap 12). Coordenadas como em <see cref="MissionsLayout"/>.
/// O botão MIGRAR fica encostado no fim do corpo quando o conteúdo é mais curto que o viewport
/// (<c>margin-top: auto</c>); senão vem após o conteúdo com gap 12.
/// </summary>
public sealed record MigrateLayout(
    float ContentHeight,
    DipRect Card,
    DipRect CardTitleBox,
    DipRect CardBodyBox,
    string[] CardBodyLines,
    DipRect[] Tiles,
    DipRect[] TileLabelBoxes,
    DipRect[] TileNumberBoxes,
    DipRect[] TileSubBoxes,
    DipRect RegionsLabelBox,
    RegionRowLayout[] Regions,
    DipRect Button,
    bool ButtonEnabled)
{
    public const float X = 18f, Width = 364f, Pad = 14f, Gap = 12f, ButtonHeight = 48f, ClickHeight = 44f;
    public const float CardBodyLineHeight = 11f * 1.5f;

    public static MigrateLayout Compute(MigrateTabModel m, IPanelTextMetrics tm, float viewport = PanelScroll.ViewportDip)
    {
        var y = PanelScroll.ViewportTopDip + Pad;
        var ix = X + 1 + Pad;
        var iw = Width - 2 - 2 * Pad;

        // 8.1 card roxo.
        var cardTop = y;
        var cy = cardTop + 1 + Pad;
        var titleBox = new DipRect(ix, cy, iw, tm.LineHeight(PanelFont.Silkscreen, 13));
        cy += titleBox.H + 8;
        var lines = TextFit.Wrap(m.CardBody, iw, PanelFont.JetBrainsMono, 11, tm);
        var bodyBox = new DipRect(ix, cy, iw, lines.Length * CardBodyLineHeight);
        cy += bodyBox.H;
        var card = new DipRect(X, cardTop, Width, cy + Pad + 1 - cardTop);
        y = card.Y + card.H + Gap;

        // 8.2 dois tiles (2 colunas, gap 8).
        var lh10 = tm.LineHeight(PanelFont.JetBrainsMono, 10);
        var numH = tm.LineHeight(PanelFont.Silkscreen, 18);
        var tileH = 2 + 24 + lh10 + 4 + numH + 4 + lh10;
        var tileW = (Width - 8) / 2f;
        var tiles = new DipRect[2];
        var labels = new DipRect[2];
        var numbers = new DipRect[2];
        var subs = new DipRect[2];
        for (var i = 0; i < 2; i++)
        {
            var tx = X + i * (tileW + 8);
            tiles[i] = new DipRect(tx, y, tileW, tileH);
            var tix = tx + 1 + 12;
            var tiw = tileW - 2 - 24;
            var ty = y + 1 + 12;
            labels[i] = new DipRect(tix, ty, tiw, lh10);
            numbers[i] = new DipRect(tix, ty + lh10 + 4, tiw, numH);
            subs[i] = new DipRect(tix, ty + lh10 + 4 + numH + 4, tiw, lh10);
        }
        y += tileH + Gap;

        // 8.3 regiões (rótulo, gap 6, linhas com gap 6).
        var lh11 = tm.LineHeight(PanelFont.JetBrainsMono, 11);
        var regionsLabel = new DipRect(X, y, Width, lh10);
        var ry = y + lh10 + 6;
        var rows = new RegionRowLayout[m.Regions.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var r = m.Regions[i];
            var h = r.Selectable ? Math.Max(ClickHeight, lh11) : lh11;
            rows[i] = new RegionRowLayout(new DipRect(X, ry, Width, h),
                new DipRect(X, ry + (h - lh11) / 2f, Width, lh11),
                TextFit.Ellipsize(r.Text, Width, PanelFont.JetBrainsMono, 11, tm), r.Selectable);
            ry += h + 6;
        }
        var regionsEnd = rows.Length > 0 ? ry - 6 : regionsLabel.Y + lh10;
        y = regionsEnd + Gap;

        // 8.4 botão MIGRAR (margin-top: auto).
        var naturalContent = y + ButtonHeight + Pad - PanelScroll.ViewportTopDip;
        var content = Math.Max(viewport, naturalContent);
        var buttonY = PanelScroll.ViewportTopDip + content - Pad - ButtonHeight;
        var button = new DipRect(X, buttonY, Width, ButtonHeight);
        return new MigrateLayout(content, card, titleBox, bodyBox, lines, tiles, labels, numbers, subs,
            regionsLabel, rows, button, m.Button != MigrateButtonState.Unavailable);
    }

    /// <summary>Alvo recortado pelo viewport (RF-24.11). Indisponível: o botão não responde (DP-24.9).</summary>
    public TabBodyHit Hit(float x, float y, float offset, float viewport = PanelScroll.ViewportDip)
    {
        if (y < PanelScroll.ViewportTopDip || y >= PanelScroll.ViewportTopDip + viewport) return TabBodyHit.None;
        var cy = y + offset;
        if (ButtonEnabled && MissionsLayout.Contains(Button, x, cy)) return new(TabBodyHitKind.Migrate);
        for (var i = 0; i < Regions.Length; i++)
            if (Regions[i].Selectable && MissionsLayout.Contains(Regions[i].Row, x, cy)) return new(TabBodyHitKind.RegionRow, i);
        return TabBodyHit.None;
    }
}
