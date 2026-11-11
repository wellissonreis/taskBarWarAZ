using SideWar.Core.Simulation;

namespace SideWar.Runtime.Hosting;

/// <summary>Consumidor de frames (T4.10 render). Chamado na T-Game quando a <c>RenderPolicy</c> pede composição.</summary>
public interface IGameFrameSink
{
    /// <param name="presentationSeconds">Relógio de apresentação (SDD-10 RF-10.8): só avança com a simulação rodando.</param>
    void Present(SimState state, double presentationSeconds);
}
