using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.ViewModels.Dialogs;

public class BooleanOperationDialogViewModel : INotifyPropertyChanged
{
    private readonly GeometryDefinitionViewModel _geometry;
    private SurfaceRegionPickItem? _selectedRegionA;
    private SurfaceRegionPickItem? _selectedRegionB;
    private BooleanOperationType _selectedOperationType = BooleanOperationType.Subtract;
    private string _resultRegionName = "";
    private bool _preserveAttributes = true;
    private string _validationMessage = "";
    private bool _isMultiRegionMode;

    public BooleanOperationDialogViewModel(GeometryDefinitionViewModel geometry)
    {
        _geometry = geometry;
        AvailableRegions = new List<SurfaceRegionPickItem>();
        foreach (var s in geometry.GeometrySurfaces.OrderBy(x => x.SurfaceNumber))
        {
            if (!geometry.TryHasClosedSurfacePolygon(s))
                continue;
            var name = string.IsNullOrWhiteSpace(s.Tag) ? $"S{s.SurfaceNumber}" : s.Tag!.Trim();
            AvailableRegions.Add(new SurfaceRegionPickItem { RegionName = name, Surface = s });
        }

        SelectedRegionA = AvailableRegions.ElementAtOrDefault(0);
        SelectedRegionB = AvailableRegions.ElementAtOrDefault(1);

        MultiRegionItems = new ObservableCollection<SurfaceRegionCheckItem>();
        var n = AvailableRegions.Count;
        for (var i = 0; i < n; i++)
        {
            var pick = AvailableRegions[i];
            var check = new SurfaceRegionCheckItem(pick, isSelected: i < 2 && n >= 2);
            check.PropertyChanged += OnMultiRegionItemPropertyChanged;
            MultiRegionItems.Add(check);
        }
    }

    private void OnMultiRegionItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SurfaceRegionCheckItem.IsSelected))
            OnPropertyChanged(nameof(CanExecute));
    }

    public IReadOnlyList<BooleanOperationType> OperationTypeChoices { get; } =
        new[] { BooleanOperationType.Union, BooleanOperationType.Subtract, BooleanOperationType.Intersect };

    public List<SurfaceRegionPickItem> AvailableRegions { get; }

    /// <summary>Checkbox list for multi-region mode (same surfaces as <see cref="AvailableRegions"/>).</summary>
    public ObservableCollection<SurfaceRegionCheckItem> MultiRegionItems { get; }

    /// <summary>When true, use checkboxes and apply the selected operation to all checked surfaces (single preview).</summary>
    public bool IsMultiRegionMode
    {
        get => _isMultiRegionMode;
        set
        {
            if (_isMultiRegionMode == value) return;
            _isMultiRegionMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanExecute));
            OnPropertyChanged(nameof(RegionADescription));
            OnPropertyChanged(nameof(RegionBDescription));
            OnPropertyChanged(nameof(MultiRegionHelpText));
        }
    }

    public SurfaceRegionPickItem? SelectedRegionA
    {
        get => _selectedRegionA;
        set { _selectedRegionA = value; OnPropertyChanged(); }
    }

    public SurfaceRegionPickItem? SelectedRegionB
    {
        get => _selectedRegionB;
        set { _selectedRegionB = value; OnPropertyChanged(); }
    }

    public BooleanOperationType SelectedOperationType
    {
        get => _selectedOperationType;
        set
        {
            if (_selectedOperationType == value) return;
            _selectedOperationType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RegionADescription));
            OnPropertyChanged(nameof(RegionBDescription));
            OnPropertyChanged(nameof(MultiRegionHelpText));
        }
    }

    public string ResultRegionName
    {
        get => _resultRegionName;
        set { _resultRegionName = value ?? ""; OnPropertyChanged(); }
    }

    public bool PreserveAttributes
    {
        get => _preserveAttributes;
        set { _preserveAttributes = value; OnPropertyChanged(); }
    }

    public string RegionADescription =>
        IsMultiRegionMode
            ? ""
            : SelectedOperationType == BooleanOperationType.Subtract
                ? "Primary region (A). For A − B, this is the region to subtract from."
                : "First region for the boolean operation.";

    public string RegionBDescription =>
        IsMultiRegionMode
            ? ""
            : SelectedOperationType == BooleanOperationType.Subtract
                ? "Region to subtract (B). The overlap is removed from A."
                : "Second region for the boolean operation.";

    public string MultiRegionHelpText =>
        SelectedOperationType switch
        {
            BooleanOperationType.Union =>
                "Check two or more surfaces. They are combined with union in one preview.",
            BooleanOperationType.Subtract =>
                "Check two or more surfaces. Subtraction follows list order (surface number): ((S1 − S2) − S3) − …",
            BooleanOperationType.Intersect =>
                "Check two or more surfaces. Result is the intersection (overlap) of all checked regions.",
            _ => ""
        };

    public bool CanExecute
    {
        get
        {
            if (IsMultiRegionMode)
                return MultiRegionItems.Count(x => x.IsSelected) >= 2;
            return AvailableRegions.Count >= 2;
        }
    }

    /// <summary>Checked surfaces in list order (for multi-region boolean).</summary>
    public IReadOnlyList<GeometrySurface> GetSelectedSurfacesForMultiRegion() =>
        MultiRegionItems.Where(x => x.IsSelected).Select(x => x.Surface).ToList();

    /// <summary>Call before closing dialog with OK. Sets <see cref="ValidationMessage"/> on failure.</summary>
    public bool Validate()
    {
        ValidationMessage = "";
        if (IsMultiRegionMode)
        {
            if (AvailableRegions.Count < 2)
            {
                ValidationMessage = "Need at least two valid closed surfaces (complete Surface grid rows).";
                return false;
            }

            var selected = GetSelectedSurfacesForMultiRegion().ToList();
            if (selected.Count < 2)
            {
                ValidationMessage = "Select at least two surfaces using the checkboxes.";
                return false;
            }

            if (selected.Select(s => s.SurfaceNumber).Distinct().Count() != selected.Count)
            {
                ValidationMessage = "Each surface can only appear once.";
                return false;
            }

            foreach (var s in selected)
            {
                if (!_geometry.TryHasClosedSurfacePolygon(s))
                {
                    ValidationMessage = "All selected surfaces must form a closed boundary.";
                    return false;
                }
            }

            return true;
        }

        if (AvailableRegions.Count < 2)
        {
            ValidationMessage = "Need at least two valid closed surfaces (complete Surface grid rows).";
            return false;
        }

        if (SelectedRegionA == null || SelectedRegionB == null)
        {
            ValidationMessage = "Select both Region A and Region B.";
            return false;
        }

        if (ReferenceEquals(SelectedRegionA.Surface, SelectedRegionB.Surface))
        {
            ValidationMessage = "Region A and Region B must be different surfaces.";
            return false;
        }

        if (!_geometry.TryHasClosedSurfacePolygon(SelectedRegionA.Surface) ||
            !_geometry.TryHasClosedSurfacePolygon(SelectedRegionB.Surface))
        {
            ValidationMessage = "Both surfaces must form a closed polygon (lines/arcs).";
            return false;
        }

        return true;
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set { _validationMessage = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
