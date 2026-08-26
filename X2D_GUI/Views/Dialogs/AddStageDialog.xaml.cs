using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class AddStageDialog : Window
{
    public AddStageDialogViewModel ViewModel { get; }

    public AddStageDialog(AddStageDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    private void AddSelectedShapes_Click(object sender, RoutedEventArgs e)
    {
        var availableListBox = AvailableShapesListBox;
        if (availableListBox != null && availableListBox.SelectedItems.Count > 0)
        {
            foreach (StageShapeItem shape in availableListBox.SelectedItems)
            {
                ViewModel.AddShapeCommand.Execute(shape);
            }
        }
    }

    private void RemoveSelectedShapes_Click(object sender, RoutedEventArgs e)
    {
        var selectedListBox = SelectedShapesListBox;
        if (selectedListBox != null && selectedListBox.SelectedItems.Count > 0)
        {
            var shapesToRemove = selectedListBox.SelectedItems.Cast<StageShapeItem>().ToList();
            foreach (var shape in shapesToRemove)
            {
                ViewModel.RemoveShapeCommand.Execute(shape);
            }
        }
    }

    public event EventHandler? StageAdded;

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void AddStageButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanExecute)
        {
            // Trigger the stage addition event but don't close the dialog
            StageAdded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
