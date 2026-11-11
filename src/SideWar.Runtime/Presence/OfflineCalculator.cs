using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Core.Waves;

namespace SideWar.Runtime.Presence;

/// <summary>
/// Cálculo offline analítico (SDD-25 RF-25.6 a RF-25.8, ADR-022). Puro: sem Rng, sem relógio, sem I/O
/// (o log sai em <see cref="OfflineResult.Logs"/>). Só <c>+ − × ÷</c>, <c>floor</c>/<c>ceil</c>, sem <c>Math.Pow</c>.
/// </summary>
public sealed class OfflineCalculator
{
    private readonly GameContent _content;
    private readonly OfflineConfig _cfg;
    private readonly PresenceMultiplierConfig _mult;
    private readonly IdleWaveBuilder _builder;
    private readonly Dictionary<TowerType, TowerDef> _defs = new();

    public OfflineCalculator(GameContent content, OfflineConfig? config = null, PresenceMultiplierConfig? multiplier = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _cfg = config ?? OfflineConfig.Default;
        _mult = multiplier ?? PresenceMultiplierConfig.Default;
        _builder = new IdleWaveBuilder(content);
        foreach (var t in content.Towers)
            if (SimIds.TryTowerType(t.Id, out var type)) _defs[type] = t;
    }

    /// <summary>Critério de vitória (RF-25.7): <c>DPS × (duração × 0,8) ≥ HPtotal</c>.</summary>
    public bool IsWon(double dps, double waveDuration, double hpTotal) => dps * (waveDuration * _cfg.WinFactor) >= hpTotal;

    /// <summary>DPS analítico (RF-25.7): só torres com slot; sem super hit, chilled, lentidão nem área; Scale Set usa o bônus de Raios.</summary>
    public double ComputeDps(IReadOnlyList<OfflineTower> towers, IReadOnlyList<int> levels)
    {
        var bolts = 0;
        for (var i = 0; i < towers.Count; i++)
            if (towers[i].Slot is not null && towers[i].Type == TowerType.Bolt) bolts++;

        var dps = 0.0;
        for (var i = 0; i < towers.Count; i++)
        {
            var t = towers[i];
            if (t.Slot is null || !_defs.TryGetValue(t.Type, out var def)) continue;
            var dmg = def.Damage;
            if (t.Type == TowerType.ScaleSet) dmg *= 1 + (def.DamageBonusPerBolt ?? 0.0) * bolts;
            for (var l = 1; l < levels[i]; l++) dmg *= _content.Economy.DamageGrowth;
            dps += dmg / def.Interval;
        }
        return dps;
    }

    public OfflineResult Compute(OfflineSnapshot s, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(s);
        var stageIdx = s.Stage - 1;
        if (stageIdx < 0 || stageIdx >= _content.Stages.Length) throw new ArgumentException("Fase inválida.", nameof(s));
        var total = _content.Stages[stageIdx].Waves;
        if (s.Wave < 1 || s.Wave > total) throw new ArgumentException("Onda inválida.", nameof(s));
        var region = _content.Regions.FirstOrDefault(r => r.Id == s.RegionId)
            ?? throw new ArgumentException("Região inválida.", nameof(s));

        var logs = new List<string>();
        var rawElapsed = (nowUtc - s.SavedUtc).TotalSeconds;
        if (!(rawElapsed > 0))
        {
            logs.Add("offline: clock skew");
            return Empty(s, logs);
        }
        var elapsed = Math.Min(rawElapsed, _cfg.MaxElapsedSeconds);

        var eco = _content.Economy;
        var towers = s.Towers;
        var levels = towers.Select(t => t.Level).ToArray();
        var dps = ComputeDps(towers, levels);
        var creditMult = (1 + eco.ReservesBonus * s.Reservas) * PresenceMultiplier.Evaluate(PresenceState.Offline, 0, _mult);
        var xpMult = PresenceMultiplier.Evaluate(PresenceState.Offline, 0, _mult);

        var cache = new Dictionary<int, WaveInfo>();
        WaveInfo Info(int w)
        {
            if (cache.TryGetValue(w, out var wi)) return wi;
            var plan = _builder.Build(w, total, stageIdx, region.HpMultiplier);
            double hp = 0, gold = 0, xp = 0;
            foreach (var sp in plan.Spawns) { hp += sp.Hp; gold += sp.Gold; xp += sp.Xp; }
            var dur = plan.Count * plan.Interval + _cfg.WaveSlackSeconds;
            return cache[w] = new WaveInfo(hp, gold, xp, dur, dur + eco.WaveRestSeconds);
        }

        double remaining = elapsed, accum = s.AutoScaleAccumSeconds, creditVal = 0, xpVal = 0;
        long spent = 0;
        var wave = s.Wave;
        var advanced = 0;
        var farmWave = 0;
        var ups = new SortedDictionary<int, (TowerType Type, int From, int To)>();
        var autoOn = s.AutoScaleOn;
        var eligible = towers.Any(t => t.Slot is not null);

        long Earned() => ToLong(Math.Floor(creditVal + s.Carry));

        void Round()
        {
            var working = s.Credits + Earned() - spent;
            var budget = AutoScaler.Budget(working, _cfg.AutoScaleBudgetFraction);
            // Regra única (T6.2): o offline só monta candidatos (torres com slot) e agrega os passos por torre.
            var cands = new List<AutoScaleCandidate>(towers.Count);
            var index = new List<int>(towers.Count);
            for (var i = 0; i < towers.Count; i++)
            {
                if (towers[i].Slot is null || !_defs.TryGetValue(towers[i].Type, out var def)) continue;
                cands.Add(new AutoScaleCandidate(towers[i].Id, towers[i].Type, levels[i], def.Cost));
                index.Add(i);
            }
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cands);
            var plan = AutoScaler.Plan(span, eco, budget);
            spent += plan.Spent;
            foreach (var step in plan.Upgrades)
                ups[step.TowerId] = ups.TryGetValue(step.TowerId, out var u) ? (u.Type, u.From, step.ToLevel) : (step.Type, step.FromLevel, step.ToLevel);
            for (var k = 0; k < span.Length; k++) levels[index[k]] = span[k].Level;
            dps = ComputeDps(towers, levels);
        }

