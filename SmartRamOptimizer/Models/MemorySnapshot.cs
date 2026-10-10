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

/// <summary>Why a running app was, or was not, trimmed at the last measurement.</summary>
public enum AppActivity
{
    /// <summary>Being optimized; nothing is holding it back.</summary>
    Optimizing,

    /// <summary>The user is working in it.</summary>
    InUse,

    /// <summary>The user left it a moment ago; it is trimmed once it has sat in the background a while.</summary>
    RecentlyUsed,

    /// <summary>It holds too little memory for a trim to be worth the disturbance.</summary>
    AlreadySmall,

    /// <summary>Windows refused: the app runs as administrator and the optimizer does not.</summary>
    AccessDenied
}

public sealed record AppUsage(
    string AppId,
    long WorkingSetBytes,
    int ProcessCount,
    long FreedBytes,
    AppActivity Activity,
    int SecondsUntilTrim,
    string? ExePath);

/// <summary>One measurement, sent from the limiter's loop to the window.</summary>
/// <param name="TrimNowFreedBytes">What an "Optimize now" request freed, on the one snapshot that follows it; otherwise null.</param>
/// <param name="OwnMemoryBytes">This app's own memory, as Task Manager counts it. Zero when it was not measured.</param>
public sealed record MemorySnapshot(
    IReadOnlyList<AppUsage> Apps,
    long FreedNowBytes,
    long FreedSessionBytes,
    long SystemTotalBytes,
    long SystemAvailableBytes,
    long? TrimNowFreedBytes,
    long OwnMemoryBytes);
