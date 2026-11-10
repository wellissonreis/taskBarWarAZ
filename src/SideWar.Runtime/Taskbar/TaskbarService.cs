using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Taskbar;

/// <summary>
/// SDD-21 RF-21.3/RF-21.6. Single-threaded on T-Game (members except the probe continuation);
/// time comes from IClock and the host drives it through NextDeadline/OnWake. No timers, no polling.
/// </summary>
public sealed class TaskbarService
{
    public const int DebounceMs = 500, QueryTimeoutMs = 300, RetryMs = 5000, SafetyPollMs = 60_000, MaxFailures = 3;
    private static readonly Meter Meter = new("SideWar.Taskbar");
    private static readonly Counter<long> QueriesCounter = Meter.CreateCounter<long>("taskbar.queries");
    private static readonly Counter<long> UpdatesCounter = Meter.CreateCounter<long>("taskbar.updates");

    private readonly ITaskbarLocator _locator;
    private readonly ITaskbarProbe _probe;
    private readonly IClock _clock;
    private readonly Action? _wake;
    private readonly ConcurrentQueue<(long Id, Task<UiaNode[]> Task)> _completed = new();
    private MonitorInfo _monitor;
    private bool _visible, _inFlight, _timedOut, _pending, _useUia = true, _safetyPoll;
    private long _queryId;
    private long? _debounceAt, _retryAt, _pollAt, _timeoutAt, _lastLogTicks;
    private TaskbarContext _queryContext;
    private int _failures;

    /// <param name="wake">Thread-safe callback that makes the host call OnWake (probe completions arrive on another thread).</param>
    public TaskbarService(ITaskbarLocator locator, ITaskbarProbe probe, IClock clock, MonitorInfo monitor, Action? wake = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _monitor = monitor;
        _wake = wake;
    }

    public TaskbarLayout? Current { get; private set; }
    /// <summary>Raised on the thread that called OnWake/SetVisible, only when the layout changed (RF-21.1).</summary>
    public event Action<TaskbarLayout>? LayoutChanged;
    public long QueryCount { get; private set; }
    public long UpdateCount { get; private set; }
    public bool LayoutDirty { get; private set; }
    public int ConsecutiveFailures => _failures;
    public bool QueryInFlight => _inFlight;

    /// <summary>settings.taskbar.useUia: false means the probe is never called (RF-21.7).</summary>
    public bool UseUia { get => _useUia; set => _useUia = value; }

    /// <summary>The 60 s poll only exists if E1 shows XAML buttons emit no WinEvent (DP-21.1); default off.</summary>
    public bool SafetyPollEnabled
    {
        get => _safetyPoll;
        set { _safetyPoll = value; _pollAt = value && _visible ? Now + Ticks(SafetyPollMs) : null; }
    }

    public void SetMonitor(MonitorInfo monitor)
    {
        _monitor = monitor;
        Signal();
    }

    /// <summary>App start and every Show after Hide: one immediate query, no debounce. Hide: all deadlines off.</summary>
    public void SetVisible(bool visible)
    {
        if (_visible == visible) return;
        _visible = visible;
        if (!visible)
        {
            _debounceAt = _retryAt = _pollAt = null;
            return;
        }
        LayoutDirty = false;
        _debounceAt = _retryAt = null;
        _pollAt = _safetyPoll ? Now + Ticks(SafetyPollMs) : null;
        RunQuery();
    }

    /// <summary>Any trigger (RF-21.6): reschedules the single 500 ms deadline; hidden only marks dirty.</summary>
    public void Signal()
    {
        if (!_visible) { LayoutDirty = true; return; }
        _debounceAt = Now + Ticks(DebounceMs);
    }

    public long? NextDeadline => Min(Min(_debounceAt, _retryAt), Min(_pollAt, _timeoutAt));

