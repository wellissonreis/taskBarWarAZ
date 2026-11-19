using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Input;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Td;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>Primeira execução (perfil vazio): tutorial visível, nenhuma onda começa sozinha, fase 1 pausada até a 1ª torre.</summary>
public sealed class TdFirstRunTests
{
    private const int Width = 1000;

    private sealed class Host : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
        public void RequestExit() { }
        public bool HudMinimized { get; set; } = true; // padrão de TdSettings.HudMinimized
        public void SetHudMinimized(bool minimized) => HudMinimized = minimized;
        public void StartStage(int stageId) { }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => stageId == 1;
        public int StageStars(int stageId) => 0;
        public bool StageCrown(int stageId) => false;
        public int StageMedals(int stageId) => 0;
        public int StageBestScore(int stageId) => 0;
        public TdRankProgress Rank => TdRank.Progress(0);
        public TdRecordOutcome LastOutcome => default;
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => true;
    }

    private sealed class FreshStore : ITdOnboardingStore
    {
        public bool TutorialDone { get; private set; }
        public void SetTutorialDone(bool done) => TutorialDone = done;
        public bool TipSeen(string id) => false;
        public void MarkTipSeen(string id) { }
    }

    private static (TdMatchState Match, TdGameHost Game, TdUiController Ui, FakeClock Clock) Fresh(bool continuous)
    {
        // Mesmos padrões de uma instalação nova: AppSettings.Default (ondas contínuas, auto-início desligado, tutorial não feito).
        var options = continuous ? TdMatchOptions.Default : TdMatchOptions.Classic;
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54), null, options);
        match.Apply(new LayoutChanged(StripLayout.Create(Width, Width - 46, 88, 4, Width - 80, TowerHost.Band, [new FreeZone(32, Width - 32)])));
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        var clock = new FakeClock();
        var game = new TdGameHost(clock, app, match, null, new TdFocusState());
        game.ApplyAutoStart(false, 15); // AppSettings.Default: auto-início desligado
        TdUiController? ui = null;
        ui = new TdUiController(() => match, c => game.OnCommand(c), new Host(), () => 0, new FreshStore(), null);
        game.WaveGate = () => !ui.StartBlocked; // igual ao TdUiPresenter
        ui.Sync();
        game.OnWake(clock);
        return (match, game, ui, clock);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Empty_profile_shows_the_tutorial_and_no_wave_starts_by_itself(bool continuous)
    {
        var (match, game, ui, clock) = Fresh(continuous);
        Assert.Equal(TdTutorialStep.HudOpen, ui.Step);
        Assert.Equal(AutoStartMode.Off, match.AutoStart);
        long step = (long)(clock.TicksPerSecond / 30);
        for (int i = 0; i < 30 * 600; i++) { clock.AdvanceTicks(step); game.OnWake(clock); ui.Sync(); }
        Assert.Equal(MatchPhase.Preparo, match.Phase);
        Assert.Equal(0, match.WaveNumber);
        Assert.Equal(TdTutorialStep.HudOpen, ui.Step);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stage_1_stays_paused_until_the_first_tower_even_if_a_wave_is_requested(bool continuous)
    {
        var (match, game, ui, clock) = Fresh(continuous);
        Assert.True(ui.StartBlocked);
        game.RequestWave();
        game.OnWake(clock);
        Assert.Equal(MatchPhase.Preparo, match.Phase);
        Assert.Equal(0, match.WaveNumber);
    }
}
