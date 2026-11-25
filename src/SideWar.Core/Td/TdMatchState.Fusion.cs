using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>
/// Resultado da checagem de uma fusão (API da UI, SDD-37 RF-37.10): motivo (None = elegível), custo exato do módulo, nome, se a combinação é nomeada,
/// quantas torres o resultado teria (<c>Count</c>) e o máximo da patente atual (<c>Limit</c>).
/// </summary>
public readonly record struct TdFusionCheck(CommandReason Reason, long Cost, long Missing, string Name, bool Named, string Effect, int Count = 0, int Limit = 0)
{
    public bool IsOk => Reason == CommandReason.None;
}

/// <summary>
/// Fusão de torres (SDD-37 TD.37a): base (chassi, mira, alcance e cadência) mais 1 a 5 módulos; o máximo de torres cresce com a patente. Cada módulo traz o
/// efeito do par base+módulo (<c>td-fusion.json</c>) ou o genérico do papel (RF-37.5); de 3 torres em diante as receitas contidas no conjunto somam um bônus
/// (o melhor valor de cada efeito, então acrescentar uma torre nunca enfraquece). Ordem fixa de resolução: base, módulos, receita, aura.
/// </summary>
public sealed partial class TdMatchState
{
    /// <summary>Fusão liberada nesta corrida (fase absoluta mínima do conteúdo; não volta a bloquear).</summary>
    public bool FusionUnlocked => CurrentRankId >= _content.Fusion.MinRank;

    /// <summary>Máximo de torres numa fusão na patente <paramref name="rank"/> (conteúdo sem a tabela: 2).</summary>
    public int FusionLimitAt(int rank) =>
        _content.Fusion.MaxComponentsByRank is { Length: > 0 } t ? t[Math.Clamp(rank, 0, t.Length - 1)] : 2;

    /// <summary>Máximo de torres numa fusão na patente atual.</summary>
    public int FusionMaxComponents => FusionLimitAt(CurrentRankId);

    /// <summary>Primeira patente que permite <paramref name="count"/> torres (e que já liberou a fusão).</summary>
    public int FusionRankFor(int count)
    {
        for (int r = Math.Max(0, _content.Fusion.MinRank); r <= 5; r++)
            if (FusionLimitAt(r) >= count) return r;
        return 5;
    }

    /// <summary>Papel da torre na fusão: attack | control | support | economy.</summary>
    public string FusionRole(int defIndex)
    {
        string id = _content.Towers[defIndex].Id;
        foreach (var (role, ids) in _content.Fusion.Roles)
            if (Array.IndexOf(ids, id) >= 0) return role;
        return "attack";
    }

    /// <summary>Par nomeado base+módulo (nulo = regra genérica).</summary>
    public TdFusionPairDef? FusionPair(int baseDefIndex, int moduleDefIndex)
    {
        string b = _content.Towers[baseDefIndex].Id, m = _content.Towers[moduleDefIndex].Id;
        foreach (var p in _content.Fusion.Pairs)
            if (p.Base == b && p.Module == m) return p;
        return null;
    }

    /// <summary>Nome próprio da combinação base+módulo (par nomeado, nome avulso ou composição de td-fusion.json "names"); nunca "A + B".</summary>
    public string FusionName(int baseDefIndex, int moduleDefIndex)
    {
        if (FusionPair(baseDefIndex, moduleDefIndex) is { } pair) return pair.Name;
        var b = _content.Towers[baseDefIndex];
        var mo = _content.Towers[moduleDefIndex];
        if (_content.Fusion.Names is not { } names) return $"{b.Name} + {mo.Name}"; // conteúdo sem "names" (fixtures antigas): rótulo genérico
        foreach (var o in names.Overrides)
            if (o.Base == b.Id && o.Module == mo.Id) return o.Name;
        if (b.Id == mo.Id) return names.Same.Replace("{noun}", b.Name, StringComparison.Ordinal);
        bool fem = names.Gender.TryGetValue(b.Id, out var g) && g == "f";
        if (!names.Adjective.TryGetValue(mo.Id, out var adj) || adj.Length == 0) return $"{b.Name} + {mo.Name}";
        return $"{b.Name} {(fem && adj.Length > 1 ? adj[1] : adj[0])}";
    }

