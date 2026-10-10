using SmartRamOptimizer.Models;

namespace SmartRamOptimizer.Services;

/// <summary>
/// Chooses the starting list for a new user: the well-known memory-heavy apps they are running right now,
/// instead of the same two apps for everyone.
/// </summary>
internal static class FirstRunSetup
{
    private const int MaxApps = 6;
    private const long MinBytes = 100L * 1024 * 1024;

    // A runtime many different apps share. Fine to add on purpose, too broad to add for the user.
    private const string SharedWebViewProcess = "msedgewebview2";

    public static AppSettings CreateSettings()
    {
        var defaults = AppSettings.CreateDefault();
        var apps = new List<ManagedApp>();

        foreach (var candidate in RunningAppFinder.FindRecommendedApps())
        {
            if (apps.Count == MaxApps || candidate.WorkingSetBytes < MinBytes)
            {
                break;
            }

            if (string.Equals(candidate.ProcessName, SharedWebViewProcess, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A default entry knows every process name of its app (Discord has four), so it is preferred.
            var known = defaults.Apps.Find(app =>
                app.ProcessNames.Contains(candidate.ProcessName, StringComparer.OrdinalIgnoreCase));

            if (known is null)
            {
                apps.Add(new ManagedApp
                {
                    Name = candidate.Name,
                    ProcessNames = [candidate.ProcessName],
                    ExePath = candidate.ExePath
                });
            }
            else if (!apps.Contains(known))
            {
                known.ExePath = candidate.ExePath;
                apps.Add(known);
            }
        }

        // Nothing suitable is running (started with Windows, say): fall back to the classic pair.
        return apps.Count == 0 ? defaults : new AppSettings { Apps = apps, DetectedAppCount = apps.Count };
    }
}
