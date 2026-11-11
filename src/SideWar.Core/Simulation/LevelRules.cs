namespace SideWar.Core.Simulation;

/// <summary>
/// Regra única de nível (SDD-27 RF-27.9): <c>enquanto xp ≥ nível × xpPorNível: xp −= nível × xpPorNível; nível += 1</c>.
/// Usada pela simulação (<see cref="SimState"/>) e pelo cálculo offline (SDD-25 RF-25.8).
/// </summary>
public static class LevelRules
{
    /// <summary>Um passo da regra: se <c>xp ≥ nível × xpPorNível</c> desconta e sobe 1 nível (true).</summary>
    public static bool TryLevelUp(ref int level, ref long xp, int xpPerLevel)
    {
        if (level >= int.MaxValue || xp < (long)level * xpPerLevel) return false;
        xp -= (long)level * xpPerLevel;
        level++;
        return true;
    }

    /// <summary>Aplica a regra até estabilizar; devolve quantos níveis subiram. <paramref name="level"/> e <paramref name="xp"/> são atualizados.</summary>
    public static int Advance(ref int level, ref long xp, int xpPerLevel)
    {
        int gained = 0;
        while (TryLevelUp(ref level, ref xp, xpPerLevel)) gained++;
        return gained;
    }
}
