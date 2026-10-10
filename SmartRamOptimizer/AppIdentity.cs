namespace SmartRamOptimizer;

/// <summary>The name that keeps one copy of the optimizer's settings, startup entry and instance lock apart from another's.</summary>
internal static class AppIdentity
{
#if DEV_INSTANCE
    // Built with -p:DevInstance=true: a development copy that can run beside the installed one.
    public const string Name = "SmartRamOptimizer.Dev";
#else
    public const string Name = "SmartRamOptimizer";
#endif
}
