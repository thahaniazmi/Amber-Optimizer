using System.IO;

namespace SmartRamOptimizer.Services;

/// <summary>Which processes the optimizer refuses to touch, and which it warns about.</summary>
internal static class AppRules
{
    // Trimming these can freeze the desktop, break audio, or fight the system's own memory manager.
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Secure System", "Memory Compression",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "svchost", "dwm", "fontdrvhost", "audiodg", "MsMpEng", "NisSrv"
    };

    // Games and launchers guarded by kernel anti-cheat. Opening their processes can get a player kicked or flagged.
    private static readonly HashSet<string> AntiCheatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "VALORANT", "VALORANT-Win64-Shipping", "vgc", "vgtray", "RiotClientServices",
        "League of Legends", "LeagueClient",
        "FortniteClient-Win64-Shipping", "FortniteLauncher",
        "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "FACEIT", "faceit",
        "cs2", "csgo", "r5apex", "TslGame", "RainbowSix", "RainbowSix_BE",
        "GTA5", "Overwatch", "destiny2", "RustClient", "EscapeFromTarkov",
        "GenshinImpact", "cod", "ModernWarfare", "eldenring", "DeadByDaylight-Win64-Shipping"
    };

    private static readonly string OwnProcessName =
        Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "SmartRamOptimizer";

    private static readonly string WindowsDirectory = Path.TrimEndingDirectorySeparator(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    public static bool IsProtectedProcess(string processName)
    {
        return ProtectedNames.Contains(processName)
            || string.Equals(processName, OwnProcessName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAntiCheatRisk(string processName)
    {
        return AntiCheatNames.Contains(processName);
    }

    public static bool IsUnderWindowsDirectory(string path)
    {
        return WindowsDirectory.Length > 0
            && path.StartsWith(WindowsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"C:\Apps\Foo.exe" and "Foo.exe" both become "Foo", the name Windows reports for the process.</summary>
    public static string ToProcessName(string value)
    {
        var name = Path.GetFileName(value.Trim());
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
