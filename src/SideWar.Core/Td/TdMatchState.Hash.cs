using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

public sealed partial class TdMatchState
{
    /// <summary>
    /// <c>StateHash</c> da partida (FNV-1a 64 via <see cref="StateHasher"/>): conteúdo, fase, relógios, integridade, créditos,
    /// auto-início, torres e inimigos por <c>Id</c> (ordem de inserção), ondas ativas e <c>Rng</c>. <c>Events</c> fica fora.
    /// </summary>
    public ulong ComputeStateHash()
    {
        var h = StateHasher.Create();
        h.AddString(_content.ContentHash);
        h.AddInt32(_stage.Id);
        if (_options.HighestCompletedStage is int highestCompleted) h.AddInt32(highestCompleted);
        h.AddInt32(GateReputation);
        h.AddByte((byte)Phase);
        h.AddInt64(Tick);
        h.AddInt64(Frame);
        h.AddInt32(WaveNumber);
        h.AddInt32((int)Math.Min(int.MaxValue, ReputationLostTotal));
        h.AddInt32(_stageIntegrityLoss);
        h.AddInt32(_backupHealsThisStage);
        h.AddInt64(Credits);
        h.AddInt32(Stars);
        h.AddByte((byte)AutoStart);
        h.AddDouble(AutoStartSeconds);
        h.AddBool(_countdownActive);
        h.AddInt32(_countdownTicks);
        h.AddInt64(CooldownReductionTicks);
        if (_shadow || _shadowRequest >= 0) { h.AddBool(_shadow); h.AddInt32(_shadowRequest); h.AddInt64(_shadowRemainder); } // só em sombra: hashes antigos não mudam
        h.AddInt32(_nextTowerId);
        h.AddInt32(_nextEnemyId);
        h.AddInt32(_nextNodeId);
        h.AddInt32(_layout?.Width ?? 0);
        h.AddInt32(_layout?.FreeZoneCount ?? 0);
        for (int i = 0; _layout is not null && i < _layout.FreeZoneCount; i++) { h.AddInt32(_layout.ZoneAt(i).X0); h.AddInt32(_layout.ZoneAt(i).X1); }
        h.AddInt32(_towers.Count);
        foreach (var t in _towers)
        {
            h.AddInt32(t.Id); h.AddInt32(t.DefIndex); h.AddInt64(t.PurchasePrice); h.AddInt32(t.X); h.AddBool(t.Placed);
            h.AddInt32(t.CooldownTicks);
            if (_content.Towers[t.DefIndex].Id == "functions")
            {
                h.AddInt32(t.BurstLeft); h.AddInt32(t.BurstGapTicks); h.AddInt32(t.BurstTargetId);
                h.AddInt32(t.RangeSeenIds?.Length ?? 0);
                if (t.RangeSeenIds is not null) foreach (int id in t.RangeSeenIds) h.AddInt32(id);
            }
            h.AddByte(t.Targeting); h.AddInt32(t.DisabledTicks); h.AddInt32(t.FocusTargetId); h.AddInt32(t.FocusHits);
            h.AddInt32(t.ShotCount); h.AddInt32(t.ParalyzeCdTicks); h.AddInt32(t.NodeCdTicks);
            if (t.HasModule) { h.AddInt32(t.ModuleSlot); h.AddInt64(t.ModulePrice); h.AddInt32(t.ModuleCdTicks); } // só híbridos: o hash de torres simples não muda
        }
        h.AddInt32(_enemies.Count);
        foreach (var e in _enemies)
        {
            h.AddInt32(e.Id); h.AddInt32(e.Wave); h.AddInt32(e.DefIndex); h.AddBool(e.IsBoss);
            h.AddDouble(e.X); h.AddDouble(e.Speed); h.AddDouble(e.Hp); h.AddDouble(e.MaxHp); h.AddDouble(e.Armor);
            if (_enemyStats[e.DefIndex].Replicator) h.AddBool(e.IsReplica);
            if (_enemyStats[e.DefIndex].ShieldHp > 0) h.AddDouble(e.ShieldHp);
            if (e.Mutations != 0) { h.AddInt32(e.Mutations); if (_enemyStats[e.DefIndex].ShieldHp <= 0) h.AddDouble(e.ShieldHp); } // só mutados: escudo adicionado também entra
            if (_enemyStats[e.DefIndex].RevealRange > 0) h.AddInt32(e.SentinelMarkTicks);
            h.AddInt64(e.Gold); h.AddInt32(e.CoreDamage); h.AddBool(e.Flying);
            h.AddDouble(e.SlowMult); h.AddInt32(e.SlowTicks); h.AddInt32(e.ParalyzeTicks); h.AddInt32(e.ParalyzeGraceTicks); h.AddInt32(e.PushGraceTicks);
            h.AddInt32(e.InvulnTicks); h.AddDouble(e.VulnBonus); h.AddInt32(e.VulnTicks); h.AddDouble(e.CorrodeAmount);
            h.AddInt32(e.CorrodeTicks); h.AddInt32(e.SinceDamageTicks); h.AddInt32(e.AbilityCdTicks); h.AddByte(e.Triggers);
            if (e.IsBoss && (_enemyStats[e.DefIndex].Id.StartsWith("chefe_", StringComparison.Ordinal)
                || _enemyStats[e.DefIndex].Id is "ransomware" or "botnet")) h.AddBool(e.Enraged);
        }
        h.AddInt32(_active.Count);
        foreach (var w in _active)
        {
            h.AddInt32(w.Number); h.AddInt64(w.StartTick); h.AddInt32(w.NextSpawn); h.AddInt32(w.Alive); h.AddBool(w.Leaked);
            h.AddInt32(w.Spawns.Length);
            foreach (var s in w.Spawns) { h.AddInt64(s.DueTick); h.AddInt32(s.EnemyIndex); if (s.Mutations != 0) { h.AddByte(1); h.AddInt32(s.Mutations); } }
        }
        h.AddInt32(_nodes.Count);
        foreach (var n in _nodes)
        {
            h.AddInt32(n.Id); h.AddInt32(n.TowerId); h.AddDouble(n.X); h.AddDouble(n.Hp);
            h.AddInt32(n.LifeTicks); h.AddInt32(n.Holds); h.AddDouble(n.Dps); h.AddByte(n.Flags);
        }
        h.AddInt32(_zones.Count);
        foreach (var z in _zones) { h.AddDouble(z.X); h.AddDouble(z.Radius); h.AddInt32(z.TicksLeft); h.AddDouble(z.DamagePerTick); }
        for (int i = 0; i < _abilityCd.Length; i++) { h.AddInt32(_abilityCd[i]); h.AddInt32(_abilityActive[i]); h.AddInt32(_abilityUses[i]); }
        foreach (var m in _metaNodes.Order(StringComparer.Ordinal)) h.AddString(m);
        h.AddBool(_present);
        h.AddBool(_bonusAnswered);
        if (Paused) h.AddBool(true); // só quando verdadeiro: o hash de partidas sem pausa não muda
        h.AddInt32((int)(uint)_awakened);
        if ((_awakened >> 32) != UInt128.Zero) { h.AddUInt64((ulong)(_awakened >> 32)); h.AddUInt64((ulong)(_awakened >> 96)); } // só com nós acima do slot 31: hashes de partidas sem runas externas não mudam
        h.AddInt64(Fragments);
        h.AddBool(_hasLeaked);
        if (!_options.IsClassic) { h.AddBool(_options.ContinuousWaves); h.AddBool(_options.PersistentRun); h.AddInt32(_stageNumber); h.AddInt32(_stageClears); } // só fora do modo clássico: os hashes antigos não mudam
        if (!_options.IsClassic) { h.AddUInt64(_mutSeed); h.AddInt32(_runWaves); foreach (int d in _mutDebut) h.AddInt32(d); } // mutações (SDD-37): semente derivada, ondas iniciadas e estreias
        h.AddUInt64(_rng.State);
        h.AddUInt64(_rng.Inc);
        return h.Value;
    }
}
