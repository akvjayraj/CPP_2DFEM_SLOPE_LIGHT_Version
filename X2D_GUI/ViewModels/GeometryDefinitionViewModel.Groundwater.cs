using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using System.ComponentModel;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

public partial class GeometryDefinitionViewModel
{
    private bool _isGroundwaterPolylineDrawActive;
    private GroundwaterViewModel? _groundwaterVm;

    public ObservableCollection<GroundwaterSurfPoint> GroundwaterSurfPoints { get; private set; } = null!;
    public ObservableCollection<GroundwaterZoneOverlay> GroundwaterZoneOverlays { get; private set; } = null!;
    public ObservableCollection<PondedWaterOverlay> PondedWaterOverlays { get; private set; } = null!;
    public ObservableCollection<PondedWaterIsobarSegment> PondedWaterIsobars { get; private set; } = null!;
    public ObservableCollection<PondedWaterCallout> PondedWaterCallouts { get; private set; } = null!;
    public ObservableCollection<LineLoadOverlayDisplay> PondedFacePressureOverlays { get; private set; } = null!;
    /// <summary>Mouse-select Water Head segments (Add Water Head), shown on Groundwater canvas.</summary>
    public ObservableCollection<LineLoadOverlayDisplay> WaterHeadOverlays { get; private set; } = null!;
    /// <summary>Prescribed-pressure bbox faces for transient p84/p88 consolidation.</summary>
    public ObservableCollection<LineLoadOverlayDisplay> TransientPressureBoundaryOverlays { get; private set; } = null!;
    /// <summary>Initial transient pore-pressure annotation shown inside the domain.</summary>
    public ObservableCollection<PondedWaterCallout> TransientPressureCallouts { get; private set; } = null!;
    public ObservableCollection<TransientFixedPressureNodeOverlay> TransientFixedPressureNodeOverlays { get; private set; } = null!;
    public bool IsTransientNodeSelectionRectVisible { get; private set; }
    public double TransientNodeSelectionLeft { get; private set; }
    public double TransientNodeSelectionTop { get; private set; }
    public double TransientNodeSelectionWidth { get; private set; }
    public double TransientNodeSelectionHeight { get; private set; }
    public ObservableCollection<GroundwaterSurfPointMarker> GroundwaterSurfPointMarkers { get; } = new();
    public PointCollection GroundwaterPhreaticPolyline { get; private set; } = new();
    /// <summary>Horizontal Total Head water-level line when that ponded method is active.</summary>
    public PointCollection PondedWaterLevelPolyline { get; private set; } = new();
    private GroundwaterSurfPoint? _highlightedGroundwaterSurfPoint;
    public IReadOnlyList<(double x, double y)> LastSaturatedZoneAxisPolygonForTests { get; private set; } =
        Array.Empty<(double x, double y)>();
    public IReadOnlyList<(double x, double y)> LastPondedZoneAxisPolygonForTests { get; private set; } =
        Array.Empty<(double x, double y)>();

    public bool IsGroundwaterPolylineDrawActive
    {
        get => _isGroundwaterPolylineDrawActive;
        private set { _isGroundwaterPolylineDrawActive = value; OnPropertyChanged(); }
    }

    public bool IsGroundwaterEnabled => _groundwaterVm?.IsEnabled == true;

    private bool _showGroundwaterZoneOverlaysOnCanvas;

