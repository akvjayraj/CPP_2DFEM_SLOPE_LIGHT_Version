using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GeotechSlopeApp.Views.Workspace;

public partial class AnalysisRibbonView : UserControl
{
    public AnalysisRibbonView()
    {
        InitializeComponent();
    }

    private void OpenFileDropdown(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null)
            return;

        var menu = button.ContextMenu;
        menu.DataContext = button.DataContext ?? DataContext;
        ApplyDarkMenuChrome(menu);
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>
    /// Default MenuItem chrome paints a light SystemColors icon gutter. Force those
    /// keys onto the popup so MDL2 glyphs stay readable on Dark.
    /// </summary>
    private static void ApplyDarkMenuChrome(ContextMenu menu)
    {
        var panel = menu.TryFindResource("PanelBackgroundBrush") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        var text = menu.TryFindResource("WindowForegroundBrush") as Brush
                   ?? new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
        menu.Resources[SystemColors.MenuBrushKey] = panel;
        menu.Resources[SystemColors.MenuBarBrushKey] = panel;
        menu.Resources[SystemColors.ControlBrushKey] = panel;
        menu.Resources[SystemColors.WindowBrushKey] = panel;
        menu.Resources[SystemColors.MenuTextBrushKey] = text;
        menu.Resources[SystemColors.ControlTextBrushKey] = text;
        menu.Background = panel;
        menu.Foreground = text;
    }
}
