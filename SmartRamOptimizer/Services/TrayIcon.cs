using System.IO;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace SmartRamOptimizer.Services;

/// <summary>The notification-area icon and its right-click menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const string AppName = "Smart RAM Optimizer";
    private const int BalloonMs = 2200;

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private readonly Drawing.Icon? _icon;
    private bool _isSyncing;

    public TrayIcon()
    {
        _pauseItem = new Forms.ToolStripMenuItem("Pause optimizing") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) => Raise(PauseChanged, _pauseItem.Checked);

        _startupItem = new Forms.ToolStripMenuItem("Launch with Windows") { CheckOnClick = true };
        _startupItem.CheckedChanged += (_, _) => Raise(LaunchAtStartupChanged, _startupItem.Checked);

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("Open", null, (_, _) => OpenRequested?.Invoke());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());

        _icon = LoadIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = AppName,
            Icon = _icon ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    public event Action<bool>? PauseChanged;
    public event Action<bool>? LaunchAtStartupChanged;

    /// <summary>Updates the menu's check marks without raising the change events.</summary>
    public void SetState(bool isPaused, bool launchAtStartup)
    {
        _isSyncing = true;
        try
        {
            _pauseItem.Checked = isPaused;
            _startupItem.Checked = launchAtStartup;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    public void SetStatus(string status)
    {
        // Windows limits tray tooltips to 127 characters.
        var text = $"{AppName}\n{status}";
        text = text.Length > 127 ? text[..127] : text;

        if (_notifyIcon.Text != text)
        {
            _notifyIcon.Text = text;
        }
    }

    public void ShowBalloon(string text, bool isWarning = false)
    {
        _notifyIcon.BalloonTipTitle = AppName;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = isWarning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(BalloonMs);
    }

    public void Hide()
    {
        _notifyIcon.Visible = false;
    }

    private void Raise(Action<bool>? handler, bool value)
    {
        if (!_isSyncing)
        {
            handler?.Invoke(value);
        }
    }

    private static Drawing.Icon? LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app_icon.ico"));
            if (resource is null)
            {
                return null;
            }

            using var stream = resource.Stream;
            return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
    }
}
