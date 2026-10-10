using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SmartRamOptimizer.Services;

public static class StartupService
{
    public const string MinimizedArgument = "--minimized";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = AppIdentity.Name;
    // The value name used while the app was still called Discord RAM Limiter.
    private const string LegacyRunValueName = "DiscordRamLimiter";

    public static bool IsLaunchAtStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(RunValueName) is string { Length: > 0 };
    }

    public static void SetLaunchAtStartup(bool isEnabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (isEnabled)
        {
            key.SetValue(RunValueName, GetStartupCommand(), RegistryValueKind.String);
            return;
        }

        key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// Moves an entry written under the app's old name to the current one, and repairs an entry
    /// that points at an exe which no longer exists (the app's folder was moved).
    /// </summary>
    public static void RepairRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        var currentExe = GetProcessPath();
        var command = GetStartupCommand();

        if (key.GetValue(LegacyRunValueName) is string legacyCommand)
        {
            var legacyExe = ExtractExePath(legacyCommand);

            if (string.Equals(legacyExe, currentExe, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(RunValueName, command, RegistryValueKind.String);
                key.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
            }
            else if (legacyExe is null || !File.Exists(legacyExe))
            {
                key.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);
            }
        }

        if (key.GetValue(RunValueName) is string registeredCommand)
        {
            var registeredExe = ExtractExePath(registeredCommand);

            if (registeredExe is null || !File.Exists(registeredExe))
            {
                key.SetValue(RunValueName, command, RegistryValueKind.String);
            }
        }
    }

    private static string GetStartupCommand()
    {
        return $"\"{GetProcessPath()}\" {MinimizedArgument}";
    }

    private static string GetProcessPath()
    {
        var processPath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            using var process = Process.GetCurrentProcess();
            processPath = process.MainModule?.FileName;
        }

        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Unable to resolve the application path.");
        }

        return processPath;
    }

    // Commands look like: "C:\Path\App.exe" --minimized
    private static string? ExtractExePath(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            var closingQuote = command.IndexOf('"', 1);
            return closingQuote > 1 ? command[1..closingQuote] : null;
        }

        var exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeEnd > 0 ? command[..(exeEnd + 4)] : null;
    }
}
