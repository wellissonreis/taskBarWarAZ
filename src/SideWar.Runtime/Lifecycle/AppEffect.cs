using SideWar.Runtime.Timing;

namespace SideWar.Runtime.Lifecycle;

/// <summary>An instruction emitted by <see cref="AppStateMachine"/>; the host executes it (SDD-07 RF-07.1).</summary>
public readonly record struct AppEffect(
    AppEffectKind Kind,
    HiddenReason HiddenReason = HiddenReason.None,
    PauseReason PauseReason = PauseReason.None,
    SaveTrigger SaveTrigger = SaveTrigger.Hidden,
    bool EcoQosOn = false)
{
    /// <param name="cause">The reason whose removal made the overlay visible (User counts as interaction, SDD-25).</param>
    public static AppEffect Show(HiddenReason cause) => new(AppEffectKind.Show, HiddenReason: cause);

    /// <param name="cause">The reason whose entry hid the overlay.</param>
    public static AppEffect Hide(HiddenReason cause) => new(AppEffectKind.Hide, HiddenReason: cause);

    public static AppEffect Pause(PauseReason reason) => new(AppEffectKind.Pause, PauseReason: reason);

    public static AppEffect Resume(PauseReason reason) => new(AppEffectKind.Resume, PauseReason: reason);

    public static AppEffect Save(SaveTrigger trigger) => new(AppEffectKind.Save, SaveTrigger: trigger);

    public static AppEffect ReleaseCaches() => new(AppEffectKind.ReleaseCaches);

    public static AppEffect RestoreContext() => new(AppEffectKind.RestoreContext);

    public static AppEffect SetEcoQos(bool on) => new(AppEffectKind.SetEcoQos, EcoQosOn: on);
}
