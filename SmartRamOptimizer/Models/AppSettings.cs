using System.Text.Json.Serialization;

namespace SmartRamOptimizer.Models;

/// <summary>One application the user wants kept trimmed.</summary>
public sealed class ManagedApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    /// <summary>Process names (without ".exe") that belong to this app.</summary>
    public List<string> ProcessNames { get; set; } = [];

    /// <summary>Where the app's exe was last seen. Only used to show its icon.</summary>
    public string? ExePath { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>Everything that is remembered between runs.</summary>
public sealed class AppSettings
{
    public bool OptimizerEnabled { get; set; } = true;

    /// <summary>Trim every 1.5 seconds, including the app in use, instead of the gentle default.</summary>
    public bool AggressiveMode { get; set; }

    public bool TrayHintShown { get; set; }

    public List<ManagedApp> Apps { get; set; } = [];

    public static AppSettings CreateDefault()
    {
        return new AppSettings
        {
            Apps =
            [
                new ManagedApp
                {
                    Id = "discord",
                    Name = "Discord",
                    ProcessNames = ["Discord", "DiscordCanary", "DiscordPTB", "DiscordDevelopment"]
                },
                new ManagedApp
                {
                    Id = "brave",
                    Name = "Brave",
                    ProcessNames = ["brave"]
                }
            ]
        };
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
