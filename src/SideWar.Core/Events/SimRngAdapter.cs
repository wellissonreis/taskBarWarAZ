using SideWar.Core.Simulation;

namespace SideWar.Core.Events;

/// <summary>Adapta o <c>Rng</c> PCG32 de <see cref="SimState"/> (SDD-11 RF-11.13) a <see cref="IQuestionRng"/>. Consumir altera o hash.</summary>
public sealed class SimRngAdapter(SimState state) : IQuestionRng
{
    /// <inheritdoc/>
    public int NextInt(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(exclusiveMax, 0);
        // Multiplicação de 64 bits: determinística e sem divisão; viés desprezível para limites pequenos.
        return (int)(((ulong)state.Rng.NextUInt() * (uint)exclusiveMax) >> 32);
    }
}
