namespace SideWar.Core.Td;

public static class TdLegacy
{
    public const int WavesPerFragment = 5;
    public const int MaxFragments = 6;
    public const int FragmentsPerRune = 6;
    public const int MaxStartingRunes = 3;
    public const int MaxTotal = 99_999;
    public static int Fragments(int waves) => Math.Min(MaxFragments, Math.Max(0, waves) / WavesPerFragment);
    public static int StartingRunes(int total) => Math.Min(MaxStartingRunes, Math.Max(0, total) / FragmentsPerRune);
}