    /// <summary>
    /// Nome de um conjunto (base primeiro): 2 torres = nome do par; 3 ou mais = a maior receita contida (com "+n" quando sobram torres);
    /// sem receita, o nome do par base + primeiro módulo com "+n".
    /// </summary>
    public string FusionNameFor(IReadOnlyList<int> components)
    {
        if (components.Count == 1) return _content.Towers[components[0]].Name;
        if (RecipeBonus(components) is { } bonus)
        {
            int rest = components.Count - bonus.Primary.Parts.Length;
            return rest > 0 ? $"{bonus.Primary.Name} +{rest}" : bonus.Primary.Name;
        }
        string pair = FusionName(components[0], components[1]);
        return components.Count > 2 ? $"{pair} +{components.Count - 2}" : pair;
    }

    /// <summary>Nome do híbrido (ver <see cref="FusionNameFor"/>); o nome da torre se simples.</summary>
    public string TowerDisplayName(in TdTower t) => FusionNameFor(t.Components());

    /// <summary>Quantas componentes de cada tipo existem (inclui módulos e torres SEM POSIÇÃO): base de custo e limite por tipo.</summary>
    public int ComponentCount(int defIndex) => TowerCount(defIndex);

    /// <summary>Checa, sem alterar nada, se <paramref name="componentTypeId"/> pode ser fundido sobre a torre (mesma ordem de validação do comando).</summary>
    public TdFusionCheck CheckFusion(int baseTowerId, string? componentTypeId)
    {
        int bi = FindTower(baseTowerId);
        int mi = componentTypeId is null ? -1 : TowerDefIndex(componentTypeId);
        if (bi < 0 || mi < 0) return new(CommandReason.TowerNotFound, 0, 0, "", false, "");
        var t = _towers[bi];
        int[] components = [.. t.Components(), mi];
        var (name, named, effect) = Describe(components);
        long cost = NextTowerCost(mi);
        TdFusionCheck R(CommandReason r, long missing = 0) => new(r, cost, missing, name, named, effect, components.Length, FusionMaxComponents);
        if (!Running) return R(CommandReason.InvalidPhase);
        if (!t.Placed) return R(CommandReason.NotPlaced);
        var rank = CheckComponents(components);
        if (rank != CommandReason.None) return R(rank);
        var def = _content.Towers[mi];
        if (def.Stats.TryGetValue("maxPerPlayer", out var limit) && TowerCount(mi) >= limit) return R(CommandReason.TowerTypeLimit);
        if (Credits < cost) return R(CommandReason.InsufficientCredits, cost - Credits);
        return R(CommandReason.None);
    }

    /// <summary>Regras de patente comuns à compra e ao arrasto: fusão liberada, teto absoluto, limite da patente e cada componente liberada.</summary>
    private CommandReason CheckComponents(int[] components)
    {
        if (!FusionUnlocked) return CommandReason.FusionLocked;
        int top = _content.Fusion.MaxComponentsByRank is { Length: > 0 } table ? table[^1] : 2;
        if (components.Length > top) return CommandReason.AlreadyFused;
        if (components.Length > FusionMaxComponents) return CommandReason.FusionLimit;
        foreach (int d in components)
            if (CurrentRankId < _content.Towers[d].MinRank) return CommandReason.TowerLocked;
        return CommandReason.None;
    }

    private (string Name, bool Named, string Effect) Describe(int[] components)
    {
        string name = FusionNameFor(components);
        if (components.Length == 2)
        {
            var pair = FusionPair(components[0], components[1]);
            return (name, pair is not null, pair?.Effect ?? "generic:" + FusionRole(components[1]));
        }
        return RecipeBonus(components) is null
            ? (name, false, "generic:" + FusionRole(components[^1]))
            : (name, true, "recipe");
    }

