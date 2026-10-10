using System.Diagnostics;
using System.Runtime.InteropServices;
using SmartRamOptimizer.Models;
using SmartRamOptimizer.Targets;

namespace SmartRamOptimizer.Services;

/// <summary>
/// App-agnostic monitor loop. It measures and trims whatever targets it is given;
/// it knows nothing about Discord or Brave specifically.
/// </summary>
public sealed class RamLimiterService : IDisposable
{
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PostTrimRefreshDelay = TimeSpan.FromMilliseconds(180);

    private readonly IReadOnlyList<AppTarget> _targets;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private bool _hadTargetProcesses;
    private bool _disposed;

    public RamLimiterService(IReadOnlyList<AppTarget> targets)
    {
        _targets = targets;
    }

    public event EventHandler<MemorySnapshot>? SnapshotUpdated;
    public event EventHandler? AllTargetProcessesExited;

    public bool IsLimiterActive { get; private set; }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

    public Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_monitorTask is { IsCompleted: false })
        {
            return Task.CompletedTask;
        }

        EnsureTargetLaunched();

        _monitorCancellation = new CancellationTokenSource();
        _monitorTask = MonitorLoopAsync(_monitorCancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        IsLimiterActive = false;

        if (_monitorCancellation is null)
        {
            return;
        }

        await _monitorCancellation.CancelAsync();

        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _monitorCancellation.Dispose();
        _monitorCancellation = null;
        _monitorTask = null;
    }

    public void SetLimiterActive(bool isActive)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IsLimiterActive = isActive;
    }

    // If none of the targets is running, start the first one that is installed.
    private bool EnsureTargetLaunched()
    {
        foreach (var target in _targets)
        {
            if (MeasureTarget(target).ProcessCount > 0)
            {
                return true;
            }
        }

        foreach (var target in _targets)
        {
            if (target.TryLaunch())
            {
                return true;
            }
        }

        return false;
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var usages = MeasureTargets();
            var totalCount = usages.Sum(usage => usage.ProcessCount);

            if (totalCount > 0)
            {
                _hadTargetProcesses = true;
            }
            else if (_hadTargetProcesses)
            {
                // Every target app has closed.
                AllTargetProcessesExited?.Invoke(this, EventArgs.Empty);
                break;
            }

            if (IsLimiterActive && totalCount > 0)
            {
                foreach (var target in _targets)
                {
                    TrimTarget(target);
                }

                await Task.Delay(PostTrimRefreshDelay, cancellationToken);
                usages = MeasureTargets();
            }

            SnapshotUpdated?.Invoke(
                this,
                new MemorySnapshot(usages, IsLimiterActive, DateTimeOffset.Now));

            await Task.Delay(MonitorInterval, cancellationToken);
        }
    }

    private List<TargetMemoryUsage> MeasureTargets()
    {
        return _targets.Select(MeasureTarget).ToList();
    }

    private static TargetMemoryUsage MeasureTarget(AppTarget target)
    {
        long totalBytes = 0;
        var processCount = 0;

        foreach (var process in target.GetProcesses())
        {
            try
            {
                totalBytes += process.WorkingSet64;
                processCount++;
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return new TargetMemoryUsage(target, totalBytes, processCount);
    }

    private static void TrimTarget(AppTarget target)
    {
        foreach (var process in target.GetProcesses())
        {
            try
            {
                SetProcessWorkingSetSize(process.Handle, -1, -1);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsLimiterActive = false;
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
    }
}
