using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

public partial class GeometryDefinitionViewModel
{
    // ── Non-horizontal layer data ─────────────────────────────────────────
    private readonly List<List<(double X, double Y)>> _nonHorizontalLayerCuts = new();

    /// <summary>Axis-space zone outlines per display layer number, captured while building summaries
    /// so the Material Properties canvas can render filled strips (parity with horizontal layers).
    /// A single zone may be multiply-connected (e.g. an inclined cut over a benched boundary splits
    /// the zone into more than one pocket), so each display layer holds a list of polygons.</summary>
    private readonly Dictionary<int, List<List<(double x, double y)>>> _nonHorizontalLayerStripPolygons = new();

    private bool _isNonHorizontalLayerDrawMode;
    private bool _isNonHorizontalLayerModeSelected;

    /// <summary>Teal polylines on the canvas for finalized non-horizontal layer cuts.</summary>
    public ObservableCollection<InternalEdgeCanvasLine> NonHorizontalLayerCanvasLines { get; } = new();

    /// <summary>Live canvas preview of the polyline currently being drawn.</summary>
    public PointCollection NonHorizontalLayerPreviewPoints { get; } = new();

    /// <summary>Canvas-space dot markers for each clicked point of the in-progress non-horizontal layer.</summary>
    public ObservableCollection<Point> NonHorizontalLayerPreviewVertexMarkers { get; } = new();

    public bool IsNonHorizontalLayerDrawMode
    {
        get => _isNonHorizontalLayerDrawMode;
        private set { _isNonHorizontalLayerDrawMode = value; OnPropertyChanged(); }
    }

    private string _nonHorizontalLayerDrawStatus = "";
    public string NonHorizontalLayerDrawStatus
    {
        get => _nonHorizontalLayerDrawStatus;
        private set { _nonHorizontalLayerDrawStatus = value ?? ""; OnPropertyChanged(); }
    }

    // ── UI mode toggle: Horizontal vs Non-Horizontal ──────────────────────

