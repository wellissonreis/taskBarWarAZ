using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using SideWar.Core;
using SideWar.Platform.Windows.Interop;
using SideWar.Runtime;
using SideWar.Platform.Windows.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Threading;

/// <summary>Owns native resources and dispatches all game work on a foreground STA.</summary>
public sealed class GameThread : IGameCommandSink, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly ConcurrentQueue<Action> queue = new();
    private readonly IGameLoopHost host;
    private readonly Action<Exception> reportError;
    private readonly Action<string> log;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? thread;
    private AutoResetEvent? wake;
    private bool accepting;
    private bool stopping;
    private long wakes;

    public GameThread(IGameLoopHost host, Action<Exception> reportError, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(reportError);
        this.host = host;
        this.reportError = reportError;
        this.log = log ?? (message => Trace.WriteLine(message));
        // Failures are surfaced through Ready/Completion/reportError; avoid unobserved-exception noise.
        ready.Task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        completed.Task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    public SceneWindow Scene { get; private set; } = null!;
    public ZOrderCoordinator ZOrder { get; private set; } = null!;
    public DockController Dock { get; private set; } = null!;
    public SceneModeController Mode { get; private set; } = null!;
    public ValueTask DisposeAsync() => new(StopAsync());

    public Task Ready => ready.Task;
    public Task Completion => completed.Task;
    public long WakeCount => Interlocked.Read(ref wakes);
    public bool IsCurrent => Thread.CurrentThread == thread;

    public void Start()
    {
        lock (gate)
        {
            if (thread is not null) throw new InvalidOperationException("GameThread is single-use.");
            thread = new Thread(Run) { Name = "SideWar.Game", IsBackground = false };
            thread.SetApartmentState(ApartmentState.STA);
            accepting = true;
            thread.Start();
        }
    }

    /// <summary>Starts T-Game and waits for Ready (RF-03.1 step 5); on failure or timeout stops and rethrows.</summary>
    public async Task StartAsync(TimeSpan? readyTimeout = null)
    {
        Start();
        try
        {
            await ready.Task.WaitAsync(readyTimeout ?? TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            try { await StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (Exception stopError) { Trace.WriteLine(stopError); }
            throw;
        }
    }

    /// <summary>Test seam: runs on T-Game during startup, before the window exists.</summary>
    internal Action? StartupHook { get; set; }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (gate)
        {
            if (!accepting) throw new InvalidOperationException("GameThread is not accepting work.");
            queue.Enqueue(action);
            wake?.Set(); // Startup consumes queued work before the first wait.
        }
    }

    public void Post(IGameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Post(() => host.OnCommand(command));
    }

    public async Task StopAsync(TimeSpan? timeout = null)
    {
        if (IsCurrent) throw new InvalidOperationException("Cannot join T-Game from itself.");
        Thread? target;
        lock (gate)
        {
            target = thread;
            if (target is null) return;
            if (accepting)
            {
                accepting = false;
                queue.Enqueue(() => stopping = true);
                wake?.Set();
            }
        }
        var limit = timeout ?? TimeSpan.FromSeconds(3);
        var watch = Stopwatch.StartNew();
        try
        {
            try { await completed.Task.WaitAsync(limit).ConfigureAwait(false); }
            catch (Exception error) when (error is not TimeoutException)
            {
                // Fatal failure was already reported by Run; Completion keeps the exception.
            }
            if (!target.Join(TimeSpan.FromTicks(Math.Max(0, (limit - watch.Elapsed).Ticks))))
                throw new TimeoutException("T-Game did not terminate before the shutdown deadline.");
        }
        catch (TimeoutException error)
        {
            Report(error); // Application owns emergency save/exit policy (SDD-07).
            throw;
        }
    }

    internal void VerifyAccess()
    {
        if (!IsCurrent) throw new InvalidOperationException("Operation requires T-Game.");
    }

    internal void Report(Exception error)
    {
        try { reportError(error); }
        catch (Exception handlerError) { Trace.WriteLine(handlerError); }
    }

    private unsafe void Run()
    {
        SafeFileHandle? timer = null;
        bool started = false;
        Exception? fatal = null;
        try
        {
            fixed (char* name = "SideWar.Game")
                PInvoke.SetThreadDescription(PInvoke.GetCurrentThread(), name).ThrowOnFailure();
            StartupHook?.Invoke();
            timer = PInvoke.CreateWaitableTimerEx(null, null,
                PInvoke.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                (uint)SYNCHRONIZATION_ACCESS_RIGHTS.TIMER_ALL_ACCESS);
            if (timer.IsInvalid)
            {
                timer.Dispose();
                log("High-resolution waitable timer unavailable; falling back to standard timer.");
                timer = PInvoke.CreateWaitableTimerEx(null, null, 0,
                    (uint)SYNCHRONIZATION_ACCESS_RIGHTS.TIMER_ALL_ACCESS);
                if (timer.IsInvalid) throw Win32Error.LastError("CreateWaitableTimerEx");
            }
            lock (gate) wake = new AutoResetEvent(false);
            // Ensure the native message queue exists before Ready.
            PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
            // SDD-05 RF-05.1 defense: PMv2 for this thread before any window exists (manifest is the primary source).
            PInvoke.SetThreadDpiAwarenessContext(MonitorService.PerMonitorV2);
            ZOrder = new ZOrderCoordinator(this);
            Scene = new SceneWindow(this);
            Dock = new DockController(this);
            Mode = new SceneModeController(this);
            bool abandoned;
            lock (gate) abandoned = !accepting;
            if (abandoned)
            {
                // Stop (or StartAsync timeout) arrived before Ready: startup is abandoned,
                // the host never ran and must not be shut down.
                ready.TrySetCanceled();
                return;
            }
            started = true;
            ready.TrySetResult();
            var clock = new MonotonicClock();
            HANDLE[] handles = [new(timer.DangerousGetHandle()), new(wake.SafeWaitHandle.DangerousGetHandle())];
            while (!stopping)
            {
                while (queue.TryDequeue(out var action))
                {
                    Guard(action);
                    if (stopping) break;
                }
                if (stopping) break;
                Guard(() => host.OnWake(clock));
                Guard(() => ZOrder.OnWake(clock.NowTicks));
                Guard(() => Dock.OnWake(clock.NowTicks));
                Guard(() => Mode.OnWake(clock.NowTicks));
                long? next = null;
                Guard(() => next = host.NextDeadline);
                if (ZOrder.NextDeadline is { } zDeadline) next = next is { } hDeadline ? Math.Min(hDeadline, zDeadline) : zDeadline;
                if (Dock.NextDeadline is { } dDeadline) next = next is { } nDeadline ? Math.Min(nDeadline, dDeadline) : dDeadline;
                if (Mode.NextDeadline is { } mDeadline) next = next is { } kDeadline ? Math.Min(kDeadline, mDeadline) : mDeadline;
                if (next is { } deadline)
                {
                    var delay = TimeSpan.FromSeconds(Math.Max(0, ((double)deadline - clock.NowTicks) / clock.TicksPerSecond));
                    long due = WaitableTimerDeadline.FromDelay(delay);
                    if (!PInvoke.SetWaitableTimer(timer, due, 0, null, null, false))
                        throw Win32Error.LastError("SetWaitableTimer");
                }
                else if (!PInvoke.CancelWaitableTimer(timer)) throw Win32Error.LastError("CancelWaitableTimer");
                var result = PInvoke.MsgWaitForMultipleObjectsEx(handles, PInvoke.INFINITE,
                    QUEUE_STATUS_FLAGS.QS_ALLINPUT, MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS.MWMO_INPUTAVAILABLE);
                if (result == WAIT_EVENT.WAIT_FAILED) throw Win32Error.LastError("MsgWaitForMultipleObjectsEx");
                Interlocked.Increment(ref wakes);
                while (PInvoke.PeekMessage(out var message, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
                {
                    if (message.message == PInvoke.WM_QUIT) { stopping = true; break; }
                    PInvoke.TranslateMessage(message);
                    PInvoke.DispatchMessage(message);
                }
            }
        }
        catch (Exception error)
        {
            fatal = error;
            ready.TrySetException(error);
            Report(error);
        }
        finally
        {
            lock (gate) accepting = false;
            // RF-03.7: stop -> host.Shutdown -> DestroyWindow/UnregisterClass -> timer -> quit.
            // The host never ran unless startup reached Ready, so it is not shut down otherwise.
            if (started)
                try { host.Shutdown(); } catch (Exception error) { Report(error); }
            try { Scene?.Dispose(); } catch (Exception error) { Report(error); }
            try { timer?.Dispose(); } catch (Exception error) { Report(error); }
            lock (gate)
            {
                wake?.Dispose();
                wake = null;
                while (queue.TryDequeue(out _)) { }
            }
            try { PInvoke.PostQuitMessage(0); } catch (Exception error) { Report(error); }
            if (fatal is null) completed.TrySetResult();
            else completed.TrySetException(fatal);
        }
    }

    // RF-03.8: a failing item is reported and the loop continues; the App decides the policy.
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception error) { Report(error); }
    }

    private sealed class MonotonicClock : IClock
    {
        public long NowTicks => Stopwatch.GetTimestamp();
        public double TicksPerSecond => Stopwatch.Frequency;
    }
}
