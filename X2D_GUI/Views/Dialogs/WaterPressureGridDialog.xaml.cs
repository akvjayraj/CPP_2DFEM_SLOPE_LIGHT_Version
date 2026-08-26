using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class WaterPressureGridDialog : Window
{
    private readonly WaterPressureGridDialogViewModel _vm;

    public WaterPressureGridDialog(WaterPressureGridDialogViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnVmPropertyChanged;
        SyncValueColumnHeader();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WaterPressureGridDialogViewModel.ValueColumnHeader)
            or nameof(WaterPressureGridDialogViewModel.SelectedGrid))
            SyncValueColumnHeader();
    }

    private void SyncValueColumnHeader()
    {
        if (PointsGrid?.Columns.Count > 3 && PointsGrid.Columns[3] is DataGridTextColumn col)
            col.Header = _vm.ValueColumnHeader;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        base.OnClosed(e);
    }
}
