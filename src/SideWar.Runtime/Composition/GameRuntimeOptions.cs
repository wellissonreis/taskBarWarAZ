using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Runtime.Hosting;
using SideWar.Runtime.Timing;

namespace SideWar.Runtime.Composition;

/// <summary>Entradas da parte pura da raiz de composição (T4.10). Tudo opcional exceto o relógio.</summary>
public sealed record GameRuntimeOptions
{
    public required IClock Clock { get; init; }

    /// <summary>Conteúdo do jogo; nulo = conteúdo embutido.</summary>
    public GameContent? Content { get; init; }

    /// <summary>
    /// Restaura a partida do <c>session.json</c> com o contexto do runtime (SDD-14 RF-14.8); devolver nulo (ou não
    /// informar) = partida nova.
    /// </summary>
    public Func<SimContext, SimState?>? Restore { get; init; }

    /// <summary>Layout da partida nova antes do primeiro resultado da barra; nulo = faixa padrão (hospedeiro Band).</summary>
    public StripLayout? InitialLayout { get; init; }

    /// <summary>Semente do PCG32 de uma partida nova.</summary>
    public ulong Seed { get; init; } = 0x5EED_5A1D_E7A2UL;

    /// <summary><c>performance.presentFps</c> (20, 30 ou 60; padrão 30) e <c>performance.absentFps</c> (4 ou 8; padrão 8).</summary>
    public int ActiveFps { get; init; } = LoopConstants.DefaultActiveFps;
    public int AbsentFps { get; init; } = LoopConstants.AmbientFps;

    public bool ReducedMotion { get; init; }
    public bool AutoScale { get; init; } = true;
    public double AutoScaleAccumSeconds { get; init; }

    /// <summary>Destino dos frames pedidos pela <c>RenderPolicy</c> (a faixa na T-Game); nulo = sem render.</summary>
    public IGameFrameSink? FrameSink { get; init; }

    public Func<DateTime>? UtcNow { get; init; }
}
