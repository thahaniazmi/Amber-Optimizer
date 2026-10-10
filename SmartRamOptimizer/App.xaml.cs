using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SmartRamOptimizer.Models;
using SmartRamOptimizer.Services;
using SmartRamOptimizer.ViewModels;

namespace SmartRamOptimizer;

/// <summary>
/// Owns everything that outlives the window: the limiter, the tray icon and the settings.
/// The window only exists while it is on screen; it is built when opened and let go when hidden.
/// </summary>
public partial class App : Application
{
    private const long OneMegabyte = 1024 * 1024;

    private SingleInstance? _instance;
    private RamLimiterService? _limiter;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private ContextMenu? _trayMenu;
    private MemorySnapshot? _latestSnapshot;
    private int _isSnapshotQueued;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The window is small and mostly still. Drawing it on the CPU keeps the graphics driver,
        // which costs tens of megabytes, out of this process.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var startMinimized = e.Args.Any(arg =>
            string.Equals(arg, StartupService.MinimizedArgument, StringComparison.OrdinalIgnoreCase));

        _instance = SingleInstance.TryAcquire(
            () => Dispatcher.InvokeAsync(ShowWindow),
            showExisting: !startMinimized);

        if (_instance is null)
        {
            Shutdown();
            return;
        }

        // A tray app that dies silently is worse than one that logs the problem and carries on.
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorLog.Write(args.Exception);
            args.Handled = true;
        };
        SessionEnding += (_, _) => PrepareToExit();

        RepairStartupRegistration();

        _limiter = new RamLimiterService();
        _viewModel = new MainViewModel(SettingsService.Load(), _limiter);
        _viewModel.SettingsChanged += SyncTray;
        _viewModel.OptimizeNowCompleted += OnOptimizeNowCompleted;
        _viewModel.LaunchAtStartupFailed += () =>
            _tray?.ShowBalloon("Could not update Windows startup settings.", isWarning: true);

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowWindow;
        _tray.MenuRequested += ShowTrayMenu;
        SyncTray();

        _limiter.SnapshotUpdated += OnSnapshotUpdated;
        _limiter.Start();

        if (!startMinimized)
        {
            ShowWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _limiter?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void ShowWindow()
    {
        if (_isExiting || _viewModel is null)
        {
            return;
        }

        if (_window is null)
        {
            _window = new MainWindow(_viewModel);
            _window.IsVisibleChanged += Window_IsVisibleChanged;
            _window.ExitRequested += ExitApp;
        }

        _window.ShowAndActivate();
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_isExiting || _viewModel is null || _limiter is null)
        {
            return;
        }

        var isVisible = (bool)e.NewValue;
        _viewModel.IsViewActive = isVisible;

        if (isVisible)
        {
            _limiter.IsUiVisible = true;
            return;
        }

        if (_viewModel.TakeTrayHint())
        {
            _tray?.ShowBalloon("Still running in the system tray.");
        }

        // Not from inside this event: the window is still in the middle of hiding itself.
        Dispatcher.BeginInvoke(ReleaseWindow, DispatcherPriority.ApplicationIdle);
    }

    // A hidden window still holds everything it drew. Closing it gives that memory back,
    // and opening it again builds a new one in a fraction of a second.
    private void ReleaseWindow()
    {
        if (_isExiting || _limiter is null || _window is not { IsVisible: false } window)
        {
            return;
        }

        // A dialog that is still open keeps its owner; the limiter is told either way.
        if (window.OwnedWindows.Count == 0)
        {
            _window = null;
            window.IsVisibleChanged -= Window_IsVisibleChanged;
            window.ExitRequested -= ExitApp;
            window.AllowClose = true;
            window.Close();
        }

        _limiter.IsUiVisible = false;
    }

    private void ShowTrayMenu()
    {
        if (_isExiting || _viewModel is null || _tray is null || _trayMenu is { IsOpen: true })
        {
            return;
        }

        var menu = TrayMenu.Create(_viewModel, ShowWindow, ExitApp);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trayMenu, menu))
            {
                _trayMenu = null;
            }
        };

        _trayMenu = menu;
        menu.IsOpen = true;

        // A menu only notices a click somewhere else if its window is the active one.
        var menuWindow = (PresentationSource.FromVisual(menu) as HwndSource)?.Handle ?? 0;
        NativeMethods.SetForegroundWindow(menuWindow != 0 ? menuWindow : _tray.WindowHandle);
    }

    // Called on the limiter's thread. Only the newest snapshot matters, so at most one update is queued.
    private void OnSnapshotUpdated(MemorySnapshot snapshot)
    {
        Volatile.Write(ref _latestSnapshot, snapshot);

        if (Interlocked.Exchange(ref _isSnapshotQueued, 1) == 0)
        {
            Dispatcher.InvokeAsync(ApplyLatestSnapshot, DispatcherPriority.Background);
        }
    }

    private void ApplyLatestSnapshot()
    {
        Interlocked.Exchange(ref _isSnapshotQueued, 0);

        var snapshot = Volatile.Read(ref _latestSnapshot);
        if (snapshot is null || _isExiting || _viewModel is null)
        {
            return;
        }

        _viewModel.ApplySnapshot(snapshot);
        _tray?.SetStatus(_viewModel.TrayStatus);
    }

    // With the window open its button shows the result; from the tray menu a notification has to.
    private void OnOptimizeNowCompleted(long freedBytes)
    {
        if (_window is { IsVisible: true })
        {
            return;
        }

        _tray?.ShowBalloon(freedBytes >= OneMegabyte
            ? $"Freed {ByteFormat.Format(freedBytes)}."
            : "Nothing more to free right now.");
    }

    private void SyncTray()
    {
        if (_viewModel is not null && _tray is not null)
        {
            _tray.SetPaused(!_viewModel.IsOptimizerEnabled);
            _tray.SetStatus(_viewModel.TrayStatus);
        }
    }

    private void ExitApp()
    {
        if (_isExiting)
        {
            return;
        }

        PrepareToExit();
        _tray?.Hide();
        Shutdown();
    }

    private void PrepareToExit()
    {
        _isExiting = true;

        if (_trayMenu is not null)
        {
            _trayMenu.IsOpen = false;
        }

        if (_window is not null)
        {
            _window.AllowClose = true;
        }
    }

    private static void RepairStartupRegistration()
    {
        try
        {
            StartupService.RepairRegistration();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or SecurityException
            or InvalidOperationException)
        {
            ErrorLog.Write(exception);
        }
    }
}
