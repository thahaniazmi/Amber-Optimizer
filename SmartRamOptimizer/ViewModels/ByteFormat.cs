namespace SmartRamOptimizer.ViewModels;

/// <summary>Turns byte counts into the short text shown in the window.</summary>
internal static class ByteFormat
{
    private const double BytesPerMegabyte = 1024d * 1024d;
    private const double BytesPerGigabyte = BytesPerMegabyte * 1024d;

    public static string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        var megabytes = bytes / BytesPerMegabyte;
        if (megabytes >= 1000)
        {
            return $"{bytes / BytesPerGigabyte:0.0} GB";
        }

        return megabytes >= 10 ? $"{megabytes:0} MB" : $"{megabytes:0.0} MB";
    }

    public static string FormatGigabytes(long bytes)
    {
        return $"{bytes / BytesPerGigabyte:0.0}";
    }
}
