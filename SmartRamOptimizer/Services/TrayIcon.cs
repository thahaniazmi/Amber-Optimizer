using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace SmartRamOptimizer.Services;

/// <summary>
/// The notification-area icon. It talks to the Windows shell directly, through one hidden window that
/// receives the icon's clicks, because pulling in Windows Forms for this would cost more memory
/// than the rest of the app put together. The menu itself is built by <see cref="TrayMenu"/>.
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const string AppName = "Smart RAM Optimizer";
    private const string WindowClassName = AppIdentity.Name + ".Tray";
    private const uint CallbackMessage = NativeMethods.MessageApp + 1;
    private const uint IconId = 1;
    private const uint IconResourceVersion = 0x00030000;

    // Held in a field: Windows keeps calling it for as long as the window exists.
    private readonly NativeMethods.WindowProc _windowProc;
    private readonly Dispatcher _dispatcher;
    private readonly uint _taskbarCreatedMessage;
    private readonly nint _instance;
    private readonly nint _window;
    private readonly nint _activeIcon;
    private readonly nint _pausedIcon;
    private string _tip = AppName;
    private bool _isPaused;
    private bool _disposed;

    public TrayIcon()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _windowProc = HandleMessage;
        _instance = NativeMethods.GetModuleHandle(null);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        var windowClass = new NativeMethods.WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WindowClassEx>(),
            WindowProc = _windowProc,
            Instance = _instance,
            ClassName = WindowClassName
        };
        NativeMethods.RegisterClassEx(ref windowClass);

        _window = NativeMethods.CreateWindowEx(0, WindowClassName, AppName, 0, 0, 0, 0, 0, 0, 0, _instance, 0);
        if (_window == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not create the tray icon's window.");
        }

        var size = NativeMethods.GetSystemMetrics(NativeMethods.MetricSmallIconWidth);
        _activeIcon = LoadIcon("Assets/app_icon.ico", size);
        _pausedIcon = LoadIcon("Assets/app_icon_paused.ico", size);
        Update(NativeMethods.NotifyAdd);
    }

    /// <summary>The icon was clicked.</summary>
    public event Action? OpenRequested;

    /// <summary>The icon was right-clicked.</summary>
    public event Action? MenuRequested;

    /// <summary>Something to hand the keyboard focus to when no window of ours is showing.</summary>
    public nint WindowHandle => _window;

    /// <summary>Greys the icon while optimizing is paused.</summary>
    public void SetPaused(bool isPaused)
    {
        if (_isPaused != isPaused)
        {
            _isPaused = isPaused;
            Update(NativeMethods.NotifyModify);
        }
    }

    public void SetStatus(string status)
    {
        // Windows limits tray tooltips to 127 characters.
        var tip = $"{AppName}\n{status}";
        tip = tip.Length > 127 ? tip[..127] : tip;

        if (_tip != tip)
        {
            _tip = tip;
            Update(NativeMethods.NotifyModify);
        }
    }

    public void ShowBalloon(string text, bool isWarning = false)
    {
        var data = CreateData();
        data.Flags = NativeMethods.NotifyFlagInfo;
        data.Info = text;
        data.InfoTitle = AppName;
        data.InfoFlags = isWarning ? NativeMethods.NotifyInfoIconWarning : NativeMethods.NotifyInfoIconInfo;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NotifyModify, ref data);
    }

    public void Hide()
    {
        var data = CreateData();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NotifyDelete, ref data);
    }

    private void Update(uint message)
    {
        var data = CreateData();
        data.Flags = NativeMethods.NotifyFlagMessage | NativeMethods.NotifyFlagIcon | NativeMethods.NotifyFlagTip;
        data.CallbackMessage = CallbackMessage;
        data.Icon = _isPaused && _pausedIcon != 0 ? _pausedIcon : _activeIcon;
        data.Tip = _tip;

        // A change can arrive before the taskbar has accepted the icon (signing in, Explorer restarting).
        if (!NativeMethods.Shell_NotifyIcon(message, ref data) && message == NativeMethods.NotifyModify)
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NotifyAdd, ref data);
        }
    }

    private NativeMethods.NotifyIconData CreateData()
    {
        return new NativeMethods.NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            Window = _window,
            Id = IconId,
            Tip = string.Empty,
            Info = string.Empty,
            InfoTitle = string.Empty
        };
    }

    private nint HandleMessage(nint window, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (message == CallbackMessage)
            {
                switch ((uint)(lParam & 0xFFFF))
                {
                    // A double-click is two clicks to us, and opening twice does no harm.
                    case NativeMethods.MessageLeftButtonUp:
                    case NativeMethods.MessageLeftButtonDoubleClick:
                        Post(OpenRequested);
                        break;
                    case NativeMethods.MessageRightButtonUp:
                    case NativeMethods.MessageContextMenu:
                        Post(MenuRequested);
                        break;
                }

                return 0;
            }

            if (message == _taskbarCreatedMessage && message != 0)
            {
                // Explorer restarted and lost every icon it was showing.
                Update(NativeMethods.NotifyAdd);
                return 0;
            }
        }
        catch (Exception exception)
        {
            // Nothing may escape into the Windows code that called this.
            ErrorLog.Write(exception);
        }

        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    // Runs the handler after this message has been dealt with, where the app's normal error handling applies.
    private void Post(Action? handler)
    {
        if (handler is not null && !_disposed)
        {
            _dispatcher.BeginInvoke(handler);
        }
    }

    private static nint LoadIcon(string resourcePath, int size)
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri($"pack://application:,,,/{resourcePath}"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                var icon = ReadIcon(stream, size);
                if (icon != 0)
                {
                    return icon;
                }
            }
        }
        catch (IOException)
        {
        }

        return LoadExeIcon();
    }

    // An .ico file is a directory followed by the same picture at several sizes.
    // Reads only the directory and the one picture that fits best, not the whole file.
    private static nint ReadIcon(Stream stream, int size)
    {
        Span<byte> header = stackalloc byte[6];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
        {
            return 0;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        Span<byte> entry = stackalloc byte[16];
        var bestWidth = 0;
        var bestLength = 0;
        long bestOffset = 0;

        for (var i = 0; i < count; i++)
        {
            if (stream.ReadAtLeast(entry, entry.Length, throwOnEndOfStream: false) < entry.Length)
            {
                break;
            }

            var width = entry[0] == 0 ? 256 : entry[0];
            var isBetter = bestWidth == 0
                || (bestWidth < size ? width > bestWidth : width >= size && width < bestWidth);
            if (isBetter)
            {
                bestWidth = width;
                bestLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
                bestOffset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            }
        }

        if (bestWidth == 0 || bestLength <= 0 || bestOffset + bestLength > stream.Length)
        {
            return 0;
        }

        var bits = new byte[bestLength];
        stream.Position = bestOffset;
        stream.ReadExactly(bits);

        fixed (byte* pointer = bits)
        {
            return NativeMethods.CreateIconFromResourceEx(pointer, (uint)bits.Length, true, IconResourceVersion, size, size, 0);
        }
    }

    private static nint LoadExeIcon()
    {
        var path = Environment.ProcessPath;
        if (path is null || NativeMethods.ExtractIconEx(path, 0, out var largeIcon, out var smallIcon, 1) == 0)
        {
            return 0;
        }

        if (largeIcon != 0)
        {
            NativeMethods.DestroyIcon(largeIcon);
        }

        return smallIcon;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Hide();
        NativeMethods.DestroyWindow(_window);
        NativeMethods.UnregisterClass(WindowClassName, _instance);

        if (_activeIcon != 0)
        {
            NativeMethods.DestroyIcon(_activeIcon);
        }

        if (_pausedIcon != 0)
        {
            NativeMethods.DestroyIcon(_pausedIcon);
        }
    }
}
