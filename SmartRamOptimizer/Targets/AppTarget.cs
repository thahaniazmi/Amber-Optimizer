using System.Diagnostics;

namespace SmartRamOptimizer.Targets;

/// <summary>
/// Describes one application whose memory the optimizer keeps trimmed.
/// Everything specific to a single app lives in its own file in this folder
/// (see <see cref="DiscordTarget"/> and <see cref="BraveTarget"/>).
/// </summary>
public sealed class AppTarget
{
    private readonly Func<ProcessStartInfo?> _getLaunchInfo;

    public AppTarget(string name, IReadOnlyList<string> processNames, Func<ProcessStartInfo?> getLaunchInfo)
    {
        Name = name;
        ProcessNames = processNames;
        _getLaunchInfo = getLaunchInfo;
    }

    public string Name { get; }

    /// <summary>Process names (without ".exe") that belong to this app.</summary>
    public IReadOnlyList<string> ProcessNames { get; }

    /// <summary>Returns every running process that belongs to this app. Callers dispose them.</summary>
    public IEnumerable<Process> GetProcesses()
    {
        foreach (var processName in ProcessNames)
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

    /// <summary>Starts the app if it is installed. Returns false when it could not be started.</summary>
    public bool TryLaunch()
    {
        var launchInfo = _getLaunchInfo();
        if (launchInfo is null)
        {
            return false;
        }

        try
        {
            Process.Start(launchInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
