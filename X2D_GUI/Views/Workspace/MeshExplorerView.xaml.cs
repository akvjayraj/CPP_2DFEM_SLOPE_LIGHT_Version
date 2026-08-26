using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.ViewModels.Workspace;

namespace GeotechSlopeApp.Views.Workspace;

public partial class MeshExplorerView : UserControl
{
    public MeshExplorerView()
    {
        InitializeComponent();
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MeshWorkspaceViewModel vm && e.NewValue is ProjectExplorerNode node)
            vm.SelectedNode = node;
    }
}
