using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartRamOptimizer.Services;

/// <summary>Reads an app's icon out of its exe, so no logos have to ship with the optimizer.</summary>
internal static class IconService
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Safe to call from any thread; the returned image is frozen.</summary>
    public static ImageSource? GetIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return null;
        }

        if (Cache.TryGetValue(exePath, out var cached))
        {
            return cached;
        }

        var icon = Load(exePath);
        if (icon is not null)
        {
            Cache[exePath] = icon;
        }

        return icon;
    }

    private static BitmapSource? Load(string exePath)
    {
        nint largeIcon = 0;
        nint smallIcon = 0;

        try
        {
            // An exe with no icon of its own gives nothing here, and the row shows the app's initial instead.
            if (!File.Exists(exePath)
                || NativeMethods.ExtractIconEx(exePath, 0, out largeIcon, out smallIcon, 1) == 0
                || largeIcon == 0)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                largeIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception exception) when (exception is IOException
            or ArgumentException
            or UnauthorizedAccessException
            or COMException)
        {
            return null;
        }
        finally
        {
            if (largeIcon != 0)
            {
                NativeMethods.DestroyIcon(largeIcon);
            }

            if (smallIcon != 0)
            {
                NativeMethods.DestroyIcon(smallIcon);
            }
        }
    }
}
