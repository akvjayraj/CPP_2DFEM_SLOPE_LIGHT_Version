using System.Windows;
using GeotechSlopeApp.ViewModels.Dialogs;

namespace GeotechSlopeApp.Views.Dialogs;

public partial class AnalysisTypeWizardDialog : Window
{
    public AnalysisTypeWizardViewModel ViewModel { get; }

    public AnalysisTypeWizardDialog(AnalysisTypeWizardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        ViewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => ViewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(bool confirmed)
    {
        DialogResult = confirmed;
        Close();
    }
}
