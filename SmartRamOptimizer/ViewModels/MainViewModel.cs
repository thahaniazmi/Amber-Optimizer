using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using SmartRamOptimizer.Models;
using SmartRamOptimizer.Services;

namespace SmartRamOptimizer.ViewModels;

/// <summary>
/// State behind the main window. It owns the settings: every change made here is saved
/// and handed to the limiter, so the window and the tray menu only have to set properties.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly RamLimiterService _limiter;
    private readonly Dictionary<string, AppRowViewModel> _rowsById = new(StringComparer.Ordinal);

    private MemorySnapshot? _lastSnapshot;
    private bool _isViewActive;
    private bool _launchAtStartup;
    private string _statusText = string.Empty;
    private string _statusState = "Idle";
    private string _freedNowText = "0 MB";
    private string _freedSessionText = "0 MB";
    private string _systemMemoryText = string.Empty;
    private double _systemMemoryPercent;

    public MainViewModel(AppSettings settings, RamLimiterService limiter)
    {
        _settings = settings;
        _limiter = limiter;
        _launchAtStartup = ReadLaunchAtStartup();

        Apps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasApps));

        foreach (var app in _settings.Apps)
        {
            AddRow(app);
        }

        PushConfig();
        UpdateStatus();
    }

    /// <summary>Raised when the pause or launch-with-Windows setting changes, so the tray menu can follow.</summary>
    public event Action? SettingsChanged;

    public event Action? LaunchAtStartupFailed;

    public ObservableCollection<AppRowViewModel> Apps { get; } = [];

    public bool HasApps => Apps.Count > 0;

    public bool IsOptimizerEnabled
    {
        get => _settings.OptimizerEnabled;
        set
        {
            if (_settings.OptimizerEnabled == value)
            {
                return;
            }

            _settings.OptimizerEnabled = value;
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

    /// <summary>Called on the UI thread with each measurement from the limiter.</summary>
    public void ApplySnapshot(MemorySnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        StoreDiscoveredExePaths(snapshot);

        if (_isViewActive)
        {
            RefreshView();
        }
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
        AddRow(app);
        Commit();
        return true;
    }

    public void RemoveApp(AppRowViewModel row)
    {
        if (!_rowsById.Remove(row.Id))
        {
            return;
        }

        _settings.Apps.Remove(row.Model);
        Apps.Remove(row);
        Commit();
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

    private void AddRow(ManagedApp app)
    {
        var row = new AppRowViewModel(app, Commit);
        _rowsById[app.Id] = row;
        Apps.Add(row);
        _ = row.LoadIconAsync();
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
            var usage = snapshot?.Apps.FirstOrDefault(candidate => candidate.AppId == row.Id);
            row.Update(usage, _settings.OptimizerEnabled, _settings.AggressiveMode);
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
            SystemMemoryPercent = Math.Round(100d * usedBytes / snapshot.SystemTotalBytes);
            SystemMemoryText =
                $"{ByteFormat.FormatGigabytes(usedBytes)} of {ByteFormat.FormatGigabytes(snapshot.SystemTotalBytes)} GB";
        }
    }

    private void UpdateStatus()
    {
        if (!_settings.OptimizerEnabled)
        {
            StatusText = "Paused";
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
