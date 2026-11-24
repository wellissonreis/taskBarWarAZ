using System.Globalization;

namespace SideWar.Core.Content.Td;

/// <summary>
/// Valida o conteúdo TD v2 (SDD-28): IDs, faixas, referências cruzadas, fraqueza declarada por torre/ramo (CA-28.4),
/// contra e fraqueza por inimigo (CA-28.5), ondas autorais e árvore de selos. Acumula todos os erros.
/// </summary>
public static class TdContentValidator
{
    public static readonly string[] Traits = ["basic", "fast", "swarm", "armored", "durable", "flying", "regen", "drain", "boss", "replicator", "shielded", "stealth"];
    private static readonly string[] RequiredTowers = ["bolt", "frost", "cannon", "balancer", "scaleset"];
    private static readonly string[] RequiredEnemies = ["grunt", "runner", "swarm", "armored", "tank", "flyer", "regen", "leech"];
    private static readonly string[] RequiredBosses = ["ransomware", "botnet", "zeroday"];
    private static readonly string[] RequiredAbilities = ["scale", "firewall", "snapshot"];
    private static readonly string[] TowerKinds = ["attack", "blocker", "support", "control", "economy", "summoner"];
    private static readonly string[] TowerTargets = ["ground", "both", "none"];
    private static readonly string[] Targetings = ["first", "last", "strong", "weak", "close"];
    private static readonly string[] AutoStartModes = ["off", "countdown"];
    private static readonly string[] EffectKinds =
        ["startingCredits", "sellRefund", "maxTowers", "damagePercent", "rangePercent", "xpShield", "abilityCooldownPercent", "unlockAbility", "autoStartBonusPercent"];

    private const int MinWaveEnemies = 1;
    private const int MaxWaveEnemies = 32;
    private const int MaxNonRestRun = 5;

    public static IReadOnlyList<string> Validate(TdContent c)
    {
        var e = new List<string>();
        var stageCount = c.Stages.Stages.Length;
        Stages(c.Stages, e);
        Enemies(c.Enemies, stageCount, e);
        Towers(c.Towers, stageCount, e);
        Counters(c.Enemies, c.Towers, e);
        Waves(c.Waves, c.Stages, c.Enemies, e);
        Abilities(c.Abilities, stageCount, c.Meta, e);
        Meta(c.Meta, c.Abilities, e);
        Runes(c.Runes, e);
        Fusion(c, e);
        Mutations(c.Mutations, e);
        return e;
    }

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static void Req(List<string> e, bool ok, string message)
    {
        if (!ok)
            e.Add(message);
    }

    private static bool Text(string? s) => !string.IsNullOrWhiteSpace(s);

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

    private static void CheckTraits(List<string> e, string owner, string[] traits, string field, bool required)
    {
        Req(e, !required || traits.Length >= 1, $"{owner}.{field} não pode ser vazio");
        foreach (var t in traits)
            Req(e, Traits.Contains(t), $"{owner}.{field}: traço desconhecido '{t}'");
    }

    // ---------------------------------------------------------------- fases e regras