        void Tick(double t)
        {
            if (!autoOn || !eligible) return;
            accum += t;
            while (accum >= _cfg.AutoScaleIntervalSeconds)
            {
                accum -= _cfg.AutoScaleIntervalSeconds;
                Round();
            }
        }

        Tick(0);
        while (remaining > 0)
        {
            var isBoss = _builder.IsBossWave(wave);
            if (advanced < _cfg.MaxWaves && !isBoss)
            {
                var info = Info(wave);
                if (IsWon(dps, info.Duration, info.Hp) && info.Consume <= remaining)
                {
                    creditVal += info.Gold * creditMult;
                    xpVal += info.Xp * xpMult;
                    remaining -= info.Consume;
                    farmWave = wave;
                    wave++;
                    advanced++;
                    Tick(info.Consume);
                    continue;
                }
            }

            // Farm (RF-25.7): última onda vencida, ou w−1 se vencida; senão nada (DP-25.4).
            var f = farmWave;
            if (f == 0 && wave - 1 >= 1)
            {
                var prev = Info(wave - 1);
                if (IsWon(dps, prev.Duration, prev.Hp)) f = wave - 1;
            }

            var recurring = autoOn && eligible;
            var chunk = recurring ? Math.Min(remaining, _cfg.AutoScaleIntervalSeconds - accum) : remaining;
            if (f > 0)
            {
                var fi = Info(f);
                var frac = chunk / fi.Consume;
                creditVal += frac * fi.Gold * creditMult;
                xpVal += frac * fi.Xp * xpMult;
            }
            else if (!recurring)
            {
                logs.Add("offline: no wave won");
            }
            remaining -= chunk;
            if (!recurring) break;
            Tick(chunk);
        }

        var gross = Earned();
        var carry = creditVal + s.Carry - gross;
        var xpGain = ToLong(Math.Floor(xpVal));
        var credits0 = s.Credits;
        var fromBalance = Math.Min(spent, credits0);
        var excess = spent - fromBalance;
        var pending = Math.Max(0, gross - excess);

        var level = s.Level;
        var xp = s.Xp + xpGain;
        LevelRules.Advance(ref level, ref xp, eco.XpPerLevel);

        var upgrades = ups.Values.Select(u => new AutoUpgrade(u.Type, u.From, u.To)).ToList();
        var nUps = ups.Values.Sum(u => u.To - u.From);
        var minutes = elapsed / 60.0;
        var stoppedAtBoss = _builder.IsBossWave(wave);
        var newTowers = towers.Select((t, i) => t with { Level = levels[i] }).ToList();
        var state = s with
        {
            SavedUtc = nowUtc, Wave = wave, WaveMax = Math.Max(s.WaveMax, wave), Credits = credits0 - fromBalance,
            Carry = carry, Towers = newTowers, AutoScaleAccumSeconds = autoOn && eligible ? accum : s.AutoScaleAccumSeconds,
        };
        var report = new OfflineReport(
            elapsed, gross, s.Wave, wave, 0, xpGain, s.Level, level, upgrades, nUps, spent,
            (int)Math.Floor(minutes / _cfg.PeakDivisorMinutes), (int)Math.Floor(minutes / _cfg.BoxDivisorMinutes),
            stoppedAtBoss ? 1 : 0, stoppedAtBoss);
        return new OfflineResult(report, state, pending, xpGain, elapsed >= _cfg.CardMinSeconds, logs);
    }

    private static OfflineResult Empty(OfflineSnapshot s, List<string> logs)
    {
        var report = new OfflineReport(0, 0, s.Wave, s.Wave, 0, 0, s.Level, s.Level, [], 0, 0, 0, 0, 0, false);
        return new OfflineResult(report, s, 0, 0, false, logs);
    }

    private static long ToLong(double v) => v >= long.MaxValue ? long.MaxValue : v <= 0 ? 0 : (long)v;

    private readonly record struct WaveInfo(double Hp, double Gold, double Xp, double Duration, double Consume);
}
