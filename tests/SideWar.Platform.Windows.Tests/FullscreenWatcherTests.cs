using System.Diagnostics;
using SideWar.Platform.Windows.Fullscreen;
using SideWar.Runtime;
using SideWar.Runtime.Fullscreen;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

/// <summary>T-06.5: a real borderless window in another process (TestFullscreenWindow) must flip the watcher.</summary>
[Trait("Env", "WindowsInteractive")]
public sealed class FullscreenWatcherTests
{
    /// <summary>
    /// Own STA thread with a message pump (the hooks are OUTOFCONTEXT). Not a GameThread on purpose: GameThread tests
    /// register the shared "SideWar.Scene" class and cannot run concurrently with another GameThread.
    /// </summary>
    private sealed class Pump : IDisposable
    {
        private readonly Thread thread;
        private readonly ManualResetEventSlim stop = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<Action> queue = new();
        private readonly ManualResetEventSlim posted = new();
        public FullscreenWatcher? Watcher;

        public Pump()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "FullscreenWatcherTests.Pump" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public Task<T> Invoke<T>(Func<T> f)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(() => { try { tcs.SetResult(f()); } catch (Exception e) { tcs.SetException(e); } });
            posted.Set();
            return tcs.Task;
        }

        private void Run()
        {
            while (!stop.IsSet)
            {
                while (queue.TryDequeue(out var a)) a();
                Watcher?.OnWake();
                int wait = 50; // short slice keeps the test simple; deadlines are honoured within 50 ms
                if (Watcher?.NextDeadline is { } d)
                    wait = (int)Math.Clamp((d - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency, 0, 50);
                var handles = new HANDLE[] { new(posted.WaitHandle.SafeWaitHandle.DangerousGetHandle()) };
                PInvoke.MsgWaitForMultipleObjectsEx(handles, (uint)wait, QUEUE_STATUS_FLAGS.QS_ALLINPUT, MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
                posted.Reset();
                while (PInvoke.PeekMessage(out var m, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
                {
                    PInvoke.TranslateMessage(m);
                    PInvoke.DispatchMessage(m);
                }
            }
            Watcher?.Dispose();
        }

        public void Dispose()
        {
            stop.Set();
            posted.Set();
            thread.Join(5000);
            stop.Dispose();
            posted.Dispose();
        }
    }

    private sealed class StopwatchClock : IClock
    {
        public long NowTicks => Stopwatch.GetTimestamp();
        public double TicksPerSecond => Stopwatch.Frequency;
    }

    private const string Script = """
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -Namespace W -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h); [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, int flags, System.UIntPtr extra);'
        $f = New-Object System.Windows.Forms.Form
        $f.FormBorderStyle = 'None'
        $f.StartPosition = 'Manual'
        $f.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $f.TopMost = $true
        $f.Text = 'SideWar.TestFullscreenWindow'
        # Form.Activate() is refused by the foreground lock when another app (e.g. a maximized browser) owns the foreground,
        # so the watcher would never see this window as foreground. A synthetic Alt tap lifts the lock for this process.
        $f.Add_Shown({ [W.U]::keybd_event(0x12, 0, 0, [System.UIntPtr]::Zero); [W.U]::keybd_event(0x12, 0, 2, [System.UIntPtr]::Zero); [void][W.U]::SetForegroundWindow($f.Handle); $f.Activate() })
        [System.Windows.Forms.Application]::Run($f)
        """;

    [Fact]
    public async Task BorderlessWindowOfAnotherProcessHidesAndRestores()
    {
        var errors = new List<Exception>();
        using var pump = new Pump();
        var entered = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitorDevice = new TaskCompletionSource<string>();
        _ = pump.Invoke(() =>
        {
            var probe = new WindowProbe();
            var desktop = probe.Capture(probe.GetForeground());
            // Primary monitor is where the helper window opens; use the foreground's monitor when available.
            var device = string.IsNullOrEmpty(desktop.MonitorDevice) ? "\\\\.\\DISPLAY1" : desktop.MonitorDevice;
            var watcher = new FullscreenWatcher(new StopwatchClock(), HideOnFullscreen.AnyMonitor, device, e => { lock (errors) errors.Add(e); });
            watcher.Transition += t => (t == FullscreenTransition.Entered ? entered : exited).TrySetResult(Stopwatch.GetTimestamp());
            pump.Watcher = watcher;
            monitorDevice.SetResult(device);
            return 0;
        });
        await monitorDevice.Task;

        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(Script));
        using var helper = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -Sta -EncodedCommand {encoded}") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var started = Stopwatch.GetTimestamp();
            var first = await Task.WhenAny(entered.Task, Task.Delay(20_000, TestContext.Current.CancellationToken));
            if (!ReferenceEquals(entered.Task, first))
            {
                var diag = await pump.Invoke(() =>
                {
                    var pr = new WindowProbe();
                    var fg = pr.Capture(pr.GetForeground());
                    return $"fg={fg}; monitors=[{string.Join(',', pump.Watcher!.FullscreenMonitors)}]; helperMainWindow={helper.MainWindowHandle}; exited={helper.HasExited}";
                });
                Assert.Fail("watcher never entered fullscreen: " + diag);
            }
            var enterMs = (await entered.Task - started) * 1000.0 / Stopwatch.Frequency;
            Trace.WriteLine($"T-06.5 enter latency from helper start: {enterMs:F0} ms");

            var killed = Stopwatch.GetTimestamp();
            helper.Kill(true);
            var second = await Task.WhenAny(exited.Task, Task.Delay(5_000, TestContext.Current.CancellationToken));
            Assert.Same(exited.Task, second);
            var exitMs = (await exited.Task - killed) * 1000.0 / Stopwatch.Frequency;
            Trace.WriteLine($"T-06.5 exit latency after close: {exitMs:F0} ms");
            Assert.True(exitMs <= 1500, $"exit latency {exitMs:F0} ms");
        }
        finally
        {
            if (!helper.HasExited) helper.Kill(true);
        }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task WatcherRegistersAppBarProbeAndDisposesCleanly()
    {
        var errors = new List<Exception>();
        using var pump = new Pump();
        var result = await pump.Invoke(() =>
        {
            var w = new FullscreenWatcher(new StopwatchClock(), HideOnFullscreen.SameMonitor, "x", e => { lock (errors) errors.Add(e); });
            var hadDeadline = w.NextDeadline is not null;
            var registered = w.ShellNotificationRegistered;
            w.Dispose();
            return (hadDeadline, registered, afterDispose: w.NextDeadline);
        });
        Assert.True(result.hadDeadline);
        Assert.Null(result.afterDispose);
        Trace.WriteLine($"AppBar ABM_NEW registered: {result.registered}");
        Assert.Empty(errors);
    }
}
