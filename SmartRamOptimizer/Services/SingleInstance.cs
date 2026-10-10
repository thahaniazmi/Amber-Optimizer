namespace SmartRamOptimizer.Services;

/// <summary>
/// Makes sure only one optimizer runs per user session. A second launch asks the first
/// one to show its window and then exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\" + AppIdentity.Name + ".Instance";
    private const string ShowEventName = @"Local\" + AppIdentity.Name + ".Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showWait;

    private SingleInstance(Mutex mutex, Action onShowRequested)
    {
        _mutex = mutex;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => onShowRequested(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Returns null when another instance is already running.</summary>
    public static SingleInstance? TryAcquire(Action onShowRequested, bool showExisting)
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex, onShowRequested);
        }

        mutex.Dispose();

        if (showExisting && EventWaitHandle.TryOpenExisting(ShowEventName, out var showEvent))
        {
            using (showEvent)
            {
                showEvent.Set();
            }
        }

        return null;
    }

    public void Dispose()
    {
        _showWait.Unregister(null);
        _showEvent.Dispose();
        _mutex.Dispose();
    }
}
