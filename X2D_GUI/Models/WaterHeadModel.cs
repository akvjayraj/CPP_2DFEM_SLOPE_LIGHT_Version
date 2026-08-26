using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

public enum WaterHeadRole
{
    Upstream,
    Downstream,
    Custom
}

public enum WaterHeadDefinitionMethod
{
    UserDefined,
    MouseSelect
}

public enum WaterHeadPlacementType
{
    Point,
    Line
}

/// <summary>
/// One interactive / typed boundary total-head assignment for seepage (Groundwater → Add Water Head).
/// </summary>
public class WaterHeadModel : INotifyPropertyChanged
{
    private string _name = "";
    private bool _isEnabled = true;
    private WaterHeadRole _role = WaterHeadRole.Upstream;
    private WaterHeadDefinitionMethod _definitionMethod = WaterHeadDefinitionMethod.UserDefined;
    private double _head;
    private WaterHeadPlacementType _placementType = WaterHeadPlacementType.Point;
    private double _startX;
    private double _startY;
    private double _endX;
    private double _endY;
    private int? _appliedToNodeId;
    private bool _hasPlacement;

    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value ?? ""; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (_isEnabled == value) return; _isEnabled = value; OnPropertyChanged(); }
    }

    public WaterHeadRole Role
    {
        get => _role;
        set
        {
            if (_role == value) return;
            _role = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RoleLabel));
            OnPropertyChanged(nameof(HeadPrompt));
            OnPropertyChanged(nameof(IsUpstreamRole));
            OnPropertyChanged(nameof(IsDownstreamRole));
            OnPropertyChanged(nameof(IsCustomRole));
        }
    }

    public WaterHeadDefinitionMethod DefinitionMethod
    {
        get => _definitionMethod;
        set
        {
            if (_definitionMethod == value) return;
            _definitionMethod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUserDefined));
            OnPropertyChanged(nameof(IsMouseSelect));
            OnPropertyChanged(nameof(DefinitionLabel));
            OnPropertyChanged(nameof(PlacementSummary));
        }
    }

    public double Head
    {
        get => _head;
        set
        {
            if (Math.Abs(_head - value) < 1e-12) return;
            _head = value;
            OnPropertyChanged();
        }
    }

    public WaterHeadPlacementType PlacementType
    {
        get => _placementType;
        set
        {
            if (_placementType == value) return;
            _placementType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPointPlacement));
            OnPropertyChanged(nameof(IsLinePlacement));
            OnPropertyChanged(nameof(PlacementTypeLabel));
            OnPropertyChanged(nameof(PlacementSummary));
        }
    }

    /// <summary>Placement start in axis coordinates.</summary>
    public double StartX
    {
        get => _startX;
        set { if (Math.Abs(_startX - value) < 1e-12) return; _startX = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlacementSummary)); }
    }

    public double StartY
    {
        get => _startY;
        set { if (Math.Abs(_startY - value) < 1e-12) return; _startY = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlacementSummary)); }
    }

    public double EndX
    {
        get => _endX;
        set { if (Math.Abs(_endX - value) < 1e-12) return; _endX = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlacementSummary)); }
    }

    public double EndY
    {
        get => _endY;
        set { if (Math.Abs(_endY - value) < 1e-12) return; _endY = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlacementSummary)); }
    }

    public int? AppliedToNodeId
    {
        get => _appliedToNodeId;
        set { if (_appliedToNodeId == value) return; _appliedToNodeId = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlacementSummary)); }
    }

    public bool HasPlacement
    {
        get => _hasPlacement;
        set { if (_hasPlacement == value) return; _hasPlacement = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlacementSummary)); }
    }

    public bool IsUserDefined => DefinitionMethod == WaterHeadDefinitionMethod.UserDefined;
    public bool IsMouseSelect => DefinitionMethod == WaterHeadDefinitionMethod.MouseSelect;
    public bool IsPointPlacement => PlacementType == WaterHeadPlacementType.Point;
    public bool IsLinePlacement => PlacementType == WaterHeadPlacementType.Line;
    public bool IsUpstreamRole => Role == WaterHeadRole.Upstream;
    public bool IsDownstreamRole => Role == WaterHeadRole.Downstream;
    public bool IsCustomRole => Role == WaterHeadRole.Custom;

    public string RoleLabel => Role switch
    {
        WaterHeadRole.Upstream => "Upstream",
        WaterHeadRole.Downstream => "Downstream",
        _ => "Custom"
    };

    public string HeadPrompt => Role switch
    {
        WaterHeadRole.Upstream => "Upstream head H₁",
        WaterHeadRole.Downstream => "Downstream head H₂",
        _ => "Head H"
    };

    public string PlacementSummary
    {
        get
        {
            if (DefinitionMethod == WaterHeadDefinitionMethod.UserDefined && !HasPlacement)
                return Role switch
                {
                    WaterHeadRole.Upstream => "User-defined → min-x face",
                    WaterHeadRole.Downstream => "User-defined → max-x face",
                    _ => "User-defined (place with mouse or set Role to Upstream/Downstream)"
                };
            if (!HasPlacement)
                return "Click canvas to place";
            if (PlacementType == WaterHeadPlacementType.Point)
                return AppliedToNodeId is int id
                    ? $"Node {id} @ ({StartX:G4}, {StartY:G4})"
                    : $"Point @ ({StartX:G4}, {StartY:G4})";
            return $"Line ({StartX:G4}, {StartY:G4}) → ({EndX:G4}, {EndY:G4})";
        }
    }

    public string DefinitionLabel => DefinitionMethod switch
    {
        WaterHeadDefinitionMethod.MouseSelect => "MouseSelect",
        _ => "UserDefined"
    };

    public string PlacementTypeLabel => PlacementType switch
    {
        WaterHeadPlacementType.Line => "Line",
        _ => "Point"
    };

    public WaterHeadModel Clone() => new()
    {
        Name = Name,
        IsEnabled = IsEnabled,
        Role = Role,
        DefinitionMethod = DefinitionMethod,
        Head = Head,
        PlacementType = PlacementType,
        StartX = StartX,
        StartY = StartY,
        EndX = EndX,
        EndY = EndY,
        AppliedToNodeId = AppliedToNodeId,
        HasPlacement = HasPlacement
    };

    public void CopyFrom(WaterHeadModel other)
    {
        Name = other.Name;
        IsEnabled = other.IsEnabled;
        Role = other.Role;
        DefinitionMethod = other.DefinitionMethod;
        Head = other.Head;
        PlacementType = other.PlacementType;
        StartX = other.StartX;
        StartY = other.StartY;
        EndX = other.EndX;
        EndY = other.EndY;
        AppliedToNodeId = other.AppliedToNodeId;
        HasPlacement = other.HasPlacement;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
