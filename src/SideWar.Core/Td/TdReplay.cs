using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>Comando aplicado <b>antes</b> do <see cref="TdMatchState.Step"/> de índice <c>Frame</c> (0 = antes do primeiro passo).</summary>
/// <remarks><c>Seq</c> desempata comandos no mesmo frame (ex.: <c>Pause</c>, compras, <c>Resume</c>); o log é ordenado por (Frame, Seq).</remarks>
public sealed record TdLogEntry(long Frame, IGameCommand Command, int Seq = 0);

/// <summary>Partida scriptada (<c>Options</c> nulo = modo clássico, compatível com os logs antigos): fase, seed da partida (PCG32), log de comandos e limite de passos (ADR-026).</summary>
public sealed record TdReplayScript(int Stage, ulong SeedState, ulong SeedStream, IReadOnlyList<TdLogEntry> Log, long MaxFrames, TdMatchOptions? Options = null);

public sealed record TdReplayResult(ulong StateHash, long Frames, long Ticks, MatchPhase Phase, int Stars, int Integrity, long Credits, int CommandsRejected);

/// <summary>Executa um <see cref="TdReplayScript"/> só pela API pública (Apply/Step/ComputeStateHash). Para na Vitória/Derrota.</summary>
public static class TdReplayRunner
{
    public static TdReplayResult Run(TdContent content, TdReplayScript script)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(script);
        var state = TdMatchState.Create(content, script.Stage, Rng.Seed(script.SeedState, script.SeedStream), null, script.Options ?? TdMatchOptions.Classic);
        var log = script.Log.OrderBy(e => e.Frame).ThenBy(e => e.Seq).ToArray(); // estável: ordem de declaração se (Frame, Seq) empatar
        int next = 0, rejected = 0;
        while (state.Frame < script.MaxFrames)
        {
            while (next < log.Length && log[next].Frame <= state.Frame)
                if (!state.Apply(log[next++].Command).IsOk) rejected++;
            if (state.Phase is MatchPhase.Vitoria or MatchPhase.Derrota) break;
            if (state.Paused && (next >= log.Length || log[next].Frame > state.Frame)) break; // pausada sem Resume aplicável: o Frame não anda
            state.Step();
        }
        return new TdReplayResult(state.ComputeStateHash(), state.Frame, state.Tick, state.Phase, state.Stars, state.Integrity, state.Credits, rejected);
    }
}
