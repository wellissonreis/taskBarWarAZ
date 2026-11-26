using SideWar.Core.Content.Td;

namespace SideWar.Core.Td;

public sealed partial class TdMatchState
{
    /// <summary>
    /// Avança um passo. Em Preparo o mundo não anda (nenhum inimigo, nenhuma recarga): só corre a contagem de auto-início, se
    /// ligada (RF-28.3). Em Onda roda um tick de 30 Hz: spawns, torres, movimento/vazamento, conclusão de ondas.
    /// A Vitória é terminal (o auto-início nunca reinicia a fase).
    /// </summary>
    public void Step()
    {
        if (Paused) return; // pausa: nada avança (inimigos, spawns, recargas, contagem, ondas, tempo)
        Frame++;
        if (Phase == MatchPhase.Preparo)
        {
            if (AutoStart == AutoStartMode.Immediate) StartImmediateIfReady();
            else if (_countdownActive && HasPlacedTower && --_countdownTicks <= 0) BeginWave(NextWaveNumber);
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
            if (Phase == MatchPhase.Onda) { TickBackupHealing(); CompleteWaves(); }
        }
        finally
        {
            _inTick = false;
        }
    }

    /// <summary>Multiplicador de HP por volta completa depois da fase 6 (corrida infinita, TD.28a).</summary>
    public const double CycleHpGrowth = 1.4;

    private void SpawnDue()
    {
        var rules = _content.Stages.Rules;
        foreach (var w in _active)
        {
            while (!w.SpawnsDone && w.Spawns[w.NextSpawn].DueTick <= Tick - w.StartTick)
            {
                if (_enemies.Count >= rules.MaxEnemiesOnField) return; // teto de 44 em campo (SDD-12): o spawn espera
                var sp = w.Spawns[w.NextSpawn++];
                var spawned = MakeEnemy(sp.EnemyIndex, w.Number, TdWorld.PathLength, sp.Mutations);
                if (!spawned.IsBoss) spawned.Speed *= EarlySpeedFactor(w.Number) * CycleSpeed();
                _enemies.Add(spawned);
                w.Alive++;
            }
        }
    }

    /// <summary>HP cresce linearmente por onda, inclusive para chefes, e mantém os fatores de fase e ciclo.</summary>
    private double ScaledHp(double baseHp, int wave)
    {
        var r = _content.Stages.Rules;
        double hp = baseHp * (1.0 + (r.HpWaveGrowth - 1.0) * (wave - 1));
        hp *= 1.0 + r.HpStageStep * (_stageNumber - 1);
        for (int c = Cycle; c > 0; c--) hp *= CycleHpGrowth; // infinito: cada volta completa pelas fases fica 40% mais dura (multiplicação iterativa, ADR-026)
        return hp;
    }

    /// <summary>Multiplicador de HP/escudo por dificuldade (campanha), desde a onda 1.</summary>
    public const double HardHpMultiplier = 1.05, ExtremeHpMultiplier = 1.1;
    /// <summary>Chefe regional da última onda: escudo (fração do HP) e armadura extra.</summary>
    public const double FinalBossShieldFraction = 0.35, FinalBossArmorBonus = 10;

    /// <summary>
    /// Escala de campanha: sobe em linha reta, sem platô, de <c>hpStart</c> (onda 1) a <c>hpEnd</c> (última onda) da região,
    /// acompanhando o progresso de runas e fusões esperado, vezes a dificuldade. 1 no modo clássico.
    /// </summary>
    public double CampaignHpFactor(int wave)
    {
        if (!_options.UsesCampaignRules) return 1.0;
        double difficulty = _options.Difficulty switch { TdDifficulty.Hard => HardHpMultiplier, TdDifficulty.Extreme => ExtremeHpMultiplier, _ => 1.0 };
        return CampaignCurve(wave) * difficulty;
    }

    /// <summary>A escala da região sem a dificuldade (1 no modo clássico).</summary>
    public double CampaignCurve(int wave)
    {
        if (!_options.UsesCampaignRules) return 1.0;
        double p = Math.Clamp((wave - 1) / (double)Math.Max(1, _waves.Length - 1), 0.0, 1.0);
        return _stage.HpStart + (_stage.HpEnd - _stage.HpStart) * p;
    }

