using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.ViewModels.Workspace;

namespace GeotechSlopeApp.Views.Workspace;

public partial class MaterialsExplorerView : UserControl
{
    public MaterialsExplorerView()
    {
        InitializeComponent();
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MaterialsWorkspaceViewModel vm && e.NewValue is ProjectExplorerNode node)
            vm.SelectedNode = node;
    }
}