    private static void Stages(TdStagesDef def, List<string> e)
    {
        var r = def.Rules;
        Req(e, r.CoreIntegrity >= 1, "td-stages.rules.coreIntegrity deve ser >= 1");
        Req(e, r.SellRefund is > 0 and <= 1, "td-stages.rules.sellRefund deve estar em (0, 1]");
        CostTable(e, r.TowerCostTable, "towerCostTable");
        CostTable(e, r.TowerCostTableLow, "towerCostTableLow");
        if (r.TowerCostTable.Length == r.TowerCostTableLow.Length)
            for (var n = 0; n < r.TowerCostTable.Length; n++)
                Req(e, r.TowerCostTableLow[n] <= r.TowerCostTable[n], $"td-stages.rules.towerCostTableLow[{n}] deve ser <= towerCostTable[{n}]");
        Req(e, r.WaveBonusBase >= 0 && r.WaveBonusPerWave >= 0, "td-stages.rules: bônus de onda deve ser >= 0");
        Req(e, r.HpWaveGrowth >= 1, "td-stages.rules.hpWaveGrowth deve ser >= 1");
        Req(e, r.HpStageStep >= 0, "td-stages.rules.hpStageStep deve ser >= 0");
        Req(e, r.SpawnJitter is >= 0 and < 1, "td-stages.rules.spawnJitter deve estar em [0, 1)");
        Req(e, r.MaxEnemiesOnField >= 1, "td-stages.rules.maxEnemiesOnField deve ser >= 1");
        Req(e, r.CallNext.BonusBase >= 0 && r.CallNext.BonusPerWave >= 0, "td-stages.rules.callNext: bônus deve ser >= 0");
        Req(e, r.CallNext.BonusCapFactor >= 1, "td-stages.rules.callNext.bonusCapFactor deve ser >= 1");
        Req(e, r.CallNext.AbilityCooldownReduction is >= 0 and <= 1, "td-stages.rules.callNext.abilityCooldownReduction deve estar em [0, 1]");
        var a = r.AutoStart;
        Req(e, AutoStartModes.Contains(a.DefaultMode), $"td-stages.rules.autoStart.defaultMode inválido '{a.DefaultMode}'");
        Req(e, a.MinCountdownSeconds > 0 && a.MinCountdownSeconds <= a.DefaultCountdownSeconds && a.DefaultCountdownSeconds <= a.MaxCountdownSeconds,
            "td-stages.rules.autoStart: 0 < min <= default <= max");
        Req(e, r.Stars.ThreeStarMaxLoss >= 0 && r.Stars.ThreeStarMaxLoss <= r.Stars.TwoStarMaxLoss && r.Stars.TwoStarMaxLoss < r.CoreIntegrity,
            "td-stages.rules.stars: 0 <= threeStarMaxLoss <= twoStarMaxLoss < coreIntegrity");
        var bq = r.BonusQuestion;
        Req(e, bq is not null, "td-stages.rules.bonusQuestion ausente");
        if (bq is not null)
        {
            Req(e, bq.TimerSeconds is >= 3 and <= 60, "td-stages.rules.bonusQuestion.timerSeconds deve estar em [3, 60]");
            Req(e, bq.CreditsBase >= 0 && bq.CreditsPerWave >= 0 && bq.CreditsBase + bq.CreditsPerWave > 0, "td-stages.rules.bonusQuestion: créditos >= 0 e com recompensa");
            Req(e, bq.CooldownReductionSeconds >= 0, "td-stages.rules.bonusQuestion.cooldownReductionSeconds deve ser >= 0");
        }
        var fr = r.Fragments;
        Req(e, fr is not null, "td-stages.rules.fragments ausente");
        if (fr is not null)
            Req(e, fr.PerWave >= 0 && fr.PerQuestion >= 0 && fr.PerBoss >= 0, "td-stages.rules.fragments: valores devem ser >= 0");

        var challenges = Ids(r.Challenges, x => x.Id, "td-stages.challenges", e);
        foreach (var ch in r.Challenges)
        {
            Req(e, Text(ch.Name) && Text(ch.Description), $"td-stages.challenges.{ch.Id}: name e description não podem ser vazios");
            Req(e, ch.MaxTowers is null or >= 1, $"td-stages.challenges.{ch.Id}.maxTowers deve ser >= 1");
            Req(e, ch.StartingCreditsFactor is > 0 and <= 1, $"td-stages.challenges.{ch.Id}.startingCreditsFactor deve estar em (0, 1]");
        }

        var stages = def.Stages;
        Req(e, stages.Length >= 1, "td-stages: ao menos uma fase");
        var regions = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < stages.Length; i++)
        {
            var s = stages[i];
            Req(e, s.Id == i + 1, $"td-stages: id da posição {i} deve ser {i + 1} (encontrado {s.Id})");
            Req(e, s.Waves >= 1, $"td-stages.{s.Id}.waves deve ser >= 1");
            Req(e, s.StartingCredits > 0, $"td-stages.{s.Id}.startingCredits deve ser > 0");
            Req(e, s.MinRank is >= 0 and <= 5, $"td-stages.{s.Id}.minRank deve estar em [0, 5]");
            Req(e, Text(s.Title) && Text(s.Region) && Text(s.Concept), $"td-stages.{s.Id}: title, region e concept não podem ser vazios");
            Req(e, regions.Add(s.Region), $"td-stages.{s.Id}.region duplicada '{s.Region}'");
            Req(e, challenges.Contains(s.Challenge), $"td-stages.{s.Id}.challenge referencia desafio inexistente '{s.Challenge}'");
        }
    }

    private const int CostTableLength = 48; // n = 0..47 (teto técnico de 48 torres, SDD-29 §5.3)

    private static void CostTable(List<string> e, int[] table, string name)
    {
        Req(e, table is not null && table.Length == CostTableLength, $"td-stages.rules.{name} deve ter {CostTableLength} entradas (n = 0..{CostTableLength - 1})");
        if (table is null || table.Length == 0)
            return;
        Req(e, table[0] == 100, $"td-stages.rules.{name}[0] deve ser 100");
        for (var n = 1; n < table.Length; n++)
            Req(e, table[n] > table[n - 1], $"td-stages.rules.{name} deve ser estritamente crescente (índice {n})");
    }

    // ---------------------------------------------------------------- inimigos

    private static void Enemies(TdEnemiesDef def, int stageCount, List<string> e)
    {
        Ids(def.Enemies.Select(x => x.Id).Concat(def.Bosses.Select(x => x.Id)), x => x, "td-enemies", e);
        foreach (var required in RequiredEnemies)
            Req(e, def.Enemies.Any(x => x.Id == required), $"td-enemies: falta o inimigo '{required}'");
        foreach (var required in RequiredBosses)
            Req(e, def.Bosses.Any(x => x.Id == required), $"td-enemies: falta o chefe '{required}'");

        foreach (var x in def.Enemies)
        {
            var o = $"td-enemies.{x.Id}";
            Req(e, x.Hp > 0, $"{o}.hp deve ser > 0");
            Req(e, x.Speed > 0, $"{o}.speed (unidades/s) deve ser > 0");
            Req(e, x.Armor >= 0, $"{o}.armor deve ser >= 0");
            Req(e, x.Gold >= 0, $"{o}.gold deve ser >= 0");
            Req(e, x.CoreDamage >= 1, $"{o}.coreDamage deve ser >= 1");
            Req(e, x.IntroStage >= 1 && x.IntroStage <= stageCount, $"{o}.introStage referencia fase inexistente");
            Req(e, Text(x.Name) && Text(x.Flavor), $"{o}: name e flavor não podem ser vazios");
            Req(e, !x.Traits.Contains("boss"), $"{o}: traço 'boss' é exclusivo de chefes");
            CheckTraits(e, o, x.Traits, "traits", true);
            TraitParams(e, o, x.Traits, x.Params, boss: false);
        }
        foreach (var b in def.Bosses)
        {
            var o = $"td-enemies.{b.Id}";
            Req(e, b.Hp > 0, $"{o}.hp deve ser > 0");
            Req(e, b.Speed > 0, $"{o}.speed (unidades/s) deve ser > 0");
            Req(e, b.Armor >= 0 && b.Gold >= 0, $"{o}: armor e gold devem ser >= 0");
            Req(e, b.CoreDamage >= 1, $"{o}.coreDamage deve ser >= 1");
            Req(e, b.Stage >= 1 && b.Stage <= stageCount, $"{o}.stage referencia fase inexistente");
            Req(e, b.ParalysisImmune, $"{o}.paralysisImmune deve ser true (chefes são imunes a paralisia)");
            Req(e, b.SlowCap is >= 0 and <= 0.2, $"{o}.slowCap deve estar em [0; 0,2]");
            Req(e, Text(b.Name) && Text(b.Flavor) && Text(b.Mechanic), $"{o}: name, flavor e mechanic não podem ser vazios");
            Req(e, b.Traits.Contains("boss"), $"{o}.traits deve conter 'boss'");
            CheckTraits(e, o, b.Traits, "traits", true);
            TraitParams(e, o, b.Traits, b.Params, boss: true);
        }
    }

    private static void TraitParams(List<string> e, string owner, string[] traits, Dictionary<string, double> p, bool boss)
    {
        if (traits.Contains("shielded")) Req(e, p.TryGetValue("shieldHp", out var shield) && double.IsFinite(shield) && shield > 0, $"{owner}: shieldHp deve ser > 0");
        if (traits.Contains("stealth")) Req(e, p.TryGetValue("revealRange", out var reveal) && double.IsFinite(reveal) && reveal > 0, $"{owner}: revealRange deve ser > 0");
        if (traits.Contains("replicator")) Req(e, p.TryGetValue("copyHpFraction", out var fraction) && double.IsFinite(fraction) && fraction > 0 && fraction <= 1, $"{owner}: copyHpFraction deve estar em (0, 1]");
        if (traits.Contains("regen"))
            Req(e, p.TryGetValue("regenPctPerSecond", out var r) && r > 0 && r <= 1, $"{owner}: traço 'regen' exige params.regenPctPerSecond em (0, 1]");
        if (traits.Contains("drain") && !boss)
            Req(e, p.TryGetValue("drainRangeUnits", out var d) && d > 0 && p.TryGetValue("disableSeconds", out var s) && s > 0,
                $"{owner}: traço 'drain' exige params.drainRangeUnits e params.disableSeconds > 0");
    }

    // ---------------------------------------------------------------- torres

    private static void Towers(TdTowerDef[] towers, int stageCount, List<string> e)
    {
        var ids = Ids(towers, x => x.Id, "td-towers", e);
        foreach (var required in RequiredTowers)
            Req(e, ids.Contains(required), $"td-towers: falta a torre '{required}'");
        foreach (var t in towers)
        {
            var o = $"td-towers.{t.Id}";
            Req(e, Text(t.Name) && Text(t.Flavor) && Text(t.Role), $"{o}: name, flavor e role não podem ser vazios");
            Req(e, new[] { "attack", "control", "support", "economy" }.Contains(t.Category), $"{o}.category inválida '{t.Category}'");
            Req(e, TowerKinds.Contains(t.Kind), $"{o}.kind inválido '{t.Kind}'");
            Req(e, TowerTargets.Contains(t.Targets), $"{o}.targets inválido '{t.Targets}'");
            Req(e, t.Cost > 0, $"{o}.cost deve ser > 0");
            Req(e, t.Range > 0, $"{o}.range (unidades) deve ser > 0");
            Req(e, t.UnlockStage >= 1 && t.UnlockStage <= stageCount, $"{o}.unlockStage referencia fase inexistente");
            if (t.Kind == "attack")
            {
                Req(e, t.Damage > 0 && t.Interval is >= 0.1 and <= 5, $"{o}: torre de ataque exige damage > 0 e interval em [0,1; 5] s");
                Req(e, t.DefaultTargeting is not null && Targetings.Contains(t.DefaultTargeting), $"{o}.defaultTargeting inválido");
                Req(e, t.Targets != "none", $"{o}: torre de ataque não pode ter targets 'none'");
            }
            else if (t.Kind == "control" && t.Targets != "none")
            {
                Req(e, t.DefaultTargeting is not null && Targetings.Contains(t.DefaultTargeting), $"{o}.defaultTargeting inválido");
                Req(e, t.Interval > 0, $"{o}.interval deve ser > 0");
            }
            else
            {
                Req(e, t.DefaultTargeting is null, $"{o}.defaultTargeting deve ser nulo (ignora mira)");
            }
            Req(e, t.Kind switch
            {
                "support" or "economy" or "summoner" => t.Targets == "none",
                "blocker" => t.Targets == "ground",
                "control" => true,
                _ => t.Targets != "none"
            }, $"{o}: targets incompatível com kind");
            if (t.Stats.TryGetValue("slowFactor", out var slow))
                Req(e, t.Kind == "attack" && slow is > 0 and < 1, $"{o}: slowFactor inválido");
            if (t.Id == "frontdoor")
                Req(e, t.Kind == "blocker" && t.Stats.TryGetValue("nodes", out var nodes) && nodes == 1
                    && t.Stats.TryGetValue("nodeHolds", out var holds) && holds == 1
                    && t.Stats.TryGetValue("nodeDamagePerSecond", out var nodeDps) && nodeDps > 0
                    && t.Stats.TryGetValue("nodeCooldownSeconds", out var nodeCooldown) && nodeCooldown > 0, $"{o}: stats de nó inválidos");
            if (t.Id == "functions")
                Req(e, t.Stats.TryGetValue("burstShots", out var shots) && shots >= 1 && shots == Math.Floor(shots)
                    && t.Stats.TryGetValue("burstGapTicks", out var gap) && gap >= 1 && gap == Math.Floor(gap)
                    && t.Stats.TryGetValue("rearmSeconds", out var rearm) && rearm > 0, $"{o}: stats de rajada inválidos");
            if (t.Id == "cdn")
                Req(e, t.Stats.TryGetValue("minRange", out var min) && min >= 0 && min < t.Range, $"{o}: minRange inválido");
            if (t.Id == "policy")
                Req(e, t.Stats.TryGetValue("armorRemoveBp", out var bp) && bp is >= 0 and <= 10000, $"{o}: armorRemoveBp inválido");

            if (t.Stats.ContainsKey("chainJumps"))
                Req(e, t.Stats.TryGetValue("chainJumps", out var jumps) && jumps >= 0 && jumps == Math.Floor(jumps)
                    && t.Stats.TryGetValue("chainRadius", out var radius) && radius > 0
                    && t.Stats.TryGetValue("chainFalloffBp", out var falloff) && falloff is >= 0 and <= 10000, $"{o}: stats de cadeia inválidos");
            if (t.Id == "sentinel")
                Req(e, t.Stats.TryGetValue("markBonusBp", out var mark) && mark >= 0
                    && t.Stats.TryGetValue("markSeconds", out var duration) && duration > 0
                    && t.Stats.TryGetValue("markMax", out var max) && max == 1, $"{o}: stats de marca inválidos");
            if (t.Id == "queue")
                Req(e, t.Stats.TryGetValue("pushbackUnits", out var push) && push > 0
                    && t.Stats.TryGetValue("bossPushbackFactorBp", out var boss) && boss is >= 0 and <= 10000
                    && t.Stats.TryGetValue("pushGraceSeconds", out var grace) && grace > 0, $"{o}: stats de empurrão inválidos");
            if (t.Id == "pipeline")
                Req(e, t.Stats.TryGetValue("rampPerHitBp", out var ramp) && ramp >= 0
                    && t.Stats.TryGetValue("rampCapBp", out var cap) && cap >= 0
                    && t.Stats.TryGetValue("rampKeepsBetweenTargets", out var keeps) && keeps == 0, $"{o}: stats de rampa inválidos");

            if (t.Stats.TryGetValue("maxPerPlayer", out var limit))
                Req(e, limit >= 1 && limit == Math.Floor(limit), $"{o}: maxPerPlayer inválido");
            if (t.Id == "vault")
                Req(e, t.Kind == "economy" && t.Stats.TryGetValue("maxPerPlayer", out var vaultLimit) && vaultLimit == 2
                    && t.Stats.TryGetValue("creditsPerWave", out var credits) && credits >= 0 && credits == Math.Floor(credits)
                    && t.Stats.TryGetValue("noLeakBonus", out var noLeak) && noLeak >= 0 && noLeak == Math.Floor(noLeak), $"{o}: stats de economia inválidos");
            if (t.Stats.TryGetValue("neighborDamageBonus", out var aura))
                Req(e, t.Kind == "support" && aura is > 0 and <= 1, $"{o}: bônus de aura inválido");
            if (t.Id == "bastion")
                Req(e, t.Kind == "support" && t.Stats.TryGetValue("reviveEverySeconds", out var bastionRevive) && bastionRevive > 0, $"{o}: reativação inválida");
            if (t.Id == "backup")
                Req(e, t.Kind == "support" && t.Stats.TryGetValue("reviveEverySeconds", out var revive) && revive > 0
                    && t.Stats.TryGetValue("coreHealEverySeconds", out var heal) && heal > 0
                    && t.Stats.TryGetValue("coreHealCapPerStage", out var healCap) && healCap >= 0 && healCap == Math.Floor(healCap), $"{o}: stats de backup inválidos");
            if (t.Id == "aks")
                Req(e, t.Kind == "summoner" && t.Stats.TryGetValue("pods", out var pods) && pods >= 1 && pods == Math.Floor(pods)
                    && t.Stats.TryGetValue("podDps", out var dps) && dps > 0
                    && t.Stats.TryGetValue("podLifeSeconds", out var life) && life > 0
                    && t.Stats.TryGetValue("triggerEnemies", out var trigger) && trigger >= 1 && trigger == Math.Floor(trigger)
                    && t.Stats.TryGetValue("interval", out var interval) && interval > 0, $"{o}: stats de pods inválidos");

            // Fraqueza declarada da torre (CA-28.4).
            Req(e, Text(t.WeaknessText), $"{o}.weaknessText: toda torre declara sua fraqueza");
            CheckTraits(e, o, t.Weak, "weak", true);
            CheckTraits(e, o, t.Strong, "strong", true);
            Req(e, !t.Strong.Intersect(t.Weak).Any(), $"{o}: um traço não pode ser forte e fraco ao mesmo tempo");
        }
        var set = towers.FirstOrDefault(t => t.Id == "scaleset");
        Req(e, set is null || set.Kind == "support", "td-towers.scaleset deve ser do tipo support");
        var bal = towers.FirstOrDefault(t => t.Id == "balancer");
        Req(e, bal is null || bal.Kind == "blocker", "td-towers.balancer deve ser do tipo blocker");
    }

    // ---------------------------------------------------------------- matriz de contra-escolha (CA-28.5)

    private static void Counters(TdEnemiesDef def, TdTowerDef[] towers, List<string> e)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in towers)
        {
            refs.Add(t.Id);
        }
        // Traços que alguma torre ou ramo declara como fraqueza (o "sofre" da matriz).
        var suffered = towers.SelectMany(t => t.Weak).ToHashSet(StringComparer.Ordinal);

        void Check(string id, string[] traits, string[] counters)
        {
            var o = $"td-enemies.{id}";
            Req(e, counters.Length >= 1, $"{o}.counters: todo inimigo tem ao menos um contra");
            foreach (var c in counters)
                Req(e, refs.Contains(c), $"{o}.counters: torre/ramo '{c}' não existe em towers");
            var meaningful = traits.Where(t => t != "basic").ToArray();
            Req(e, meaningful.Length == 0 || meaningful.Any(suffered.Contains),
                $"{o}: nenhuma torre ou ramo declara fraqueza contra os traços do inimigo ({string.Join(", ", meaningful)})");
        }
        foreach (var x in def.Enemies)
            Check(x.Id, x.Traits, x.Counters);
        foreach (var b in def.Bosses)
            Check(b.Id, b.Traits, b.Counters);
    }

    // ---------------------------------------------------------------- ondas autorais

    private static void Waves(TdWavesDef def, TdStagesDef stages, TdEnemiesDef enemies, List<string> e)
    {
        var intro = enemies.Enemies.ToDictionary(x => x.Id, x => x.IntroStage, StringComparer.Ordinal);
        var bossStage = enemies.Bosses.ToDictionary(x => x.Id, x => x.Stage, StringComparer.Ordinal);

        Req(e, def.Stages.Length == stages.Stages.Length, $"td-waves: deve haver uma entrada por fase ({stages.Stages.Length})");
        for (var si = 0; si < def.Stages.Length; si++)
        {
            var sw = def.Stages[si];
            var o = $"td-waves.{sw.Stage}";
            Req(e, sw.Stage == si + 1, $"td-waves: fase da posição {si} deve ser {si + 1} (encontrado {sw.Stage})");
            if (si < stages.Stages.Length)
                Req(e, sw.Waves.Length == stages.Stages[si].Waves, $"{o}: {sw.Waves.Length} ondas, mas td-stages declara {stages.Stages[si].Waves}");
            var run = 0;
            var bossesInStage = new HashSet<string>(StringComparer.Ordinal);
            for (var wi = 0; wi < sw.Waves.Length; wi++)
            {
                var w = sw.Waves[wi];
                var wo = $"{o}.{w.Number}";
                var last = wi == sw.Waves.Length - 1;
                Req(e, w.Number == wi + 1, $"{o}: número da onda da posição {wi} deve ser {wi + 1}");
                Req(e, w.BonusFactor > 0, $"{wo}.bonusFactor deve ser > 0");
                Req(e, !(w.Rest && last), $"{wo}: a última onda da fase não pode ser de respiro");
                Req(e, !w.Rest || w.BonusFactor > 1, $"{wo}: onda de respiro deve pagar bônus maior (bonusFactor > 1)");
                run = w.Rest ? 0 : run + 1;
                Req(e, run <= MaxNonRestRun, $"{wo}: mais de {MaxNonRestRun} ondas seguidas sem respiro");
                Req(e, w.Groups.Length >= 1, $"{wo}: ao menos um grupo");
                var total = 0;
                foreach (var g in w.Groups)
                {
                    total += g.Count;
                    Req(e, g.Count >= 1 && g.Offset >= 0 && g.Interval > 0, $"{wo}.{g.Enemy}: count >= 1, offset >= 0 e interval > 0");
                    if (intro.TryGetValue(g.Enemy, out var from))
                        Req(e, from <= sw.Stage, $"{wo}: '{g.Enemy}' só é introduzido na fase {from}");
                    else if (bossStage.TryGetValue(g.Enemy, out var bs))
                    {
                        Req(e, bs == sw.Stage, $"{wo}: chefe '{g.Enemy}' pertence à fase {bs}");
                        Req(e, last, $"{wo}: chefe '{g.Enemy}' só aparece na última onda da fase");
                        Req(e, g.Count == 1, $"{wo}: chefe '{g.Enemy}' deve vir em quantidade 1");
                        bossesInStage.Add(g.Enemy);
                    }
                    else
                        e.Add($"{wo}: inimigo '{g.Enemy}' não existe em td-enemies");
                }
                Req(e, total >= MinWaveEnemies && total <= MaxWaveEnemies, $"{wo}: {total} inimigos fora de [{MinWaveEnemies}, {MaxWaveEnemies}]");
            }
            Req(e, sw.Waves.Any(w => w.Rest), $"{o}: a fase precisa de ao menos uma onda de respiro");
            foreach (var (id, bs) in bossStage)
                if (bs == sw.Stage)
                    Req(e, bossesInStage.Contains(id), $"{o}: chefe '{id}' deve aparecer na última onda da fase {bs}");
        }
    }

    // ---------------------------------------------------------------- habilidades

    private static void Abilities(TdAbilityDef[] abilities, int stageCount, TdMetaDef meta, List<string> e)
    {
        var ids = Ids(abilities, x => x.Id, "td-abilities", e);
        foreach (var required in RequiredAbilities)
            Req(e, ids.Contains(required), $"td-abilities: falta a habilidade '{required}'");
        var nodes = meta.Branches.SelectMany(b => b.Nodes).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var a in abilities)
        {
            var o = $"td-abilities.{a.Id}";
            Req(e, Text(a.Name) && Text(a.Effect), $"{o}: name e effect não podem ser vazios");
            Req(e, a.CooldownSeconds >= 0 && a.DurationSeconds >= 0, $"{o}: cooldownSeconds e durationSeconds devem ser >= 0");
            Req(e, a.UsesPerStage is null or >= 1, $"{o}.usesPerStage deve ser >= 1");
            Req(e, (a.UnlockStage is null) != (a.UnlockMetaNode is null), $"{o}: exatamente um entre unlockStage e unlockMetaNode");
            Req(e, a.UnlockStage is null || (a.UnlockStage >= 1 && a.UnlockStage <= stageCount), $"{o}.unlockStage referencia fase inexistente");
            Req(e, a.UnlockMetaNode is null || nodes.Contains(a.UnlockMetaNode), $"{o}.unlockMetaNode referencia nó inexistente '{a.UnlockMetaNode}'");
            Req(e, a.UsesPerStage is not null || a.CooldownSeconds > 0, $"{o}: sem usesPerStage exige cooldownSeconds > 0");
        }
    }

    // ---------------------------------------------------------------- selos e árvore

    private static void Meta(TdMetaDef meta, TdAbilityDef[] abilities, List<string> e)
    {
        Req(e, meta.SealsPerStar >= 1 && meta.SealsPerChallenge >= 1, "td-meta: sealsPerStar e sealsPerChallenge devem ser >= 1");
        Req(e, meta.Branches.Length == 3, "td-meta: a árvore tem exatamente 3 ramos");
        Ids(meta.Branches, b => b.Id, "td-meta.branches", e);
        Ids(meta.Branches.SelectMany(b => b.Nodes), n => n.Id, "td-meta.nodes", e);
        var abilityIds = abilities.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var b in meta.Branches)
        {
            Req(e, Text(b.Name), $"td-meta.{b.Id}.name vazio");
            Req(e, b.Nodes.Length == 4, $"td-meta.{b.Id}: o ramo tem exatamente 4 nós");
            for (var i = 0; i < b.Nodes.Length; i++)
            {
                var n = b.Nodes[i];
                var o = $"td-meta.{n.Id}";
                Req(e, n.Cost == i + 1, $"{o}.cost deve ser {i + 1} (custos 1 a 4 em ordem)");
                Req(e, Text(n.Name) && n.Effects.Length >= 1, $"{o}: name e ao menos um efeito são obrigatórios");
                foreach (var ef in n.Effects)
                {
                    Req(e, EffectKinds.Contains(ef.Kind), $"{o}: efeito desconhecido '{ef.Kind}'");
                    Req(e, ef.Value > 0, $"{o}.{ef.Kind}.value deve ser > 0");
                    if (ef.Kind == "unlockAbility")
                        Req(e, ef.Target is not null && abilityIds.Contains(ef.Target), $"{o}: unlockAbility referencia habilidade inexistente '{ef.Target}'");
                    if (ef.Kind == "sellRefund")
                        Req(e, ef.Value <= 1, $"{o}.sellRefund deve ser <= 1 (fração)");
                }
            }
        }
        var unlockedByNode = meta.Branches.SelectMany(b => b.Nodes).SelectMany(n => n.Effects.Select(ef => (n.Id, ef)))
            .Where(x => x.ef.Kind == "unlockAbility").ToDictionary(x => x.ef.Target!, x => x.Id, StringComparer.Ordinal);
        foreach (var a in abilities.Where(a => a.UnlockMetaNode is not null))
            Req(e, unlockedByNode.TryGetValue(a.Id, out var node) && node == a.UnlockMetaNode,
                $"td-abilities.{a.Id}: o nó '{a.UnlockMetaNode}' deve ter o efeito unlockAbility '{a.Id}'");
    }

    // ---------------------------------------------------------------- teia de runas (SDD-29 §21, TD.29a)

    /// <summary>Efeitos que o Core sabe aplicar; cada tipo de runa do JSON aponta para um deles.</summary>
    internal static readonly string[] RuneEffects =
        ["rangeBp", "boltDamageBp", "waveCredits", "frostSlowTenths", "xpShield", "cannonDamageBp", "abilityCooldownBp", "firewallTenths"];
    internal const int WebRings = 8;
    internal const int WebSectors = 16;

    private static void Runes(TdRunesDef r, List<string> e)
    {
        const string f = "td-runes";
        Ids(r.Types, x => x.Id, $"{f}.types", e);
        Req(e, r.Types.Length == RuneEffects.Length, $"{f}.types: exatamente {RuneEffects.Length} tipos");
        foreach (var t in r.Types)
        {
            Req(e, Text(t.Name) && Text(t.Text), $"{f}.types.{t.Id}: name e text não podem ser vazios");
            Req(e, RuneEffects.Contains(t.Effect), $"{f}.types.{t.Id}.effect desconhecido '{t.Effect}'");
            Req(e, t.Value != 0, $"{f}.types.{t.Id}.value deve ser != 0");
        }
        Req(e, r.Types.Select(t => t.Effect).Distinct(StringComparer.Ordinal).Count() == r.Types.Length, $"{f}.types: um tipo por efeito");
        var typeIds = r.Types.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        Ids(r.Nodes, x => x.Id, $"{f}.nodes", e);
        Req(e, r.Nodes.Length == WebRings * WebSectors, $"{f}.nodes: esperado {WebRings * WebSectors} nós (encontrado {r.Nodes.Length})");
        foreach (var n in r.Nodes)
        {
            var o = $"{f}.{n.Id}";
            Req(e, n.Ring is >= 0 and < WebRings && n.Sector is >= 0 and < WebSectors, $"{o}: anel/setor fora da teia");
            Req(e, n.Id == $"{n.Ring}-{n.Sector}", $"{o}: id deve ser 'anel-setor'");
            Req(e, typeIds.Contains(n.Type), $"{o}.type referencia tipo inexistente '{n.Type}'");
            Req(e, n.X is >= 0 and <= 1 && n.Y is >= 0 and <= 1, $"{o}: x/y normalizados em [0, 1]");
            Req(e, n.Power is >= 1 and <= 10, $"{o}.power deve estar em [1, 10]");
        }
        for (int ring = 0; ring < WebRings; ring++)
            for (int sector = 0; sector < WebSectors; sector++)
                Req(e, r.Nodes.Count(n => n.Ring == ring && n.Sector == sector) == 1, $"{f}: falta exatamente um nó em {ring}-{sector}");

        Req(e, r.RingCosts is { Length: WebRings } && r.RingCosts.All(c => c >= 1), $"{f}.ringCosts: {WebRings} custos (um por anel), cada um >= 1");
        var cs = r.CreditSources;
        Req(e, cs.StartingCreditsBonus >= 0 && cs.WaveBonusBase >= 0 && cs.WaveBonusPerWave >= 0 && cs.NoLeakBase >= 0 && cs.NoLeakPerWave >= 0
               && cs.BonusQuestionBase >= 0 && cs.BonusQuestionPerWave >= 0 && cs.BossCredits >= 0,
            $"{f}.creditSources: todos os valores devem ser >= 0");
    }

    // ---------------------------------------------------------------- fusão de torres (SDD-37)

    public static readonly string[] FusionRoles = ["attack", "control", "support", "economy"];
    public static readonly string[] FusionEffects = ["hitDamage", "hitSlow", "hitSplash", "hitChain", "hitCorrode", "nodeSlow", "periodicDamage", "periodicSlow"];

    private static void Fusion(TdContent c, List<string> e)
    {
        var f = c.Fusion;
        var towers = c.Towers.ToDictionary(t => t.Id, StringComparer.Ordinal);
        Req(e, f.MinStage >= 1 && f.MinStage <= c.Stages.Stages.Length, "td-fusion.minStage referencia fase inexistente");
        Req(e, f.ResolveOrder is ["base", "module", "aura"], "td-fusion.resolveOrder deve ser [base, module, aura]");

        var roleOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (role, ids) in f.Roles)
        {
            Req(e, FusionRoles.Contains(role), $"td-fusion.roles: papel desconhecido '{role}'");
            foreach (var id in ids)
            {
                Req(e, towers.ContainsKey(id), $"td-fusion.roles.{role}: torre inexistente '{id}'");
                Req(e, roleOf.TryAdd(id, role), $"td-fusion.roles: torre '{id}' em mais de um papel");
            }
        }
        foreach (var t in c.Towers)
            Req(e, roleOf.ContainsKey(t.Id), $"td-fusion.roles: torre '{t.Id}' sem papel");
        foreach (var t in c.Towers)
            if (roleOf.GetValueOrDefault(t.Id) == "attack")
                Req(e, t.Damage > 0, $"td-fusion.roles.attack: '{t.Id}' sem dano positivo não pode ser módulo de ataque");

        double G(string role, string key) => f.Generic.TryGetValue(role, out var d) && d.TryGetValue(key, out var v) ? v : 0;
        Req(e, G("attack", "intervalSeconds") > 0 && G("attack", "damagePct") > 0, "td-fusion.generic.attack: intervalSeconds e damagePct devem ser > 0");
        Req(e, G("control", "intervalSeconds") > 0 && G("control", "slowFactor") is > 0 and < 1 && G("control", "slowSeconds") > 0, "td-fusion.generic.control: intervalSeconds, slowSeconds > 0 e slowFactor em (0, 1)");
        Req(e, G("support", "damageAuraBp") > 0, "td-fusion.generic.support: damageAuraBp deve ser > 0");
        Req(e, G("economy", "incomePct") is > 0 and <= 1, "td-fusion.generic.economy: incomePct deve estar em (0, 1]");

        var nm = f.Names;
        if (nm is not null)
        {
            Req(e, nm.Same.Contains("{noun}", StringComparison.Ordinal), "td-fusion.names.same deve conter {noun}");
            foreach (var t in c.Towers)
            {
                Req(e, nm.Gender.TryGetValue(t.Id, out var g) && g is "m" or "f", $"td-fusion.names.gender: torre '{t.Id}' sem gênero m/f");
                Req(e, nm.Adjective.TryGetValue(t.Id, out var a) && a.Length is 1 or 2 && a.All(Text), $"td-fusion.names.adjective: torre '{t.Id}' sem adjetivo");
            }
            foreach (var o in nm.Overrides)
                Req(e, towers.ContainsKey(o.Base ?? "") && towers.ContainsKey(o.Module ?? "") && Text(o.Name), $"td-fusion.names.overrides.{o.Base}+{o.Module}: torre inexistente ou nome vazio");
        }

        var seen = new HashSet<(string, string)>();
        foreach (var p in f.Pairs)
        {
            var o = $"td-fusion.pairs.{p.Base}+{p.Module}";
            bool bOk = towers.TryGetValue(p.Base ?? "", out var b);
            bool mOk = towers.TryGetValue(p.Module ?? "", out var m);
            Req(e, bOk && mOk, $"{o}: torre inexistente");
            Req(e, Text(p.Name), $"{o}.name não pode ser vazio");
            Req(e, seen.Add((p.Base ?? "", p.Module ?? "")), $"{o}: par duplicado");
            Req(e, FusionEffects.Contains(p.Effect), $"{o}.effect desconhecido '{p.Effect}'");
            foreach (var (k, v) in p.Params)
                Req(e, v > 0 || (k == "rangeFromModule" && v >= 0), $"{o}.params.{k} deve ser positivo");
            double P(string k) => p.Params.GetValueOrDefault(k);
            if (!bOk || !mOk) continue;
            switch (p.Effect)
            {
                case "hitDamage": Req(e, P("damagePct") > 0 && b!.Kind == "attack" && m!.Damage > 0, $"{o}: hitDamage exige damagePct > 0, base de ataque e módulo com dano"); break;
                case "hitSlow": Req(e, b!.Kind == "attack" && m!.Stats.GetValueOrDefault("slowFactor") > 0, $"{o}: hitSlow exige base de ataque e módulo com slowFactor"); break;
                case "hitSplash": Req(e, b!.Kind == "attack" && m!.Stats.GetValueOrDefault("splashRadius") > 0 && m.Stats.GetValueOrDefault("splashFactor") > 0, $"{o}: hitSplash exige base de ataque e módulo com splashRadius/splashFactor"); break;
                case "hitChain": Req(e, P("jumps") >= 1 && P("jumps") == Math.Floor(P("jumps")) && P("damagePct") > 0 && b!.Kind == "attack" && m!.Stats.GetValueOrDefault("chainRadius") > 0, $"{o}: hitChain exige jumps inteiro >= 1, damagePct > 0, base de ataque e módulo com chainRadius"); break;
                case "hitCorrode": Req(e, P("armorPct") is > 0 and <= 1 && P("seconds") > 0 && b!.Kind == "attack", $"{o}: hitCorrode exige armorPct em (0, 1], seconds > 0 e base de ataque"); break;
                case "nodeSlow": Req(e, b!.Kind == "blocker" && m!.Stats.GetValueOrDefault("slowFactor") > 0, $"{o}: nodeSlow exige base blocker e módulo com slowFactor"); break;
                case "periodicDamage": Req(e, P("intervalSeconds") > 0 && P("damagePct") > 0 && m!.Damage > 0, $"{o}: periodicDamage exige intervalSeconds, damagePct > 0 e módulo com dano"); break;
                case "periodicSlow": Req(e, P("intervalSeconds") > 0 && m!.Stats.GetValueOrDefault("slowFactor") > 0, $"{o}: periodicSlow exige intervalSeconds > 0 e módulo com slowFactor"); break;
            }
        }
    }

    // ---------------------------------------------------------------- mutações de inimigos (SDD-37 §4)

    /// <summary>Máximo de traços (a máscara por inimigo é um <c>ushort</c>).</summary>
    public const int MaxMutationTraits = 16;

    private static void Mutations(TdMutationsDef m, List<string> e)
    {
        const string f = "td-mutations";
        Req(e, m.RewardPerTrait >= 1 && m.RewardCap >= 1 && m.RewardCap <= 2, $"{f}: rewardPerTrait >= 1 e rewardCap em [1, 2]");
        Req(e, m.SpeedMin > 0 && m.SpeedMax > m.SpeedMin, $"{f}: speedMin > 0 e speedMax > speedMin");
        Req(e, m.DoubleGapWaves >= 1 && m.DoubleMinMutated >= 2 && m.MinEligibleForOne >= 1, $"{f}: doubleGapWaves >= 1, doubleMinMutated >= 2 e minEligibleForOne >= 1");
        Req(e, m.Traits.Length is >= 2 and <= MaxMutationTraits, $"{f}.traits: de 2 a {MaxMutationTraits} traços");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in m.Traits)
            Req(e, Text(t.Id) && ids.Add(t.Id), $"{f}.traits: id vazio ou duplicado '{t.Id}'");
        foreach (var t in m.Traits)
        {
            var o = $"{f}.traits.{t.Id}";
            Req(e, Text(t.Name) && Text(t.Label), $"{o}: name e label não podem ser vazios");
            Req(e, t.MinStage >= 1, $"{o}.minStage deve ser >= 1");
            Req(e, t.HpMult > 0 && t.SpeedMult > 0 && t.ArmorAdd >= 0 && t.ShieldPct >= 0, $"{o}: hpMult e speedMult > 0; armorAdd e shieldPct >= 0");
            Req(e, t.HpMult != 1 || t.SpeedMult != 1 || t.ArmorAdd > 0 || t.ShieldPct > 0 || t.Flying, $"{o}: traço sem efeito");
            foreach (var x in t.Excludes)
            {
                Req(e, x != t.Id && ids.Contains(x), $"{o}.excludes: '{x}' inexistente ou o próprio traço");
                var other = m.Traits.FirstOrDefault(y => y.Id == x);
                Req(e, other is null || other.Excludes.Contains(t.Id), $"{o}.excludes: exclusão com '{x}' deve ser simétrica");
            }
        }
        Req(e, m.Tiers.Length >= 1, $"{f}.tiers: pelo menos um degrau");
        for (int i = 0; i < m.Tiers.Length; i++)
        {
            var t = m.Tiers[i];
            var o = $"{f}.tiers[{i}]";
            Req(e, t.MinStage >= 1 && t.MinWaves >= 0, $"{o}: minStage >= 1 e minWaves >= 0");
            Req(e, t.MaxMutatedPct is > 0 and <= 0.3, $"{o}.maxMutatedPct deve estar em (0, 0,3] (pelo menos 70% sem mutação)");
            Req(e, t.MaxDoubles >= -1 && t.DoublePct is >= 0 and <= 0.25, $"{o}: maxDoubles >= -1 e doublePct em [0, 0,25]");
            if (i > 0)
                Req(e, t.MinStage >= m.Tiers[i - 1].MinStage && t.MinWaves >= m.Tiers[i - 1].MinWaves && t.MaxMutatedPct >= m.Tiers[i - 1].MaxMutatedPct,
                    $"{o}: degraus devem ser crescentes (fase, ondas e percentual)");
        }
    }
}
