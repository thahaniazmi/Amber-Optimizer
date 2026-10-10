using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using SmartRamOptimizer.Models;
using SmartRamOptimizer.Services;
using SmartRamOptimizer.ViewModels;

namespace SmartRamOptimizer;

/// <summary>
/// Owns everything that outlives the window: the limiter, the tray icon and the settings.
/// The window is only created when the user actually opens it.
/// </summary>
public partial class App : Application
{
    private SingleInstance? _instance;
    private RamLimiterService? _limiter;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private MemorySnapshot? _latestSnapshot;
    private int _isSnapshotQueued;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        _viewModel.LaunchAtStartupFailed += () =>
            _tray?.ShowBalloon("Could not update Windows startup settings.", isWarning: true);

        _tray = new TrayIcon();
        _tray.OpenRequested += ShowWindow;
        _tray.ExitRequested += ExitApp;
        _tray.PauseChanged += isPaused => _viewModel.IsOptimizerEnabled = !isPaused;
        _tray.LaunchAtStartupChanged += isEnabled => _viewModel.LaunchAtStartup = isEnabled;
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
        _limiter.IsUiVisible = isVisible;

        if (!isVisible && _viewModel.TakeTrayHint())
        {
            _tray?.ShowBalloon("Still running in the system tray.");
        }
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
        _tray?.SetStatus(_viewModel.IsOptimizerEnabled
            ? $"{ByteFormat.Format(snapshot.FreedNowBytes)} freed"
            : "Paused");
    }

    private void SyncTray()
    {
        if (_viewModel is not null)
        {
            _tray?.SetState(!_viewModel.IsOptimizerEnabled, _viewModel.LaunchAtStartup);
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
