using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class FieldStressOverridesDialog : Window
{
    private readonly FieldStressViewModel _vm;

    public FieldStressOverridesDialog(FieldStressViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.PropertyChanged += OnVmPropertyChanged;
        SyncMaterialCombo();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FieldStressViewModel.SelectedOverride))
            SyncMaterialCombo();
    }

    private void SyncMaterialCombo()
    {
        if (MaterialComboBox == null) return;
        var ov = _vm.SelectedOverride;
        if (ov == null || _vm.MaterialsSource == null)
        {
            MaterialComboBox.SelectedIndex = -1;
            return;
        }
        for (int i = 0; i < _vm.MaterialsSource.Count; i++)
        {
            if (_vm.MaterialsSource[i].Id == ov.MaterialIndex ||
                _vm.MaterialsSource[i].Name == ov.MaterialName)
            {
                MaterialComboBox.SelectedIndex = i;
                return;
            }
        }
        MaterialComboBox.SelectedIndex = -1;
    }

    private void OnMaterialComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm.SelectedOverride == null) return;
        if (MaterialComboBox.SelectedItem is MaterialModel mat)
        {
            _vm.SelectedOverride.MaterialIndex = mat.Id > 0 ? mat.Id : MaterialComboBox.SelectedIndex + 1;
            _vm.SelectedOverride.MaterialName = mat.Name;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(System.EventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        base.OnClosed(e);
    }
}
