using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.Models.Geometry;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public sealed class DefineJointPropertiesViewModel : INotifyPropertyChanged
{
    private FeJointPropertySet? _selected;
    private string _filter = "";
    private bool _showFilter;

    public DefineJointPropertiesViewModel(IEnumerable<FeJointPropertySet> source)
    {
        foreach (var s in source)
            WorkingSets.Add(s.Clone());
        if (WorkingSets.Count == 0)
            WorkingSets.Add(FeJointPropertySet.CreateDefault(1));
        _selected = WorkingSets[0];

        AddCommand = new RelayCommand(Add);
        DeleteCommand = new RelayCommand(Delete, () => Selected != null && WorkingSets.Count > 1);
        DuplicateCommand = new RelayCommand(Duplicate, () => Selected != null);
        ToggleFilterCommand = new RelayCommand(() => ShowFilter = !ShowFilter);
        AddStageFactorCommand = new RelayCommand(() =>
        {
            if (Selected == null) return;
            var n = Selected.StageFactors.Count == 0 ? 1 : Selected.StageFactors.Max(f => f.StageNumber) + 1;
            Selected.StageFactors.Add(new FeJointStageFactor { StageNumber = n });
        }, () => Selected?.UseStagedProperties == true);
        RemoveStageFactorCommand = new RelayCommand<FeJointStageFactor>(f =>
        {
            if (Selected != null && f != null)
                Selected.StageFactors.Remove(f);
        });
        DefineAdditionalPressureCommand = new RelayCommand(
            () => DefineAdditionalPressureRequested?.Invoke(this, EventArgs.Empty),
            () => Selected?.UseAdditionalPressure == true);
        DefineStageFactorsCommand = new RelayCommand(() =>
        {
            if (Selected == null) return;
            Selected.UseStagedProperties = true;
            if (Selected.StageFactors.Count == 0)
                Selected.StageFactors.Add(new FeJointStageFactor { StageNumber = 1 });
            (AddStageFactorCommand as RelayCommand)?.RaiseCanExecuteChanged();
        });
        OkCommand = new RelayCommand(() => OkRequested?.Invoke(this, EventArgs.Empty));
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty));
        if (_selected != null)
            _selected.PropertyChanged += OnSelectedSetPropertyChanged;
    }

    public event EventHandler? OkRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? DefineAdditionalPressureRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FeJointPropertySet> WorkingSets { get; } = new();
    public IReadOnlyList<FeJointSlipCriterion> Criteria { get; } = Enum.GetValues<FeJointSlipCriterion>();
    public IReadOnlyList<Color> ColourPalette { get; } = new[]
    {
        Color.FromRgb(0xEA, 0x58, 0x0C),
        Color.FromRgb(0xB4, 0x53, 0x09),
        Color.FromRgb(0xDC, 0x26, 0x26),
        Color.FromRgb(0x7C, 0x3A, 0xED),
        Color.FromRgb(0x25, 0x63, 0xEB),
        Color.FromRgb(0x0D, 0x94, 0x88),
        Color.FromRgb(0x65, 0xA3, 0x0D),
        Color.FromRgb(0xCA, 0x8A, 0x04),
        Color.FromRgb(0x57, 0x55, 0x4E),
        Color.FromRgb(0x0F, 0x17, 0x2A)
    };

    public IEnumerable<FeJointPropertySet> FilteredSets =>
        string.IsNullOrWhiteSpace(Filter)
            ? WorkingSets
            : WorkingSets.Where(s => s.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    public FeJointPropertySet? Selected
    {
        get => _selected;
        set
        {
            if (_selected != null)
                _selected.PropertyChanged -= OnSelectedSetPropertyChanged;
            _selected = value;
            if (_selected != null)
                _selected.PropertyChanged += OnSelectedSetPropertyChanged;
            OnPropertyChanged();
            (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DuplicateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddStageFactorCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DefineAdditionalPressureCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string Filter
    {
        get => _filter;
        set
        {
            _filter = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredSets));
        }
    }

    public bool ShowFilter
    {
        get => _showFilter;
        set { _showFilter = value; OnPropertyChanged(); }
    }

    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand ToggleFilterCommand { get; }
    public ICommand AddStageFactorCommand { get; }
    public ICommand RemoveStageFactorCommand { get; }
    public ICommand DefineAdditionalPressureCommand { get; }
    public ICommand DefineStageFactorsCommand { get; }
    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    private void Add()
    {
        var id = WorkingSets.Count == 0 ? 1 : WorkingSets.Max(s => s.Id) + 1;
        var set = FeJointPropertySet.CreateDefault(id);
        WorkingSets.Add(set);
        Selected = set;
        OnPropertyChanged(nameof(FilteredSets));
    }

    private void Delete()
    {
        if (Selected == null || WorkingSets.Count <= 1) return;
        var idx = WorkingSets.IndexOf(Selected);
        WorkingSets.Remove(Selected);
        Selected = WorkingSets[Math.Clamp(idx, 0, WorkingSets.Count - 1)];
        OnPropertyChanged(nameof(FilteredSets));
    }

    private void Duplicate()
    {
        if (Selected == null) return;
        var id = WorkingSets.Max(s => s.Id) + 1;
        var copy = Selected.Clone(id);
        WorkingSets.Add(copy);
        Selected = copy;
        OnPropertyChanged(nameof(FilteredSets));
    }

    private void OnSelectedSetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FeJointPropertySet.UseStagedProperties))
            (AddStageFactorCommand as RelayCommand)?.RaiseCanExecuteChanged();
        if (e.PropertyName == nameof(FeJointPropertySet.UseAdditionalPressure))
            (DefineAdditionalPressureCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
