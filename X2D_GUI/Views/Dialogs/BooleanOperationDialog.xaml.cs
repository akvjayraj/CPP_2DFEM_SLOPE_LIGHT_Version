using System.Windows;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class BooleanOperationDialog : Window
{
    public BooleanOperationDialogViewModel ViewModel { get; }

    public BooleanOperationDialog(BooleanOperationDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.Validate())
        {
            MessageBox.Show(
                string.IsNullOrWhiteSpace(ViewModel.ValidationMessage) ? "Invalid selection." : ViewModel.ValidationMessage,
                "Boolean operation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}

