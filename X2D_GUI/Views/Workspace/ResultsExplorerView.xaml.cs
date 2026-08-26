using System.Windows.Controls;
using System.Windows.Input;
using GeotechSlopeApp.ViewModels.Workspace;

namespace GeotechSlopeApp.Views.Workspace;

public partial class ResultsExplorerView : UserControl
{
    public ResultsExplorerView()
    {
        InitializeComponent();
    }

    private void OnNodeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem item || item.DataContext is not ResultsManagerNode node)
            return;
        if (DataContext is ResultsWorkspaceViewModel vm)
            vm.SelectNodeCommand.Execute(node);
    }
}
