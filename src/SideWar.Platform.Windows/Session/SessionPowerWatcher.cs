using System.Diagnostics;
using System.Runtime.InteropServices;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Session;

/// <summary>
/// Registers WTS session and power-setting notifications on the SceneWindow (top-level, SDD-07 §3) and feeds
/// <see cref="SessionPowerCoordinator"/>. T-Game only. Dispose unregisters everything (RF-07.10 step 3).
/// </summary>
public sealed class SessionPowerWatcher : IDisposable
{
    private const uint WmPowerBroadcast = 0x218, WmWtsSessionChange = 0x2B1;
    private const nuint PbtApmSuspend = 0x4, PbtApmResumeAutomatic = 0x12, PbtPowerSettingChange = 0x8013;
    private const uint NotifyForThisSession = 0;
    private static readonly Guid[] Settings =
    [
        SessionPowerCoordinator.ConsoleDisplayState, SessionPowerCoordinator.PowerSavingStatus,
        SessionPowerCoordinator.EnergySaverStatus, SessionPowerCoordinator.AcDcPowerSource,
    ];

    private readonly GameThread owner;
    private readonly SceneWindow scene;
    private readonly SessionPowerCoordinator coordinator;
    private readonly Action<SessionPowerResult> sink;
    private readonly Action<string> log;
    private readonly List<SafeHandle> registrations = [];
    private SafeFileHandle? recipient;
    private HWND hwnd;
    private bool sessionRegistered, disposed;

    /// <param name="sink">Receives every non-empty result on T-Game; the host executes the effects.</param>
    public SessionPowerWatcher(GameThread owner, SceneWindow scene, SessionPowerCoordinator coordinator,
        Action<SessionPowerResult> sink, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(sink);
        this.owner = owner;
        this.scene = scene;
        this.coordinator = coordinator;
        this.sink = sink;
        this.log = log ?? (m => Trace.WriteLine(m));
        owner.VerifyAccess();
    }

    public int RegisteredPowerSettings => registrations.Count;
    public bool SessionRegistered => sessionRegistered;

    /// <summary>Registers WTS and the four power settings; a failing power GUID is logged and ignored (SDD-07 risk table).</summary>
    public unsafe void Start()
    {
        owner.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sessionRegistered) return;
        hwnd = new HWND(scene.Handle);
        recipient = new SafeFileHandle(scene.Handle, ownsHandle: false); // non-owning wrapper of the HWND for the SafeHandle overload
        scene.SystemMessageHandler = OnMessage;
        sessionRegistered = PInvoke.WTSRegisterSessionNotification(hwnd, NotifyForThisSession);
        if (!sessionRegistered) log($"WTSRegisterSessionNotification failed ({Marshal.GetLastPInvokeError()})");
        foreach (Guid setting in Settings)
        {
            Guid g = setting;
            var handle = PInvoke.RegisterPowerSettingNotification(recipient!, in g, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
            if (handle.IsInvalid) { log($"RegisterPowerSettingNotification {setting} failed ({Marshal.GetLastWin32Error()})"); handle.Dispose(); }
            else registrations.Add(handle);
        }
    }

    internal unsafe bool OnMessage(uint message, nuint wParam, nint lParam)
    {
        SessionPowerResult result;
        switch (message)
        {
            case WmWtsSessionChange:
                result = coordinator.OnSessionChange((uint)wParam);
                break;
            case WmPowerBroadcast when wParam == PbtApmSuspend:
                result = coordinator.OnSuspend();
                break;
            case WmPowerBroadcast when wParam == PbtApmResumeAutomatic:
                result = coordinator.OnResumeAutomatic();
                break;
            case WmPowerBroadcast when wParam == PbtPowerSettingChange && lParam != 0:
                var data = (POWERBROADCAST_SETTING*)lParam;
                if (data->DataLength < sizeof(uint)) return false;
                result = coordinator.OnPowerSetting(data->PowerSetting, *(uint*)&data->Data);
                break;
            default:
                return false;
        }
        if (result.Effects.Count > 0 || result.ScheduleReenumeration) sink(result);
        return true;
    }

    public unsafe void Dispose()
    {
        if (disposed) return;
        disposed = true;
        scene.SystemMessageHandler = null;
        foreach (SafeHandle registration in registrations)
        {
            try { registration.Dispose(); } // releases via UnregisterPowerSettingNotification
            catch (Exception e) { log($"UnregisterPowerSettingNotification: {e.Message}"); }
        }
        registrations.Clear();
        recipient?.Dispose();
        if (sessionRegistered)
        {
            try { PInvoke.WTSUnRegisterSessionNotification(hwnd); }
            catch (Exception e) { log($"WTSUnRegisterSessionNotification: {e.Message}"); }
            sessionRegistered = false;
        }
    }
}
