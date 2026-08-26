using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Services.Geometry;

namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// Canvas drawing aids: snapping and the measure tool. Vertex-drag / soil-block lock
/// editing stays on the FEM geometry host.
/// </summary>
public partial class GeometryDefinitionViewModel
{
    private Point? _measureStart;
    private Point? _measureEnd;
    private bool _isMeasureMode;
    private string _snapStatus = "";
    private Point? _snapIndicatorCanvas;

    /// <summary>Drawing aids: grid snap, object snaps and the ortho constraint.</summary>
    public GeometrySnapSettings SnapSettings { get; } = new();

    /// <summary>Two-click distance measurement, which places nothing in the model.</summary>
    public bool IsMeasureMode
    {
        get => _isMeasureMode;
        set
        {
            if (_isMeasureMode == value) return;
            _isMeasureMode = value;
            if (!value) ClearMeasurement();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MeasureButtonText));
        }
    }

    public string MeasureButtonText => IsMeasureMode ? "Measuring…" : "Measure";

    private ICommand? _toggleMeasureCommand;

    /// <summary>Turns the measure tool on and off from the menu or the geometry toolbar.</summary>
    public ICommand ToggleMeasureModeCommand =>
        _toggleMeasureCommand ??= new RelayCommand(() => IsMeasureMode = !IsMeasureMode);

    /// <summary>
    /// Clears digitize / measure tools that live on the shared geometry VM so read-only
    /// canvases (Postprocessor, Surface) are not blocked by leftovers from Geometry.
    /// </summary>
    public void ClearLeftoverCanvasToolModes()
    {
        CancelDrawLineMode();
        CancelDrawArcMode();
        CancelDrawCircleMode();
        CancelGeologicalDraw();
        IsMeasureMode = false;
        IsGeometryVerticesEditMode = false;
        IsNewPointMode = false;
        IsExcavationMode = false;
        IsMaterialAssignMode = false;
        CancelBulkPointDeleteMode();
    }

    /// <summary>What the cursor last snapped to, for the status bar.</summary>
    public string SnapStatus
    {
        get => _snapStatus;
        private set { _snapStatus = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Canvas position of the snap marker, or null when the cursor is not snapped.</summary>
    public Point? SnapIndicatorCanvas
    {
        get => _snapIndicatorCanvas;
        private set
        {
            _snapIndicatorCanvas = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SnapIndicatorX));
            OnPropertyChanged(nameof(SnapIndicatorY));
            OnPropertyChanged(nameof(HasSnapIndicator));
        }
    }

    public bool HasSnapIndicator => _snapIndicatorCanvas.HasValue;
    public double SnapIndicatorX => (_snapIndicatorCanvas?.X ?? 0) - SnapMarkerRadius;
    public double SnapIndicatorY => (_snapIndicatorCanvas?.Y ?? 0) - SnapMarkerRadius;

    private const double SnapMarkerRadius = 11.0;

    /// <summary>Measurement leader line and its label, in canvas coordinates.</summary>
    public ObservableCollection<MeasureOverlay> MeasureOverlays { get; } = new();

    public sealed class MeasureOverlay
    {
        public double X1 { get; init; }
        public double Y1 { get; init; }
        public double X2 { get; init; }
        public double Y2 { get; init; }
        public double LabelX { get; init; }
        public double LabelY { get; init; }
        public string Label { get; init; } = "";
    }

    /// <summary>
    /// Applies the snap rules to a canvas point and returns the result in canvas coordinates, so
    /// callers that already work in canvas space do not have to convert twice.
    /// </summary>
    public Point SnapCanvasPoint(Point canvasPoint, Point? orthoAnchorCanvas = null)
    {
        var result = ResolveSnap(canvasPoint, orthoAnchorCanvas);
        return result.CanvasPoint;
    }

    /// <summary>Snap result plus the marker and status text the canvas shows while hovering.</summary>
    private (Point CanvasPoint, Point AxisPoint, GeometrySnapKind Kind) ResolveSnap(
        Point canvasPoint,
        Point? orthoAnchorCanvas)
    {
        if (double.IsNaN(canvasPoint.X) || double.IsNaN(canvasPoint.Y))
            return (canvasPoint, ConvertCanvasToAxis(canvasPoint), GeometrySnapKind.None);

        var axisPoint = ConvertCanvasToAxis(canvasPoint);

        // Ortho first: it decides the direction, and the snap then refines the position along it.
        if (SnapSettings is { SnapEnabled: true, OrthoEnabled: true } && orthoAnchorCanvas.HasValue)
        {
            var anchorAxis = ConvertCanvasToAxis(orthoAnchorCanvas.Value);
            axisPoint = GeometrySnapEngine.ApplyOrtho(anchorAxis, axisPoint, SnapSettings.OrthoAngleStepDegrees);
        }

        var settings = EffectiveSnapSettingsForDigitize();
        var snap = GeometrySnapEngine.Snap(
            axisPoint,
            SnapVertexCandidates(),
            SnapSegmentCandidates(),
            settings,
            SnapToleranceAxis());

        return (ConvertAxisToCanvas(snap.Point), snap.Point, snap.Kind);
    }

    /// <summary>
    /// While a slope-image underlay is present, free-click tracing must not latch onto already-drawn
    /// edges/midpoints/intersections — those pulls land on the black polyline instead of the image ink.
    /// Vertex (and user grid) snap remain so endpoints can still be closed cleanly.
    /// </summary>
    private GeometrySnapSettings EffectiveSnapSettingsForDigitize()
    {
        if (!HasSlopeImageUnderlay)
            return SnapSettings;

        var settings = SnapSettings.Clone();
        settings.EdgeSnap = false;
        settings.MidpointSnap = false;
        settings.IntersectionSnap = false;
        return settings;
    }

    /// <summary>Snap tolerance converted from screen pixels to model units at the current zoom.</summary>
    private double SnapToleranceAxis()
    {
        var pixels = SnapSettings.SnapTolerancePixels;
        var zoom = _currentZoomFactor > 1e-6 ? _currentZoomFactor : 1.0;
        var axisPerCanvasX = CanvasWidth > 1 ? (AxisXMax - AxisXMin) / CanvasWidth : 1.0;
        var axisPerCanvasY = CanvasHeight > 1 ? (AxisYMax - AxisYMin) / CanvasHeight : 1.0;
        return pixels / zoom * Math.Max(axisPerCanvasX, axisPerCanvasY);
    }

    private List<Point> SnapVertexCandidates()
    {
        var points = GeometryVertexRows.Select(v => new Point(v.AxisX, v.AxisY)).ToList();
        points.AddRange(CollectBoreholeContactSnapPoints());
        return points;
    }

    /// <summary>
    /// Boundary edges plus the internal edges, because a snap that ignored layer boundaries would
    /// miss exactly the intersections a user wants to pick up.
    /// </summary>
    private List<(Point A, Point B)> SnapSegmentCandidates()
    {
        var vertices = SnapVertexCandidates();
        var segments = GeometrySnapEngine.ClosedRingSegments(vertices);

        var byStableId = GeometryVertexRows.ToDictionary(v => v.StablePointId, v => new Point(v.AxisX, v.AxisY));
        foreach (var edge in GeometryInternalEdges)
        {
            if (edge.IsArc) continue;
            if (byStableId.TryGetValue(edge.StableMin, out var a) && byStableId.TryGetValue(edge.StableMax, out var b))
                segments.Add((a, b));
        }

        return segments;
    }

    /// <summary>
    /// Updates the snap marker and status while the cursor moves. Returns the snapped canvas point
    /// so the caller can use the same position it is showing the user.
    /// </summary>
    public Point UpdateSnapPreview(Point canvasPoint, Point? orthoAnchorCanvas = null)
    {
        if (double.IsNaN(canvasPoint.X) || double.IsNaN(canvasPoint.Y))
        {
            ClearSnapPreview();
            return canvasPoint;
        }

        var (snappedCanvas, _, kind) = ResolveSnap(canvasPoint, orthoAnchorCanvas);
        if (kind == GeometrySnapKind.None)
        {
            ClearSnapPreview();
            return snappedCanvas;
        }

        SnapIndicatorCanvas = snappedCanvas;
        SnapStatus = $"Snapped to {new GeometrySnapResult(default, kind).Label}";
        return snappedCanvas;
    }

    public void ClearSnapPreview()
    {
        SnapIndicatorCanvas = null;
        SnapStatus = "";
    }

    public void StartMeasurement(Point canvasPoint)
    {
        _measureStart = SnapCanvasPoint(canvasPoint);
        _measureEnd = _measureStart;
        RefreshMeasureOverlay();
    }

    public void UpdateMeasurement(Point canvasPoint)
    {
        if (!_measureStart.HasValue) return;
        _measureEnd = SnapCanvasPoint(canvasPoint, _measureStart);
        RefreshMeasureOverlay();
    }

    /// <summary>True when the measurement is complete, so the caller knows to leave measure mode.</summary>
    public bool HasMeasurementStart => _measureStart.HasValue;

    public void FinishMeasurement(Point canvasPoint)
    {
        UpdateMeasurement(canvasPoint);
        _measureStart = null;
    }

    public void ClearMeasurement()
    {
        _measureStart = null;
        _measureEnd = null;
        MeasureOverlays.Clear();
        MeasurementSummary = "";
    }

    private string _measurementSummary = "";

    /// <summary>Length and bearing of the current measurement, for the status bar.</summary>
    public string MeasurementSummary
    {
        get => _measurementSummary;
        private set { _measurementSummary = value ?? ""; OnPropertyChanged(); }
    }

    private void RefreshMeasureOverlay()
    {
        MeasureOverlays.Clear();
        if (_measureStart is not { } start || _measureEnd is not { } end)
        {
            MeasurementSummary = "";
            return;
        }

        var a = ConvertCanvasToAxis(start);
        var b = ConvertCanvasToAxis(end);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        MeasureOverlays.Add(new MeasureOverlay
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            LabelX = (start.X + end.X) / 2,
            LabelY = (start.Y + end.Y) / 2 - 16,
            Label = $"{length:F3} m",
        });

        MeasurementSummary =
            $"Length {length:F3} m   ΔX {dx:F3} m   ΔY {dy:F3} m   Bearing {angle:F1}°";
    }
}
