using System.Diagnostics;
using System.IO;

namespace SmartRamOptimizer.Targets;

/// <summary>Everything the optimizer knows about Brave Browser.</summary>
public static class BraveTarget
{
    private const string BravePath = @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";

    public static readonly AppTarget Instance = new(
        "Brave",
        [
            "brave"
        ],
        GetLaunchInfo);

    private static ProcessStartInfo? GetLaunchInfo()
    {
        if (!File.Exists(BravePath))
        {
            return null;
        }

        return new ProcessStartInfo
        {
            FileName = BravePath,
            UseShellExecute = true
        };
    }
}
