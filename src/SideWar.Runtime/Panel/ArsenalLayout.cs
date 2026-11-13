using SideWar.Runtime.Geometry;
using SideWar.Runtime.Localization;

namespace SideWar.Runtime.Panel;

/// <summary>Linha do Arsenal posicionada, em coordenadas de conteúdo (x a partir do corpo, y a partir do topo do viewport, antes da rolagem).</summary>
public sealed record ArsenalRowLayout(
    ArsenalRowModel Model, DipRect Rect, DipRect Slot, float TextX, float TextWidth,
    float NameTop, float AzureTop, float StatsTop, DipRect Button, ArsenalHit ButtonHit, ArsenalHit IconHit);

/// <summary>Card de auto-escala posicionado (RF-24.5 §5.1).</summary>
public sealed record ArsenalAutoCardLayout(
    DipRect Rect, float TitleTop, float TitleLineHeight, string[] DescriptionLines, float DescriptionTop, float DescriptionLineHeight,
    DipRect Toggle, DipRect Box, float ToggleTextX, string ToggleText);

/// <summary>Cartão de ajuda posicionado (RF-24.5 §5.1b): sem alvo de clique.</summary>
public sealed record ArsenalHelpCardLayout(DipRect Rect, string[] Lines, float TextTop, float LineHeight);

/// <summary>
/// Layout puro da aba Arsenal (SDD-24 RF-24.3/24.5; alturas de linha dependem das medidas de texto e são calculadas a cada mudança
/// de dados/escala, não por frame). Retângulos de hit-test saem daqui; o clique devolve só <see cref="ArsenalHit"/>.
/// </summary>
public sealed class ArsenalLayout
{
    public const float BodyLeft = 2f;            // x do corpo na caixa do painel
    public const float PadX = 16f, PadY = 14f, Gap = 10f;
    public const float ContentWidth = 364f;      // x 16..380 do corpo (RF-24.3)
    public const float TowerRowHeight = 74f;
    public const float SlotBox = 52f, SpriteBox = 44f;
    public const float ButtonMinWidth = 84f, ButtonHeight = 44f;
    public const float ToggleBoxSize = 20f, ToggleHeight = 44f;
    public const float LabelFontSize = 10f;

    public ArsenalModel Model { get; }
    public ArsenalAutoCardLayout AutoCard { get; }
    public ArsenalHelpCardLayout HelpCard { get; }
    public IReadOnlyList<ArsenalRowLayout> Rows { get; }

    /// <summary>Altura total do conteúdo rolável, com o padding 14 de cima e de baixo (use em <c>PanelViewState.SetContent</c>).</summary>
    public float ContentHeight { get; }

    private ArsenalLayout(ArsenalModel model, ArsenalAutoCardLayout card, ArsenalHelpCardLayout help, IReadOnlyList<ArsenalRowLayout> rows, float height)
    {
        Model = model; AutoCard = card; HelpCard = help; Rows = rows; ContentHeight = height;
    }

    public static ArsenalLayout Compute(ArsenalModel model, IPanelTextMetrics tm)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(tm);
        float y = PadY;

        // 5.1 card de auto-escala
        string toggleText = model.AutoScaleOn ? Strings.ArsenalToggleOn : Strings.ArsenalToggleOff;
        float toggleW = Math.Max(ToggleHeight, ToggleBoxSize + 8f + tm.Width(PanelFont.Silkscreen, 10f, toggleText));
        const float innerW = ContentWidth - 2f - 24f;
        float textW = innerW - 12f - toggleW;
        float titleLh = tm.LineHeight(PanelFont.Silkscreen, 11f);
        const float descLh = 11f * 1.45f;
        var lines = PanelTextWrap.Wrap(tm, PanelFont.JetBrainsMono, 11f, ArsenalModel.AutoScaleDescription, textW);
        float colH = titleLh + 4f + lines.Length * descLh;
        float inner = Math.Max(colH, ToggleHeight);
        float cardH = inner + 24f + 2f;
        float innerTop = y + 1f + 12f;
        float toggleX = PadX + 1f + 12f + innerW - toggleW;
        var toggle = new DipRect(toggleX, innerTop + (inner - ToggleHeight) / 2f, toggleW, ToggleHeight);
        var box = new DipRect(toggleX, toggle.Y + (ToggleHeight - ToggleBoxSize) / 2f, ToggleBoxSize, ToggleBoxSize);
        float colTop = innerTop + (inner - colH) / 2f;
        var card = new ArsenalAutoCardLayout(new DipRect(PadX, y, ContentWidth, cardH), colTop, titleLh, lines, colTop + titleLh + 4f, descLh,
            toggle, box, toggleX + ToggleBoxSize + 8f, toggleText);
        y += cardH;

