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
        h.AddByte((byte)Phase);
        h.AddInt64(Tick);
        h.AddInt64(Frame);
        h.AddInt32(WaveNumber);
        h.AddInt32(Integrity);
        h.AddInt64(Credits);
        h.AddInt32(Stars);
        h.AddByte((byte)AutoStart);
        h.AddDouble(AutoStartSeconds);
        h.AddBool(_countdownActive);
        h.AddInt32(_countdownTicks);
        h.AddInt64(CooldownReductionTicks);
        h.AddInt32(_nextTowerId);
        h.AddInt32(_nextEnemyId);
        h.AddInt32(_towers.Count);
        foreach (var t in _towers)
        {
            h.AddInt32(t.Id); h.AddInt32(t.DefIndex); h.AddInt32(t.X); h.AddInt32(t.Level);
            h.AddInt32(t.CooldownTicks); h.AddInt64(t.Invested); h.AddByte(t.Branch);
        }
        h.AddInt32(_enemies.Count);
        foreach (var e in _enemies)
        {
            h.AddInt32(e.Id); h.AddInt32(e.Wave); h.AddInt32(e.DefIndex); h.AddBool(e.IsBoss);
            h.AddDouble(e.X); h.AddDouble(e.Speed); h.AddDouble(e.Hp); h.AddDouble(e.MaxHp); h.AddDouble(e.Armor);
            h.AddInt64(e.Gold); h.AddInt32(e.CoreDamage); h.AddBool(e.Flying);
        }
        h.AddInt32(_active.Count);
        foreach (var w in _active)
        {
            h.AddInt32(w.Number); h.AddInt64(w.StartTick); h.AddInt32(w.NextSpawn); h.AddInt32(w.Alive);
            h.AddInt32(w.Spawns.Length);
            foreach (var s in w.Spawns) { h.AddInt64(s.DueTick); h.AddInt32(s.EnemyIndex); }
        }
        h.AddUInt64(_rng.State);
        h.AddUInt64(_rng.Inc);
        return h.Value;
    }
}
