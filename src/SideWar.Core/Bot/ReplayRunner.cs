using SideWar.Core.Content;
using SideWar.Core.Simulation;

namespace SideWar.Core.Bot;

/// <summary>Comando de replay aplicado <b>antes</b> do tick de índice <see cref="Tick"/> (0 = antes do primeiro tick).</summary>
public sealed record ReplayCommand(long Tick, IGameCommand Command);

/// <summary>Partida scriptada: seed, layout inicial, comandos por tick, bot opcional e critério de parada.</summary>
public sealed record ReplayScript(
    string Name, ulong SeedState, ulong SeedStream, StripLayout InitialLayout,
    IReadOnlyList<ReplayCommand> Commands, bool UseBot, int StopAfterWavesCleared, long MaxTicks);

/// <summary>Resultado de uma execução (CA-12.3: <c>ondaMax</c>, quedas e créditos ao fim).</summary>
public sealed record ReplayResult(
    ulong StateHash, long Ticks, int WavesCleared, int Stage, int Wave, int UnlockedStage, int StageWaveMax, int Falls, long Credits, int Level, int CommandsRejected);

/// <summary>Executa um <see cref="ReplayScript"/> só pela API pública (Apply/Step/ComputeStateHash).</summary>
public static class ReplayRunner
{
    public static ReplayResult Run(GameContent content, ReplayScript script)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(script);
        var ctx = new SimContext(content);
        var state = SimState.CreateInitial(content, script.InitialLayout, Rng.Seed(script.SeedState, script.SeedStream));
        var bot = script.UseBot ? new SanityBot(ctx) : null;
        var commands = script.Commands.OrderBy(c => c.Tick).ToArray(); // estável: ordem de declaração no mesmo tick
        int next = 0, cleared = 0, rejected = 0;
        long cursor = state.Events.Written;
        while (state.Tick < script.MaxTicks && (script.StopAfterWavesCleared <= 0 || cleared < script.StopAfterWavesCleared))
        {
            while (next < commands.Length && commands[next].Tick <= state.Tick)
                if (!state.Apply(ctx, commands[next++].Command).IsOk) rejected++;
            bot?.Act(state);
            state.Step(ctx);
            while (state.Events.TryRead(ref cursor, out var e))
                if (e.Type == SimEventType.WaveCleared) cleared++;
        }
        return new ReplayResult(state.ComputeStateHash(), state.Tick, cleared, state.Stage, state.Wave,
            state.UnlockedStage, state.StageWaveMax, state.Falls, state.Credits, state.Level, rejected);
    }
}
