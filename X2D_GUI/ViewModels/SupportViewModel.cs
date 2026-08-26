using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Models.Support;
using GeotechSlopeApp.Services.Support;

namespace GeotechSlopeApp.ViewModels;

public class SupportViewModel : INotifyPropertyChanged
{
    private FeSupportPropertySet? _selectedPropertySet;
    private FeSupportElement? _selectedElement;
    private double _patternStartX;
    private double _patternEndX = 10;
    private double _patternSpacing = 1.5;
    private double _patternLength = 8;
    private double _patternInclination = 15;
    private int _patternRows = 1;
    private string _patternStatus = "Set Start X / End X along the slope face, then Apply Pattern.";

    public SupportViewModel()
    {
        PropertySets.Add(new FeSupportPropertySet { Id = 1, Name = "Soil Nail 1", Type = FeSupportType.SoilNail });
        _selectedPropertySet = PropertySets[0];
        AddPropertySetCommand = new RelayCommand(OnAddPropertySet);
        DeletePropertySetCommand = new RelayCommand(OnDeletePropertySet, () => SelectedPropertySet != null && PropertySets.Count > 1);
        AddElementCommand = new RelayCommand(OnAddElement, () => SelectedPropertySet != null);
        DeleteElementCommand = new RelayCommand(OnDeleteElement, () => SelectedElement != null);
        ApplyPatternCommand = new RelayCommand(OnApplyPattern, () => SelectedPropertySet != null);
        ClearElementsCommand = new RelayCommand(() =>
        {
            Elements.Clear();
            PatternStatus = "All support placements cleared.";
        });
    }

    public GeometryDefinitionViewModel? GeometryDefinition { get; set; }

    public ObservableCollection<FeSupportPropertySet> PropertySets { get; } = new();
    public ObservableCollection<FeSupportElement> Elements { get; } = new();
    public IReadOnlyList<FeSupportType> AvailableTypes { get; } = Enum.GetValues<FeSupportType>();

    public FeSupportPropertySet? SelectedPropertySet
    {
        get => _selectedPropertySet;
        set
        {
            _selectedPropertySet = value;
            OnPropertyChanged();
            (DeletePropertySetCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddElementCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyPatternCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public FeSupportElement? SelectedElement
    {
        get => _selectedElement;
        set
        {
            _selectedElement = value;
            OnPropertyChanged();
            (DeleteElementCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public double PatternStartX { get => _patternStartX; set { _patternStartX = value; OnPropertyChanged(); } }
    public double PatternEndX { get => _patternEndX; set { _patternEndX = value; OnPropertyChanged(); } }
    public double PatternSpacing { get => _patternSpacing; set { _patternSpacing = Math.Max(0.05, value); OnPropertyChanged(); } }
    public double PatternLength { get => _patternLength; set { _patternLength = Math.Max(0.1, value); OnPropertyChanged(); } }
    public double PatternInclination { get => _patternInclination; set { _patternInclination = value; OnPropertyChanged(); } }
    public int PatternRows { get => _patternRows; set { _patternRows = Math.Clamp(value, 1, FeSupportPatternGenerator.MaxRows); OnPropertyChanged(); } }
    public string PatternStatus { get => _patternStatus; set { _patternStatus = value ?? ""; OnPropertyChanged(); } }

    public ICommand AddPropertySetCommand { get; }
    public ICommand DeletePropertySetCommand { get; }
    public ICommand AddElementCommand { get; }
    public ICommand DeleteElementCommand { get; }
    public ICommand ApplyPatternCommand { get; }
    public ICommand ClearElementsCommand { get; }

    public void ReplaceFrom(
        IEnumerable<FeSupportPropertySet>? properties,
        IEnumerable<FeSupportElement>? elements)
    {
        PropertySets.Clear();
        Elements.Clear();
        if (properties != null)
        {
            foreach (var p in properties)
                PropertySets.Add(p);
        }
        if (PropertySets.Count == 0)
            PropertySets.Add(new FeSupportPropertySet { Id = 1, Name = "Soil Nail 1" });
        if (elements != null)
        {
            foreach (var e in elements)
                Elements.Add(e);
        }
        SelectedPropertySet = PropertySets[0];
        SelectedElement = Elements.FirstOrDefault();
    }

    private void OnAddPropertySet()
    {
        var id = PropertySets.Count == 0 ? 1 : PropertySets.Max(p => p.Id) + 1;
        var set = new FeSupportPropertySet
        {
            Id = id,
            Name = $"Support {id}",
            Type = SelectedPropertySet?.Type ?? FeSupportType.SoilNail
        };
        PropertySets.Add(set);
        SelectedPropertySet = set;
    }

    private void OnDeletePropertySet()
    {
        if (SelectedPropertySet == null || PropertySets.Count <= 1) return;
        var id = SelectedPropertySet.Id;
        PropertySets.Remove(SelectedPropertySet);
        foreach (var el in Elements.Where(e => e.PropertySetId == id).ToList())
            Elements.Remove(el);
        SelectedPropertySet = PropertySets[0];
    }

    private void OnAddElement()
    {
        if (SelectedPropertySet == null) return;
        var id = Elements.Count == 0 ? 1 : Elements.Max(e => e.Id) + 1;
        var el = new FeSupportElement
        {
            Id = id,
            PropertySetId = SelectedPropertySet.Id,
            HeadX = PatternStartX,
            HeadY = 0,
            TailX = PatternStartX + PatternLength * Math.Cos(PatternInclination * Math.PI / 180.0),
            TailY = -PatternLength * Math.Sin(PatternInclination * Math.PI / 180.0)
        };
        Elements.Add(el);
        SelectedElement = el;
    }

    private void OnDeleteElement()
    {
        if (SelectedElement == null) return;
        Elements.Remove(SelectedElement);
        SelectedElement = Elements.FirstOrDefault();
    }

    private void OnApplyPattern()
    {
        if (SelectedPropertySet == null) return;
        var request = new FeSupportPatternRequest(
            SelectedPropertySet.Type,
            PatternStartX,
            PatternEndX,
            PatternSpacing,
            FixedCount: null,
            PatternLength,
            PatternInclination,
            FeSupportSpacingMeasure.AlongFace,
            RowCount: PatternRows);
        var result = FeSupportPatternGenerator.Generate(request, BuildGround());
        PatternStatus = result.Summary;
        if (!result.IsValid) return;
        var nextId = Elements.Count == 0 ? 1 : Elements.Max(e => e.Id) + 1;
        foreach (var seg in result.Segments)
        {
            Elements.Add(new FeSupportElement
            {
                Id = nextId++,
                PropertySetId = SelectedPropertySet.Id,
                HeadX = seg.HeadX,
                HeadY = seg.HeadY,
                TailX = seg.TailX,
                TailY = seg.TailY
            });
        }
    }

    public IReadOnlyList<Point> BuildGround()
    {
        var verts = GeometryDefinition?.GeometryVertexRows;
        if (verts == null || verts.Count < 2)
            return Array.Empty<Point>();
        return verts
            .OrderBy(v => v.AxisX)
            .ThenByDescending(v => v.AxisY)
            .GroupBy(v => Math.Round(v.AxisX, 6))
            .Select(g => new Point(g.First().AxisX, g.First().AxisY))
            .ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
