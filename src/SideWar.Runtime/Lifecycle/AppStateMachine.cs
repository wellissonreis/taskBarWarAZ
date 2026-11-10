using SideWar.Runtime.Power;
using SideWar.Runtime.Timing;

namespace SideWar.Runtime.Lifecycle;

/// <summary>
/// Pure application lifecycle state machine (SDD-07 RF-07.1..RF-07.3, RF-07.5, RF-07.6, RF-07.7).
/// Callers (T-Game only) report facts and execute the returned effects in order.
/// Hidden means Offline (SDD-25): any <see cref="HiddenReason"/> implies <see cref="PauseReason.Hidden"/>.
/// </summary>
public sealed class AppStateMachine
{
    /// <summary>Delay after Hide before the host should call <see cref="ReleaseCachesTimerElapsed"/> (RF-07.3 step 7).</summary>
    public const double ReleaseCachesDelaySeconds = 60;

    private const HiddenReason EcoQosHiddenReasons =
        HiddenReason.User | HiddenReason.SessionLocked | HiddenReason.DisplayOff | HiddenReason.Suspended;

    private static readonly AppEffect[] None = [];

    private PauseReason manualPauses;
    private PowerProfile powerProfile = PowerProfile.Normal;
    private bool ecoQosOn;
    private bool cachesReleased;

    /// <summary>Starts hidden with <see cref="HiddenReason.Starting"/> and no effects (RF-07.1).</summary>
    public AppStateMachine() => HiddenReasons = HiddenReason.Starting;

    public HiddenReason HiddenReasons { get; private set; }

    public bool IsVisible => HiddenReasons == HiddenReason.None;

    public PowerProfile PowerProfile => powerProfile;

    public bool EcoQosOn => ecoQosOn;

    /// <summary>User and Dialog as set by the host, plus Hidden whenever the overlay is hidden (RF-07.2, SDD-25 RF-25.2).</summary>
    public PauseReason PauseReasons => manualPauses | (IsVisible ? PauseReason.None : PauseReason.Hidden);

    /// <summary>The simulation advances only when nothing pauses it.</summary>
    public bool SimulationRunning => PauseReasons == PauseReason.None;

    /// <summary>Adds hidden reasons. First hide: Pause(Hidden), Hide, Save, SetEcoQos (RF-07.3). Already hidden: no repeat.</summary>
    public IReadOnlyList<AppEffect> EnterHidden(HiddenReason reasons)
    {
        ValidateReasons(reasons);
        HiddenReason added = reasons & ~HiddenReasons;
        if (added == HiddenReason.None)
        {
            return None;
        }

        bool firstHide = IsVisible;
        HiddenReasons |= added;
        List<AppEffect> effects = [];
        if (firstHide)
        {
            cachesReleased = false;
            effects.Add(AppEffect.Pause(PauseReason.Hidden));
            effects.Add(AppEffect.Hide(added));
            effects.Add(AppEffect.Save((added & HiddenReason.Suspended) != 0 ? SaveTrigger.Suspended : SaveTrigger.Hidden));
        }
        else if ((added & HiddenReason.Suspended) != 0)
        {
            // Sleep while already hidden for another reason still needs the synchronous save (RF-07.7).
            effects.Add(AppEffect.Save(SaveTrigger.Suspended));
        }

        AddEcoQosChange(effects);
        return effects;
    }

    /// <summary>
    /// Removes hidden reasons. Last reason removed: SetEcoQos(off) before the frame, RestoreContext, Show, Resume(Hidden)
    /// (RF-07.6, risk table: EcoQoS off before the re-show frame). Reasons that remain: no Show.
    /// </summary>
    public IReadOnlyList<AppEffect> LeaveHidden(HiddenReason reasons)
    {
        ValidateReasons(reasons);
        HiddenReason removed = reasons & HiddenReasons;
        if (removed == HiddenReason.None)
        {
            return None;
        }

        HiddenReasons &= ~removed;
        List<AppEffect> effects = [];
        AddEcoQosChange(effects);
        if (IsVisible)
        {
            effects.Add(AppEffect.RestoreContext());
            effects.Add(AppEffect.Show(removed));
            effects.Add(AppEffect.Resume(PauseReason.Hidden));
        }

        return effects;
    }

    /// <summary>Applies a User or Dialog pause. Hidden is derived and cannot be set directly.</summary>
    public IReadOnlyList<AppEffect> SetPause(PauseReason reason)
    {
        ValidateManualPause(reason);
        if ((manualPauses & reason) != 0)
        {
            return None;
        }

        manualPauses |= reason;
        return [AppEffect.Pause(reason)];
    }

    public IReadOnlyList<AppEffect> ClearPause(PauseReason reason)
    {
        ValidateManualPause(reason);
        if ((manualPauses & reason) == 0)
        {
            return None;
        }

        manualPauses &= ~reason;
        return [AppEffect.Resume(reason)];
    }

    /// <summary>Applies a resolved power profile; Saver turns EcoQoS on (RF-07.5).</summary>
    public IReadOnlyList<AppEffect> SetPowerProfile(PowerProfile profile)
    {
        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        powerProfile = profile;
        List<AppEffect> effects = [];
        AddEcoQosChange(effects);
        return effects;
    }

    /// <summary>The 60 s timer after Hide fired: release caches once per hidden period, only if still hidden.</summary>
    public IReadOnlyList<AppEffect> ReleaseCachesTimerElapsed()
    {
        if (IsVisible || cachesReleased)
        {
            return None;
        }

        cachesReleased = true;
        return [AppEffect.ReleaseCaches()];
    }

    private void AddEcoQosChange(List<AppEffect> effects)
    {
        bool wanted = powerProfile == PowerProfile.Saver || (HiddenReasons & EcoQosHiddenReasons) != 0;
        if (wanted != ecoQosOn)
        {
            ecoQosOn = wanted;
            effects.Add(AppEffect.SetEcoQos(wanted));
        }
    }

    private static void ValidateReasons(HiddenReason reasons)
    {
        const HiddenReason all = HiddenReason.User | HiddenReason.Fullscreen | HiddenReason.SessionLocked |
            HiddenReason.RemoteDisconnected | HiddenReason.DisplayOff | HiddenReason.Suspended | HiddenReason.Starting;
        if (reasons == HiddenReason.None || (reasons & ~all) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reasons), reasons, "Expected one or more defined hidden reasons.");
        }
    }

    private static void ValidateManualPause(PauseReason reason)
    {
        if (reason is not (PauseReason.User or PauseReason.Dialog))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Only User or Dialog can be set; Hidden is derived.");
        }
    }
}
