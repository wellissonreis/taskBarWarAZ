using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Taskbar;
using SideWar.Runtime.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChangedTd = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.28c: velocidade 1x/2x/3x do host (determinística, replay igual), troca de altura da faixa ao vivo (layout/hit-test) e fps sem depender do foco.</summary>
public sealed class TdSpeedAndLiveBandTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();

    private sealed class CountingSink : ITdFrameSink
    {
        public int Frames;
        public void Present(TdMatchState match, double nowSeconds, in TdView view) => Frames++;
    }

    private static (TdMatchState Match, TdGameHost Host, FakeClock Clock, AppStateMachine App) Rig(int speed)
    {
        var clock = new FakeClock();
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        var match = TdMatchState.Create(Content, 1, Rng.Seed(42, 54));
        var host = new TdGameHost(clock, app, match) { GameSpeed = speed };
        host.OnCommand(new LayoutChangedTd(StripLayoutFactory.Create(BuildZoneMap.TdBand(1200), 1200)));
        host.OnWake(clock);
        return (match, host, clock, app);
    }

    private static void Tick(TdGameHost host, FakeClock clock, int n)
    {
        long step = (long)(clock.TicksPerSecond / 30);
        for (int i = 0; i < n; i++) { clock.AdvanceTicks(step); host.OnWake(clock); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Speed_runs_N_steps_per_tick(int speed)
    {
        var (match, host, clock, _) = Rig(speed);
        host.OnCommand(new BuildTower("bolt", 600));
        host.OnCommand(new StartWave());
        host.OnWake(clock);
        long before = host.StepsExecuted;
        Tick(host, clock, 30);
        Assert.InRange(host.StepsExecuted - before, 30 * speed - speed, 30 * speed + speed);
        Assert.Equal(MatchPhase.Onda, match.Phase);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Replay_of_a_sped_up_match_reproduces_the_same_hash(int speed)
    {
        var (match, host, clock, _) = Rig(speed);
        host.OnCommand(new BuildTower("bolt", 600));
        host.OnWake(clock);
        host.OnCommand(new StartWave());
        Tick(host, clock, 40);
        host.OnCommand(new BuildTower("frost", 500));
        Tick(host, clock, 40);
        var replay = host.BuildReplay(1, 42, 54) with { MaxFrames = match.Frame };
        var result = TdReplayRunner.Run(Content, replay);
        Assert.Equal(match.ComputeStateHash(), result.StateHash);
        Assert.Equal(match.Frame, result.Frames);
    }

    [Fact]
    public void Speed_does_not_move_while_the_game_is_paused_and_is_clamped()
    {
        var (match, host, clock, app) = Rig(3);
        host.OnCommand(new BuildTower("bolt", 600));
        host.OnCommand(new StartWave());
        host.OnWake(clock);
        app.SetPause(SideWar.Runtime.Timing.PauseReason.User);
        long steps = host.StepsExecuted;
        Tick(host, clock, 30);
        Assert.Equal(steps, host.StepsExecuted);
        host.GameSpeed = 99;
        app.ClearPause(SideWar.Runtime.Timing.PauseReason.User);
        long before = host.StepsExecuted;
        Tick(host, clock, 30);
        Assert.InRange(host.StepsExecuted - before, 80, 93); // limitado a 3x
    }

    [Fact]
    public void Speed_selector_in_the_hud_sets_1_2_3_and_marks_the_current_one()
    {
        var host = new SpeedHost();
        var match = TdMatchState.Create(Content, 1, Rng.Seed(1, 54));
        var ui = new TdUiController(() => match, _ => { }, host, () => 0.0, null);
        var chip = ui.BuildHud();
        // chip: UM botão pequeno que mostra a velocidade atual e alterna 1x -> 2x -> 3x -> 1x
        var btn = chip.Items.Single(i => i.Action == TdUiAction.SetSpeed);
        Assert.Equal("1x", btn.Text);
        ui.Activate(btn);
        Assert.Equal(2, host.GameOptions.GameSpeed);
        btn = ui.BuildHud().Items.Single(i => i.Action == TdUiAction.SetSpeed);
        Assert.Equal("2x", btn.Text);
        ui.Activate(btn);
        Assert.Equal(3, host.GameOptions.GameSpeed);
        ui.Activate(ui.BuildHud().Items.Single(i => i.Action == TdUiAction.SetSpeed));
        Assert.Equal(1, host.GameOptions.GameSpeed);
        // aba Jogo: seletor segmentado completo
        ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        var seg = ui.BuildPopover()!.Items.Where(i => i.Action == TdUiAction.SetSpeed).ToArray();
        Assert.Equal(["1x", "2x", "3x"], seg.Select(i => i.Text));
        Assert.True(seg[0].Selected);
    }

    private sealed class SpeedHost : ITdUiHost
    {
        public TdGameOptions GameOptions { get; private set; } = new();
        public void SetGameOptions(TdGameOptions options) => GameOptions = options;
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
        public void RequestExit() { }
        public bool HudMinimized { get; set; } = true;
        public void SetHudMinimized(bool minimized) => HudMinimized = minimized;
        public void StartStage(int stageId) { }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => true;
        public int StageStars(int stageId) => 0;
        public bool StageCrown(int stageId) => false;
        public int StageMedals(int stageId) => 0;
        public int StageBestScore(int stageId) => 0;
        public TdRankProgress Rank => TdRank.Progress(0);
        public TdRecordOutcome LastOutcome { get; set; }
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => true;
    }

    // ---------------------------------------------------------------- altura ao vivo

    private static MonitorInfo Monitor(uint dpi = 96) =>
        new("DISPLAY1", new PixelRect(0, 0, 1920, 1032), new PixelRect(0, 0, 1920, 1032), dpi, true, 0);

    private static TaskbarLayout Bar() =>
        new("DISPLAY1", TaskbarEdge.Bottom, false, default, 48, Array.Empty<AppButton>(), null,
            default, [], 0, TaskbarAlignment.Center, TaskbarSource.Uia, 96);

    [Fact]
    public void Changing_the_band_height_live_updates_layout_scene_and_hit_test_without_restarting()
    {
        var (match, host, clock, _) = Rig(1);
        var watcher = new SceneLayoutWatcher(host, null, l => new LayoutChangedTd(l));
        var small = watcher.Observe(Monitor(), Bar(), new BuildZoneOptions { TdBand = true, TdBandHeightDip = 40 })!.Value;
        host.OnWake(clock);
        Assert.Equal(1920 * 32 / 40, match.Layout!.Width);
        Assert.Equal((int)Math.Round(TdBand.SceneHeightDip(40), MidpointRounding.AwayFromZero), small.Scene.Height);

        // mesma instância do watcher, nova altura: nova cena (maior), novo layout lógico (largura / k) já entregue ao Core
        var large = watcher.Observe(Monitor(), Bar(), new BuildZoneOptions { TdBand = true, TdBandHeightDip = 88 })!.Value;
        host.OnWake(clock);
        Assert.True(large.SceneChanged);
        Assert.True(large.LayoutPosted);
        Assert.True(large.Scene.Height > small.Scene.Height);
        Assert.Equal((int)Math.Round(1920 / TdBand.Scale(88)), match.Layout!.Width);
        Assert.Equal(1032, large.Scene.Bottom); // a base não se move
        Assert.True(TdBand.HeadroomRealDip(88) > TdBand.HeadroomRealDip(40));

        // hit-test: a torre é achada no centro dela (espaço de desenho; a composição divide por k e tira a folga)
        host.OnCommand(new BuildTower("bolt", 300));
        host.OnWake(clock);
        var tower = match.Towers[0];
        float cx = TdBand.TowerCenterDip(match, tower, match.Layout!.Width);
        Assert.Equal(HitTarget.TdTower(tower.Id), TdHitTester.Test(match, cx, 20));
    }

    // ---------------------------------------------------------------- fps

    [Fact]
    public void Render_fps_is_the_same_with_or_without_focus()
    {
        int Frames(bool absent)
        {
            var (match, host, clock, app) = Rig(1);
            var sink = new CountingSink();
            var driver = new TdRenderDriver(host, app, clock, sink) { Absent = absent };
            driver.OnWake(clock);
            host.OnCommand(new StartWave());
            long step = (long)(clock.TicksPerSecond / 30);
            host.OnWake(clock);
            int start = sink.Frames;
            for (int i = 0; i < 60; i++) { clock.AdvanceTicks(step); host.OnWake(clock); driver.OnWake(clock); }
            return sink.Frames - start;
        }
        int present = Frames(false), absent = Frames(true);
        Assert.Equal(present, absent);
        Assert.InRange(present, 58, 61);
    }
}
