using System.Runtime.InteropServices;

namespace SmartRamOptimizer.Services;

internal readonly record struct ProcessSample(int ProcessId, long WorkingSetBytes, long PrivateBytes);

internal enum TrimResult
{
    Trimmed,
    AccessDenied,
    Failed
}

/// <summary>Thin wrappers over the Windows calls the optimizer needs.</summary>
internal static class ProcessTools
{
    /// <summary>Moves a process's pages out of physical RAM. Only asks for the one right that needs.</summary>
    public static TrimResult Trim(int processId)
    {
        using var handle = NativeMethods.OpenProcess(NativeMethods.ProcessSetQuota, false, processId);
        if (handle.IsInvalid)
        {
            return ToFailure(Marshal.GetLastPInvokeError());
        }

        return NativeMethods.SetProcessWorkingSetSize(handle, -1, -1)
            ? TrimResult.Trimmed
            : ToFailure(Marshal.GetLastPInvokeError());
    }

    public static void TrimSelf()
    {
        NativeMethods.SetCurrentProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), -1, -1);
    }

    /// <summary>Full path of a running process's exe, or null when Windows will not tell us.</summary>
    public static string? TryGetImagePath(int processId)
    {
        using var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new char[1024];
        var size = (uint)buffer.Length;
        return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0
            ? new string(buffer, 0, (int)size)
            : null;
    }

    /// <summary>Id of the process that owns the window the user is working in, or 0.</summary>
    public static int GetForegroundProcessId()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == 0)
        {
            return 0;
        }

        _ = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }

    public static (long TotalBytes, long AvailableBytes) GetSystemMemory()
    {
        var status = new NativeMethods.MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>()
        };

        return NativeMethods.GlobalMemoryStatusEx(ref status)
            ? ((long)status.TotalPhys, (long)status.AvailPhys)
            : (0, 0);
    }

    private static TrimResult ToFailure(int error)
    {
        return error == NativeMethods.ErrorAccessDenied ? TrimResult.AccessDenied : TrimResult.Failed;
    }
}
