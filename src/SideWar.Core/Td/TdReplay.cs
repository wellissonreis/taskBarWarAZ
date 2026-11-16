using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>Comando aplicado <b>antes</b> do <see cref="TdMatchState.Step"/> de índice <c>Frame</c> (0 = antes do primeiro passo).</summary>
public sealed record TdLogEntry(long Frame, IGameCommand Command);

/// <summary>Partida scriptada: fase, seed da partida (PCG32), log de comandos e limite de passos (ADR-026).</summary>
public sealed record TdReplayScript(int Stage, ulong SeedState, ulong SeedStream, IReadOnlyList<TdLogEntry> Log, long MaxFrames);

public sealed record TdReplayResult(ulong StateHash, long Frames, long Ticks, MatchPhase Phase, int Stars, int Integrity, long Credits, int CommandsRejected);

/// <summary>Executa um <see cref="TdReplayScript"/> só pela API pública (Apply/Step/ComputeStateHash). Para na Vitória/Derrota.</summary>
public static class TdReplayRunner
{
    public static TdReplayResult Run(TdContent content, TdReplayScript script)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(script);
        var state = TdMatchState.Create(content, script.Stage, Rng.Seed(script.SeedState, script.SeedStream));
        var log = script.Log.OrderBy(e => e.Frame).ToArray(); // estável: ordem de declaração no mesmo frame
        int next = 0, rejected = 0;
        while (state.Frame < script.MaxFrames)
        {
            while (next < log.Length && log[next].Frame <= state.Frame)
                if (!state.Apply(log[next++].Command).IsOk) rejected++;
            if (state.Phase is MatchPhase.Vitoria or MatchPhase.Derrota) break;
            state.Step();
        }
        return new TdReplayResult(state.ComputeStateHash(), state.Frame, state.Tick, state.Phase, state.Stars, state.Integrity, state.Credits, rejected);
    }
}
