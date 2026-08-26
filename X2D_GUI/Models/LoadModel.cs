using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace GeotechSlopeApp.Models;

public enum LoadType
{
    Point,
    Line,
    Triangular
}

public class LoadModel : INotifyPropertyChanged
{
    private LoadType _type = LoadType.Point;
    private LoadDirection _direction = LoadDirection.Axial;
    private double _magnitude;
    private double _endMagnitude;
    private double _startX;
    private double _startY;
    private double _endX;
    private double _endY;
    private int? _appliedToNodeId;
    private int? _stageNumber;
    private bool _syncingPointFields;
    private string _name = "";
    private string _idCode = "";
    private string _applyTo = "";
    private string _loadGroup = "LG-01 (Permanent)";
    private string _coordinateSystem = "Global";
    private LoadDistribution _distribution = LoadDistribution.Uniform;
    private string _loadProjection = "Normal to Surface";
    private double _loadDepth;
    private bool _useProjection;
    private bool _hasRampTimeDependency;
    private int? _applyFromStage;
    private int? _applyToStage;
    private string _notes = "";
    private LoadOrientation _orientation = LoadOrientation.NormalToBoundary;
    private double _angleDeg;
    private bool _isStageLoad;
    private bool _isEnabled = true;

    /// <summary>
    /// Construction stage this load belongs to (1-based StageDefinition.StageNumber).
    /// Null = legacy / pre-stage load created before staged construction was enabled (treated as shared, visible in every stage).
    /// Once non-null, the load is *only* visible / editable while that stage is active in the footer.
    /// </summary>
    public int? StageNumber
    {
        get => _stageNumber;
        set { _stageNumber = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// When false, the load stays in the Load Conditions grid but is hidden on the canvas
    /// and omitted from solver export.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    public LoadType Type
    {
        get => _type;
        set
        {
            if (_type == value)
                return;
            _type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPointLoad));
            OnPropertyChanged(nameof(IsLineLoad));
            OnPropertyChanged(nameof(IsTriangularLoad));
            OnPropertyChanged(nameof(IsDistributedLineLoad));
            OnPropertyChanged(nameof(SupportsLoadDetailPanel));
            OnPropertyChanged(nameof(ShowsEndMagnitude));
            OnPropertyChanged(nameof(MagnitudeUnit));
            OnPropertyChanged(nameof(ValueLabel));
            OnPropertyChanged(nameof(DisplayType));
            OnPropertyChanged(nameof(ApplyTo));
            if (value == LoadType.Triangular && Math.Abs(_endMagnitude) < 1e-12)
                EndMagnitude = _magnitude + 5.0;
            if (value == LoadType.Point)
                SyncPointEndFromStart();
        }
    }

    /// <summary>Direction of load: Axial (along bar, 1D), X or Y (global axis, 2D).</summary>
    public LoadDirection Direction { get => _direction; set { _direction = value; OnPropertyChanged(); } }

    public double Magnitude
    {
        get => _magnitude;
        set
        {
            if (Math.Abs(_magnitude - value) < 1e-12)
                return;
            _magnitude = value;
            OnPropertyChanged();
            if (IsPointLoad && !_syncingPointFields)
                EndMagnitude = value;
        }
    }

    /// <summary>Intensity at the end of a triangular distributed load (start = <see cref="Magnitude"/>).</summary>
    public double EndMagnitude
    {
        get => _endMagnitude;
        set
        {
            if (Math.Abs(_endMagnitude - value) < 1e-12)
                return;
            if (IsPointLoad && !_syncingPointFields)
            {
                _magnitude = value;
                OnPropertyChanged(nameof(Magnitude));
            }
            _endMagnitude = value;
            OnPropertyChanged();
        }
    }

    public double StartX
    {
        get => _startX;
        set
        {
            if (Math.Abs(_startX - value) < 1e-12)
                return;
            _startX = value;
            OnPropertyChanged();
            if (IsPointLoad && !_syncingPointFields)
                EndX = value;
        }
    }

    public double StartY
    {
        get => _startY;
        set
        {
            if (Math.Abs(_startY - value) < 1e-12)
                return;
            _startY = value;
            OnPropertyChanged();
            if (IsPointLoad && !_syncingPointFields)
                EndY = value;
        }
    }

    public double EndX
    {
        get => _endX;
        set
        {
            if (Math.Abs(_endX - value) < 1e-12)
                return;
            if (IsPointLoad && !_syncingPointFields)
            {
                _startX = value;
                OnPropertyChanged(nameof(StartX));
            }
            _endX = value;
            OnPropertyChanged();
        }
    }

    public double EndY
    {
        get => _endY;
        set
        {
            if (Math.Abs(_endY - value) < 1e-12)
                return;
            if (IsPointLoad && !_syncingPointFields)
            {
                _startY = value;
                OnPropertyChanged(nameof(StartY));
            }
            _endY = value;
            OnPropertyChanged();
        }
    }

