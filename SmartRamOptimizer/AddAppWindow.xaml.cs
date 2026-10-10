using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using SmartRamOptimizer.Services;
using SmartRamOptimizer.ViewModels;

namespace SmartRamOptimizer;

/// <summary>Lets the user pick a running app, or browse for an exe, to add to the optimizer.</summary>
public partial class AddAppWindow : Window
{
    private const string AppName = "Smart RAM Optimizer";

    private readonly MainViewModel _viewModel;
    private readonly ObservableCollection<CandidateRowViewModel> _rows = [];
    private readonly ICollectionView _view;
    private bool _isLoaded;

    public AddAppWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = item => item is CandidateRowViewModel row && row.Matches(SearchBox.Text.Trim());
        CandidateList.ItemsSource = _view;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();

        try
        {
            // Reading exe descriptions and icons takes a moment; keep the window responsive meanwhile.
            var candidates = await Task.Run(RunningAppFinder.FindRunningApps);

            foreach (var candidate in candidates)
            {
                if (!_viewModel.IsManaged(candidate.ProcessName))
                {
                    _rows.Add(new CandidateRowViewModel(candidate));
                }
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            ErrorLog.Write(exception);
            ShowMessage("Could not read the list of running apps. You can still browse for an .exe.");
        }

        _isLoaded = true;
        UpdateEmptyText();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view.Refresh();
        UpdateEmptyText();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CandidateRowViewModel row && TryAdd(row.Candidate))
        {
            row.IsAdded = true;
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an application",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var candidate = RunningAppFinder.FromExePath(dialog.FileName, out var error);
        if (candidate is null)
        {
            ShowMessage(error ?? "That application can't be added.");
            return;
        }

        if (_viewModel.IsManaged(candidate.ProcessName))
        {
            ShowMessage($"{candidate.Name} is already in your list.");
            return;
        }

        if (TryAdd(candidate))
        {
            Close();
        }
    }

    private bool TryAdd(AppCandidate candidate)
    {
        if (candidate.Advice.Suitability == Suitability.NotRecommended)
        {
            var answer = MessageBox.Show(
                this,
                $"Optimizing {candidate.Name} is not recommended.\n\n"
                + $"{candidate.Advice.Reason}\n\n"
                + "Add it anyway?",
                AppName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        if (_viewModel.AddApp(candidate))
        {
            return true;
        }

        ShowMessage($"{candidate.Name} can't be added.");
        return false;
    }

    private void UpdateEmptyText()
    {
        if (!_isLoaded)
        {
            return;
        }

        EmptyText.Text = SearchBox.Text.Trim().Length > 0
            ? "No running app matches. Try browsing for its .exe."
            : "No other apps are running. Browse for an .exe instead.";
        EmptyText.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
