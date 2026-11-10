using Xunit;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Power;
using SideWar.Runtime.Timing;

namespace SideWar.Runtime.Tests.Lifecycle;

public sealed class AppStateMachineTests
{
    private static readonly HiddenReason[] Reasons =
    [
        HiddenReason.User, HiddenReason.Fullscreen, HiddenReason.SessionLocked, HiddenReason.RemoteDisconnected,
        HiddenReason.DisplayOff, HiddenReason.Suspended, HiddenReason.Starting,
    ];

    private const HiddenReason EcoMask =
        HiddenReason.User | HiddenReason.SessionLocked | HiddenReason.DisplayOff | HiddenReason.Suspended;

    public static TheoryData<HiddenReason, HiddenReason> Pairs()
    {
        TheoryData<HiddenReason, HiddenReason> data = [];
        foreach (HiddenReason a in Reasons)
        {
            foreach (HiddenReason b in Reasons)
            {
                if (a != b)
                {
                    data.Add(a, b);
                }
            }
        }

        return data;
    }

    private static AppStateMachine Visible()
    {
        AppStateMachine m = new();
        m.LeaveHidden(HiddenReason.Starting);
        return m;
    }

    private static AppEffectKind[] Kinds(IReadOnlyList<AppEffect> e) => [.. e.Select(x => x.Kind)];

    [Fact]
    public void StartsHiddenStartingWithoutEffects()
    {
        AppStateMachine m = new();
        Assert.Equal(HiddenReason.Starting, m.HiddenReasons);
        Assert.False(m.IsVisible);
        Assert.Equal(PauseReason.Hidden, m.PauseReasons);
        Assert.False(m.SimulationRunning);
    }

    [Fact]
    public void FirstFrameReadyShowsWithResume()
    {
        AppStateMachine m = new();
        IReadOnlyList<AppEffect> e = m.LeaveHidden(HiddenReason.Starting);
        Assert.Equal([AppEffectKind.RestoreContext, AppEffectKind.Show, AppEffectKind.Resume], Kinds(e));
        Assert.Equal(HiddenReason.Starting, e[1].HiddenReason);
        Assert.Equal(PauseReason.Hidden, e[2].PauseReason);
        Assert.True(m.IsVisible);
        Assert.True(m.SimulationRunning);
    }

    [Theory]
    [InlineData(HiddenReason.Fullscreen, false)]
    [InlineData(HiddenReason.RemoteDisconnected, false)]
    [InlineData(HiddenReason.User, true)]
    [InlineData(HiddenReason.SessionLocked, true)]
    [InlineData(HiddenReason.DisplayOff, true)]
    public void SingleHideSequenceFollowsRf073(HiddenReason reason, bool eco)
    {
        AppStateMachine m = Visible();
        IReadOnlyList<AppEffect> e = m.EnterHidden(reason);
        AppEffectKind[] expected = eco
            ? [AppEffectKind.Pause, AppEffectKind.Hide, AppEffectKind.Save, AppEffectKind.SetEcoQos]
            : [AppEffectKind.Pause, AppEffectKind.Hide, AppEffectKind.Save];
        Assert.Equal(expected, Kinds(e));
        Assert.Equal(PauseReason.Hidden, e[0].PauseReason);
        Assert.Equal(SaveTrigger.Hidden, e[2].SaveTrigger);
        Assert.Equal(eco, m.EcoQosOn);
        Assert.Equal(PauseReason.Hidden, m.PauseReasons);
    }

    [Fact]
    public void SuspendSavesWithSuspendedTriggerAndEco()
    {
        AppStateMachine m = Visible();
        IReadOnlyList<AppEffect> e = m.EnterHidden(HiddenReason.Suspended);
        Assert.Equal(SaveTrigger.Suspended, e.Single(x => x.Kind == AppEffectKind.Save).SaveTrigger);
        Assert.True(m.EcoQosOn);
    }

