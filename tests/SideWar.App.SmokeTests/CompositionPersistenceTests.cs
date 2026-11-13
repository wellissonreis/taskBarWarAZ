using System.IO;
using SideWar.App.Composition;
using SideWar.Core;
using SideWar.Core.Simulation;
using SideWar.Runtime;
using SideWar.Runtime.Composition;
using Xunit;

namespace SideWar.App.SmokeTests;

/// <summary>T4.10: a raiz de composição restaura o que gravou (sem janelas, sem T-Game; pasta temporária).</summary>
public sealed class CompositionPersistenceTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "SideWar-T410-" + Guid.NewGuid().ToString("N"));

    private sealed class ManualClock : IClock
    {
        public long NowTicks { get; set; }
        public double TicksPerSecond => 1000;
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Saved_session_is_restored_by_a_new_runtime()
    {
        Directory.CreateDirectory(dir);
        var log = new List<string>();
        var clock = new ManualClock();
        var first = new GameRuntime(new GameRuntimeOptions { Clock = clock });
        first.CompleteStartup();
        first.NotifyInteraction();
        for (int i = 0; i < 300; i++) { clock.NowTicks += 34; first.Host.OnWake(clock); }
        first.Host.OnCommand(new BuildTower(TowerType.Bolt, 300));
        clock.NowTicks += 34;
        first.Host.OnWake(clock);
        Assert.True(first.State.Tick > 0);

        var store = new SessionPersistence(dir, "0.0.0-test", log.Add);
        var (session, profile) = store.Encode(first);
        Assert.True(store.Write(session, profile));
        Assert.Same(session, store.LastSessionBytes);

        var reopened = new SessionPersistence(dir, "0.0.0-test", log.Add);
        var second = new GameRuntime(new GameRuntimeOptions { Clock = new ManualClock(), Restore = reopened.Restore });
        Assert.NotNull(second.Restored);
        Assert.Equal(first.State.ComputeStateHash(), second.State.ComputeStateHash());
        Assert.Empty(log);
    }

    [Fact]
    public void Missing_session_starts_a_new_game()
    {
        Directory.CreateDirectory(dir);
        var store = new SessionPersistence(dir, "0.0.0-test", _ => { });
        var runtime = new GameRuntime(new GameRuntimeOptions { Clock = new ManualClock(), Restore = store.Restore });
        Assert.Null(runtime.Restored);
        Assert.Equal(1, runtime.State.Stage);
    }
}
