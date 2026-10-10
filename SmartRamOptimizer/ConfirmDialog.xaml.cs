using System.Windows;
using System.Windows.Input;

namespace SmartRamOptimizer;

/// <summary>A warning with a yes-or-no answer, in the app's own style instead of a plain Windows message box.</summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>True only if the user chose <paramref name="confirmText"/>; closing the dialog any other way is a no.</summary>
    public static bool Show(Window owner, string heading, string message, string confirmText, string cancelText)
    {
        var dialog = new ConfirmDialog { Owner = owner };
        dialog.HeadingText.Text = heading;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;
        dialog.CancelButton.Content = cancelText;
        return dialog.ShowDialog() == true;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        CancelButton.Focus();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
