using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SmartRamOptimizer.Services;

internal static unsafe class NativeMethods
{
    internal const uint ProcessSetQuota = 0x0100;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const int ErrorAccessDenied = 5;

    internal const uint MemoryCommitAndReserve = 0x3000;
    internal const uint MemoryRelease = 0x8000;
    internal const uint PageReadWrite = 0x04;

    internal const int SystemProcessInformation = 5;
    internal const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    internal const uint NotifyAdd = 0;
    internal const uint NotifyModify = 1;
    internal const uint NotifyDelete = 2;
    internal const uint NotifyFlagMessage = 0x01;
    internal const uint NotifyFlagIcon = 0x02;
    internal const uint NotifyFlagTip = 0x04;
    internal const uint NotifyFlagInfo = 0x10;
    internal const uint NotifyInfoIconInfo = 1;
    internal const uint NotifyInfoIconWarning = 2;

    internal const uint MessageApp = 0x8000;
    internal const uint MessageContextMenu = 0x007B;
    internal const uint MessageLeftButtonUp = 0x0202;
    internal const uint MessageLeftButtonDoubleClick = 0x0203;
    internal const uint MessageRightButtonUp = 0x0205;

    internal const int MetricSmallIconWidth = 49;
    internal const uint MonitorDefaultToNull = 0;

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? moduleName);

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

    [DllImport("kernel32.dll")]
    internal static extern void* VirtualAlloc(void* address, nuint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualFree(void* address, nuint size, uint freeType);

    // One call returns every process with its memory counters. It is what .NET's own Process class uses.
    [DllImport("ntdll.dll")]
    internal static extern int NtQuerySystemInformation(int informationClass, void* buffer, uint length, out uint returnLength);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    // Builds an icon from one image of an .ico file, scaled to the size asked for.
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CreateIconFromResourceEx(
        byte* bits,
        uint size,
        [MarshalAs(UnmanagedType.Bool)] bool isIcon,
        uint version,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint ExtractIconEx(string file, int index, out nint largeIcon, out nint smallIcon, uint count);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProc(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public WindowProc WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public char* Buffer;
    }

    // The start of each entry NtQuerySystemInformation returns; the same layout .NET itself relies on.
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemProcessInfo
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long PrivateWorkingSetSize;
        public fixed byte Reserved1[40];
        public UnicodeString ImageName;
        public int BasePriority;
        public nint UniqueProcessId;
        public nuint Reserved2;
        public uint HandleCount;
        public uint SessionId;
        public nuint Reserved3;
        public nuint PeakVirtualSize;
        public nuint VirtualSize;
        public uint Reserved4;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint Reserved5;
        public nuint QuotaPagedPoolUsage;
        public nuint Reserved6;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivatePageCount;
    }
}