    private static bool IsRegionalBoss(TdEnemyStats st) => st.IsBoss && st.Id.StartsWith("chefe_", StringComparison.Ordinal);

    /// <summary>
    /// Chefe (não aliado) da última onda na campanha: não é empurrado, retido nem paralisado, para a onda sempre terminar
    /// (vitória ou derrota) em vez de ficar presa para sempre.
    /// </summary>
    private bool IsUnstoppable(in TdEnemy e)
    {
        var st = _enemyStats[e.DefIndex];
        return _options.UsesCampaignRules && st.IsBoss && e.Wave == TotalWaves && !st.Id.StartsWith("aliado_", StringComparison.Ordinal);
    }

    /// <summary>Região final da campanha (última fase do conteúdo, em qualquer ciclo).</summary>
    public bool IsFinalRegion => _stage.Id == _content.Stages.Stages.Length;

    /// <summary>Regra-mestra: o chefe da região final só recebe dano com a teia inteira despertada.</summary>
    public bool FinalBossImmune => _options.UsesCampaignRules && IsFinalRegion && AwakenedCount < _content.Runes.Nodes.Length;

    /// <summary>Início mais calmo (RITMO-INICIO): na fase 1 do primeiro ciclo a velocidade sobe de 70% (onda 1) até 100% (onda 13, 2,5 pp por onda); depois disso, valores originais.</summary>
    private double EarlySpeedFactor(int wave) =>
        _stageNumber == 1 && Cycle == 0 && wave < 13 ? 0.70 + 0.025 * (wave - 1) : 1.0;

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
            if ((e.ParalyzeTicks > 0 || _held.Contains(e.Id)) && !IsUnstoppable(e)) mult = 0.0;
            else
            {
                if (e.SlowTicks > 0) mult = Math.Min(mult, e.SlowMult);
                if (firewall > 0 && e.ShieldHp <= 0) mult = Math.Min(mult, 1.0 - (st.IsBoss ? FirewallBossSlow() : firewall));
                mult = Math.Max(mult, 1.0 - st.SlowCap); // teto de lentidão (chefes 20%, Zero-day 0%)
            }
            e.X -= e.Speed * (e.Enraged ? 1.6 : 1.0) * mult / TdWorld.TicksPerSecond;
            if (e.X > leakX)
            {
                if (st.IsBoss && st.InvulnTicks > 0) e = BossPathTriggers(e, st);
                _enemies[i++] = e;
                continue;
            }
            _enemies.RemoveAt(i);
            WaveOf(e.Wave).Alive--;
            _stageIntegrityLoss = (int)Math.Min(int.MaxValue, (long)_stageIntegrityLoss + e.CoreDamage);
            _hasLeaked = true;
            WaveOf(e.Wave).Leaked = true;
            if (e.CoreDamage > 0 && e.Wave == TotalWaves && WaveOf(e.Wave).Agenda.Any(s => s.IsBoss))
            {
                _finalWaveFailed = true;
                if (st.IsBoss && !st.Id.StartsWith("aliado_", StringComparison.Ordinal)) _bossBreached = true;
            }
            RecordLeak(e);
            Emit(TdEventType.EnemyLeaked, e.Id, e.CoreDamage);
            LoseReputation(e.CoreDamage); // sem game over: só perde XP de reputação
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
            bonus += RuneWaveCredits(w.Number); // runas de Créditos (TD.29a)
            bonus = Income(bonus);
            Credits = Simulation.Sat.Add(Credits, bonus);
            foreach (var tower in _towers)
            {
                var def = _content.Towers[tower.DefIndex];
                long income = ModuleVaultIncome(tower);
                if (def.Id == "vault" && tower.Placed && tower.DisabledTicks == 0)
                    income += (long)Stat(tower, def, "creditsPerWave", 0) + (w.Leaked ? 0 : (long)Stat(tower, def, "noLeakBonus", 0));
                income = Income(income);
                if (income <= 0) continue;
                Credits = Simulation.Sat.Add(Credits, income);
                Emit(TdEventType.VaultIncome, tower.Id, income);
            }
            GainFragments(_content.Stages.Rules.Fragments.PerWave);
            Emit(TdEventType.WaveCompleted, w.Number, bonus);
            WinWaveReputation(w.Number, w.Leaked);
        }
        SettlePhase();
    }
}