    /// <summary>When set, point load is snapped to this mesh node (1-based), matching discretization order.</summary>
    public int? AppliedToNodeId
    {
        get => _appliedToNodeId;
        set { _appliedToNodeId = value; OnPropertyChanged(); }
    }

    public bool IsPointLoad => Type == LoadType.Point;
    public bool IsLineLoad => Type == LoadType.Line;
    public bool IsTriangularLoad => Type == LoadType.Triangular;
    public bool IsDistributedLineLoad => Type == LoadType.Line || Type == LoadType.Triangular;
    public bool SupportsLoadDetailPanel => IsPointLoad || IsDistributedLineLoad;
    public bool ShowsEndMagnitude =>
        IsDistributedLineLoad && Distribution != LoadDistribution.Uniform;
    public string MagnitudeUnit => Type switch
    {
        LoadType.Point => "kN",
        LoadType.Line => "kN/m",
        _ => "kN/m²"
    };
    public string ValueLabel => $"Value ({MagnitudeUnit})";

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    /// <summary>Short identifier shown in Load Properties (e.g. SL-01).</summary>
    public string IdCode
    {
        get => _idCode;
        set { _idCode = value ?? ""; OnPropertyChanged(); }
    }

    public string ApplyTo
    {
        get => string.IsNullOrWhiteSpace(_applyTo) ? DefaultApplyTo : _applyTo;
        set { _applyTo = value ?? ""; OnPropertyChanged(); }
    }

    public string LoadGroup
    {
        get => _loadGroup;
        set { _loadGroup = string.IsNullOrWhiteSpace(value) ? "LG-01 (Permanent)" : value; OnPropertyChanged(); }
    }

    public string CoordinateSystem
    {
        get => _coordinateSystem;
        set { _coordinateSystem = string.IsNullOrWhiteSpace(value) ? "Global" : value; OnPropertyChanged(); }
    }

