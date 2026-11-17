using SideWar.Core.Content.Td;

namespace SideWar.Core.Td;

public sealed partial class TdMatchState
{
    /// <summary>
    /// Avança um passo. Em Preparo o mundo não anda (nenhum inimigo, nenhuma recarga): só corre a contagem de auto-início, se
    /// ligada (RF-28.3). Em Onda roda um tick de 30 Hz: spawns, torres, movimento/vazamento, conclusão de ondas.
    /// Vitória e Derrota são terminais (o auto-início nunca reinicia a fase).
    /// </summary>
    public void Step()
    {
        if (Paused) return; // pausa: nada avança (inimigos, spawns, recargas, contagem, ondas, tempo)
        Frame++;
        if (Phase == MatchPhase.Preparo)
        {
            if (_countdownActive && --_countdownTicks <= 0) BeginWave(NextWaveNumber);
            return;
        }
        if (Phase != MatchPhase.Onda) return;
        _inTick = true;
        try
        {
            Tick++;
            TickAbilities();
            SpawnDue();
            TickEnemyStatus();
            FireTowers();
            TickNodesAndZones();
            ReapAndTriggers();
            MoveEnemies();
            if (Phase == MatchPhase.Onda) CompleteWaves();
        }
        finally
        {
            _inTick = false;
        }
    }

    /// <summary>Multiplicador de HP por volta completa depois da fase 6 (corrida infinita, TD.28a).</summary>
    public const double CycleHpGrowth = 1.25;

    private void SpawnDue()
    {
        var rules = _content.Stages.Rules;
        foreach (var w in _active)
        {
            while (!w.SpawnsDone && w.Spawns[w.NextSpawn].DueTick <= Tick - w.StartTick)
            {
                if (_enemies.Count >= rules.MaxEnemiesOnField) return; // teto de 44 em campo (SDD-12): o spawn espera
                var sp = w.Spawns[w.NextSpawn++];
                _enemies.Add(MakeEnemy(sp.EnemyIndex, w.Number, TdWorld.PathLength));
                w.Alive++;
            }
        }
    }

    /// <summary><c>HP(w) = hpBase × growth^(w−1) × (1 + step × (fase−1))</c>; potência por multiplicação iterativa (ADR-026). Chefes não escalam.</summary>
    private double ScaledHp(double baseHp, int wave)
    {
        var r = _content.Stages.Rules;
        double hp = baseHp;
        for (int i = 1; i < wave; i++) hp *= r.HpWaveGrowth;
        hp *= 1.0 + r.HpStageStep * (_stageNumber - 1);
        for (int c = Cycle; c > 0; c--) hp *= CycleHpGrowth; // infinito: cada volta completa pelas fases fica 25% mais dura (multiplicação iterativa, ADR-026)
        return hp;
    }

    private TdActiveWave WaveOf(int number) => _active.First(w => w.Number == number);

    private void MoveEnemies()
    {
        double leakX = LeakX;
        double firewall = IsAbilityActive("firewall") ? FirewallSlow() : 0.0;
        for (int i = 0; i < _enemies.Count;)
        {
            var e = _enemies[i];
            var st = _enemyStats[e.DefIndex];
            double mult = 1.0;
            if (e.ParalyzeTicks > 0 || _held.Contains(e.Id)) mult = 0.0;
            else
            {
                if (e.SlowTicks > 0) mult = Math.Min(mult, e.SlowMult);
                if (firewall > 0) mult = Math.Min(mult, 1.0 - (st.IsBoss ? FirewallBossSlow() : firewall));
                mult = Math.Max(mult, 1.0 - st.SlowCap); // teto de lentidão (chefes 20%, Zero-day 0%)
            }
            e.X -= e.Speed * mult / TdWorld.TicksPerSecond;
            if (e.X > leakX)
            {
                if (st.IsBoss && st.InvulnTicks > 0) e = BossPathTriggers(e, st);
                _enemies[i++] = e;
                continue;
            }
            _enemies.RemoveAt(i);
            WaveOf(e.Wave).Alive--;
            Integrity = Math.Max(0, Integrity - e.CoreDamage);
            _hasLeaked = true;
            WaveOf(e.Wave).Leaked = true;
            RecordLeak(e);
            Emit(TdEventType.EnemyLeaked, e.Id, e.CoreDamage);
            if (Integrity == 0)
            {
                Phase = MatchPhase.Derrota;
                Stars = 0;
                Emit(TdEventType.Defeat, WaveNumber, IntegrityLost);
                return;
            }
        }
    }

    /// <summary>Zero-day: invulnerável por <c>invulnerableSeconds</c> ao entrar em cada nova metade do caminho (frações percorridas 0,5 e 0,75).</summary>
    private static TdEnemy BossPathTriggers(TdEnemy e, TdEnemyStats st)
    {
        double travelled = 1.0 - e.X / TdWorld.PathLength;
        if ((e.Triggers & 4) == 0 && st.Inv1 > 0 && travelled >= st.Inv1) { e.Triggers |= 4; e.InvulnTicks = st.InvulnTicks; }
        if ((e.Triggers & 8) == 0 && st.Inv2 > 0 && travelled >= st.Inv2) { e.Triggers |= 8; e.InvulnTicks = st.InvulnTicks; }
        return e;
    }

    private void CompleteWaves()
    {
        for (int i = 0; i < _active.Count;)
        {
            var w = _active[i];
            if (!w.SpawnsDone || w.Alive > 0)
            {
                i++;
                continue;
            }
            _active.RemoveAt(i);
            long bonus = WaveBonusFor(w.Number);
            if (!w.Leaked) bonus += _content.Runes.CreditSources.NoLeakBase + (long)_content.Runes.CreditSources.NoLeakPerWave * w.Number;
            bonus += (long)Rune("waveCredits"); // runas de Créditos (TD.29a)
            Credits = Simulation.Sat.Add(Credits, bonus);
            GainFragments(_content.Stages.Rules.Fragments.PerWave);
            Emit(TdEventType.WaveCompleted, w.Number, bonus);
        }
        SettlePhase();
    }
}