    public void OnWake()
    {
        DrainCompleted();
        var now = Now;
        if (_inFlight && !_timedOut && _timeoutAt is { } t && now >= t)
        {
            _timedOut = true;
            _timeoutAt = null;
            Failure("timeout");
        }
        if (_debounceAt is { } d && now >= d) { _debounceAt = null; RunQuery(); }
        if (_retryAt is { } r && now >= r) { _retryAt = null; RunQuery(); }
        if (_pollAt is { } p && now >= p)
        {
            _pollAt = _visible && _safetyPoll ? now + Ticks(SafetyPollMs) : null;
            if (_visible) RunQuery();
        }
    }

    private void DrainCompleted()
    {
        while (_completed.TryDequeue(out var item))
        {
            if (item.Id != _queryId) continue;
            _inFlight = false;
            _timeoutAt = null;
            if (_timedOut) { _timedOut = false; continue; } // late arrival: already counted as failure, ignored
            if (item.Task.IsCompletedSuccessfully)
            {
                _failures = 0;
                Apply(TaskbarClassifier.Classify(item.Task.Result, _queryContext));
            }
            else
            {
                Failure(item.Task.Exception?.GetBaseException().GetType().Name ?? "canceled");
            }
        }
        if (_pending && !_inFlight) { _pending = false; if (_visible) RunQuery(); }
    }

    private void RunQuery()
    {
        var context = BuildContext(out var hwnd);
        var needsUia = _useUia && hwnd != 0 && !context.AutoHide && context.Edge == TaskbarEdge.Bottom;
        if (!needsUia)
        {
            _failures = 0;
            _retryAt = null;
            Apply(TaskbarClassifier.Classify([], context));
            return;
        }
        if (_inFlight) { _pending = true; return; } // never accumulate requests
        _queryContext = context;
        _inFlight = true;
        _timedOut = false;
        var id = ++_queryId;
        _timeoutAt = Now + Ticks(QueryTimeoutMs);
        QueryCount++;
        QueriesCounter.Add(1);
        Task<UiaNode[]> task;
        try { task = _probe.QueryAsync(hwnd); }
        catch (Exception ex) { task = Task.FromException<UiaNode[]>(ex); }
        task.ContinueWith(static (t, s) =>
        {
            var (self, queryId) = ((TaskbarService, long))s!;
            self._completed.Enqueue((queryId, t));
            self._wake?.Invoke();
        }, (this, id), TaskContinuationOptions.ExecuteSynchronously);
    }

    private TaskbarContext BuildContext(out nint hwnd)
    {
        var target = _locator.Locate(_monitor);
        if (target is { } found)
        {
            hwnd = found.Hwnd;
            return found.Context with { UseUia = _useUia };
        }
        hwnd = 0; // monitor without a taskbar: Edge Unknown + Fallback (RF-21.2 item 5)
        return new(_monitor, default, TaskbarEdge.Unknown, false, _useUia);
    }

    private void Failure(string reason)
    {
        _failures++;
        Log($"Taskbar: UIA failure {_failures}/{MaxFailures} ({reason}).");
        if (_failures >= MaxFailures)
        {
            _retryAt = null;
            Apply(TaskbarClassifier.Classify([], _queryContext with { UseUia = false }));
            return;
        }
        if (Current is null) Apply(TaskbarClassifier.Classify([], _queryContext with { UseUia = false }));
        if (_visible) _retryAt = Now + Ticks(RetryMs);
    }

    private void Apply(TaskbarLayout layout)
    {
        if (Current is { } old && TaskbarLayoutComparison.Equivalent(old, layout)) return;
        Current = layout;
        UpdateCount++;
        UpdatesCounter.Add(1);
        LayoutChanged?.Invoke(layout);
    }

    private void Log(string message)
    {
        var now = Now;
        if (_lastLogTicks is { } last && now - last < Ticks(10_000)) return; // at most 1 line per 10 s
        _lastLogTicks = now;
        Debug.WriteLine(message);
    }

    private long Now => _clock.NowTicks;
    private long Ticks(int ms) => (long)(ms / 1000.0 * _clock.TicksPerSecond);
    private static long? Min(long? a, long? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
}