    /// <summary>
    /// Quem é a base ao arrastar <paramref name="sourceId"/> sobre <paramref name="targetId"/>: a de maior custo de tipo (empate: a que já tem mais torres; depois o alvo).
    /// Devolve (base, módulo) ou (-1, -1) se inválido.
    /// </summary>
    public (int BaseId, int ModuleId, CommandReason Reason) CheckFusionTowers(int targetId, int sourceId)
    {
        int ti = FindTower(targetId), si = FindTower(sourceId);
        if (ti < 0 || si < 0 || targetId == sourceId) return (-1, -1, CommandReason.TowerNotFound);
        var t = _towers[ti];
        var s = _towers[si];
        if (!Running) return (-1, -1, CommandReason.InvalidPhase);
        if (!t.Placed || !s.Placed) return (-1, -1, CommandReason.NotPlaced);
        var reason = CheckComponents([.. t.Components(), .. s.Components()]);
        if (reason != CommandReason.None) return (-1, -1, reason);
        return SourceIsBase(t, s) ? (sourceId, targetId, CommandReason.None) : (targetId, sourceId, CommandReason.None);
    }

    private bool SourceIsBase(in TdTower target, in TdTower source)
    {
        long tc = _content.Towers[target.DefIndex].Cost, sc = _content.Towers[source.DefIndex].Cost;
        return sc > tc || (sc == tc && source.ComponentTotal > target.ComponentTotal);
    }

    private Rng _demotionRng;
    private int _fusionLossCount;
    private List<int> _lastFusionLoss = [];

    /// <summary>Sorteio injetável (testes): recebe N e devolve 0..N-1. Nulo = gerador próprio da partida (não consome o RNG principal).</summary>
    public Func<int, int>? DemotionPick { get; set; }
    /// <summary>Quantos rebaixamentos já destruíram componentes de fusão.</summary>
    public int FusionLossCount => _fusionLossCount;
    /// <summary>Definições (índices de torre) perdidas no último rebaixamento.</summary>
    public IReadOnlyList<int> LastFusionLoss => _lastFusionLoss;

    /// <summary>Rebaixamento: sorteia e destrói componentes de fusão acima do limite da nova patente (a base fica; sem reembolso).</summary>
    private void TrimFusionsToRank()
    {
        int limit = Math.Max(1, FusionMaxComponents);
        var lost = new List<int>();
        for (int i = 0; i < _towers.Count; i++)
        {
            var t = _towers[i];
            if (t.ComponentTotal <= limit) continue;
            var parts = new List<TdFusionPart>(t.ModuleCount);
            for (int k = 0; k < t.ModuleCount; k++) parts.Add(new(t.ModuleDef(k), t.ModulePriceAt(k)));
            while (parts.Count + 1 > limit)
            {
                int pick = Math.Clamp(NextDemotionPick(parts.Count), 0, parts.Count - 1);
                lost.Add(parts[pick].DefIndex);
                Emit(TdEventType.FusionLost, t.Id, parts[pick].DefIndex);
                parts.RemoveAt(pick);
            }
            t.ModuleSlot = 0;
            t.ModulePrice = 0;
            t.ExtraModules = null;
            t.ModuleCdTicks = 0;
            if (parts.Count > 0) AddModules(ref t, parts);
            _towers[i] = t;
        }
        if (lost.Count == 0) return;
        _lastFusionLoss = lost;
        _fusionLossCount++;
    }

    private int NextDemotionPick(int n)
    {
        if (DemotionPick is { } pick) return pick(n);
        if (_demotionRng.Inc == 0) _demotionRng = Rng.Seed(_mutSeed, 97);
        return (int)(_demotionRng.NextUInt() % (uint)n);
    }

    /// <summary>Preview completo de um arrasto (nome, quantidade resultante e limite), mesmo quando bloqueado. Arrastar não cobra.</summary>
    public TdFusionCheck CheckFusionDrag(int targetId, int sourceId)
    {
        var (_, _, reason) = CheckFusionTowers(targetId, sourceId);
        int ti = FindTower(targetId), si = FindTower(sourceId);
        if (ti < 0 || si < 0 || targetId == sourceId) return new(reason, 0, 0, "", false, "", 0, FusionMaxComponents);
        var t = _towers[ti];
        var s = _towers[si];
        bool swap = SourceIsBase(t, s);
        int[] components = swap ? [.. s.Components(), .. t.Components()] : [.. t.Components(), .. s.Components()];
        var (name, named, effect) = Describe(components);
        return new(reason, 0, 0, name, named, effect, components.Length, FusionMaxComponents);
    }

    /// <summary>Nome do híbrido de um arrasto (a base é a mais forte); vazio se inválido.</summary>
    public string FusionNameOfTowers(int targetId, int sourceId)
    {
        var c = CheckFusionDrag(targetId, sourceId);
        return c.IsOk ? c.Name : "";
    }

