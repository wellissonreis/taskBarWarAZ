using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>Resultado da checagem de uma fusão (API da UI, SDD-37 RF-37.10): motivo (None = elegível), custo exato do módulo, nome e se o par é nomeado.</summary>
public readonly record struct TdFusionCheck(CommandReason Reason, long Cost, long Missing, string Name, bool Named, string Effect)
{
    public bool IsOk => Reason == CommandReason.None;
}

/// <summary>
/// Fusão de torres (SDD-37 TD.37a): até duas componentes por entidade (base A define chassi, mira, alcance e cadência; módulo B traz um efeito limitado,
/// RF-37.1). Pares nomeados vêm de <c>td-fusion.json</c>; o resto usa o módulo genérico por papel (RF-37.5). Ordem fixa de resolução: base, módulo, aura.
/// </summary>
public sealed partial class TdMatchState
{
    /// <summary>Fusão liberada nesta corrida (fase absoluta mínima do conteúdo; não volta a bloquear).</summary>
    public bool FusionUnlocked => CurrentRankId >= _content.Fusion.MinRank;

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

    /// <summary>Nome do híbrido: o do par nomeado ou "Base + Módulo" na regra genérica; o nome da torre se simples.</summary>
    public string TowerDisplayName(in TdTower t)
    {
        var def = _content.Towers[t.DefIndex];
        if (!t.HasModule) return def.Name;
        return FusionName(t.DefIndex, t.ModuleDefIndex);
    }

    /// <summary>Quantas componentes de cada tipo existem (inclui módulos e torres SEM POSIÇÃO): base de custo e limite por tipo.</summary>
    public int ComponentCount(int defIndex) => TowerCount(defIndex);

    /// <summary>Checa, sem alterar nada, se <paramref name="componentTypeId"/> pode ser fundido sobre a torre (mesma ordem de validação do comando).</summary>
    public TdFusionCheck CheckFusion(int baseTowerId, string? componentTypeId)
    {
        int bi = FindTower(baseTowerId);
        int mi = componentTypeId is null ? -1 : TowerDefIndex(componentTypeId);
        if (bi < 0 || mi < 0) return new(CommandReason.TowerNotFound, 0, 0, "", false, "");
        var t = _towers[bi];
        var pair = FusionPair(t.DefIndex, mi);
        string name = FusionName(t.DefIndex, mi);
        string effect = pair?.Effect ?? "generic:" + FusionRole(mi);
        long cost = NextTowerCost(mi);
        TdFusionCheck R(CommandReason r, long missing = 0) => new(r, cost, missing, name, pair is not null, effect);
        if (!Running) return R(CommandReason.InvalidPhase);
        if (!t.Placed) return R(CommandReason.NotPlaced);
        if (t.HasModule) return R(CommandReason.AlreadyFused);
        if (!FusionUnlocked) return R(CommandReason.FusionLocked);
        var def = _content.Towers[mi];
        if (CurrentRankId < def.MinRank || CurrentRankId < _content.Towers[t.DefIndex].MinRank) return R(CommandReason.TowerLocked);
        if (def.Stats.TryGetValue("maxPerPlayer", out var limit) && TowerCount(mi) >= limit) return R(CommandReason.TowerTypeLimit);
        if (Credits < cost) return R(CommandReason.InsufficientCredits, cost - Credits);
        return R(CommandReason.None);
    }

    /// <summary>Quem é a base ao arrastar <paramref name="sourceId"/> sobre <paramref name="targetId"/>: a de maior custo de tipo (empate: o alvo). Devolve (base, módulo) ou (-1, -1) se inválido.</summary>
    public (int BaseId, int ModuleId, CommandReason Reason) CheckFusionTowers(int targetId, int sourceId)
    {
        int ti = FindTower(targetId), si = FindTower(sourceId);
        if (ti < 0 || si < 0 || targetId == sourceId) return (-1, -1, CommandReason.TowerNotFound);
        var t = _towers[ti];
        var s = _towers[si];
        if (!Running) return (-1, -1, CommandReason.InvalidPhase);
        if (!t.Placed || !s.Placed) return (-1, -1, CommandReason.NotPlaced);
        if (t.HasModule || s.HasModule) return (-1, -1, CommandReason.AlreadyFused);
        if (!FusionUnlocked) return (-1, -1, CommandReason.FusionLocked);
        if (CurrentRankId < _content.Towers[t.DefIndex].MinRank || CurrentRankId < _content.Towers[s.DefIndex].MinRank)
            return (-1, -1, CommandReason.TowerLocked);
        bool sourceStronger = _content.Towers[s.DefIndex].Cost > _content.Towers[t.DefIndex].Cost;
        return sourceStronger ? (sourceId, targetId, CommandReason.None) : (targetId, sourceId, CommandReason.None);
    }

