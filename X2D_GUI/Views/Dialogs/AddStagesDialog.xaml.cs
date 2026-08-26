using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.ViewModels;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class AddStagesDialog : Window
{
    public AddStagesDialogViewModel ViewModel { get; }

    public event EventHandler<List<(int StageNumber, string StageName)>>? StagesAdded;
    public event EventHandler? StagesCleared;

    public AddStagesDialog(AddStagesDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
        
        // Set up SaveCommand
        ViewModel.SaveCommand = new RelayCommand(() =>
        {
            try
            {
                if (ViewModel.CanExecute)
                {
                    // Validate and filter stages before saving (allow Stage 0)
                    var stages = ViewModel.StageEntries
                        .Where(s => s.StageNumber >= 0 && !string.IsNullOrWhiteSpace(s.StageName))
                        .Select(s => (s.StageNumber, s.StageName.Trim()))
                        .ToList();
                    
                    if (stages.Count == 0)
                    {
                        MessageBox.Show("Please enter at least one valid stage with a stage number and name.", 
                            "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    // Invoke the event to process and save the stages
                    StagesAdded?.Invoke(this, stages);
                    
                    // Close the dialog after saving
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving stages: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        });
        
        // Set up ClearCommand
        ViewModel.ClearCommand = new RelayCommand(() =>
        {
            ViewModel.ClearAll();
            // Notify that stages were cleared so they can be removed from storage
            StagesCleared?.Invoke(this, EventArgs.Empty);
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