    private CommandResult DoFuseTowers(FuseTowers cmd)
    {
        var (baseId, moduleId, reason) = CheckFusionTowers(cmd.TargetId, cmd.SourceId);
        if (reason != CommandReason.None) return Reject(reason);
        int bi = FindTower(baseId), mi = FindTower(moduleId);
        var module = _towers[mi];
        var t = _towers[bi];
        var added = new List<TdFusionPart> { new(module.DefIndex, module.PurchasePrice) };
        for (int k = 0; k < module.ModuleCount; k++) added.Add(new(module.ModuleDef(k), module.ModulePriceAt(k)));
        AddModules(ref t, added);
        _towers[bi] = t;
        _nodes.RemoveAll(n => n.TowerId == moduleId);
        _towers.RemoveAt(mi);
        Emit(TdEventType.TowerFused, baseId, 0);
        return CommandResult.Ok;
    }

    private CommandResult DoFuse(FuseTower cmd)
    {
        var check = CheckFusion(cmd.BaseId, cmd.ComponentTypeId);
        if (!check.IsOk) return Reject(check.Reason, check.Missing);
        int bi = FindTower(cmd.BaseId);
        var t = _towers[bi];
        Credits -= check.Cost;
        AddModules(ref t, [new TdFusionPart(TowerDefIndex(cmd.ComponentTypeId), check.Cost)]);
        _towers[bi] = t;
        Emit(TdEventType.TowerFused, t.Id, check.Cost);
        return CommandResult.Ok;
    }

    private void AddModules(ref TdTower t, List<TdFusionPart> added)
    {
        int from = 0;
        if (!t.HasModule)
        {
            t.ModuleSlot = added[0].DefIndex + 1;
            t.ModulePrice = added[0].Price;
            from = 1;
        }
        if (added.Count > from) t.ExtraModules = [.. t.ExtraModules ?? [], .. added.Skip(from)];
        t.ModuleCdTicks = ModuleIntervalTicks(t);
    }

    /// <summary>Intervalo (ticks) das ações periódicas (módulos e receita); 0 se nada age por temporizador (efeitos por acerto, aura, renda).</summary>
    private int ModuleIntervalTicks(in TdTower t)
    {
        if (!t.HasModule) return 0;
        int interval = 0;
        for (int k = 0; k < t.ModuleCount; k++)
        {
            int m = t.ModuleDef(k), ticks = 0;
            var pair = FusionPair(t.DefIndex, m);
            if (pair is not null)
                ticks = pair.Effect is "periodicDamage" or "periodicSlow" ? Ticks(pair.Params["intervalSeconds"]) : 0;
            else if (FusionRole(m) is "attack" or "control")
                ticks = Ticks(_content.Fusion.Generic[FusionRole(m)]["intervalSeconds"]);
            if (ticks > 0 && (interval == 0 || ticks < interval)) interval = ticks;
        }
        if (interval == 0 && RecipePulses(t)) interval = Ticks(_content.Fusion.Generic["attack"]["intervalSeconds"]);
        return interval;
    }

    /// <summary>Base que não atira (bloqueio, suporte, economia, invocador): os efeitos "por acerto" da receita saem num pulso periódico.</summary>
    private bool RecipePulses(in TdTower t) =>
        _content.Towers[t.DefIndex].Kind != "attack" && RecipeBonus(t) is { HasHitEffects: true };

    /// <summary>Valor de venda de uma torre: por componente, <c>min(preço pago, SellValueAt(tipo, contagem antes da remoção))</c>, base depois módulos, decrementando a contagem virtual (RF-37.3).</summary>
    private long HybridSellValue(in TdTower t)
    {
        var counts = new Dictionary<int, int>();
        int Count(int d) => counts.TryGetValue(d, out var c) ? c : TowerCount(d);
        long sum = Math.Min(t.PurchasePrice, SellValueAt(t.DefIndex, Count(t.DefIndex)));
        counts[t.DefIndex] = Count(t.DefIndex) - 1;
        for (int k = 0; k < t.ModuleCount; k++)
        {
            int d = t.ModuleDef(k);
            sum += Math.Min(t.ModulePriceAt(k), SellValueAt(d, Count(d)));
            counts[d] = Count(d) - 1;
        }
        return sum;
    }

