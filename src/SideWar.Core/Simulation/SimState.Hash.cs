namespace SideWar.Core.Simulation;

/// <summary>FNV-1a 64 sobre valores primitivos em little-endian fixo; floats e doubles pelos bits (SDD-11 RF-11.12).</summary>
public struct StateHasher
{
    private const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;
    private ulong _h;

    public static StateHasher Create() => new() { _h = Offset };
    public readonly ulong Value => _h;

    public void AddByte(byte b) { _h = unchecked((_h ^ b) * Prime); }

    public void AddUInt64(ulong v)
    {
        for (int i = 0; i < 8; i++) { AddByte((byte)v); v >>= 8; }
    }

    public void AddInt64(long v) => AddUInt64(unchecked((ulong)v));
    public void AddInt32(int v) => AddUInt64(unchecked((uint)v));
    public void AddSingle(float v) => AddInt32(BitConverter.SingleToInt32Bits(v));
    public void AddDouble(double v) => AddInt64(BitConverter.DoubleToInt64Bits(v));
    public void AddBool(bool v) => AddByte(v ? (byte)1 : (byte)0);

    public void AddString(string s)
    {
        AddInt32(s.Length);
        foreach (char c in s) { AddByte((byte)c); AddByte((byte)(c >> 8)); }
    }
}

/// <summary>
/// <c>StateHash</c> determinístico (SDD-11 RF-11.12, ADR-026). Seções em ordem fixa: escalares, layout, torres e inimigos
/// por <c>Id</c> crescente, onda (plano restante, relógios) e <c>Rng</c>; depois o ponto de extensão
/// <see cref="HashExtraSections"/> (T3.8: núcleo, queda, fases, XP; SDD-26: eventos e ledger). <c>Events</c> fica fora.
/// Sem alocação.
/// </summary>
public sealed partial class SimState
{
    public ulong ComputeStateHash()
    {
        var h = StateHasher.Create();
        HashScalars(ref h);
        HashLayout(ref h);
        HashTowers(ref h);
        HashEnemies(ref h);
        HashWave(ref h);
        h.AddUInt64(_rng.State);
        h.AddUInt64(_rng.Inc);
        HashExtraSections(ref h);
        return h.Value;
    }

    /// <summary>Ponto de extensão para as próximas fichas acrescentarem seções (implementar em outro arquivo partial).</summary>
    partial void HashExtraSections(ref StateHasher h);

    private void HashScalars(ref StateHasher h)
    {
        h.AddInt64(Tick);
        h.AddInt32(Stage);
        h.AddInt32(Wave);
        h.AddInt32(Falls);
        h.AddInt64(Credits);
        h.AddDouble(Carry);
        h.AddInt64(Reservas);
        h.AddInt64(Xp);
        h.AddInt32(Level);
        h.AddInt32(BaseHp);
        h.AddInt32(MaxBaseHp);
        h.AddInt32(NextId);
        h.AddInt64(ManualUpgrades);
        h.AddDouble(PeakDamageFactor);
    }

    private void HashLayout(ref StateHasher h)
    {
        var l = _layout;
        h.AddSingle(l.SpawnX);
        h.AddSingle(l.LeakX);
        h.AddSingle(l.CoreX);
        h.AddSingle(l.PortalX);
        h.AddInt32(l.SlotCount);
        foreach (float x in l.SlotX) h.AddSingle(x);
    }

    private void HashTowers(ref StateHasher h)
    {
        Span<int> order = stackalloc int[TowerStore.CapacityConst];
        int n = Towers.CopyIndicesByIdAscending(order);
        h.AddInt32(n);
        for (int k = 0; k < n; k++)
        {
            ref var t = ref Towers.At(order[k]);
            h.AddInt32(t.Id);
            h.AddByte((byte)t.Type);
            h.AddInt32(t.SlotIndex);
            h.AddSingle(t.Cooldown);
            h.AddInt32(t.Level);
            h.AddByte((byte)t.Mode);
            h.AddByte(t.Hits);
        }
    }

    private void HashEnemies(ref StateHasher h)
    {
        Span<int> order = stackalloc int[EnemyStore.CapacityConst];
        int n = Enemies.CopyIndicesByIdAscending(order);
        h.AddInt32(n);
        for (int k = 0; k < n; k++)
        {
            ref var e = ref Enemies.At(order[k]);
            h.AddInt32(e.Id);
            h.AddByte((byte)e.Kind);
            h.AddSingle(e.X);
            h.AddSingle(e.Speed);
            h.AddSingle(e.SpawnSpeed);
            h.AddDouble(e.Hp);
            h.AddDouble(e.MaxHp);
            h.AddInt32(e.Armor);
            h.AddByte((byte)e.Flags);
            h.AddByte((byte)e.LastHit);
        }
    }

    private void HashWave(ref StateHasher h)
    {
        h.AddSingle(WaveClock);
        h.AddSingle(Rest);
        h.AddBool(_plan is not null);
        if (_plan is null) return;
        h.AddInt32(_plan.Wave);
        h.AddInt32(_plan.Count);
        h.AddDouble(_plan.Interval);
        h.AddBool(_plan.IsBoss);
        h.AddInt32(_plan.Spawns.Length);
        h.AddInt32(_nextSpawn);
        for (int i = _nextSpawn; i < _plan.Spawns.Length; i++)
        {
            ref readonly var s = ref _plan.Spawns[i];
            h.AddDouble(s.Time);
            h.AddString(s.Enemy.Id);
            h.AddDouble(s.Hp);
            h.AddDouble(s.Gold);
            h.AddDouble(s.Xp);
        }
    }

    /// <summary>
    /// Restore (SDD-14): depois de <see cref="StartWave"/> reconstruir o plano, reposiciona o próximo spawn pendente.
    /// Lança se não houver plano ou o índice estiver fora de [0, Spawns.Length].
    /// </summary>
    public void RestoreNextSpawn(int nextSpawnIndex)
    {
        if (_plan is null) throw new InvalidOperationException("Sem plano de onda ativo.");
        if ((uint)nextSpawnIndex > (uint)_plan.Spawns.Length) throw new ArgumentOutOfRangeException(nameof(nextSpawnIndex));
        _nextSpawn = nextSpawnIndex;
    }
}
