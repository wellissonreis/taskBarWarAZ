using System.Globalization;

namespace SideWar.Core.Content;

/// <summary>Valida faixas, IDs, referências cruzadas e fatores fixos (SDD-12 RF-12.9, SDD-27 RF-27.14).</summary>
public static class ContentValidator
{
    private static readonly string[] RequiredTowers = ["bolt", "frost", "cannon", "scaleset"];
    private static readonly string[] RequiredEnemies = ["grunt", "runner", "swarm", "armored", "tank", "boss"];
    private static readonly string[] UnlockKinds = ["always", "firstMigration", "reserves"];
    private static readonly string[] RewardKinds = ["chestRare", "incomeMinutes", "reserves", "xp"];

    public static IReadOnlyList<string> Validate(GameContent c)
    {
        var e = new List<string>();
        Rules(c.Rules, e);
        var kinds = Enemies(c.Enemies, e);
        Stages(c.Stages, c.Waves.BossEvery, e);
        Towers(c.Towers, kinds, c.Stages, e);
        Waves(c.Waves, kinds, e);
        Regions(c.Regions, e);
        Missions(c.Missions, e);
        Economy(c.Economy, e);
        return e;
    }

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static void Req(List<string> e, bool ok, string message)
    {
        if (!ok)
            e.Add(message);
    }

    private static void Rules(RulesDef r, List<string> e)
    {
        Req(e, r.MaxEnemiesOnField >= 1, "rules.maxEnemiesOnField deve ser >= 1");
        Req(e, r.SlotRangeDip > 0, "rules.slotRangeDip deve ser > 0");
        Req(e, r.SplashRadius > 0, "rules.splashRadius deve ser > 0");
        Req(e, r.SplashFactor is > 0 and <= 1, "rules.splashFactor deve estar em (0, 1]");
        Req(e, r.ChilledBonus >= 1, "rules.chilledBonus deve ser >= 1");
        Req(e, r.CannonVsRunner is > 0 and <= 1, "rules.cannonVsRunner deve estar em (0, 1]");
        Req(e, r.FrostSlowFactor is > 0 and < 1, "rules.frostSlowFactor deve estar em (0, 1)");
        Req(e, r.FrostSlowFloor is > 0 and < 1, "rules.frostSlowFloor deve estar em (0, 1)");
        Req(e, r.BoltSuperHitEvery >= 2, "rules.boltSuperHitEvery deve ser >= 2");
        Req(e, r.BoltSuperHitMultiplier >= 1, "rules.boltSuperHitMultiplier deve ser >= 1");
    }

