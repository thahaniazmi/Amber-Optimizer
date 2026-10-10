namespace DiscordRamLimiter.Models;

public sealed record DiscordMemorySnapshot(
    long CurrentWorkingSetBytes,
    long InitialWorkingSetBytes,
    int ProcessCount,
    long DiscordWorkingSetBytes,
    int DiscordProcessCount,
    long BraveWorkingSetBytes,
    int BraveProcessCount,
    bool IsLimiterActive,
    DateTimeOffset CapturedAt);
