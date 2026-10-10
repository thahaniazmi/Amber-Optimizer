namespace SmartRamOptimizer.Models;

/// <summary>What the limiter is asked to watch for one app.</summary>
public sealed record TargetApp(
    string Id,
    IReadOnlyList<string> ProcessNames,
    bool Enabled,
    bool NeedsExePath);

/// <summary>The limiter's whole configuration. Replaced as one unit so the loop never sees half an update.</summary>
public sealed record LimiterConfig(
    IReadOnlyList<TargetApp> Apps,
    bool Enabled,
    bool Aggressive)
{
    public static readonly LimiterConfig Empty = new([], false, false);
}

public sealed record AppUsage(
    string AppId,
    long WorkingSetBytes,
    int ProcessCount,
    long FreedBytes,
    bool IsForeground,
    bool AccessDenied,
    string? ExePath);

public sealed record MemorySnapshot(
    IReadOnlyList<AppUsage> Apps,
    long FreedNowBytes,
    long FreedSessionBytes,
    long SystemTotalBytes,
    long SystemAvailableBytes);
