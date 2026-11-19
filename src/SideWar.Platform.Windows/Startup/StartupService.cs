using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace SideWar.Platform.Windows.Startup;

public enum StartupStatus { Disabled, Enabled, DisabledByUser, DisabledByPolicy, EnabledByPolicy }

public interface IStartupService
{
    bool IsPackaged { get; }
    Task<StartupStatus> GetStatusAsync();
    Task<StartupStatus> SetEnabledAsync(bool enabled);
}

/// <summary>Controls the opt-in startup task declared in the MSIX manifest.</summary>
public sealed class StartupService : IStartupService
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;
    private const string TaskId = "SideWarStartup";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);

    public bool IsPackaged
    {
        get
        {
            uint length = 0;
            int result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
            return result switch
            {
                ErrorInsufficientBuffer => true,
                AppModelErrorNoPackage => false,
                _ => throw new Win32Exception(result),
            };
        }
    }

    public async Task<StartupStatus> GetStatusAsync() => Map((await StartupTask.GetAsync(TaskId)).State);

    public async Task<StartupStatus> SetEnabledAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (enabled) return Map(await task.RequestEnableAsync());
        task.Disable();
        return Map(task.State);
    }

    private static StartupStatus Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => StartupStatus.Disabled,
        StartupTaskState.Enabled => StartupStatus.Enabled,
        StartupTaskState.EnabledByPolicy => StartupStatus.EnabledByPolicy,
        StartupTaskState.DisabledByUser => StartupStatus.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupStatus.DisabledByPolicy,
        _ => throw new InvalidOperationException($"Unknown startup task state: {state}"),
    };
}