    // ---- ações periódicas (por torre, em FireTowers) -------------------------------------------------------------------

    private void TickModule(ref TdTower t, int ti)
    {
        if (t.ModuleCdTicks > 0) return;
        int interval = ModuleIntervalTicks(t);
        if (interval <= 0) return;
        bool acted = false;
        for (int k = 0; k < t.ModuleCount; k++) acted |= ModuleAction(t, ti, t.ModuleDef(k));
        if (RecipePulses(t)) acted |= RecipePulse(t, ti);
        if (acted) t.ModuleCdTicks = interval; // sem alvo não consome recarga
    }

    private bool ModuleAction(in TdTower t, int ti, int moduleDefIndex)
    {
        var baseDef = _content.Towers[t.DefIndex];
        var mdef = _content.Towers[moduleDefIndex];
        var pair = FusionPair(t.DefIndex, moduleDefIndex);
        string role;
        if (pair is null) role = FusionRole(moduleDefIndex);
        else if (pair.Effect == "periodicDamage") role = "attack";
        else if (pair.Effect == "periodicSlow") role = "control";
        else return false;
        if (role is not ("attack" or "control")) return false;
        var p = pair?.Params;
        bool moduleRange = pair is null || p!["rangeFromModule"] > 0;
        double range = moduleRange ? EffectiveRange(mdef) : EffectiveRange(baseDef) + _auraRange[ti];
        // Atinge voadores só se o módulo atinge (generic control: ambos, salvo módulo de solo). Mira da base se existir, senão primeiro (empates por Id, em PickTarget).
        var scan = mdef with { Targets = mdef.Targets == "ground" ? "ground" : "both" };
        var mode = baseDef.Kind == "attack" || (baseDef.Kind == "control" && baseDef.Targets != "none") ? t.Targeting : (byte)TdTargeting.First;
        var probe = t;
        probe.Targeting = mode;
        int target = PickTarget(probe, scan, range);
        if (target < 0) return false;
        _damageSource = t.Id;
        if (role == "attack")
        {
            double pct = pair is null ? _content.Fusion.Generic["attack"]["damagePct"] : p!["damagePct"];
            var enemy = _enemies[target];
            DealDamage(target, ComputeDamage(t, mdef, enemy, _enemyStats[enemy.DefIndex], ti, pct, 1.0));
        }
        else if (pair is null)
        {
            var g = _content.Fusion.Generic["control"];
            ApplySlow(target, g["slowFactor"], Ticks(g["slowSeconds"]) + FrostRuneTicks(mdef));
        }
        else
        {
            ApplySlow(target, Stat(t, mdef, "slowFactor", 1.0), SlowDurationTicks + FrostRuneTicks(mdef));
        }
        return true;
    }

    private int FrostRuneTicks(TdTowerDef def) => def.Id == "frost" ? Rune("frostSlowTenths") * 3 : 0;

    /// <summary>Pulso da receita: mesmo conjunto de efeitos do acerto, num alvo no maior alcance entre as componentes.</summary>
    private bool RecipePulse(in TdTower t, int ti)
    {
        var strong = StrongestAttack(t);
        double range = 0;
        foreach (int d in t.Components()) range = Math.Max(range, EffectiveRange(_content.Towers[d]));
        var scan = _content.Towers[t.DefIndex] with { Targets = "both" };
        var probe = t;
        probe.Targeting = (byte)TdTargeting.First;
        int target = PickTarget(probe, scan, range + _auraRange[ti]);
        if (target < 0) return false;
        _damageSource = t.Id;
        ApplyRecipeOnHit(t, strong, ti, target);
        return true;
    }

    /// <summary>Componente de maior dano: referência dos efeitos de dano da receita (empate: menor índice, independe da ordem de fusão).</summary>
    private TdTowerDef StrongestAttack(in TdTower t)
    {
        int best = t.DefIndex;
        foreach (int d in t.Components())
            if (_content.Towers[d].Damage > _content.Towers[best].Damage || (_content.Towers[d].Damage == _content.Towers[best].Damage && d < best)) best = d;
        return _content.Towers[best];
    }

