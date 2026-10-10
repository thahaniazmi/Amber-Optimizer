using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SmartRamOptimizer.Services;
using SmartRamOptimizer.ViewModels;

namespace SmartRamOptimizer;

public partial class MainWindow : Window
{
    private static readonly Duration FadeInDuration = new(TimeSpan.FromMilliseconds(140));

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Never opens the placeholder page, which anyone could register and collect donations on.
        CoffeeButton.Visibility = AppConfig.IsCoffeeUrlConfigured ? Visibility.Visible : Visibility.Collapsed;
        RestorePosition();
    }

    /// <summary>Raised when the user picks "Quit" from the settings menu.</summary>
    public event Action? ExitRequested;

    /// <summary>Closing normally only hides the window; the app sets this when it is really exiting.</summary>
    public bool AllowClose { get; set; }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            // Fades in instead of popping onto the screen.
            Opacity = 0;
            Show();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeInDuration));
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        // Lifts the window above the others even when Windows will not hand it the focus.
        Topmost = true;
        Topmost = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (IsVisible)
        {
            _viewModel.SaveWindowPosition(Left, Top);
        }

        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    // Comes back where the user left it, unless that spot is on a screen that is no longer there.
    private void RestorePosition()
    {
        if (_viewModel.WindowPosition is not { } position)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var titleBar = new NativeMethods.Point
        {
            X = (int)((position.Left + (Width / 2)) * dpi.DpiScaleX),
            Y = (int)((position.Top + 48) * dpi.DpiScaleY)
        };

        if (NativeMethods.MonitorFromPoint(titleBar, NativeMethods.MonitorDefaultToNull) != 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = position.Left;
            Top = position.Top;
        }
    }

    private void HideToTray()
    {
        _viewModel.SaveWindowPosition(Left, Top);
        Hide();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
            WindowState = WindowState.Normal;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !SettingsPopup.IsOpen)
        {
            e.Handled = true;
            HideToTray();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !SettingsPopup.IsOpen)
        {
            DragMove();
        }
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void AddAppButton_Click(object sender, RoutedEventArgs e)
    {
        new AddAppWindow(_viewModel) { Owner = this }.ShowDialog();
    }

    private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AppRowViewModel row)
        {
            _viewModel.RemoveApp(row);
        }
    }

    private void NoticeActionButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RunNoticeAction();
    }

    private void NoticeDismissButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DismissNotice();
    }

    private void OptimizeNowButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OptimizeNow();
    }

    private void CoffeeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AppConfig.IsCoffeeUrlConfigured)
        {
            return;
        }

        try
        {
            // UseShellExecute hands the link to the default browser.
            Process.Start(new ProcessStartInfo(AppConfig.CoffeeUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            ErrorLog.Write(exception);
        }
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = false;
        ExitRequested?.Invoke();
    }
}
