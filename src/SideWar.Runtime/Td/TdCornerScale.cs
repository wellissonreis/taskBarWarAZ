namespace SideWar.Runtime.Td;

/// <summary>Conversão compartilhada entre pixels da janela e coordenadas da interface do canto.</summary>
public readonly record struct TdCornerScale(int Dpi, int Percent)
{
    public int EffectiveDpi => (int)MathF.Round(Dpi * (Percent / 100f));
    public float PixelsPerDip => EffectiveDpi / 96f;
    public float ToDip(float pixels) => pixels / PixelsPerDip;
    public int ToPixels(float dip) => (int)MathF.Round(dip * PixelsPerDip);
}
