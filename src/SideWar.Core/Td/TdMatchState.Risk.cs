namespace SideWar.Core.Td;

public sealed partial class TdMatchState
{
    /// <summary>Id de conteúdo do tipo de inimigo (somente leitura; usado pelo catálogo de alertas).</summary>
    public string EnemyDefId(int defIndex) => _enemyStats[defIndex].Id;

    /// <summary>
    /// Limite conservador do dano possível no próximo passo, incluindo spawns, réplicas e divisões.
    /// Não altera estado, sorteio ou eventos. O limite pode incluir alternativas incompatíveis.
    /// </summary>
    public long ProjectNextStepLeakDamage()
    {
        if (Phase != MatchPhase.Onda) return 0;
        long damage = 0;
        int swarm = Array.FindIndex(_enemyStats, s => s.Id == "swarm");
        foreach (var e in _enemies)
            AddRisk(e.DefIndex, e.X, e.Speed, e.CoreDamage, e.Hp > 0, e.IsReplica, e.Triggers);

        // SpawnDue acontece antes do combate e pode alimentar os mesmos gatilhos.
        foreach (var wave in _active)
            for (int i = wave.NextSpawn; i < wave.Spawns.Length && wave.Spawns[i].DueTick <= Tick + 1 - wave.StartTick; i++)
            {
                var stats = _enemyStats[wave.Spawns[i].EnemyIndex];
                AddRisk(wave.Spawns[i].EnemyIndex, TdWorld.PathLength, wave.Spawns[i].Mutations == 0 ? stats.Speed : SpawnSpeed(wave.Spawns[i]), stats.CoreDamage, true, false, 0);
            }
        return damage;

        void AddDamage(int value)
        {
            if (value > 0) damage = Math.Min(long.MaxValue - value, damage) + value;
        }

        bool Reaches(double x, double speed) => x - Math.Max(0, speed) / TdWorld.TicksPerSecond <= LeakX;

        void AddRisk(int index, double x, double speed, int coreDamage, bool alive, bool replica, int triggers)
        {
            var stats = _enemyStats[index];
            if (alive && Reaches(x, speed)) AddDamage(coreDamage);
            // Mesmo um inimigo vivo pode morrer no combate anterior a ReapAndTriggers.
            if (stats.Replicator && !replica)
                for (int k = 0; k < 2; k++)
                    if (Reaches(Math.Min(TdWorld.PathLength, x + k * 2), stats.Speed)) AddDamage(stats.CoreDamage);
            if (alive && stats.SplitCount > 0 && swarm >= 0)
            {
                var child = _enemyStats[swarm];
                int remaining = ((triggers & 1) == 0 ? 1 : 0) + ((triggers & 2) == 0 ? 1 : 0);
                for (int k = 0; k < remaining * stats.SplitCount; k++)
                    if (Reaches(Math.Min(TdWorld.PathLength, x + (k % stats.SplitCount) * 2.0), child.Speed)) AddDamage(child.CoreDamage);
            }
        }
    }
}
