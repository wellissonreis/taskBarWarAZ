using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

// Combate da TD.3 (SDD-28 RF-28.5 a RF-28.8): torres de ataque, balanceador (nós), scaleset (aura), traços e chefes.
// Tudo em double com ordem de iteração fixa (lista = ordem de Id); sem Rng, relógio real ou Math.Pow (ADR-026).
public sealed partial class TdMatchState
{
    // Valores iniciais não cobertos numericamente pelo SDD-28 (ajustáveis; ver REPORT TD.3).
    private const int SlowDurationTicks = 45;          // lentidão do gelo dura 1,5 s e renova a cada acerto
    private const int ParalysisGraceTicks = 30;        // após a paralisia, 1 s de imunidade a nova paralisia (cap anti stun-lock)
    private const int MarkDurationTicks = 90;          // corrosão (armadura) e vulnerável: 3 s, renovados a cada acerto
    private const double ParalyzeAreaRadius = 50.0;    // "Zero absoluto"
    private const double NodeCatchRadius = 12.0;       // um nó retém inimigos de solo a até 12 unidades
    private const int NodeLifeTicks = 180;             // tempo máximo de um nó: 6 s
    private const double NodeHp = 30.0;                // o nó cai sob a pressão dos retidos
    private const double NodeHpDrainPerCoreDamage = 10.0; // por segundo, por ponto de dano ao núcleo do inimigo retido
    private const double NodeExplosionRadius = 40.0;   // Failover
    private const double NodeExplosionDpsFactor = 10.0; // dano da explosão = 10 × dano/s do nó
    private const double BurnDpsFactor = 0.2;          // Saturação: 20% do dano base do tiro por segundo
    private const int MaxZones = 64;
    private const double BaseAura = 0.05;              // Scale Set: +5% de cadência nas vizinhas (sem nível/ramo)
    private const int ResetFocusCap = 1000;

    private double[] _auraCadence = new double[8];
    private int _damageSource;
    private Dictionary<int, int> _killsByTower = [];

    /// <summary>Mortes por id de torre (0 = zona de queimadura). Só observação (SanityBot/balanceamento); fora do hash.</summary>
    public IReadOnlyDictionary<int, int> KillsByTower => _killsByTower;

    private double[] _auraDamage = new double[8];
    private double[] _auraRange = new double[8];

    // ---- estatísticas por torre ----------------------------------------------------------------------------------

    /// <summary>Estatística base com os modificadores da teia (TD.29a): só o dano do Raio e do Canhão mudam (+5% por runa).</summary>
    internal double Stat(in TdTower t, TdTowerDef def, string key, double fallback)
    {
        double value = def.Stats.TryGetValue(key, out var v) ? v : fallback;
        if (key != "damageMultiplier") return value;
        return def.Id switch
        {
            "bolt" => value * (1 + Rune("boltDamageBp") / 10000.0),
            "cannon" => value * (1 + Rune("cannonDamageBp") / 10000.0),
            _ => value,
        };
    }

    private static int Ticks(double seconds) => (int)Math.Round(seconds * TdWorld.TicksPerSecond, MidpointRounding.ToEven);

    private static double Dist(double a, double b) => Math.Abs(a - b);

    // ---- estado dos inimigos (por tick) ---------------------------------------------------------------------------

    private void TickEnemyStatus()
    {
        for (int i = 0; i < _enemies.Count; i++)
        {
            var e = _enemies[i];
            if (e.Hp <= 0) continue;
            var st = _enemyStats[e.DefIndex];
            if (e.SlowTicks > 0 && --e.SlowTicks == 0) e.SlowMult = 1.0;
            if (e.SentinelMarkTicks > 0) e.SentinelMarkTicks--;
            if (e.ParalyzeTicks > 0) { if (--e.ParalyzeTicks == 0) e.ParalyzeGraceTicks = ParalysisGraceTicks; }
            else if (e.ParalyzeGraceTicks > 0) e.ParalyzeGraceTicks--;
            if (e.PushGraceTicks > 0) e.PushGraceTicks--;
            if (e.InvulnTicks > 0) e.InvulnTicks--;
            if (e.VulnTicks > 0 && --e.VulnTicks == 0) e.VulnBonus = 0;
            if (e.CorrodeTicks > 0 && --e.CorrodeTicks == 0) e.CorrodeAmount = 0;
            if (e.SinceDamageTicks < ResetFocusCap * 1000) e.SinceDamageTicks++;
            if (st.RegenPct > 0 && e.SinceDamageTicks >= st.RegenDelayTicks && e.Hp < e.MaxHp)
                e.Hp = Math.Min(e.MaxHp, e.Hp + e.MaxHp * st.RegenPct / TdWorld.TicksPerSecond);
            if (st.ActRange > 0)
            {
                if (e.AbilityCdTicks > 0) e.AbilityCdTicks--;
                if (e.AbilityCdTicks == 0) DisableNearestTower(ref e, st);
            }
            _enemies[i] = e;
        }
    }

