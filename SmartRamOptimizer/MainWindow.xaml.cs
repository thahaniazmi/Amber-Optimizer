using System.ComponentModel;
using System.IO;
using System.Runtime.Versioning;
using System.Security;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using SmartRamOptimizer.Models;
using SmartRamOptimizer.Services;
using SmartRamOptimizer.Targets;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace SmartRamOptimizer;

[SupportedOSPlatform("windows")]
public partial class MainWindow : Window, IDisposable
{
    private readonly RamLimiterService _limiterService = new(AppTargets.All);
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Drawing.Icon? _customTrayIcon;
    private readonly bool _startMinimized;
    private readonly Forms.ToolStripMenuItem _startupMenuItem;
    private bool _isExitRequested;
    private bool _isMinimizingToTray;

    public MainWindow(bool startMinimized = false)
    {
        _startMinimized = startMinimized;

        InitializeComponent();
        ApplyWindowIcon();

        _customTrayIcon = LoadTrayIcon();
        _trayIcon = BuildTrayIcon(_customTrayIcon, out _startupMenuItem);
        _limiterService.SnapshotUpdated += LimiterService_SnapshotUpdated;
        _limiterService.AllTargetProcessesExited += LimiterService_AllTargetProcessesExited;
    }

    private async void LimiterService_AllTargetProcessesExited(object? sender, EventArgs e)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            await ExitApplicationAsync();
        });
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _limiterService.StartAsync();
        var isActive = true;
        _limiterService.SetLimiterActive(isActive);
        AnimateToggle(isActive, durationMs: 1);
        UpdateStatus(isActive);
        RefreshStartupState();

        if (_startMinimized)
        {
            MinimizeToTray(showNotification: false);
        }
    }

    private void LimiterToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var isActive = !_limiterService.IsLimiterActive;
        _limiterService.SetLimiterActive(isActive);
        AnimateToggle(isActive, durationMs: 240);
        UpdateStatus(isActive);
    }

    private void LimiterService_SnapshotUpdated(object? sender, MemorySnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            // Discord card
            var discord = snapshot.For(DiscordTarget.Instance);
            DiscordRamText.Text = FormatBytes(discord.WorkingSetBytes);
            DiscordProcessCountText.Text = FormatProcessCount(discord.ProcessCount);

            // Brave card
            var brave = snapshot.For(BraveTarget.Instance);
            BraveRamText.Text = FormatBytes(brave.WorkingSetBytes);
            BraveProcessCountText.Text = FormatProcessCount(brave.ProcessCount);

            TotalRamText.Text = $"Total RAM: {FormatBytes(snapshot.TotalWorkingSetBytes)} ({snapshot.TotalProcessCount} processes)";

            UpdateStatus(snapshot.IsLimiterActive);
        });
    }

    private static string FormatProcessCount(int processCount)
    {
        return processCount == 1 ? "1 process" : $"{processCount} processes";
    }

    private void UpdateStatus(bool isActive)
    {
        StatusText.Text = isActive ? "Limiter Active" : "Limiter Disabled";
        StatusText.Foreground = new SolidColorBrush(isActive ? MediaColor.FromRgb(86, 217, 143) : MediaColor.FromRgb(255, 143, 155));
    }

    private void AnimateToggle(bool isActive, int durationMs)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        ToggleThumbLabel.Text = isActive ? "ON" : "OFF";
        ToggleThumbLabel.Foreground = new SolidColorBrush(isActive ? MediaColor.FromRgb(25, 146, 85) : MediaColor.FromRgb(218, 75, 90));

        ToggleTrackBrush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation
            {
                To = isActive ? MediaColor.FromRgb(54, 206, 125) : MediaColor.FromRgb(155, 66, 76),
                Duration = duration,
                EasingFunction = easing
            });

        ToggleTrackBorderBrush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation
            {
                To = isActive ? MediaColor.FromRgb(86, 217, 143) : MediaColor.FromRgb(70, 55, 67),
                Duration = duration,
                EasingFunction = easing
            });

        ToggleGlowEffect.BeginAnimation(
            DropShadowEffect.OpacityProperty,
            new DoubleAnimation
            {
                To = isActive ? 0.55 : 0,
                Duration = duration,
                EasingFunction = easing
            });

        ToggleGlowEffect.BeginAnimation(
            DropShadowEffect.BlurRadiusProperty,
            new DoubleAnimation
            {
                To = isActive ? 34 : 18,
                Duration = duration,
                EasingFunction = easing
            });

        ToggleThumbTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation
            {
                To = isActive ? 140 : 0,
                Duration = duration,
                EasingFunction = easing
            });
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        var megabytes = bytes / 1024d / 1024d;
        return megabytes >= 100
            ? $"{megabytes:0} MB"
            : $"{megabytes:0.0} MB";
    }

    private Forms.NotifyIcon BuildTrayIcon(Drawing.Icon? trayIcon, out Forms.ToolStripMenuItem startupMenuItem)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        startupMenuItem = new Forms.ToolStripMenuItem("Launch at startup")
        {
            CheckOnClick = true
        };
        startupMenuItem.CheckedChanged += StartupMenuItem_CheckedChanged;
        menu.Items.Add(startupMenuItem);
        menu.Items.Add("Exit", null, async (_, _) => await ExitApplicationAsync());

        var notifyIcon = new Forms.NotifyIcon
        {
            Text = "Smart RAM Optimizer",
            Icon = trayIcon ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };

        notifyIcon.DoubleClick += (_, _) => ShowFromTray();
        return notifyIcon;
    }

    private void AutoStartCheckBox_Click(object sender, RoutedEventArgs e)
    {
        SetLaunchAtStartup(AutoStartCheckBox.IsChecked == true);
    }

    private void StartupMenuItem_CheckedChanged(object? sender, EventArgs e)
    {
        SetLaunchAtStartup(_startupMenuItem.Checked);
    }

    private void RefreshStartupState()
    {
        var isEnabled = StartupService.IsLaunchAtStartupEnabled();
        AutoStartCheckBox.IsChecked = isEnabled;
        _startupMenuItem.CheckedChanged -= StartupMenuItem_CheckedChanged;
        _startupMenuItem.Checked = isEnabled;
        _startupMenuItem.CheckedChanged += StartupMenuItem_CheckedChanged;
    }

    private void SetLaunchAtStartup(bool isEnabled)
    {
        try
        {
            StartupService.SetLaunchAtStartup(isEnabled);
            RefreshStartupState();
        }
        catch (UnauthorizedAccessException)
        {
            RefreshStartupState();
            ShowStartupError();
        }
        catch (IOException)
        {
            RefreshStartupState();
            ShowStartupError();
        }
        catch (SecurityException)
        {
            RefreshStartupState();
            ShowStartupError();
        }
    }

    private void ShowStartupError()
    {
        _trayIcon.BalloonTipTitle = "Smart RAM Optimizer";
        _trayIcon.BalloonTipText = "Could not update Windows startup settings.";
        _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
        _trayIcon.ShowBalloonTip(2200);
    }

    private static string GetAppIconPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "app_icon.ico");
    }

    private void ApplyWindowIcon()
    {
        var iconPath = GetAppIconPath();
        if (File.Exists(iconPath))
        {
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
            return;
        }

        using var appIcon = LoadExecutableIcon();
        if (appIcon is not null)
        {
            var imageSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                appIcon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            imageSource.Freeze();
            Icon = imageSource;
        }
    }

    private static Drawing.Icon? LoadTrayIcon()
    {
        var iconPath = GetAppIconPath();
        var traySize = Forms.SystemInformation.SmallIconSize;
        if (File.Exists(iconPath))
        {
            try
            {
                return new Drawing.Icon(iconPath, traySize);
            }
            catch
            {
            }
        }
        return LoadExecutableIcon();
    }

    private static Drawing.Icon? LoadExecutableIcon()
    {
        return Environment.ProcessPath is { Length: > 0 } processPath && File.Exists(processPath)
            ? Drawing.Icon.ExtractAssociatedIcon(processPath)
            : null;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && !_isExitRequested && IsVisible)
        {
            MinimizeToTray();
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeToTray();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeToTray();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        MinimizeToTray();
    }

    private void MinimizeToTray(bool showNotification = true)
    {
        if (_isMinimizingToTray || !IsVisible)
        {
            return;
        }

        _isMinimizingToTray = true;
        try
        {
            WindowState = WindowState.Minimized;
            Hide();
            if (showNotification)
            {
                ShowTrayNotification();
            }
        }
        finally
        {
            _isMinimizingToTray = false;
        }
    }

    private void ShowTrayNotification()
    {
        _trayIcon.BalloonTipTitle = "Smart RAM Optimizer";
        _trayIcon.BalloonTipText = "Still running in the system tray.";
        _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _trayIcon.ShowBalloonTip(2200);
    }

    private async Task ExitApplicationAsync()
    {
        _isExitRequested = true;
        _trayIcon.Visible = false;
        await _limiterService.StopAsync();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    public void Dispose()
    {
        _trayIcon.Dispose();
        _customTrayIcon?.Dispose();
        _limiterService.Dispose();
    }
}
