using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.Views.Dialogs;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Tabs;

public partial class BoundariesPopupView : UserControl
{
    public BoundariesPopupView()
    {
        InitializeComponent();
    }

    private GeometryDefinitionViewModel? GetGeometryDefinitionViewModel()
    {
        // First try to get from this UserControl's DataContext
        if (DataContext is GeometryDefinitionViewModel geometryVM)
        {
            return geometryVM;
        }

        // Fallback: Try to get from the Owner window (MainWindow)
        var window = Window.GetWindow(this);
        if (window?.Owner is MainWindow mainWindow && mainWindow.DataContext is MainWindowViewModel mainViewModel)
        {
            return mainViewModel.GeometryDefinitionVM;
        }

        return null;
    }

    private void AddStage_Click(object sender, RoutedEventArgs e)
    {
        var geometryVM = GetGeometryDefinitionViewModel();
        if (geometryVM == null)
        {
            MessageBox.Show("Unable to access Geometry Definition data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        geometryVM.OpenAddStageDialogCommand.Execute(null);
    }

    private void AddExcavation_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Implement Add Excavation functionality
        MessageBox.Show("Add Excavation clicked", "Boundaries", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AddJoint_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        var main = window as MainWindow ?? window?.Owner as MainWindow;
        if (main?.DataContext is MainWindowViewModel vm)
        {
            vm.OpenJointWizardCommand.Execute(null);
            return;
        }

        var geometryVM = GetGeometryDefinitionViewModel();
        if (geometryVM == null)
        {
            MessageBox.Show("Unable to access Geometry Definition data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        geometryVM.StartDrawWeakPlaneCommand.Execute(null);
    }

    private void AddTunnel_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Implement Add Tunnel functionality
        MessageBox.Show("Add Tunnel clicked", "Boundaries", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}