    // ---- efeitos por acerto (base atira) -----------------------------------------------------------------------------

    private void ApplyModuleOnHit(in TdTower t, TdTowerDef baseDef, int ti, int target)
    {
        if (!t.HasModule) return;
        for (int k = 0; k < t.ModuleCount; k++) ApplyPairOnHit(t, baseDef, ti, target, t.ModuleDef(k));
        if (baseDef.Kind == "attack") ApplyRecipeOnHit(t, StrongestAttack(t), ti, target);
    }

    private void ApplyPairOnHit(in TdTower t, TdTowerDef baseDef, int ti, int target, int moduleDefIndex)
    {
        var pair = FusionPair(t.DefIndex, moduleDefIndex);
        if (pair is null || _enemies[target].Hp <= 0) return;
        var mdef = _content.Towers[moduleDefIndex];
        var p = pair.Params;
        var e = _enemies[target];
        switch (pair.Effect)
        {
            case "hitDamage":
                DealDamage(target, ComputeDamage(t, mdef, e, _enemyStats[e.DefIndex], ti, p["damagePct"], 1.0));
                break;
            case "hitSlow":
                ApplySlow(target, Stat(t, mdef, "slowFactor", 1.0), SlowDurationTicks + FrostRuneTicks(mdef));
                break;
            case "hitSplash":
                Splash(t, baseDef, ti, target, Stat(t, mdef, "splashRadius", 0) * Stat(t, mdef, "splashRadiusMultiplier", 1.0), Stat(t, mdef, "splashFactor", 0));
                break;
            case "hitChain":
                Chain(t, baseDef, ti, target, (int)p["jumps"], Stat(t, mdef, "chainRadius", 0), p["damagePct"]);
                break;
            case "hitCorrode":
                Corrode(target, p["armorPct"], p["seconds"]);
                break;
        }
    }

    private void ApplyRecipeOnHit(in TdTower t, TdTowerDef damageDef, int ti, int target)
    {
        if (RecipeBonus(t) is not { HasHitEffects: true } r) return;
        var e = _enemies[target];
        if (e.Hp <= 0) return;
        // Dano da receita atinge o que a base atinge; base sem mira própria (suporte, economia) atinge os dois.
        var scan = damageDef with { Targets = "both" };
        if (r.SlowFactor > 0) ApplySlow(target, r.SlowFactor, Ticks(r.SlowSeconds));
        if (r.CorrodePct > 0) Corrode(target, r.CorrodePct, r.CorrodeSeconds);
        if (r.HitDamagePct > 0) DealDamage(target, ComputeDamage(t, scan, _enemies[target], _enemyStats[e.DefIndex], ti, r.HitDamagePct, 1.0));
        if (r.SplashPct > 0) Splash(t, scan, ti, target, r.SplashRadius, r.SplashPct);
        if (r.ChainJumps > 0) Chain(t, scan, ti, target, r.ChainJumps, ChainRadius, r.ChainPct);
    }

    private const double ChainRadius = 60;

    private void Splash(in TdTower t, TdTowerDef def, int ti, int target, double radius, double factor)
    {
        var e = _enemies[target];
        for (int j = 0; j < _enemies.Count; j++)
        {
            var o = _enemies[j];
            if (j == target || o.Flying || o.Hp <= 0 || o.InvulnTicks > 0 || Dist(o.X, e.X) > radius) continue;
            DealDamage(j, ComputeDamage(t, def, o, _enemyStats[o.DefIndex], ti, factor, 1.0));
        }
    }

    private void Chain(in TdTower t, TdTowerDef def, int ti, int target, int jumps, double radius, double factor)
    {
        var e = _enemies[target];
        var hit = new HashSet<int> { e.Id };
        double x = e.X;
        for (int jump = 0; jump < jumps; jump++)
        {
            int next = -1; double distance = double.MaxValue;
            for (int j = 0; j < _enemies.Count; j++)
            {
                var c = _enemies[j];
                double d = Dist(x, c.X);
                if (c.Hp <= 0 || c.InvulnTicks > 0 || !VisibleToTower(c, def) || hit.Contains(c.Id) || (c.Flying && def.Targets != "both") || d > radius) continue;
                if (d < distance || (d == distance && (next < 0 || c.Id < _enemies[next].Id))) { next = j; distance = d; }
            }
            if (next < 0) break;
            var other = _enemies[next]; hit.Add(other.Id); x = other.X;
            DealDamage(next, ComputeDamage(t, def, other, _enemyStats[other.DefIndex], ti, factor, 1.0));
        }
    }