    [Fact]
    public void ShowSequenceTurnsEcoOffBeforeFrame()
    {
        AppStateMachine m = Visible();
        m.EnterHidden(HiddenReason.SessionLocked);
        IReadOnlyList<AppEffect> e = m.LeaveHidden(HiddenReason.SessionLocked);
        Assert.Equal(
            [AppEffectKind.SetEcoQos, AppEffectKind.RestoreContext, AppEffectKind.Show, AppEffectKind.Resume],
            Kinds(e));
        Assert.False(e[0].EcoQosOn);
        Assert.Equal(HiddenReason.SessionLocked, e[2].HiddenReason);
        Assert.False(m.EcoQosOn);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TwoSimultaneousReasonsHideOnceAndShowOnceInEitherLeaveOrder(HiddenReason a, HiddenReason b)
    {
        foreach (bool leaveAFirst in new[] { true, false })
        {
            AppStateMachine m = Visible();
            List<AppEffect> all = [];
            all.AddRange(m.EnterHidden(a));
            IReadOnlyList<AppEffect> second = m.EnterHidden(b);
            all.AddRange(second);

            Assert.Equal(a | b, m.HiddenReasons);
            Assert.Equal(PauseReason.Hidden, m.PauseReasons);
            Assert.Single(all, x => x.Kind == AppEffectKind.Hide);
            Assert.Single(all, x => x.Kind == AppEffectKind.Pause);
            Assert.DoesNotContain(second, x => x.Kind is AppEffectKind.Hide or AppEffectKind.Pause);
            Assert.Equal(((a | b) & EcoMask) != 0, m.EcoQosOn);

            HiddenReason first = leaveAFirst ? a : b;
            HiddenReason last = leaveAFirst ? b : a;
            IReadOnlyList<AppEffect> mid = m.LeaveHidden(first);
            Assert.DoesNotContain(mid, x => x.Kind is AppEffectKind.Show or AppEffectKind.Resume or AppEffectKind.RestoreContext);
            Assert.Equal(last, m.HiddenReasons);
            Assert.False(m.IsVisible);
            Assert.Equal((last & EcoMask) != 0, m.EcoQosOn);

            IReadOnlyList<AppEffect> end = m.LeaveHidden(last);
            Assert.Single(end, x => x.Kind == AppEffectKind.Show);
            Assert.Equal(last, end.Single(x => x.Kind == AppEffectKind.Show).HiddenReason);
            Assert.Equal(
                [AppEffectKind.RestoreContext, AppEffectKind.Show, AppEffectKind.Resume],
                Kinds(end.Where(x => x.Kind != AppEffectKind.SetEcoQos).ToList()));
            Assert.True(m.IsVisible);
            Assert.False(m.EcoQosOn);
            Assert.Equal(PauseReason.None, m.PauseReasons);
        }
    }

    [Fact]
    public void SuspendWhileAlreadyHiddenStillSavesSynchronously()
    {
        AppStateMachine m = Visible();
        m.EnterHidden(HiddenReason.Fullscreen);
        IReadOnlyList<AppEffect> e = m.EnterHidden(HiddenReason.Suspended);
        Assert.Equal([AppEffectKind.Save, AppEffectKind.SetEcoQos], Kinds(e));
        Assert.Equal(SaveTrigger.Suspended, e[0].SaveTrigger);
    }

    [Fact]
    public void RepeatedEnterAndLeaveOfUnsetReasonAreNoOps()
    {
        AppStateMachine m = Visible();
        Assert.Empty(m.LeaveHidden(HiddenReason.User));
        m.EnterHidden(HiddenReason.User);
        Assert.Empty(m.EnterHidden(HiddenReason.User));
        m.LeaveHidden(HiddenReason.User);
        Assert.Empty(m.LeaveHidden(HiddenReason.User));
    }

    [Fact]
    public void CombinedFlagsInOneCallHideOnce()
    {
        AppStateMachine m = Visible();
        IReadOnlyList<AppEffect> e = m.EnterHidden(HiddenReason.Fullscreen | HiddenReason.User);
        Assert.Single(e, x => x.Kind == AppEffectKind.Hide);
        Assert.Equal(HiddenReason.Fullscreen | HiddenReason.User, m.HiddenReasons);
    }

    [Fact]
    public void ManualPausesSurviveHideAndShowWithoutAutoConversion()
    {
        AppStateMachine m = Visible();
        Assert.Equal([AppEffectKind.Pause], Kinds(m.SetPause(PauseReason.User)));
        Assert.Empty(m.SetPause(PauseReason.User));
        m.EnterHidden(HiddenReason.Fullscreen);
        Assert.Equal(PauseReason.User | PauseReason.Hidden, m.PauseReasons);
        m.LeaveHidden(HiddenReason.Fullscreen);
        Assert.Equal(PauseReason.User, m.PauseReasons);
        Assert.False(m.SimulationRunning);
        Assert.Equal([AppEffectKind.Resume], Kinds(m.ClearPause(PauseReason.User)));
        Assert.Empty(m.ClearPause(PauseReason.User));
        Assert.True(m.SimulationRunning);
    }

    [Fact]
    public void DialogPausesAndHiddenCannotBeSetDirectly()
    {
        AppStateMachine m = Visible();
        m.SetPause(PauseReason.Dialog);
        Assert.False(m.SimulationRunning);
        Assert.Throws<ArgumentOutOfRangeException>(() => m.SetPause(PauseReason.Hidden));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.SetPause(PauseReason.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.ClearPause(PauseReason.User | PauseReason.Dialog));
    }

    [Fact]
    public void SaverTurnsEcoOnAndStaysOnWhileHiddenReasonPersists()
    {
        AppStateMachine m = Visible();
        Assert.Equal([AppEffectKind.SetEcoQos], Kinds(m.SetPowerProfile(PowerProfile.Saver)));
        Assert.Empty(m.SetPowerProfile(PowerProfile.Saver));
        m.EnterHidden(HiddenReason.User);
        Assert.True(m.EcoQosOn);
        IReadOnlyList<AppEffect> e = m.LeaveHidden(HiddenReason.User);
        Assert.DoesNotContain(e, x => x.Kind == AppEffectKind.SetEcoQos);
        Assert.True(m.EcoQosOn);
        Assert.Equal([AppEffectKind.SetEcoQos], Kinds(m.SetPowerProfile(PowerProfile.Normal)));
        Assert.False(m.EcoQosOn);
    }

    [Fact]
    public void ReleaseCachesOnlyOnceAndOnlyWhileHidden()
    {
        AppStateMachine m = Visible();
        Assert.Empty(m.ReleaseCachesTimerElapsed());
        m.EnterHidden(HiddenReason.Fullscreen);
        Assert.Equal([AppEffectKind.ReleaseCaches], Kinds(m.ReleaseCachesTimerElapsed()));
        Assert.Empty(m.ReleaseCachesTimerElapsed());
        m.LeaveHidden(HiddenReason.Fullscreen);
        Assert.Empty(m.ReleaseCachesTimerElapsed());
        m.EnterHidden(HiddenReason.Fullscreen);
        Assert.Equal([AppEffectKind.ReleaseCaches], Kinds(m.ReleaseCachesTimerElapsed()));
        Assert.Equal(60, AppStateMachine.ReleaseCachesDelaySeconds);
    }

    [Theory]
    [InlineData(HiddenReason.None)]
    [InlineData((HiddenReason)1024)]
    public void InvalidReasonsThrow(HiddenReason bad)
    {
        AppStateMachine m = Visible();
        Assert.Throws<ArgumentOutOfRangeException>(() => m.EnterHidden(bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.LeaveHidden(bad));
    }
}
