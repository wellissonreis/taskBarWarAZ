using System.Reflection;
using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>
/// Regressão do travamento ao iniciar a onda: com a próxima fase travada por patente a mesma fase se repete, e a proteção contra
/// derrota lançava exceção antes de cada Step (a partida parava para sempre e o laço girava sem espera).
/// </summary>
public sealed class TdStageRepeatFreezeTests
{
    [Theory(Timeout = 5000)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(TdSpeed.Max)]
    public async Task Waves_keep_running_after_repeating_a_rank_locked_stage(int speed)
    {
        await Task.Yield();
        var clock = new FakeClock();
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(42, 54), null,
            TdMatchOptions.Default with { InitialReputation = 0 });
        var host = new TdGameHost(clock, app, match) { ExtraSpeeds = true, GameSpeed = speed, BudgetClockMs = () => 0 };
        host.Alerts.Settings.AutoPause = true;
        host.OnCommand(new LayoutChanged(TdSanityBot.TypicalLayout()));
        host.OnCommand(new BuildTower("bolt", 600));
        host.OnWake(clock);

        // Fim da fase 1 duas vezes com patente Estagiário: a fase 2 está travada e a 1 se repete.
        var advance = typeof(TdMatchState).GetMethod("ClearStageAndAdvance", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var active = (System.Collections.IList)typeof(TdMatchState).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(match)!;
        for (int clear = 0; clear < 2; clear++) { active.Clear(); advance.Invoke(match, null); }
        Assert.Equal(1, match.StageNumber);
        Assert.Equal(2, match.StagesCleared);
        Assert.Equal(MatchPhase.Onda, match.Phase);

        long before = host.StepsExecuted;
        for (int i = 0; i < 10; i++)
        {
            clock.AdvanceTicks((long)(clock.TicksPerSecond / 30) + 1);
            host.OnWake(clock);
        }
        Assert.True(host.StepsExecuted >= before + 9);
    }

    /// <summary>Uma falha dentro do wake não pode deixar o prazo no passado (laço girando sem espera).</summary>
    [Fact(Timeout = 5000)]
    public async Task A_failing_wake_still_schedules_the_next_one_in_the_future()
    {
        await Task.Yield();
        var clock = new FakeClock();
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(42, 54));
        bool fail = false;
        var host = new TdGameHost(clock, app, match) { ExtraSpeeds = true, GameSpeed = 10,
            BudgetClockMs = () => fail ? throw new InvalidOperationException("falha simulada") : 0 };
        host.OnCommand(new BuildTower("bolt", 600));
        host.OnCommand(new SetAutoStart(AutoStartMode.Immediate, 5));
        host.OnWake(clock);
        clock.AdvanceTicks((long)(clock.TicksPerSecond / 30) + 1);
        host.OnWake(clock);
        fail = true;
        clock.AdvanceTicks((long)clock.TicksPerSecond);
        Assert.Throws<InvalidOperationException>(() => host.OnWake(clock));
        Assert.True(host.NextDeadline > clock.NowTicks);
    }
}
