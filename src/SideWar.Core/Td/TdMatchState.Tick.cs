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
            SpawnDue();
            FireTowers();
            MoveEnemies();
            if (Phase == MatchPhase.Onda) CompleteWaves();
        }
        finally
        {
            _inTick = false;
        }
    }

    private void SpawnDue()
    {
        var rules = _content.Stages.Rules;
        foreach (var w in _active)
        {
            while (!w.SpawnsDone && w.Spawns[w.NextSpawn].DueTick <= Tick - w.StartTick)
            {
                if (_enemies.Count >= rules.MaxEnemiesOnField) return; // teto de 44 em campo (SDD-12): o spawn espera
                var sp = w.Spawns[w.NextSpawn++];
                var st = _enemyStats[sp.EnemyIndex];
                double hp = st.Hp;
                if (!st.IsBoss) hp = ScaledHp(st.Hp, w.Number);
                _enemies.Add(new TdEnemy
                {
                    Id = _nextEnemyId++, Wave = w.Number, DefIndex = sp.EnemyIndex, IsBoss = st.IsBoss,
                    X = TdWorld.PathLength, Speed = st.Speed, Hp = hp, MaxHp = hp, Armor = st.Armor,
                    Gold = st.Gold, CoreDamage = st.CoreDamage, Flying = st.Flying,
                });
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
        return hp * (1.0 + r.HpStageStep * (_stage.Id - 1));
    }

    /// <summary>Gancho TD.3: ordem do dano de RF-28.5 (ramos, foco, vulnerável, habilidades, perfuração). Mínimo: dano × nível − armadura.</summary>
    private static double ComputeDamage(in TdTower tower, TdTowerDef def, in TdEnemy target)
    {
        double raw = def.Damage * def.Levels[tower.Level - 1].DamageMultiplier;
        return Math.Max(1.0, Math.Floor(raw - target.Armor));
    }

    /// <summary>Gancho TD.3: mira. Mínimo: First (mais perto do núcleo, menor X; empate: menor Id) dentro do alcance; voador só por alvo "both".</summary>
    private int PickTarget(in TdTower tower, TdTowerDef def)
    {
        int best = -1;
        for (int i = 0; i < _enemies.Count; i++)
        {
            var e = _enemies[i];
            if (e.Flying && def.Targets != "both") continue;
            if (Math.Abs(e.X - tower.X) > def.Range) continue;
            if (best < 0 || e.X < _enemies[best].X) best = i;
        }
        return best;
    }

    private void FireTowers()
    {
        for (int ti = 0; ti < _towers.Count; ti++)
        {
            var t = _towers[ti];
            var def = _content.Towers[t.DefIndex];
            if (t.CooldownTicks > 0) t.CooldownTicks--;
            // Só torres de ataque disparam aqui; balanceador (blocker) e scaleset (support) são da TD.3.
            if (def.Kind == "attack" && t.CooldownTicks == 0)
            {
                int target = PickTarget(t, def);
                if (target >= 0)
                {
                    var e = _enemies[target];
                    e.Hp -= ComputeDamage(t, def, e);
                    t.CooldownTicks = (int)Math.Ceiling(def.Interval * TdWorld.TicksPerSecond * (1.0 - 1e-12));
                    if (e.Hp <= 0)
                    {
                        _enemies.RemoveAt(target);
                        Credits = Simulation.Sat.Add(Credits, e.Gold);
                        Emit(TdEventType.EnemyKilled, e.Id, e.Gold);
                        WaveOf(e.Wave).Alive--;
                    }
                    else
                    {
                        _enemies[target] = e;
                    }
                }
            }
            _towers[ti] = t;
        }
    }

    private TdActiveWave WaveOf(int number) => _active.First(w => w.Number == number);

    private void MoveEnemies()
    {
        for (int i = 0; i < _enemies.Count;)
        {
            var e = _enemies[i];
            e.X -= e.Speed / TdWorld.TicksPerSecond;
            if (e.X > 0)
            {
                _enemies[i++] = e;
                continue;
            }
            _enemies.RemoveAt(i);
            WaveOf(e.Wave).Alive--;
            Integrity = Math.Max(0, Integrity - e.CoreDamage);
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
            Credits = Simulation.Sat.Add(Credits, bonus);
            Emit(TdEventType.WaveCompleted, w.Number, bonus);
        }
        SettlePhase();
    }
}
