using CommunityToolkit.Mvvm.ComponentModel;

namespace GeotechSlopeApp.Models;

public partial class GeometryLineSegment : ObservableObject
{
    public GeometryLineSegment(int lineNumber, string fromPoint, string toPoint, bool isBoundaryEdge = true)
    {
        LineNumber = lineNumber;
        IsBoundaryEdge = isBoundaryEdge;
        FromPoint = fromPoint;
        ToPoint = toPoint;
        UpdateConnectedPoints();
    }

    public int LineNumber { get; }

    /// <summary>False for shared/internal edges between non-consecutive vertices (same vertex pool as boundary).</summary>
    public bool IsBoundaryEdge { get; }
    public string LineNumberLabel => $"L{LineNumber}";

    public string EdgeKindLabel => IsCircle ? "Circle" : (IsBoundaryEdge ? "Boundary" : "Shared");

    [ObservableProperty]
    private string _fromPoint;

    [ObservableProperty]
    private string _toPoint;

    partial void OnFromPointChanged(string value)
    {
        if (IsCircle) return;
        UpdateConnectedPoints();
    }

    partial void OnToPointChanged(string value)
    {
        if (IsCircle) return;
        UpdateConnectedPoints();
    }

    private void UpdateConnectedPoints()
    {
        ConnectedPoints = $"{FromPoint} → {ToPoint}";
        OnPropertyChanged(nameof(ConnectedPoints));
    }

    /// <summary>Used for circle rows so Connected Points shows center and radius instead of from→to.</summary>
    public void SetCircleRowDisplay(double centerAxisX, double centerAxisY, double radiusAxis)
    {
        CircleCenterAxisX = centerAxisX;
        CircleCenterAxisY = centerAxisY;
        CircleRadiusAxis = radiusAxis;
        ArcRadius = radiusAxis;
        ConnectedPoints = $"Center ({centerAxisX:0.###}, {centerAxisY:0.###}), R {radiusAxis:0.###}";
        OnPropertyChanged(nameof(ConnectedPoints));
    }

    public string ConnectedPoints { get; private set; } = string.Empty;

    [ObservableProperty]
    private string? _tag;

    [ObservableProperty]
    private bool _isHighlighted;

    // Material properties (saved per layer when Apply is clicked)
    [ObservableProperty]
    private string _unitWeight = "";

    [ObservableProperty]
    private bool _isSaturatedUnitWeightEnabled;

    [ObservableProperty]
    private string _saturatedUnitWeight = "";

    [ObservableProperty]
    private string _cohesion = "";

    [ObservableProperty]
    private string _frictionAngle = "";

    [ObservableProperty]
    private string _youngsModulus = "";

    [ObservableProperty]
    private string _poissonsRatio = "";

    [ObservableProperty]
    private string _density = "";

    [ObservableProperty]
    private bool _considerDensityForFE;

    [ObservableProperty]
    private bool _planeStrain;

    /// <summary>When true the From/To points are locked and cannot be changed from the UI.</summary>
    [ObservableProperty]
    private bool _isApplied;

    // ---- Arc fields (only meaningful when IsArc = true) ----

    [ObservableProperty]
    private bool _isArc;

    /// <summary>Label of the through-point (middle click) that defines the arc.</summary>
    [ObservableProperty]
    private string _throughPoint = "";

    [ObservableProperty]
    private double _arcRadius;

    /// <summary>True when this row is a closed circle (center + radius), not a chord between vertices.</summary>
    [ObservableProperty]
    private bool _isCircle;

    /// <summary>Matches <see cref="GeometryInternalCircle.Id"/> when <see cref="IsCircle"/>.</summary>
    [ObservableProperty]
    private int _internalCircleId;

    [ObservableProperty]
    private double _circleCenterAxisX;

    [ObservableProperty]
    private double _circleCenterAxisY;

    /// <summary>Axis radius when <see cref="IsCircle"/>; same as arc radius column for display.</summary>
    [ObservableProperty]
    private double _circleRadiusAxis;

    partial void OnIsCircleChanged(bool value) => OnPropertyChanged(nameof(EdgeKindLabel));
}
