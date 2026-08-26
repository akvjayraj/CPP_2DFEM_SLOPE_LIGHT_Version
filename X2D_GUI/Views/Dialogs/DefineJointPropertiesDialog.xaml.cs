using System;
using System.Windows;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class DefineJointPropertiesDialog : Window
{
    public DefineJointPropertiesViewModel ViewModel { get; }
    public bool Confirmed { get; private set; }

    public DefineJointPropertiesDialog(DefineJointPropertiesViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        ViewModel.OkRequested += (_, _) =>
        {
            Confirmed = true;
            DialogResult = true;
            Close();
        };
        ViewModel.CancelRequested += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        ViewModel.DefineAdditionalPressureRequested += (_, _) => EditAdditionalPressure();
    }

    private void EditAdditionalPressure()
    {
        if (ViewModel.Selected == null) return;
        var dlg = new AdditionalJointPressureDialog(ViewModel.Selected.AdditionalPressureKpa) { Owner = this };
        if (dlg.ShowDialog() == true)
            ViewModel.Selected.AdditionalPressureKpa = dlg.PressureKpa;
    }
}
