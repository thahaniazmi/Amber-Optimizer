using System.Windows.Media;
using SmartRamOptimizer.Models;
using SmartRamOptimizer.Services;

namespace SmartRamOptimizer.ViewModels;

/// <summary>One row of the application list.</summary>
public sealed class AppRowViewModel : ObservableObject
{
    private readonly Action _enabledChanged;
    private ImageSource? _icon;
    private string _statusText = "Not running";
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

    public async Task LoadIconAsync()
    {
        var exePath = Model.ExePath;
        var icon = await Task.Run(() => IconService.GetIcon(exePath));

        if (icon is not null && exePath == Model.ExePath)
        {
            Icon = icon;
        }
    }

    public void Update(AppUsage? usage, bool optimizerEnabled, bool aggressive)
    {
        if (usage is null || usage.ProcessCount == 0)
        {
            IsRunning = false;
            StatusText = "Not running";
            return;
        }

        IsRunning = true;

        var size = ByteFormat.Format(usage.WorkingSetBytes);
        var processes = usage.ProcessCount == 1 ? "1 process" : $"{usage.ProcessCount} processes";

        if (!optimizerEnabled)
        {
            StatusText = $"{size} · paused";
        }
        else if (!IsEnabled)
        {
            StatusText = $"{size} · not optimized";
        }
        else if (usage.AccessDenied)
        {
            StatusText = $"{size} · needs administrator";
        }
        else if (usage.IsForeground && !aggressive)
        {
            StatusText = $"{size} · {processes} · in use";
        }
        else
        {
            StatusText = $"{size} · {processes}";
        }
    }
}
