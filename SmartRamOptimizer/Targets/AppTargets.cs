namespace SmartRamOptimizer.Targets;

/// <summary>
/// The apps the optimizer manages. To support another app, add a target file
/// next to this one and list it here.
/// </summary>
public static class AppTargets
{
    // Order matters: when nothing is running, the first installed target is launched.
    public static readonly IReadOnlyList<AppTarget> All =
    [
        DiscordTarget.Instance,
        BraveTarget.Instance
    ];
}
