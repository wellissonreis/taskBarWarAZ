using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Widget;

/// <summary>
/// Layout interno do widget em DIP, origem no canto da janela (SDD-23 RF-23.2/RF-23.3, ADR-034). Largura natural. Puro. Os retângulos dos
/// botões servem ao hit-test (T5.2).
/// </summary>
public readonly record struct WidgetLayout(
    float WidthDip,
    float HeightDip,
    DipRect Credits,
    DipRect Wave,
    DipRect Seal,
    DipRect Chip,
    bool HasChip,
    DipRect Nav0,
    DipRect Nav1,
    DipRect Nav2,
    DipRect Nav3)
{
    public const float BorderDip = 1, PaddingLeft = 12, PaddingRight = 6, CreditsWidth = 70, WaveWidth = 52,
        ColumnGap = 12, ButtonSize = 44, ButtonGap = 2, HeightDipFixed = 48;
    public const float NavWidth = 4 * ButtonSize + 3 * ButtonGap;

    /// <summary>Soma das larguras fixas sem selo nem chip ×3: 1+12+70+12+52+12+182+6+1 = 348 (RF-23.2).</summary>
    public const float FixedWidth = BorderDip + PaddingLeft + CreditsWidth + ColumnGap + WaveWidth + ColumnGap + NavWidth + PaddingRight + BorderDip;

    public DipRect Button(int index) => index switch
    {
        0 => Nav0, 1 => Nav1, 2 => Nav2, 3 => Nav3,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>Índice do botão em (x, y) DIP, ou -1.</summary>
    public int HitButton(float x, float y)
    {
        for (int i = 0; i < WidgetModel.NavButtonCount; i++)
        {
            DipRect r = Button(i);
            if (x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H) return i;
        }
        return -1;
    }

    /// <summary>Largura natural (DIP): fixa + selo + [gap 12 + chip ×3] (RF-23.2). Sem espaço flexível.</summary>
    public static float NaturalWidth(float sealWidthDip, float chipWidthDip) =>
        FixedWidth + ColumnGap + sealWidthDip + (chipWidthDip > 0 ? ColumnGap + chipWidthDip : 0);

    /// <param name="sealWidthDip">Largura natural do selo (texto + 6+6 + 1+1), medida.</param>
    /// <param name="sealHeightDip">Altura do selo (linha + 4+4 + 1+1).</param>
    /// <param name="chipWidthDip">Largura natural do chip ×3, ou 0 sem chip. O chip fica depois do selo (ADR-034).</param>
    public static WidgetLayout Compute(float heightDip, float sealWidthDip, float sealHeightDip, float chipWidthDip)
    {
        float cy = heightDip / 2f;
        float width = NaturalWidth(sealWidthDip, chipWidthDip);
        float x = BorderDip + PaddingLeft;
        var credits = new DipRect(x, 0, CreditsWidth, heightDip);
        x += CreditsWidth + ColumnGap;
        var wave = new DipRect(x, 0, WaveWidth, heightDip);
        x += WaveWidth + ColumnGap;
        var seal = new DipRect(x, cy - sealHeightDip / 2f, sealWidthDip, sealHeightDip);
        x += sealWidthDip + ColumnGap;
        var chip = new DipRect(x, cy - sealHeightDip / 2f, chipWidthDip, sealHeightDip);
        float navX = width - BorderDip - PaddingRight - NavWidth;
        float by = cy - ButtonSize / 2f;
        DipRect Btn(int i) => new(navX + i * (ButtonSize + ButtonGap), by, ButtonSize, ButtonSize);
        return new(width, heightDip, credits, wave, seal, chip, chipWidthDip > 0, Btn(0), Btn(1), Btn(2), Btn(3));
    }
}
