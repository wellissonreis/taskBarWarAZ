namespace SideWar.Core.Td;

/// <summary>Projeções conservadoras somente de leitura; não consomem RNG nem alteram a partida.</summary>
public static class TdRiskAnalyzer
{
    /// <summary>
    /// Soma o dano dos inimigos que poderiam vazar no próximo passo à velocidade integral.
    /// Ignora lentidão e ataques: um falso positivo é preferível a perder sem confirmação.
    /// </summary>
    public static long NextStepLeakDamage(IReadOnlyList<TdEnemy> enemies, double leakX)
    {
        long damage = 0;
        for (int i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy.Hp <= 0 || enemy.CoreDamage <= 0 || enemy.X - Math.Max(0, enemy.Speed) / TdWorld.TicksPerSecond > leakX) continue;
            damage = Math.Min(long.MaxValue - enemy.CoreDamage, damage) + enemy.CoreDamage;
        }
        return damage;
    }

    /// <summary>O próximo passo pode zerar o núcleo, considerando vazamentos simultâneos.</summary>
    public static bool FatalNextStep(TdMatchState match) =>
        match.Phase == MatchPhase.Onda && match.Integrity > 0 && NextStepLeakDamage(match.Enemies, match.LeakX) >= match.Integrity;

    /// <summary>Menor tempo projetado até a borda à velocidade integral; infinito se não houver ameaça móvel.</summary>
    public static double SecondsToLeak(IReadOnlyList<TdEnemy> enemies, double leakX)
    {
        double seconds = double.PositiveInfinity;
        for (int i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            if (enemy.Hp <= 0 || enemy.Speed <= 0 || enemy.CoreDamage <= 0) continue;
            seconds = Math.Min(seconds, Math.Max(0, enemy.X - leakX) / enemy.Speed);
        }
        return seconds;
    }
}
