using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Power;

namespace SideWar.Platform.Windows.Session;

/// <summary>Effects to execute in order plus whether the host must schedule the 1 s re-enumeration (RF-07.7).</summary>
public readonly record struct SessionPowerResult(IReadOnlyList<AppEffect> Effects, bool ScheduleReenumeration)
{
    public static readonly SessionPowerResult Empty = new([], false);
}

/// <summary>
/// Translates WTS session codes, PBT_* events and power-setting values into AppStateMachine calls
/// (SDD-07 RF-07.1, RF-07.5, RF-07.7). Pure and T-Game only; the HWND plumbing is <see cref="SessionPowerWatcher"/>.
/// </summary>
public sealed class SessionPowerCoordinator
{
    public static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");
    public static readonly Guid PowerSavingStatus = new("e00958c0-c213-4ace-ac77-fecced2eeea5");
    public static readonly Guid EnergySaverStatus = new("550e8400-e29b-41d4-a716-446655440000");
    public static readonly Guid AcDcPowerSource = new("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548");

    public const uint WtsConsoleConnect = 1, WtsConsoleDisconnect = 2, WtsRemoteConnect = 3,
        WtsRemoteDisconnect = 4, WtsSessionLock = 7, WtsSessionUnlock = 8;

    private readonly AppStateMachine machine;
    private bool powerSaving, onBattery, saverOnBattery = true, forceSaver;
    private uint energySaver;

    public SessionPowerCoordinator(AppStateMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        this.machine = machine;
    }

    /// <summary><c>settings.performance.saverOnBattery</c> (default true).</summary>
    public SessionPowerResult SetSaverOnBattery(bool value) { saverOnBattery = value; return Reevaluate(); }

    /// <summary><c>settings.performance.forceSaver</c>.</summary>
    public SessionPowerResult SetForceSaver(bool value) { forceSaver = value; return Reevaluate(); }

    public SessionPowerResult OnSessionChange(uint code) => code switch
    {
        WtsSessionLock => Enter(HiddenReason.SessionLocked),
        WtsSessionUnlock => Leave(HiddenReason.SessionLocked),
        WtsConsoleDisconnect or WtsRemoteDisconnect => Enter(HiddenReason.RemoteDisconnected),
        WtsConsoleConnect or WtsRemoteConnect => Leave(HiddenReason.RemoteDisconnected),
        _ => SessionPowerResult.Empty,
    };

    public SessionPowerResult OnSuspend() => Enter(HiddenReason.Suspended);

    /// <summary>PBT_APMRESUMEAUTOMATIC: removes Suspended and asks for the delayed re-enumeration.</summary>
    public SessionPowerResult OnResumeAutomatic()
    {
        var left = machine.LeaveHidden(HiddenReason.Suspended);
        return new SessionPowerResult(left, true);
    }

    /// <summary>PBT_POWERSETTINGCHANGE for one registered GUID; unknown GUIDs are ignored.</summary>
    public SessionPowerResult OnPowerSetting(Guid setting, uint value)
    {
        if (setting == ConsoleDisplayState)
            return value == 0 ? Enter(HiddenReason.DisplayOff) : Leave(HiddenReason.DisplayOff);
        if (setting == PowerSavingStatus) powerSaving = value == 1;
        else if (setting == EnergySaverStatus) energySaver = value;
        else if (setting == AcDcPowerSource) onBattery = value == 1;
        else return SessionPowerResult.Empty;
        return Reevaluate();
    }

    private SessionPowerResult Enter(HiddenReason r) => new(machine.EnterHidden(r), false);

    private SessionPowerResult Leave(HiddenReason r) => new(machine.LeaveHidden(r), false);

    private SessionPowerResult Reevaluate()
    {
        PowerProfile profile = PowerProfileResolver.Resolve(powerSaving, energySaver, onBattery, saverOnBattery, forceSaver);
        return new SessionPowerResult(machine.SetPowerProfile(profile), false);
    }
}
