using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using SmartRamOptimizer.Services;
using SmartRamOptimizer.ViewModels;

namespace SmartRamOptimizer;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    /// <summary>Raised when the user picks "Quit" from the settings menu.</summary>
    public event Action? ExitRequested;

    /// <summary>Closing normally only hides the window; the app sets this when it is really exiting.</summary>
    public bool AllowClose { get; set; }

    public void ShowAndActivate()
    {
        Show();

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

        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            WindowState = WindowState.Normal;
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
        Hide();
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

    private void CoffeeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AppConfig.IsCoffeeUrlConfigured)
        {
            MessageBox.Show(
                this,
                "The support page hasn't been set up yet. Thanks for wanting to help!",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
