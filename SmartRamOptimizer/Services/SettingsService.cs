using System.IO;
using System.Text.Json;
using SmartRamOptimizer.Models;

namespace SmartRamOptimizer.Services;

/// <summary>Loads and saves settings.json in %AppData%\SmartRamOptimizer.</summary>
public static class SettingsService
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppIdentity.Name);

    private static readonly string FilePath = Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            // Saved straight away, so the list a new user is given does not change on the next start.
            var created = FirstRunSetup.CreateSettings();
            Save(created);
            return created;
        }

        try
        {
            using var stream = File.OpenRead(FilePath);
            var settings = JsonSerializer.Deserialize(stream, AppSettingsJsonContext.Default.AppSettings);
            if (settings is not null)
            {
                return Sanitize(settings);
            }
        }
        catch (JsonException exception)
        {
            ErrorLog.Write(exception);
        }
        catch (IOException exception)
        {
            ErrorLog.Write(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ErrorLog.Write(exception);
        }

        KeepUnreadableFile();
        return AppSettings.CreateDefault();
    }

    public static bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);

            // Write beside the real file and swap, so a crash mid-write cannot leave half a file.
            var tempPath = FilePath + ".tmp";
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, settings, AppSettingsJsonContext.Default.AppSettings);
            }

            File.Move(tempPath, FilePath, overwrite: true);
            return true;
        }
        catch (IOException exception)
        {
            ErrorLog.Write(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ErrorLog.Write(exception);
        }

        return false;
    }

    // The file may have been edited by hand, so nothing in it is trusted.
    private static AppSettings Sanitize(AppSettings settings)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<ManagedApp>();

        foreach (var app in settings.Apps ?? [])
        {
            if (app is null)
            {
                continue;
            }

            app.ProcessNames = (app.ProcessNames ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(AppRules.ToProcessName)
                .Where(name => name.Length > 0 && !AppRules.IsProtectedProcess(name) && seenProcessNames.Add(name))
                .ToList();

            if (app.ProcessNames.Count == 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(app.Id) || !seenIds.Add(app.Id))
            {
                app.Id = Guid.NewGuid().ToString("N");
                seenIds.Add(app.Id);
            }

            if (string.IsNullOrWhiteSpace(app.Name))
            {
                app.Name = app.ProcessNames[0];
            }

            apps.Add(app);
        }

        settings.Apps = apps;

        // The window is only ever placed at a position that has both halves and is a real number.
        if (settings.WindowLeft is not { } left || settings.WindowTop is not { } top
            || !double.IsFinite(left) || !double.IsFinite(top))
        {
            settings.WindowLeft = null;
            settings.WindowTop = null;
        }

        return settings;
    }

    private static void KeepUnreadableFile()
    {
        try
        {
            File.Move(FilePath, Path.Combine(DataDirectory, "settings.unreadable.json"), overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
