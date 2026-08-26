using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public class SimpleAddStageDialogViewModel : INotifyPropertyChanged
{
    private int _selectedStageNumber = 1;
    private string _stageName = "Stage 1";

    public IReadOnlyList<int> AvailableStageNumbers { get; set; } = new List<int> { 1 };
    public int SelectedStageNumber
    {
        get => _selectedStageNumber;
        set { _selectedStageNumber = value; OnPropertyChanged(); }
    }
    public string StageName
    {
        get => _stageName;
        set { _stageName = value; OnPropertyChanged(); }
    }

    public ICommand AddStageCommand { get; set; } = new RelayCommand(() => { });

    public bool CanExecute => !string.IsNullOrWhiteSpace(_stageName);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
