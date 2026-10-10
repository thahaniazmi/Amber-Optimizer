using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SmartRamOptimizer.ViewModels;

namespace SmartRamOptimizer;

/// <summary>
/// The tray icon's right-click menu. Built fresh each time it opens, so it always shows the
/// current state and holds no memory while it is closed.
/// </summary>
internal static class TrayMenu
{
    private const string OpenGlyph = "";
    private const string OptimizeGlyph = "";
    private const string PauseGlyph = "";
    private const string ResumeGlyph = "";
    private const string CheckGlyph = "";
    private const string QuitGlyph = "";

    public static ContextMenu Create(MainViewModel viewModel, Action open, Action exit)
    {
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("TrayMenuStyle"),
            Placement = PlacementMode.MousePoint
        };

        menu.Items.Add(CreateItem("Open Smart RAM Optimizer", OpenGlyph, open));
        menu.Items.Add(CreateItem("Optimize now", OptimizeGlyph, viewModel.OptimizeNow));
        menu.Items.Add(CreateSeparator());

        if (viewModel.IsOptimizerEnabled)
        {
            menu.Items.Add(CreateItem("Pause for 30 minutes", PauseGlyph, () => viewModel.PauseFor(TimeSpan.FromMinutes(30))));
            menu.Items.Add(CreateItem("Pause for 1 hour", null, () => viewModel.PauseFor(TimeSpan.FromHours(1))));
            menu.Items.Add(CreateItem("Pause until I resume", null, () => viewModel.PauseFor(null)));
        }
        else
        {
            menu.Items.Add(CreateItem("Resume optimizing", ResumeGlyph, () => viewModel.IsOptimizerEnabled = true));
        }

        // The tick is drawn as the item's icon; IsChecked is what a screen reader announces.
        var launchAtStartup = viewModel.LaunchAtStartup;
        var startupItem = CreateItem(
            "Launch with Windows",
            launchAtStartup ? CheckGlyph : null,
            () => viewModel.LaunchAtStartup = !launchAtStartup);
        startupItem.IsCheckable = true;
        startupItem.IsChecked = launchAtStartup;

        menu.Items.Add(CreateSeparator());
        menu.Items.Add(startupItem);
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(CreateItem("Quit", QuitGlyph, exit));

        return menu;
    }

    private static MenuItem CreateItem(string text, string? glyph, Action onClick)
    {
        var item = new MenuItem
        {
            Header = text,
            Icon = glyph,
            Style = (Style)Application.Current.FindResource("TrayMenuItemStyle")
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static Separator CreateSeparator()
    {
        return new Separator { Style = (Style)Application.Current.FindResource("TrayMenuSeparatorStyle") };
    }
}
