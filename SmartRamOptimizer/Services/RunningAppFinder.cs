using System.Diagnostics;
using System.IO;
using System.Windows.Media;

namespace SmartRamOptimizer.Services;

/// <summary>An app the user could add, found running or picked from disk.</summary>
public sealed record AppCandidate(
    string Name,
    string ProcessName,
    string ExePath,
    long WorkingSetBytes,
    int ProcessCount,
    ImageSource? Icon,
    AppAdvice Advice);

/// <summary>Builds the list shown in the "Add application" window.</summary>
internal static class RunningAppFinder
{
    /// <summary>
    /// Running apps the optimizer could manage: the ones known to suit it first, the ones it warns
    /// about last, and the largest first within each group. Call from a background thread.
    /// </summary>
    public static List<AppCandidate> FindRunningApps()
    {
        return Find(recommendedOnly: false);
    }

    /// <summary>Only the running apps marked "Good to optimize", largest first, without their icons.</summary>
    public static List<AppCandidate> FindRecommendedApps()
    {
        return Find(recommendedOnly: true);
    }

    private static List<AppCandidate> Find(bool recommendedOnly)
    {
        var groups = new Dictionary<string, (long Bytes, int Count, List<int> ProcessIds)>(StringComparer.OrdinalIgnoreCase);

        using (var snapshot = ProcessSnapshot.Capture())
        {
            foreach (var process in snapshot)
            {
                // Session 0 is services and the system itself, not something the user runs.
                if (process.ProcessId <= 4 || process.SessionId == 0 || process.ProcessId == Environment.ProcessId)
                {
                    continue;
                }

                var processName = process.Name.ToString();
                if (AppRules.IsProtectedProcess(processName))
                {
                    continue;
                }

                if (!groups.TryGetValue(processName, out var group))
                {
                    group = (0, 0, []);
                }

                group.ProcessIds.Add(process.ProcessId);
                groups[processName] = (group.Bytes + process.WorkingSetBytes, group.Count + 1, group.ProcessIds);
            }
        }

        var candidates = new List<AppCandidate>();

        foreach (var (processName, group) in groups)
        {
            var advice = AppRules.GetAdvice(processName);
            if (recommendedOnly && advice.Suitability != Suitability.Recommended)
            {
                continue;
            }

            // No readable path means Windows will not let us open the process, so it could not be trimmed either.
            var exePath = group.ProcessIds.Take(3).Select(ProcessTools.TryGetImagePath).FirstOrDefault(path => path is not null);
            if (exePath is null || AppRules.IsUnderWindowsDirectory(exePath))
            {
                continue;
            }

            candidates.Add(new AppCandidate(
                GetDisplayName(exePath, processName),
                processName,
                exePath,
                group.Bytes,
                group.Count,
                recommendedOnly ? null : IconService.GetIcon(exePath),
                advice));
        }

        candidates.Sort((left, right) =>
        {
            var byAdvice = Rank(left.Advice.Suitability).CompareTo(Rank(right.Advice.Suitability));
            return byAdvice != 0 ? byAdvice : right.WorkingSetBytes.CompareTo(left.WorkingSetBytes);
        });
        return candidates;
    }

    private static int Rank(Suitability suitability)
    {
        return suitability switch
        {
            Suitability.Recommended => 0,
            Suitability.NotRecommended => 2,
            _ => 1
        };
    }

    /// <summary>Describes an exe the user browsed to. Returns null with a reason when it cannot be managed.</summary>
    public static AppCandidate? FromExePath(string exePath, out string? error)
    {
        var processName = AppRules.ToProcessName(exePath);

        if (processName.Length == 0 || !File.Exists(exePath))
        {
            error = "That file could not be found.";
            return null;
        }

        if (AppRules.IsUnderWindowsDirectory(exePath) || AppRules.IsProtectedProcess(processName))
        {
            error = "Windows system processes can't be optimized.";
            return null;
        }

        error = null;
        return new AppCandidate(
            GetDisplayName(exePath, processName),
            processName,
            exePath,
            0,
            0,
            IconService.GetIcon(exePath),
            AppRules.GetAdvice(processName));
    }

    // "Google Chrome" reads better than "chrome"; fall back to the process name when the exe has no description.
    private static string GetDisplayName(string exePath, string processName)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(exePath).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description) && description.Length <= 40)
            {
                return description;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return processName;
    }
}
