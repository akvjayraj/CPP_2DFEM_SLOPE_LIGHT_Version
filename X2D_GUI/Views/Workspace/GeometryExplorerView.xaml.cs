using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.ViewModels.Workspace;

namespace GeotechSlopeApp.Views.Workspace;

public partial class GeometryExplorerView : UserControl
{
    public GeometryExplorerView()
    {
        InitializeComponent();
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is GeometryWorkspaceViewModel vm && e.NewValue is ProjectExplorerNode node)
            vm.SelectedNode = node;
    }
}
