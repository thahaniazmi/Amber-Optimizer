using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SmartRamOptimizer.Services;

internal static class NativeMethods
{
    internal const uint ProcessSetQuota = 0x0100;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const int ErrorAccessDenied = 5;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    // Passing -1 for both sizes asks Windows to move the process's pages out of physical RAM.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessWorkingSetSize(SafeProcessHandle process, nint minimumSize, nint maximumSize);

    [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCurrentProcessWorkingSetSize(nint process, nint minimumSize, nint maximumSize);

    [DllImport("kernel32.dll")]
    internal static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        uint flags,
        [Out] char[] exeName,
        ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
