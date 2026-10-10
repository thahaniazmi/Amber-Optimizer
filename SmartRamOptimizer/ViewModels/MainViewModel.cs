using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Windows.Threading;
using SmartRamOptimizer.Models;
using SmartRamOptimizer.Services;

namespace SmartRamOptimizer.ViewModels;

/// <summary>
/// State behind the main window. It owns the settings: every change made here is saved
/// and handed to the limiter, so the window and the tray menu only have to set properties.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const string OptimizeNowLabel = "Optimize now";
    private const long OneMegabyte = 1024 * 1024;
    private const int HighMemoryPercent = 80;
    private const int CriticalMemoryPercent = 90;

    private static readonly TimeSpan UndoDuration = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan WelcomeDuration = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OptimizeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OptimizeResultDuration = TimeSpan.FromSeconds(2.5);

    private readonly AppSettings _settings;
    private readonly RamLimiterService _limiter;
    private readonly Dictionary<string, AppRowViewModel> _rowsById = new(StringComparer.Ordinal);

    private MemorySnapshot? _lastSnapshot;
    private DispatcherTimer? _noticeTimer;
    private DispatcherTimer? _optimizeTimer;
    private Action? _noticeAction;
    private bool _isViewActive;
    private bool _launchAtStartup;
    private bool _canOptimizeNow = true;
    private string _statusText = string.Empty;
    private string _statusState = "Idle";
    private string _freedNowText = "0 MB";
    private string _freedSessionText = "0 MB";
    private string _systemMemoryText = string.Empty;
    private string _systemMemoryLevel = "Normal";
    private string _optimizeNowText = OptimizeNowLabel;
    private string _noticeText = string.Empty;
    private string _noticeActionText = string.Empty;
    private string _aboutText = VersionText;
    private double _systemMemoryPercent;

    public MainViewModel(AppSettings settings, RamLimiterService limiter)
    {
        _settings = settings;
        _limiter = limiter;
        _launchAtStartup = ReadLaunchAtStartup();

        Apps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasApps));

        foreach (var app in _settings.Apps)
        {
            Apps.Add(CreateRow(app));
        }

        ResumeIfPauseEnded();
        PushConfig();
        UpdateStatus();

        if (_settings.DetectedAppCount > 0)
        {
            var found = _settings.DetectedAppCount == 1 ? "1 app" : $"{_settings.DetectedAppCount} apps";
            ShowNotice($"Found {found} worth optimizing. Switch off any you'd rather leave alone.", null, null, WelcomeDuration);
        }
    }

    /// <summary>Raised when the pause or launch-with-Windows setting changes, so the tray icon can follow.</summary>
    public event Action? SettingsChanged;

    public event Action? LaunchAtStartupFailed;

    /// <summary>Raised with the bytes freed when an "Optimize now" request has been carried out.</summary>
    public event Action<long>? OptimizeNowCompleted;

    public ObservableCollection<AppRowViewModel> Apps { get; } = [];

    public bool HasApps => Apps.Count > 0;

    public bool IsOptimizerEnabled
    {
        get => _settings.OptimizerEnabled;
        set
        {
            if (_settings.OptimizerEnabled == value && _settings.PausedUntil is null)
            {
                return;
            }

            // Switching by hand, either way, replaces whatever timed pause was running.
            _settings.OptimizerEnabled = value;
            _settings.PausedUntil = null;
            OnPropertyChanged();
            Commit();
            SettingsChanged?.Invoke();
        }
    }

    public bool IsAggressiveMode
    {
        get => _settings.AggressiveMode;
        set
        {
            if (_settings.AggressiveMode == value)
            {
                return;
            }

            _settings.AggressiveMode = value;
            OnPropertyChanged();
            Commit();
        }
    }

    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set
        {
            if (_launchAtStartup == value)
            {
                return;
            }

            try
            {
                StartupService.SetLaunchAtStartup(value);
            }
            catch (Exception exception) when (IsRegistryFailure(exception))
            {
                ErrorLog.Write(exception);
                LaunchAtStartupFailed?.Invoke();
            }

            _launchAtStartup = ReadLaunchAtStartup();
            OnPropertyChanged();
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>False while the window is hidden, so nothing is formatted or redrawn for nobody.</summary>
    public bool IsViewActive
    {
        get => _isViewActive;
        set
        {
            _isViewActive = value;
            if (value)
            {
                RefreshView();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>"Active", "Idle" or "Paused"; the window colours the status line from it.</summary>
    public string StatusState
    {
        get => _statusState;
        private set => SetProperty(ref _statusState, value);
    }

    public string FreedNowText
    {
        get => _freedNowText;
        private set => SetProperty(ref _freedNowText, value);
    }

    public string FreedSessionText
    {
        get => _freedSessionText;
        private set => SetProperty(ref _freedSessionText, value);
    }

    public string SystemMemoryText
    {
        get => _systemMemoryText;
        private set => SetProperty(ref _systemMemoryText, value);
    }

    public double SystemMemoryPercent
    {
        get => _systemMemoryPercent;
        private set => SetProperty(ref _systemMemoryPercent, value);
    }

    /// <summary>"Normal", "High" or "Critical"; the window colours the memory bar from it.</summary>
    public string SystemMemoryLevel
    {
        get => _systemMemoryLevel;
        private set => SetProperty(ref _systemMemoryLevel, value);
    }

    /// <summary>The button's label: its name, then briefly what pressing it freed.</summary>
    public string OptimizeNowText
    {
        get => _optimizeNowText;
        private set => SetProperty(ref _optimizeNowText, value);
    }

    public bool CanOptimizeNow
    {
        get => _canOptimizeNow;
        private set => SetProperty(ref _canOptimizeNow, value);
    }

    /// <summary>A short message under the list, such as "Removed Discord" with its Undo.</summary>
    public string NoticeText
    {
        get => _noticeText;
        private set
        {
            if (SetProperty(ref _noticeText, value))
            {
                OnPropertyChanged(nameof(IsNoticeVisible));
            }
        }
    }

    public string NoticeActionText
    {
        get => _noticeActionText;
        private set
        {
            if (SetProperty(ref _noticeActionText, value))
            {
                OnPropertyChanged(nameof(HasNoticeAction));
            }
        }
    }

    public bool IsNoticeVisible => _noticeText.Length > 0;

    public bool HasNoticeAction => _noticeActionText.Length > 0;

    /// <summary>The version, and what the optimizer itself is using, for the bottom of the settings menu.</summary>
    public string AboutText
    {
        get => _aboutText;
        private set => SetProperty(ref _aboutText, value);
    }

    /// <summary>The line under the app's name in the tray icon's tooltip.</summary>
    public string TrayStatus => _settings.OptimizerEnabled
        ? $"{ByteFormat.Format(_lastSnapshot?.FreedNowBytes ?? 0)} freed"
        : StatusText;

    /// <summary>Where the window was last left, or null if it has never been moved.</summary>
    public (double Left, double Top)? WindowPosition =>
        _settings is { WindowLeft: { } left, WindowTop: { } top } ? (left, top) : null;

    private static string VersionText
    {
        get
        {
            var version = typeof(MainViewModel).Assembly.GetName().Version;
            return version is null ? string.Empty : $"Version {version.ToString(3)}";
        }
    }

    /// <summary>Called on the UI thread with each measurement from the limiter.</summary>
    public void ApplySnapshot(MemorySnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        StoreDiscoveredExePaths(snapshot);
        ResumeIfPauseEnded();

        if (snapshot.TrimNowFreedBytes is { } freedBytes)
        {
            ShowOptimizeNowResult(freedBytes);
        }

        if (_isViewActive)
        {
            RefreshView();
        }
    }

    /// <summary>Trims every switched-on app once, right now. The button then shows what that freed.</summary>
    public void OptimizeNow()
    {
        if (!_canOptimizeNow)
        {
            return;
        }

        CanOptimizeNow = false;
        OptimizeNowText = "Optimizing…";
        _limiter.TrimNow();

        // The answer comes with the next measurement; the timer is only a way back if it never does.
        RestartTimer(ref _optimizeTimer, OptimizeTimeout, ResetOptimizeNow);
    }

    /// <summary>Pauses optimizing, for a while or (with null) until the user resumes it.</summary>
    public void PauseFor(TimeSpan? duration)
    {
        _settings.OptimizerEnabled = false;
        _settings.PausedUntil = duration is { } length ? DateTimeOffset.Now + length : null;
        OnPropertyChanged(nameof(IsOptimizerEnabled));
        Commit();
        SettingsChanged?.Invoke();
    }

    public bool IsManaged(string processName)
    {
        return _settings.Apps.Any(app => app.ProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase));
    }

    public bool AddApp(AppCandidate candidate)
    {
        if (IsManaged(candidate.ProcessName) || AppRules.IsProtectedProcess(candidate.ProcessName))
        {
            return false;
        }

        var app = new ManagedApp
        {
            Name = candidate.Name,
            ProcessNames = [candidate.ProcessName],
            ExePath = candidate.ExePath
        };

        _settings.Apps.Add(app);
        Apps.Add(CreateRow(app));
        Commit();
        return true;
    }

    /// <summary>Removes the app straight away and offers to undo it for a few seconds.</summary>
    public void RemoveApp(AppRowViewModel row)
    {
        if (!_rowsById.Remove(row.Id))
        {
            return;
        }

        var app = row.Model;
        var index = _settings.Apps.IndexOf(app);
        _settings.Apps.Remove(app);
        Apps.Remove(row);
        Commit();

        ShowNotice($"Removed {app.Name}", "Undo", () => Restore(app, index), UndoDuration);
    }

    public void RunNoticeAction()
    {
        var action = _noticeAction;
        DismissNotice();
        action?.Invoke();
    }

    public void DismissNotice()
    {
        _noticeTimer?.Stop();
        _noticeAction = null;
        NoticeText = string.Empty;
        NoticeActionText = string.Empty;
    }

    public void SaveWindowPosition(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top)
            || (_settings.WindowLeft == left && _settings.WindowTop == top))
        {
            return;
        }

        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        SettingsService.Save(_settings);
    }

    /// <summary>True the first time it is asked, so the "still running in the tray" hint shows once ever.</summary>
    public bool TakeTrayHint()
    {
        if (_settings.TrayHintShown)
        {
            return false;
        }

        _settings.TrayHintShown = true;
        SettingsService.Save(_settings);
        return true;
    }

    private AppRowViewModel CreateRow(ManagedApp app)
    {
        var row = new AppRowViewModel(app, Commit);
        _rowsById[app.Id] = row;
        _ = row.LoadIconAsync();
        return row;
    }

    private void Restore(ManagedApp app, int index)
    {
        // The same app may have been added again by hand in the meantime.
        if (_rowsById.ContainsKey(app.Id) || app.ProcessNames.Any(IsManaged))
        {
            return;
        }

        index = Math.Clamp(index, 0, _settings.Apps.Count);
        _settings.Apps.Insert(index, app);
        Apps.Insert(index, CreateRow(app));
        Commit();
    }

    private void ShowNotice(string text, string? actionText, Action? action, TimeSpan duration)
    {
        _noticeAction = action;
        NoticeText = text;
        NoticeActionText = actionText ?? string.Empty;
        RestartTimer(ref _noticeTimer, duration, DismissNotice);
    }

    private void ShowOptimizeNowResult(long freedBytes)
    {
        OptimizeNowCompleted?.Invoke(freedBytes);
        CanOptimizeNow = false;
        OptimizeNowText = freedBytes >= OneMegabyte ? $"Freed {ByteFormat.Format(freedBytes)}" : "Nothing more to free";
        RestartTimer(ref _optimizeTimer, OptimizeResultDuration, ResetOptimizeNow);
    }

    private void ResetOptimizeNow()
    {
        _optimizeTimer?.Stop();
        OptimizeNowText = OptimizeNowLabel;
        CanOptimizeNow = true;
    }

    private static void RestartTimer(ref DispatcherTimer? timer, TimeSpan interval, Action elapsed)
    {
        if (timer is null)
        {
            timer = new DispatcherTimer();
            timer.Tick += (_, _) => elapsed();
        }

        timer.Stop();
        timer.Interval = interval;
        timer.Start();
    }

    // A timed pause ends at the first measurement after its time is up; the loop never sleeps longer than 30 seconds.
    private void ResumeIfPauseEnded()
    {
        if (!_settings.OptimizerEnabled && _settings.PausedUntil is { } until && DateTimeOffset.Now >= until)
        {
            IsOptimizerEnabled = true;
        }
    }

    // Save, tell the limiter, and show the change straight away instead of at the next measurement.
    private void Commit()
    {
        SettingsService.Save(_settings);
        PushConfig();

        if (_isViewActive)
        {
            RefreshView();
        }
        else
        {
            UpdateStatus();
        }
    }

    private void PushConfig()
    {
        var apps = _settings.Apps
            .Select(app => new TargetApp(
                app.Id,
                app.ProcessNames.ToArray(),
                app.Enabled,
                NeedsExePath: string.IsNullOrEmpty(app.ExePath) || !File.Exists(app.ExePath)))
            .ToArray();

        _limiter.Configure(new LimiterConfig(apps, _settings.OptimizerEnabled, _settings.AggressiveMode));
    }

    // The limiter reports where a running app's exe lives when we did not know; keep it for the icon.
    private void StoreDiscoveredExePaths(MemorySnapshot snapshot)
    {
        var changed = false;

        foreach (var usage in snapshot.Apps)
        {
            if (usage.ExePath is null
                || !_rowsById.TryGetValue(usage.AppId, out var row)
                || string.Equals(row.Model.ExePath, usage.ExePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            row.Model.ExePath = usage.ExePath;
            _ = row.LoadIconAsync();
            changed = true;
        }

        if (changed)
        {
            SettingsService.Save(_settings);
            PushConfig();
        }
    }

    private void RefreshView()
    {
        var snapshot = _lastSnapshot;

        foreach (var row in Apps)
        {
            row.Update(FindUsage(snapshot, row.Id), _settings.OptimizerEnabled);
        }

        UpdateStatus();

        if (snapshot is null)
        {
            return;
        }

        FreedNowText = ByteFormat.Format(snapshot.FreedNowBytes);
        FreedSessionText = ByteFormat.Format(snapshot.FreedSessionBytes);

        if (snapshot.SystemTotalBytes > 0)
        {
            var usedBytes = snapshot.SystemTotalBytes - snapshot.SystemAvailableBytes;
            var percent = Math.Round(100d * usedBytes / snapshot.SystemTotalBytes);
            SystemMemoryPercent = percent;
            SystemMemoryLevel = percent >= CriticalMemoryPercent ? "Critical" : percent >= HighMemoryPercent ? "High" : "Normal";
            SystemMemoryText =
                $"{ByteFormat.FormatGigabytes(usedBytes)} of {ByteFormat.FormatGigabytes(snapshot.SystemTotalBytes)} GB";
        }

        if (snapshot.OwnMemoryBytes > 0)
        {
            AboutText = $"{VersionText} · using {ByteFormat.Format(snapshot.OwnMemoryBytes)} itself";
        }
    }

    private static AppUsage? FindUsage(MemorySnapshot? snapshot, string appId)
    {
        if (snapshot is not null)
        {
            foreach (var usage in snapshot.Apps)
            {
                if (usage.AppId == appId)
                {
                    return usage;
                }
            }
        }

        return null;
    }

    private void UpdateStatus()
    {
        if (!_settings.OptimizerEnabled)
        {
            StatusText = _settings.PausedUntil is { } until ? $"Paused until {until.LocalDateTime:t}" : "Paused";
            StatusState = "Paused";
            return;
        }

        var optimizing = Apps.Count(row => row.IsEnabled && row.IsRunning);
        if (optimizing > 0)
        {
            StatusText = optimizing == 1 ? "Optimizing 1 app" : $"Optimizing {optimizing} apps";
            StatusState = "Active";
            return;
        }

        StatusText = Apps.Any(row => row.IsEnabled) ? "Waiting for apps to start" : "Nothing to optimize";
        StatusState = "Idle";
    }

    private static bool ReadLaunchAtStartup()
    {
        try
        {
            return StartupService.IsLaunchAtStartupEnabled();
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return false;
        }
    }

    private static bool IsRegistryFailure(Exception exception)
    {
        return exception is UnauthorizedAccessException
            or IOException
            or SecurityException
            or InvalidOperationException;
    }
}
