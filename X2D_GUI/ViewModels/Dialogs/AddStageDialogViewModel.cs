using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public class AddStageDialogViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<StageShapeItem> _availableShapes = new();
    private readonly ObservableCollection<StageShapeItem> _selectedShapes = new();

    public ObservableCollection<StageShapeItem> AvailableShapes => _availableShapes;
    public ObservableCollection<StageShapeItem> SelectedShapes => _selectedShapes;

    public ICommand AddShapeCommand { get; set; } = new RelayCommand<StageShapeItem>(_ => { });
    public ICommand RemoveShapeCommand { get; set; } = new RelayCommand<StageShapeItem>(_ => { });

    public bool CanExecute => _selectedShapes.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