    private static HashSet<string> Ids<T>(IEnumerable<T> items, Func<T, string> id, string file, List<string> e)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var value = id(item);
            if (string.IsNullOrWhiteSpace(value))
                e.Add($"{file}: id vazio");
            else if (!set.Add(value))
                e.Add($"{file}: id duplicado '{value}'");
        }
        return set;
    }

    private static HashSet<string> Enemies(EnemyDef[] enemies, List<string> e)
    {
        var ids = Ids(enemies, x => x.Id, "enemies", e);
        foreach (var required in RequiredEnemies)
            Req(e, ids.Contains(required), $"enemies: falta o inimigo '{required}'");
        foreach (var x in enemies)
        {
            Req(e, x.Hp > 0, $"enemies.{x.Id}.hp deve ser > 0");
            Req(e, x.Gold >= 0, $"enemies.{x.Id}.gold deve ser >= 0");
            Req(e, x.Xp >= 0, $"enemies.{x.Id}.xp deve ser >= 0");
            Req(e, x.Armor >= 0, $"enemies.{x.Id}.armor deve ser >= 0");
            Req(e, x.CoreDamage >= 1, $"enemies.{x.Id}.coreDamage deve ser >= 1");
            Req(e, x.Speed > 0, $"enemies.{x.Id}.speed (DIP/s) deve ser > 0");
            Req(e, !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Sprite),
                $"enemies.{x.Id}: name e sprite não podem ser vazios");
        }
        return ids;
    }

    private static void Stages(StageDef[] stages, int bossEvery, List<string> e)
    {
        Req(e, stages.Length >= 1, "stages: ao menos uma fase");
        for (var i = 0; i < stages.Length; i++)
        {
            var s = stages[i];
            Req(e, s.Id == i + 1, $"stages: id da posição {i} deve ser {i + 1} (encontrado {s.Id})");
            Req(e, s.Waves >= 1, $"stages.{s.Id}.waves deve ser >= 1");
            Req(e, bossEvery <= 0 || s.Waves % bossEvery == 0,
                $"stages.{s.Id}.waves ({s.Waves}) deve ser múltiplo de bossEvery ({bossEvery}): a última onda é de chefe");
            Req(e, !string.IsNullOrWhiteSpace(s.Title) && !string.IsNullOrWhiteSpace(s.StageTopic),
                $"stages.{s.Id}: title e stageTopic não podem ser vazios");
        }
    }

    private static void Towers(TowerDef[] towers, HashSet<string> kinds, StageDef[] stages, List<string> e)
    {
        var ids = Ids(towers, x => x.Id, "towers", e);
        foreach (var required in RequiredTowers)
            Req(e, ids.Contains(required), $"towers: falta a torre '{required}'");
        foreach (var t in towers)
        {
            Req(e, t.Cost > 0, $"towers.{t.Id}.cost deve ser > 0");
            Req(e, t.RangeSlots >= 1, $"towers.{t.Id}.rangeSlots deve ser >= 1");
            Req(e, t.Damage > 0, $"towers.{t.Id}.damage deve ser > 0");
            Req(e, t.Interval is >= 0.1 and <= 5, $"towers.{t.Id}.interval deve estar em [0,1; 5] s");
            Req(e, t.UnlockStage is null || (t.UnlockStage >= 1 && t.UnlockStage <= stages.Length),
                $"towers.{t.Id}.unlockStage referencia fase inexistente");
            Req(e, t.DamageBonusPerBolt is null or > 0, $"towers.{t.Id}.damageBonusPerBolt deve ser > 0");
            Req(e, !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Description),
                $"towers.{t.Id}: name e description não podem ser vazios");
            foreach (var k in t.Strong.Concat(t.Weak))
                Req(e, kinds.Contains(k), $"towers.{t.Id}: kind '{k}' não existe em enemies");
        }
        var set = towers.FirstOrDefault(t => t.Id == "scaleset");
        Req(e, set is null || set.DamageBonusPerBolt is not null, "towers.scaleset.damageBonusPerBolt é obrigatório");
    }

    private static void Waves(WavesDef w, HashSet<string> kinds, List<string> e)
    {
        Req(e, w.Count.Base >= 1 && w.Count.PerWaves >= 1, "waves.count: base e perWaves devem ser >= 1");
        Req(e, w.Count.Cap >= w.Count.Base, "waves.count.cap deve ser >= base");
        Req(e, w.Interval.Start > 0 && w.Interval.Floor > 0 && w.Interval.Floor <= w.Interval.Start,
            "waves.interval: start > 0, floor > 0 e floor <= start");
        Req(e, w.BossEvery >= 1, "waves.bossEvery deve ser >= 1");
        Req(e, w.BossRegularFactor is > 0 and <= 1, "waves.bossRegularFactor deve estar em (0, 1]");
        Req(e, w.BossRegularMin >= 1, "waves.bossRegularMin deve ser >= 1");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        double capSum = 0;
        foreach (var m in w.Mix)
        {
            Req(e, kinds.Contains(m.Kind), $"waves.mix: kind '{m.Kind}' não existe em enemies");
            Req(e, m.Kind is not ("grunt" or "boss"), $"waves.mix: '{m.Kind}' não pode ser grupo de mistura (grunt é o resto; boss é inserido)");
            Req(e, seen.Add(m.Kind), $"waves.mix: kind duplicado '{m.Kind}'");
            Req(e, m.Start >= 0 && m.Slope > 0 && m.Cap is > 0 and <= 1,
                $"waves.mix.{m.Kind}: start >= 0, slope > 0 e cap em (0, 1]");
            capSum += m.Cap;
        }
        // Σ dos grupos não pode exceder a quantidade (RF-27.14): garantido por Σ cap <= 1.
        Req(e, capSum <= 1, $"waves.mix: soma dos cap ({F(capSum)}) excede 1 (excederia a quantidade da onda)");
    }

    private static void Regions(RegionDef[] regions, List<string> e)
    {
        Ids(regions, x => x.Id, "regions", e);
        Req(e, regions.Any(r => r.Unlock == "always"), "regions: ao menos uma região com unlock 'always'");
        foreach (var r in regions)
        {
            Req(e, r.HpMultiplier > 0 && r.ReservesMultiplier > 0, $"regions.{r.Id}: multiplicadores devem ser > 0");
            Req(e, UnlockKinds.Contains(r.Unlock), $"regions.{r.Id}.unlock inválido '{r.Unlock}'");
            Req(e, r.Unlock != "reserves" || r.UnlockReserves is > 0, $"regions.{r.Id}: unlock 'reserves' exige unlockReserves > 0");
            Req(e, !string.IsNullOrWhiteSpace(r.Name), $"regions.{r.Id}.name vazio");
        }
    }

    private static void Missions(MissionsDef m, List<string> e)
    {
        Ids(m.Missions, x => x.Id, "missions", e);
        Req(e, m.DailyCount >= 1 && m.DailyCount <= m.Missions.Length, "missions.dailyCount deve estar em [1, nº de missões]");
        foreach (var x in m.Missions)
        {
            Req(e, x.Target > 0, $"missions.{x.Id}.target deve ser > 0");
            Req(e, RewardKinds.Contains(x.Reward.Kind), $"missions.{x.Id}.reward.kind inválido '{x.Reward.Kind}'");
            Req(e, x.Reward.Amount > 0, $"missions.{x.Id}.reward.amount deve ser > 0");
            Req(e, !string.IsNullOrWhiteSpace(x.Title), $"missions.{x.Id}.title vazio");
        }
    }

    private static void Factor(List<string> e, string name, double actual, double expected) =>
        Req(e, actual == expected, $"economy.{name} deve ser {F(expected)} (encontrado {F(actual)})");

    private static void Economy(EconomyDef x, List<string> e)
    {
        Factor(e, "buildCostFactor", x.BuildCostFactor, 1.5);
        Factor(e, "upgradeCostFactor", x.UpgradeCostFactor, 1.2);
        Factor(e, "upgradeCostGrowth", x.UpgradeCostGrowth, 1.18);
        Factor(e, "damageGrowth", x.DamageGrowth, 1.12);
        Factor(e, "hpGrowth", x.HpGrowth, 1.09);
        Factor(e, "goldGrowth", x.GoldGrowth, 1.07);
        Factor(e, "xpGrowth", x.XpGrowth, 1.04);
        Req(e, x.StartingCredits > 0, "economy.startingCredits deve ser > 0");
        Req(e, x.MaxTowerLevel is >= 1 and <= 999, "economy.maxTowerLevel deve estar em [1, 999]");
        Req(e, x.StageHpStep >= 0, "economy.stageHpStep deve ser >= 0");
        Req(e, x.XpPerLevel == 45, "economy.xpPerLevel deve ser 45");
        Req(e, x.Core.BaseHp == 100 && x.Core.HpPerLevel == 5, "economy.core deve ser 100 + 5 por nível");
        Req(e, x.FallWaves == 5, "economy.fallWaves deve ser 5");
        Req(e, x.WaveRestSeconds > 0, "economy.waveRestSeconds deve ser > 0");
        Req(e, x.RateWindowSeconds >= 1, "economy.rateWindowSeconds deve ser >= 1");
        Req(e, x.ReservesBonus > 0, "economy.reservesBonus deve ser > 0");
        Req(e, x.Prestige.MinWave >= 1 && x.Prestige.Divisor >= 1, "economy.prestige: minWave e divisor devem ser >= 1");
        foreach (var (name, chest) in new[] { ("common", x.Chests.Common), ("rare", x.Chests.Rare) })
            Req(e, chest.IncomeMinutes >= 0 && chest.Xp >= 0 && chest.Reserves >= 0, $"economy.chests.{name}: valores devem ser >= 0");
    }
}