    /// <summary>Drenagem do parasita / criptografia do Ransomware: desativa a torre ativa mais próxima dentro do alcance.</summary>
    private void DisableNearestTower(ref TdEnemy e, TdEnemyStats st)
    {
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < _towers.Count; i++)
        {
            if (_towers[i].DisabledTicks > 0 || !_towers[i].Placed) continue;
            double d = Dist(_towers[i].X, e.X);
            if (d <= st.ActRange && d < bestD) { best = i; bestD = d; }
        }
        if (best < 0) return;
        var t = _towers[best];
        t.DisabledTicks = st.ActDisableTicks;
        _towers[best] = t;
        e.AbilityCdTicks = st.ActCdTicks;
        Emit(TdEventType.TowerDisabled, t.Id, e.Id);
    }

    // ---- auras (Scale Set) ----------------------------------------------------------------------------------------

    private void ComputeAuras()
    {
        int n = _towers.Count;
        if (_auraCadence.Length < n)
        {
            _auraCadence = new double[n * 2];
            _auraDamage = new double[n * 2];
            _auraRange = new double[n * 2];
        }
        bool scale = IsAbilityActive("scale");
        for (int i = 0; i < n; i++)
        {
            var t = _towers[i];
            bool bolt = _content.Towers[t.DefIndex].Id == "bolt";
            double cad = 0, dmg = 0, rng = 0;
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                var s = _towers[j];
                var sd = _content.Towers[s.DefIndex];
                if (sd.Id != "scaleset" || s.DisabledTicks > 0 || !s.Placed || Dist(s.X, t.X) > EffectiveRange(sd)) continue;
                double c = BaseAura;
                if (c > 0 && scale) c *= Stat(s, sd, "bonusMultiplierDuringScale", 1.0);
                cad += c;
                dmg += Stat(s, sd, "neighborDamageBonus", 0);
                if (bolt) rng += Stat(s, sd, "boltRangeBonus", 0);
            }
            // Módulo genérico de suporte (SDD-37 RF-37.5): +5% de dano a OUTRAS entidades posicionadas no alcance do módulo, sem autoaura.
            for (int j = 0; j < n; j++)
            {
                var s = _towers[j];
                if (j == i || !s.HasModule || s.DisabledTicks > 0 || !s.Placed || !t.Placed) continue;
                var md = _content.Towers[s.ModuleDefIndex];
                if (FusionRole(s.ModuleDefIndex) != "support" || FusionPair(s.DefIndex, s.ModuleDefIndex) is not null || Dist(s.X, t.X) > EffectiveRange(md)) continue;
                dmg += _content.Fusion.Generic["support"]["damageAuraBp"] / 10000.0;
            }
            _auraCadence[i] = cad;
            _auraDamage[i] = dmg;
            _auraRange[i] = rng;
        }
    }

    // ---- torres ---------------------------------------------------------------------------------------------------

    private void FireTowers()
    {
        RefreshHeldTargets();
        TickBackupRevive();
        ComputeAuras();
        bool scale = IsAbilityActive("scale");
        TickControlTowers("sentinel");
        TickControlTowers("queue");
        for (int ti = 0; ti < _towers.Count; ti++)
        {
            var t = _towers[ti];
            var def = _content.Towers[t.DefIndex];
            if (t.CooldownTicks > 0) t.CooldownTicks--;
            if (t.ParalyzeCdTicks > 0) t.ParalyzeCdTicks--;
            if (t.ModuleCdTicks > 0) t.ModuleCdTicks--;
            if (t.NodeCdTicks > 0 && def.Id != "backup") t.NodeCdTicks--;
            bool disabled = t.DisabledTicks > 0;
            if (def.Id == "pipeline" && (disabled || !t.Placed || !_enemies.Any(e => e.Id == t.FocusTargetId && e.Hp > 0 && Dist(e.X, t.X) <= EffectiveRange(def) + _auraRange[ti])))
            { t.FocusTargetId = 0; t.FocusHits = 0; }
            if (!disabled && t.Placed)
            {
                if (def.Id == "functions") TickFunctions(ref t, def, ti);
                else if (def.Kind == "attack" && t.CooldownTicks == 0) FireAttack(ref t, def, ti, scale);
                else if (def.Kind == "blocker") SpawnNodes(ref t, def, ti);
                else if (def.Id == "aks") SpawnPods(ref t, def);
                if (t.HasModule) TickModule(ref t, ti);
            }
            else if (def.Id == "functions")
            {
                int x = t.X;
                double range = EffectiveRange(def) + _auraRange[ti];
                t.RangeSeenIds = _enemies.Where(e => e.Hp > 0 && Dist(e.X, x) <= range).Select(e => e.Id).Order().ToArray();
                t.BurstLeft = 0; t.BurstGapTicks = 0; t.BurstTargetId = 0;
            }
            _towers[ti] = t;
        }
        for (int i = 0; i < _towers.Count; i++)
        {
            var t = _towers[i];
            if (t.DisabledTicks > 0) { t.DisabledTicks--; _towers[i] = t; }
        }
    }

    private void TickControlTowers(string kind)
    {
        for (int i = 0; i < _towers.Count; i++)
        {
            var t = _towers[i];
            var def = _content.Towers[t.DefIndex];
            if (def.Id != kind || !t.Placed || t.DisabledTicks > 0 || t.CooldownTicks > 1) continue;
            int target = PickTarget(t, def, EffectiveRange(def) + _auraRange[i]);
            if (target < 0) continue;
            var e = _enemies[target];
            if (kind == "queue")
            {
                if (e.PushGraceTicks > 0 || _held.Contains(e.Id)) continue;
                double amount = Stat(t, def, "pushbackUnits", 60);
                if (e.IsBoss) amount *= Stat(t, def, "bossPushbackFactorBp", 5000) / 10000.0;
                double oldX = e.X;
                e.X = Math.Min(TdWorld.PathLength, e.X + amount);
                for (int j = 0; j < _towers.Count; j++)
                {
                    var functions = _towers[j];
                    var fd = _content.Towers[functions.DefIndex];
                    if (fd.Id != "functions" || !functions.Placed) continue;
                    double range = EffectiveRange(fd) + _auraRange[j];
                    if (Dist(oldX, functions.X) <= range || Dist(e.X, functions.X) > range) continue;
                    functions.RangeSeenIds = (functions.RangeSeenIds ?? []).Append(e.Id).Distinct().Order().ToArray();
                    _towers[j] = functions;
                }
                e.PushGraceTicks = Ticks(Stat(t, def, "pushGraceSeconds", 3));
            }
            else
            {
                e.VulnBonus = Math.Max(e.VulnTicks > 0 ? e.VulnBonus : 0, Stat(t, def, "markBonusBp", 2500) / 10000.0);
                e.VulnTicks = Math.Max(e.VulnTicks, Ticks(Stat(t, def, "markSeconds", 4)));
                if (_enemyStats[e.DefIndex].RevealRange > 0) e.SentinelMarkTicks = Ticks(Stat(t, def, "markSeconds", 4));
            }
            _enemies[target] = e;
            t.CooldownTicks = Ticks(def.Interval) + 1;
            _towers[i] = t;
        }
    }

    private void TickFunctions(ref TdTower t, TdTowerDef def, int ti)
    {
        double range = EffectiveRange(def) + _auraRange[ti];
        int towerX = t.X;
        var inside = _enemies.Where(e => e.Hp > 0 && VisibleToTower(e, def) && Dist(e.X, towerX) <= range).Select(e => e.Id).Order().ToArray();
        if (t.BurstGapTicks > 0) t.BurstGapTicks--;
        if (t.BurstLeft == 0 && t.CooldownTicks == 0)
        {
            int target = -1;
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                if (e.Hp <= 0 || e.InvulnTicks > 0 || !VisibleToTower(e, def) || Dist(e.X, t.X) > range || (t.RangeSeenIds is not null && Array.BinarySearch(t.RangeSeenIds, e.Id) >= 0)) continue;
                if (target < 0 || Better(TdTargeting.First, e, _enemies[target], t.X)) target = i;
            }
            if (target >= 0)
            {
                t.BurstTargetId = _enemies[target].Id;
                t.BurstLeft = (int)Stat(t, def, "burstShots", 3);
                t.CooldownTicks = Ticks(Stat(t, def, "rearmSeconds", 3));
            }
        }
        t.RangeSeenIds = inside;
        if (t.BurstLeft > 0 && t.BurstGapTicks == 0)
        {
            int targetId = t.BurstTargetId;
            int target = _enemies.FindIndex(e => e.Id == targetId && e.Hp > 0 && e.InvulnTicks == 0 && VisibleToTower(e, def) && Dist(e.X, towerX) <= range);
            if (target < 0) { t.BurstLeft = 0; t.BurstTargetId = 0; return; }
            var enemy = _enemies[target];
            _damageSource = t.Id;
            DealDamage(target, ComputeDamage(t, def, enemy, _enemyStats[enemy.DefIndex], ti, 1, 1));
            t.ShotCount++;
            t.BurstLeft--;
            t.BurstGapTicks = t.BurstLeft > 0 ? (int)Stat(t, def, "burstGapTicks", 4) : 0;
            if (t.BurstLeft == 0) t.BurstTargetId = 0;
        }
    }

    private void FireAttack(ref TdTower t, TdTowerDef def, int ti, bool scale)
    {
        double range = EffectiveRange(def) + _auraRange[ti];
        int target = PickTarget(t, def, range);
        if (target < 0) return;
        var e = _enemies[target];
        var st = _enemyStats[e.DefIndex];

        // Foco (Rajada) e super hit (Desligamento): passo 2 da ordem do dano.
        double burst = 1.0;
        double perHit = def.Id == "pipeline" ? Stat(t, def, "rampPerHitBp", 0) / 10000.0 : Stat(t, def, "focusBonusPerHit", 0);
        if (perHit > 0)
        {
            if (t.FocusTargetId != e.Id)
            {
                if (Stat(t, def, "focusKeepsBetweenTargets", 0) <= 0) t.FocusHits = 0;
                t.FocusTargetId = e.Id;
            }
            burst *= 1.0 + Math.Min(def.Id == "pipeline" ? Stat(t, def, "rampCapBp", 0) / 10000.0 : Stat(t, def, "focusBonusCap", 0), t.FocusHits * perHit);
            if (t.FocusHits < ResetFocusCap) t.FocusHits++;
        }
        t.ShotCount++;
        int every = (int)Stat(t, def, "superHitEvery", 0);
        if (every > 0 && t.ShotCount % every == 0) burst *= Stat(t, def, "superHitMultiplier", 1.0);

        _damageSource = t.Id;
        DealDamage(target, ComputeDamage(t, def, e, st, ti, 1.0, burst));
        ApplyOnHit(ref t, def, target);
        ApplyModuleOnHit(t, def, ti, target);
        if (def.Id == "cosmos")
        {
            var hit = new HashSet<int> { e.Id };
            double x = e.X, factor = 1;
            int jumps = (int)Stat(t, def, "chainJumps", 3);
            for (int jump = 0; jump < jumps; jump++)
            {
                int next = -1; double distance = double.MaxValue;
                for (int j = 0; j < _enemies.Count; j++)
                {
                    var candidate = _enemies[j];
                    double d = Dist(x, candidate.X);
                    if (candidate.Hp <= 0 || candidate.InvulnTicks > 0 || !VisibleToTower(candidate, def) || hit.Contains(candidate.Id) || (candidate.Flying && def.Targets != "both") || d > Stat(t, def, "chainRadius", 60)) continue;
                    if (d < distance || (d == distance && (next < 0 || candidate.Id < _enemies[next].Id))) { next = j; distance = d; }
                }
                if (next < 0) break;
                var other = _enemies[next]; hit.Add(other.Id); x = other.X;
                factor *= Stat(t, def, "chainFalloffBp", 8000) / 10000.0;
                DealDamage(next, ComputeDamage(t, def, other, _enemyStats[other.DefIndex], ti, factor, 1));
            }
        }

        double splashFactor = Stat(t, def, "splashFactor", 0);
        double radius = Stat(t, def, "splashRadius", 0) * Stat(t, def, "splashRadiusMultiplier", 1.0);
        if (splashFactor > 0 && radius > 0)
        {
            for (int j = 0; j < _enemies.Count; j++)
            {
                var o = _enemies[j];
                if (j == target || o.Flying || o.Hp <= 0 || o.InvulnTicks > 0 || Dist(o.X, e.X) > radius) continue;
                DealDamage(j, ComputeDamage(t, def, o, _enemyStats[o.DefIndex], ti, splashFactor, 1.0));
            }
        }
        double burn = Stat(t, def, "burnZoneSeconds", 0);
        if (burn > 0 && radius > 0 && _zones.Count < MaxZones)
            _zones.Add(new TdZone { X = e.X, Radius = radius, TicksLeft = Ticks(burn), DamagePerTick = def.Damage * BurnDpsFactor / TdWorld.TicksPerSecond });

        double interval = def.Interval * Stat(t, def, "intervalMultiplier", 1.0);
        double cadence = 1.0 + _auraCadence[ti] + (scale ? AbilityStat("scale", "cadenceBonus") : 0.0);
        t.CooldownTicks = Math.Max(1, (int)Math.Ceiling(interval / cadence * TdWorld.TicksPerSecond * (1.0 - 1e-12)));
    }

    /// <summary>Efeitos por acerto lidos dos stats: lentidão, corrosão/vulnerável e paralisia periódica (com área no capstone).</summary>
    private void ApplyOnHit(ref TdTower t, TdTowerDef def, int target)
    {
        double slow = Stat(t, def, "slowFactor", 0);
        if (slow > 0)
        {
            ApplySlow(target, slow);
        }
        double corrode = Stat(t, def, "armorReduction", 0);
        double vuln = Stat(t, def, "vulnerableBonus", 0);
        if (corrode > 0 || vuln > 0)
        {
            var e = _enemies[target];
            if (corrode > 0) { e.CorrodeAmount = Math.Max(e.CorrodeTicks > 0 ? e.CorrodeAmount : 0, corrode); e.CorrodeTicks = MarkDurationTicks; }
            if (vuln > 0) { e.VulnBonus = Math.Max(e.VulnTicks > 0 ? e.VulnBonus : 0, vuln); e.VulnTicks = MarkDurationTicks; }
            _enemies[target] = e;
        }
        double every = Stat(t, def, "paralyzeEverySeconds", 0);
        if (every > 0 && t.ParalyzeCdTicks == 0)
        {
            int dur = Ticks(Stat(t, def, "paralyzeSeconds", 0));
            double cx = _enemies[target].X;
            bool area = Stat(t, def, "paralyzeArea", 0) > 0;
            for (int j = 0; j < _enemies.Count; j++)
                if (j == target || (area && _enemies[j].Hp > 0 && Dist(_enemies[j].X, cx) <= ParalyzeAreaRadius)) Paralyze(j, dur);
            t.ParalyzeCdTicks = Ticks(every);
        }
    }

    private void ApplySlow(int idx, double mult, int ticks = -1)
    {
        var e = _enemies[idx];
        if (e.ShieldHp > 0 || _enemyStats[e.DefIndex].SlowCap <= 0) return; // escudo ou imunidade
        e.SlowMult = e.SlowTicks > 0 ? Math.Min(e.SlowMult, mult) : mult;
        e.SlowTicks = ticks >= 0 ? ticks : SlowDurationTicks + Rune("frostSlowTenths") * 3; // +0,1 s = 3 ticks por décimo (runa Gelo, TD.29a)
        _enemies[idx] = e;
    }

    private void Paralyze(int idx, int ticks)
    {
        var e = _enemies[idx];
        if (_enemyStats[e.DefIndex].ParalysisImmune || e.ParalyzeTicks > 0 || e.ParalyzeGraceTicks > 0 || e.Hp <= 0) return;
        e.ParalyzeTicks = ticks;
        _enemies[idx] = e;
    }

    private void DealDamage(int idx, double dmg)
    {
        var e = _enemies[idx];
        if (e.InvulnTicks > 0 || dmg <= 0) return;
        double absorbed = Math.Min(e.ShieldHp, dmg);
        e.ShieldHp -= absorbed;
        e.Hp -= dmg - absorbed;
        e.LastHitTowerId = _damageSource;
        e.SinceDamageTicks = 0;
        _enemies[idx] = e;
    }

    /// <summary>
    /// Ordem do dano (RF-28.5): (1) dano do nível/ramo × fator; (2) foco/super hit (<paramref name="burst"/>); (3) vulnerável;
    /// (4) multiplicadores de habilidade/aura (+ redução do Ransomware); (5) armadura efetiva (corrosão, depois perfuração);
    /// <c>max(1, floor)</c>. Hook TD.5: bônus de meta (dano %) entram no passo 4.
    /// </summary>
    private double ComputeDamage(in TdTower tower, TdTowerDef def, in TdEnemy target, TdEnemyStats st, int towerIndex, double factor, double burst)
    {
        double raw = def.Damage * Stat(tower, def, "damageMultiplier", 1.0) * factor;
        if (st.Armored || st.IsBoss) raw *= Stat(tower, def, "damageMultiplierVsArmoredAndBoss", 1.0);
        raw *= burst;
        if (target.VulnTicks > 0) raw *= 1.0 + target.VulnBonus;
        raw *= 1.0 + _auraDamage[towerIndex];
        if (st.BlockedReduction > 0 && Dist(tower.X, target.X) <= st.ActRange) raw *= 1.0 - st.BlockedReduction;
        double armor = Math.Max(0.0, target.Armor - (target.CorrodeTicks > 0 ? target.CorrodeAmount : 0.0));
        armor *= 1.0 - Math.Clamp(Stat(tower, def, "armorPenetration", 0), 0.0, 1.0);
        double removal = 0;
        foreach (var policy in _towers)
        {
            var pd = _content.Towers[policy.DefIndex];
            if (pd.Id == "policy" && policy.Placed && policy.DisabledTicks == 0 && Dist(policy.X, target.X) <= EffectiveRange(pd))
                removal = Math.Max(removal, Stat(policy, pd, "armorRemoveBp", 0) / 10000.0);
        }
        armor *= 1.0 - Math.Clamp(removal, 0, 1);
        return Math.Max(1.0, Math.Floor(raw - armor + 1e-9));
    }

    /// <summary>Mira (RF-28.6): voador só por torre "both"; invulneráveis e mortos ignorados; empates pelo mais perto do núcleo e depois menor Id.</summary>
    private int PickTarget(in TdTower tower, TdTowerDef def, double range)
    {
        var mode = (TdTargeting)tower.Targeting;
        int best = -1;
        for (int i = 0; i < _enemies.Count; i++)
        {
            var e = _enemies[i];
            if (e.Hp <= 0 || e.InvulnTicks > 0) continue;
            if (!VisibleToTower(e, def)) continue;
            if (def.Id == "queue" && (e.PushGraceTicks > 0 || _held.Contains(e.Id))) continue;
            if (e.Flying && def.Targets != "both") continue;
            if (Dist(e.X, tower.X) > range || Dist(e.X, tower.X) < Stat(tower, def, "minRange", 0)) continue;
            if (best < 0 || Better(mode, e, _enemies[best], tower.X)) best = i;
        }
        return best;
    }

    private bool VisibleToTower(in TdEnemy enemy, TdTowerDef tower) =>
        _enemyStats[enemy.DefIndex].RevealRange <= 0 || tower.Id == "sentinel" || enemy.SentinelMarkTicks > 0 || enemy.X - LeakX <= _enemyStats[enemy.DefIndex].RevealRange;

    private static bool Better(TdTargeting mode, in TdEnemy e, in TdEnemy b, double towerX)
    {
        int c = mode switch
        {
            TdTargeting.Last => b.X.CompareTo(e.X),
            TdTargeting.Strong => b.Hp.CompareTo(e.Hp),
            TdTargeting.Weak => e.Hp.CompareTo(b.Hp),
            TdTargeting.Close => Dist(e.X, towerX).CompareTo(Dist(b.X, towerX)),
            _ => e.X.CompareTo(b.X),
        };
        if (c == 0 && mode != TdTargeting.First) c = e.X.CompareTo(b.X);
        if (c == 0) c = e.Id.CompareTo(b.Id);
        return c < 0;
    }

    // ---- balanceador: nós ----------------------------------------------------------------------------------------

    private void TickBackupRevive()
    {
        for (int i = 0; i < _towers.Count; i++)
        {
            var t = _towers[i]; var def = _content.Towers[t.DefIndex];
            if (def.Id != "backup" || !t.Placed || t.DisabledTicks > 0 || t.CooldownTicks > 1) continue;
            for (int j = 0; j < _towers.Count; j++)
            {
                var other = _towers[j];
                if (j == i || !other.Placed || other.DisabledTicks <= 0 || Dist(t.X, other.X) > EffectiveRange(def)) continue;
                other.DisabledTicks = 0; _towers[j] = other;
                t.CooldownTicks = Ticks(Stat(t, def, "reviveEverySeconds", 6)) + 1; _towers[i] = t; break;
            }
        }
    }

    private void TickBackupHealing()
    {
        for (int i = 0; i < _towers.Count; i++)
        {
            var t = _towers[i]; var def = _content.Towers[t.DefIndex];
            if (def.Id != "backup" || !t.Placed || t.DisabledTicks > 0) continue;
            t.NodeCdTicks++;
            if (t.NodeCdTicks >= Ticks(Stat(t, def, "coreHealEverySeconds", 45)))
            {
                t.NodeCdTicks = 0;
                if (Integrity < IntegrityMax && _backupHealsThisStage < Stat(t, def, "coreHealCapPerStage", 3)) { Integrity++; _backupHealsThisStage++; }
            }
            _towers[i] = t;
        }
    }

    private void SpawnPods(ref TdTower t, TdTowerDef def)
    {
        if (t.NodeCdTicks > 0) return;
        int count = 0; double first = double.MaxValue;
        foreach (var e in _enemies)
            if (!e.Flying && e.Hp > 0 && VisibleToTower(e, def) && Dist(e.X, t.X) <= EffectiveRange(def)) { count++; first = Math.Min(first, e.X); }
        if (count < Stat(t, def, "triggerEnemies", 3)) return;
        for (int i = 0; i < (int)Stat(t, def, "pods", 3); i++)
        {
            _nodes.Add(new TdNode { Id = _nextNodeId++, TowerId = t.Id, X = first + 12 * i, Hp = 1, Holds = 0, Dps = Stat(t, def, "podDps", 8), LifeTicks = Ticks(Stat(t, def, "podLifeSeconds", 12)), Flags = 4 });
            Emit(TdEventType.NodeSpawned, t.Id, (long)(first + 12 * i));
        }
        t.NodeCdTicks = Ticks(Stat(t, def, "interval", 20));
    }

    private void SpawnNodes(ref TdTower t, TdTowerDef def, int ti)
    {
        if (t.NodeCdTicks > 0) return;
        int maxNodes = (int)Stat(t, def, "nodes", 1);
        int active = 0;
        foreach (var n in _nodes) if (n.TowerId == t.Id) active++;
        bool front = Stat(t, def, "spawnAtFront", 0) > 0;
        double frontX = Math.Max(0.0, t.X - EffectiveRange(def));
        while (active < maxNodes)
        {
            double x = -1;
            if (front)
            {
                if (HasGroundEnemyInRange(t.X, EffectiveRange(def)) && !NodeNear(frontX)) x = frontX;
            }
            else
            {
                double bestX = double.MaxValue;
                foreach (var e in _enemies)
                    if (!e.Flying && e.Hp > 0 && Dist(e.X, t.X) <= EffectiveRange(def) && e.X < bestX && !NodeNear(e.X)) bestX = e.X;
                if (bestX < double.MaxValue) x = bestX;
            }
            if (x < 0) break;
            byte flags = (byte)((Stat(t, def, "explodeOnEnd", 0) > 0 ? 1 : 0) | (Stat(t, def, "instantRespawn", 0) > 0 ? 2 : 0));
            _nodes.Add(new TdNode
            {
                Id = _nextNodeId++, TowerId = t.Id, X = x, Hp = NodeHp, LifeTicks = NodeLifeTicks,
                Holds = (int)Stat(t, def, "nodeHolds", 1),
                Dps = Stat(t, def, "nodeDamagePerSecond", 0), Flags = flags,
            });
            Emit(TdEventType.NodeSpawned, t.Id, (long)x);
            active++;
        }
    }

    private bool HasGroundEnemyInRange(double tx, double range)
    {
        foreach (var e in _enemies)
            if (!e.Flying && e.Hp > 0 && Dist(e.X, tx) <= range) return true;
        return false;
    }

    private bool NodeNear(double x)
    {
        foreach (var n in _nodes)
            if ((n.Flags & 4) == 0 && Dist(n.X, x) <= NodeCatchRadius) return true;
        return false;
    }

    /// <summary>Nós retêm (até <c>Holds</c> inimigos de solo cada), causam dano/s e caem por pressão ou tempo; zonas de queimadura queimam.</summary>
    private void TickNodesAndZones()
    {
        _held.Clear();
        for (int ni = 0; ni < _nodes.Count;)
        {
            var n = _nodes[ni];
            if ((n.Flags & 4) != 0)
            {
                int ti = FindTower(n.TowerId);
                if (ti >= 0)
                {
                    var tower = _towers[ti]; var def = _content.Towers[tower.DefIndex];
                    int target = -1;
                    for (int j = 0; j < _enemies.Count; j++)
                    {
                        var enemy = _enemies[j];
                        if (enemy.Flying || enemy.Hp <= 0 || enemy.InvulnTicks > 0 || !VisibleToTower(enemy, def) || Dist(enemy.X, tower.X) > EffectiveRange(def)) continue;
                        if (target < 0 || Better(TdTargeting.First, enemy, _enemies[target], tower.X)) target = j;
                    }
                    if (target >= 0) { _damageSource = n.TowerId; DealDamage(target, n.Dps / TdWorld.TicksPerSecond); }
                }
                if (--n.LifeTicks > 0) _nodes[ni++] = n;
                else { Emit(TdEventType.NodeEnded, n.TowerId); _nodes.RemoveAt(ni); }
                continue;
            }
            int held = 0;
            double pressure = 0;
            for (int i = 0; i < _enemies.Count && held < n.Holds; i++)
            {
                var e = _enemies[i];
                if (e.Flying || e.Hp <= 0 || _held.Contains(e.Id) || Dist(e.X, n.X) > NodeCatchRadius) continue;
                _held.Add(e.Id);
                held++;
                _damageSource = n.TowerId;
                DealDamage(i, n.Dps / TdWorld.TicksPerSecond);
                ApplyNodeModuleSlow(n.TowerId, i);
                pressure += e.CoreDamage * NodeHpDrainPerCoreDamage / TdWorld.TicksPerSecond;
            }
            n.Hp -= pressure;
            n.LifeTicks--;
            bool broken = n.Hp <= 0;
            if (!broken && n.LifeTicks > 0)
            {
                _nodes[ni++] = n;
                continue;
            }
            EndNode(ref n, broken);
            if ((n.Flags & 2) != 0 && broken && n.Hp > 0) _nodes[ni++] = n; // Blue/green: renasce na hora, mesmo X
            else _nodes.RemoveAt(ni);
        }
        for (int zi = 0; zi < _zones.Count;)
        {
            var z = _zones[zi];
            _damageSource = 0;
            for (int i = 0; i < _enemies.Count; i++)
                if (!_enemies[i].Flying && _enemies[i].Hp > 0 && Dist(_enemies[i].X, z.X) <= z.Radius) DealDamage(i, z.DamagePerTick);
            if (--z.TicksLeft > 0) _zones[zi++] = z;
            else _zones.RemoveAt(zi);
        }
    }

    private void EndNode(ref TdNode n, bool broken)
    {
        Emit(TdEventType.NodeEnded, n.TowerId, broken ? 1 : 0);
        if ((n.Flags & 1) != 0)
        {
            double dmg = n.Dps * NodeExplosionDpsFactor;
            _damageSource = n.TowerId;
            for (int i = 0; i < _enemies.Count; i++)
                if (!_enemies[i].Flying && _enemies[i].Hp > 0 && Dist(_enemies[i].X, n.X) <= NodeExplosionRadius) DealDamage(i, dmg);
        }
        if ((n.Flags & 2) != 0 && broken)
        {
            n.Hp = NodeHp; // renasce com vida e tempo cheios, sem recarga
            n.LifeTicks = NodeLifeTicks;
            return;
        }
        int ti = FindTower(n.TowerId);
        if (ti >= 0)
        {
            var t = _towers[ti];
            t.NodeCdTicks = Ticks(Stat(t, _content.Towers[t.DefIndex], "nodeCooldownSeconds", 6.0));
            _towers[ti] = t;
        }
    }

    // ---- mortes e gatilhos de chefe ---------------------------------------------------------------------------------

    private void ReapAndTriggers()
    {
        for (int i = 0; i < _enemies.Count;)
        {
            var e = _enemies[i];
            if (e.Hp <= 0)
            {
                _enemies.RemoveAt(i);
                Credits = Sat.Add(Credits, (long)e.Gold + (e.IsBoss ? _content.Runes.CreditSources.BossCredits : 0));
                if (e.IsBoss) GainFragments(_content.Stages.Rules.Fragments.PerBoss);
                Emit(TdEventType.EnemyKilled, e.Id, e.Gold);
                _killsByTower[e.LastHitTowerId] = _killsByTower.GetValueOrDefault(e.LastHitTowerId) + 1;
                WaveOf(e.Wave).Alive--;
                var deadStats = _enemyStats[e.DefIndex];
                if (deadStats.Replicator && !e.IsReplica)
                {
                    var wave = WaveOf(e.Wave);
                    for (int k = 0; k < 2 && _enemies.Count < _content.Stages.Rules.MaxEnemiesOnField; k++)
                    {
                        var copy = MakeEnemy(e.DefIndex, e.Wave, Math.Min(TdWorld.PathLength, e.X + k * 2));
                        copy.MaxHp = e.MaxHp * deadStats.CopyHpFraction;
                        copy.Hp = copy.MaxHp; copy.IsReplica = true; copy.Gold = 0;
                        _enemies.Add(copy); wave.Alive++;
                    }
                }
                continue;
            }
            var st = _enemyStats[e.DefIndex];
            if (st.SplitCount > 0)
            {
                int splits = 0;
                if ((e.Triggers & 1) == 0 && e.Hp <= e.MaxHp * st.Split1) { e.Triggers |= 1; splits++; }
                if ((e.Triggers & 2) == 0 && e.Hp <= e.MaxHp * st.Split2) { e.Triggers |= 2; splits++; }
                if (splits > 0)
                {
                    _enemies[i] = e;
                    SplitBoss(e, st, splits);
                }
            }
            i++;
        }
    }

    /// <summary>Botnet: cada divisão gera <c>splitCount</c> enxames (sem respeitar o teto em campo) na posição do chefe.</summary>
    private void SplitBoss(in TdEnemy boss, TdEnemyStats st, int splits)
    {
        int swarm = Array.FindIndex(_enemyStats, s => s.Id == "swarm");
        if (swarm < 0) return;
        var wave = WaveOf(boss.Wave);
        for (int k = 0; k < splits * st.SplitCount; k++)
        {
            _enemies.Add(MakeEnemy(swarm, boss.Wave, Math.Min(TdWorld.PathLength, boss.X + (k % st.SplitCount) * 2.0)));
            wave.Alive++;
        }
        Emit(TdEventType.BossSplit, boss.Id, splits * st.SplitCount);
    }

    private TdEnemy MakeEnemy(int defIndex, int wave, double x)
    {
        var st = _enemyStats[defIndex];
        double hp = st.IsBoss ? st.Hp : ScaledHp(st.Hp, wave);
        return new TdEnemy
        {
            Id = _nextEnemyId++, Wave = wave, DefIndex = defIndex, IsBoss = st.IsBoss, X = x, Speed = st.Speed,
            Hp = hp, MaxHp = hp, ShieldHp = st.ShieldHp, Armor = st.Armor, Gold = st.Gold, CoreDamage = st.CoreDamage, Flying = st.Flying,
            SlowMult = 1.0, AbilityCdTicks = st.ActInitialCdTicks,
        };
    }
}
