using System.Diagnostics;
using SmartRamOptimizer.Models;

namespace SmartRamOptimizer.Services;

/// <summary>
/// Background loop that measures the configured apps and trims their memory.
/// It knows nothing about any particular app; it works from the <see cref="LimiterConfig"/> it is given.
/// </summary>
public sealed class RamLimiterService : IDisposable
{
    private const int VisibleSampleMs = 2000;
    private const int HiddenSampleMs = 5000;
    private const int IdleSampleMs = 30000;
    private const int AggressiveSampleMs = 1500;
    private const int PostTrimDelayMs = 250;

    // Gentle mode: leave the app in use alone (and for a while after the user switches away),
    // and give background apps time between trims.
    private const long GentleTrimIntervalMs = 30_000;
    private const long BackgroundGraceMs = 30_000;
    private const long MinTrimBytes = 100L * 1024 * 1024;

    private const long PathLookupIntervalMs = 30_000;
    private const long SelfTrimIntervalMs = 60_000;

    private readonly Dictionary<string, AppState> _states = [];
    private readonly Dictionary<string, int> _appIndexByProcessName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _liveProcessIds = [];
    private readonly List<int> _deadProcessIds = [];
    private readonly SemaphoreSlim _wake = new(0, 1);

    private volatile LimiterConfig _config = LimiterConfig.Empty;
    private volatile bool _isUiVisible;
    private LimiterConfig? _indexedConfig;
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private long _freedSessionBytes;
    private long _lastSelfTrimTick;
    private bool _disposed;

    /// <summary>Raised on a background thread after every measurement.</summary>
    public event Action<MemorySnapshot>? SnapshotUpdated;

    /// <summary>While no window is showing, the loop slows down and trims this app's own memory too.</summary>
    public bool IsUiVisible
    {
        get => _isUiVisible;
        set
        {
            _isUiVisible = value;
            if (!value)
            {
                Interlocked.Exchange(ref _lastSelfTrimTick, 0);
            }

            RequestRefresh();
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_loopTask is not null)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        var cancellationToken = _cancellation.Token;
        _loopTask = Task.Run(() => RunAsync(cancellationToken), cancellationToken);
    }

    public void Configure(LimiterConfig config)
    {
        _config = config;
        RequestRefresh();
    }

    /// <summary>Wakes the loop so a change shows up now instead of after the current wait.</summary>
    public void RequestRefresh()
    {
        if (_disposed || _wake.CurrentCount > 0)
        {
            return;
        }

        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // One bad pass must not end monitoring for the rest of the session.
                ErrorLog.Write(exception);
            }

