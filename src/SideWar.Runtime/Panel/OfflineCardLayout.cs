using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Panel;

/// <summary>Alvos do card de retorno (SDD-24 RF-24.11): <c>OfflineCollectX2</c>, <c>OfflineCollect</c>, <c>Background</c>.</summary>
public enum OfflineCardHitKind { None, Background, CollectX2, Collect }

/// <summary>Linha do parágrafo de abertura; <c>HighlightStart/Len</c> marcam o tempo em <c>txt-principal</c> (−1 = nenhum).</summary>
public readonly record struct IntroLine(string Text, int HighlightStart, int HighlightLen);

/// <summary>Geometria de um tile 2 × 2: caixa e as três linhas de texto (padding 12, gap 4, borda 1).</summary>
public sealed record OfflineTileLayout(DipRect Box, DipRect LabelBox, DipRect ValueBox, DipRect SubBox, string Sub);

/// <summary>
/// Layout puro do card "Bem-vindo de volta" (SDD-24 RF-24.10): caixa 472 × 612, borda 2, padding 22, coluna com gap 16,
/// botões ancorados na base (<c>margin-top: auto</c>). Coordenadas em DIP da caixa visível (origem no canto superior
/// esquerdo; a janela tem +6 DIP de sombra). Sem alocação por clique: o hit-test só compara retângulos.
/// </summary>
public sealed record OfflineCardLayout(
    DipRect OverlineBox, DipRect TitleBox, DipRect IntroBox, IntroLine[] IntroLines,
    DipRect TimeRowBox, DipRect BarTrack, DipRect BarFill,
    OfflineTileLayout[] Tiles,
    DipRect? WarningBox, DipRect WarningIcon, DipRect WarningTextBox, string[] WarningLines,
    DipRect CollectX2, DipRect CollectX2LabelBox, DipRect CollectX2HintBox, DipRect Collect, DipRect CollectLabelBox)
{
    public const float Width = 472f, Height = 612f, Border = 2f, Pad = 22f, Gap = 16f;
    public const float BarHeight = 8f, TileGap = 8f, TilePad = 12f, TileInnerGap = 4f;
    public const float WarningPad = 12f, WarningGap = 12f, IconSize = 20f, WarningLineHeight = 11f * 1.45f;
    public const float X2Height = 52f, CollectHeight = 44f, ButtonGap = 8f;

    public static OfflineCardLayout Compute(OfflineCardModel m, IPanelTextMetrics tm)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(tm);
        var x = Border + Pad;
        var w = Width - 2 * (Border + Pad);
        var y = x;
        const PanelFont Mono = PanelFont.JetBrainsMono;
        const PanelFont Silk = PanelFont.Silkscreen;

        // 1. Cabeçalho (gap 6)
        var lh10 = tm.LineHeight(Mono, 10);
        var overline = new DipRect(x, y, w, lh10);
        y += lh10 + 6;
        var lh24 = tm.LineHeight(Silk, 24);
        var title = new DipRect(x, y, w, lh24);
        y += lh24 + 6;
        var lh12 = tm.LineHeight(Mono, 12);
        var wrapped = TextFit.Wrap(m.Intro, w, Mono, 12, tm);
        var intro = new IntroLine[wrapped.Length];
        var hs = m.IntroPrefix.Length;
        var he = hs + m.TimeText.Length;
        var pos = 0;
        for (var i = 0; i < wrapped.Length; i++)
        {
            var at = m.Intro.IndexOf(wrapped[i], pos, StringComparison.Ordinal);
            if (at < 0) at = pos;
            var s = Math.Max(hs, at);
            var e = Math.Min(he, at + wrapped[i].Length);
            intro[i] = e > s ? new(wrapped[i], s - at, e - s) : new(wrapped[i], -1, 0);
            pos = at + wrapped[i].Length;
        }
        var introBox = new DipRect(x, y, w, wrapped.Length * lh12);
        y += introBox.H + Gap;

        // 2. Tempo acumulado (gap 6)
        var timeRow = new DipRect(x, y, w, lh10);
        y += lh10 + 6;
        var track = new DipRect(x, y, w, BarHeight);
        var fill = new DipRect(x, y, MathF.Round(w * m.TimeFraction), BarHeight);
        y += BarHeight + Gap;

        // 3. Grade 2 × 2 (gap 8)
        var tw = (w - TileGap) / 2f;
        var lh20 = tm.LineHeight(Silk, 20);
        var th = 2 + 2 * TilePad + lh10 + TileInnerGap + lh20 + TileInnerGap + lh10;
        var tiles = new OfflineTileLayout[m.Tiles.Count];
        for (var i = 0; i < tiles.Length; i++)
        {
            var tx = x + (i % 2) * (tw + TileGap);
            var ty = y + (i / 2) * (th + TileGap);
            var ix = tx + 1 + TilePad;
            var iw = tw - 2 - 2 * TilePad;
            var iy = ty + 1 + TilePad;
            var sub = TextFit.Ellipsize(m.Tiles[i].Sub, iw, Mono, 10, tm);
            tiles[i] = new(new DipRect(tx, ty, tw, th), new DipRect(ix, iy, iw, lh10),
                new DipRect(ix, iy + lh10 + TileInnerGap, iw, lh20),
                new DipRect(ix, iy + lh10 + TileInnerGap + lh20 + TileInnerGap, iw, lh10), sub);
        }
        var rows = (tiles.Length + 1) / 2;
        y += rows * th + Math.Max(0, rows - 1) * TileGap + Gap;

        // 4. Aviso de perdas (omitido sem perdas)
        DipRect? warning = null;
        var wIcon = default(DipRect);
        var wText = default(DipRect);
        var wLines = Array.Empty<string>();
        if (m.WarningText is not null)
        {
            var textW = w - 2 - 2 * WarningPad - IconSize - WarningGap;
            wLines = TextFit.Wrap(m.WarningText, textW, Mono, 11, tm);
            var inner = Math.Max(IconSize, wLines.Length * WarningLineHeight);
            var wh = 2 + 2 * WarningPad + inner;
            warning = new DipRect(x, y, w, wh);
            wIcon = new DipRect(x + 1 + WarningPad, y + 1 + WarningPad + (inner - IconSize) / 2f, IconSize, IconSize);
            wText = new DipRect(x + 1 + WarningPad + IconSize + WarningGap, y + 1 + WarningPad + (inner - wLines.Length * WarningLineHeight) / 2f,
                textW, wLines.Length * WarningLineHeight);
        }

        // 5. Botões na base: bottom = Height − Border − Pad
        var bottom = Height - Border - Pad;
        var x2 = new DipRect(x, bottom - CollectHeight - ButtonGap - X2Height, w, X2Height);
        var col = new DipRect(x, bottom - CollectHeight, w, CollectHeight);
        var lh13 = tm.LineHeight(Silk, 13);
        var blockH = lh13 + 2 + lh10;
        var x2Top = x2.Y + (X2Height - blockH) / 2f;
        var x2Label = new DipRect(x, x2Top, w, lh13);
        var x2Hint = new DipRect(x, x2Top + lh13 + 2, w, lh10);
        var lh11 = tm.LineHeight(Silk, 11);
        var colLabel = new DipRect(x, col.Y + (CollectHeight - lh11) / 2f, w, lh11);

        return new OfflineCardLayout(overline, title, introBox, intro, timeRow, track, fill, tiles, warning, wIcon, wText, wLines,
            x2, x2Label, x2Hint, col, colLabel);
    }

    /// <summary>Alvo na caixa visível. Os botões só respondem com <paramref name="buttonsEnabled"/> (card aberto, sem pergunta).</summary>
    public OfflineCardHitKind Hit(float x, float y, bool buttonsEnabled = true)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return OfflineCardHitKind.None;
        if (buttonsEnabled)
        {
            if (MissionsLayout.Contains(CollectX2, x, y)) return OfflineCardHitKind.CollectX2;
            if (MissionsLayout.Contains(Collect, x, y)) return OfflineCardHitKind.Collect;
        }
        return OfflineCardHitKind.Background;
    }

    /// <summary>Menor Y ocupado acima dos botões (para testar sobreposição no layout).</summary>
    public float ContentBottom => WarningBox is { } w ? w.Y + w.H : Tiles[^1].Box.Y + Tiles[^1].Box.H;
}
