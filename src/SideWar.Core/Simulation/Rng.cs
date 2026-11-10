namespace SideWar.Core.Simulation;

/// <summary>PCG32 (SDD-11 RF-11.13): estado de 64 bits mais incremento; seed persistida, nunca reiniciada.</summary>
public struct Rng
{
    private const ulong Mult = 6364136223846793005UL;

    public ulong State { get; private set; }
    public ulong Inc { get; private set; }

    public static Rng Seed(ulong initState, ulong initSeq)
    {
        var r = new Rng { State = 0, Inc = (initSeq << 1) | 1UL };
        r.NextUInt();
        r.State = unchecked(r.State + initState);
        r.NextUInt();
        return r;
    }

    /// <summary>Restaura o estado exato (save/restore).</summary>
    public static Rng FromState(ulong state, ulong inc) => new() { State = state, Inc = inc | 1UL };

    public uint NextUInt()
    {
        ulong old = State;
        State = unchecked(old * Mult + Inc);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }
}