    /// <summary>
    /// Filled saturated/unsaturated zone overlays — only on the Groundwater tab.
    /// Other tabs keep the phreatic line when groundwater is enabled but hide zone fills.
    /// </summary>
    public bool ShowGroundwaterZoneOverlaysOnCanvas
    {
        get => _showGroundwaterZoneOverlaysOnCanvas;
        set
        {
            if (_showGroundwaterZoneOverlaysOnCanvas == value) return;
            _showGroundwaterZoneOverlaysOnCanvas = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGroundwaterZoneOverlayVisible));
            OnPropertyChanged(nameof(IsPondedFreeWaterOverlayVisible));
            OnPropertyChanged(nameof(IsPondedHeadIsobarsVisible));
            OnPropertyChanged(nameof(IsPondedFacePressureArrowsVisible));
            OnPropertyChanged(nameof(IsWaterHeadOverlayVisible));
            OnPropertyChanged(nameof(IsTransientPressureOverlayVisible));
            // Rebuild when entering Groundwater tab so annotations map to current canvas coords.
            if (value)
            {
                RefreshWaterHeadOverlays();
                RefreshTransientPressureOverlays();
            }
        }
    }

    public bool IsGroundwaterZoneOverlayVisible =>
        IsGroundwaterEnabled && ShowGroundwaterZoneOverlaysOnCanvas;

    private bool IsPondedCanvasBaseVisible =>
        IsGroundwaterEnabled
        && ShowGroundwaterZoneOverlaysOnCanvas
        && _groundwaterVm?.IsPondedFreeWaterAllowed == true
        && _groundwaterVm.IsHydrostaticMode;

    /// <summary>
    /// Ponded depth field is Groundwater-tab only (same gate as in-soil saturated wash).
    /// Phreatic line remains visible on other tabs when GW is enabled.
    /// </summary>
    public bool IsPondedFreeWaterOverlayVisible =>
        IsPondedCanvasBaseVisible
        && _groundwaterVm?.ShowPondedFreeWaterRegion == true
        && PondedWaterOverlays.Count > 0;

    /// <summary>Head isobars — independent of the filled ponded region checkbox.</summary>
    public bool IsPondedHeadIsobarsVisible =>
        IsPondedCanvasBaseVisible
        && _groundwaterVm?.ShowPondedHeadIsobars == true
        && PondedWaterIsobars.Count > 0;

    /// <summary>Auto-assigned ponded DistributedEdgeLoads as face-pressure arrows (mesh required).</summary>
    public bool IsPondedFacePressureArrowsVisible =>
        IsPondedCanvasBaseVisible
        && _groundwaterVm?.ShowPondedFacePressureArrows == true
        && PondedFacePressureOverlays.Count > 0;

    /// <summary>
    /// Water Head overlays on the Groundwater tab. Gated by tab + Water Head setting only
    /// (not by Count) so Collapsed→Visible transitions cannot hide a just-placed segment.
    /// </summary>
    public bool IsWaterHeadOverlayVisible =>
        ShowGroundwaterZoneOverlaysOnCanvas
        && _groundwaterVm?.IsWaterHeadAllowed == true;

    public bool IsTransientPressureOverlayVisible =>
        ShowGroundwaterZoneOverlaysOnCanvas
        && _groundwaterVm?.IsTransientBcAllowed == true;

    internal void SetGroundwaterViewModel(GroundwaterViewModel vm) => _groundwaterVm = vm;

    /// <summary>
    /// When Groundwater → Water Head → Mouse select is active, convert canvas click to axis
    /// and place the selected water head. Returns true if the click was consumed.
    /// </summary>
    public bool TryHandleWaterHeadCanvasClick(Point canvasPoint)
    {
        if (_groundwaterVm is not { IsWaterHeadMousePlaceActive: true }) return false;
        var axis = ConvertCanvasToAxis(canvasPoint);
        _groundwaterVm.RegisterWaterHeadPoint(axis);
        return true;
    }

    /// <summary>Place a transient prescribed-pressure Line from a Groundwater canvas click.</summary>
    public bool TryHandleTransientFixedPressureCanvasClick(Point canvasPoint)
    {
        if (_groundwaterVm is not
            { IsTransientBcAllowed: true, IsTransientFixedPressureMousePlacementActive: true })
            return false;
        _groundwaterVm.RegisterTransientFixedPressureNode(ConvertCanvasToAxis(canvasPoint));
        return true;
    }

    public bool IsTransientNodeSelectionModeActive =>
        _groundwaterVm?.IsTransientNodeSelectionEnabled == true
        && _groundwaterVm.IsTransientBcAllowed;

    public void UpdateTransientNodeSelectionRect(Rect canvasRect)
    {
        var normalized = NormalizeCanvasRect(canvasRect);
        TransientNodeSelectionLeft = normalized.Left;
        TransientNodeSelectionTop = normalized.Top;
        TransientNodeSelectionWidth = normalized.Width;
        TransientNodeSelectionHeight = normalized.Height;
        IsTransientNodeSelectionRectVisible = normalized.Width > 0 && normalized.Height > 0;
        OnPropertyChanged(nameof(TransientNodeSelectionLeft));
        OnPropertyChanged(nameof(TransientNodeSelectionTop));
        OnPropertyChanged(nameof(TransientNodeSelectionWidth));
        OnPropertyChanged(nameof(TransientNodeSelectionHeight));
        OnPropertyChanged(nameof(IsTransientNodeSelectionRectVisible));
    }

    public void ClearTransientNodeSelectionRect()
    {
        if (!IsTransientNodeSelectionRectVisible
            && TransientNodeSelectionWidth == 0
            && TransientNodeSelectionHeight == 0)
            return;
        TransientNodeSelectionLeft = 0;
        TransientNodeSelectionTop = 0;
        TransientNodeSelectionWidth = 0;
        TransientNodeSelectionHeight = 0;
        IsTransientNodeSelectionRectVisible = false;
        OnPropertyChanged(nameof(TransientNodeSelectionLeft));
        OnPropertyChanged(nameof(TransientNodeSelectionTop));
        OnPropertyChanged(nameof(TransientNodeSelectionWidth));
        OnPropertyChanged(nameof(TransientNodeSelectionHeight));
        OnPropertyChanged(nameof(IsTransientNodeSelectionRectVisible));
    }

    public void CompleteTransientNodeSelection(Rect canvasRect, double clickToleranceCanvasPx)
    {
        if (_groundwaterVm is not { IsTransientNodeSelectionEnabled: true }) return;
        ClearTransientNodeSelectionRect();
        var normalized = NormalizeCanvasRect(canvasRect);
        bool isBox = Math.Abs(normalized.Width) >= 8.0 && Math.Abs(normalized.Height) >= 8.0;
        var nodes = isBox
            ? CollectMeshNodesInCanvasRect(normalized)
            : CollectNearestMeshNode(
                new Point(normalized.X + normalized.Width * 0.5, normalized.Y + normalized.Height * 0.5),
                clickToleranceCanvasPx);
        _groundwaterVm.RegisterTransientFixedPressureNodesFromSelection(nodes);
    }

    private List<(int NodeId, Point Axis)> CollectMeshNodesInCanvasRect(Rect canvasRect)
    {
        var result = new List<(int NodeId, Point Axis)>();
        var mesh = MeshSource;
        if (mesh is not { IsDiscretized: true, DiscretizationNodes.Count: > 0 })
            return result;
        for (var i = 0; i < mesh.DiscretizationNodes.Count; i++)
        {
            var node = mesh.DiscretizationNodes[i];
            var id = node.NodeId > 0 ? node.NodeId : i + 1;
            var canvas = ConvertAxisToCanvas(node.Location);
            if (!canvasRect.Contains(canvas)) continue;
            result.Add((id, node.Location));
        }
        return result;
    }

    private List<(int NodeId, Point Axis)> CollectNearestMeshNode(Point canvasPoint, double toleranceCanvasPx)
    {
        var mesh = MeshSource;
        if (mesh is not { IsDiscretized: true, DiscretizationNodes.Count: > 0 })
            return new List<(int NodeId, Point Axis)>();
        double tol2 = toleranceCanvasPx * toleranceCanvasPx;
        double best = double.MaxValue;
        int bestId = 0;
        Point bestAxis = default;
        for (var i = 0; i < mesh.DiscretizationNodes.Count; i++)
        {
            var node = mesh.DiscretizationNodes[i];
            var id = node.NodeId > 0 ? node.NodeId : i + 1;
            var canvas = ConvertAxisToCanvas(node.Location);
            double dx = canvas.X - canvasPoint.X, dy = canvas.Y - canvasPoint.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 >= best) continue;
            best = d2;
            bestId = id;
            bestAxis = node.Location;
        }
        return best <= tol2 && bestId > 0
            ? new List<(int NodeId, Point Axis)> { (bestId, bestAxis) }
            : new List<(int NodeId, Point Axis)>();
    }

    private static Rect NormalizeCanvasRect(Rect rect)
    {
        double x = Math.Min(rect.Left, rect.Right);
        double y = Math.Min(rect.Top, rect.Bottom);
        return new Rect(x, y, Math.Abs(rect.Width), Math.Abs(rect.Height));
    }

    private void InitGroundwaterCollections()
    {
        GroundwaterSurfPoints = new ObservableCollection<GroundwaterSurfPoint>();
        GroundwaterZoneOverlays = new ObservableCollection<GroundwaterZoneOverlay>();
        PondedWaterOverlays = new ObservableCollection<PondedWaterOverlay>();
        PondedWaterIsobars = new ObservableCollection<PondedWaterIsobarSegment>();
        PondedWaterCallouts = new ObservableCollection<PondedWaterCallout>();
        PondedFacePressureOverlays = new ObservableCollection<LineLoadOverlayDisplay>();
        WaterHeadOverlays = new ObservableCollection<LineLoadOverlayDisplay>();
        TransientPressureBoundaryOverlays = new ObservableCollection<LineLoadOverlayDisplay>();
        TransientPressureCallouts = new ObservableCollection<PondedWaterCallout>();
        TransientFixedPressureNodeOverlays = new ObservableCollection<TransientFixedPressureNodeOverlay>();
        GroundwaterSurfPoints.CollectionChanged += OnGroundwaterSurfPointsChanged;
    }

    private void OnGroundwaterSurfPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (GroundwaterSurfPoint p in e.NewItems)
                p.PropertyChanged += OnGroundwaterSurfPointPropertyChanged;
        if (e.OldItems != null)
            foreach (GroundwaterSurfPoint p in e.OldItems)
                p.PropertyChanged -= OnGroundwaterSurfPointPropertyChanged;
        ReindexGroundwaterSurfPoints();
        RefreshGroundwaterOverlays();
        _groundwaterVm?.OnSurfPointsChanged();
    }

    private void OnGroundwaterSurfPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GroundwaterSurfPoint.AxisX) or nameof(GroundwaterSurfPoint.AxisY))
        {
            RefreshGroundwaterOverlays();
            _groundwaterVm?.OnSurfPointsChanged();
        }
    }

    public void AddHorizontalWaterTable(double elevation)
    {
        if (GeometryVertexRows.Count < 3) return;
        var poly = GeometryVertexRows.Select(v => (x: v.AxisX, y: v.AxisY)).ToList();
        var n = poly.Count;
        var eps = AxisEqualityTolerance;
        if (!TryGetWidestHorizontalCutSpan(poly, n, elevation, eps, out var xLo, out var xHi))
            return;

        if (_groundwaterVm?.DrawMode == GroundwaterDrawMode.Horizontal)
            GroundwaterSurfPoints.Clear();

        GroundwaterSurfPoints.Add(new GroundwaterSurfPoint(GroundwaterSurfPoints.Count + 1, xLo, elevation));
        GroundwaterSurfPoints.Add(new GroundwaterSurfPoint(GroundwaterSurfPoints.Count + 1, xHi, elevation));
    }

    public void BeginGroundwaterPolylineDraw()
    {
        IsGroundwaterPolylineDrawActive = true;
        GroundwaterSurfPoints.Clear();
    }

    public void EndGroundwaterPolylineDraw() => IsGroundwaterPolylineDrawActive = false;

    public void ClearGroundwater()
    {
        GroundwaterSurfPoints.Clear();
        RefreshGroundwaterOverlays();
    }

    public void RemoveGroundwaterSurfPoint(GroundwaterSurfPoint point)
    {
        GroundwaterSurfPoints.Remove(point);
    }

    public void ApplyGroundwaterSurfPoints(IReadOnlyList<(double X, double Y)> points)
    {
        GroundwaterSurfPoints.Clear();
        foreach (var (x, y) in points)
            GroundwaterSurfPoints.Add(new GroundwaterSurfPoint(GroundwaterSurfPoints.Count + 1, x, y));
    }

    public void HandleGroundwaterPolylineCanvasClick(Point canvasPoint)
    {
        if (!IsGroundwaterPolylineDrawActive) return;
        var axis = ConvertCanvasToAxis(canvasPoint);
        var snapped = SnapGroundwaterAxisPoint(axis);
        GroundwaterSurfPoints.Add(new GroundwaterSurfPoint(
            GroundwaterSurfPoints.Count + 1, snapped.X, snapped.Y));
    }

    private Point SnapGroundwaterAxisPoint(Point axis)
    {
        var snapTol = Math.Max(AxisEqualityTolerance * 100, ComputeAxisHitTolerance() * 2);
        var snapTol2 = snapTol * snapTol;
        Point best = axis;
        double bestD2 = snapTol2;

        foreach (var v in GeometryVertexRows)
        {
            var dx = axis.X - v.AxisX;
            var dy = axis.Y - v.AxisY;
            var d2 = dx * dx + dy * dy;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = new Point(v.AxisX, v.AxisY);
            }
        }

        if (GeometryVertexRows.Count >= 3)
        {
            var poly = GeometryVertexRows.Select(v => (x: v.AxisX, y: v.AxisY)).ToList();
            var n = poly.Count;
            if (TryGetWidestHorizontalCutSpan(poly, n, axis.Y, AxisEqualityTolerance, out var xLo, out var xHi))
            {
                var xSnap = Math.Clamp(axis.X, Math.Min(xLo, xHi), Math.Max(xLo, xHi));
                var ground = new Point(xSnap, axis.Y);
                var dx = axis.X - ground.X;
                var dy = axis.Y - ground.Y;
                var d2 = dx * dx + dy * dy;
                if (d2 < bestD2)
                    best = ground;
            }
        }

        return best;
    }

    private void ReindexGroundwaterSurfPoints()
    {
        for (var i = 0; i < GroundwaterSurfPoints.Count; i++)
            GroundwaterSurfPoints[i].Index = i + 1;
    }

    public void UpdateGroundwaterSurfPointHighlight(GroundwaterSurfPoint? selected)
    {
        _highlightedGroundwaterSurfPoint = selected;
        ApplyGroundwaterSurfPointHighlight();
    }

    private void ApplyGroundwaterSurfPointHighlight()
    {
        var selectedIndex = _highlightedGroundwaterSurfPoint?.Index ?? -1;
        foreach (var marker in GroundwaterSurfPointMarkers)
            marker.IsHighlighted = marker.Index == selectedIndex;
    }

    public void RefreshGroundwaterOverlays() => RefreshGroundwaterOverlays(ensureAxisBounds: true);

    public void RefreshGroundwaterOverlays(bool ensureAxisBounds)
    {
        GroundwaterLines.Clear();
        GroundwaterZoneOverlays.Clear();
        PondedWaterOverlays.Clear();
        PondedWaterIsobars.Clear();
        PondedWaterCallouts.Clear();
        PondedFacePressureOverlays.Clear();
        GroundwaterSurfPointMarkers.Clear();
        LastPondedZoneAxisPolygonForTests = Array.Empty<(double x, double y)>();
        // Keep the WT line visible on every tab while groundwater is enabled
        // (zone fills remain gated by ShowGroundwaterZoneOverlaysOnCanvas).
        ShowGroundwaterPhreaticOnCanvas = IsGroundwaterEnabled;
        OnPropertyChanged(nameof(IsGroundwaterEnabled));
        OnPropertyChanged(nameof(IsGroundwaterZoneOverlayVisible));
        OnPropertyChanged(nameof(IsGroundwaterPhreaticVisible));

        if (_groundwaterVm?.IsEnabled != true)
        {
            LastSaturatedZoneAxisPolygonForTests = Array.Empty<(double x, double y)>();
            GroundwaterPhreaticPolyline = new PointCollection();
            PondedWaterLevelPolyline = new PointCollection();
            OnPropertyChanged(nameof(GroundwaterPhreaticPolyline));
            OnPropertyChanged(nameof(PondedWaterLevelPolyline));
            OnPropertyChanged(nameof(GroundwaterSurfPointMarkers));
            NotifyPondedOverlayVisibility();
            RefreshWaterHeadOverlays();
            RefreshTransientPressureOverlays();
            return;
        }

        var polyline = new PointCollection();
        for (var i = 0; i < GroundwaterSurfPoints.Count; i++)
        {
            var p = GroundwaterSurfPoints[i];
            var canvas = ConvertAxisToCanvas(new Point(p.AxisX, p.AxisY));
            polyline.Add(canvas);
            GroundwaterSurfPointMarkers.Add(new GroundwaterSurfPointMarker(p.Index, canvas.X, canvas.Y));
            if (i > 0)
            {
                var prev = GroundwaterSurfPoints[i - 1];
                var s = ConvertAxisToCanvas(new Point(prev.AxisX, prev.AxisY));
                var e = ConvertAxisToCanvas(new Point(p.AxisX, p.AxisY));
                GroundwaterLines.Add(new GroundwaterLine(s, e, p.AxisY));
            }
        }

        GroundwaterPhreaticPolyline = polyline;
        ApplyGroundwaterSurfPointHighlight();
        OnPropertyChanged(nameof(GroundwaterPhreaticPolyline));
        OnPropertyChanged(nameof(GroundwaterSurfPointMarkers));

        if (GeometryVertexRows.Count < 3)
        {
            PondedWaterLevelPolyline = new PointCollection();
            OnPropertyChanged(nameof(PondedWaterLevelPolyline));
            NotifyPondedOverlayVisibility();
            RefreshWaterHeadOverlays();
            RefreshTransientPressureOverlays();
            return;
        }

        var poly = GeometryVertexRows.Select(v => (x: v.AxisX, y: v.AxisY, label: v.PointLabel)).ToList();
        var (_, _, yMin, yMax) = GetBoundingBoxBounds();
        var eps = AxisEqualityTolerance;
        var xMin = poly.Min(p => p.x);
        var xMax = poly.Max(p => p.x);

        // Always compute the saturated geometry for verification/solver-alignment probes, even
        // when its canvas wash is hidden by the display checkbox.
        LastSaturatedZoneAxisPolygonForTests = Array.Empty<(double x, double y)>();
        if (GroundwaterSurfPoints.Count > 0)
        {
            BuildGroundwaterZoneForSide(
                poly, yMin, yMax, eps, xMin, xMax,
                saturated: true,
                addOverlay: _groundwaterVm?.ShowSaturatedMaterialZone != false);

            if (_groundwaterVm?.ShowUnsaturatedMaterialZone == true)
            {
                BuildGroundwaterZoneForSide(
                    poly, yMin, yMax, eps, xMin, xMax,
                    saturated: false,
                    addOverlay: true);
            }
        }

        BuildPondedFreeWaterOverlays(poly, yMin, yMax, eps, xMin, xMax);
        BuildPondedFacePressureOverlays();
        RefreshWaterHeadOverlays();
        RefreshTransientPressureOverlays();
        OnPropertyChanged(nameof(PondedWaterLevelPolyline));
        OnPropertyChanged(nameof(PondedWaterOverlays));
        OnPropertyChanged(nameof(PondedWaterIsobars));
        OnPropertyChanged(nameof(PondedWaterCallouts));
        OnPropertyChanged(nameof(PondedFacePressureOverlays));
        NotifyPondedOverlayVisibility();
        // Only when WT content actually changed — not on every canvas resize remap.
        if (ensureAxisBounds)
            EnsureAxisBoundsIncludeContent();
    }

    /// <summary>Rebuild Water Head line overlays (grid rows + in-progress draft).</summary>
    public void RefreshWaterHeadOverlays()
    {
        WaterHeadOverlays.Clear();
        if (_groundwaterVm?.IsWaterHeadAllowed == true)
        {
            var selected = _groundwaterVm.SelectedWaterHead;

            // Selected grid row first — clicking a row must show its head line prominently.
            if (selected is { HasPlacement: true })
            {
                var selectedOverlay = WaterHeadOverlayBuilder.Build(
                    selected, this, awaitingEnd: false, isSelected: true);
                if (selectedOverlay != null)
                    WaterHeadOverlays.Add(selectedOverlay);
            }

            foreach (var head in _groundwaterVm.WaterHeads)
            {
                if (ReferenceEquals(head, selected)) continue;
                if (!head.HasPlacement) continue;
                var overlay = selected != null
                    ? WaterHeadOverlayBuilder.BuildDimmed(head, this)
                    : WaterHeadOverlayBuilder.Build(head, this, awaitingEnd: false);
                if (overlay != null)
                    WaterHeadOverlays.Add(overlay);
            }

            // Draft preview while placing a new head (before Add), when it is not the selected row.
            var draft = _groundwaterVm.WaterHeadDraft;
            bool awaiting = _groundwaterVm.IsWaterHeadLineAwaitingEnd;
            if (draft.HasPlacement || awaiting)
            {
                bool matchesSelected = selected is { HasPlacement: true }
                    && Math.Abs(selected.StartX - draft.StartX) < 1e-9
                    && Math.Abs(selected.StartY - draft.StartY) < 1e-9
                    && Math.Abs(selected.EndX - draft.EndX) < 1e-9
                    && Math.Abs(selected.EndY - draft.EndY) < 1e-9;
                if (!matchesSelected || awaiting)
                {
                    var draftOverlay = WaterHeadOverlayBuilder.Build(
                        draft, this, awaiting, isSelected: selected == null);
                    if (draftOverlay != null)
                        WaterHeadOverlays.Add(draftOverlay);
                }
            }
        }
        OnPropertyChanged(nameof(WaterHeadOverlays));
        OnPropertyChanged(nameof(IsWaterHeadOverlayVisible));
    }

    /// <summary>Rebuild transient initial/fixed-pressure annotations on the Groundwater canvas.</summary>
    public void RefreshTransientPressureOverlays()
    {
        TransientPressureBoundaryOverlays.Clear();
        TransientPressureCallouts.Clear();
        TransientFixedPressureNodeOverlays.Clear();

        if (_groundwaterVm is { IsTransientBcAllowed: true, TransientSettings: { } settings })
        {
            foreach (var overlay in TransientPressureOverlayBuilder.BuildFixedBoundaries(
                         settings, this, _groundwaterVm.StressUnitLabel,
                         _groundwaterVm.TransientFixedPressureNodes))
                TransientPressureBoundaryOverlays.Add(overlay);

            var initial = TransientPressureOverlayBuilder.BuildInitialPressureCallout(
                settings, this, _groundwaterVm.StressUnitLabel);
            if (initial != null)
                TransientPressureCallouts.Add(initial);

            foreach (var node in _groundwaterVm.TransientFixedPressureNodes.Where(n =>
                         n.IsEnabled && n.HasPlacement && n.IsPointPlacement))
                AddTransientFixedPressureNodeOverlay(node, ReferenceEquals(
                    node, _groundwaterVm.SelectedTransientFixedPressureNode));

            foreach (var preview in _groundwaterVm.TransientNodeSelectionPreview)
            {
                if (_groundwaterVm.TransientFixedPressureNodes.Any(n =>
                        n.IsPointPlacement && n.NodeId == preview.NodeId))
                    continue;
                var canvas = ConvertAxisToCanvas(preview.Axis);
                TransientFixedPressureNodeOverlays.Add(new TransientFixedPressureNodeOverlay
                {
                    X = canvas.X,
                    Y = canvas.Y,
                    Text = $"Node {preview.NodeId} (selected)",
                    IsSelected = true
                });
            }

            var draft = _groundwaterVm.TransientFixedPressureDraft;
            if (draft.IsMouseSelect && draft.IsLinePlacement
                && (draft.HasPlacement || _groundwaterVm.IsTransientFixedPressureLineAwaitingEnd))
            {
                var draftOnlySettings = new TransientAnalysisSettings
                {
                    FixTopFace = false,
                    FixBottomFace = false,
                    FixMinXFace = false,
                    FixMaxXFace = false,
                };
                foreach (var overlay in TransientPressureOverlayBuilder.BuildFixedBoundaries(
                             draftOnlySettings, this, _groundwaterVm.StressUnitLabel,
                             new[] { draft }))
                    TransientPressureBoundaryOverlays.Add(overlay);
            }
        }

        OnPropertyChanged(nameof(TransientPressureBoundaryOverlays));
        OnPropertyChanged(nameof(TransientPressureCallouts));
        OnPropertyChanged(nameof(TransientFixedPressureNodeOverlays));
        OnPropertyChanged(nameof(IsTransientPressureOverlayVisible));
    }

    private void AddTransientFixedPressureNodeOverlay(TransientFixedPressureNode node, bool isSelected)
    {
        var canvas = ConvertAxisToCanvas(new Point(node.StartX, node.StartY));
        TransientFixedPressureNodeOverlays.Add(new TransientFixedPressureNodeOverlay
        {
            X = canvas.X,
            Y = canvas.Y,
            Text = node.NodeId > 0
                ? $"Node {node.NodeId}: p={node.Pressure:G5} {_groundwaterVm?.StressUnitLabel}".TrimEnd()
                : $"p={node.Pressure:G5} {_groundwaterVm?.StressUnitLabel}".TrimEnd(),
            IsSelected = isSelected
        });
    }

    private void NotifyPondedOverlayVisibility()
    {
        OnPropertyChanged(nameof(IsPondedFreeWaterOverlayVisible));
        OnPropertyChanged(nameof(IsPondedHeadIsobarsVisible));
        OnPropertyChanged(nameof(IsPondedFacePressureArrowsVisible));
        OnPropertyChanged(nameof(IsWaterHeadOverlayVisible));
        OnPropertyChanged(nameof(IsTransientPressureOverlayVisible));
    }

    private void BuildPondedFacePressureOverlays()
    {
        if (_groundwaterVm is null
            || !_groundwaterVm.IsPondedFreeWaterAllowed
            || !_groundwaterVm.IsHydrostaticMode
            || !_groundwaterVm.ShowPondedFacePressureArrows)
            return;

        var model = _groundwaterVm.BuildModel();
        foreach (var overlay in PondedFacePressureOverlayBuilder.Build(model, MeshSource, this))
            PondedFacePressureOverlays.Add(overlay);
    }

    /// <summary>
    /// Max / toe / crest free-water heads for status text. Null when inactive.
    /// Head is pressure head of ponded free water: h = y_water(x) − y_ground(x),
    /// sampled on the ground free-surface chain (not foundation / sidewalls).
    /// </summary>
    public (double MaxHead, double? ToeHead, double? CrestHead)? ComputePondedHeadSummary(
        PondedWaterLevelMethod method,
        double totalHead)
    {
        if (GeometryVertexRows.Count < 3) return null;
        var poly = GeometryVertexRows.Select(v => (x: v.AxisX, y: v.AxisY, label: v.PointLabel)).ToList();
        var (_, _, yMin, _) = GetBoundingBoxBounds();
        var eps = AxisEqualityTolerance;
        var xMin = poly.Min(p => p.x);
        var xMax = poly.Max(p => p.x);
        if (!TryBuildEffectiveWaterTopChain(method, totalHead, xMin, xMax, eps, out var top)
            || top.Count < 2)
            return null;

        var ground = ExtractGroundFreeSurfaceChain(poly, yMin, xMin, xMax, eps);
        if (ground.Count < 2) return null;

        var withHead = ground
            .Select(v => (v.x, v.y, h: InterpolateChainY(top, v.x) - v.y))
            .Where(t => t.h > 1e-9)
            .ToList();
        if (withHead.Count == 0) return null;

        double maxHead = withHead.Max(t => t.h);
        // Toe = lowest free-surface station; crest = highest (not domain foundation corners).
        double toeHead = withHead.OrderBy(t => t.y).ThenBy(t => t.x).First().h;
        double crestHead = withHead.OrderByDescending(t => t.y).ThenByDescending(t => t.x).First().h;

        return (maxHead, toeHead, crestHead);
    }

    private void BuildGroundwaterZoneForSide(
        List<(double x, double y, string label)> poly,
        double yMin,
        double yMax,
        double eps,
        double xMin,
        double xMax,
        bool saturated,
        bool addOverlay)
    {
        var rings = ClipSlopePolygonToPhreaticSide(poly, yMin, yMax, xMin, xMax, saturated, eps);
        if (rings.Count == 0) return;

        if (saturated)
        {
            // Prefer the largest ring for smoke-test probes (bench + slope cases are one pocket).
            var largest = rings
                .OrderByDescending(r => Math.Abs(SignedAxisRingArea(r)))
                .First();
            LastSaturatedZoneAxisPolygonForTests = largest.Select(p => (p.x, p.y)).ToList();
        }

        if (!addOverlay)
            return;

        var fill = saturated
            ? new SolidColorBrush(Color.FromArgb(0x88, 0x0E, 0xA5, 0xE9))
            : new SolidColorBrush(Color.FromArgb(0x40, 0xC4, 0xA5, 0x74));

        foreach (var merged in rings)
        {
            if (merged.Count < 3) continue;

            var canvasPoly = new PointCollection();
            foreach (var p in merged)
                canvasPoly.Add(ConvertAxisToCanvas(new Point(p.x, p.y)));

            GroundwaterZoneOverlays.Add(new GroundwaterZoneOverlay
            {
                Kind = saturated ? GroundwaterZoneKind.Saturated : GroundwaterZoneKind.Unsaturated,
                // Translucent sky-blue so soil fill remains readable under the wash.
                Fill = fill,
                Polygon = canvasPoly
            });
        }
    }

    /// <summary>
    /// Intersect the slope polygon with the saturated (below) or unsaturated (above) side of the
    /// row-order phreatic polyline via a WPF geometry boolean. Building an explicit below/above-WT
    /// region avoids the old per-segment Sutherland–Hodgman banding bug that invented exterior
    /// triangles when the water table peaked above the ground surface.
    /// </summary>
    private List<List<(double x, double y, string label)>> ClipSlopePolygonToPhreaticSide(
        IReadOnlyList<(double x, double y, string label)> slopePoly,
        double yMin,
        double yMax,
        double xMin,
        double xMax,
        bool saturated,
        double eps)
    {
        if (slopePoly.Count < 3 || GroundwaterSurfPoints.Count == 0)
            return new List<List<(double x, double y, string label)>>();

        var region = saturated
            ? BuildBelowPhreaticRegionPolygon(xMin, xMax, yMin, eps)
            : BuildAbovePhreaticRegionPolygon(xMin, xMax, yMax, eps);
        if (region.Count < 3)
            return new List<List<(double x, double y, string label)>>();

        return IntersectAxisRegionAll(slopePoly, region, eps);
    }

    /// <summary>
    /// Closed polygon for the half-space below the phreatic polyline, spanning the soil x-range
    /// and closed through a floor below <paramref name="yMin"/>. Top chain follows surf points
    /// left→right (reversed if drawn right→left) with endpoint elevation extended to xMin/xMax.
    /// </summary>
    /// <remarks>
    /// The top chain is inset slightly below the WT so WPF <see cref="CombinedGeometry"/> does not
    /// share an edge with the ground when the piezometric line sits on the bench (that coincident
    /// case produced a sawtooth fill outline along the mid-bench).
    /// </remarks>
    private List<(double x, double y, string label)> BuildBelowPhreaticRegionPolygon(
        double xMin,
        double xMax,
        double yMin,
        double eps)
    {
        var top = BuildPhreaticTopChain(xMin, xMax, eps);
        if (top.Count == 0) return new List<(double x, double y, string label)>();

        var ySpan = Math.Max(Math.Abs(yMin), top.Max(p => Math.Abs(p.y)));
        var yBottom = yMin - Math.Max(1.0, ySpan * 0.05 + (xMax - xMin) * 0.01);
        // Tiny downward inset avoids coincident-edge boolean flatten zigzags; canvas WT line is unchanged.
        var inset = Math.Max(eps * 10, (xMax - xMin) * 1e-4 + ySpan * 1e-4);

        var ring = new List<(double x, double y, string label)>(top.Count + 2);
        foreach (var p in top)
            ring.Add((p.x, p.y - inset, p.label));
        ring.Add((top[^1].x, yBottom, ""));
        ring.Add((top[0].x, yBottom, ""));
        return ring;
    }

    /// <summary>
    /// Closed polygon for the half-space above the phreatic polyline.
    /// </summary>
    private List<(double x, double y, string label)> BuildAbovePhreaticRegionPolygon(
        double xMin,
        double xMax,
        double yMax,
        double eps)
    {
        var top = BuildPhreaticTopChain(xMin, xMax, eps);
        if (top.Count == 0) return new List<(double x, double y, string label)>();

        var ySpan = Math.Max(Math.Abs(yMax), top.Max(p => Math.Abs(p.y)));
        var yCeiling = yMax + Math.Max(1.0, ySpan * 0.05 + (xMax - xMin) * 0.01);
        // Match the below-WT guard: avoid coincident WT/ground edges in WPF's native boolean engine.
        var inset = Math.Max(eps * 10, (xMax - xMin) * 1e-4 + ySpan * 1e-4);

        // Walk WT right→left along the chain so the above-WT pocket winds consistently.
        var ring = new List<(double x, double y, string label)>(top.Count + 2);
        ring.Add((top[0].x, yCeiling, ""));
        ring.Add((top[^1].x, yCeiling, ""));
        for (var i = top.Count - 1; i >= 0; i--)
            ring.Add((top[i].x, top[i].y + inset, top[i].label));
        return ring;
    }

    private List<(double x, double y, string label)> BuildPhreaticTopChain(
        double xMin,
        double xMax,
        double eps)
    {
        var pts = GroundwaterSurfPoints;
        if (pts.Count == 0) return new List<(double x, double y, string label)>();

        var ordered = pts.ToList();
        if (ordered.Count >= 2 && ordered[^1].AxisX < ordered[0].AxisX - eps)
            ordered.Reverse();

        var chain = new List<(double x, double y, string label)>();
        var yLeft = WaterTableYAtX(xMin);
        if (double.IsNaN(yLeft)) yLeft = ordered[0].AxisY;
        if (ordered[0].AxisX > xMin + eps)
            chain.Add((xMin, yLeft, ""));

        foreach (var p in ordered)
            chain.Add((p.AxisX, p.AxisY, ""));

        var yRight = WaterTableYAtX(xMax);
        if (double.IsNaN(yRight)) yRight = ordered[^1].AxisY;
        if (ordered[^1].AxisX < xMax - eps)
            chain.Add((xMax, yRight, ""));

        // Degenerate single-vertex WT: horizontal across the soil span.
        if (chain.Count == 1)
        {
            var y = chain[0].y;
            return new List<(double x, double y, string label)>
            {
                (xMin, y, ""),
                (xMax, y, "")
            };
        }

        return chain;
    }

    private void BuildPondedFreeWaterOverlays(
        List<(double x, double y, string label)> soilPoly,
        double yMin,
        double yMax,
        double eps,
        double xMin,
        double xMax)
    {
        PondedWaterLevelPolyline = new PointCollection();
        if (_groundwaterVm is null
            || !_groundwaterVm.IsPondedFreeWaterAllowed
            || !_groundwaterVm.IsHydrostaticMode)
            return;

        // Build geometry whenever any ponded display is requested (region / isobars independent).
        bool wantRegion = _groundwaterVm.ShowPondedFreeWaterRegion;
        bool wantIsobars = _groundwaterVm.ShowPondedHeadIsobars;
        if (!wantRegion && !wantIsobars)
            return;

        if (!TryBuildEffectiveWaterTopChain(
                _groundwaterVm.PondedWaterLevelMethod,
                _groundwaterVm.PondedTotalHead,
                xMin, xMax, eps, out var top)
            || top.Count < 2)
            return;

        // Total Head water-level line; phreatic uses GroundwaterPhreaticPolyline.
        if (wantRegion && _groundwaterVm.PondedWaterLevelMethod == PondedWaterLevelMethod.TotalHead)
        {
            var pc = new PointCollection();
            foreach (var p in top)
                pc.Add(ConvertAxisToCanvas(new Point(p.x, p.y)));
            PondedWaterLevelPolyline = pc;
        }

        // Ponded = free water ABOVE the ground surface only (WT chain + free-surface chain).
        // Do NOT use below-WT ∖ soil: that invents a slab under the foundation when the
        // below-WT floor sits below yMin.
        var ground = ExtractGroundFreeSurfaceChain(soilPoly, yMin, xMin, xMax, eps);
        if (ground.Count < 2) return;

        var pondedRing = BuildPondedFreeWaterRing(top, ground, eps);
        if (pondedRing.Count < 3) return;
        if (Math.Abs(SignedAxisRingArea(pondedRing)) <= eps) return;

        // Drop if no positive free-water head (WT at/below ground everywhere).
        double maxHeadProbe = 0;
        foreach (var g in ground)
            maxHeadProbe = Math.Max(maxHeadProbe, InterpolateChainY(top, g.x) - g.y);
        if (maxHeadProbe <= 1e-9) return;

        LastPondedZoneAxisPolygonForTests = pondedRing.Select(p => (p.x, p.y)).ToList();

        double yWaterMax = top.Max(p => p.y);
        double yGroundMin = ground.Min(p => p.y);
        double maxHead = Math.Max(1e-6, maxHeadProbe);

        if (wantRegion)
        {
            var canvasPoly = new PointCollection();
            foreach (var p in pondedRing)
                canvasPoly.Add(ConvertAxisToCanvas(new Point(p.x, p.y)));

            var cTop = ConvertAxisToCanvas(new Point(0.5 * (xMin + xMax), yWaterMax));
            var cBot = ConvertAxisToCanvas(new Point(0.5 * (xMin + xMax), yGroundMin));
            var brush = PondedWaterPatternBrushes.Create(
                _groundwaterVm.PondedWaterDisplayPattern,
                cTop.Y,
                cBot.Y);

            PondedWaterOverlays.Add(new PondedWaterOverlay
            {
                Polygon = canvasPoly,
                Fill = brush
            });

            // Toe / crest callouts from free-surface stations with head.
            var withHead = ground
                .Select(v => (v.x, v.y, h: InterpolateChainY(top, v.x) - v.y))
                .Where(t => t.h > 1e-9)
                .OrderBy(t => t.x)
                .ToList();
            if (withHead.Count > 0)
            {
                AddPondedCallout(withHead[0].x, withHead[0].y, withHead[0].h);
                if (withHead.Count > 1)
                    AddPondedCallout(withHead[^1].x, withHead[^1].y, withHead[^1].h);
            }
        }

        if (wantIsobars)
        {
            // Head isobars — sparse horizontals at regular Δh, clipped to free-surface x-span.
            int bands = Math.Clamp((int)Math.Round(maxHead / Math.Max(maxHead * 0.25, 1.0)), 2, 5);
            double dh = maxHead / bands;
            double xLo = ground.Min(p => p.x);
            double xHi = ground.Max(p => p.x);
            for (int i = 1; i < bands; i++)
            {
                double yIso = yWaterMax - i * dh;
                if (yIso <= yGroundMin + eps) continue;
                var a = ConvertAxisToCanvas(new Point(xLo, yIso));
                var b = ConvertAxisToCanvas(new Point(xHi, yIso));
                PondedWaterIsobars.Add(new PondedWaterIsobarSegment
                {
                    X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y
                });
            }
        }
    }

    /// <summary>
    /// Closed ponded ring: water surface left→right, then ground free surface right→left.
    /// Only stations with positive head are kept on the ground chain.
    /// </summary>
    private static List<(double x, double y, string label)> BuildPondedFreeWaterRing(
        IReadOnlyList<(double x, double y, string label)> waterTop,
        IReadOnlyList<(double x, double y, string label)> ground,
        double eps)
    {
        var groundWet = ground
            .Select(g => (g.x, g.y, g.label, h: InterpolateChainY(waterTop, g.x) - g.y))
            .Where(g => g.h > eps)
            .OrderBy(g => g.x)
            .ToList();
        if (groundWet.Count < 2)
            return new List<(double x, double y, string label)>();

        double xLo = groundWet[0].x;
        double xHi = groundWet[^1].x;
        var waterSeg = waterTop
            .Where(p => p.x >= xLo - eps && p.x <= xHi + eps)
            .OrderBy(p => p.x)
            .ToList();
        if (waterSeg.Count == 0)
        {
            waterSeg =
            [
                (xLo, InterpolateChainY(waterTop, xLo), ""),
                (xHi, InterpolateChainY(waterTop, xHi), "")
            ];
        }
        else
        {
            if (waterSeg[0].x > xLo + eps)
                waterSeg.Insert(0, (xLo, InterpolateChainY(waterTop, xLo), ""));
            if (waterSeg[^1].x < xHi - eps)
                waterSeg.Add((xHi, InterpolateChainY(waterTop, xHi), ""));
        }

        var ring = new List<(double x, double y, string label)>();
        foreach (var p in waterSeg)
            ring.Add((p.x, p.y, p.label));
        for (var i = groundWet.Count - 1; i >= 0; i--)
            ring.Add((groundWet[i].x, groundWet[i].y, groundWet[i].label));
        return ring;
    }

    /// <summary>
    /// Exterior free-surface chain of the soil polygon (toe bench / slope / crest),
    /// skipping the foundation base and steep vertical sidewalls.
    /// </summary>
    private static List<(double x, double y, string label)> ExtractGroundFreeSurfaceChain(
        IReadOnlyList<(double x, double y, string label)> soilPoly,
        double yMin,
        double xMin,
        double xMax,
        double eps)
    {
        if (soilPoly.Count < 3)
            return new List<(double x, double y, string label)>();

        double ySpan = Math.Max(eps, soilPoly.Max(p => p.y) - yMin);
        double baseBand = 0.02 * ySpan;
        double xSpan = Math.Max(eps, xMax - xMin);
        double sideBand = 0.02 * xSpan;

        var edges = new List<(double x0, double y0, string l0, double x1, double y1, string l1)>();
        for (var i = 0; i < soilPoly.Count; i++)
        {
            var a = soilPoly[i];
            var b = soilPoly[(i + 1) % soilPoly.Count];
            double midY = 0.5 * (a.y + b.y);
            double midX = 0.5 * (a.x + b.x);
            double dx = b.x - a.x;
            double dy = b.y - a.y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < eps) continue;

            // Foundation / base contact.
            if (midY <= yMin + baseBand)
                continue;

            // Steep sidewalls at domain left/right (rollers) — not free-water faces for viz.
            double nx = Math.Abs(dy) / len; // |normal_x| for edge (dx,dy)
            double ny = Math.Abs(dx) / len;
            if (nx > 0.85 && ny < 0.35
                && (midX <= xMin + sideBand || midX >= xMax - sideBand))
                continue;

            edges.Add((a.x, a.y, a.label, b.x, b.y, b.label));
        }

        if (edges.Count == 0)
            return new List<(double x, double y, string label)>();

        // Stitch into a left→right polyline of unique vertices.
        var pts = new List<(double x, double y, string label)>();
        foreach (var e in edges.OrderBy(e => Math.Min(e.x0, e.x1)))
        {
            void Add(double x, double y, string label)
            {
                if (pts.Count > 0)
                {
                    var last = pts[^1];
                    if (Math.Abs(last.x - x) <= eps && Math.Abs(last.y - y) <= eps)
                        return;
                }
                pts.Add((x, y, label));
            }

            if (e.x0 <= e.x1)
            {
                Add(e.x0, e.y0, e.l0);
                Add(e.x1, e.y1, e.l1);
            }
            else
            {
                Add(e.x1, e.y1, e.l1);
                Add(e.x0, e.y0, e.l0);
            }
        }

        return pts.OrderBy(p => p.x).ThenBy(p => p.y).ToList();
    }

    private void AddPondedCallout(double axisX, double axisY, double head)
    {
        var c = ConvertAxisToCanvas(new Point(axisX, axisY));
        PondedWaterCallouts.Add(new PondedWaterCallout
        {
            X = c.X,
            Y = c.Y - 14,
            Text = $"h = {head:G3}"
        });
    }

    private bool TryBuildEffectiveWaterTopChain(
        PondedWaterLevelMethod method,
        double totalHead,
        double xMin,
        double xMax,
        double eps,
        out List<(double x, double y, string label)> top)
    {
        if (method == PondedWaterLevelMethod.TotalHead && double.IsFinite(totalHead))
        {
            top = new List<(double x, double y, string label)>
            {
                (xMin, totalHead, ""),
                (xMax, totalHead, "")
            };
            return true;
        }

        top = BuildPhreaticTopChain(xMin, xMax, eps);
        return top.Count >= 2 || top.Count == 1;
    }

    private static double InterpolateChainY(IReadOnlyList<(double x, double y, string label)> chain, double x)
    {
        if (chain.Count == 0) return double.NaN;
        if (chain.Count == 1) return chain[0].y;
        if (x <= chain[0].x) return chain[0].y;
        if (x >= chain[^1].x) return chain[^1].y;
        for (var i = 1; i < chain.Count; i++)
        {
            var x0 = chain[i - 1].x;
            var y0 = chain[i - 1].y;
            var x1 = chain[i].x;
            var y1 = chain[i].y;
            if (x < Math.Min(x0, x1) || x > Math.Max(x0, x1)) continue;
            if (Math.Abs(x1 - x0) < 1e-14) return 0.5 * (y0 + y1);
            var t = (x - x0) / (x1 - x0);
            return y0 + t * (y1 - y0);
        }
        return chain[^1].y;
    }

    /// <summary>
    /// Phreatic elevation at x using row-order segments (nosurf/surf sequence).
    /// When multiple segments span the same x, the last row-order segment wins.
    /// </summary>
    private double WaterTableYAtX(double x)
    {
        var pts = GroundwaterSurfPoints;
        if (pts.Count == 0) return double.NaN;
        if (pts.Count == 1) return pts[0].AxisY;

        var first = pts[0];
        var last = pts[^1];
        if (x <= Math.Min(first.AxisX, pts[1].AxisX) - AxisEqualityTolerance)
            return first.AxisY;
        if (x >= Math.Max(last.AxisX, pts[^2].AxisX) + AxisEqualityTolerance)
            return last.AxisY;

        double? bestY = null;
        for (var i = 1; i < pts.Count; i++)
        {
            var x0 = pts[i - 1].AxisX;
            var y0 = pts[i - 1].AxisY;
            var x1 = pts[i].AxisX;
            var y1 = pts[i].AxisY;
            var xLo = Math.Min(x0, x1);
            var xHi = Math.Max(x0, x1);
            if (x < xLo - AxisEqualityTolerance || x > xHi + AxisEqualityTolerance)
                continue;

            if (Math.Abs(x1 - x0) < 1e-14)
                bestY = 0.5 * (y0 + y1);
            else
            {
                var t = (x - x0) / (x1 - x0);
                bestY = y0 + t * (y1 - y0);
            }
        }

        return bestY ?? last.AxisY;
    }
}
