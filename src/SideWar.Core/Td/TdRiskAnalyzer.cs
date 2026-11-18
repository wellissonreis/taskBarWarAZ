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
        match.Phase == MatchPhase.Onda && match.Integrity > 0 && match.ProjectNextStepLeakDamage() >= match.Integrity;

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

    /// <summary>Sem torre de defesa posicionada, o gatilho é um inimigo a esta distância lógica (20% do caminho) da borda do portal.</summary>
    public const double SafetyDistance = 240.0;

    /// <summary>
    /// Linha de defesa: X lógico da torre posicionada, ativa (não desativada) e que ataca/controla/bloqueia (alvos diferentes de "none")
    /// mais próxima do portal (menor X). Sem torre assim, a linha é <c>LeakX + SafetyDistance</c>. Venda ou desativação mudam a referência.
    /// </summary>
    public static double DefenseLineX(TdMatchState match)
    {
        double line = double.PositiveInfinity;
        foreach (var t in match.Towers)
            if (t.Placed && t.DisabledTicks <= 0 && match.Content.Towers[t.DefIndex].Targets != "none") line = Math.Min(line, t.X);
        return double.IsPositiveInfinity(line) ? match.LeakX + SafetyDistance : line;
    }

    /// <summary>Ids (em ordem de lista) dos inimigos vivos, voadores inclusos, que já passaram da linha de defesa. Só leitura.</summary>
    public static IReadOnlyList<int> PastDefenseLine(TdMatchState match)
    {
        var ids = new List<int>();
        if (match.Phase != MatchPhase.Onda) return ids;
        double line = DefenseLineX(match);
        foreach (var e in match.Enemies)
            if (e.Hp > 0 && e.X < line) ids.Add(e.Id);
        return ids;
    }
}
