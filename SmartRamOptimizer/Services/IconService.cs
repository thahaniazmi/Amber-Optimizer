using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

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
        try
        {
            if (!File.Exists(exePath))
            {
                return null;
            }

            using var icon = Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception exception) when (exception is IOException
            or ArgumentException
            or UnauthorizedAccessException
            or Win32Exception
            or COMException)
        {
            return null;
        }
    }
}
