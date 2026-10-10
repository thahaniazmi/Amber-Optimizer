using System.IO;

namespace SmartRamOptimizer.Services;

/// <summary>Appends unexpected errors to error.log next to the settings file.</summary>
internal static class ErrorLog
{
    private const long MaxBytes = 256 * 1024;

    private static readonly Lock Gate = new();
    private static string? _lastMessage;

    public static void Write(Exception exception)
    {
        var message = exception.ToString();

        lock (Gate)
        {
            // The monitor loop can hit the same failure every tick; one entry is enough.
            if (message == _lastMessage)
            {
                return;
            }

            _lastMessage = message;

            try
            {
                Directory.CreateDirectory(SettingsService.DataDirectory);
                var path = Path.Combine(SettingsService.DataDirectory, "error.log");

                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Delete(path);
                }

                File.AppendAllText(path, $"{DateTimeOffset.Now:u} {message}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
