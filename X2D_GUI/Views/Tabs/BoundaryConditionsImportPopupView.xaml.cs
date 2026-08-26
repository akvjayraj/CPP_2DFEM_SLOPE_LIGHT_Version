using System.Windows.Controls;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Views.Tabs;

public partial class BoundaryConditionsImportPopupView : UserControl
{
    public BoundaryConditionsImportPopupView()
    {
        InitializeComponent();
    }

    private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (DataContext is BoundaryConditionsViewModel viewModel)
        {
            viewModel.SyncToFEAnalysisViewModel();
        }
    }
}
