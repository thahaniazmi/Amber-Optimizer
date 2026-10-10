using SmartRamOptimizer.Targets;

namespace SmartRamOptimizer.Models;

public sealed record TargetMemoryUsage(
    AppTarget Target,
    long WorkingSetBytes,
    int ProcessCount);

public sealed record MemorySnapshot(
    IReadOnlyList<TargetMemoryUsage> Targets,
    bool IsLimiterActive,
    DateTimeOffset CapturedAt)
{
    public long TotalWorkingSetBytes => Targets.Sum(usage => usage.WorkingSetBytes);

    public int TotalProcessCount => Targets.Sum(usage => usage.ProcessCount);

    public TargetMemoryUsage For(AppTarget target)
    {
        return Targets.FirstOrDefault(usage => usage.Target == target)
            ?? new TargetMemoryUsage(target, 0, 0);
    }
}
