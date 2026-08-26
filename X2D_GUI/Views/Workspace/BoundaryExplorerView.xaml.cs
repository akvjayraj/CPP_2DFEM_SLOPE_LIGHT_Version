using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.ViewModels.Workspace;

namespace GeotechSlopeApp.Views.Workspace;

public partial class BoundaryExplorerView : UserControl
{
    public BoundaryExplorerView()
    {
        InitializeComponent();
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is BoundaryWorkspaceViewModel vm && e.NewValue is ProjectExplorerNode node)
            vm.SelectedNode = node;
    }
}
