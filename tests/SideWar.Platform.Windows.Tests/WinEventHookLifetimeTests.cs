using System.Runtime.CompilerServices;
using SideWar.Platform.Windows.Fullscreen;
using Windows.Win32;
using Windows.Win32.UI.Accessibility;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

/// <summary>
/// Regression for the FailFast "callback was made on a garbage collected delegate ... WINEVENTPROC": the native
/// delegate must stay reachable while the hook exists. Non-interactive: installs an out-of-context hook on a
/// rarely-fired event, never opens a window and never touches the mouse.
/// </summary>
public sealed class WinEventHookLifetimeTests
{
    private const uint DesktopSwitch = 0x0020; // EVENT_SYSTEM_DESKTOPSWITCH
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WinEventHook Hook, WeakReference Target) Install()
    {
        var target = new object();
        var hook = WinEventHook.Install(DesktopSwitch, DesktopSwitch,
            new WINEVENTPROC((_, _, _, _, _, _, _) => GC.KeepAlive(target)), 0, 0, true);
        return (hook, new WeakReference(target));
    }

    private static void FullCollect()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
        }
    }

    [Fact]
    public void Delegate_stays_alive_while_hook_is_installed_and_is_released_after_dispose()
    {
        var (hook, target) = Install();
        FullCollect();
        Assert.True(hook.IsInstalled);
        Assert.True(target.IsAlive, "native delegate was collected while the hook was installed");

        hook.Dispose();
        Assert.False(hook.IsInstalled);
        GC.KeepAlive(hook);
    }
}
