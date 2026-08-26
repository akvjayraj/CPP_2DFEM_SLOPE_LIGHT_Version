using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Views.Tabs;

public partial class ProjectSettingsAnalysisTypeView : UserControl
{
    public ProjectSettingsAnalysisTypeView()
    {
        InitializeComponent();
    }

    private void OnAnalysisTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not ProjectSettingsViewModel vm)
            return;

        switch (e.NewValue)
        {
            case AnalysisTypeNode { IsEnabled: false }:
                break;
            case AnalysisTypeNode typeNode:
                vm.SelectedAnalysisTreeNode = typeNode;
                break;
            case AnalysisCategoryNode categoryNode:
                vm.SelectedAnalysisCategoryNode = categoryNode;
                break;
        }
    }
}
