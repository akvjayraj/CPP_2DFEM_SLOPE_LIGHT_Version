using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public class StageEntryViewModel : INotifyPropertyChanged
{
    private int _stageNumber;
    private string _stageName = "";

    public int StageNumber
    {
        get => _stageNumber;
        set { _stageNumber = value; OnPropertyChanged(); }
    }

    public string StageName
    {
        get => _stageName;
        set { _stageName = value; OnPropertyChanged(); }
    }

    public ICommand DeleteCommand { get; set; } = new RelayCommand(() => { });

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class AddStagesDialogViewModel : INotifyPropertyChanged
{
    private int _numberOfStages;
    private readonly ObservableCollection<StageEntryViewModel> _stageEntries = new();

    public AddStagesDialogViewModel(IEnumerable<int> existingStageNumbers, IEnumerable<(int StageNumber, string StageName)> existingStages)
    {
        foreach (var (num, name) in existingStages)
        {
            var entry = new StageEntryViewModel { StageNumber = num, StageName = name };
            entry.DeleteCommand = new RelayCommand(() => _stageEntries.Remove(entry));
            _stageEntries.Add(entry);
        }
        _numberOfStages = _stageEntries.Count;
    }

    public int NumberOfStages
    {
        get => _numberOfStages;
        set
        {
            _numberOfStages = value;
            while (_stageEntries.Count < value)
            {
                var n = _stageEntries.Count;
                var entry = new StageEntryViewModel { StageNumber = n, StageName = $"Stage {n}" };
                entry.DeleteCommand = new RelayCommand(() => _stageEntries.Remove(entry));
                _stageEntries.Add(entry);
            }
            while (_stageEntries.Count > value && _stageEntries.Count > 0)
                _stageEntries.RemoveAt(_stageEntries.Count - 1);
            OnPropertyChanged();
        }
    }

    public ObservableCollection<StageEntryViewModel> StageEntries => _stageEntries;

    public ICommand SaveCommand { get; set; } = new RelayCommand(() => { });
    public ICommand ClearCommand { get; set; } = new RelayCommand(() => { });

    public bool CanExecute => _stageEntries.Any(s => s.StageNumber >= 0 && !string.IsNullOrWhiteSpace(s.StageName));

    public void ClearAll()
    {
        _stageEntries.Clear();
        _numberOfStages = 0;
        OnPropertyChanged(nameof(NumberOfStages));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