        // 5.1b cartão de ajuda: padding 10 12, borda 1, mono 11, line-height 1,45
        y += Gap;
        const float helpLh = 11f * 1.45f;
        var helpLines = PanelTextWrap.Wrap(tm, PanelFont.JetBrainsMono, 11f, ArsenalModel.HelpText, ContentWidth - 2f - 24f);
        float helpH = helpLines.Length * helpLh + 20f + 2f;
        var help = new ArsenalHelpCardLayout(new DipRect(PadX, y, ContentWidth, helpH), helpLines, y + 1f + 10f, helpLh);
        y += helpH;

        var rows = new List<ArsenalRowLayout>(model.Rows.Count);
        foreach (var row in model.Rows)
        {
            y += Gap;
            rows.Add(LayoutRow(row, y, tm));
            y += TowerRowHeight;
        }
        return new ArsenalLayout(model, card, help, rows, y + PadY);
    }

    private static ArsenalRowLayout LayoutRow(ArsenalRowModel m, float y, IPanelTextMetrics tm)
    {
        float inX = PadX + 1f + 12f;
        var slot = new DipRect(inX, y + 1f + 10f, SlotBox, SlotBox);
        float textX = inX + SlotBox + 12f;
        float btnW = Math.Max(ButtonMinWidth,
            Math.Max(tm.Width(PanelFont.Silkscreen, 10f, m.ButtonLabel), tm.Width(PanelFont.Silkscreen, 9f, m.CostText)) + 12f);
        float btnRight = PadX + ContentWidth - 1f - 12f;
        var btn = new DipRect(btnRight - btnW, y + (TowerRowHeight - ButtonHeight) / 2f, btnW, ButtonHeight);
        float textW = Math.Max(0f, btn.X - 12f - textX);
        float h1 = tm.LineHeight(PanelFont.Silkscreen, 12f);
        float h2 = tm.LineHeight(PanelFont.JetBrainsMono, 10f);
        float total = h1 + 3f + h2 + 3f + h2;
        float top = y + (TowerRowHeight - total) / 2f;
        ArsenalHit hit = m.ButtonStyle == ArsenalButtonStyle.Locked ? ArsenalHit.None : m.Kind switch
        {
            ArsenalRowKind.Tower => ArsenalHit.Upgrade(m.TowerId),
            ArsenalRowKind.NoPosition => ArsenalHit.Reposition(m.TowerId, m.Type, Strings.ArsenalBuildHint(m.Name, null)),
            _ => ArsenalHit.None,
        };
        ArsenalHit iconHit = m.IconEnabled && m.Kind is ArsenalRowKind.Tower or ArsenalRowKind.Available ? ArsenalHit.Icon(m.Type, m.HintTitle) : ArsenalHit.None;
        return new ArsenalRowLayout(m, new DipRect(PadX, y, ContentWidth, TowerRowHeight), slot, textX, textW,
            top, top + h1 + 3f, top + h1 + 3f + h2 + 3f, btn, hit, iconHit);
    }

    /// <summary>Topo (conteúdo) da linha da torre de <paramref name="towerId"/>, para a rolagem automática de <c>OpenArsenal</c> (uma vez, ao abrir).</summary>
    public bool TryGetTowerRowTop(int towerId, out float top)
    {
        foreach (var r in Rows)
        {
            if (r.Model.Kind is ArsenalRowKind.Tower or ArsenalRowKind.NoPosition && r.Model.TowerId == towerId) { top = r.Rect.Y; return true; }
        }
        top = 0f;
        return false;
    }

    /// <summary>
    /// Hit-test do corpo. (<paramref name="panelX"/>, <paramref name="panelY"/>) em DIP relativos ao canto da caixa visível do painel
    /// (o mesmo sistema de <c>PanelChromeHit</c>). Recorta no viewport [55, 55+viewport): nada fora é clicável (RF-24.11).
    /// Sem alocação.
    /// </summary>
    public ArsenalHit Hit(float panelX, float panelY, float offset, float viewport)
    {
        float vy = PanelScroll.ViewportTopDip;
        if (panelY < vy || panelY >= vy + viewport) return ArsenalHit.None;
        float x = panelX - BodyLeft;
        float y = panelY - vy + offset;
        if (Contains(AutoCard.Toggle, x, y)) return ArsenalHit.AutoScale;
        foreach (var r in Rows)
        {
            if (y < r.Rect.Y) break;
            if (!r.ButtonHit.IsNone && Contains(r.Button, x, y)) return r.ButtonHit;
            if (!r.IconHit.IsNone && Contains(r.Slot, x, y)) return r.IconHit;
        }
        return ArsenalHit.None;
    }

    private static bool Contains(DipRect r, float x, float y) => x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H;
}