    /// <summary>Nome do híbrido de um arrasto (a base é a mais forte).</summary>
    public string FusionNameOfTowers(int targetId, int sourceId)
    {
        var (b, mo, r) = CheckFusionTowers(targetId, sourceId);
        return r != CommandReason.None ? "" : FusionName(_towers[FindTower(b)].DefIndex, _towers[FindTower(mo)].DefIndex);
    }

    private CommandResult DoFuseTowers(FuseTowers cmd)
    {
        var (baseId, moduleId, reason) = CheckFusionTowers(cmd.TargetId, cmd.SourceId);
        if (reason != CommandReason.None) return Reject(reason);
        int bi = FindTower(baseId), mi = FindTower(moduleId);
        var module = _towers[mi];
        var t = _towers[bi];
        t.ModuleSlot = module.DefIndex + 1;
        t.ModulePrice = module.PurchasePrice;
        t.ModuleCdTicks = ModuleIntervalTicks(t);
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
        int mi = TowerDefIndex(cmd.ComponentTypeId);
        var t = _towers[bi];
        Credits -= check.Cost;
        t.ModuleSlot = mi + 1;
        t.ModulePrice = check.Cost;
        t.ModuleCdTicks = ModuleIntervalTicks(t);
        _towers[bi] = t;
        Emit(TdEventType.TowerFused, t.Id, check.Cost);
        return CommandResult.Ok;
    }

    /// <summary>Intervalo (ticks) da ação periódica do módulo; 0 se o módulo não age por temporizador (efeitos por acerto, aura, renda).</summary>
    private int ModuleIntervalTicks(in TdTower t)
    {
        if (!t.HasModule) return 0;
        var pair = FusionPair(t.DefIndex, t.ModuleDefIndex);
        if (pair is not null)
            return pair.Effect is "periodicDamage" or "periodicSlow" ? Ticks(pair.Params["intervalSeconds"]) : 0;
        string role = FusionRole(t.ModuleDefIndex);
        return role is "attack" or "control" ? Ticks(_content.Fusion.Generic[role]["intervalSeconds"]) : 0;
    }

    /// <summary>Valor de venda de uma torre: por componente, <c>min(preço pago, SellValueAt(tipo, contagem antes da remoção))</c>, base depois módulo, decrementando a contagem virtual (RF-37.3).</summary>
    private long HybridSellValue(in TdTower t)
    {
        var counts = new Dictionary<int, int>();
        int Count(int d) => counts.TryGetValue(d, out var c) ? c : TowerCount(d);
        long sum = Math.Min(t.PurchasePrice, SellValueAt(t.DefIndex, Count(t.DefIndex)));
        counts[t.DefIndex] = Count(t.DefIndex) - 1;
        if (t.HasModule) sum += Math.Min(t.ModulePrice, SellValueAt(t.ModuleDefIndex, Count(t.ModuleDefIndex)));
        return sum;
    }

    // ---- ação periódica do módulo (por torre, em FireTowers) -----------------------------------------------------------

