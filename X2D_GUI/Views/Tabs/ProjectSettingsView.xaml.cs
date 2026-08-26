using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Views;

namespace GeotechSlopeApp.Views.Tabs;

public partial class ProjectSettingsView : UserControl
{
    public ProjectSettingsView()
    {
        InitializeComponent();
        Loaded += ProjectSettingsView_Loaded;
    }

    private void ProjectSettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        GetProjectSettingsViewModel()?.RefreshStagedAnalysisBindings();
        LoadExistingStagesIntoFooterTabs();
    }

    private void LoadExistingStagesIntoFooterTabs()
    {
        var projectSettingsVM = GetProjectSettingsViewModel();
        var geometryVM = GetGeometryDefinitionViewModel();

        if (projectSettingsVM != null && geometryVM?.Stages != null && geometryVM.Stages.Count > 0)
        {
            var existingStages = geometryVM.Stages
                .Select(s => (s.StageNumber, s.StageName))
                .ToList();
            projectSettingsVM.UpdateStageFooterTabs(existingStages);
        }
    }

    private MainWindowViewModel? GetMainWindowViewModel()
    {
        var window = Window.GetWindow(this);
        if (window is MainWindow mainWindow && mainWindow.DataContext is MainWindowViewModel mainViewModel)
        {
            return mainViewModel;
        }
        return null;
    }

    private GeometryDefinitionViewModel? GetGeometryDefinitionViewModel()
    {
        var mainViewModel = GetMainWindowViewModel();
        return mainViewModel?.GeometryDefinitionVM;
    }

    private ProjectSettingsViewModel? GetProjectSettingsViewModel()
    {
        return DataContext as ProjectSettingsViewModel;
    }
}
