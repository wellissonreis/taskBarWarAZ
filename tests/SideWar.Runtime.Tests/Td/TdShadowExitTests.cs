using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>Sair do modo sombra não pode parar o jogo (Preparo ou Onda, auto-início Imediato/Contagem, velocidades extras).</summary>
public sealed class TdShadowExitTests
{
    private readonly ITestOutputHelper output;
    public TdShadowExitTests(ITestOutputHelper output) => this.output = output;

    private static (TdGameHost Host, TdMatchState Match, FakeClock Clock) Rig(AutoStartMode mode, bool extras, int speed)
    {
        var clock = new FakeClock();
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(42, 54));
        var host = new TdGameHost(clock, app, match) { ExtraSpeeds = extras, GameSpeed = speed };
        host.OnCommand(new LayoutChanged(TdSanityBot.TypicalLayout()));
        host.OnCommand(new BuildTower("bolt", 600));
        host.OnCommand(new SetAutoStart(mode, 5));
        host.OnWake(clock);
        return (host, match, clock);
    }

    private static void Frames(TdGameHost host, FakeClock clock, int n)
    {
        for (int i = 0; i < n; i++) { clock.AdvanceTicks((long)(clock.TicksPerSecond / 30)); host.OnWake(clock); }
    }

    [Theory]
    [InlineData(AutoStartMode.Immediate, false, 1, false)]
    [InlineData(AutoStartMode.Immediate, true, 5, false)]
    [InlineData(AutoStartMode.Immediate, true, TdSpeed.Max, false)]
    [InlineData(AutoStartMode.Immediate, false, 1, true)]
    [InlineData(AutoStartMode.Immediate, true, 10, true)]
    [InlineData(AutoStartMode.Countdown, true, 5, false)]
    [InlineData(AutoStartMode.Countdown, false, 1, true)]
    [InlineData(AutoStartMode.Immediate, false, 1, false, true)]
    [InlineData(AutoStartMode.Immediate, true, 5, true, true)]
    [InlineData(AutoStartMode.Countdown, true, 5, false, true)]
    public void Leaving_shadow_keeps_the_game_running(AutoStartMode mode, bool extras, int speed, bool leaveDuringWave, bool autoPause = false)
    {
        var (host, match, clock) = Rig(mode, extras, speed);
        if (autoPause) { host.Alerts.Settings.Enabled = true; host.Alerts.Settings.AutoPause = true; }
        host.OnCommand(new SetShadow(true));
        Frames(host, clock, 60);
        Assert.True(match.Shadow);
        if (leaveDuringWave)
        {
            for (int i = 0; i < 2000 && match.Phase != MatchPhase.Onda; i++) Frames(host, clock, 1);
            Assert.Equal(MatchPhase.Onda, match.Phase);
        }
        host.OnCommand(new SetShadow(false));
        Frames(host, clock, 2);
        long wavesBefore = match.WavesReached;
        long tickBefore = match.Tick;
        Frames(host, clock, 3000);
        output.WriteLine($"phase={match.Phase} shadow={match.Shadow} pending={match.ShadowPending} waves {wavesBefore}->{match.WavesReached} tick {tickBefore}->{match.Tick} paused={match.Paused}");
        Assert.False(host.Alerts.Holding, "partida retida pela proteção depois de sair da sombra");
        Assert.False(match.Shadow);
        Assert.Null(match.ShadowPending);
        Assert.True(match.Tick > tickBefore + 100, "o tempo da partida parou depois de sair da sombra");
        Assert.True(match.WavesReached > wavesBefore || match.Phase == MatchPhase.Vitoria, "nenhuma onda nova depois de sair da sombra");
    }

    [Fact]
    public void Leaving_shadow_with_manual_start_drops_the_frozen_countdown_and_a_manual_wave_still_runs()
    {
        var (host, match, clock) = Rig(AutoStartMode.Off, false, 1);
        host.OnCommand(new SetShadow(true));
        Frames(host, clock, 60);
        Assert.True(match.CountdownRemainingSeconds > 0); // em sombra a contagem roda mesmo com auto-início desligado
        host.OnCommand(new SetShadow(false));
        Frames(host, clock, 5);
        Assert.False(match.Shadow);
        Assert.Equal(0.0, match.CountdownRemainingSeconds); // regressão: ficava congelada
        host.OnCommand(new TdWaveRequest());
        Frames(host, clock, 300);
        Assert.Equal(MatchPhase.Onda, match.Phase);
        Assert.True(match.Tick > 100);
    }
}
