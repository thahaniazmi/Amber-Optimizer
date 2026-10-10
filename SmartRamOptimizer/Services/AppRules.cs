using System.IO;

namespace SmartRamOptimizer.Services;

/// <summary>How well an app takes to having its memory trimmed.</summary>
public enum Suitability
{
    Unknown,
    Recommended,
    NotRecommended
}

/// <summary>What the "Add application" window tells the user about an app before they add it.</summary>
public sealed record AppAdvice(Suitability Suitability, string Label, string Reason)
{
    public static AppAdvice None { get; } = new(Suitability.Unknown, string.Empty, string.Empty);
}

/// <summary>Which processes the optimizer refuses to touch, which it warns about, and which it recommends.</summary>
internal static class AppRules
{
    private static readonly AppAdvice RecommendedAdvice = new(
        Suitability.Recommended,
        "Good to optimize",
        "A known memory-heavy app that copes well with being trimmed.");

    private static readonly AppAdvice AntiCheatAdvice = new(
        Suitability.NotRecommended,
        "Not recommended · anti-cheat",
        "It looks like a game or launcher protected by anti-cheat. "
        + "Trimming its memory from another program can get you kicked from a match or flagged.");

    private static readonly AppAdvice RealTimeAdvice = new(
        Suitability.NotRecommended,
        "Not recommended · recording or audio",
        "It works in real time while it sits in the background. "
        + "Trimming its memory can cause dropped frames, stutter or audio glitches.");

    private static readonly AppAdvice VirtualMachineAdvice = new(
        Suitability.NotRecommended,
        "Not recommended · virtual machine",
        "It runs a whole operating system in its memory. "
        + "Trimming it pushes that memory to disk and makes the virtual machine crawl.");

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
        "Riot Client", "RiotClientUx", "RiotClientUxRender", "RiotClientCrashHandler",
        "League of Legends", "LeagueClient", "LeagueClientUx", "LeagueClientUxRender",
        "FortniteClient-Win64-Shipping", "FortniteLauncher",
        "EasyAntiCheat", "EasyAntiCheat_EOS", "start_protected_game", "BEService", "FACEIT", "faceit",
        "EAAntiCheat.GameService", "EAAntiCheat.GameServiceLauncher",
        "cs2", "csgo", "r5apex", "TslGame", "RainbowSix", "RainbowSix_BE",
        "GTA5", "Overwatch", "destiny2", "RustClient", "EscapeFromTarkov",
        "GenshinImpact", "cod", "ModernWarfare", "eldenring", "DeadByDaylight-Win64-Shipping",
        "Marvel-Win64-Shipping", "helldivers2", "RobloxPlayerBeta"
    };

    // Recorders, streamers and audio mixers keep working while another app has the focus,
    // which is exactly when the optimizer would trim them.
    private static readonly HashSet<string> RealTimeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "obs64", "obs32", "obs", "Streamlabs OBS", "Streamlabs Desktop", "XSplit.Core",
        "voicemeeter", "voicemeeterpro", "voicemeeter8", "voicemeeter8x64"
    };

    private static readonly HashSet<string> VirtualMachineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "vmware-vmx", "VirtualBoxVM", "VBoxHeadless", "vmmem", "vmmemWSL", "vmwp",
        "qemu-system-x86_64", "HD-Player", "Nox", "LdVBoxHeadless", "dnplayer"
    };

    // Browsers, chat apps and launcher web helpers: they hoard memory in the background
    // and simply page it back in when the user returns to them.
    private static readonly HashSet<string> RecommendedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "msedgewebview2", "brave", "firefox", "opera", "vivaldi",
        "Discord", "DiscordCanary", "DiscordPTB", "DiscordDevelopment",
        "slack", "ms-teams", "Teams", "Telegram", "WhatsApp", "Signal",
        "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper", "Battle.net", "EADesktop",
        "GalaxyClient", "GalaxyClient Helper", "upc", "UbisoftConnect",
        "Spotify", "Code", "Notion", "Obsidian", "Figma", "Postman", "GitHubDesktop"
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

    /// <summary>Warnings win over recommendations, so a name on two lists is never called safe.</summary>
    public static AppAdvice GetAdvice(string processName)
    {
        if (IsAntiCheatRisk(processName))
        {
            return AntiCheatAdvice;
        }

        if (RealTimeNames.Contains(processName))
        {
            return RealTimeAdvice;
        }

        if (VirtualMachineNames.Contains(processName))
        {
            return VirtualMachineAdvice;
        }

        return RecommendedNames.Contains(processName) ? RecommendedAdvice : AppAdvice.None;
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