            try
            {
                await _wake.WaitAsync(GetSampleInterval(), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private int GetSampleInterval()
    {
        var config = _config;

        if (config.Enabled && config.Aggressive)
        {
            return AggressiveSampleMs;
        }

        if (_isUiVisible)
        {
            return VisibleSampleMs;
        }

        return config.Enabled ? HiddenSampleMs : IdleSampleMs;
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var config = _config;
        IndexConfig(config);

        var now = Environment.TickCount64;
        var scan = Scan(config);
        var foregroundProcessId = ProcessTools.GetForegroundProcessId();
        var isForeground = new bool[config.Apps.Count];
        List<(int AppIndex, List<ProcessSample> Trimmed)>? pending = null;

        for (var i = 0; i < config.Apps.Count; i++)
        {
            var app = config.Apps[i];
            var processes = scan[i];
            var state = _states[app.Id];

            if (!config.Enabled || !app.Enabled || processes.Count == 0)
            {
                state.StopTrimming();
                continue;
            }

            isForeground[i] = Contains(processes, foregroundProcessId);
            if (isForeground[i])
            {
                state.LastForegroundTick = now;
            }

            if (!ShouldTrim(config.Aggressive, state, isForeground[i], TotalWorkingSet(processes), now))
            {
                continue;
            }

            var trimmed = new List<ProcessSample>(processes.Count);
            var denied = 0;

            foreach (var process in processes)
            {
                switch (ProcessTools.Trim(process.ProcessId))
                {
                    case TrimResult.Trimmed:
                        trimmed.Add(process);
                        break;
                    case TrimResult.AccessDenied:
                        denied++;
                        break;
                }
            }

            state.LastTrimTick = now;
            state.AccessDenied = trimmed.Count == 0 && denied > 0;

            if (trimmed.Count > 0)
            {
                (pending ??= []).Add((i, trimmed));
            }
        }

        if (pending is not null)
        {
            // Give Windows a moment to move the pages out, then measure what the trim achieved.
            await Task.Delay(PostTrimDelayMs, cancellationToken);
            scan = Scan(config);

            foreach (var (appIndex, trimmed) in pending)
            {
                RecordTrim(_states[config.Apps[appIndex].Id], trimmed, scan[appIndex]);
            }
        }

        var usages = new AppUsage[config.Apps.Count];
        long freedNowBytes = 0;

        for (var i = 0; i < config.Apps.Count; i++)
        {
            var app = config.Apps[i];
            var processes = scan[i];
            var state = _states[app.Id];
            var freedBytes = MeasureFreed(state, processes);
            if (!config.Enabled || !app.Enabled)
            {
                // Nothing is being held back for an app the user has switched off.
                freedBytes = 0;
            }

            freedNowBytes += freedBytes;

            usages[i] = new AppUsage(
                app.Id,
                TotalWorkingSet(processes),
                processes.Count,
                freedBytes,
                isForeground[i],
                state.AccessDenied,
                ResolveExePath(app, state, processes, now));
        }

        var (totalBytes, availableBytes) = ProcessTools.GetSystemMemory();
        SnapshotUpdated?.Invoke(new MemorySnapshot(usages, freedNowBytes, _freedSessionBytes, totalBytes, availableBytes));

        if (!_isUiVisible && now - Interlocked.Read(ref _lastSelfTrimTick) >= SelfTrimIntervalMs)
        {
            Interlocked.Exchange(ref _lastSelfTrimTick, now);
            ProcessTools.TrimSelf();
        }
    }

    private static bool ShouldTrim(bool aggressive, AppState state, bool isForeground, long totalBytes, long now)
    {
        if (aggressive)
        {
            return true;
        }

        if (isForeground || now - state.LastForegroundTick < BackgroundGraceMs)
        {
            return false;
        }

        if (totalBytes < MinTrimBytes)
        {
            return false;
        }

        return state.LastTrimTick is not { } lastTrim || now - lastTrim >= GentleTrimIntervalMs;
    }

    // Remembers how much each process held before the trim, so "freed" can be reported honestly later.
    private void RecordTrim(AppState state, List<ProcessSample> trimmed, List<ProcessSample> after)
    {
        foreach (var before in trimmed)
        {
            var index = after.FindIndex(sample => sample.ProcessId == before.ProcessId);
            if (index < 0)
            {
                continue;
            }

            _freedSessionBytes += Math.Max(0, before.WorkingSetBytes - after[index].WorkingSetBytes);

            if (!state.Baselines.TryGetValue(before.ProcessId, out var baseline)
                || before.WorkingSetBytes >= baseline.Estimate(before.PrivateBytes))
            {
                state.Baselines[before.ProcessId] = new Baseline(before.WorkingSetBytes, before.PrivateBytes);
            }
        }
    }

    // Freed = what each trimmed process held before, minus what it has taken back since.
    private long MeasureFreed(AppState state, List<ProcessSample> processes)
    {
        if (state.Baselines.Count == 0)
        {
            return 0;
        }

        long freedBytes = 0;
        _liveProcessIds.Clear();

        foreach (var process in processes)
        {
            _liveProcessIds.Add(process.ProcessId);

            if (state.Baselines.TryGetValue(process.ProcessId, out var baseline))
            {
                freedBytes += Math.Max(0, baseline.Estimate(process.PrivateBytes) - process.WorkingSetBytes);
            }
        }

        _deadProcessIds.Clear();
        foreach (var processId in state.Baselines.Keys)
        {
            if (!_liveProcessIds.Contains(processId))
            {
                _deadProcessIds.Add(processId);
            }
        }

        foreach (var processId in _deadProcessIds)
        {
            state.Baselines.Remove(processId);
        }

        return freedBytes;
    }

    // The window shows each app's own icon, which needs the exe path of a running copy.
    private static string? ResolveExePath(TargetApp app, AppState state, List<ProcessSample> processes, long now)
    {
        if (!app.NeedsExePath)
        {
            state.ExePath = null;
            return null;
        }

        if (state.ExePath is not null || processes.Count == 0)
        {
            return state.ExePath;
        }

        if (state.LastPathLookupTick is { } lastLookup && now - lastLookup < PathLookupIntervalMs)
        {
            return null;
        }

        state.LastPathLookupTick = now;

        var largest = processes[0];
        foreach (var process in processes)
        {
            if (process.WorkingSetBytes > largest.WorkingSetBytes)
            {
                largest = process;
            }
        }

        state.ExePath = ProcessTools.TryGetImagePath(largest.ProcessId);
        return state.ExePath;
    }

    // Rebuilds the process-name lookup and per-app state only when the configuration object changes.
    private void IndexConfig(LimiterConfig config)
    {
        if (ReferenceEquals(config, _indexedConfig))
        {
            return;
        }

        _indexedConfig = config;
        _appIndexByProcessName.Clear();

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < config.Apps.Count; i++)
        {
            var app = config.Apps[i];
            ids.Add(app.Id);
            _states.TryAdd(app.Id, new AppState());

            foreach (var processName in app.ProcessNames)
            {
                if (!AppRules.IsProtectedProcess(processName))
                {
                    _appIndexByProcessName.TryAdd(processName, i);
                }
            }
        }

        foreach (var id in _states.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            _states.Remove(id);
        }
    }

    // One pass over the system's process list covers every app.
    private List<ProcessSample>[] Scan(LimiterConfig config)
    {
        var result = new List<ProcessSample>[config.Apps.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = [];
        }

        if (_appIndexByProcessName.Count == 0)
        {
            return result;
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id > 4
                    && process.Id != Environment.ProcessId
                    && _appIndexByProcessName.TryGetValue(process.ProcessName, out var index))
                {
                    result[index].Add(new ProcessSample(process.Id, process.WorkingSet64, process.PrivateMemorySize64));
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited while it was being read.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static bool Contains(List<ProcessSample> processes, int processId)
    {
        foreach (var process in processes)
        {
            if (process.ProcessId == processId)
            {
                return true;
            }
        }

        return false;
    }

    private static long TotalWorkingSet(List<ProcessSample> processes)
    {
        long total = 0;
        foreach (var process in processes)
        {
            total += process.WorkingSetBytes;
        }

        return total;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation?.Cancel();

        try
        {
            // The loop never waits on the UI thread, so this cannot deadlock.
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cancellation?.Dispose();
        _wake.Dispose();
    }

    /// <summary>
    /// How much a process held when it was trimmed. If the process has since released memory
    /// by itself (its private bytes shrank), the estimate shrinks with it so that is not counted as freed.
    /// </summary>
    private readonly record struct Baseline(long WorkingSetBytes, long PrivateBytes)
    {
        public long Estimate(long currentPrivateBytes)
        {
            if (PrivateBytes <= 0 || currentPrivateBytes >= PrivateBytes)
            {
                return WorkingSetBytes;
            }

            return (long)(WorkingSetBytes * ((double)currentPrivateBytes / PrivateBytes));
        }
    }

    private sealed class AppState
    {
        public Dictionary<int, Baseline> Baselines { get; } = [];
        public long? LastTrimTick { get; set; }
        public long LastForegroundTick { get; set; } = long.MinValue / 2;
        public long? LastPathLookupTick { get; set; }
        public bool AccessDenied { get; set; }
        public string? ExePath { get; set; }

        // Baselines are kept: they describe the processes and are dropped as each one exits.
        // Switching an app off and on again should not make its earlier savings vanish from the stats.
        public void StopTrimming()
        {
            LastTrimTick = null;
            AccessDenied = false;
        }
    }
}
