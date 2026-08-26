using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>One row in the Boundary Conditions DataGrid (node ID, boundary name, BC type, DOF, value, description).</summary>
public class BoundaryConditionRow : INotifyPropertyChanged
{
    private int _nodeId;
    private string _boundaryName = "";
    private string _bcType = "Fixed";
    private string _analysisCategory = BoundaryConditionAnalysisCategories.Structural;
    private int _degreeOfFreedom;
    private string _value = "0";
    private string _description = "";

    public int NodeId
    {
        get => _nodeId;
        set { _nodeId = value; OnPropertyChanged(); }
    }

    public string BoundaryName
    {
        get => _boundaryName;
        set { _boundaryName = value ?? ""; OnPropertyChanged(); }
    }

    public string BCType
    {
        get => _bcType;
        set
        {
            _bcType = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(SymbolKind));
            OnPropertyChanged(nameof(UsesDegreeOfFreedom));
            OnPropertyChanged(nameof(UsesEditableDof));
            OnPropertyChanged(nameof(DofDisplay));
            // Keep Analysis heading in sync when the type is edited in the grid.
            AnalysisCategory = BoundaryConditionAnalysisCategories.Resolve(_bcType);
            var implied = BcSymbolCatalog.ImpliedDegreeOfFreedom(_bcType);
            if (implied.HasValue)
                DegreeOfFreedom = implied.Value;
        }
    }

    /// <summary>
    /// Project Settings → Analysis heading family for this row
    /// (<see cref="BoundaryConditionAnalysisCategories.Structural"/> or
    /// <see cref="BoundaryConditionAnalysisCategories.Seepage"/>).
    /// </summary>
    public string AnalysisCategory
    {
        get => _analysisCategory;
        set
        {
            var next = string.IsNullOrWhiteSpace(value)
                ? BoundaryConditionAnalysisCategories.Resolve(_bcType)
                : value;
            if (_analysisCategory == next) return;
            _analysisCategory = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSeepageAnalysis));
        }
    }

    public bool IsSeepageAnalysis =>
        string.Equals(_analysisCategory, BoundaryConditionAnalysisCategories.Seepage, System.StringComparison.Ordinal);

    public BcSymbolKind SymbolKind => BcSymbolCatalog.ResolveKind(_bcType);

    public bool UsesDegreeOfFreedom => BcSymbolCatalog.UsesDegreeOfFreedom(_bcType);

    public bool UsesEditableDof => BcSymbolCatalog.UsesEditableDof(_bcType);

    public string DofDisplay => BcSymbolCatalog.FormatDofDisplay(_bcType, _degreeOfFreedom);

    public int DegreeOfFreedom
    {
        get => _degreeOfFreedom;
        set
        {
            if (_degreeOfFreedom == value) return;
            _degreeOfFreedom = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DofDisplay));
        }
    }

    public string Value
    {
        get => _value;
        set { _value = value ?? ""; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value ?? ""; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