    /// <summary>True when the Layering Controls GroupBox is in Horizontal mode (default).</summary>
    public bool IsHorizontalLayerMode
    {
        get => !_isNonHorizontalLayerModeSelected;
        set
        {
            if (!value || _isNonHorizontalLayerModeSelected == false) return;
            CancelNonHorizontalLayerDraw();
            _isNonHorizontalLayerModeSelected = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNonHorizontalLayerMode));
        }
    }

    /// <summary>True when the Layering Controls GroupBox is in Non-Horizontal mode.</summary>
    public bool IsNonHorizontalLayerMode
    {
        get => _isNonHorizontalLayerModeSelected;
        set
        {
            if (value == _isNonHorizontalLayerModeSelected) return;
            if (!value) CancelNonHorizontalLayerDraw();
            _isNonHorizontalLayerModeSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHorizontalLayerMode));
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────
    public ICommand StartNonHorizontalLayerDrawCommand { get; private set; } = null!;
    public ICommand FinishNonHorizontalLayerDrawCommand { get; private set; } = null!;
    public ICommand CancelNonHorizontalLayerDrawCommand { get; private set; } = null!;

    internal void InitNonHorizontalLayerCommands()
    {
        StartNonHorizontalLayerDrawCommand  = new RelayCommand(StartNonHorizontalLayerDraw);
        FinishNonHorizontalLayerDrawCommand = new RelayCommand(FinishNonHorizontalLayerDraw);
        CancelNonHorizontalLayerDrawCommand = new RelayCommand(CancelNonHorizontalLayerDraw);
    }

    // ── Drawing lifecycle ─────────────────────────────────────────────────

    public void StartNonHorizontalLayerDraw()
    {
        ClearPendingLayerPointsInternal();
        IsNonHorizontalLayerDrawMode = true;
        NonHorizontalLayerDrawStatus =
            "Click on the canvas or enter X,Y coordinates below. Click 'Add Layer' when done (≥ 2 points).";
        RefreshNonHorizontalLayerPreview();
    }

    public void CancelNonHorizontalLayerDraw()
    {
        ClearPendingLayerPointsInternal();
        IsNonHorizontalLayerDrawMode = false;
        NonHorizontalLayerDrawStatus = "";
        LayeringControlsStatus = "";
        RefreshNonHorizontalLayerPreview();
    }

    public void FinishNonHorizontalLayerDraw()
    {
        if (PendingLayerPoints.Count < 2)
        {
            var msg = "At least 2 points required — add coordinates or click on the canvas.";
            NonHorizontalLayerDrawStatus = msg;
            LayeringControlsStatus = msg;
            return;
        }

        AddLayerCutPolylineFromPendingPoints();
    }

    internal void AddPendingLayerPointAxis(Point axis)
    {
        if (!IsNonHorizontalLayerMode)
            return;

        var snapped = SnapGroundwaterAxisPoint(axis);
        IsNonHorizontalLayerDrawMode = true;

        PendingLayerPoints.Add(new LayerPendingPointDisplay(PendingLayerPoints.Count + 1, snapped.X, snapped.Y));
        var status =
            $"Point {PendingLayerPoints.Count} added at ({snapped.X:F2}, {snapped.Y:F2}). " +
            (PendingLayerPoints.Count < 2
                ? "Add at least one more point, then click Add Layer."
                : "Add more points or click Add Layer.");
        NonHorizontalLayerDrawStatus = status;
        LayeringControlsStatus = status;
        RefreshNonHorizontalLayerPreview();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Called from <see cref="Views.Controls.SlopeCanvasView"/> when a left-click fires in non-horizontal draw mode.</summary>
    public void HandleNonHorizontalLayerCanvasClick(Point canvasPoint)
    {
        if (!IsNonHorizontalLayerDrawMode) return;
        var axis = ConvertCanvasToAxis(canvasPoint);
        AddPendingLayerPointAxis(axis);
    }

    // ── Canvas overlays ───────────────────────────────────────────────────

    internal void RefreshNonHorizontalLayerPreview()
    {
        NonHorizontalLayerPreviewPoints.Clear();
        NonHorizontalLayerPreviewVertexMarkers.Clear();
        foreach (var point in PendingLayerPoints)
        {
            var canvas = ConvertAxisToCanvas(new Point(point.AxisX, point.AxisY));
            NonHorizontalLayerPreviewPoints.Add(canvas);
            NonHorizontalLayerPreviewVertexMarkers.Add(canvas);
        }
        OnPropertyChanged(nameof(NonHorizontalLayerPreviewPoints));
        OnPropertyChanged(nameof(NonHorizontalLayerPreviewVertexMarkers));
    }

    internal void RefreshNonHorizontalLayerCanvasLines()
    {
        // Always draw the finalized cut polylines (teal). Unlike the horizontal chords, these are
        // kept visible even in summaries mode so the user-drawn interface line shows on both the
        // Material Properties canvas and the Mesh canvas (which forwards this collection).
        NonHorizontalLayerCanvasLines.Clear();

        foreach (var cut in _nonHorizontalLayerCuts)
        {
            if (cut.Count < 2) continue;
            var pts = new PointCollection();
            foreach (var (x, y) in cut)
                pts.Add(ConvertAxisToCanvas(new Point(x, y)));
            NonHorizontalLayerCanvasLines.Add(new InternalEdgeCanvasLine { Points = pts, IsArc = false });
        }
    }

    /// <summary>Axis-space copies of the finalized non-horizontal layer cut polylines (≥ 2 points each),
    /// used by the mesh boundary discretizer to sample nodes along inclined material interfaces.</summary>
    public IReadOnlyList<IReadOnlyList<Point>> GetNonHorizontalLayerCutPolylines()
    {
        var polylines = new List<IReadOnlyList<Point>>();
        foreach (var cut in _nonHorizontalLayerCuts)
        {
            if (cut.Count < 2) continue;
            polylines.Add(cut.Select(p => new Point(p.X, p.Y)).ToList());
        }
        return polylines;
    }

    /// <summary>
    /// Axis-space zone polygons produced by the non-horizontal layer cuts, paired with the material id
    /// assigned to that display layer (null when unassigned). Used by the structured-mesh generators to
    /// resolve per-element etype by point-in-zone for inclined material interfaces.
    /// </summary>
    public IReadOnlyList<(int DisplayLayer, IReadOnlyList<Point> Polygon, int? MaterialId)> GetNonHorizontalLayerZones()
    {
        var zones = new List<(int, IReadOnlyList<Point>, int?)>();
        foreach (var kvp in _nonHorizontalLayerStripPolygons.OrderBy(k => k.Key))
        {
            var materialId = GetLayerMaterialAssignmentMaterialId(kvp.Key);
            foreach (var poly in kvp.Value)
            {
                if (poly == null || poly.Count < 3) continue;
                var pts = poly.Select(p => new Point(p.x, p.y)).ToList();
                zones.Add((kvp.Key, pts, materialId));
            }
        }
        return zones;
    }

    // ── State helpers ─────────────────────────────────────────────────────

    private bool ShouldUseNonHorizontalLayerSummaries() => _nonHorizontalLayerCuts.Count > 0;

    /// <summary>Maps a layering row display # to the non-horizontal cut whose removal merges that zone away.</summary>
    private bool TryGetNonHorizontalCutToRemove(
        int displayNumber,
        int horizontalRowCount,
        out List<(double X, double Y)>? cutToRemove)
    {
        cutToRemove = null;
        var zoneIndex = displayNumber - horizontalRowCount - 1;
        if (zoneIndex < 0)
            return false;

        var sortedCuts = _nonHorizontalLayerCuts
            .Where(c => c.Count >= 2)
            .OrderBy(c => c.Average(p => p.Y))
            .ToList();
        if (sortedCuts.Count == 0)
            return false;

        var zoneCount = sortedCuts.Count + 1;
        if (zoneIndex >= zoneCount)
            return false;

        var cutIndexInSorted = zoneIndex < zoneCount - 1 ? zoneIndex : sortedCuts.Count - 1;
        if (cutIndexInSorted < 0 || cutIndexInSorted >= sortedCuts.Count)
            return false;

        cutToRemove = sortedCuts[cutIndexInSorted];
        return cutToRemove != null;
    }

    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> NonHorizontalLayerCutsSnapshot =>
        _nonHorizontalLayerCuts
            .Select(c => (IReadOnlyList<(double X, double Y)>)c.ToList())
            .ToList();

    /// <summary>Restores finalized non-horizontal layer cut polylines after .xfem load.</summary>
    public void SetNonHorizontalLayerCutsFromRestore(IEnumerable<IReadOnlyList<(double X, double Y)>>? cuts)
    {
        ClearNonHorizontalLayerCuts();
        if (cuts == null) return;
        foreach (var cut in cuts)
        {
            if (cut == null || cut.Count < 2) continue;
            _nonHorizontalLayerCuts.Add(cut.Select(p => (p.X, p.Y)).ToList());
        }

        if (_nonHorizontalLayerCuts.Count == 0) return;
        RefreshNonHorizontalLayerCanvasLines();
        RefreshLayerCoordinateSummaries();
        RefreshSurfaceRegionOverlays();
    }

    internal void ClearNonHorizontalLayerCuts()
    {
        _nonHorizontalLayerCuts.Clear();
        ClearPendingLayerPointsInternal();
        _nonHorizontalLayerStripPolygons.Clear();
        IsNonHorizontalLayerDrawMode = false;
        NonHorizontalLayerDrawStatus = "";
        LayeringControlsStatus = "";
        NonHorizontalLayerCanvasLines.Clear();
        NonHorizontalLayerPreviewPoints.Clear();
        NonHorizontalLayerPreviewVertexMarkers.Clear();
        OnPropertyChanged(nameof(NonHorizontalLayerPreviewPoints));
        OnPropertyChanged(nameof(NonHorizontalLayerPreviewVertexMarkers));
        RefreshSurfaceRegionOverlays();
    }

    // ── Layer summary rows for DataGrid ──────────────────────────────────

    /// <summary>
    /// Builds one <see cref="LayerCoordinateSummary"/> row per zone created by the non-horizontal
    /// polyline cuts: N cuts → N+1 zones ordered bottom-to-top.
    /// <paramref name="startDisplayNumber"/> is the 1-based offset so numbers continue after horizontal rows.
    /// </summary>
    private bool TryBuildNonHorizontalLayerSummaries(int startDisplayNumber, out List<LayerCoordinateSummary> rows)
    {
        rows = new List<LayerCoordinateSummary>();
        _nonHorizontalLayerStripPolygons.Clear();
        if (GeometryVertexRows.Count < 3 || _nonHorizontalLayerCuts.Count == 0)
            return false;

        var eps = Math.Max(AxisEqualityTolerance, (AxisYMax - AxisYMin) * 1e-9);

        // Build boundary polygon with vertex labels
        var poly = GeometryVertexRows
            .Select(v => (x: v.AxisX, y: v.AxisY, label: v.PointLabel))
            .ToList();

        var (xMinPoly, xMaxPoly, yMinPoly, yMaxPoly) = GetBoundingBoxBounds();
        // Extend domain by a small margin so clip polygon fully covers the boundary
        var margin = Math.Max((xMaxPoly - xMinPoly) * 0.02, eps * 1000);
        var xMin = xMinPoly - margin;
        var xMax = xMaxPoly + margin;
        var yMin = yMinPoly - margin;
        var yMax = yMaxPoly + margin;

        // Sort cuts by average Y ascending (bottom-to-top layering)
        var sortedCuts = _nonHorizontalLayerCuts
            .Where(c => c.Count >= 2)
            .OrderBy(c => c.Average(p => p.Y))
            .ToList();

        if (sortedCuts.Count == 0) return false;

        // Extend each drawn cut along its end-segment direction to the model boundary so the
        // resulting zones divide the slope edge-to-edge instead of leaving triangular wedges.
        var extendedCuts = sortedCuts
            .Select(c => ExtendCutToBoundary(c, poly, xMin, xMax, eps))
            .ToList();

        var layerDisplay = startDisplayNumber;

        // Zone 0 — below the first cut
        AddNonHorizontalZone(
            poly,
            ClipBelow(extendedCuts[0], xMin, xMax, yMin, yMax),
            ref layerDisplay, rows, yMin, extendedCuts[0].Average(p => p.Y), eps);

        // Intermediate zones — between consecutive cuts
        for (var i = 1; i < extendedCuts.Count; i++)
        {
            var lowerCut = extendedCuts[i - 1];
            var upperCut = extendedCuts[i];

            var belowUpper = ClipBelow(upperCut, xMin, xMax, yMin, yMax);
            var aboveLower = ClipAbove(lowerCut, xMin, xMax, yMin, yMax);

            if (belowUpper.Count < 3 || aboveLower.Count < 3) continue;

            // Two-pass intersect: first below upper cut, then above lower cut. Each pass can
            // produce several disconnected pockets (e.g. a benched boundary), so intersect every
            // pocket from the first pass with the second region and keep them all in this zone.
            var step1Rings = IntersectAxisRegionAll(poly, belowUpper, eps);
            var zoneRings = new List<List<(double x, double y, string label)>>();
            foreach (var ring in step1Rings)
            {
                if (ring.Count < 3) continue;
                zoneRings.AddRange(IntersectAxisRegionAll(ring, aboveLower, eps));
            }

            AddNonHorizontalZoneFromClippedRings(
                zoneRings, ref layerDisplay, rows,
                lowerCut.Average(p => p.Y), upperCut.Average(p => p.Y), eps);
        }

        // Top zone — above the last cut
        AddNonHorizontalZone(
            poly,
            ClipAbove(extendedCuts[^1], xMin, xMax, yMin, yMax),
            ref layerDisplay, rows, extendedCuts[^1].Average(p => p.Y), yMax, eps);

        return rows.Count > 0;
    }

    private void AddNonHorizontalZone(
        List<(double x, double y, string label)> poly,
        List<(double x, double y, string label)> clipPoly,
        ref int layerDisplay,
        List<LayerCoordinateSummary> rows,
        double yLo, double yHi, double eps)
    {
        if (clipPoly.Count < 3) return;
        var rings = IntersectAxisRegionAll(poly, clipPoly, eps);
        AddNonHorizontalZoneFromClippedRings(rings, ref layerDisplay, rows, yLo, yHi, eps);
    }

    /// <summary>
    /// Robustly intersects <paramref name="subject"/> with a possibly-concave <paramref name="region"/>
    /// using a WPF geometry boolean (handles wavy/non-monotonic cut polylines, unlike the convex-only
    /// Sutherland-Hodgman clip). Returns EVERY resulting figure as an axis-space ring with blank labels
    /// (labels are re-derived downstream by vertex snapping). A single zone can be multiply-connected
    /// (e.g. an inclined cut over a benched boundary), so all pockets must be kept — returning only the
    /// largest figure would silently orphan boundary vertices that fall in the smaller pockets.
    /// </summary>
    private List<List<(double x, double y, string label)>> IntersectAxisRegionAll(
        IReadOnlyList<(double x, double y, string label)> subject,
        IReadOnlyList<(double x, double y, string label)> region,
        double eps)
        => CombineAxisRegions(subject, region, GeometryCombineMode.Intersect, eps);

    /// <summary>Subject minus region (used for ponded free water = below WT \ soil).</summary>
    private List<List<(double x, double y, string label)>> ExcludeAxisRegionAll(
        IReadOnlyList<(double x, double y, string label)> subject,
        IReadOnlyList<(double x, double y, string label)> region,
        double eps)
        => CombineAxisRegions(subject, region, GeometryCombineMode.Exclude, eps);

    private List<List<(double x, double y, string label)>> CombineAxisRegions(
        IReadOnlyList<(double x, double y, string label)> subject,
        IReadOnlyList<(double x, double y, string label)> region,
        GeometryCombineMode mode,
        double eps)
    {
        var result = new List<List<(double x, double y, string label)>>();
        if (subject.Count < 3 || region.Count < 3)
            return result;

        var subjectGeo = AxisRingToPathGeometry(subject);
        var regionGeo  = AxisRingToPathGeometry(region);
        if (subjectGeo == null || regionGeo == null)
            return result;

        Geometry combined = new CombinedGeometry(mode, subjectGeo, regionGeo);
        var flat = combined.GetFlattenedPathGeometry();

        foreach (var fig in flat.Figures)
        {
            var ring = FlattenedFigureToRing(fig);
            if (ring.Count < 3) continue;
            if (Math.Abs(SignedAxisRingArea(ring)) <= eps) continue;
            result.Add(ring);
        }
        return result;
    }

    private static PathGeometry? AxisRingToPathGeometry(IReadOnlyList<(double x, double y, string label)> ring)
    {
        if (ring.Count < 3) return null;
        var fig = new PathFigure { StartPoint = new Point(ring[0].x, ring[0].y), IsClosed = true };
        for (var i = 1; i < ring.Count; i++)
            fig.Segments.Add(new LineSegment(new Point(ring[i].x, ring[i].y), true));
        var pg = new PathGeometry { FillRule = FillRule.Nonzero };
        pg.Figures.Add(fig);
        pg.Freeze();
        return pg;
    }

    private static List<(double x, double y, string label)> FlattenedFigureToRing(PathFigure fig)
    {
        var ring = new List<(double x, double y, string label)> { (fig.StartPoint.X, fig.StartPoint.Y, "") };
        foreach (var seg in fig.Segments)
        {
            if (seg is PolyLineSegment poly)
            {
                foreach (var p in poly.Points)
                    ring.Add((p.X, p.Y, ""));
            }
            else if (seg is LineSegment line)
            {
                ring.Add((line.Point.X, line.Point.Y, ""));
            }
        }
        return ring;
    }

    private static double SignedAxisRingArea(IReadOnlyList<(double x, double y, string label)> ring)
    {
        var area = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            area += a.x * b.y - b.x * a.y;
        }
        return area * 0.5;
    }

    private void AddNonHorizontalZoneFromClipped(
        List<(double x, double y, string label)> clipped,
        ref int layerDisplay,
        List<LayerCoordinateSummary> rows,
        double yLo, double yHi, double eps)
        => AddNonHorizontalZoneFromClippedRings(
            new List<List<(double x, double y, string label)>> { clipped },
            ref layerDisplay, rows, yLo, yHi, eps);

    /// <summary>
    /// Emits a single display layer for a zone that may consist of several disconnected pockets.
    /// All pockets share one layer number / material assignment and one summary row; every pocket
    /// with real area is retained so no boundary vertex is orphaned (e.g. Point 6 on a benched slope).
    /// </summary>
    private void AddNonHorizontalZoneFromClippedRings(
        List<List<(double x, double y, string label)>> rings,
        ref int layerDisplay,
        List<LayerCoordinateSummary> rows,
        double yLo, double yHi, double eps)
    {
        var areaEps = Math.Max(eps, (AxisXMax - AxisXMin) * (AxisYMax - AxisYMin) * 1e-7);

        // Keep every non-sliver pocket, ordered largest first so the summary row's named corners and
        // the on-canvas label come from the dominant pocket.
        var kept = new List<List<(double x, double y, string label)>>();
        foreach (var ring in rings)
        {
            if (ring == null || ring.Count < 3) continue;
            SnapStripPointsToNearestVertices(ring, eps);
            if (Math.Abs(SignedAxisRingArea(ring)) <= areaEps) continue;
            kept.Add(ring);
        }
        if (kept.Count == 0) return;

        kept.Sort((a, b) => Math.Abs(SignedAxisRingArea(b)).CompareTo(Math.Abs(SignedAxisRingArea(a))));

        // Collect named corners across all pockets (dominant pocket first) for the summary row.
        var labels = new List<string>();
        foreach (var ring in kept)
            foreach (var lbl in DedupeConsecutivePolygonLabels(ring))
                if (!string.IsNullOrEmpty(lbl) && !labels.Contains(lbl))
                    labels.Add(lbl);

        layerDisplay++;
        _nonHorizontalLayerStripPolygons[layerDisplay] =
            kept.Select(r => r.Select(p => (p.x, p.y)).ToList()).ToList();
        var arr = new string[8];
        for (var j = 0; j < 8; j++)
            arr[j] = j < labels.Count ? labels[j] : "";

        var rep     = PickRepresentativeSegmentForLayer(yLo, yHi, eps);
        var repLine = rep?.LineNumber ?? layerDisplay;
        rows.Add(new LayerCoordinateSummary(layerDisplay, repLine, arr, rep, this, this));
    }

    // ── Clip polygon builders ─────────────────────────────────────────────

    // <paramref name="polyline"/> here is the boundary-extended cut: its first/last points sit on
    // (or just outside) the slope boundary, so the flat connectors to the domain edge fall outside
    // the polygon and are trimmed away by <see cref="IntersectAxisRegionAll"/>.
    private List<(double x, double y, string label)> ClipBelow(
        IReadOnlyList<(double X, double Y)> polyline,
        double xMin, double xMax, double yMin, double yMax)
    {
        var left  = polyline[0];
        var right = polyline[^1];
        var clip = new List<(double x, double y, string label)>
        {
            (xMin, yMin,    ""),
            (xMax, yMin,    ""),
            (xMax, right.Y, ""),
        };
        for (var i = polyline.Count - 1; i >= 0; i--)
            clip.Add((polyline[i].X, polyline[i].Y, ""));
        clip.Add((xMin, left.Y, ""));
        return clip;
    }

    private List<(double x, double y, string label)> ClipAbove(
        IReadOnlyList<(double X, double Y)> polyline,
        double xMin, double xMax, double yMin, double yMax)
    {
        var left  = polyline[0];
        var right = polyline[^1];
        var clip = new List<(double x, double y, string label)>
        {
            (xMin, left.Y, ""),
        };
        foreach (var p in polyline)
            clip.Add((p.X, p.Y, ""));
        clip.Add((xMax, right.Y, ""));
        clip.Add((xMax, yMax,    ""));
        clip.Add((xMin, yMax,    ""));
        return clip;
    }

    // ── Boundary extension of cut polylines ───────────────────────────────

    /// <summary>
    /// Extends a sorted-by-X cut polyline at both ends along the direction of its first/last segment
    /// until it reaches the slope polygon boundary, so the zones built from it divide the model
    /// edge-to-edge instead of leaving triangular wedges. When an end finds no boundary crossing
    /// (or its segment is near-vertical) it falls back to a directional extension to the domain edge,
    /// so the result is always a full-width cut.
    /// </summary>
    private List<(double X, double Y)> ExtendCutToBoundary(
        IReadOnlyList<(double X, double Y)> sortedCut,
        IReadOnlyList<(double x, double y, string label)> poly,
        double xMin, double xMax, double eps)
    {
        var result = new List<(double X, double Y)>(sortedCut);
        if (sortedCut.Count < 2) return result;

        // Only extend an end whose endpoint is strictly interior to the slope. When the drawn cut
        // already terminates on the boundary (e.g. the slope toe, which is a boundary vertex), any
        // outward ray would shoot PAST that corner and land elsewhere on the polygon, making the
        // interface visually "cross the toe" and end on the far edge. Such ends need no extension.
        var onBoundaryTol = Math.Max(eps, (xMax - xMin) * 1e-4);

        var leftEnd  = IsAxisPointOnPolygonBoundary(sortedCut[0], poly, onBoundaryTol)
            ? null
            : ExtendCutEnd(sortedCut[0],  sortedCut[1],   poly, xMin, eps);
        var rightEnd = IsAxisPointOnPolygonBoundary(sortedCut[^1], poly, onBoundaryTol)
            ? null
            : ExtendCutEnd(sortedCut[^1], sortedCut[^2],  poly, xMax, eps);

        if (leftEnd.HasValue)  result.Insert(0, leftEnd.Value);
        if (rightEnd.HasValue) result.Add(rightEnd.Value);
        return result;
    }

    /// <summary>
    /// True when <paramref name="pt"/> lies within <paramref name="tol"/> (axis units) of any edge of
    /// the closed polygon <paramref name="poly"/>, i.e. the point already sits on the model boundary.
    /// </summary>
    private static bool IsAxisPointOnPolygonBoundary(
        (double X, double Y) pt,
        IReadOnlyList<(double x, double y, string label)> poly,
        double tol)
    {
        var n = poly.Count;
        if (n < 2) return false;
        var p = new Point(pt.X, pt.Y);
        for (var i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            if (DistancePointToSegmentAxis(p, new Point(a.x, a.y), new Point(b.x, b.y), out _, out _) <= tol)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Computes the extension point for one end of a cut by shooting a ray outward from
    /// <paramref name="endPt"/> (away from <paramref name="neighbor"/>) and returning the first
    /// crossing with the polygon boundary. Falls back to a directional extension to
    /// <paramref name="domainX"/> when no crossing exists, or null when the end segment is degenerate.
    /// </summary>
    private static (double X, double Y)? ExtendCutEnd(
        (double X, double Y) endPt,
        (double X, double Y) neighbor,
        IReadOnlyList<(double x, double y, string label)> poly,
        double domainX,
        double eps)
    {
        var dx = endPt.X - neighbor.X;
        var dy = endPt.Y - neighbor.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-12) return null;
        var ux = dx / len;
        var uy = dy / len;

        if (TryRayPolygonFirstHit(endPt, ux, uy, poly, eps, out var hit))
            return hit;

        // Fallback: continue the end-segment slope out to the domain edge.
        if (Math.Abs(dx) < 1e-12) return null;
        var slope = dy / dx;
        return (domainX, endPt.Y + slope * (domainX - endPt.X));
    }

    /// <summary>
    /// Finds the nearest forward (t &gt; eps) intersection of the ray origin + t*(ux,uy) with any
    /// edge of the closed polygon <paramref name="poly"/>.
    /// </summary>
    private static bool TryRayPolygonFirstHit(
        (double X, double Y) origin, double ux, double uy,
        IReadOnlyList<(double x, double y, string label)> poly,
        double eps, out (double X, double Y) hit)
    {
        hit = default;
        var bestT = double.PositiveInfinity;
        var found = false;
        var n = poly.Count;
        for (var i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            if (TryRaySegmentIntersection(origin, ux, uy, (a.x, a.y), (b.x, b.y), out var t)
                && t > eps && t < bestT)
            {
                bestT = t;
                found = true;
            }
        }
        if (found)
            hit = (origin.X + ux * bestT, origin.Y + uy * bestT);
        return found;
    }

    /// <summary>
    /// Parametric ray/segment intersection. Ray is origin + t*(ux,uy); segment is a..b. Returns
    /// true and the ray distance <paramref name="t"/> when they cross within the segment span.
    /// </summary>
    private static bool TryRaySegmentIntersection(
        (double X, double Y) origin, double ux, double uy,
        (double X, double Y) a, (double X, double Y) b,
        out double t)
    {
        t = 0;
        var ex = b.X - a.X;
        var ey = b.Y - a.Y;
        var denom = ux * ey - uy * ex;
        if (Math.Abs(denom) < 1e-12) return false; // parallel / degenerate
        var ax = a.X - origin.X;
        var ay = a.Y - origin.Y;
        t = (ax * ey - ay * ex) / denom;
        var u = (ax * uy - ay * ux) / denom;
        const double uEps = 1e-9;
        return t >= 0 && u >= -uEps && u <= 1 + uEps;
    }
}
