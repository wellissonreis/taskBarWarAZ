namespace SideWar.Runtime.Td;

/// <summary>
/// Velocidades do jogo: o padrão é 1x/2x/3x; "velocidades extras" (desligadas por padrão) acrescentam as escolhidas pelo usuário.
/// <see cref="Max"/> = "Máx": roda o que couber no teto de tempo por frame do host.
/// </summary>
public static class TdSpeed
{
    public const int Max = 100;
    public static readonly int[] Defaults = [1, 2, 3];
    /// <summary>Extras na ordem do seletor; o índice é o bit da máscara.</summary>
    public static readonly int[] Extras = [4, 5, 8, 10, 16, 20, 50, Max];
    /// <summary>Extras já marcadas quando o usuário liga "Velocidades extras": 5x, 10x e Máx.</summary>
    public const int DefaultMask = 0b1000_1010;

    public static string Label(int speed) => speed >= Max ? "Máx" : $"{speed}x";

    public static int[] Allowed(bool extras, int mask)
    {
        if (!extras) return Defaults;
        var list = new List<int>(Defaults);
        for (int i = 0; i < Extras.Length; i++) if ((mask >> i & 1) != 0) list.Add(Extras[i]);
        return [.. list];
    }

    /// <summary>Velocidade permitida mais próxima sem passar da pedida (ou 1).</summary>
    public static int Resolve(int speed, int[] allowed)
    {
        int best = 1;
        foreach (int v in allowed) if (v <= speed && v > best) best = v;
        return best;
    }

    /// <summary>Próxima velocidade da lista (cicla só as habilitadas).</summary>
    public static int Next(int current, int[] allowed)
    {
        int i = Array.IndexOf(allowed, current);
        return allowed[(i + 1) % allowed.Length];
    }
}
