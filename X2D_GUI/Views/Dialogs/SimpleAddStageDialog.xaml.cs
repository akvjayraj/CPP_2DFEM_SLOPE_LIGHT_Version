using System;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class SimpleAddStageDialog : Window
{
    public SimpleAddStageDialogViewModel ViewModel { get; }

    public event EventHandler<(int StageNumber, string StageName)>? StageAdded;

    public SimpleAddStageDialog(SimpleAddStageDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
        
        // Set up AddStageCommand
        ViewModel.AddStageCommand = new RelayCommand(() =>
        {
            if (ViewModel.CanExecute)
            {
                StageAdded?.Invoke(this, (ViewModel.SelectedStageNumber, ViewModel.StageName));
                
                // Reset for next stage
                var maxExisting = ViewModel.AvailableStageNumbers.Count > 0 
                    ? ViewModel.AvailableStageNumbers.Max() 
                    : 0;
                ViewModel.SelectedStageNumber = maxExisting + 1;
                ViewModel.StageName = $"Stage {ViewModel.SelectedStageNumber}";
            }
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
