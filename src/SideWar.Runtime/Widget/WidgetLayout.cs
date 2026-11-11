using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Widget;

/// <summary>
/// Layout interno do widget em DIP, origem no canto da janela (SDD-23 RF-23.3). Puro. Os retângulos dos
/// botões servem ao hit-test (T5.2).
/// </summary>
public readonly record struct WidgetLayout(
    float WidthDip,
    float HeightDip,
    DipRect Credits,
    DipRect Wave,
    DipRect Seal,
    DipRect Chip,
    bool ChipFits,
    DipRect Nav0,
    DipRect Nav1,
    DipRect Nav2,
    DipRect Nav3)
{
    public const float InsetDip = 8, CreditsWidth = 78, WaveWidth = 56, ColumnGap = 12, ButtonSize = 44, ButtonGap = 2, ChipGap = 4;
    public const float NavWidth = 4 * ButtonSize + 3 * ButtonGap;

    /// <summary>Soma das larguras fixas sem o selo: 8+78+12+56+12+12+182+8 = 368 (DP-23.1).</summary>
    public const float FixedWidth = InsetDip + CreditsWidth + ColumnGap + WaveWidth + ColumnGap + ColumnGap + NavWidth + InsetDip;

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

    /// <summary>Sobra (DIP) com o selo e sem chip; negativa = não cabe (CA-23.10).</summary>
    public static float Slack(float widthDip, float sealWidthDip) => widthDip - FixedWidth - sealWidthDip;

    /// <param name="sealWidthDip">Largura natural do selo (texto + 6+6 + 1+1), medida.</param>
    /// <param name="sealHeightDip">Altura do selo (linha + 4+4 + 1+1).</param>
    /// <param name="chipWidthDip">Largura natural do chip ×3, ou 0 sem chip.</param>
    public static WidgetLayout Compute(float widthDip, float heightDip, float sealWidthDip, float sealHeightDip, float chipWidthDip)
    {
        float cy = heightDip / 2f;
        float x = InsetDip;
        var credits = new DipRect(x, 0, CreditsWidth, heightDip);
        x += CreditsWidth + ColumnGap;
        var wave = new DipRect(x, 0, WaveWidth, heightDip);
        x += WaveWidth + ColumnGap;
        var seal = new DipRect(x, cy - sealHeightDip / 2f, sealWidthDip, sealHeightDip);
        float navX = widthDip - InsetDip - NavWidth;
        float chipX = x + sealWidthDip + ChipGap;
        bool fits = chipWidthDip > 0 && chipX + chipWidthDip + ColumnGap <= navX;
        var chip = new DipRect(chipX, cy - sealHeightDip / 2f, chipWidthDip, sealHeightDip);
        float by = cy - ButtonSize / 2f;
        DipRect Btn(int i) => new(navX + i * (ButtonSize + ButtonGap), by, ButtonSize, ButtonSize);
        return new(widthDip, heightDip, credits, wave, seal, chip, fits, Btn(0), Btn(1), Btn(2), Btn(3));
    }
}
