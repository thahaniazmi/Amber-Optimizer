using System.Windows.Media;
using SmartRamOptimizer.Models;
using SmartRamOptimizer.Services;

namespace SmartRamOptimizer.ViewModels;

/// <summary>One row of the application list.</summary>
public sealed class AppRowViewModel : ObservableObject
{
    private const long MinFreedBytesShown = 1024 * 1024;

    private readonly Action _enabledChanged;
    private ImageSource? _icon;
    private string _statusText = "Not running";
    private string _freedText = string.Empty;
    private bool _isRunning;

    public AppRowViewModel(ManagedApp model, Action enabledChanged)
    {
        Model = model;
        _enabledChanged = enabledChanged;
    }

    public ManagedApp Model { get; }

    public string Id => Model.Id;

    public string Name => Model.Name;

    /// <summary>Shown in place of the icon until the app's exe has been seen.</summary>
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    public string ToggleLabel => $"Optimize {Name}";

    public string RemoveLabel => $"Remove {Name}";

    public bool IsEnabled
    {
        get => Model.Enabled;
        set
        {
            if (Model.Enabled == value)
            {
                return;
            }

            Model.Enabled = value;
            OnPropertyChanged();
            _enabledChanged();
        }
    }

    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => _icon is not null;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    /// <summary>How much of this app's memory is being held back right now, or empty when there is nothing to show.</summary>
    public string FreedText
    {
        get => _freedText;
        private set
        {
            if (SetProperty(ref _freedText, value))
            {
                OnPropertyChanged(nameof(HasFreed));
            }
        }
    }

    public bool HasFreed => _freedText.Length > 0;

    public async Task LoadIconAsync()
    {
        var exePath = Model.ExePath;
        var icon = await Task.Run(() => IconService.GetIcon(exePath));

        if (icon is not null && exePath == Model.ExePath)
        {
            Icon = icon;
        }
    }

    public void Update(AppUsage? usage, bool optimizerEnabled)
    {
        if (usage is null || usage.ProcessCount == 0)
        {
            IsRunning = false;
            StatusText = "Not running";
            FreedText = string.Empty;
            return;
        }

        IsRunning = true;

        var size = ByteFormat.Format(usage.WorkingSetBytes);
        string detail;

        if (!optimizerEnabled)
        {
            detail = "paused";
        }
        else if (!IsEnabled)
        {
            detail = "not optimized";
        }
        else
        {
            // Says why an app is being left alone, so a row that is not shrinking is never a mystery.
            detail = usage.Activity switch
            {
                AppActivity.AccessDenied => "needs administrator",
                AppActivity.InUse => "in use",
                AppActivity.RecentlyUsed => $"trims in {usage.SecondsUntilTrim} s",
                AppActivity.AlreadySmall => "already small",
                _ => usage.ProcessCount == 1 ? "1 process" : $"{usage.ProcessCount} processes"
            };
        }

        StatusText = $"{size} · {detail}";
        FreedText = usage.FreedBytes >= MinFreedBytesShown ? ByteFormat.Format(usage.FreedBytes) : string.Empty;
    }
}
