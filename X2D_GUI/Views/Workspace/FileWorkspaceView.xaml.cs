using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GeotechSlopeApp.Views.Workspace;

public partial class FileWorkspaceView : UserControl
{
    public FileWorkspaceView()
    {
        InitializeComponent();
    }

    private void OpenDropdown(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null)
            return;

        button.ContextMenu.DataContext = button.DataContext ?? DataContext;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Right;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
}
