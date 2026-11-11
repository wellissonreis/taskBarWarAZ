namespace SideWar.Platform.Windows.Instance;

/// <summary>
/// Single-instance guard (SDD-07 RF-07.8): named mutex plus an activation event. The first instance owns the mutex and
/// waits on the event; a second instance calls <see cref="TryAcquire"/>, which signals the event and returns null.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\SideWar.Desktop.SingleInstance";
    public const string ActivateEventName = @"Local\SideWar.Desktop.Activate";

    private readonly Mutex mutex;
    private readonly EventWaitHandle activate;
    private RegisteredWaitHandle? registration;
    private bool disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        this.mutex = mutex;
        this.activate = activate;
    }

    /// <summary>Returns the owner, or null if another instance already runs (it was signalled to activate).</summary>
    public static SingleInstance? TryAcquire(string mutexName = MutexName, string eventName = ActivateEventName)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName, out bool created);
        if (!created)
        {
            mutex.Dispose();
            try
            {
                using var existing = EventWaitHandle.OpenExisting(eventName);
                existing.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // First instance is still starting and has not created the event yet; nothing to activate.
            }
            return null;
        }
        return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, eventName));
    }

    /// <summary>Starts listening; <paramref name="onActivate"/> runs on a ThreadPool thread for each second launch.</summary>
    public void StartListening(Action onActivate)
    {
        ArgumentNullException.ThrowIfNull(onActivate);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (registration is not null) throw new InvalidOperationException("Already listening.");
        registration = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        registration?.Unregister(null);
        activate.Dispose();
        try { mutex.ReleaseMutex(); } catch (ApplicationException) { /* released from another thread at shutdown */ }
        mutex.Dispose();
    }
}
