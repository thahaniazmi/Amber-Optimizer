using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DiscordRamLimiter.Models;

namespace DiscordRamLimiter.Services;

public sealed class DiscordLimiterService : IDisposable
{
    private static readonly TimeSpan MonitorInterval = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PostTrimRefreshDelay = TimeSpan.FromMilliseconds(180);
    private static readonly string[] DiscordProcessNames =
    [
        "Discord",
        "DiscordCanary",
        "DiscordPTB",
        "DiscordDevelopment",
        "brave"
    ];

    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private long? _initialWorkingSetBytes;
    private bool _hadDiscordProcesses;
    private bool _disposed;

    public event EventHandler<DiscordMemorySnapshot>? SnapshotUpdated;
    public event EventHandler? AllDiscordProcessesExited;

    public bool IsLimiterActive { get; private set; }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, int min, int max);

    public static bool EnsureDiscordLaunched()
    {
        if (GetDiscordProcesses().Any())
        {
            return true;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var updateExePath = Path.Combine(localAppData, "Discord", "Update.exe");
        var bravePath = @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";

        var launched = false;

        if (File.Exists(updateExePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = updateExePath,
                    Arguments = "--processStart Discord.exe",
                    UseShellExecute = true
                });
                launched = true;
            }
            catch
            {
            }
        }

        if (!launched && File.Exists(bravePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = bravePath,
                    UseShellExecute = true
                });
                launched = true;
            }
            catch
            {
            }
        }

        return launched;
    }

    public Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_monitorTask is { IsCompleted: false })
        {
            return Task.CompletedTask;
        }

        EnsureDiscordLaunched();

        _initialWorkingSetBytes ??= GetTotalDiscordWorkingSetBytes(out _);
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

    public int GetLargestDiscordProcessId()
    {
        return GetDiscordProcesses()
            .OrderByDescending(process => SafeWorkingSet(process))
            .Select(process => process.Id)
            .FirstOrDefault(-1);
    }

    private static readonly string[] DiscordOnlyNames =
    [
        "Discord",
        "DiscordCanary",
        "DiscordPTB",
        "DiscordDevelopment"
    ];

    private static readonly string[] BraveOnlyNames =
    [
        "brave"
    ];

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var discordBytes = GetWorkingSetForNames(DiscordOnlyNames, out var discordCount);
            var braveBytes = GetWorkingSetForNames(BraveOnlyNames, out var braveCount);

            var totalBytes = discordBytes + braveBytes;
            var totalCount = discordCount + braveCount;

            if (totalCount > 0)
            {
                _hadDiscordProcesses = true;
            }
            else if (_hadDiscordProcesses)
            {
                // Both Discord and Brave have closed!
                AllDiscordProcessesExited?.Invoke(this, EventArgs.Empty);
                break;
            }

            if (IsLimiterActive && totalCount > 0)
            {
                LimitDiscordMemory();

                await Task.Delay(PostTrimRefreshDelay, cancellationToken);
                discordBytes = GetWorkingSetForNames(DiscordOnlyNames, out discordCount);
                braveBytes = GetWorkingSetForNames(BraveOnlyNames, out braveCount);
                totalBytes = discordBytes + braveBytes;
                totalCount = discordCount + braveCount;
            }

            SnapshotUpdated?.Invoke(
                this,
                new DiscordMemorySnapshot(
                    totalBytes,
                    _initialWorkingSetBytes ?? 0,
                    totalCount,
                    discordBytes,
                    discordCount,
                    braveBytes,
                    braveCount,
                    IsLimiterActive,
                    DateTimeOffset.Now));

            await Task.Delay(MonitorInterval, cancellationToken);
        }
    }

    private static long GetWorkingSetForNames(string[] names, out int processCount)
    {
        long totalBytes = 0;
        processCount = 0;

        foreach (var processName in names)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(processName); }
            catch { continue; }

            foreach (var process in processes)
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
        }

        return totalBytes;
    }

    private static void LimitDiscordMemory()
    {
        foreach (var process in GetDiscordProcesses())
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

    private static long GetTotalDiscordWorkingSetBytes(out int processCount)
    {
        long totalBytes = 0;
        processCount = 0;

        foreach (var process in GetDiscordProcesses())
        {
            try
            {
                totalBytes += process.WorkingSet64;
                processCount++;
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return totalBytes;
    }

    private static IEnumerable<Process> GetDiscordProcesses()
    {
        foreach (var processName in DiscordProcessNames)
        {
            Process[] processes;

            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (var process in processes)
            {
                yield return process;
            }
        }
    }

    private static long SafeWorkingSet(Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch (InvalidOperationException)
        {
            return 0;
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