    public LoadDistribution Distribution
    {
        get => _distribution;
        set
        {
            if (_distribution == value) return;
            _distribution = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsEndMagnitude));
            OnPropertyChanged(nameof(DisplayDistribution));
            if (value == LoadDistribution.Uniform && IsDistributedLineLoad)
                EndMagnitude = Magnitude;
        }
    }

    public string LoadProjection
    {
        get => _loadProjection;
        set { _loadProjection = string.IsNullOrWhiteSpace(value) ? "Normal to Surface" : value; OnPropertyChanged(); }
    }

    public double LoadDepth
    {
        get => _loadDepth;
        set { if (Math.Abs(_loadDepth - value) < 1e-12) return; _loadDepth = value; OnPropertyChanged(); }
    }

    public bool UseProjection
    {
        get => _useProjection;
        set { if (_useProjection == value) return; _useProjection = value; OnPropertyChanged(); }
    }

    public bool HasRampTimeDependency
    {
        get => _hasRampTimeDependency;
        set { if (_hasRampTimeDependency == value) return; _hasRampTimeDependency = value; OnPropertyChanged(); }
    }

    public int? ApplyFromStage
    {
        get => _applyFromStage ?? StageNumber;
        set
        {
            if (_applyFromStage == value)
                return;
            _applyFromStage = value;
            if (value is int n)
                StageNumber = n;
            OnPropertyChanged();
        }
    }

    /// <summary>Null means all subsequent stages.</summary>
    public int? ApplyToStage
    {
        get => _applyToStage;
        set { _applyToStage = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApplyToStageOrAll)); }
    }

    public int ApplyToStageOrAll
    {
        get => _applyToStage ?? 0;
        set
        {
            int? next = value <= 0 ? null : value;
            if (_applyToStage == next)
                return;
            ApplyToStage = next;
            OnPropertyChanged();
        }
    }

    public string Notes
    {
        get => _notes;
        set { _notes = value ?? ""; OnPropertyChanged(); }
    }

    public string DisplayType
    {
        get => Type switch
        {
            LoadType.Line => "Line Load",
            LoadType.Triangular => "Surface Load",
            _ => "Point Load"
        };
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var next = value switch
            {
                "Line Load" => LoadType.Line,
                "Surface Load" => LoadType.Triangular,
                "Point Load" => LoadType.Point,
                _ => Type
            };
            if (Type == next)
                return;
            Type = next;
            OnPropertyChanged();
        }
    }

    public string DisplayDirection
    {
        get => Orientation switch
        {
            LoadOrientation.VerticalDown => "Vertical",
            LoadOrientation.HorizontalRight => "Horizontal",
            LoadOrientation.AngleFromHorizontal => "Angle from Horizontal",
            LoadOrientation.AngleToBoundary => "Angle to Boundary",
            _ => "Normal to Surface"
        };
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var next = value switch
            {
                "Vertical" => LoadOrientation.VerticalDown,
                "Horizontal" => LoadOrientation.HorizontalRight,
                "Angle from Horizontal" => LoadOrientation.AngleFromHorizontal,
                "Angle to Boundary" => LoadOrientation.AngleToBoundary,
                "Normal to Surface" => LoadOrientation.NormalToBoundary,
                _ => Orientation
            };
            if (Orientation == next)
                return;
            Orientation = next;
            OnPropertyChanged();
        }
    }

    public string DisplayDistribution
    {
        get => Distribution.ToString();
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var next = value switch
            {
                "Linear" => LoadDistribution.Linear,
                "Triangular" => LoadDistribution.Triangular,
                "Uniform" => LoadDistribution.Uniform,
                _ => Distribution
            };
            if (Distribution == next)
                return;
            Distribution = next;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => IsEnabled ? "Active" : "Inactive";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            IsEnabled = !string.Equals(value, "Inactive", StringComparison.OrdinalIgnoreCase);
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    public string StatusColor => IsEnabled ? "#FF22C55E" : "#FF94A3B8";

    private string DefaultApplyTo => Type switch
    {
        LoadType.Point when AppliedToNodeId is int node => $"Node {node}",
        LoadType.Point => "Selected Nodes (1)",
        LoadType.Line => "Selected Edges (1)",
        _ => "Selected Surfaces (1)"
    };

    public LoadOrientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation == value) return;
            _orientation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNormalToBoundaryOrientation));
            OnPropertyChanged(nameof(IsVerticalDownOrientation));
            OnPropertyChanged(nameof(IsHorizontalRightOrientation));
            OnPropertyChanged(nameof(IsAngleFromHorizontalOrientation));
            OnPropertyChanged(nameof(IsAngleToBoundaryOrientation));
            OnPropertyChanged(nameof(IsAngleOrientation));
            OnPropertyChanged(nameof(OrientationSummary));
            OnPropertyChanged(nameof(DisplayDirection));
        }
    }

    public double AngleDeg
    {
        get => _angleDeg;
        set
        {
            if (Math.Abs(_angleDeg - value) < 1e-12) return;
            _angleDeg = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OrientationSummary));
        }
    }

    public bool IsStageLoad
    {
        get => _isStageLoad;
        set
        {
            if (_isStageLoad == value) return;
            _isStageLoad = value;
            if (value && ApplyFromStage is null && StageNumber is int n)
                ApplyFromStage = n;
            OnPropertyChanged();
        }
    }

    public bool IsNormalToBoundaryOrientation
    {
        get => Orientation == LoadOrientation.NormalToBoundary;
        set { if (value) Orientation = LoadOrientation.NormalToBoundary; }
    }

    public bool IsVerticalDownOrientation
    {
        get => Orientation == LoadOrientation.VerticalDown;
        set { if (value) Orientation = LoadOrientation.VerticalDown; }
    }

    public bool IsHorizontalRightOrientation
    {
        get => Orientation == LoadOrientation.HorizontalRight;
        set { if (value) Orientation = LoadOrientation.HorizontalRight; }
    }

    public bool IsAngleFromHorizontalOrientation
    {
        get => Orientation == LoadOrientation.AngleFromHorizontal;
        set { if (value) Orientation = LoadOrientation.AngleFromHorizontal; }
    }

    public bool IsAngleToBoundaryOrientation
    {
        get => Orientation == LoadOrientation.AngleToBoundary;
        set { if (value) Orientation = LoadOrientation.AngleToBoundary; }
    }

    public bool IsAngleOrientation =>
        Orientation is LoadOrientation.AngleFromHorizontal or LoadOrientation.AngleToBoundary;

    public string OrientationSummary => Orientation switch
    {
        LoadOrientation.NormalToBoundary => "Normal",
        LoadOrientation.VerticalDown => "Vertical ↓",
        LoadOrientation.HorizontalRight => "Horizontal →",
        LoadOrientation.AngleFromHorizontal => $"{AngleDeg:F0}° (H)",
        LoadOrientation.AngleToBoundary => $"{AngleDeg:F0}° (B)",
        _ => "Normal"
    };

    public PointCollection LinePoints { get; } = new();

    /// <summary>Copies start magnitude/coordinates to end fields for point loads.</summary>
    public void SyncPointEndFromStart()
    {
        if (!IsPointLoad)
            return;
        _syncingPointFields = true;
        try
        {
            _endMagnitude = _magnitude;
            _endX = _startX;
            _endY = _startY;
            OnPropertyChanged(nameof(EndMagnitude));
            OnPropertyChanged(nameof(EndX));
            OnPropertyChanged(nameof(EndY));
        }
        finally
        {
            _syncingPointFields = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