    private void TickModule(ref TdTower t, int ti)
    {
        if (t.ModuleCdTicks > 0) return;
        int interval = ModuleIntervalTicks(t);
        if (interval <= 0) return;
        var baseDef = _content.Towers[t.DefIndex];
        var mdef = _content.Towers[t.ModuleDefIndex];
        var pair = FusionPair(t.DefIndex, t.ModuleDefIndex);
        string role = pair is null ? FusionRole(t.ModuleDefIndex) : pair.Effect == "periodicDamage" ? "attack" : "control";
        var p = pair?.Params;
        bool moduleRange = pair is null || p!["rangeFromModule"] > 0;
        double range = moduleRange ? EffectiveRange(mdef) : EffectiveRange(baseDef) + _auraRange[ti];
        // Atinge voadores só se o módulo atinge (generic control: ambos, salvo módulo de solo). Mira da base se existir, senão primeiro (empates por Id, em PickTarget).
        var scan = mdef with { Targets = mdef.Targets == "ground" ? "ground" : "both" };
        var mode = baseDef.Kind == "attack" || (baseDef.Kind == "control" && baseDef.Targets != "none") ? t.Targeting : (byte)TdTargeting.First;
        var probe = t;
        probe.Targeting = mode;
        int target = PickTarget(probe, scan, range);
        if (target < 0) return; // sem alvo não consome recarga
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
            ApplySlow(target, g["slowFactor"], Ticks(g["slowSeconds"]) + (mdef.Id == "frost" ? Rune("frostSlowTenths") * 3 : 0));
        }
        else
        {
            ApplySlow(target, Stat(t, mdef, "slowFactor", 1.0), SlowDurationTicks + (mdef.Id == "frost" ? Rune("frostSlowTenths") * 3 : 0));
        }
        t.ModuleCdTicks = interval;
    }

    // ---- efeitos por acerto (base atira) -----------------------------------------------------------------------------

    private void ApplyModuleOnHit(in TdTower t, TdTowerDef baseDef, int ti, int target)
    {
        if (!t.HasModule) return;
        var pair = FusionPair(t.DefIndex, t.ModuleDefIndex);
        if (pair is null || _enemies[target].Hp <= 0) return;
        var mdef = _content.Towers[t.ModuleDefIndex];
        var p = pair.Params;
        var e = _enemies[target];
        switch (pair.Effect)
        {
            case "hitDamage":
                DealDamage(target, ComputeDamage(t, mdef, e, _enemyStats[e.DefIndex], ti, p["damagePct"], 1.0));
                break;
            case "hitSlow":
                ApplySlow(target, Stat(t, mdef, "slowFactor", 1.0), SlowDurationTicks + (mdef.Id == "frost" ? Rune("frostSlowTenths") * 3 : 0));
                break;
            case "hitSplash":
            {
                double factor = Stat(t, mdef, "splashFactor", 0), radius = Stat(t, mdef, "splashRadius", 0) * Stat(t, mdef, "splashRadiusMultiplier", 1.0);
                for (int j = 0; j < _enemies.Count; j++)
                {
                    var o = _enemies[j];
                    if (j == target || o.Flying || o.Hp <= 0 || o.InvulnTicks > 0 || Dist(o.X, e.X) > radius) continue;
                    DealDamage(j, ComputeDamage(t, baseDef, o, _enemyStats[o.DefIndex], ti, factor, 1.0));
                }
                break;
            }
            case "hitChain":
            {
                var hit = new HashSet<int> { e.Id };
                double x = e.X, radius = Stat(t, mdef, "chainRadius", 0);
                int jumps = (int)p["jumps"];
                for (int jump = 0; jump < jumps; jump++)
                {
                    int next = -1; double distance = double.MaxValue;
                    for (int j = 0; j < _enemies.Count; j++)
                    {
                        var c = _enemies[j];
                        double d = Dist(x, c.X);
                        if (c.Hp <= 0 || c.InvulnTicks > 0 || !VisibleToTower(c, baseDef) || hit.Contains(c.Id) || (c.Flying && baseDef.Targets != "both") || d > radius) continue;
                        if (d < distance || (d == distance && (next < 0 || c.Id < _enemies[next].Id))) { next = j; distance = d; }
                    }
                    if (next < 0) break;
                    var other = _enemies[next]; hit.Add(other.Id); x = other.X;
                    DealDamage(next, ComputeDamage(t, baseDef, other, _enemyStats[other.DefIndex], ti, p["damagePct"], 1.0));
                }
                break;
            }
            case "hitCorrode":
            {
                double amount = e.Armor * p["armorPct"];
                if (amount > 0)
                {
                    e.CorrodeAmount = Math.Max(e.CorrodeTicks > 0 ? e.CorrodeAmount : 0, amount);
                    e.CorrodeTicks = Math.Max(e.CorrodeTicks, Ticks(p["seconds"]));
                    _enemies[target] = e;
                }
                break;
            }
        }
    }

    /// <summary>Bloqueio gelado: nó do Balanceador com módulo Gelo aplica a lentidão do Gelo ao retido (respeita escudo e teto de lentidão).</summary>
    private void ApplyNodeModuleSlow(int towerId, int enemyIndex)
    {
        int ti = FindTower(towerId);
        if (ti < 0 || !_towers[ti].HasModule) return;
        var t = _towers[ti];
        var pair = FusionPair(t.DefIndex, t.ModuleDefIndex);
        if (pair is not { Effect: "nodeSlow" }) return;
        var mdef = _content.Towers[t.ModuleDefIndex];
        ApplySlow(enemyIndex, Stat(t, mdef, "slowFactor", 1.0), SlowDurationTicks + (mdef.Id == "frost" ? Rune("frostSlowTenths") * 3 : 0));
    }

    /// <summary>Renda do módulo Cofre num híbrido cuja base não é Cofre (RF-37.5): 50% da renda base, piso; sem bônus sem vazamento. O Cofre base já paga integral.</summary>
    private long ModuleVaultIncome(in TdTower t)
    {
        if (!t.HasModule || !t.Placed || t.DisabledTicks > 0 || FusionRole(t.ModuleDefIndex) != "economy") return 0;
        var vault = _content.Towers[t.ModuleDefIndex];
        return (long)Math.Floor(Stat(t, vault, "creditsPerWave", 0) * _content.Fusion.Generic["economy"]["incomePct"]);
    }
}