    private void Corrode(int target, double armorPct, double seconds)
    {
        var e = _enemies[target];
        double amount = e.Armor * armorPct;
        if (amount <= 0) return;
        e.CorrodeAmount = Math.Max(e.CorrodeTicks > 0 ? e.CorrodeAmount : 0, amount);
        e.CorrodeTicks = Math.Max(e.CorrodeTicks, Ticks(seconds));
        _enemies[target] = e;
    }

    /// <summary>Bloqueio gelado: nó do Balanceador com módulo Gelo aplica a lentidão do Gelo ao retido (respeita escudo e teto de lentidão).</summary>
    private void ApplyNodeModuleSlow(int towerId, int enemyIndex)
    {
        int ti = FindTower(towerId);
        if (ti < 0 || !_towers[ti].HasModule) return;
        var t = _towers[ti];
        for (int k = 0; k < t.ModuleCount; k++)
        {
            if (FusionPair(t.DefIndex, t.ModuleDef(k)) is not { Effect: "nodeSlow" }) continue;
            var mdef = _content.Towers[t.ModuleDef(k)];
            ApplySlow(enemyIndex, Stat(t, mdef, "slowFactor", 1.0), SlowDurationTicks + FrostRuneTicks(mdef));
        }
    }

    // ---- auras e renda ---------------------------------------------------------------------------------------------

    /// <summary>Módulos de suporte de uma mesma entidade que contam para a aura genérica.</summary>
    private const int MaxSupportModules = 2;

    /// <summary>
    /// Bônus de dano (fração) que as fusões dão à torre <paramref name="i"/>: módulo genérico de suporte (SDD-37 RF-37.5, inclusive a própria), aura de receita,
    /// bônus próprio da receita e o ganho por torre além da segunda.
    /// </summary>
    private double FusionAuraBonus(int i)
    {
        var t = _towers[i];
        double bonus = 0;
        if (t.HasModule)
        {
            bonus += (t.ComponentTotal - 2) * _content.Fusion.ExtraComponentDamageBp / 10000.0;
            if (RecipeBonus(t) is { } own) bonus += own.DamageBp / 10000.0;
        }
        for (int j = 0; j < _towers.Count; j++)
        {
            var s = _towers[j];
            if (!s.HasModule || s.DisabledTicks > 0 || !s.Placed || !t.Placed) continue;
            double distance = Dist(s.X, t.X);
            int supports = 0;
            for (int k = 0; k < s.ModuleCount && supports < MaxSupportModules; k++)
            {
                int m = s.ModuleDef(k);
                if (FusionRole(m) != "support" || FusionPair(s.DefIndex, m) is not null || distance > EffectiveRange(_content.Towers[m])) continue;
                bonus += _content.Fusion.Generic["support"]["damageAuraBp"] / 10000.0;
                supports++;
            }
            if (RecipeBonus(s) is { AuraBp: > 0 } r)
            {
                double range = 0;
                foreach (int d in s.Components()) range = Math.Max(range, EffectiveRange(_content.Towers[d]));
                if (distance <= range) bonus += r.AuraBp / 10000.0;
            }
        }
        return bonus;
    }

    /// <summary>
    /// Renda de fusão: cada módulo Cofre rende 50% da renda base, piso, sem bônus sem vazamento (RF-37.5; o Cofre base já paga integral) mais os créditos da receita.
    /// </summary>
    private long ModuleVaultIncome(in TdTower t)
    {
        if (!t.HasModule || !t.Placed || t.DisabledTicks > 0) return 0;
        long income = 0;
        for (int k = 0; k < t.ModuleCount; k++)
        {
            int m = t.ModuleDef(k);
            if (FusionRole(m) != "economy") continue;
            income += (long)Math.Floor(Stat(t, _content.Towers[m], "creditsPerWave", 0) * _content.Fusion.Generic["economy"]["incomePct"]);
        }
        if (RecipeBonus(t) is { } r) income += r.WaveCredits;
        return income;
    }
}
