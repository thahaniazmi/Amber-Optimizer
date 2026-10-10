using System.Diagnostics;
using System.IO;

namespace SmartRamOptimizer.Targets;

/// <summary>Everything the optimizer knows about Discord.</summary>
public static class DiscordTarget
{
    public static readonly AppTarget Instance = new(
        "Discord",
        [
            "Discord",
            "DiscordCanary",
            "DiscordPTB",
            "DiscordDevelopment"
        ],
        GetLaunchInfo);

    private static ProcessStartInfo? GetLaunchInfo()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var updateExePath = Path.Combine(localAppData, "Discord", "Update.exe");

        if (!File.Exists(updateExePath))
        {
            return null;
        }

        return new ProcessStartInfo
        {
            FileName = updateExePath,
            Arguments = "--processStart Discord.exe",
            UseShellExecute = true
        };
    }
}
