using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models;

/// <summary>Drain face for UserDefined transient fixed-pressure rows (bbox edges).</summary>
public enum TransientDrainFace
{
    Top,
    Bottom,
    MinX,
    MaxX
}

/// <summary>
/// One interactive / typed prescribed pore-pressure assignment for transient consolidation
/// (Groundwater → Transient consolidation BCs), mirroring Water Head Definition/Placement UX.
/// </summary>
public sealed class TransientFixedPressureNode : INotifyPropertyChanged
{
    private string _name = "";
    private bool _isEnabled = true;
    private WaterHeadDefinitionMethod _definitionMethod = WaterHeadDefinitionMethod.UserDefined;
    private TransientDrainFace _face = TransientDrainFace.Top;
    private WaterHeadPlacementType _placementType = WaterHeadPlacementType.Point;
    private double _pressure;
    private double _startX;
    private double _startY;
    private double _endX;
    private double _endY;
    private int _nodeId;
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
            OnPropertyChanged(nameof(Summary));
        }
    }

    public TransientDrainFace Face
    {
        get => _face;
        set
        {
            if (_face == value) return;
            _face = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FaceLabel));
            OnPropertyChanged(nameof(Summary));
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
            OnPropertyChanged(nameof(Summary));
        }
    }

    public double Pressure
    {
        get => _pressure;
        set { if (Math.Abs(_pressure - value) < 1e-12) return; _pressure = value; OnPropertyChanged(); }
    }

    public double StartX
    {
        get => _startX;
        set { if (Math.Abs(_startX - value) < 1e-12) return; _startX = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public double StartY
    {
        get => _startY;
        set { if (Math.Abs(_startY - value) < 1e-12) return; _startY = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public double EndX
    {
        get => _endX;
        set { if (Math.Abs(_endX - value) < 1e-12) return; _endX = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public double EndY
    {
        get => _endY;
        set { if (Math.Abs(_endY - value) < 1e-12) return; _endY = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    /// <summary>Compatibility alias used by older overlays / typed Node ID entry.</summary>
    public double AxisX
    {
        get => StartX;
        set => StartX = value;
    }

    public double AxisY
    {
        get => StartY;
        set => StartY = value;
    }

    public int NodeId
    {
        get => _nodeId;
        set { if (_nodeId == value) return; _nodeId = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public bool HasPlacement
    {
        get => _hasPlacement;
        set { if (_hasPlacement == value) return; _hasPlacement = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public bool IsUserDefined => DefinitionMethod == WaterHeadDefinitionMethod.UserDefined;
    public bool IsMouseSelect => DefinitionMethod == WaterHeadDefinitionMethod.MouseSelect;
    public bool IsPointPlacement => PlacementType == WaterHeadPlacementType.Point;
    public bool IsLinePlacement => PlacementType == WaterHeadPlacementType.Line;

    public string FaceLabel => Face switch
    {
        TransientDrainFace.Bottom => "Bottom (min-y)",
        TransientDrainFace.MinX => "Min-x",
        TransientDrainFace.MaxX => "Max-x",
        _ => "Top (max-y)"
    };

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

    public string Summary
    {
        get
        {
            if (IsUserDefined && !HasPlacement)
                return $"User-defined → {FaceLabel}";
            if (!HasPlacement)
                return PlacementType == WaterHeadPlacementType.Line
                    ? "Click canvas start then end"
                    : "Enter a node ID or click canvas";
            if (PlacementType == WaterHeadPlacementType.Point)
                return NodeId > 0
                    ? $"Node {NodeId} @ ({StartX:G4}, {StartY:G4})"
                    : $"Point @ ({StartX:G4}, {StartY:G4})";
            return $"Line ({StartX:G4}, {StartY:G4}) → ({EndX:G4}, {EndY:G4})";
        }
    }

    public TransientFixedPressureNode Clone() => new()
    {
        Name = Name,
        IsEnabled = IsEnabled,
        DefinitionMethod = DefinitionMethod,
        Face = Face,
        PlacementType = PlacementType,
        Pressure = Pressure,
        StartX = StartX,
        StartY = StartY,
        EndX = EndX,
        EndY = EndY,
        NodeId = NodeId,
        HasPlacement = HasPlacement
    };

    public void CopyFrom(TransientFixedPressureNode other)
    {
        Name = other.Name;
        IsEnabled = other.IsEnabled;
        DefinitionMethod = other.DefinitionMethod;
        Face = other.Face;
        PlacementType = other.PlacementType;
        Pressure = other.Pressure;
        StartX = other.StartX;
        StartY = other.StartY;
        EndX = other.EndX;
        EndY = other.EndY;
        NodeId = other.NodeId;
        HasPlacement = other.HasPlacement;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
