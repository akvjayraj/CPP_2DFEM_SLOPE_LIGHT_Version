using System.Windows;
using GeotechSlopeApp.Services.FieldStations;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class FieldStationImportWizardDialog : Window
{
    public FieldStationImportWizardDialog(FieldStationImportWizardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
    }

    public FieldStationImportWizardViewModel ViewModel { get; }
    public FieldStationColumnMap? ResultMap { get; private set; }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TryBuildMap(out var map, out var error))
        {
            MessageBox.Show(this, error, "Import Evidence Pack", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultMap = map;
        DialogResult = true;
        Close();
    }
}
