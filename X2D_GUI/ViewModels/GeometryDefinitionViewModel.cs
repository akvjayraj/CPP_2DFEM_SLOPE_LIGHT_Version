using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services;
using GeotechSlopeApp.Services.GeometryImport;
using GeotechSlopeApp.ViewModels.Dialogs;
using GeotechSlopeApp.Views.Dialogs;
using Microsoft.Win32;

namespace GeotechSlopeApp.ViewModels;

public partial class GeometryDefinitionViewModel : INotifyPropertyChanged, ILayerDisplayNameStore, ILayerDisplayColorStore
{
    private const double DefaultCanvasWidth = 600.0;
    private const double DefaultCanvasHeight = 400.0;
    private const double DefaultAxisMinX = 0.0;
    private const double DefaultAxisMaxX = 100.0;
    private const double DefaultAxisMinY = 0.0;
    private const double DefaultAxisMaxY = 60.0;
    private const double DefaultGridSpacing = 10.0;
    private IProjectUndoHost? _undoHost;
    private const double MinGridPixelSpacing = 8.0;
    /// <summary>Target minimum major-tick spacing (px). Slightly below classic CAD density so small Viewbox panels still show several labels.</summary>
    private const double MinAxisTickPixelSpacing = 48.0;
    private const double MaxAxisTickPixelSpacing = 160.0;
    private const double TargetAxisTickPixelSpacing = 72.0;
    private const int MinimumAxisTickCount = 3;
    private const int MaxAxisTickIterations = 2048;
    private const int MaxAxisDecimalPlaces = 6;
    private const int MinorTickDivisionsPerMajor = 10;
    private const double MajorTickLength = 6.0;
    private const double MinorTickLength = 3.0;
    private const double HorizontalMinorTickLength = 4.0;
    private const double OriginAxisArrowLength = 36.0;
    private const double OriginAxisWorldInset = 2.0;
    private const double AxisEqualityTolerance = 1e-6;
    /// <summary>When stitching arc samples into a polygon, treat near-coincident canvas points as one.</summary>
    private const double SurfaceOverlayPointMergeEps = 0.75;

    private double _canvasWidth = DefaultCanvasWidth;
    private double _canvasHeight = DefaultCanvasHeight;
    private double _pendingLogicalCanvasWidth = DefaultCanvasWidth;
    private double _pendingLogicalCanvasHeight = DefaultCanvasHeight;
    private bool _logicalCanvasResizeQueued;
    private double _axisXMin = DefaultAxisMinX;
    private double _axisXMax = DefaultAxisMaxX;
    private double _axisYMin = DefaultAxisMinY;
    private double _axisYMax = DefaultAxisMaxY;
    private double _gridSpacing = DefaultGridSpacing;
    private bool _showGrid;
    private bool _showAxes = true;
    private double _currentZoomFactor = 1.0;
    private Matrix _viewMatrix = Matrix.Identity;
    private string _geometryCanvasCoordinates = "";
    private string _gridSpacingDescription = "";
    private double _tunnelCirclePreviewCenterX;
    private double _tunnelCirclePreviewCenterY;
    private double _tunnelCirclePreviewRadius = double.NaN;
    private double _tunnelCirclePreviewDiameter;
    private double _tunnelCirclePreviewNegativeRadius;
    private bool _isTunnelCirclePreviewVisible;
    private string _tunnelCirclePreviewRadiusText = "";
    private bool _isBoxMethodCircleVisible;
    private string _layerElevationInput = "";
    private string _layerPointXInput = "";
    private string _layerPointYInput = "";
    private string _layeringControlsStatus = "";
    private LayerPendingPointDisplay? _selectedPendingLayerPoint;
    private string _tunnelCenterXInput = "";
    private string _tunnelCenterYInput = "";
    private string _tunnelRadiusInput = "";
    private string _tunnelCircleDrawingStatus = "";
    private string _geometryXInput = "";
    private string _geometryYInput = "";
    private int? _highlightedLineNumber;
    private GeometrySurface? _selectedGeometrySurface;
    private BooleanOperationRow? _selectedExcavationOperation;
    private bool _hasAddedElevationLayer;
    /// <summary>Elevations where the user explicitly added a horizontal material layer (Material Properties → Add layer). Used for canvas overlay only.</summary>
    private readonly List<double> _materialLayerCutElevations = new();
    private int _nextStablePointId = 1;
    private int _boundarySegmentCount;
    private string _geometryImportStatusMessage = "";
    private MeshViewModel? _meshSource;
    private ProjectSettingsViewModel? _projectSettings;
    private string _sharedEdgeFromPointLabel = "";
    private string _sharedEdgeToPointLabel = "";
    private bool _suppressSurfaceGeometryRotation;
    private bool _isOpenPolygonBoundaryMode;
    /// <summary>When set, canvas shows only this boolean result instead of individual surface fills (Excavate).</summary>
    private Geometry? _surfaceBooleanResultGeometry;
    private string _surfaceBooleanDisplayName = "";
    /// <summary>Material Properties: when set, <see cref="RefreshSurfaceRegionOverlays"/> uses <see cref="_materialAssignmentPreviewFill"/> for that surface instead of the default palette.</summary>
    private int? _materialAssignmentPreviewSurfaceNumber;
    private Brush? _materialAssignmentPreviewFill;

    /// <summary>Persistent surface→material fill overrides painted by the Assign button in the Materials grid.</summary>
    private readonly Dictionary<int, Brush> _surfaceMaterialFills = new();
    /// <summary>Persistent surface→material id painted by the Assign button (parallel to <see cref="_surfaceMaterialFills"/>).
    /// Used by .xfem persistence so reloaded projects can re-link a fill to its source material.</summary>
    private readonly Dictionary<int, int> _surfaceMaterialIds = new();
    /// <summary>Last-known AppliedInStageNumber per boolean operation row, used to move excavations between stages.</summary>
    private readonly Dictionary<BooleanOperationRow, int> _excavationRowStageSnapshot = new();
    /// <summary>True while the user has clicked an Assign button and is about to click a canvas surface.</summary>
    private bool _isMaterialAssignMode;
    private bool _isBulkPointDeleteMode;
    private string _bulkPointDeleteStatus = "";
    private bool _isBulkPointDeleteSelectionVisible;
    private double _bulkPointDeleteSelectionLeft;
    private double _bulkPointDeleteSelectionTop;
    private double _bulkPointDeleteSelectionWidth;
    private double _bulkPointDeleteSelectionHeight;
    /// <summary>Fired by <see cref="AssignMaterialAtPoint"/> when a surface is hit; argument is the surface number.</summary>
    public Action<int>? SurfaceClickedInAssignMode;
    /// <summary>Fired by <see cref="AssignMaterialAtPoint"/> when a material layer strip is hit (Layer Inputs workflow).</summary>
    public Action<int>? LayerClickedInAssignMode;

    internal bool HasAddedElevationLayer { get => _hasAddedElevationLayer; set => _hasAddedElevationLayer = value; }

    /// <summary>Axis equality tolerance for layering sync helpers.</summary>
    public double AxisEqualityTolerancePublic => AxisEqualityTolerance;

    internal IReadOnlyList<double> MaterialLayerCutElevationsSnapshot => _materialLayerCutElevations;

    /// <summary>Explicit horizontal layer cut elevations (from UI or script etype sync).</summary>
    public IReadOnlyList<double> MaterialLayerCutElevations => _materialLayerCutElevations;

    public void SetUndoHost(IProjectUndoHost? host) => _undoHost = host;

    private void RecordUndo(string description) => _undoHost?.RecordUndoPoint(description);

    private int _geometryBulkUpdateDepth;

    /// <summary>Suppresses repeated axis/grid/canvas overlay rebuilds during bulk model restore (undo/redo, .xfem apply).</summary>
    public bool IsGeometryBulkUpdate => _geometryBulkUpdateDepth > 0;

    public IDisposable BeginGeometryBulkUpdate()
    {
        _geometryBulkUpdateDepth++;
        return new GeometryBulkUpdateScope(this);
    }

    private sealed class GeometryBulkUpdateScope : IDisposable
    {
        private GeometryDefinitionViewModel? _owner;

        public GeometryBulkUpdateScope(GeometryDefinitionViewModel owner) => _owner = owner;

        public void Dispose()
        {
            if (_owner == null) return;
            _owner._geometryBulkUpdateDepth = Math.Max(0, _owner._geometryBulkUpdateDepth - 1);
            _owner = null;
        }
    }

    /// <summary>
    /// Replaces explicit horizontal layer cuts from script <c>etype</c> inference and rebuilds layering summaries.
    /// </summary>
    public void ApplyInferredMaterialLayerCuts(IReadOnlyList<double> cutYs)
    {
        _materialLayerCutElevations.Clear();
        var (_, _, yMin, yMax) = GetBoundingBoxBounds();
        var eps = AxisEqualityTolerance;
        foreach (var y in cutYs.OrderBy(v => v))
        {
            if (y <= yMin + eps || y >= yMax - eps)
                continue;
            if (_materialLayerCutElevations.Count > 0 && Math.Abs(y - _materialLayerCutElevations[^1]) <= eps)
                continue;
            _materialLayerCutElevations.Add(y);
        }

        foreach (var cutY in _materialLayerCutElevations.OrderBy(y => y))
            TryInsertHorizontalLayerCutVertices(cutY);

        _hasAddedElevationLayer = _materialLayerCutElevations.Count > 0;
        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments(resyncLayerCutChordEdges: true);
    }

    /// <summary>
    /// Example 2 two-layer preset: one horizontal cut with weak material below and strong material above.
    /// </summary>
    public void ApplyEx2TwoLayerLayerPreset(double layerCutY = 20.0)
    {
        ApplyInferredMaterialLayerCuts(new[] { layerCutY });
        SetLayerMaterialAssignmentMaterialId(1, 2);
        SetLayerMaterialAssignmentMaterialId(2, 1);
        OnPropertyChanged(nameof(MaterialLayerCutElevations));
    }

    /// <summary>
    /// Creates <see cref="GeometryInternalEdges"/> for horizontal layer-cut chords so they appear as L1, L2, …
    /// in <see cref="GeometryLineSegments"/> alongside boundary edges.
    /// </summary>
    /// <param name="replaceExistingLayerCutChordEdges">
    /// When true (etype/cut re-sync), remove auto layer chords and rebuild from widest-span algorithm.
    /// When false (project load, bulk delete, draw), keep edges restored from <c>.xfem</c> or user edits.
    /// </param>
    private void SyncMaterialLayerCutInternalEdges(bool replaceExistingLayerCutChordEdges = false)
    {
        var eps = AxisEqualityTolerance;
        var n = GeometryVertexRows.Count;
        if (n < 3)
            return;

        if (replaceExistingLayerCutChordEdges)
            RemoveAllHorizontalMaterialLayerCutInternalEdges(eps);

        if (_materialLayerCutElevations.Count == 0)
            return;

        var poly = GeometryVertexRows.Select(v => (x: v.AxisX, y: v.AxisY)).ToList();
        var xSnap = Math.Max(eps, ComputeAxisHitTolerance() * 2);

        foreach (var cutY in _materialLayerCutElevations.OrderBy(y => y))
        {
            if (!TryGetWidestHorizontalCutSpan(poly, n, cutY, eps, out var xLo, out var xHi))
                continue;

            if (!TryFindLayerCutVertexAt(cutY, xLo, xSnap, eps, out var va) ||
                !TryFindLayerCutVertexAt(cutY, xHi, xSnap, eps, out var vb))
                continue;

            var min = Math.Min(va!.StablePointId, vb!.StablePointId);
            var max = Math.Max(va.StablePointId, vb.StablePointId);
            if (GeometryInternalEdges.Any(e => !e.IsArc && e.StableMin == min && e.StableMax == max))
                continue;

            GeometryInternalEdges.Add(new GeometryInternalEdge(va.StablePointId, vb.StablePointId));
        }
    }

    /// <summary>Removes horizontal internal chords created for material layer cuts (including orphaned cuts after delete).</summary>
    private void RemoveAllHorizontalMaterialLayerCutInternalEdges(double eps)
    {
        for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
        {
            var ie = GeometryInternalEdges[i];
            if (ie.IsArc) continue;
            var va = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMin);
            var vb = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMax);
            if (va == null || vb == null) continue;
            if (Math.Abs(va.AxisY - vb.AxisY) > eps) continue;
            GeometryInternalEdges.RemoveAt(i);
        }
    }

    /// <summary>
    /// When no explicit horizontal layer cuts remain, drop orphaned full-width dashed chords left on the
    /// Material Properties canvas (Delete is disabled once the layering grid is empty).
    /// </summary>
    private void RemoveOrphanedFullWidthHorizontalLayerCutEdges()
    {
        if (_materialLayerCutElevations.Count > 0)
            return;

        var eps = AxisEqualityTolerance;
        var n = GeometryVertexRows.Count;
        if (n < 3 || GeometryInternalEdges.Count == 0)
            return;

        var poly = GeometryVertexRows.Select(v => (x: v.AxisX, y: v.AxisY)).ToList();
        var xSnap = Math.Max(eps, ComputeAxisHitTolerance() * 2);
        var removed = false;

        for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
        {
            var ie = GeometryInternalEdges[i];
            if (ie.IsArc) continue;
            var va = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMin);
            var vb = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMax);
            if (va == null || vb == null) continue;
            if (Math.Abs(va.AxisY - vb.AxisY) > eps) continue;

            var cutY = (va.AxisY + vb.AxisY) * 0.5;
            if (!TryGetWidestHorizontalCutSpan(poly, n, cutY, eps, out var xLo, out var xHi))
                continue;

            var edgeLo = Math.Min(va.AxisX, vb.AxisX);
            var edgeHi = Math.Max(va.AxisX, vb.AxisX);
            if (Math.Abs(edgeLo - xLo) > xSnap || Math.Abs(edgeHi - xHi) > xSnap)
                continue;

            GeometryInternalEdges.RemoveAt(i);
            removed = true;
        }

        if (removed)
            RefreshInternalEdgeCanvasLines();
    }

    /// <summary>
    /// Removes vertices inserted by Add Layer at (or near) <paramref name="cutY"/> so canvas point
    /// labels matching deleted dashed chords disappear with the layer.
    /// </summary>
    private void RemoveMaterialLayerCutVerticesNearElevation(double cutY)
    {
        var eps = AxisEqualityTolerance;
        var activeCuts = _materialLayerCutElevations;
        var toRemove = new List<int>();

        for (var i = 0; i < GeometryVertexRows.Count; i++)
        {
            var v = GeometryVertexRows[i];
            if (Math.Abs(v.AxisY - cutY) > eps)
                continue;

            // Prefer tagged Add Layer vertices; also catch unmarked cut stubs from older sessions
            // when no remaining active cut uses this elevation.
            if (!v.IsMaterialLayerCutVertex &&
                activeCuts.Any(c => Math.Abs(c - v.AxisY) <= eps))
                continue;

            if (!v.IsMaterialLayerCutVertex)
            {
                // Only remove unmarked vertices that sit on a remaining polygon edge interior
                // at this elevation (typical Add Layer insertions), not original corners that
                // happen to share a Y with a cut.
                if (!IsInteriorBoundaryVertexAtElevation(i, cutY, eps))
                    continue;
            }

            toRemove.Add(i);
        }

        if (toRemove.Count == 0)
            return;

        RemoveVerticesAtIndicesSilent(toRemove);
    }

    /// <summary>
    /// When no horizontal cuts remain, remove all Add Layer-tagged cut vertices (and orphaned
    /// unmarked cut stubs on the full-width chord elevations).
    /// </summary>
    /// <returns>True if any vertex was removed.</returns>
    private bool RemoveOrphanedMaterialLayerCutVertices()
    {
        if (_materialLayerCutElevations.Count > 0)
            return false;

        var toRemove = new List<int>();
        for (var i = 0; i < GeometryVertexRows.Count; i++)
        {
            var v = GeometryVertexRows[i];
            // Only remove vertices explicitly created as horizontal layer-cut insertions.
            // A previous heuristic also removed "unmarked leftovers" on the widest horizontal
            // span, but that wrongly deleted ordinary boundary vertices sitting on a horizontal
            // edge (e.g. a slope crest/base), which made it impossible to create more than two
            // points on any geometry with a horizontal edge.
            if (v.IsMaterialLayerCutVertex)
                toRemove.Add(i);
        }

        if (toRemove.Count == 0)
            return false;

        RemoveVerticesAtIndicesSilent(toRemove);
        return true;
    }

    private bool IsInteriorBoundaryVertexAtElevation(int index, double cutY, double eps)
    {
        var n = GeometryVertexRows.Count;
        if (n < 3 || index < 0 || index >= n)
            return false;

        var v = GeometryVertexRows[index];
        if (Math.Abs(v.AxisY - cutY) > eps)
            return false;

        var prev = GeometryVertexRows[(index - 1 + n) % n];
        var next = GeometryVertexRows[(index + 1) % n];

        // Cut insertions lie between neighbors that straddle (or meet) the cut Y, and are not sharp corners.
        var straddles =
            (prev.AxisY < cutY - eps && next.AxisY > cutY + eps) ||
            (prev.AxisY > cutY + eps && next.AxisY < cutY - eps) ||
            (Math.Abs(prev.AxisY - cutY) <= eps && Math.Abs(next.AxisY - cutY) > eps) ||
            (Math.Abs(next.AxisY - cutY) <= eps && Math.Abs(prev.AxisY - cutY) > eps);

        return straddles;
    }

    /// <summary>Removes vertices without recording a separate undo (caller already recorded).</summary>
    private void RemoveVerticesAtIndicesSilent(IEnumerable<int> indices)
    {
        var ordered = indices
            .Where(i => i >= 0 && i < GeometryVertexRows.Count)
            .Distinct()
            .OrderByDescending(i => i)
            .ToList();
        if (ordered.Count == 0)
            return;

        var removedStableIds = new HashSet<int>();
        foreach (var i in ordered)
        {
            removedStableIds.Add(GeometryVertexRows[i].StablePointId);
            GeometryVertexRows.RemoveAt(i);
        }

        for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
        {
            if (removedStableIds.Any(id => GeometryInternalEdges[i].TouchesStableId(id)))
                GeometryInternalEdges.RemoveAt(i);
        }

        for (var i = 0; i < GeometryVertexRows.Count; i++)
            GeometryVertexRows[i].Index = i;
    }

    private bool TryFindLayerCutVertexAt(
        double cutY,
        double x,
        double xSnap,
        double yEps,
        out GeometryVertexDisplay? vertex)
    {
        vertex = null;
        double bestDx = double.MaxValue;
        foreach (var v in GeometryVertexRows)
        {
            if (Math.Abs(v.AxisY - cutY) > yEps)
                continue;
            var dx = Math.Abs(v.AxisX - x);
            if (dx < bestDx)
            {
                bestDx = dx;
                vertex = v;
            }
        }

        return vertex != null && bestDx <= xSnap;
    }

    /// <summary>
    /// Inserts vertices on the outer polygon where a horizontal line at <paramref name="axisElevation"/> crosses edges.
    /// Same geometry as Material Properties → Add Layer.
    /// </summary>
    private bool TryInsertHorizontalLayerCutVertices(double axisElevation)
    {
        if (axisElevation <= AxisYMin + AxisEqualityTolerance || axisElevation >= AxisYMax - AxisEqualityTolerance)
            return false;

        var count = GeometryVertexRows.Count;
        if (count < 2)
            return false;

        var isClosed = ShouldAutoCloseBoundary(count);
        var insertions = new List<(int insertIndex, double x)>();
        for (int i = 0; i < count; i++)
        {
            var next = isClosed ? (i + 1) % count : i + 1;
            if (next >= count) break;

            var v1 = GeometryVertexRows[i];
            var v2 = GeometryVertexRows[next];
            var y1 = v1.AxisY;
            var y2 = v2.AxisY;

            if (Math.Abs(y1 - axisElevation) < AxisEqualityTolerance ||
                Math.Abs(y2 - axisElevation) < AxisEqualityTolerance)
                continue;

            var straddles = (y1 < axisElevation && axisElevation < y2) ||
                            (y2 < axisElevation && axisElevation < y1);
            if (!straddles) continue;

            var t = (axisElevation - y1) / (y2 - y1);
            var x = v1.AxisX + t * (v2.AxisX - v1.AxisX);
            insertions.Add((next == 0 ? count : next, x));
        }

        if (insertions.Count == 0)
            return false;

        foreach (var (insertIndex, x) in insertions.OrderByDescending(ins => ins.insertIndex))
        {
            var display = new GeometryVertexDisplay { Index = insertIndex, AxisX = x, AxisY = axisElevation };
            display.StablePointId = AllocateStablePointId();
            display.IsMaterialLayerCutVertex = true;
            display.Owner = this;
            GeometryVertexRows.Insert(insertIndex, display);
        }

        for (int j = 0; j < GeometryVertexRows.Count; j++)
            GeometryVertexRows[j].Index = j;

        return true;
    }

    /// <summary>Bottom and top Y of a horizontal material strip (display layer #, 1 = bottom).</summary>
    public bool TryGetHorizontalStripElevationRange(int displayLayerNumber, out double yLo, out double yHi)
    {
        yLo = yHi = 0;
        if (!TryCollectHorizontalBandElevations(out var merged) || merged.Count < 2)
            return false;

        var eps = AxisEqualityTolerance;
        var layerDisplay = 0;
        for (var i = 0; i < merged.Count - 1; i++)
        {
            var lo = merged[i];
            var hi = merged[i + 1];
            if (hi - lo < eps)
                continue;
            layerDisplay++;
            if (layerDisplay != displayLayerNumber)
                continue;
            yLo = lo;
            yHi = hi;
            return true;
        }

        return false;
    }

    /// <summary>Display layer # (1 = bottom) containing <paramref name="axisY"/> between material layer cuts.</summary>
    public bool TryGetDisplayLayerNumberAtAxisY(double axisY, out int displayLayerNumber)
    {
        displayLayerNumber = 0;
        if (!TryCollectHorizontalBandElevations(out var merged) || merged.Count < 2)
            return false;

        var eps = AxisEqualityTolerance;
        var layerDisplay = 0;
        for (var i = 0; i < merged.Count - 1; i++)
        {
            var lo = merged[i];
            var hi = merged[i + 1];
            if (hi - lo < eps)
                continue;
            layerDisplay++;
            var isTopBand = i == merged.Count - 2;
            if (axisY >= lo - eps && (isTopBand ? axisY <= hi + eps : axisY < hi - eps))
            {
                displayLayerNumber = layerDisplay;
                return true;
            }
        }

        if (axisY < merged[0])
        {
            displayLayerNumber = 1;
            return true;
        }

        displayLayerNumber = Math.Max(1, layerDisplay);
        return layerDisplay > 0;
    }

    internal void SetMaterialLayerCutElevationsFromRestore(IEnumerable<double>? elevations)
    {
        _materialLayerCutElevations.Clear();
        if (elevations != null)
            _materialLayerCutElevations.AddRange(elevations);
        _hasAddedElevationLayer = _materialLayerCutElevations.Count > 0;
    }

    /// <summary>Material library assignment per layering row (display layer # from Layering Controls), keyed by display layer number.</summary>
    private readonly Dictionary<int, int> _layerMaterialAssignmentByDisplayNumber = new();

    /// <summary>Project-material ID assigned to the given layering row, or null if none.</summary>
    public int? GetLayerMaterialAssignmentMaterialId(int displayLayerNumber) =>
        _layerMaterialAssignmentByDisplayNumber.TryGetValue(displayLayerNumber, out var id) ? id : null;

    /// <summary>Incremented when layer assignments or strip colors change (mesh canvas listens).</summary>
    public int LayerMaterialVisualRevision { get; private set; }

    public void SetLayerMaterialAssignmentMaterialId(int displayLayerNumber, int? materialId)
    {
        if (materialId == null)
            _layerMaterialAssignmentByDisplayNumber.Remove(displayLayerNumber);
        else
            _layerMaterialAssignmentByDisplayNumber[displayLayerNumber] = materialId.Value;
        BumpLayerMaterialVisualRevision();
    }

    public void ClearLayerMaterialAssignments()
    {
        _layerMaterialAssignmentByDisplayNumber.Clear();
        BumpLayerMaterialVisualRevision();
    }

    public IReadOnlyDictionary<int, int> LayerMaterialAssignmentsSnapshot => _layerMaterialAssignmentByDisplayNumber;

    public void ReplaceLayerMaterialAssignmentsFrom(IReadOnlyDictionary<int, int>? map)
    {
        _layerMaterialAssignmentByDisplayNumber.Clear();
        if (map == null) return;
        foreach (var kv in map)
            _layerMaterialAssignmentByDisplayNumber[kv.Key] = kv.Value;
        BumpLayerMaterialVisualRevision();
    }

    private void BumpLayerMaterialVisualRevision()
    {
        LayerMaterialVisualRevision++;
        OnPropertyChanged(nameof(LayerMaterialVisualRevision));
        OnPropertyChanged(nameof(LayerMaterialAssignmentsSnapshot));
    }

    /// <summary>
    /// Inserts vertices on horizontal layer cuts and rebuilds internal chord edges so RS2D discretize/mesh
    /// can honor material interfaces from Geometry Definition + Material Properties.
    /// </summary>
    public void PrepareMaterialLayerBoundariesForMesh()
    {
        if (_materialLayerCutElevations.Count == 0 || GeometryVertexRows.Count < 3)
            return;

        foreach (var cutY in _materialLayerCutElevations.OrderBy(y => y).ToList())
            TryInsertHorizontalLayerCutVertices(cutY);

        SyncMaterialLayerCutInternalEdges(replaceExistingLayerCutChordEdges: true);
        RefreshGeometryLineSegments(resyncLayerCutChordEdges: true);
    }

    /// <summary>
    /// Widest-span horizontal chords at each explicit material layer cut (model axis coordinates).
    /// </summary>
    public IReadOnlyList<(Point A, Point B)> CollectMaterialLayerBoundarySegments()
    {
        var segments = new List<(Point, Point)>();
        if (_materialLayerCutElevations.Count == 0 || GeometryVertexRows.Count < 3)
            return segments;

        PrepareMaterialLayerBoundariesForMesh();

        var eps = AxisEqualityTolerance;
        var poly = GeometryVertexRows.Select(v => (x: v.AxisX, y: v.AxisY)).ToList();
        int n = poly.Count;

        foreach (var cutY in _materialLayerCutElevations.OrderBy(y => y))
        {
            if (!TryGetWidestHorizontalCutSpan(poly, n, cutY, eps, out var xLo, out var xHi))
                continue;
            segments.Add((new Point(xLo, cutY), new Point(xHi, cutY)));
        }

        return segments;
    }

    /// <summary>
    /// Widest-span horizontal chords using dense mesh polygons (arc-sampled surfaces) so layer
    /// constraint endpoints match the meshed slope boundary rather than the coarse vertex ring.
    /// </summary>
    public IReadOnlyList<(Point A, Point B)> CollectMaterialLayerBoundarySegmentsForMeshes(
        IEnumerable<IReadOnlyList<Point>> meshPolygons)
    {
        var segments = new List<(Point, Point)>();
        if (_materialLayerCutElevations.Count == 0)
            return segments;

        PrepareMaterialLayerBoundariesForMesh();

        var polys = meshPolygons?
            .Where(p => p != null && p.Count >= 3)
            .Select(p => p.Select(v => (x: v.X, y: v.Y)).ToList())
            .ToList() ?? new List<List<(double x, double y)>>();

        if (polys.Count == 0 && GeometryVertexRows.Count >= 3)
        {
            return CollectMaterialLayerBoundarySegments();
        }

        var eps = AxisEqualityTolerance;
        foreach (var cutY in _materialLayerCutElevations.OrderBy(y => y))
        {
            double xLo = double.PositiveInfinity;
            double xHi = double.NegativeInfinity;
            bool any = false;
            foreach (var poly in polys)
            {
                if (!TryGetWidestHorizontalCutSpan(poly, poly.Count, cutY, eps, out var lo, out var hi))
                    continue;
                xLo = Math.Min(xLo, lo);
                xHi = Math.Max(xHi, hi);
                any = true;
            }

            if (any && xHi - xLo > eps)
                segments.Add((new Point(xLo, cutY), new Point(xHi, cutY)));
        }

        return segments;
    }
    private readonly Dictionary<int, string> _layerDisplayNameByDisplayNumber = new();

    /// <inheritdoc />
    public string GetLayerDisplayName(int displayLayerNumber, string? segmentTagFallback)
    {
        if (_layerDisplayNameByDisplayNumber.TryGetValue(displayLayerNumber, out var name) && !string.IsNullOrWhiteSpace(name))
            return name.Trim();
        if (!string.IsNullOrWhiteSpace(segmentTagFallback))
            return segmentTagFallback.Trim();
        return $"Layer {displayLayerNumber}";
    }

    /// <inheritdoc />
    public void SetLayerDisplayName(int displayLayerNumber, string value)
    {
        var v = value?.Trim() ?? "";
        if (string.IsNullOrEmpty(v))
            v = $"Layer {displayLayerNumber}";
        _layerDisplayNameByDisplayNumber[displayLayerNumber] = v;
    }

    public void ClearLayerDisplayNames() => _layerDisplayNameByDisplayNumber.Clear();

    public IReadOnlyDictionary<int, string> LayerDisplayNamesSnapshot => _layerDisplayNameByDisplayNumber;

    public void ReplaceLayerDisplayNamesFrom(IReadOnlyDictionary<int, string>? map)
    {
        _layerDisplayNameByDisplayNumber.Clear();
        if (map == null) return;
        foreach (var kv in map)
            _layerDisplayNameByDisplayNumber[kv.Key] = kv.Value;
    }

    /// <summary>ARGB per layering row for swatches and strip highlight.</summary>
    private readonly Dictionary<int, uint> _layerDisplayColorArgb = new();

    /// <summary>ComboBox palette in Layering Controls (same defaults as <see cref="GetLayerDisplayColor"/>).</summary>
    public IReadOnlyList<Color> MaterialLayerColorPalette => MaterialLayerDefaultPalette;

    private static readonly Color[] MaterialLayerDefaultPalette =
    {
        Color.FromRgb(59, 130, 246),
        Color.FromRgb(34, 197, 94),
        Color.FromRgb(234, 179, 8),
        Color.FromRgb(239, 68, 68),
        Color.FromRgb(168, 85, 247),
        Color.FromRgb(236, 72, 153),
        Color.FromRgb(20, 184, 166),
        Color.FromRgb(249, 115, 22),
        Color.FromRgb(99, 102, 241),
        Color.FromRgb(132, 204, 22),
        Color.FromRgb(14, 165, 233),
        Color.FromRgb(100, 116, 139)
    };

    /// <inheritdoc />
    public Color GetLayerDisplayColor(int displayLayerNumber)
    {
        if (_layerDisplayColorArgb.TryGetValue(displayLayerNumber, out var argb))
            return ColorFromArgb(argb);
        var idx = Math.Max(0, displayLayerNumber - 1) % MaterialLayerDefaultPalette.Length;
        return MaterialLayerDefaultPalette[idx];
    }

    /// <inheritdoc />
    public void SetLayerDisplayColor(int displayLayerNumber, Color value)
    {
        _layerDisplayColorArgb[displayLayerNumber] = ColorToArgb(value);
        var summary = LayerCoordinateSummaries.FirstOrDefault(s => s.DisplayLayerNumber == displayLayerNumber);
        summary?.NotifyLayerColorChangedFromStore();
        BumpLayerMaterialVisualRevision();
        RefreshSurfaceRegionOverlays();
    }

    public void ClearLayerDisplayColors() => _layerDisplayColorArgb.Clear();

    public IReadOnlyDictionary<int, uint> LayerDisplayColorsSnapshot => _layerDisplayColorArgb;

    public void ReplaceLayerDisplayColorsFrom(IReadOnlyDictionary<int, uint>? map)
    {
        _layerDisplayColorArgb.Clear();
        if (map == null) return;
        foreach (var kv in map)
            _layerDisplayColorArgb[kv.Key] = kv.Value;
    }

    private static uint ColorToArgb(Color c) =>
        ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private static Color ColorFromArgb(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>Material Properties: highlight one horizontal strip when a Layering Controls row is selected.</summary>
    private int? _materialLayerStripHighlightDisplayNumber;

    public void SetMaterialLayerStripHighlight(int? displayLayerNumber)
    {
        _materialLayerStripHighlightDisplayNumber = displayLayerNumber;
        RefreshSurfaceRegionOverlays();
    }

    private bool _useLayerNamesOnMaterialCanvas;

    /// <summary>
    /// When true (main window Material Properties tab), surface region overlays do not show S1/S2 tags;
    /// the selected material layer strip shows the layering row name only.
    /// </summary>
    public bool UseLayerNamesOnMaterialCanvas
    {
        get => _useLayerNamesOnMaterialCanvas;
        set
        {
            if (_useLayerNamesOnMaterialCanvas == value) return;
            _useLayerNamesOnMaterialCanvas = value;
            RefreshInternalEdgeCanvasLines();
            RefreshSurfaceRegionOverlays();
            OnPropertyChanged();
        }
    }

    private bool _isRefreshingMaterialLayerOverlays;

    /// <summary>Rebuilds material layer strip fills (e.g. after mesh axis fit on the Mesh tab).</summary>
    public void RefreshMaterialLayerCanvasOverlays()
    {
        if (_isRefreshingMaterialLayerOverlays) return;
        _isRefreshingMaterialLayerOverlays = true;
        try
        {
            RefreshSurfaceRegionOverlays();
        }
        finally
        {
            _isRefreshingMaterialLayerOverlays = false;
        }
    }

    /// <summary>Axis polygon for the horizontal strip corresponding to display layer # (1 = bottom strip).</summary>
    private bool TryGetHorizontalStripPolygonAxis(int displayLayerNumber, out List<(double x, double y)>? axisPts)
    {
        axisPts = null;
        var n = GeometryVertexRows.Count;
        if (n < 3) return false;

        var eps = Math.Max(AxisEqualityTolerance, (AxisYMax - AxisYMin) * 1e-9);
        var poly = new List<(double x, double y, string label)>(n);
        for (var i = 0; i < n; i++)
        {
            var v = GeometryVertexRows[i];
            poly.Add((v.AxisX, v.AxisY, v.PointLabel));
        }

        if (!TryCollectHorizontalBandElevations(out var merged) || merged.Count < 2)
            return false;

        var layerDisplay = 0;
        for (var i = 0; i < merged.Count - 1; i++)
        {
            var yLo = merged[i];
            var yHi = merged[i + 1];
            if (yHi - yLo < eps) continue;

            var clipped = ClipPolygonToHorizontalStrip(poly, yLo, yHi, eps);
            if (clipped == null || clipped.Count < 3) continue;

            SnapStripPointsToNearestVertices(clipped, eps);
            var labels = DedupeConsecutivePolygonLabels(clipped);
            if (labels.Count < 3) continue;

            layerDisplay++;
            if (layerDisplay != displayLayerNumber) continue;

            axisPts = clipped.Select(p => (p.x, p.y)).ToList();
            return axisPts.Count >= 3;
        }

        return false;
    }

    /// <summary>
    /// Sorted unique Y breaks for horizontal material strips: polygon vertex elevations plus explicit layer cuts.
    /// </summary>
    private bool TryCollectHorizontalBandElevations(out List<double> merged)
    {
        merged = new List<double>();
        var n = GeometryVertexRows.Count;
        if (n < 3)
            return false;

        var eps = Math.Max(AxisEqualityTolerance, (AxisYMax - AxisYMin) * 1e-9);
        var (_, _, yMin, yMax) = GetBoundingBoxBounds();

        // Explicit cuts (Add Layer or script etype sync): bands are base → cuts → crest only.
        if (_materialLayerCutElevations.Count == 0)
            return false;

        merged.Add(yMin);
        foreach (var cutY in _materialLayerCutElevations.OrderBy(y => y))
        {
            if (cutY <= yMin + eps || cutY >= yMax - eps)
                continue;
            if (merged.Count == 0 || Math.Abs(cutY - merged[^1]) > eps)
                merged.Add(cutY);
        }

        if (merged.Count == 0 || Math.Abs(yMax - merged[^1]) > eps)
            merged.Add(yMax);

        merged.Sort();
        return merged.Count >= 2;
    }

    /// <summary>Assigns a new stable point id for a vertex created in this session. Persists in .xfem vertex metadata.</summary>
    public int AllocateStablePointId() => _nextStablePointId++;

    /// <summary>After loading vertices from a file, fills missing ids and advances the counter past all assigned ids.</summary>
    public void NormalizeStablePointIdsAfterLoad()
    {
        var max = 0;
        foreach (var row in GeometryVertexRows)
            if (row.StablePointId > max) max = row.StablePointId;
        _nextStablePointId = max + 1;
        foreach (var row in GeometryVertexRows)
        {
            if (row.StablePointId <= 0)
                row.StablePointId = _nextStablePointId++;
        }
        max = 0;
        foreach (var row in GeometryVertexRows)
            if (row.StablePointId > max) max = row.StablePointId;
        _nextStablePointId = max + 1;
    }

    public int? HighlightedLineNumber { get => _highlightedLineNumber; private set { _highlightedLineNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLineSegmentSelected)); OnPropertyChanged(nameof(CanDeleteLineSegment)); } }
    public bool IsLineSegmentSelected => HighlightedLineNumber.HasValue;
    public bool CanDeleteLineSegment => IsLineSegmentSelected && !IsTunnelCircleDrawingMode && !IsDrawCircleMode;

    public class GeometryVertexDisplay : INotifyPropertyChanged
    {
        private double _axisX;
        private double _axisY;
        private bool _isHighlighted;
        private GeometryDefinitionViewModel? _owner;
        private int _index;
        private int _stablePointId;
        private int? _splitFromLineNumber;
        private bool _isMaterialLayerCutVertex;

        /// <summary>Order along the boundary polyline/polygon (0-based index). Changes when vertices are inserted, deleted, or rotated; does not change point names.</summary>
        public int Index
        {
            get => _index;
            set
            {
                if (_index == value) return;
                _index = value;
                OnPropertyChanged();
                // Fallback numbering (Index+1) only when stable id not yet assigned; canvas/grid bind to PointNumber.
                if (_stablePointId <= 0)
                {
                    OnPropertyChanged(nameof(PointNumber));
                    OnPropertyChanged(nameof(PointLabel));
                }
            }
        }

        /// <summary>Shown as "Point {n}"; uses <see cref="StablePointId"/> so labels do not change when the boundary is rotated (e.g. for surfaces).</summary>
        public int PointNumber => StablePointId > 0 ? StablePointId : Index + 1;

        /// <summary>Monotonic id assigned at creation; stable across reordering of list index when other points are inserted or when geometry is rotated to match a surface.</summary>
        public int StablePointId
        {
            get => _stablePointId;
            set
            {
                if (_stablePointId == value) return;
                _stablePointId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PointNumber));
                OnPropertyChanged(nameof(PointLabel));
            }
        }

        /// <summary>When set, this vertex was created by splitting the given line number (before the split).</summary>
        public int? SplitFromLineNumber
        {
            get => _splitFromLineNumber;
            set
            {
                if (_splitFromLineNumber == value) return;
                _splitFromLineNumber = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PointLabel));
            }
        }

        /// <summary>True when this vertex was inserted by Material Properties → Add Layer (horizontal cut).</summary>
        public bool IsMaterialLayerCutVertex
        {
            get => _isMaterialLayerCutVertex;
            set
            {
                if (_isMaterialLayerCutVertex == value) return;
                _isMaterialLayerCutVertex = value;
                OnPropertyChanged();
            }
        }

        public string PointLabel =>
            SplitFromLineNumber is int ln
                ? $"Point {PointNumber} (split of L{ln})"
                : $"Point {PointNumber}";

        internal GeometryDefinitionViewModel? Owner { get => _owner; set { _owner = value; OnPropertyChanged(nameof(X)); OnPropertyChanged(nameof(Y)); } }

        public double AxisX
        {
            get => _axisX;
            set { _axisX = value; OnPropertyChanged(); OnPropertyChanged(nameof(X)); }
        }

        public double AxisY
        {
            get => _axisY;
            set { _axisY = value; OnPropertyChanged(); OnPropertyChanged(nameof(Y)); }
        }

        public double X => _owner != null ? _owner.ConvertAxisToCanvas(new Point(_axisX, _axisY)).X : _axisX;
        public double Y => _owner != null ? _owner.ConvertAxisToCanvas(new Point(_axisX, _axisY)).Y : _axisY;

        public bool IsHighlighted
        {
            get => _isHighlighted;
            set { _isHighlighted = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public GeometryDefinitionViewModel()
    {
        GeometryPathPoints = new PointCollection();
        HighlightedSegmentPoints = new PointCollection();
        XAxisTicks = new ObservableCollection<AxisTickInfo>();
        YAxisTicks = new ObservableCollection<AxisTickInfo>();
        XAxisMinorTicks = new ObservableCollection<AxisTickInfo>();
        YAxisMinorTicks = new ObservableCollection<AxisTickInfo>();
        GridLines = new ObservableCollection<AxisTickInfo>();
        Layers = new ObservableCollection<MaterialLayerDisplay>();
        ExcavationPolygons = new ObservableCollection<PointCollection>();
        RegionBPolygons = new ObservableCollection<PointCollection>();
        UnionFillPolygons = new ObservableCollection<PointCollection>();
        BoxMethodCircleSegments = new ObservableCollection<PointCollection>();
        CirclePreviews = new ObservableCollection<object>();
        FilteredTunnelExcavationCircles = new ObservableCollection<TunnelExcavationCircle>();
        FilteredTunnelExcavationCircleRows = new ObservableCollection<TunnelExcavationCircleRow>();
        FilteredTunnelExcavationPolygons = new ObservableCollection<TunnelExcavationPolygon>();
        GroundwaterLines = new ObservableCollection<GroundwaterLine>();
        InitGroundwaterCollections();
        GeometryLineSegments = new ObservableCollection<GeometryLineSegment>();
        AvailablePointLabels = new ObservableCollection<string>();
        AvailableLineNumbers = new ObservableCollection<LineOrCircleItem>();
        LineSegmentTagOptions = new ObservableCollection<string> { "Bottom side", "Left side", "Right side", "Top", "Slope surface" };
        ExcavationOperations = new ObservableCollection<BooleanOperationRow>();
        ExcavationOperations.CollectionChanged += OnExcavationOperationsCollectionChanged;
        SurfaceColorOptions = new ObservableCollection<SurfaceColorOption>
        {
            new() { Index = -1, DisplayName = "Auto (by S#)" },
            new() { Index = 0, DisplayName = "Indigo" },
            new() { Index = 1, DisplayName = "Emerald" },
            new() { Index = 2, DisplayName = "Amber" },
            new() { Index = 3, DisplayName = "Pink" },
            new() { Index = 4, DisplayName = "Blue" },
            new() { Index = 5, DisplayName = "Violet" },
            new() { Index = 6, DisplayName = "Red" },
            new() { Index = 7, DisplayName = "Teal" }
        };
        _gridSpacingDescription = $"Grid spacing: {DefaultGridSpacing:F1} m";
        GeometryCanvasCoordinates = FormatCoordinate(new Point(_axisXMin, _axisYMin));
        UpdateAxisTicks();
        RefreshAvailablePointLabels();
        RefreshAvailableLineNumbers();

        OpenAddStageDialogCommand = new RelayCommand(() => { });
        StartNewPointModeCommand = new RelayCommand(() => { CancelBulkPointDeleteMode(); IsNewPointMode = true; });
        StartGeometryVerticesEditCommand = new RelayCommand(() => { CancelBulkPointDeleteMode(); IsGeometryVerticesEditMode = true; });
        FinishGeometryVerticesEditCommand = new RelayCommand(() => { IsGeometryVerticesEditMode = false; IsNewPointMode = false; });
        AddLayerLineCommand = new RelayCommand(AddLayerLine);
        AddLayerPointFromInputCommand = new RelayCommand(AddLayerPointFromInput, CanAddLayerPointFromInput);
        ClearPendingLayerPointsCommand = new RelayCommand(ClearPendingLayerPoints, () => PendingLayerPoints.Count > 0);
        RemovePendingLayerPointCommand = new RelayCommand<LayerPendingPointDisplay?>(RemovePendingLayerPoint, p => p != null);
        ImportLayersFromCsvCommand = new RelayCommand(() => { });
        ExportLayersToCsvCommand = new RelayCommand(() => { });
        RemoveSelectedLayerCommand = new RelayCommand<LayerCoordinateSummary?>(RemoveSelectedLayer, CanRemoveSelectedLayer);
        RemoveGeometryVertexAtCommand = new RelayCommand<int>(RemoveVertexAt);
        RemoveGeometryVertexCommand = new RelayCommand<object?>(RemoveVertexByDisplay);
        AddGeometryVertexAtCommand = new RelayCommand<object?>(_ => AddVertexAtEnd());
        AddSharedGeometryEdgeCommand = new RelayCommand(AddSharedGeometryEdge);
        StartDrawLineModeCommand = new RelayCommand(StartDrawLineMode);
        CancelDrawLineModeCommand = new RelayCommand(CancelDrawLineMode);
        FinishDrawLineModeCommand = new RelayCommand(FinishDrawLineMode);
        StartDrawArcModeCommand = new RelayCommand(StartDrawArcMode);
        CancelDrawArcModeCommand = new RelayCommand(CancelDrawArcMode);
        StartDrawCircleModeCommand = new RelayCommand(StartDrawCircleMode);
        CancelDrawCircleModeCommand = new RelayCommand(CancelDrawCircleMode);
        StartBulkPointDeleteModeCommand = new RelayCommand(StartBulkPointDeleteMode);
        CancelBulkPointDeleteModeCommand = new RelayCommand(CancelBulkPointDeleteMode);
        AddLineSegmentCommand = new RelayCommand(AddLineSegmentRow);
        DeleteLineSegmentCommand = new RelayCommand(DeleteSelectedLineSegment);
        AddTunnelExcavationCircleCommand = new RelayCommand(AddTunnelCircleFromInputs);
        ToggleTunnelCirclesEditModeCommand = new RelayCommand(() => { IsTunnelCirclesEditMode = !IsTunnelCirclesEditMode; });
        DisplaySurfaceOnCanvasCommand = new RelayCommand<object>(o =>
        {
            if (o is not GeometrySurface gs) return;
            SelectedGeometrySurface = gs;
            CommitSurfacePolygonOnAdd(gs);
        });
        DeleteTunnelCircleCommand = new RelayCommand<int>(RemoveTunnelCircleAt);
        DeleteExcavationOperationCommand = new RelayCommand<int>(OnDeleteExcavationOperation);
        DeleteSelectedSurfaceCommand = new RelayCommand(RemoveSelectedSurface, () => CanDeleteSelectedSurface);
        StartExcavationModeCommand = new RelayCommand(StartSurfaceExcavationDialog);
        StartFillModeCommand = new RelayCommand(StartFillTunnelRegionDialog);
        AddSurfaceCommand = new RelayCommand(AddSurface);
        FinishActiveCanvasToolCommand = new RelayCommand(FinishActiveCanvasTool, () => CanFinishCanvasTool);
        SetClosedPolygonModeCommand = new RelayCommand(SetClosedPolygonMode);
        SetOpenPolygonModeCommand = new RelayCommand(SetOpenPolygonMode);
        GeometrySurfaces.CollectionChanged += OnGeometrySurfacesCollectionChanged;
        AddGeometryPointFromInputCommand = new RelayCommand(AddPointFromInput);
        DeleteLastGeometryPointCommand = new RelayCommand(DeleteLastVertex);
        ClearGeometryPointsCommand = new RelayCommand(ClearVertices);
        ImportGeometryFromCsvCommand = new RelayCommand(ImportGeometryFromFile);
        ExportGeometryToCsvCommand = new RelayCommand(ExportGeometryToCsv);
        ClearCurrentStageCommand = new RelayCommand(ClearVertices);
        ImportBoundaryFromMeshCommand = new RelayCommand(ImportBoundaryFromMesh);
        ToggleShowGridCommand = new RelayCommand(() => ShowGrid = !ShowGrid);
        ToggleShowAxesCommand = new RelayCommand(() => ShowAxes = !ShowAxes);
        BoundaryOutlinePoints = new PointCollection();
        InitNonHorizontalLayerCommands();
        InitProductionGeometryCommands();
        InitializeSlopeImageCommands();
    }

    /// <summary>Mesh tab VM used to import outer-boundary corner points and segments.</summary>
    public MeshViewModel? MeshSource
    {
        get => _meshSource;
        set
        {
            if (ReferenceEquals(_meshSource, value)) return;
            if (_meshSource != null)
                _meshSource.PropertyChanged -= OnMeshSourcePropertyChanged;
            _meshSource = value;
            if (_meshSource != null)
                _meshSource.PropertyChanged += OnMeshSourcePropertyChanged;
            RefreshGroundwaterOverlays();
        }
    }

    private void OnMeshSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MeshViewModel.IsDiscretized)
            or nameof(MeshViewModel.DiscretizationNodes)
            or nameof(MeshViewModel.MeshInformationRows))
            RefreshGroundwaterOverlays();
    }

    public ProjectSettingsViewModel? ProjectSettings
    {
        get => _projectSettings;
        set
        {
            if (ReferenceEquals(_projectSettings, value)) return;
            if (_projectSettings != null)
            {
                _projectSettings.SeepageSettings.PropertyChanged -= OnSeepageSettingsPropertyChanged;
                _projectSettings.PropertyChanged -= OnProjectSettingsAxisymPropertyChanged;
            }
            _projectSettings = value;
            if (_projectSettings != null)
            {
                _projectSettings.SeepageSettings.PropertyChanged += OnSeepageSettingsPropertyChanged;
                _projectSettings.PropertyChanged += OnProjectSettingsAxisymPropertyChanged;
            }
            NotifyAxisOfSymmetryOverlayChanged();
        }
    }


    private void OnProjectSettingsAxisymPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(ProjectSettingsViewModel.SrfAxisymmetric))
            NotifyAxisOfSymmetryOverlayChanged();
    }
    private void OnSeepageSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(SeepageAnalysisSettings.Axisymmetric))
            NotifyAxisOfSymmetryOverlayChanged();
    }

    public string GeometryImportStatusMessage
    {
        get => _geometryImportStatusMessage;
        private set { _geometryImportStatusMessage = value ?? ""; OnPropertyChanged(); }
    }

    public void ClearGeometryImportStatusMessage() => GeometryImportStatusMessage = "";

    public void SetGeometryImportStatusMessage(string message) => GeometryImportStatusMessage = message ?? "";

    public ICommand ImportBoundaryFromMeshCommand { get; }

    /// <summary>Closed boundary polyline for the geometry canvas (includes closing segment).</summary>
    public PointCollection BoundaryOutlinePoints { get; private set; }

    private bool _showBoundaryMicroOutlineOnCanvas;

    /// <summary>Mesh tab: full discretized boundary ring. Hidden on Geometry Definition (macro S1 outline only).</summary>
    public bool ShowBoundaryMicroOutlineOnCanvas
    {
        get => _showBoundaryMicroOutlineOnCanvas;
        set
        {
            if (_showBoundaryMicroOutlineOnCanvas == value) return;
            _showBoundaryMicroOutlineOnCanvas = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowBoundaryPolylineOutline));
        }
    }

    private bool _showInternalAndLayerCutLinesOnCanvas;

    /// <summary>Material Properties tab: shared/internal edges and Add Layer cut chords.</summary>
    public bool ShowInternalAndLayerCutLinesOnCanvas
    {
        get => _showInternalAndLayerCutLinesOnCanvas;
        set
        {
            if (_showInternalAndLayerCutLinesOnCanvas == value) return;
            _showInternalAndLayerCutLinesOnCanvas = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowInternalEdgeLinesOnCanvas));
            OnPropertyChanged(nameof(ShowMaterialLayerCutLinesOnCanvas));
        }
    }

    private bool _showGeometryEdgeLinesOnCanvas;

    /// <summary>
    /// Geometry Definition tab: user-drawn boundary ring and internal edges/arcs/circles.
    /// Without this the tab shows only vertex dots — segments live in
    /// <see cref="GeometryLineSegments"/> but render through <see cref="BoundaryOutlinePoints"/>
    /// and <see cref="InternalEdgeCanvasLines"/>, both otherwise gated to other tabs.
    /// </summary>
    public bool ShowGeometryEdgeLinesOnCanvas
    {
        get => _showGeometryEdgeLinesOnCanvas;
        set
        {
            if (_showGeometryEdgeLinesOnCanvas == value) return;
            _showGeometryEdgeLinesOnCanvas = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowInternalEdgeLinesOnCanvas));
            OnPropertyChanged(nameof(ShowBoundaryPolylineOutline));
        }
    }

    public bool ShowInternalEdgeLinesOnCanvas =>
        _showInternalAndLayerCutLinesOnCanvas || _showGeometryEdgeLinesOnCanvas;
    public bool ShowMaterialLayerCutLinesOnCanvas => _showInternalAndLayerCutLinesOnCanvas;

    private bool _showGeometryPointsOnCanvas = true;
    private bool _showGeometrySurfaceFillsOnCanvas = true;
    private bool _showGeometryLabelsOnCanvas = true;
    private bool _showMaterialLayerLabelsOnCanvas = true;

    /// <summary>Geometry View Options: vertex dots on the shared slope canvas.</summary>
    public bool ShowGeometryPointsOnCanvas
    {
        get => _showGeometryPointsOnCanvas;
        set
        {
            if (_showGeometryPointsOnCanvas == value) return;
            _showGeometryPointsOnCanvas = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Geometry View Options: surface/region fills (and boolean result polygons).</summary>
    public bool ShowGeometrySurfaceFillsOnCanvas
    {
        get => _showGeometrySurfaceFillsOnCanvas;
        set
        {
            if (_showGeometrySurfaceFillsOnCanvas == value) return;
            _showGeometrySurfaceFillsOnCanvas = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Geometry View Options: vertex number labels next to points.</summary>
    public bool ShowGeometryLabelsOnCanvas
    {
        get => _showGeometryLabelsOnCanvas;
        set
        {
            if (_showGeometryLabelsOnCanvas == value) return;
            _showGeometryLabelsOnCanvas = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Materials View Options: layer-name tags on material strip fills.</summary>
    public bool ShowMaterialLayerLabelsOnCanvas
    {
        get => _showMaterialLayerLabelsOnCanvas;
        set
        {
            if (_showMaterialLayerLabelsOnCanvas == value) return;
            _showMaterialLayerLabelsOnCanvas = value;
            OnPropertyChanged();
        }
    }

    private bool _showGroundwaterPhreaticOnCanvas;

    /// <summary>When true and groundwater is enabled, show the phreatic polyline on the shared slope canvas (all tabs).</summary>
    public bool ShowGroundwaterPhreaticOnCanvas
    {
        get => _showGroundwaterPhreaticOnCanvas;
        set
        {
            if (_showGroundwaterPhreaticOnCanvas == value) return;
            _showGroundwaterPhreaticOnCanvas = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGroundwaterPhreaticVisible));
        }
    }

    public bool IsGroundwaterPhreaticVisible =>
        IsGroundwaterEnabled && _showGroundwaterPhreaticOnCanvas;

    /// <summary>True when the closed boundary vertex ring is drawn on the geometry canvas.</summary>
    /// <remarks>
    /// Must stay visible after horizontal layer cuts are added; otherwise consecutive boundary edges
    /// (e.g. 81→91 on the left face) exist in <see cref="GeometryLineSegments"/> but are neither drawn here
    /// nor kept as <see cref="InternalEdgeCanvasLines"/> after <see cref="PruneDuplicateBoundaryInternalEdges"/>.
    /// </remarks>
    public bool ShowBoundaryPolylineOutline =>
        (_showBoundaryMicroOutlineOnCanvas || _showGeometryEdgeLinesOnCanvas)
        && _boundarySegmentCount > 0
        && GeometryVertexRows.Count >= 2
        && !IsMacroOnlyBoundaryRing();

    private bool ShouldAutoCloseBoundary(int vertexCount) =>
        vertexCount >= 3 && !IsOpenPolygonBoundaryMode;

    public double CanvasWidth
    {
        get => _canvasWidth;
        set { _canvasWidth = value; OnPropertyChanged(); UpdateAxisTicks(); }
    }

    public double CanvasHeight
    {
        get => _canvasHeight;
        set
        {
            _canvasHeight = value;
            OnPropertyChanged();
            NotifyOriginAxisOverlayChanged();
            UpdateAxisTicks();
        }
    }

    /// <summary>
    /// Matches the logical plot size to the host panel so <c>Viewbox Stretch=Uniform</c>
    /// fills without letterboxing. Debounced — host SizeChanged must not synchronously
    /// rebuild mesh/GW overlays (that path hung the UI).
    /// </summary>
    public void SetLogicalCanvasSize(double width, double height)
    {
        if (double.IsNaN(width) || double.IsNaN(height) || double.IsInfinity(width) || double.IsInfinity(height))
            return;
        if (width < 50 || height < 50)
            return;

        width = Math.Clamp(width, 200, 4000);
        height = Math.Clamp(height, 150, 3000);
        if (Math.Abs(_canvasWidth - width) < 2.0 && Math.Abs(_canvasHeight - height) < 2.0
            && Math.Abs(_pendingLogicalCanvasWidth - width) < 2.0
            && Math.Abs(_pendingLogicalCanvasHeight - height) < 2.0)
            return;

        _pendingLogicalCanvasWidth = width;
        _pendingLogicalCanvasHeight = height;
        if (_logicalCanvasResizeQueued || IsGeometryBulkUpdate)
            return;

        _logicalCanvasResizeQueued = true;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            ApplyPendingLogicalCanvasSize();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyPendingLogicalCanvasSize);
    }

    private void ApplyPendingLogicalCanvasSize()
    {
        _logicalCanvasResizeQueued = false;
        var width = _pendingLogicalCanvasWidth;
        var height = _pendingLogicalCanvasHeight;
        if (Math.Abs(_canvasWidth - width) < 0.5 && Math.Abs(_canvasHeight - height) < 0.5)
            return;

        _canvasWidth = width;
        _canvasHeight = height;
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        NotifyOriginAxisOverlayChanged();
        // Size-only remaps: skip EnsureAxisBounds (would fight nice ticks during layout).
        RefreshGeometryPath(ensureGroundwaterAxisBounds: false);
    }

    /// <summary>Canvas anchor for global axis glyph at world (AxisXMin+2, AxisYMin+2).</summary>
    public double OriginAxisOriginCanvasX => GetOriginAxisAnchorCanvasPoint().X;

    public double OriginAxisOriginCanvasY => GetOriginAxisAnchorCanvasPoint().Y;

    public double OriginAxisArrowLengthPx => OriginAxisArrowLength;

    /// <summary>Negative canvas offset for +Y arrow tip (upward from anchor).</summary>
    public double OriginAxisYTipOffset => -OriginAxisArrowLength;

    private Point GetOriginAxisAnchorCanvasPoint()
    {
        var baseAnchor = ConvertAxisToCanvas(new Point(AxisXMin + OriginAxisWorldInset, AxisYMin + OriginAxisWorldInset));
        return _viewMatrix.Transform(baseAnchor);
    }

    private void NotifyOriginAxisOverlayChanged()
    {
        OnPropertyChanged(nameof(OriginAxisOriginCanvasX));
        OnPropertyChanged(nameof(OriginAxisOriginCanvasY));
        OnPropertyChanged(nameof(OriginAxisYTipOffset));
        NotifyAxisOfSymmetryOverlayChanged();
    }

    private void NotifyAxisOfSymmetryOverlayChanged()
    {
        UpdateAxisOfSymmetryScreenCoords(_viewMatrix);
        OnPropertyChanged(nameof(ShowAxisOfSymmetry));
        OnPropertyChanged(nameof(AxisOfSymmetryX1));
        OnPropertyChanged(nameof(AxisOfSymmetryY1));
        OnPropertyChanged(nameof(AxisOfSymmetryX2));
        OnPropertyChanged(nameof(AxisOfSymmetryY2));
        OnPropertyChanged(nameof(AxisOfSymmetryLabelX));
        OnPropertyChanged(nameof(AxisOfSymmetryLabelY));
    }

    /// <summary>Maps world x = 0 (radial axis) to current screen coordinates for the symmetry overlay.</summary>
    private void UpdateAxisOfSymmetryScreenCoords(Matrix viewMatrix)
    {
        if (CanvasWidth <= 0 || CanvasHeight <= 0)
        {
            AxisOfSymmetryX1 = AxisOfSymmetryX2 = 0;
            AxisOfSymmetryY1 = 0;
            AxisOfSymmetryY2 = CanvasHeight;
            AxisOfSymmetryLabelX = 4;
            AxisOfSymmetryLabelY = 4;
            return;
        }

        var top = AxisToScreen(new Point(0, AxisYMax), viewMatrix);
        var bottom = AxisToScreen(new Point(0, AxisYMin), viewMatrix);
        AxisOfSymmetryX1 = top.X;
        AxisOfSymmetryY1 = top.Y;
        AxisOfSymmetryX2 = bottom.X;
        AxisOfSymmetryY2 = bottom.Y;
        AxisOfSymmetryLabelX = top.X + 6;
        AxisOfSymmetryLabelY = Math.Clamp(Math.Min(top.Y, bottom.Y) + 4, 2, Math.Max(2, CanvasHeight - 18));
    }

    public double AxisXMin { get => _axisXMin; set { _axisXMin = value; OnPropertyChanged(); UpdateAxisTicks(); } }
    public double AxisXMax { get => _axisXMax; set { _axisXMax = value; OnPropertyChanged(); UpdateAxisTicks(); } }
    public double AxisYMin { get => _axisYMin; set { _axisYMin = value; OnPropertyChanged(); UpdateAxisTicks(); } }
    public double AxisYMax { get => _axisYMax; set { _axisYMax = value; OnPropertyChanged(); UpdateAxisTicks(); } }

    public string GeometryCanvasCoordinates
    {
        get => _geometryCanvasCoordinates;
        set { _geometryCanvasCoordinates = value ?? ""; OnPropertyChanged(); }
    }

    public string GridSpacingDescription
    {
        get => _gridSpacingDescription;
        set { _gridSpacingDescription = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>When true, draws a world-coordinate snap grid on the slope canvas (RS2 View → Grid).</summary>
    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (_showGrid == value) return;
            _showGrid = value;
            if (_projectSettings?.Project != null)
                _projectSettings.Project.ShowGrid = value;
            OnPropertyChanged();
            UpdateGridLines();
        }
    }

    public ObservableCollection<AxisTickInfo> GridLines { get; }

    public ICommand ToggleShowGridCommand { get; }

    /// <summary>When true, draws global X/Y origin arrows at the canvas bottom-left (Plaxis View → Axes).</summary>
    public bool ShowAxes
    {
        get => _showAxes;
        set
        {
            if (_showAxes == value) return;
            _showAxes = value;
            if (_projectSettings?.Project != null)
                _projectSettings.Project.ShowAxes = value;
            OnPropertyChanged();
        }
    }

    public ICommand ToggleShowAxesCommand { get; }

    /// <summary>
    /// When seepage or mechanical SRF Axisymmetric is on, draw the r = 0 axis of symmetry
    /// (vertical line at world x = 0) on geometry canvases.
    /// </summary>
    public bool ShowAxisOfSymmetry => _projectSettings?.SeepageSettings.Axisymmetric == true || _projectSettings?.SrfAxisymmetric == true;

    public double AxisOfSymmetryX1 { get; private set; }
    public double AxisOfSymmetryY1 { get; private set; }
    public double AxisOfSymmetryX2 { get; private set; }
    public double AxisOfSymmetryY2 { get; private set; }
    public double AxisOfSymmetryLabelX { get; private set; }
    public double AxisOfSymmetryLabelY { get; private set; }

    /// <summary>Applies grid spacing from Project Settings → Grid &amp; Axes.</summary>
    public void SyncGridSpacing(double spacingMeters)
    {
        _gridSpacing = Math.Max(0.1, spacingMeters);
        _gridSpacingDescription = $"Grid spacing: {_gridSpacing:F1} m";
        OnPropertyChanged(nameof(GridSpacingDescription));
        UpdateAxisTicks();
    }

    public PointCollection GeometryPathPoints { get; private set; }
    public PointCollection HighlightedSegmentPoints { get; set; }

    /// <summary>Geometry canvas reuses the shared slope canvas; RS2D boundary crosses are shown only on the Mesh tab (<see cref="MeshTabCanvasWrapper"/>).</summary>
    public bool ShowMeshDiscretizationMarkers => false;

    /// <summary>All user-defined closed surface regions (from Surface grid); each has its own fill color and label. Selected row draws on top.</summary>
    public ObservableCollection<SurfaceRegionOverlayItem> SurfaceRegionOverlays { get; } = new();

    /// <summary>Canvas highlight polylines for each line segment currently picked for the active (unlocked) surface.</summary>
    public ObservableCollection<PointCollection> PickedSurfaceLineHighlightPolylines { get; } = new();

    /// <summary>Geometry Explorer selection: extra canvas strokes for a surface/region/block outline.</summary>
    public ObservableCollection<PointCollection> ExplorerHighlightPolylines { get; } = new();

    public const string SoilBlockExplorerPayload = "soil-block";
    public const string NewSurfaceExplorerPayload = "new-surface";
    private object? _explorerHighlightPayload;

    /// <summary>Surface row selected in the Geometry Definition details grid; used for emphasis stroke and draw order.</summary>
    public GeometrySurface? SelectedGeometrySurface
    {
        get => _selectedGeometrySurface;
        set
        {
            if (_selectedGeometrySurface == value) return;
            _selectedGeometrySurface = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSurfacePolygonPickMode));
            OnPropertyChanged(nameof(CanDeleteSelectedSurface));
            RefreshSurfaceRegionOverlays();
        }
    }

    /// <summary>Excavation Operations grid selection; when set to a completed row, the canvas shows that operation's result.</summary>
    public BooleanOperationRow? SelectedExcavationOperation
    {
        get => _selectedExcavationOperation;
        set
        {
            if (ReferenceEquals(_selectedExcavationOperation, value)) return;
            _selectedExcavationOperation = value;
            OnPropertyChanged();
            if (_selectedExcavationOperation == null)
            {
                RecomputeSurfaceBooleanFromLastOperation();
                return;
            }
            if (!_selectedExcavationOperation.IsComplete)
            {
                ClearSurfaceBooleanCanvasResult();
                RefreshSurfaceRegionOverlays();
                return;
            }

            if (StagedConstruction != null && StagedConstruction.Stages.Count > 1)
            {
                int active = StagedConstruction.ActiveStageNumber;
                int applied = ResolveEffectiveAppliedStageNumber(_selectedExcavationOperation);
                if (active < 2 || applied > active)
                {
                    ClearSurfaceBooleanCanvasResult();
                    RefreshSurfaceRegionOverlays();
                    return;
                }
            }

            ApplySurfaceBooleanPreviewForRow(_selectedExcavationOperation);
        }
    }

    public ObservableCollection<AxisTickInfo> XAxisTicks { get; }
    public ObservableCollection<AxisTickInfo> YAxisTicks { get; }
    public ObservableCollection<AxisTickInfo> XAxisMinorTicks { get; }
    public ObservableCollection<AxisTickInfo> YAxisMinorTicks { get; }
    public ObservableCollection<MaterialLayerDisplay> Layers { get; }
    public ObservableCollection<PointCollection> ExcavationPolygons { get; }

    /// <summary>
    /// Excavated zone records pushed by <see cref="StagedConstructionViewModel"/>.
    /// Rendered on canvas as solid white filled polygons (dashed outline).
    /// </summary>
    public ObservableCollection<StageExcavationZone> ExcavatedStageZones { get; } = new();

    /// <summary>Set by MainWindowViewModel after StagedConstructionViewModel is created.</summary>
    public StagedConstructionViewModel? StagedConstruction { get; set; }
    public ObservableCollection<PointCollection> RegionBPolygons { get; }
    public ObservableCollection<PointCollection> UnionFillPolygons { get; }
    public ObservableCollection<object> CirclePreviews { get; }
    public ObservableCollection<PointCollection> BoxMethodCircleSegments { get; }
    public ObservableCollection<TunnelExcavationCircle> FilteredTunnelExcavationCircles { get; }
    public ObservableCollection<TunnelExcavationCircleRow> FilteredTunnelExcavationCircleRows { get; }
    /// <summary>Non-circular tunnel polygons (horseshoe / rectangular / NATM arch / DXF). Sibling to <see cref="FilteredTunnelExcavationCircles"/>.</summary>
    public ObservableCollection<TunnelExcavationPolygon> FilteredTunnelExcavationPolygons { get; }
    public ObservableCollection<GroundwaterLine> GroundwaterLines { get; }

    public string TunnelCenterXInput { get => _tunnelCenterXInput; set { _tunnelCenterXInput = value ?? ""; OnPropertyChanged(); } }
    public string TunnelCenterYInput { get => _tunnelCenterYInput; set { _tunnelCenterYInput = value ?? ""; OnPropertyChanged(); } }
    public string TunnelRadiusInput { get => _tunnelRadiusInput; set { _tunnelRadiusInput = value ?? ""; OnPropertyChanged(); } }
    public string TunnelCircleDrawingStatus { get => _tunnelCircleDrawingStatus; set { _tunnelCircleDrawingStatus = value ?? ""; OnPropertyChanged(); } }
    public bool IsAddCircleEnabled => !IsTunnelCircleDrawingMode && double.TryParse(TunnelCenterXInput, out _) && double.TryParse(TunnelCenterYInput, out _) && double.TryParse(TunnelRadiusInput, out var r) && r > 0;
    public string GeometryXInput { get => _geometryXInput; set { _geometryXInput = value ?? ""; OnPropertyChanged(); } }
    public string GeometryYInput { get => _geometryYInput; set { _geometryYInput = value ?? ""; OnPropertyChanged(); } }

    public double TunnelCirclePreviewCenterX { get => _tunnelCirclePreviewCenterX; set { _tunnelCirclePreviewCenterX = value; OnPropertyChanged(); } }
    public double TunnelCirclePreviewCenterY { get => _tunnelCirclePreviewCenterY; set { _tunnelCirclePreviewCenterY = value; OnPropertyChanged(); } }
    public double TunnelCirclePreviewDiameter { get => _tunnelCirclePreviewDiameter; set { _tunnelCirclePreviewDiameter = value; OnPropertyChanged(); } }
    public double TunnelCirclePreviewNegativeRadius { get => _tunnelCirclePreviewNegativeRadius; set { _tunnelCirclePreviewNegativeRadius = value; OnPropertyChanged(); } }
    public bool IsTunnelCirclePreviewVisible { get => _isTunnelCirclePreviewVisible; set { _isTunnelCirclePreviewVisible = value; OnPropertyChanged(); } }
    public string TunnelCirclePreviewRadiusText { get => _tunnelCirclePreviewRadiusText; set { _tunnelCirclePreviewRadiusText = value ?? ""; OnPropertyChanged(); } }
    public bool IsBoxMethodCircleVisible { get => _isBoxMethodCircleVisible; set { _isBoxMethodCircleVisible = value; OnPropertyChanged(); } }

    public string LayerElevationInput
    {
        get => _layerElevationInput;
        set { _layerElevationInput = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>X coordinate for the next layering point (project axis units).</summary>
    public string LayerPointXInput
    {
        get => _layerPointXInput;
        set { _layerPointXInput = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Y coordinate for the next layering point (project axis units). For horizontal layers this is the cut elevation.</summary>
    public string LayerPointYInput
    {
        get => _layerPointYInput;
        set { _layerPointYInput = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Status text for coordinate-based layering (Add Point / Add Layer).</summary>
    public string LayeringControlsStatus
    {
        get => _layeringControlsStatus;
        private set { _layeringControlsStatus = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Points queued before Add Layer (shared by horizontal and non-horizontal modes).</summary>
    public ObservableCollection<LayerPendingPointDisplay> PendingLayerPoints { get; } = new();

    public LayerPendingPointDisplay? SelectedPendingLayerPoint
    {
        get => _selectedPendingLayerPoint;
        set
        {
            if (ReferenceEquals(_selectedPendingLayerPoint, value)) return;
            _selectedPendingLayerPoint = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public ObservableCollection<LayerCoordinateSummary> LayerCoordinateSummaries { get; } = new();

    private LayerCoordinateSummary? _selectedLayerSummary;
    /// <summary>
    /// Selected row in Layering Controls DataGrid. When set, highlights the corresponding layer segment on the canvas.
    /// </summary>
    public LayerCoordinateSummary? SelectedLayerSummary
    {
        get => _selectedLayerSummary;
        set
        {
            if (_selectedLayerSummary == value) return;
            _selectedLayerSummary = value;
            OnPropertyChanged();
            SetHighlightedLineSegment(value?.LayerNumber);
        }
    }

    public ICommand OpenAddStageDialogCommand { get; set; } = null!;
    public ICommand StartNewPointModeCommand { get; set; } = null!;
    public ICommand StartGeometryVerticesEditCommand { get; set; } = null!;
    public ICommand FinishGeometryVerticesEditCommand { get; set; } = null!;
    public ICommand AddLayerLineCommand { get; set; } = null!;
    public ICommand AddLayerPointFromInputCommand { get; set; } = null!;
    public ICommand ClearPendingLayerPointsCommand { get; set; } = null!;
    public ICommand RemovePendingLayerPointCommand { get; set; } = null!;
    public ICommand ImportLayersFromCsvCommand { get; set; } = null!;
    public ICommand ExportLayersToCsvCommand { get; set; } = null!;
    public ICommand RemoveSelectedLayerCommand { get; set; } = null!;
    public ICommand RemoveGeometryVertexAtCommand { get; set; } = null!;
    public ICommand RemoveGeometryVertexCommand { get; set; } = null!;
    public ICommand AddGeometryVertexAtCommand { get; set; } = null!;
    public ICommand AddSharedGeometryEdgeCommand { get; set; } = null!;
    public ICommand AddLineSegmentCommand { get; set; } = null!;
    public ICommand DeleteLineSegmentCommand { get; set; } = null!;
    public ICommand AddTunnelExcavationCircleCommand { get; set; } = null!;
    public ICommand ToggleTunnelCirclesEditModeCommand { get; set; } = null!;
    public ICommand DisplaySurfaceOnCanvasCommand { get; set; } = null!;
    public ICommand DeleteTunnelCircleCommand { get; set; } = null!;
    public ICommand DeleteExcavationOperationCommand { get; set; } = null!;
    public ICommand DeleteSelectedSurfaceCommand { get; set; } = null!;
    public bool CanDeleteSelectedSurface => SelectedGeometrySurface != null;
    public ICommand StartExcavationModeCommand { get; set; } = null!;
    public ICommand StartFillModeCommand { get; set; } = null!;
    public ICommand AddSurfaceCommand { get; set; } = null!;
    public ICommand SetClosedPolygonModeCommand { get; set; } = null!;
    public ICommand SetOpenPolygonModeCommand { get; set; } = null!;
    public ICommand AddGeometryPointFromInputCommand { get; set; } = null!;
    public ICommand DeleteLastGeometryPointCommand { get; set; } = null!;
    public ICommand ClearGeometryPointsCommand { get; set; } = null!;
    public ICommand ImportGeometryFromCsvCommand { get; set; } = null!;
    public ICommand ExportGeometryToCsvCommand { get; set; } = null!;
    public ICommand ClearCurrentStageCommand { get; set; } = null!;
    public ICommand StartBulkPointDeleteModeCommand { get; private set; } = null!;
    public ICommand CancelBulkPointDeleteModeCommand { get; private set; } = null!;

    /// <summary>Callbacks set by GeometryDefinitionView so the main menu can open the same tool popups.</summary>
    public Action? RequestOpenPointsPanel { get; set; }
    public Action? RequestOpenLineSegmentsPanel { get; set; }
    public Action? RequestOpenArcPanel { get; set; }
    public Action? RequestOpenCirclePanel { get; set; }
    public Action? RequestOpenSurfacePanel { get; set; }

    public Point BoxMethodCircleCenter { get; set; }
    public double BoxMethodCircleRadius { get; set; }
    public bool IsTunnelCirclesEditMode { get; set; }
    public ObservableCollection<TunnelExcavationCircleRow> TunnelExcavationCircleRows { get; } = new();
    public IReadOnlyList<TunnelExcavationCircle> TunnelExcavationCircles => FilteredTunnelExcavationCircles;
    public bool IsExcavationMode { get; set; }

    /// <summary>True while an Assign button is active and the user should click a surface to paint it.</summary>
    public bool IsMaterialAssignMode
    {
        get => _isMaterialAssignMode;
        private set { _isMaterialAssignMode = value; OnPropertyChanged(); }
    }
    public bool IsTunnelCircleDrawingMode { get; set; }
    public Point? TunnelCirclePreviewCenter { get; set; }
    public bool IsGeometryVerticesEditMode { get; set; }
    public bool IsNewPointMode { get; set; }
    /// <summary>When true, boundary vertices remain an open chain until the user creates explicit line segments/surfaces.</summary>
    public bool IsOpenPolygonBoundaryMode
    {
        get => _isOpenPolygonBoundaryMode;
        set
        {
            if (_isOpenPolygonBoundaryMode == value) return;
            _isOpenPolygonBoundaryMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsClosedPolygonBoundaryMode));
            RefreshGeometryPath();
            RefreshGeometryLineSegments();
        }
    }
    public bool IsClosedPolygonBoundaryMode => !IsOpenPolygonBoundaryMode;
    public bool IsBulkPointDeleteMode
    {
        get => _isBulkPointDeleteMode;
        private set
        {
            if (_isBulkPointDeleteMode == value) return;
            _isBulkPointDeleteMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBulkPointDeleteModeInactive));
        }
    }

    public bool IsBulkPointDeleteModeInactive => !IsBulkPointDeleteMode;

    public string BulkPointDeleteStatus
    {
        get => _bulkPointDeleteStatus;
        private set { _bulkPointDeleteStatus = value ?? ""; OnPropertyChanged(); }
    }

    public bool IsBulkPointDeleteSelectionVisible
    {
        get => _isBulkPointDeleteSelectionVisible;
        private set { _isBulkPointDeleteSelectionVisible = value; OnPropertyChanged(); }
    }

    public double BulkPointDeleteSelectionLeft
    {
        get => _bulkPointDeleteSelectionLeft;
        private set { _bulkPointDeleteSelectionLeft = value; OnPropertyChanged(); }
    }

    public double BulkPointDeleteSelectionTop
    {
        get => _bulkPointDeleteSelectionTop;
        private set { _bulkPointDeleteSelectionTop = value; OnPropertyChanged(); }
    }

    public double BulkPointDeleteSelectionWidth
    {
        get => _bulkPointDeleteSelectionWidth;
        private set { _bulkPointDeleteSelectionWidth = value; OnPropertyChanged(); }
    }

    public double BulkPointDeleteSelectionHeight
    {
        get => _bulkPointDeleteSelectionHeight;
        private set { _bulkPointDeleteSelectionHeight = value; OnPropertyChanged(); }
    }

    /// <summary>Stub so SlopeCanvasView BC-import marquee bindings succeed on geometry-only DataContexts.</summary>
    public bool IsBcImportSelectionVisible => false;
    public double BcImportSelectionLeft => 0;
    public double BcImportSelectionTop => 0;
    public double BcImportSelectionWidth => 0;
    public double BcImportSelectionHeight => 0;

    public IReadOnlyList<(int StageNumber, string StageName)> Stages { get; } = new List<(int, string)>();
    public ObservableCollection<string> LineSegmentTagOptions { get; }
    public ObservableCollection<GeometryVertexDisplay> GeometryVertexRows { get; } = new();
    public ObservableCollection<GeometryVertexDisplay> GeometryVertices => GeometryVertexRows;
    public ObservableCollection<GeometryLineSegment> GeometryLineSegments { get; }
    /// <summary>Undirected shared edges between existing vertices (stable ids); appear as extra L# after boundary segments.</summary>
    public ObservableCollection<GeometryInternalEdge> GeometryInternalEdges { get; } = new();
    /// <summary>Circles drawn with the Circle tool (one grid row each: center + radius).</summary>
    public ObservableCollection<GeometryInternalCircle> GeometryInternalCircles { get; } = new();
    private int _nextInternalCircleId = 1;
    /// <summary>Canvas lines for drawing shared edges (dashed).</summary>
    public ObservableCollection<InternalEdgeCanvasLine> InternalEdgeCanvasLines { get; } = new();
    /// <summary>
    /// Solid orange chords at each <see cref="_materialLayerCutElevations"/> entry (Materials → Layer Inputs → Add Layer).
    /// Hidden on the Material Properties canvas when <see cref="UseLayerNamesOnMaterialCanvas"/> already draws filled strips.
    /// </summary>
    public ObservableCollection<InternalEdgeCanvasLine> HorizontalLayerCanvasLines { get; } = new();
    /// <summary>Count of consecutive boundary segments (excludes shared/internal); used for 1D mesh layer count.</summary>
    public int BoundarySegmentCount => _boundarySegmentCount;
    public string SharedEdgeFromPointLabel
    {
        get => _sharedEdgeFromPointLabel;
        set { _sharedEdgeFromPointLabel = value ?? ""; OnPropertyChanged(); }
    }
    public string SharedEdgeToPointLabel
    {
        get => _sharedEdgeToPointLabel;
        set { _sharedEdgeToPointLabel = value ?? ""; OnPropertyChanged(); }
    }
    public ObservableCollection<string> AvailablePointLabels { get; }
    public ObservableCollection<LineOrCircleItem> AvailableLineNumbers { get; }
    public ObservableCollection<GeometrySurface> GeometrySurfaces { get; } = new();

    /// <summary>Surface grid Color column: Auto (-1) or fixed palette slot 0..7.</summary>
    public ObservableCollection<SurfaceColorOption> SurfaceColorOptions { get; }
    public ObservableCollection<BooleanOperationRow> ExcavationOperations { get; }

    public Point? ExcavationRectangleStart { get; set; }
    public Point? ExcavationRectangleEnd { get; set; }

    // ---- Draw-line mode ----
    private bool _isDrawLineMode;
    private string? _drawLineFirstLabel;
    private string _drawLineModeStatus = "";
    private double _drawLinePreviewX1, _drawLinePreviewY1, _drawLinePreviewX2, _drawLinePreviewY2;

    public ICommand StartDrawLineModeCommand { get; private set; } = null!;
    public ICommand CancelDrawLineModeCommand { get; private set; } = null!;
    public ICommand FinishDrawLineModeCommand { get; private set; } = null!;
    public ICommand FinishActiveCanvasToolCommand { get; private set; } = null!;
    public ICommand StartDrawArcModeCommand { get; private set; } = null!;
    public ICommand CancelDrawArcModeCommand { get; private set; } = null!;
    public ICommand StartDrawCircleModeCommand { get; private set; } = null!;
    public ICommand CancelDrawCircleModeCommand { get; private set; } = null!;

    public bool IsDrawLineMode
    {
        get => _isDrawLineMode;
        private set { _isDrawLineMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDrawLinePreviewVisible)); }
    }
    public string? DrawLineFirstLabel
    {
        get => _drawLineFirstLabel;
        private set { _drawLineFirstLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDrawLinePreviewVisible)); }
    }
    public bool IsDrawLinePreviewVisible => _isDrawLineMode && _drawLineFirstLabel != null;
    public string DrawLineModeStatus
    {
        get => _drawLineModeStatus;
        private set { _drawLineModeStatus = value ?? ""; OnPropertyChanged(); }
    }
    public double DrawLinePreviewX1 { get => _drawLinePreviewX1; private set { _drawLinePreviewX1 = value; OnPropertyChanged(); } }
    public double DrawLinePreviewY1 { get => _drawLinePreviewY1; private set { _drawLinePreviewY1 = value; OnPropertyChanged(); } }
    public double DrawLinePreviewX2 { get => _drawLinePreviewX2; private set { _drawLinePreviewX2 = value; OnPropertyChanged(); } }
    public double DrawLinePreviewY2 { get => _drawLinePreviewY2; private set { _drawLinePreviewY2 = value; OnPropertyChanged(); } }

    private void StartDrawLineMode()
    {
        CancelBulkPointDeleteMode();
        CancelDrawArcMode();
        CancelDrawCircleMode();
        IsDrawLineMode = true;
        DrawLineFirstLabel = null;
        DrawLineModeStatus = "Click the first vertex on the canvas.";
    }

    private void CancelDrawLineMode()
    {
        IsDrawLineMode = false;
        DrawLineFirstLabel = null;
        DrawLineModeStatus = "";
    }

    /// <summary>Ends draw-line session after one or more segments (same as cancel for line mode).</summary>
    private void FinishDrawLineMode() => CancelDrawLineMode();

    public bool CanFinishCanvasTool =>
        IsDrawLineMode || IsDrawArcMode || IsDrawCircleMode || IsSurfacePolygonPickMode;

    /// <summary>Right-click Finish: drop the rubber-band preview and leave the active canvas tool.</summary>
    public void FinishActiveCanvasTool()
    {
        if (IsDrawLineMode)
            FinishDrawLineMode();
        if (IsDrawArcMode)
            CancelDrawArcMode();
        if (IsDrawCircleMode)
            CancelDrawCircleMode();

        var surface = SelectedGeometrySurface;
        if (surface == null || surface.IsPolygonLocked)
            return;

        CommitSurfacePolygonOnAdd(surface);
        if (!surface.IsPolygonLocked)
            SelectedGeometrySurface = null;
    }

    /// <summary>Called from the canvas when in draw-line mode. Snaps to the nearest existing vertex.</summary>
    public void HandleDrawLineModeCanvasClick(Point canvasPoint)
    {
        if (!IsDrawLineMode || GeometryVertexRows.Count < 2) return;

        // Find nearest vertex in canvas pixel distance
        GeometryVertexDisplay? nearest = null;
        double bestDistSq = double.MaxValue;
        foreach (var v in GeometryVertexRows)
        {
            var vc = ConvertAxisToCanvas(new Point(v.AxisX, v.AxisY));
            var dx = vc.X - canvasPoint.X;
            var dy = vc.Y - canvasPoint.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 < bestDistSq) { bestDistSq = d2; nearest = v; }
        }
        if (nearest == null) return;

        if (DrawLineFirstLabel == null)
        {
            DrawLineFirstLabel = nearest.PointLabel;
            var vc = ConvertAxisToCanvas(new Point(nearest.AxisX, nearest.AxisY));
            DrawLinePreviewX1 = vc.X;
            DrawLinePreviewY1 = vc.Y;
            DrawLinePreviewX2 = vc.X;
            DrawLinePreviewY2 = vc.Y;
            DrawLineModeStatus = $"First point: {nearest.PointLabel}. Now click the second vertex.";
        }
        else
        {
            if (nearest.PointLabel == DrawLineFirstLabel)
            {
                DrawLineModeStatus = "Select a different vertex for the second point.";
                return;
            }
            var fromV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == DrawLineFirstLabel);
            if (fromV == null) { CancelDrawLineMode(); return; }
            var sa = fromV.StablePointId;
            var sb = nearest.StablePointId;
            if (HasInternalEdgeStablePair(sa, sb))
            {
                DrawLineModeStatus = "That connection already exists. Click a different vertex or cancel.";
                return;
            }
            GeometryInternalEdges.Add(new GeometryInternalEdge(sa, sb));
            RefreshGeometryLineSegments();
            DrawLineModeStatus =
                $"Line added: {DrawLineFirstLabel} → {nearest.PointLabel}. Click the first vertex for the next segment, or Finish.";
            DrawLineFirstLabel = null;
        }
    }

    // ---- Draw-arc mode (3 clicks: start → through → end) ----
    private bool _isDrawArcMode;
    private string? _arcFirstLabel;   // start vertex
    private string? _arcMidLabel;     // through vertex (defines the arc)
    private string _drawArcStatus = "";

    public bool IsDrawArcMode
    {
        get => _isDrawArcMode;
        private set { _isDrawArcMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDrawArcPreviewVisible)); }
    }
    public string DrawArcStatus
    {
        get => _drawArcStatus;
        private set { _drawArcStatus = value ?? ""; OnPropertyChanged(); }
    }
    public bool IsDrawArcPreviewVisible => _isDrawArcMode && _arcFirstLabel != null;

    // Preview arc as a sampled PointCollection
    private System.Windows.Media.PointCollection _drawArcPreviewPoints = new();
    public System.Windows.Media.PointCollection DrawArcPreviewPoints
    {
        get => _drawArcPreviewPoints;
        private set { _drawArcPreviewPoints = value; OnPropertyChanged(); }
    }

    // ---- Draw-circle mode (center click, drag radius, release) → closed polygon as internal edges / line segments ----
    private const double MinGeometryCircleRadiusAxis = 1e-9;

    private bool _isDrawCircleMode;
    private Point? _drawCircleCenterCanvas;
    private double _drawCircleRadiusCanvas = double.NaN;
    private double _drawCirclePreviewCenterX;
    private double _drawCirclePreviewCenterY;
    private double _drawCirclePreviewDiameter;
    private double _drawCirclePreviewNegativeRadius;
    private string _drawCirclePreviewRadiusText = "";
    private string _drawCircleStatus = "";

    public bool IsDrawCircleMode
    {
        get => _isDrawCircleMode;
        private set
        {
            if (_isDrawCircleMode == value) return;
            _isDrawCircleMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDrawCirclePreviewVisible));
            OnPropertyChanged(nameof(CanDeleteLineSegment));
        }
    }

    public string DrawCircleStatus
    {
        get => _drawCircleStatus;
        private set { _drawCircleStatus = value ?? ""; OnPropertyChanged(); }
    }

    public bool IsDrawCirclePreviewVisible => _isDrawCircleMode && _drawCircleCenterCanvas.HasValue;

    /// <summary>True after first click placed the circle center (dragging radius).</summary>
    public bool HasGeometryCircleCenter => _drawCircleCenterCanvas.HasValue;

    public double DrawCirclePreviewCenterX
    {
        get => _drawCirclePreviewCenterX;
        private set { _drawCirclePreviewCenterX = value; OnPropertyChanged(); }
    }

    public double DrawCirclePreviewCenterY
    {
        get => _drawCirclePreviewCenterY;
        private set { _drawCirclePreviewCenterY = value; OnPropertyChanged(); }
    }

    public double DrawCirclePreviewDiameter
    {
        get => _drawCirclePreviewDiameter;
        private set { _drawCirclePreviewDiameter = value; OnPropertyChanged(); }
    }

    public double DrawCirclePreviewNegativeRadius
    {
        get => _drawCirclePreviewNegativeRadius;
        private set { _drawCirclePreviewNegativeRadius = value; OnPropertyChanged(); }
    }

    public string DrawCirclePreviewRadiusText
    {
        get => _drawCirclePreviewRadiusText;
        private set { _drawCirclePreviewRadiusText = value ?? ""; OnPropertyChanged(); }
    }

    private void StartDrawArcMode()
    {
        CancelBulkPointDeleteMode();
        CancelDrawLineMode();   // ensure draw-line mode is off
        CancelDrawCircleMode();
        IsDrawArcMode = true;
        _arcFirstLabel = null;
        _arcMidLabel = null;
        DrawArcStatus = "Click the first (start) vertex on the canvas.";
        DrawArcPreviewPoints = new System.Windows.Media.PointCollection();
    }

    private void CancelDrawArcMode()
    {
        IsDrawArcMode = false;
        _arcFirstLabel = null;
        _arcMidLabel = null;
        DrawArcStatus = "";
        DrawArcPreviewPoints = new System.Windows.Media.PointCollection();
    }

    private void StartDrawCircleMode()
    {
        CancelBulkPointDeleteMode();
        CancelDrawLineMode();
        CancelDrawArcMode();
        if (IsTunnelCircleDrawingMode)
            ClearTunnelCirclePreview();
        CancelDrawCircleMode();
        IsDrawCircleMode = true;
        DrawCircleStatus = "Click the sketch for center, drag to set radius, release to finish.";
    }

    private void CancelDrawCircleMode()
    {
        IsDrawCircleMode = false;
        ClearDrawCirclePreviewState();
        DrawCircleStatus = "";
        Mouse.Capture(null);
    }

    private void ClearDrawCirclePreviewState()
    {
        _drawCircleCenterCanvas = null;
        _drawCircleRadiusCanvas = double.NaN;
        DrawCirclePreviewCenterX = 0;
        DrawCirclePreviewCenterY = 0;
        DrawCirclePreviewDiameter = 0;
        DrawCirclePreviewNegativeRadius = 0;
        DrawCirclePreviewRadiusText = "";
        OnPropertyChanged(nameof(IsDrawCirclePreviewVisible));
        OnPropertyChanged(nameof(HasGeometryCircleCenter));
    }

    /// <summary>First mouse down in draw-circle mode: fix center on canvas.</summary>
    public void StartGeometryCircleCenter(Point canvasCenter)
    {
        if (!IsDrawCircleMode) return;
        _drawCircleCenterCanvas = canvasCenter;
        _drawCircleRadiusCanvas = 0.1;
        DrawCirclePreviewCenterX = canvasCenter.X;
        DrawCirclePreviewCenterY = canvasCenter.Y;
        DrawCirclePreviewDiameter = _drawCircleRadiusCanvas * 2;
        DrawCirclePreviewNegativeRadius = -_drawCircleRadiusCanvas;
        DrawCirclePreviewRadiusText = "R: 0.00 m";
        DrawCircleStatus = "Drag to set radius, release to add circle to line segments.";
        OnPropertyChanged(nameof(IsDrawCirclePreviewVisible));
        OnPropertyChanged(nameof(HasGeometryCircleCenter));
    }

    /// <summary>Mouse move while dragging circle radius.</summary>
    public void UpdateGeometryCirclePreview(Point currentCanvas)
    {
        if (!IsDrawCircleMode || !_drawCircleCenterCanvas.HasValue) return;
        var center = _drawCircleCenterCanvas.Value;
        var centerAxis = ConvertCanvasToAxis(center);
        var currentAxis = ConvertCanvasToAxis(currentCanvas);
        var dxAxis = currentAxis.X - centerAxis.X;
        var dyAxis = currentAxis.Y - centerAxis.Y;
        var radiusAxis = Math.Sqrt(dxAxis * dxAxis + dyAxis * dyAxis);
        _drawCircleRadiusCanvas = ConvertAxisRadiusToCanvas(centerAxis, radiusAxis);
        DrawCirclePreviewDiameter = _drawCircleRadiusCanvas * 2;
        DrawCirclePreviewNegativeRadius = -_drawCircleRadiusCanvas;
        DrawCirclePreviewRadiusText = radiusAxis > MinGeometryCircleRadiusAxis ? $"R: {radiusAxis:F2} m" : "R: 0.00 m";
    }

    /// <summary>Mouse up: commit circle as vertices + internal edges → <see cref="GeometryLineSegments"/>.</summary>
    public void FinishGeometryCircleDrawing()
    {
        if (!IsDrawCircleMode || !_drawCircleCenterCanvas.HasValue)
        {
            CancelDrawCircleMode();
            return;
        }

        if (double.IsNaN(_drawCircleRadiusCanvas) || _drawCircleRadiusCanvas <= 1e-9)
        {
            CancelDrawCircleMode();
            return;
        }

        var centerCanvas = _drawCircleCenterCanvas.Value;
        var radiusAxis = ConvertCanvasRadiusToAxis(centerCanvas, _drawCircleRadiusCanvas);
        if (radiusAxis < MinGeometryCircleRadiusAxis)
        {
            CancelDrawCircleMode();
            return;
        }

        var centerAxis = ConvertCanvasToAxis(centerCanvas);
        GeometryInternalCircles.Add(new GeometryInternalCircle
        {
            Id = _nextInternalCircleId++,
            CenterAxisX = centerAxis.X,
            CenterAxisY = centerAxis.Y,
            RadiusAxis = radiusAxis
        });
        RefreshGeometryLineSegments();
        CancelDrawCircleMode();
    }

    /// <summary>Called from the canvas when in draw-arc mode. Three clicks: start, through, end.</summary>
    public void HandleDrawArcModeCanvasClick(Point canvasPoint)
    {
        if (!IsDrawArcMode || GeometryVertexRows.Count < 2) return;

        var nearest = FindNearestVertex(canvasPoint);
        if (nearest == null) return;

        if (_arcFirstLabel == null)
        {
            _arcFirstLabel = nearest.PointLabel;
            DrawArcStatus = $"Start: {nearest.PointLabel}. Click the through (middle) vertex.";
            UpdateArcPreview(canvasPoint);
        }
        else if (_arcMidLabel == null)
        {
            if (nearest.PointLabel == _arcFirstLabel)
            {
                DrawArcStatus = "Choose a different vertex for the through point.";
                return;
            }
            _arcMidLabel = nearest.PointLabel;
            DrawArcStatus = $"Through: {nearest.PointLabel}. Click the end vertex.";
            UpdateArcPreview(canvasPoint);
        }
        else
        {
            if (nearest.PointLabel == _arcFirstLabel || nearest.PointLabel == _arcMidLabel)
            {
                DrawArcStatus = "Choose a different vertex for the end point.";
                return;
            }
            var vStart = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == _arcFirstLabel);
            var vMid   = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == _arcMidLabel);
            var vEnd   = nearest;
            if (vStart == null || vMid == null) { CancelDrawArcMode(); return; }

            GeometryInternalEdges.Add(new Models.GeometryInternalEdge(vStart.StablePointId, vMid.StablePointId, vEnd.StablePointId));
            RefreshGeometryLineSegments();
            DrawArcStatus = $"Arc created: {_arcFirstLabel} → {_arcMidLabel} → {nearest.PointLabel}.";
            CancelDrawArcMode();
        }
    }

    /// <summary>Updates the arc preview polyline while the cursor moves.</summary>
    public void UpdateArcPreview(Point cursorCanvas)
    {
        if (!IsDrawArcMode || _arcFirstLabel == null) return;

        var vStart = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == _arcFirstLabel);
        if (vStart == null) return;

        var p1 = ConvertAxisToCanvas(new Point(vStart.AxisX, vStart.AxisY));

        System.Windows.Media.PointCollection pts;
        if (_arcMidLabel == null)
        {
            // Only start chosen — draw a straight preview to cursor
            pts = new System.Windows.Media.PointCollection { p1, cursorCanvas };
        }
        else
        {
            var vMid = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == _arcMidLabel);
            if (vMid == null) return;
            var p2 = ConvertAxisToCanvas(new Point(vMid.AxisX, vMid.AxisY));
            pts = SampleArcPoints(p1, p2, cursorCanvas, 48);
        }
        DrawArcPreviewPoints = pts;
    }

    private GeometryVertexDisplay? FindNearestVertex(Point canvasPoint)
    {
        GeometryVertexDisplay? nearest = null;
        double bestDistSq = double.MaxValue;
        foreach (var v in GeometryVertexRows)
        {
            var vc = ConvertAxisToCanvas(new Point(v.AxisX, v.AxisY));
            var dx = vc.X - canvasPoint.X;
            var dy = vc.Y - canvasPoint.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 < bestDistSq) { bestDistSq = d2; nearest = v; }
        }
        return nearest;
    }

    /// <summary>Compute circle through three 2-D canvas points and return sampled polyline.</summary>
    private static System.Windows.Media.PointCollection SampleArcPoints(Point p1, Point p2, Point p3, int samples)
    {
        var pts = new System.Windows.Media.PointCollection();
        if (!TryCircleFromThreePoints(p1, p2, p3, out var cx, out var cy, out var r) || r < 1e-6)
        {
            pts.Add(p1); pts.Add(p2); pts.Add(p3);
            return pts;
        }

        double a1 = Math.Atan2(p1.Y - cy, p1.X - cx);
        double a2 = Math.Atan2(p2.Y - cy, p2.X - cx);
        double a3 = Math.Atan2(p3.Y - cy, p3.X - cx);

        // Determine sweep direction so arc goes through p2
        double sweep = NormAngle(a3 - a1);
        double sweepMid = NormAngle(a2 - a1);
        if (Math.Abs(sweepMid - sweep) > 1e-9 && sweepMid > sweep)
            sweep -= 2 * Math.PI;   // flip to go the other way

        for (int i = 0; i <= samples; i++)
        {
            double t = (double)i / samples;
            double angle = a1 + t * sweep;
            pts.Add(new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle)));
        }
        return pts;
    }

    private static bool TryCircleFromThreePoints(Point p1, Point p2, Point p3,
        out double cx, out double cy, out double r)
    {
        cx = cy = r = 0;
        double ax = p2.X - p1.X, ay = p2.Y - p1.Y;
        double bx = p3.X - p1.X, by = p3.Y - p1.Y;
        double D = 2 * (ax * by - ay * bx);
        if (Math.Abs(D) < 1e-10) return false;
        double ux = (by * (ax * ax + ay * ay) - ay * (bx * bx + by * by)) / D;
        double uy = (ax * (bx * bx + by * by) - bx * (ax * ax + ay * ay)) / D;
        cx = p1.X + ux;
        cy = p1.Y + uy;
        r  = Math.Sqrt(ux * ux + uy * uy);
        return true;
    }

    private static double NormAngle(double a)
    {
        while (a < 0) a += 2 * Math.PI;
        while (a > 2 * Math.PI) a -= 2 * Math.PI;
        return a;
    }

    /// <summary>Axis-space arc samples (same geometry as <see cref="SampleArcPoints"/>).</summary>
    private static List<Point> SampleArcAxisPolylinePoints(Point p1, Point p2, Point p3, int samples)
    {
        var pts = new List<Point>(samples + 1);
        if (!TryCircleFromThreePoints(p1, p2, p3, out var cx, out var cy, out var r) || r < 1e-6)
        {
            pts.Add(p1);
            pts.Add(p2);
            pts.Add(p3);
            return pts;
        }

        double a1 = Math.Atan2(p1.Y - cy, p1.X - cx);
        double a2 = Math.Atan2(p2.Y - cy, p2.X - cx);
        double a3 = Math.Atan2(p3.Y - cy, p3.X - cx);
        double sweep = NormAngle(a3 - a1);
        double sweepMid = NormAngle(a2 - a1);
        if (Math.Abs(sweepMid - sweep) > 1e-9 && sweepMid > sweep)
            sweep -= 2 * Math.PI;

        for (int i = 0; i <= samples; i++)
        {
            double t = (double)i / samples;
            double angle = a1 + t * sweep;
            pts.Add(new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle)));
        }

        return pts;
    }

    private static bool AxisPointsNearlyEqual(Point a, Point b) =>
        Math.Abs(a.X - b.X) < 1e-12 && Math.Abs(a.Y - b.Y) < 1e-12;

    private static void AppendPerimeterSegmentsAsDenseAxisPolygon(
        List<SurfaceAxisPerimeterSegment> segments,
        List<Point> output,
        int arcSamples)
    {
        foreach (var seg in segments)
        {
            if (!seg.IsArc)
            {
                if (output.Count == 0)
                    output.Add(seg.A);
                if (!AxisPointsNearlyEqual(output[^1], seg.B))
                    output.Add(seg.B);
                continue;
            }

            var arcPts = SampleArcAxisPolylinePoints(seg.A, seg.B, seg.C, arcSamples);
            foreach (var p in arcPts)
            {
                if (output.Count > 0 && AxisPointsNearlyEqual(output[^1], p))
                    continue;
                output.Add(p);
            }
        }
    }

    /// <summary>
    /// Closed boundary in axis space for meshing: straight edges and arc edges (sampled), so the interior
    /// includes the true curved boundary — not the chord through arc endpoints.
    /// </summary>
    private bool TryGetSurfaceAxisPolygonDenseForSurface(GeometrySurface surface, out List<Point> axisPoints)
    {
        axisPoints = new List<Point>();
        if (IsCircleOnlySurface(surface))
        {
            var seg = TryGetCircleOnlySegment(surface);
            if (seg is not { IsCircle: true }) return false;
            double cx = seg.CircleCenterAxisX, cy = seg.CircleCenterAxisY, r = seg.CircleRadiusAxis;
            for (int i = 0; i < 64; i++)
            {
                double ang = i * 2.0 * Math.PI / 64;
                axisPoints.Add(new Point(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang)));
            }

            return axisPoints.Count >= 3;
        }

        if (!TryGetSurfacePerimeterAxisSegments(surface.SurfaceNumber, out var segs) || segs.Count == 0)
            return false;

        AppendPerimeterSegmentsAsDenseAxisPolygon(segs, axisPoints, arcSamples: 48);
        return axisPoints.Count >= 3;
    }

    public void UpdateGeometryCanvasCoordinates(Point canvasPoint)
    {
        GeometryCanvasCoordinates = FormatCoordinate(ConvertCanvasToAxis(canvasPoint));
        // Update the live endpoint of the draw-line preview
        if (_isDrawLineMode && _drawLineFirstLabel != null && !double.IsNaN(canvasPoint.X))
        {
            DrawLinePreviewX2 = canvasPoint.X;
            DrawLinePreviewY2 = canvasPoint.Y;
        }
        // Update live arc preview
        if (_isDrawArcMode && _arcFirstLabel != null && !double.IsNaN(canvasPoint.X))
            UpdateArcPreview(canvasPoint);
    }

    /// <summary>
    /// Called by the canvas when the user clicks in excavation mode.
    /// <paramref name="modelCanvasPoint"/> is in model canvas space (0…<see cref="CanvasWidth"/> / Height),
    /// i.e. after <c>ScreenToWorld</c> on the mouse position — same space as <see cref="ConvertAxisToCanvas"/>.
    /// Performs a point-in-polygon test in axis space, then delegates to <see cref="StagedConstructionViewModel.CommitExcavation"/>.
    /// </summary>
    public void ExcavateAtPoint(Point modelCanvasPoint)
    {
        if (StagedConstruction == null || !IsExcavationMode) return;

        // Hit-test in model canvas space (same as <see cref="SlopeCanvasView"/> after ScreenToWorld),
        // matching overlay vertex coordinates exactly. Axis-space tests can miss due to float drift.
        GeometrySurface? hit = null;
        PointCollection? hitCanvas = null;
        List<(double X, double Y)>? hitAxis = null;
        double hitCx = 0, hitCy = 0;

        foreach (var surface in GeometrySurfaces.OrderBy(s => s.SurfaceNumber))
        {
            if (!TryBuildSurfaceOverlayGeometry(surface, out var canvasPoly, out var cx, out var cy) || canvasPoly.Count < 3)
                continue;

            if (!PointInPolygonModelCanvas(modelCanvasPoint, canvasPoly))
                continue;

            var axisPoly = new List<(double X, double Y)>(canvasPoly.Count);
            foreach (Point cp in canvasPoly)
            {
                var ap = ConvertCanvasToAxis(cp);
                axisPoly.Add((ap.X, ap.Y));
            }

            hit       = surface;
            hitCanvas = canvasPoly;
            hitAxis   = axisPoly;
            hitCx     = cx;
            hitCy     = cy;
            // Don't break — keep the innermost (highest-numbered) surface that contains the point
        }

        if (hit == null) return;

        StagedConstruction.CommitExcavation(
            hit.SurfaceNumber, hitCanvas!, hitAxis!, hitCx, hitCy);

        // Optionally refresh the surface fill so excavated surfaces show white instantly
        RefreshSurfaceRegionOverlaysWithExcavations();
    }

    /// <summary>Ray-casting point-in-polygon in model canvas coordinates (skips horizontal edges).</summary>
    private static bool PointInPolygonModelCanvas(Point p, PointCollection poly)
    {
        int n = poly.Count;
        if (n < 3) return false;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = poly[i].X, yi = poly[i].Y;
            double xj = poly[j].X, yj = poly[j].Y;
            if (Math.Abs(yj - yi) < 1e-12)
                continue;
            if ((yi > p.Y) != (yj > p.Y))
            {
                double xInt = (xj - xi) * (p.Y - yi) / (yj - yi) + xi;
                if (p.X < xInt)
                    inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// Refreshes surface region overlays, overriding excavated-surface fills with white.
    /// Called immediately after an excavation is committed.
    /// </summary>
    private void RefreshSurfaceRegionOverlaysWithExcavations()
    {
        if (StagedConstruction == null)
        {
            RefreshSurfaceRegionOverlays();
            return;
        }
        var excavated = StagedConstruction.ExcavatedSurfacesUpToActiveStage;
        // Use existing material assignment preview mechanism: temporarily inject white fill
        // for every excavated surface then call the standard refresh.
        // Simpler: just call the standard refresh — the canvas overlay ItemsControl for
        // ExcavatedStageZones (solid white) draws on top, so the surface color beneath
        // doesn't matter visually.
        RefreshSurfaceRegionOverlays();
    }
    public void StartTunnelCircleDrawing(Point centerCanvas)
    {
        if (!IsTunnelCircleDrawingMode) return;
        TunnelCirclePreviewCenter = centerCanvas;
        _tunnelCirclePreviewRadius = 0.1;
        TunnelCirclePreviewCenterX = centerCanvas.X;
        TunnelCirclePreviewCenterY = centerCanvas.Y;
        TunnelCirclePreviewDiameter = _tunnelCirclePreviewRadius * 2;
        TunnelCirclePreviewNegativeRadius = -_tunnelCirclePreviewRadius;
        IsTunnelCirclePreviewVisible = true;
        TunnelCirclePreviewRadiusText = "R: 0.00 m";
        OnPropertyChanged(nameof(TunnelCirclePreviewCenter));
    }
    public void MoveTunnelCircle(int index, Point p) { }
    public void MoveBoxMethodCircleCenter(Point p) { }
    public void FinishTunnelCircleDrawing(Point p = default)
    {
        if (!IsTunnelCircleDrawingMode || !TunnelCirclePreviewCenter.HasValue || double.IsNaN(_tunnelCirclePreviewRadius) || _tunnelCirclePreviewRadius <= 0)
        {
            ClearTunnelCirclePreview();
            return;
        }
        var centerCanvas = TunnelCirclePreviewCenter.Value;
        var radiusCanvas = _tunnelCirclePreviewRadius;
        var centerAxis = ConvertCanvasToAxis(centerCanvas);
        var radiusAxis = ConvertCanvasRadiusToAxis(centerCanvas, radiusCanvas);
        var fullCircle = new PointCollection();
        const int samples = 64;
        for (var i = 0; i <= samples; i++)
        {
            var angle = i * 2.0 * Math.PI / samples;
            fullCircle.Add(new Point(centerCanvas.X + radiusCanvas * Math.Cos(angle), centerCanvas.Y + radiusCanvas * Math.Sin(angle)));
        }
        var circle = new TunnelExcavationCircle
        {
            Center = centerCanvas,
            Radius = radiusCanvas
        };
        circle.ClippedSegments.Add(fullCircle);
        FilteredTunnelExcavationCircles.Add(circle);
        var row = new TunnelExcavationCircleRow
        {
            CircleNumber = TunnelExcavationCircleRows.Count + 1,
            CenterX = centerAxis.X,
            CenterY = centerAxis.Y,
            Radius = radiusAxis,
            UnitLabel = ProjectUnitSystem.LengthLabel(ProjectSettings?.Project.Units),
            Circle = circle
        };
        TunnelExcavationCircleRows.Add(row);
        FilteredTunnelExcavationCircleRows.Add(row);
        TunnelCenterXInput = centerAxis.X.ToString("F3", CultureInfo.InvariantCulture);
        TunnelCenterYInput = centerAxis.Y.ToString("F3", CultureInfo.InvariantCulture);
        TunnelRadiusInput = radiusAxis.ToString("F3", CultureInfo.InvariantCulture);
        ClearTunnelCirclePreview();
    }
    private void ClearTunnelCirclePreview()
    {
        TunnelCirclePreviewCenter = null;
        _tunnelCirclePreviewRadius = double.NaN;
        IsTunnelCircleDrawingMode = false;
        TunnelCirclePreviewCenterX = 0;
        TunnelCirclePreviewCenterY = 0;
        TunnelCirclePreviewDiameter = 0;
        TunnelCirclePreviewNegativeRadius = 0;
        IsTunnelCirclePreviewVisible = false;
        TunnelCirclePreviewRadiusText = "";
        OnPropertyChanged(nameof(TunnelCirclePreviewCenter));
        OnPropertyChanged(nameof(IsTunnelCircleDrawingMode));
    }
    public double CurrentZoomFactor
    {
        get => _currentZoomFactor;
        private set
        {
            if (Math.Abs(_currentZoomFactor - value) < 1e-9) return;
            _currentZoomFactor = value;
            OnPropertyChanged();
        }
    }

    public void UpdateViewTransform(Matrix viewMatrix, double zoomFactor)
    {
        CurrentZoomFactor = zoomFactor;
        _viewMatrix = viewMatrix;
        UpdateAxisTicksForView(viewMatrix);
    }

    public void UpdateZoom(double zoomFactor)
    {
        UpdateViewTransform(_viewMatrix, zoomFactor);
    }
    public void UpdateTunnelCirclePreview(Point currentCanvas)
    {
        if (!IsTunnelCircleDrawingMode || !TunnelCirclePreviewCenter.HasValue) return;
        var center = TunnelCirclePreviewCenter.Value;
        var centerAxis = ConvertCanvasToAxis(center);
        var currentAxis = ConvertCanvasToAxis(currentCanvas);
        var dxAxis = currentAxis.X - centerAxis.X;
        var dyAxis = currentAxis.Y - centerAxis.Y;
        var radiusAxis = Math.Sqrt(dxAxis * dxAxis + dyAxis * dyAxis);
        _tunnelCirclePreviewRadius = ConvertAxisRadiusToCanvas(centerAxis, radiusAxis);
        TunnelCirclePreviewDiameter = _tunnelCirclePreviewRadius * 2;
        TunnelCirclePreviewNegativeRadius = -_tunnelCirclePreviewRadius;
        TunnelCirclePreviewRadiusText = radiusAxis > 0 ? $"R: {radiusAxis:F2} m" : "R: 0.00 m";
    }
    /// <summary>Adds a geometry vertex at the given canvas position (from mouse click). If the point lies on an existing segment, inserts on that edge and splits it into two consecutive line segments.</summary>
    public void AddGeometryVertex(Point canvasPoint)
    {
        RecordUndo("Add geometry vertex");
        var axisPoint = ConvertCanvasToAxis(canvasPoint);
        if (TryFindInsertOnExistingSegment(axisPoint, out var insertIndex, out var splitSegmentIndex, out var snappedAxis))
        {
            AddVertexAtInsertIndex(insertIndex, snappedAxis, splitSegmentIndex);
            GeometryCanvasCoordinates = FormatCoordinate(snappedAxis);
        }
        else
        {
            var idx = GeometryVertexRows.Count;
            var display = new GeometryVertexDisplay { Index = idx, AxisX = axisPoint.X, AxisY = axisPoint.Y };
            display.StablePointId = AllocateStablePointId();
            display.Owner = this;
            GeometryVertexRows.Add(display);
            if (TryRelocateLastVertexOntoBoundaryIfInterior(out var snappedReloc))
                GeometryCanvasCoordinates = FormatCoordinate(snappedReloc);
            else
            {
                RefreshGeometryPath();
                RefreshAvailablePointLabels();
                RefreshGeometryLineSegments();
                GeometryCanvasCoordinates = FormatCoordinate(axisPoint);
            }
        }

        TryAppendDraftSurfaceVertexAfterGeometryClick(axisPoint);
    }

    /// <summary>Appends a geometry vertex at exact axis coordinates (Python / automation; no canvas snap).</summary>
    public void AddVertexAxis(double x, double y)
    {
        var idx = GeometryVertexRows.Count;
        var display = new GeometryVertexDisplay { Index = idx, AxisX = x, AxisY = y };
        display.StablePointId = AllocateStablePointId();
        display.Owner = this;
        GeometryVertexRows.Add(display);
    }

    /// <summary>Replaces the outer ring with axis vertices and closes the polygon for meshing.</summary>
    public void SetOutlineAxis(IReadOnlyList<(double X, double Y)> points)
    {
        if (points == null || points.Count < 3)
            throw new ArgumentException("Outline needs at least three axis points.");

        GeometryVertexRows.Clear();
        ResetStablePointIdCounter();
        foreach (var (x, y) in points)
            AddVertexAxis(x, y);

        IsOpenPolygonBoundaryMode = false;
        IsNewPointMode = true;
        FitAxisBoundsToVertexExtents();
        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments();
        EnsureEmbankmentMacroSurfaceAndAxisFit();
    }

    private void StartBulkPointDeleteMode()
    {
        CancelDrawLineMode();
        CancelDrawArcMode();
        CancelDrawCircleMode();
        if (IsTunnelCircleDrawingMode)
            ClearTunnelCirclePreview();
        if (IsMaterialAssignMode)
            StopMaterialAssignMode();

        IsExcavationMode = false;
        IsNewPointMode = false;
        IsGeometryVerticesEditMode = false;
        ClearBulkPointDeleteSelection();
        IsBulkPointDeleteMode = true;
        BulkPointDeleteStatus = "Click a point to delete it, or drag a box to delete enclosed points.";
    }

    private void CancelBulkPointDeleteMode()
    {
        IsBulkPointDeleteMode = false;
        ClearBulkPointDeleteSelection();
        SetHighlightedVertices(Array.Empty<int>());
        BulkPointDeleteStatus = "";
    }

    public void UpdateBulkPointDeleteSelection(Rect canvasRect)
    {
        var normalized = NormalizeRect(canvasRect);
        BulkPointDeleteSelectionLeft = normalized.Left;
        BulkPointDeleteSelectionTop = normalized.Top;
        BulkPointDeleteSelectionWidth = normalized.Width;
        BulkPointDeleteSelectionHeight = normalized.Height;
        IsBulkPointDeleteSelectionVisible = normalized.Width > 0 && normalized.Height > 0;

        var selected = new List<int>();
        for (var i = 0; i < GeometryVertexRows.Count; i++)
        {
            var v = GeometryVertexRows[i];
            var p = ConvertAxisToCanvas(new Point(v.AxisX, v.AxisY));
            if (normalized.Contains(p))
                selected.Add(i);
        }
        SetHighlightedVertices(selected);
        BulkPointDeleteStatus = selected.Count == 0
            ? "Drag a box around points to delete, or click a point."
            : $"Release to delete {selected.Count} point(s).";
    }

    public void ClearBulkPointDeleteSelection()
    {
        BulkPointDeleteSelectionLeft = 0;
        BulkPointDeleteSelectionTop = 0;
        BulkPointDeleteSelectionWidth = 0;
        BulkPointDeleteSelectionHeight = 0;
        IsBulkPointDeleteSelectionVisible = false;
    }

    public int DeleteNearestGeometryVertex(Point canvasPoint, double toleranceCanvasPx)
    {
        if (!IsBulkPointDeleteMode || GeometryVertexRows.Count == 0) return 0;

        var toleranceSq = toleranceCanvasPx * toleranceCanvasPx;
        var bestDistSq = double.MaxValue;
        var bestIndex = -1;
        for (var i = 0; i < GeometryVertexRows.Count; i++)
        {
            var v = GeometryVertexRows[i];
            var p = ConvertAxisToCanvas(new Point(v.AxisX, v.AxisY));
            var dx = p.X - canvasPoint.X;
            var dy = p.Y - canvasPoint.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 < bestDistSq)
            {
                bestDistSq = d2;
                bestIndex = i;
            }
        }

        if (bestIndex < 0 || bestDistSq > toleranceSq)
        {
            SetHighlightedVertices(Array.Empty<int>());
            BulkPointDeleteStatus = "No point found near click. Click closer to a point, or drag a box.";
            return 0;
        }

        var label = GeometryVertexRows[bestIndex].PointLabel;
        RemoveVerticesAtIndices(new[] { bestIndex });
        BulkPointDeleteStatus = $"Deleted {label}.";
        return 1;
    }

    public int DeleteGeometryVerticesInCanvasRect(Rect canvasRect)
    {
        if (!IsBulkPointDeleteMode || GeometryVertexRows.Count == 0) return 0;

        var normalized = NormalizeRect(canvasRect);
        var indices = new List<int>();
        for (var i = 0; i < GeometryVertexRows.Count; i++)
        {
            var v = GeometryVertexRows[i];
            var p = ConvertAxisToCanvas(new Point(v.AxisX, v.AxisY));
            if (normalized.Contains(p))
                indices.Add(i);
        }

        if (indices.Count == 0)
        {
            SetHighlightedVertices(Array.Empty<int>());
            BulkPointDeleteStatus = "No points inside selection box.";
            return 0;
        }

        var deleted = RemoveVerticesAtIndices(indices);
        BulkPointDeleteStatus = $"Deleted {deleted} point(s).";
        return deleted;
    }

    private int RemoveVerticesAtIndices(IEnumerable<int> indices)
    {
        var ordered = indices
            .Where(i => i >= 0 && i < GeometryVertexRows.Count)
            .Distinct()
            .OrderByDescending(i => i)
            .ToList();
        if (ordered.Count == 0) return 0;

        var removedStableIds = new HashSet<int>();
        foreach (var i in ordered)
        {
            removedStableIds.Add(GeometryVertexRows[i].StablePointId);
            GeometryVertexRows.RemoveAt(i);
        }

        for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
        {
            if (removedStableIds.Any(id => GeometryInternalEdges[i].TouchesStableId(id)))
                GeometryInternalEdges.RemoveAt(i);
        }

        for (var i = 0; i < GeometryVertexRows.Count; i++)
            GeometryVertexRows[i].Index = i;

        ClearBulkPointDeleteSelection();
        SetHighlightedVertices(Array.Empty<int>());
        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments();
        return ordered.Count;
    }

    private static Rect NormalizeRect(Rect rect)
        => new(
            Math.Min(rect.Left, rect.Right),
            Math.Min(rect.Top, rect.Bottom),
            Math.Abs(rect.Width),
            Math.Abs(rect.Height));

    public void SetHighlightedVertices(IEnumerable<int> indices)
    {
        var set = indices?.ToHashSet() ?? new HashSet<int>();
        for (int i = 0; i < GeometryVertexRows.Count; i++)
            GeometryVertexRows[i].IsHighlighted = set.Contains(i);
    }

    /// <summary>Highlights the canvas geometry that matches a Geometry Explorer leaf (point, line, surface, region, block).</summary>
    public void HighlightExplorerSelection(object? payload)
    {
        _explorerHighlightPayload = payload;
        ExplorerHighlightPolylines.Clear();

        if (payload == null)
        {
            SetHighlightedVertices(Array.Empty<int>());
            SetHighlightedLineSegment(null);
            if (SelectedGeometrySurface != null)
                SelectedGeometrySurface = null;
            return;
        }

        switch (payload)
        {
            case GeometryVertexDisplay vertex:
                SelectedGeometrySurface = null;
                SetHighlightedLineSegment(null);
                SetHighlightedVertices(new[] { vertex.Index });
                break;
            case GeometryLineSegment line:
                SelectedGeometrySurface = null;
                SetHighlightedLineSegment(line.LineNumber);
                HighlightPointLabels(line.FromPoint, line.ToPoint, line.ThroughPoint);
                break;
            case GeometrySurface surface:
                SetHighlightedLineSegment(null);
                SelectedGeometrySurface = surface;
                HighlightSurfaceParameters(surface);
                break;
            case GeologicalFeature feature:
                SelectedGeometrySurface = null;
                SetHighlightedLineSegment(null);
                HighlightGeologicalFeature(feature);
                break;
            case string id when string.Equals(id, SoilBlockExplorerPayload, StringComparison.Ordinal):
                SelectedGeometrySurface = null;
                SetHighlightedLineSegment(null);
                HighlightSoilBlock();
                break;
            default:
                SetHighlightedVertices(Array.Empty<int>());
                SetHighlightedLineSegment(null);
                break;
        }
    }

    private void RebuildExplorerHighlight()
    {
        if (_explorerHighlightPayload == null) return;
        HighlightExplorerSelection(_explorerHighlightPayload);
    }

    private void HighlightPointLabels(params string?[] labels)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            if (!string.IsNullOrWhiteSpace(label) && label != "—")
                wanted.Add(label);
        }

        var indices = new List<int>();
        for (var i = 0; i < GeometryVertexRows.Count; i++)
        {
            if (wanted.Contains(GeometryVertexRows[i].PointLabel))
                indices.Add(i);
        }
        SetHighlightedVertices(indices);
    }

    private void HighlightSurfaceParameters(GeometrySurface surface)
    {
        var lineNums = surface.PickedLineNumbers.Count > 0
            ? surface.PickedLineNumbers.ToList()
            : surface.ConnectedLineNumbers.ToList();
        foreach (var n in lineNums)
        {
            var poly = CanvasPolylineForLineNumber(n);
            if (poly.Count >= 2)
                ExplorerHighlightPolylines.Add(poly);
        }

        var labels = new List<string>();
        if (surface.LockedPolygonVertexLabels.Count > 0)
            labels.AddRange(surface.LockedPolygonVertexLabels);
        else if (surface.DraftPolygonVertexLabels.Count > 0)
            labels.AddRange(surface.DraftPolygonVertexLabels);
        else
        {
            foreach (var n in lineNums)
            {
                var seg = GeometryLineSegments.FirstOrDefault(s => s.LineNumber == n);
                if (seg == null) continue;
                labels.Add(seg.FromPoint);
                labels.Add(seg.ToPoint);
            }
        }

        HighlightPointLabels(labels.ToArray());
        OnPropertyChanged(nameof(ExplorerHighlightPolylines));
    }

    private void HighlightGeologicalFeature(GeologicalFeature feature)
    {
        var poly = new PointCollection();
        var labels = new List<string>();
        foreach (var (x, y) in feature.Vertices)
        {
            poly.Add(ConvertAxisToCanvas(new Point(x, y)));
            var match = GeometryVertexRows.FirstOrDefault(v =>
                Math.Abs(v.AxisX - x) < 1e-6 && Math.Abs(v.AxisY - y) < 1e-6);
            if (match != null)
                labels.Add(match.PointLabel);
        }
        if (poly.Count >= 2)
            ExplorerHighlightPolylines.Add(poly);
        HighlightPointLabels(labels.ToArray());
        OnPropertyChanged(nameof(ExplorerHighlightPolylines));
    }

    private void HighlightSoilBlock()
    {
        var indices = Enumerable.Range(0, GeometryVertexRows.Count).ToList();
        SetHighlightedVertices(indices);
        var ring = new PointCollection(GeometryPathPoints);
        if (ring.Count >= 3)
        {
            ring.Add(ring[0]);
            ExplorerHighlightPolylines.Add(ring);
        }
        OnPropertyChanged(nameof(ExplorerHighlightPolylines));
    }

    private PointCollection CanvasPolylineForLineNumber(int lineNumber)
    {
        var points = new PointCollection();
        var seg = GeometryLineSegments.FirstOrDefault(s => s.LineNumber == lineNumber);
        if (seg == null) return points;
        if (seg.IsCircle)
            return SampleCircleCanvas(seg.CircleCenterAxisX, seg.CircleCenterAxisY, seg.CircleRadiusAxis);
        var fromVertex = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
        var toVertex = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
        if (fromVertex == null || toVertex == null) return points;
        if (seg.IsArc && !string.IsNullOrEmpty(seg.ThroughPoint))
        {
            var midVertex = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ThroughPoint);
            if (midVertex != null)
            {
                return SampleArcPoints(
                    ConvertAxisToCanvas(new Point(fromVertex.AxisX, fromVertex.AxisY)),
                    ConvertAxisToCanvas(new Point(midVertex.AxisX, midVertex.AxisY)),
                    ConvertAxisToCanvas(new Point(toVertex.AxisX, toVertex.AxisY)),
                    48);
            }
        }
        points.Add(ConvertAxisToCanvas(new Point(fromVertex.AxisX, fromVertex.AxisY)));
        points.Add(ConvertAxisToCanvas(new Point(toVertex.AxisX, toVertex.AxisY)));
        return points;
    }

    private PointCollection SampleCircleCanvas(double cx, double cy, double r)
    {
        var points = new PointCollection();
        var cc = ConvertAxisToCanvas(new Point(cx, cy));
        var rim = ConvertAxisToCanvas(new Point(cx + r, cy));
        var rCanvas = Math.Sqrt((rim.X - cc.X) * (rim.X - cc.X) + (rim.Y - cc.Y) * (rim.Y - cc.Y));
        for (var i = 0; i <= 64; i++)
        {
            var ang = i * 2.0 * Math.PI / 64;
            points.Add(new Point(cc.X + rCanvas * Math.Cos(ang), cc.Y + rCanvas * Math.Sin(ang)));
        }
        return points;
    }

    public void SetHighlightedLineSegment(int? lineNumber)
    {
        HighlightedLineNumber = lineNumber;
        foreach (var seg in GeometryLineSegments)
            seg.IsHighlighted = seg.LineNumber == lineNumber;
        RefreshHighlightedSegmentPoints();
    }

    private void RefreshHighlightedSegmentPoints()
    {
        var points = new PointCollection();
        if (HighlightedLineNumber.HasValue)
        {
            var seg = GeometryLineSegments.FirstOrDefault(s => s.LineNumber == HighlightedLineNumber.Value);
            if (seg != null)
            {
                if (seg.IsCircle)
                {
                    var cx = seg.CircleCenterAxisX;
                    var cy = seg.CircleCenterAxisY;
                    var r = seg.CircleRadiusAxis;
                    var cc = ConvertAxisToCanvas(new Point(cx, cy));
                    var rim = ConvertAxisToCanvas(new Point(cx + r, cy));
                    var rdx = rim.X - cc.X;
                    var rdy = rim.Y - cc.Y;
                    var rCanvas = Math.Sqrt(rdx * rdx + rdy * rdy);
                    for (var i = 0; i <= 64; i++)
                    {
                        var ang = i * 2.0 * Math.PI / 64;
                        points.Add(new Point(cc.X + rCanvas * Math.Cos(ang), cc.Y + rCanvas * Math.Sin(ang)));
                    }
                }
                else
                {
                var fromVertex = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
                var toVertex = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
                if (fromVertex != null && toVertex != null)
                {
                    if (seg.IsArc && !string.IsNullOrEmpty(seg.ThroughPoint))
                    {
                        var midVertex = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ThroughPoint);
                        if (midVertex != null)
                        {
                            var p1 = ConvertAxisToCanvas(new Point(fromVertex.AxisX, fromVertex.AxisY));
                            var p2 = ConvertAxisToCanvas(new Point(midVertex.AxisX, midVertex.AxisY));
                            var p3 = ConvertAxisToCanvas(new Point(toVertex.AxisX, toVertex.AxisY));
                            points = SampleArcPoints(p1, p2, p3, 64);
                        }
                        else
                        {
                            points.Add(ConvertAxisToCanvas(new Point(fromVertex.AxisX, fromVertex.AxisY)));
                            points.Add(ConvertAxisToCanvas(new Point(toVertex.AxisX, toVertex.AxisY)));
                        }
                    }
                    else
                    {
                        points.Add(ConvertAxisToCanvas(new Point(fromVertex.AxisX, fromVertex.AxisY)));
                        points.Add(ConvertAxisToCanvas(new Point(toVertex.AxisX, toVertex.AxisY)));
                    }
                }
                }
            }
        }
        HighlightedSegmentPoints = points;
        OnPropertyChanged(nameof(HighlightedSegmentPoints));
    }

    private static readonly Color[] SurfaceHighlightPalette =
    {
        Color.FromArgb(170, 99, 102, 241),
        Color.FromArgb(170, 16, 185, 129),
        Color.FromArgb(170, 245, 158, 11),
        Color.FromArgb(170, 236, 72, 153),
        Color.FromArgb(170, 59, 130, 246),
        Color.FromArgb(170, 168, 85, 247),
        Color.FromArgb(170, 239, 68, 68),
        Color.FromArgb(170, 20, 184, 166)
    };

    private static int ResolveSurfacePaletteSlot(GeometrySurface surface)
    {
        if (surface.ColorIndex >= 0)
            return surface.ColorIndex % SurfaceHighlightPalette.Length;
        return (surface.SurfaceNumber - 1) % SurfaceHighlightPalette.Length;
    }

    public bool TryHasClosedSurfacePolygon(GeometrySurface surface)
    {
        if (IsCircleOnlySurface(surface))
            return true;
        return TryGetSurfacePolygonOrderedLabels(surface, out var labels) && labels.Count >= 3;
    }

    private void ClearSurfaceBooleanCanvasResult()
    {
        _surfaceBooleanResultGeometry = null;
        _surfaceBooleanDisplayName = "";
        RegionBPolygons.Clear();
        UnionFillPolygons.Clear();
    }

    private void StartSurfaceExcavationDialog()
    {
        if (GeometrySurfaces.Count < 2)
        {
            AppDialogs.Show(
                "Add at least two closed surfaces in the Surface grid (each with a valid boundary loop) before excavating.",
                "Excavate",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dvm = new BooleanOperationDialogViewModel(this);
        var dlg = new BooleanOperationDialog(dvm) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;
        ApplySurfaceBooleanFromDialog(dvm);
    }

    private static PointCollection CopyPointCollection(PointCollection source)
    {
        var copy = new PointCollection();
        foreach (Point p in source)
        {
            copy.Add(p);
        }

        return copy;
    }

    private void UpdateRegionBPolygonsForSubtract(GeometrySurface regionB)
    {
        RegionBPolygons.Clear();
        UnionFillPolygons.Clear();
        if (TryBuildSurfaceOverlayGeometry(regionB, out var bCanvas, out _, out _))
        {
            RegionBPolygons.Add(bCanvas);
        }
    }

    private void UpdateRegionBPolygonsForMultiSubtract(IReadOnlyList<GeometrySurface> surfaces)
    {
        RegionBPolygons.Clear();
        UnionFillPolygons.Clear();
        for (var i = 1; i < surfaces.Count; i++)
        {
            if (TryBuildSurfaceOverlayGeometry(surfaces[i], out var holeCanvas, out _, out _))
            {
                RegionBPolygons.Add(holeCanvas);
            }
        }
    }

    /// <summary>
    /// Rebuilds <see cref="RegionBPolygons"/> for the given Subtract row so the Fill dialog matches the selected
    /// excavation operation. Does not clear <see cref="UnionFillPolygons"/> (user may have marked fills already).
    /// </summary>
    private void EnsureRegionBPolygonsForFillDialog(BooleanOperationRow row)
    {
        RegionBPolygons.Clear();
        if (row.TypeEnum != BooleanOperationType.Subtract)
        {
            return;
        }

        if (row.IsMultiRegion && row.ParticipatingSurfaceNumbers.Count >= 2)
        {
            var surfaces = new List<GeometrySurface>();
            foreach (var n in row.ParticipatingSurfaceNumbers)
            {
                var s = GeometrySurfaces.FirstOrDefault(x => x.SurfaceNumber == n);
                if (s == null || !TryHasClosedSurfacePolygon(s))
                {
                    return;
                }

                surfaces.Add(s);
            }

            if (surfaces.Count < 2)
            {
                return;
            }

            for (var i = 1; i < surfaces.Count; i++)
            {
                if (TryBuildSurfaceOverlayGeometry(surfaces[i], out var holeCanvas, out _, out _))
                {
                    RegionBPolygons.Add(holeCanvas);
                }
            }

            return;
        }

        var sb = GeometrySurfaces.FirstOrDefault(s => s.SurfaceNumber == row.RegionBSurfaceNumber);
        if (sb == null || !TryHasClosedSurfacePolygon(sb))
        {
            return;
        }

        if (TryBuildSurfaceOverlayGeometry(sb, out var bCanvas, out _, out _))
        {
            RegionBPolygons.Add(bCanvas);
        }
    }

    private void StartFillTunnelRegionDialog()
    {
        var row = SelectedExcavationOperation ?? (ExcavationOperations.Count > 0 ? ExcavationOperations[^1] : null);
        if (row == null || !row.IsComplete)
        {
            AppDialogs.Show(
                "Select a completed excavation operation in Excavation Operations, or add one with Excavate first.",
                "Fill regions",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (row.TypeEnum != BooleanOperationType.Subtract)
        {
            AppDialogs.Show(
                "Fill only applies to Subtract operations (excavated Region B). The selected operation is not Subtract.",
                "Fill regions",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        EnsureRegionBPolygonsForFillDialog(row);

        var dvm = new FillRegionDialogViewModel(this, row);
        if (dvm.Items.Count == 0)
        {
            AppDialogs.Show(
                "Could not build outlines for the subtracted region(s). Ensure Region B (e.g. S2) has a closed polygon on the Surface grid, then try Fill again.",
                "Fill regions",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dlg = new FillRegionDialog(dvm) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        UnionFillPolygons.Clear();
        foreach (var item in dvm.Items)
        {
            if (!item.IsSelected)
            {
                continue;
            }

            if (item.RegionIndex < 0 || item.RegionIndex >= RegionBPolygons.Count)
            {
                continue;
            }

            UnionFillPolygons.Add(CopyPointCollection(RegionBPolygons[item.RegionIndex]));
        }
    }

    private void OnDeleteExcavationOperation(int i)
    {
        if (i < 0 || i >= ExcavationOperations.Count) return;
        var removed = ExcavationOperations[i];
        ExcavationOperations.RemoveAt(i);
        RenumberExcavationOperationIds();
        if (ReferenceEquals(_selectedExcavationOperation, removed))
        {
            _selectedExcavationOperation = null;
            OnPropertyChanged(nameof(SelectedExcavationOperation));
        }
        RecomputeSurfaceBooleanFromLastOperation();
    }

    private void RemoveSelectedSurface()
    {
        var surface = SelectedGeometrySurface;
        if (surface == null) return;

        int surfaceNumber = surface.SurfaceNumber;
        GeometrySurfaces.Remove(surface);

        _surfaceMaterialFills.Remove(surfaceNumber);
        _surfaceMaterialIds.Remove(surfaceNumber);
        if (_materialAssignmentPreviewSurfaceNumber == surfaceNumber)
        {
            _materialAssignmentPreviewSurfaceNumber = null;
            _materialAssignmentPreviewFill = null;
        }

        for (int i = ExcavationOperations.Count - 1; i >= 0; i--)
        {
            var row = ExcavationOperations[i];
            if (row.RegionASurfaceNumber == surfaceNumber
                || row.RegionBSurfaceNumber == surfaceNumber
                || row.ParticipatingSurfaceNumbers.Contains(surfaceNumber))
            {
                if (ReferenceEquals(_selectedExcavationOperation, row))
                {
                    _selectedExcavationOperation = null;
                    OnPropertyChanged(nameof(SelectedExcavationOperation));
                }
                ExcavationOperations.RemoveAt(i);
            }
        }

        RenumberExcavationOperationIds();
        RecomputeSurfaceBooleanFromLastOperation();
        OnPropertyChanged(nameof(CanDeleteSelectedSurface));
    }

    private void RenumberExcavationOperationIds()
    {
        for (var i = 0; i < ExcavationOperations.Count; i++)
            ExcavationOperations[i].OperationId = i + 1;
    }

    /// <summary>Clears circle entities before loading a project (segments rebuild without circles until <see cref="RestoreGeometryInternalCircles"/>).</summary>
    public void ClearGeometryInternalCirclesBeforeLoad()
    {
        GeometryInternalCircles.Clear();
        _nextInternalCircleId = 1;
    }

    /// <summary>Loads geometry circles from project file after vertices and internal edges are restored.</summary>
    public void RestoreGeometryInternalCircles(IReadOnlyList<GeometryInternalCircle>? circles)
    {
        GeometryInternalCircles.Clear();
        if (circles == null || circles.Count == 0)
        {
            _nextInternalCircleId = 1;
            RefreshGeometryLineSegments();
            return;
        }

        var maxId = 0;
        foreach (var c in circles)
        {
            GeometryInternalCircles.Add(new GeometryInternalCircle
            {
                Id = c.Id,
                CenterAxisX = c.CenterAxisX,
                CenterAxisY = c.CenterAxisY,
                RadiusAxis = c.RadiusAxis
            });
            if (c.Id > maxId) maxId = c.Id;
        }

        _nextInternalCircleId = maxId + 1;
        RefreshGeometryLineSegments();
    }

    /// <summary>
    /// Restores excavation rows from project file or stage copy, then recomputes the canvas preview from the last row.
    /// </summary>
    public void RestoreSurfaceExcavationOperations(IReadOnlyList<SurfaceBooleanOperationSnapshot>? snapshots)
    {
        ExcavationOperations.Clear();
        _selectedExcavationOperation = null;
        OnPropertyChanged(nameof(SelectedExcavationOperation));
        ClearSurfaceBooleanCanvasResult();
        if (snapshots == null || snapshots.Count == 0)
        {
            RefreshSurfaceRegionOverlays();
            return;
        }

        foreach (var s in snapshots.OrderBy(x => x.OperationId))
        {
            var row = new BooleanOperationRow
            {
                OperationId = s.OperationId,
                OperationType = s.OperationType,
                TypeEnum = s.TypeEnum,
                RegionAName = s.RegionAName,
                RegionBName = s.RegionBName,
                ResultRegionName = s.ResultRegionName,
                RegionASurfaceNumber = s.RegionASurfaceNumber,
                RegionBSurfaceNumber = s.RegionBSurfaceNumber,
                RegionAArea = s.RegionAArea,
                RegionBArea = s.RegionBArea,
                ResultArea = s.ResultArea,
                IsComplete = s.IsComplete,
                AppliedInStageNumber = s.AppliedInStageNumber
            };
            if (s.ParticipatingSurfaceNumbers is { Count: >= 2 })
            {
                row.IsMultiRegion = true;
                foreach (var n in s.ParticipatingSurfaceNumbers)
                    row.ParticipatingSurfaceNumbers.Add(n);
            }

            ExcavationOperations.Add(row);
        }

        RenumberExcavationOperationIds();
        StagedConstruction?.SyncBooleanSubtractExcavationsFromGeometry();
        RecomputeSurfaceBooleanFromLastOperation();
    }

    private void RecomputeSurfaceBooleanFromLastOperation()
    {
        RefreshStageAwareExcavationDisplay();
    }

    /// <summary>
    /// Recomputes boolean subtract/union overlays based on the active footer stage.
    /// Stage 1 shows individual surfaces; excavation stages show cumulative boolean results.
    /// </summary>
    public void RefreshStageAwareExcavationDisplay()
    {
        RefreshInternalEdgeCanvasLines();

        if (ExcavationOperations.Count == 0)
        {
            ClearSurfaceBooleanCanvasResult();
            RefreshSurfaceRegionOverlays();
            return;
        }

        if (StagedConstruction == null || StagedConstruction.Stages.Count <= 1)
        {
            ApplySurfaceBooleanPreviewForRow(ExcavationOperations[^1]);
            return;
        }

        int activeStageNum = StagedConstruction.ActiveStageNumber;
        if (activeStageNum < 2)
        {
            ClearSurfaceBooleanCanvasResult();
            RefreshSurfaceRegionOverlays();
            return;
        }

        var applicable = ExcavationOperations
            .Where(r => ResolveEffectiveAppliedStageNumber(r) <= activeStageNum)
            .ToList();

        if (applicable.Count == 0)
        {
            ClearSurfaceBooleanCanvasResult();
            RefreshSurfaceRegionOverlays();
            return;
        }

        var subtractOps = applicable.Where(r => r.TypeEnum == BooleanOperationType.Subtract).ToList();
        if (subtractOps.Count == 0)
        {
            ApplySurfaceBooleanPreviewForRow(applicable[^1]);
            return;
        }

        var excavated = GetCumulativeExcavatedSurfaceNumbers();
        if (excavated.Count == 0)
        {
            ApplySurfaceBooleanPreviewForRow(subtractOps[^1]);
            return;
        }

        int baseSurface = subtractOps[0].RegionASurfaceNumber > 0 ? subtractOps[0].RegionASurfaceNumber : 1;
        if (excavated.Count == 1)
        {
            var single = subtractOps.FirstOrDefault(r => !r.IsMultiRegion && r.RegionBSurfaceNumber == excavated[0]);
            if (single != null)
            {
                ApplySurfaceBooleanPreviewForRow(single);
                return;
            }
        }

        var participating = new List<int> { baseSurface };
        foreach (var n in excavated)
            if (n != baseSurface && !participating.Contains(n))
                participating.Add(n);

        var cumulativeRow = new BooleanOperationRow
        {
            OperationType = "Subtract",
            TypeEnum = BooleanOperationType.Subtract,
            IsMultiRegion = true,
            RegionASurfaceNumber = baseSurface,
            ParticipatingSurfaceNumbers = participating,
        };

        ApplySurfaceBooleanPreviewForRow(cumulativeRow);
    }

    private List<int> GetCumulativeExcavatedSurfaceNumbers()
    {
        var excavated = new List<int>();
        if (StagedConstruction == null || StagedConstruction.Stages.Count <= 1)
            return excavated;
        int activeStageNum = StagedConstruction.ActiveStageNumber;
        if (activeStageNum < 2)
            return excavated;

        foreach (var op in ExcavationOperations)
        {
            if (op.TypeEnum != BooleanOperationType.Subtract) continue;
            if (ResolveEffectiveAppliedStageNumber(op) > activeStageNum) continue;
            foreach (var n in GetExcavatedSurfaceNumbers(op))
                if (!excavated.Contains(n))
                    excavated.Add(n);
        }
        return excavated;
    }

    private int ResolveEffectiveAppliedStageNumber(BooleanOperationRow row)
    {
        if (row.AppliedInStageNumber > 0)
            return row.AppliedInStageNumber;
        return StagedConstruction?.ResolveDefaultExcavationStageNumber() ?? 2;
    }

    private static List<int> GetExcavatedSurfaceNumbers(BooleanOperationRow row)
    {
        var result = new List<int>();
        if (row.TypeEnum != BooleanOperationType.Subtract)
            return result;

        if (row.IsMultiRegion && row.ParticipatingSurfaceNumbers.Count >= 2)
        {
            for (int i = 1; i < row.ParticipatingSurfaceNumbers.Count; i++)
                result.Add(row.ParticipatingSurfaceNumbers[i]);
        }
        else if (row.RegionBSurfaceNumber > 0)
        {
            result.Add(row.RegionBSurfaceNumber);
        }

        return result;
    }

    private int ResolveDefaultExcavationStageNumberForNewOperation()
    {
        var active = StagedConstruction?.ActiveStageNumber ?? 0;
        if (active >= 2)
            return active;
        return StagedConstruction?.ResolveDefaultExcavationStageNumber() ?? 2;
    }

    private void OnExcavationOperationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (BooleanOperationRow row in e.OldItems)
            {
                row.PropertyChanged -= OnExcavationOperationRowPropertyChanged;
                _excavationRowStageSnapshot.Remove(row);
            }
        }

        if (e.NewItems != null)
        {
            foreach (BooleanOperationRow row in e.NewItems)
                HookExcavationOperationRow(row);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _excavationRowStageSnapshot.Clear();
            foreach (var row in ExcavationOperations)
                HookExcavationOperationRow(row);
        }
    }

    private void HookExcavationOperationRow(BooleanOperationRow row)
    {
        row.PropertyChanged -= OnExcavationOperationRowPropertyChanged;
        row.PropertyChanged += OnExcavationOperationRowPropertyChanged;
        _excavationRowStageSnapshot[row] = row.AppliedInStageNumber;
    }

    private void OnExcavationOperationRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BooleanOperationRow.AppliedInStageNumber))
            return;
        if (sender is not BooleanOperationRow row)
            return;

        int oldStage = _excavationRowStageSnapshot.TryGetValue(row, out var prev) ? prev : 0;
        int newStage = row.AppliedInStageNumber;
        if (oldStage == newStage)
            return;
        _excavationRowStageSnapshot[row] = newStage;

        StagedConstruction?.ReassignBooleanSubtractStage(row, oldStage);
        RefreshStageAwareExcavationDisplay();
    }

    /// <summary>Computes and sets the canvas boolean overlay from one excavation row (same logic as the latest operation).</summary>
    private void ApplySurfaceBooleanPreviewForRow(BooleanOperationRow row)
    {
        if (row.IsMultiRegion && row.ParticipatingSurfaceNumbers.Count >= 2)
        {
            var surfaces = new List<GeometrySurface>();
            foreach (var n in row.ParticipatingSurfaceNumbers)
            {
                var s = GeometrySurfaces.FirstOrDefault(x => x.SurfaceNumber == n);
                if (s == null || !TryHasClosedSurfacePolygon(s))
                {
                    ClearSurfaceBooleanCanvasResult();
                    RefreshSurfaceRegionOverlays();
                    return;
                }

                surfaces.Add(s);
            }

            if (surfaces.Count < 2)
            {
                ClearSurfaceBooleanCanvasResult();
                RefreshSurfaceRegionOverlays();
                return;
            }

            var result = TryComputeMultiSurfaceGeometry(surfaces, row.TypeEnum, out var combined, out var resultAreaAxis);
            if (result != SurfaceBooleanComputeResult.Success || combined == null)
            {
                ClearSurfaceBooleanCanvasResult();
                RefreshSurfaceRegionOverlays();
                return;
            }

            row.ResultArea = resultAreaAxis;
            _surfaceBooleanResultGeometry = combined;
            _surfaceBooleanDisplayName = GetDisplayNameForBooleanRow(row);
            if (row.TypeEnum == BooleanOperationType.Subtract && surfaces.Count >= 2)
            {
                UpdateRegionBPolygonsForMultiSubtract(surfaces);
            }
            else
            {
                RegionBPolygons.Clear();
                UnionFillPolygons.Clear();
            }

            RefreshSurfaceRegionOverlays();
            return;
        }

        var sa = GeometrySurfaces.FirstOrDefault(s => s.SurfaceNumber == row.RegionASurfaceNumber);
        var sb = GeometrySurfaces.FirstOrDefault(s => s.SurfaceNumber == row.RegionBSurfaceNumber);
        if (sa == null || sb == null || !TryHasClosedSurfacePolygon(sa) || !TryHasClosedSurfacePolygon(sb))
        {
            ClearSurfaceBooleanCanvasResult();
            RefreshSurfaceRegionOverlays();
            return;
        }

        var binaryResult = TryComputeSurfaceBooleanGeometry(sa, sb, row.TypeEnum, out var combinedBin, out var resultAreaAxisBin);
        if (binaryResult != SurfaceBooleanComputeResult.Success || combinedBin == null)
        {
            ClearSurfaceBooleanCanvasResult();
            RefreshSurfaceRegionOverlays();
            return;
        }

        var areaA = ComputeSurfaceAxisArea(sa);
        var areaB = ComputeSurfaceAxisArea(sb);
        row.RegionAArea = areaA;
        row.RegionBArea = areaB;
        row.ResultArea = resultAreaAxisBin;

        _surfaceBooleanResultGeometry = combinedBin;
        _surfaceBooleanDisplayName = GetDisplayNameForBooleanRow(row);
        if (row.TypeEnum == BooleanOperationType.Subtract)
        {
            UpdateRegionBPolygonsForSubtract(sb);
        }
        else
        {
            RegionBPolygons.Clear();
            UnionFillPolygons.Clear();
        }

        RefreshSurfaceRegionOverlays();
    }

    private static string GetDisplayNameForBooleanRow(BooleanOperationRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.ResultRegionName))
            return row.ResultRegionName!.Trim();
        if (row.IsMultiRegion && row.ParticipatingSurfaceNumbers.Count > 0)
        {
            var nums = row.ParticipatingSurfaceNumbers;
            return row.TypeEnum switch
            {
                BooleanOperationType.Union => $"∪({string.Join(", ", nums.Select(n => $"S{n}"))})",
                BooleanOperationType.Intersect => $"∩({string.Join(", ", nums.Select(n => $"S{n}"))})",
                BooleanOperationType.Subtract => string.Join("−", nums.Select(n => $"S{n}")),
                _ => string.Join(", ", nums.Select(n => $"S{n}"))
            };
        }

        var a = string.IsNullOrWhiteSpace(row.RegionAName) ? $"S{row.RegionASurfaceNumber}" : row.RegionAName!.Trim();
        var b = string.IsNullOrWhiteSpace(row.RegionBName) ? $"S{row.RegionBSurfaceNumber}" : row.RegionBName!.Trim();
        return $"{a}−{b}";
    }

    private enum SurfaceBooleanComputeResult
    {
        Success,
        PathsFailed,
        EmptyResult
    }

    private SurfaceBooleanComputeResult TryComputeSurfaceBooleanGeometry(
        GeometrySurface sa,
        GeometrySurface sb,
        BooleanOperationType type,
        out CombinedGeometry? combined,
        out double resultAreaAxis)
    {
        combined = null;
        resultAreaAxis = 0;
        if (!TryGetSurfaceCanvasPathGeometry(sa, out var pathA) || !TryGetSurfaceCanvasPathGeometry(sb, out var pathB))
            return SurfaceBooleanComputeResult.PathsFailed;

        var mode = type switch
        {
            BooleanOperationType.Union => GeometryCombineMode.Union,
            BooleanOperationType.Subtract => GeometryCombineMode.Exclude,
            BooleanOperationType.Intersect => GeometryCombineMode.Intersect,
            _ => GeometryCombineMode.Exclude
        };

        var cg = new CombinedGeometry(mode, pathA, pathB);
        cg.Freeze();

        var areaCanvas = Math.Abs(cg.GetArea());
        if (areaCanvas < 1e-8)
            return SurfaceBooleanComputeResult.EmptyResult;

        var sx = CanvasWidth > 1e-9 ? (AxisXMax - AxisXMin) / CanvasWidth : 1;
        var sy = CanvasHeight > 1e-9 ? (AxisYMax - AxisYMin) / CanvasHeight : 1;
        resultAreaAxis = areaCanvas * sx * sy;
        combined = cg;
        return SurfaceBooleanComputeResult.Success;
    }

    private SurfaceBooleanComputeResult TryComputeMultiSurfaceGeometry(
        IReadOnlyList<GeometrySurface> surfaces,
        BooleanOperationType type,
        out CombinedGeometry? combined,
        out double resultAreaAxis)
    {
        combined = null;
        resultAreaAxis = 0;
        if (surfaces.Count < 2)
            return SurfaceBooleanComputeResult.PathsFailed;

        var paths = new List<PathGeometry>();
        foreach (var s in surfaces)
        {
            if (!TryGetSurfaceCanvasPathGeometry(s, out var path))
                return SurfaceBooleanComputeResult.PathsFailed;
            paths.Add(path);
        }

        Geometry acc = paths[0];
        if (type == BooleanOperationType.Subtract)
        {
            for (var i = 1; i < paths.Count; i++)
            {
                var cg = new CombinedGeometry(GeometryCombineMode.Exclude, acc, paths[i]);
                cg.Freeze();
                acc = cg;
            }
        }
        else
        {
            var mode = type == BooleanOperationType.Union ? GeometryCombineMode.Union : GeometryCombineMode.Intersect;
            for (var i = 1; i < paths.Count; i++)
            {
                var cg = new CombinedGeometry(mode, acc, paths[i]);
                cg.Freeze();
                acc = cg;
            }
        }

        if (acc is not CombinedGeometry cgOut)
            return SurfaceBooleanComputeResult.PathsFailed;

        combined = cgOut;

        var areaCanvas = Math.Abs(combined.GetArea());
        if (areaCanvas < 1e-8)
            return SurfaceBooleanComputeResult.EmptyResult;

        var sx = CanvasWidth > 1e-9 ? (AxisXMax - AxisXMin) / CanvasWidth : 1;
        var sy = CanvasHeight > 1e-9 ? (AxisYMax - AxisYMin) / CanvasHeight : 1;
        resultAreaAxis = areaCanvas * sx * sy;
        return SurfaceBooleanComputeResult.Success;
    }

    private void ApplySurfaceBooleanFromDialog(BooleanOperationDialogViewModel dvm)
    {
        if (dvm.IsMultiRegionMode)
        {
            ApplyMultiSurfaceFromDialog(dvm);
            return;
        }

        ApplyBinarySurfaceBooleanFromDialog(dvm);
    }

    private void ApplyMultiSurfaceFromDialog(BooleanOperationDialogViewModel dvm)
    {
        var surfaces = dvm.GetSelectedSurfacesForMultiRegion().ToList();
        var opType = dvm.SelectedOperationType;
        var result = TryComputeMultiSurfaceGeometry(surfaces, opType, out var combined, out var resultAreaAxis);
        if (result == SurfaceBooleanComputeResult.PathsFailed)
        {
            AppDialogs.Show("Could not build surface outlines for the selected regions.", "Excavate", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (result == SurfaceBooleanComputeResult.EmptyResult || combined == null)
        {
            AppDialogs.Show("The boolean operation produced an empty region.", "Excavate", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _surfaceBooleanResultGeometry = combined;
        var customName = string.IsNullOrWhiteSpace(dvm.ResultRegionName) ? null : dvm.ResultRegionName.Trim();
        var names = string.Join(", ", surfaces.Select(s => string.IsNullOrWhiteSpace(s.Tag) ? $"S{s.SurfaceNumber}" : s.Tag!.Trim()));
        var tempRow = new BooleanOperationRow
        {
            IsMultiRegion = true,
            ParticipatingSurfaceNumbers = surfaces.Select(s => s.SurfaceNumber).ToList(),
            TypeEnum = opType,
            ResultRegionName = customName ?? ""
        };
        _surfaceBooleanDisplayName = customName ?? GetDisplayNameForBooleanRow(tempRow);

        var opShort = opType switch
        {
            BooleanOperationType.Union => "Union",
            BooleanOperationType.Subtract => "Subtract",
            BooleanOperationType.Intersect => "Intersect",
            _ => "Boolean"
        };

        var nums = surfaces.Select(s => s.SurfaceNumber).ToList();
        var appliedStage = ResolveDefaultExcavationStageNumberForNewOperation();
        ExcavationOperations.Add(new BooleanOperationRow
        {
            OperationId = ExcavationOperations.Count + 1,
            OperationType = $"{opShort} (multi)",
            TypeEnum = opType,
            IsMultiRegion = true,
            ParticipatingSurfaceNumbers = nums,
            RegionAName = names,
            RegionBName = "—",
            RegionASurfaceNumber = nums[0],
            RegionBSurfaceNumber = nums.Count > 1 ? nums[1] : 0,
            RegionAArea = 0,
            RegionBArea = 0,
            ResultArea = resultAreaAxis,
            ResultRegionName = customName ?? "",
            IsComplete = true,
            AppliedInStageNumber = appliedStage
        });

        StagedConstruction?.RegisterBooleanSubtractForStage(ExcavationOperations[^1]);
        RefreshSurfaceRegionOverlays();
    }

    private void ApplyBinarySurfaceBooleanFromDialog(BooleanOperationDialogViewModel dvm)
    {
        var sa = dvm.SelectedRegionA!.Surface;
        var sb = dvm.SelectedRegionB!.Surface;
        var result = TryComputeSurfaceBooleanGeometry(sa, sb, dvm.SelectedOperationType, out var combined, out var resultAreaAxis);
        if (result == SurfaceBooleanComputeResult.PathsFailed)
        {
            AppDialogs.Show("Could not build surface outlines for the selected regions.", "Excavate", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (result == SurfaceBooleanComputeResult.EmptyResult || combined == null)
        {
            AppDialogs.Show("The boolean operation produced an empty region.", "Excavate", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var areaA = ComputeSurfaceAxisArea(sa);
        var areaB = ComputeSurfaceAxisArea(sb);

        _surfaceBooleanResultGeometry = combined;
        var customName = string.IsNullOrWhiteSpace(dvm.ResultRegionName) ? null : dvm.ResultRegionName.Trim();
        _surfaceBooleanDisplayName = customName ?? $"{dvm.SelectedRegionA!.RegionName}−{dvm.SelectedRegionB!.RegionName}";

        var opLabel = dvm.SelectedOperationType switch
        {
            BooleanOperationType.Union => "Union",
            BooleanOperationType.Subtract => "Subtract",
            BooleanOperationType.Intersect => "Intersect",
            _ => "Boolean"
        };

        var appliedStage = ResolveDefaultExcavationStageNumberForNewOperation();
        ExcavationOperations.Add(new BooleanOperationRow
        {
            OperationId = ExcavationOperations.Count + 1,
            OperationType = opLabel,
            TypeEnum = dvm.SelectedOperationType,
            RegionAName = dvm.SelectedRegionA.RegionName,
            RegionBName = dvm.SelectedRegionB.RegionName,
            ResultRegionName = customName ?? "",
            RegionASurfaceNumber = sa.SurfaceNumber,
            RegionBSurfaceNumber = sb.SurfaceNumber,
            RegionAArea = areaA,
            RegionBArea = areaB,
            ResultArea = resultAreaAxis,
            IsComplete = true,
            AppliedInStageNumber = appliedStage
        });

        StagedConstruction?.RegisterBooleanSubtractForStage(ExcavationOperations[^1]);
        RefreshSurfaceRegionOverlays();
    }

    private bool TryGetSurfaceCanvasPathGeometry(GeometrySurface surface, out PathGeometry path)
    {
        path = new PathGeometry();
        if (!TryBuildSurfaceOverlayGeometry(surface, out var pts, out _, out _) || pts.Count < 3)
            return false;
        path = PointsToPathGeometry(pts);
        return true;
    }

    private static PathGeometry PointsToPathGeometry(PointCollection pts)
    {
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = true };
        for (var i = 1; i < pts.Count; i++)
            fig.Segments.Add(new LineSegment(pts[i], true));
        var pg = new PathGeometry();
        pg.Figures.Add(fig);
        pg.FillRule = System.Windows.Media.FillRule.Nonzero;
        pg.Freeze();
        return pg;
    }

    private double ComputeSurfaceAxisArea(GeometrySurface surface)
    {
        if (IsCircleOnlySurface(surface))
        {
            var seg = TryGetCircleOnlySegment(surface);
            if (seg is not { IsCircle: true })
                return 0;
            var r = seg.CircleRadiusAxis;
            return Math.PI * r * r;
        }

        if (!TryGetSurfacePolygonOrderedLabels(surface, out var labels) || labels.Count < 3)
            return 0;
        var pts = new List<Point>();
        foreach (var label in labels)
        {
            var v = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == label);
            if (v == null) return 0;
            pts.Add(new Point(v.AxisX, v.AxisY));
        }
        return Math.Abs(ShoelaceArea(pts));
    }

    public double GetSurfaceAxisArea(GeometrySurface surface) => ComputeSurfaceAxisArea(surface);

    public double GetClosedSoilBlockArea()
    {
        if (!TryGetClosedBoundaryAxisPolygon(out var pts) || pts.Count < 3)
            return 0;
        return Math.Abs(ShoelaceArea(pts));
    }

    private static double ShoelaceArea(IReadOnlyList<Point> pts)
    {
        var n = pts.Count;
        double a = 0;
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            a += pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
        }
        return a * 0.5;
    }

    /// <summary>Material Properties &amp; Regions: tint the given surface region with the assigned material colour on the shared geometry canvas. Pass null to clear.</summary>
    public void SetMaterialAssignmentSurfacePreview(int? surfaceNumber, Brush? regionFillBrush)
    {
        _materialAssignmentPreviewSurfaceNumber = surfaceNumber;
        _materialAssignmentPreviewFill = regionFillBrush;
        RefreshSurfaceRegionOverlays();
    }

    /// <summary>
    /// Sets a persistent material fill for the given surface (painted via Assign button). Pass null to remove.
    /// <paramref name="materialId"/> is recorded alongside the brush so .xfem persistence can re-link the
    /// surface to its source material on reload (the brush itself is rebuilt from the material's current colour).
    /// </summary>
    public void SetSurfaceMaterialFill(int surfaceNumber, Brush? fill, int? materialId = null)
    {
        if (fill == null)
        {
            _surfaceMaterialFills.Remove(surfaceNumber);
            _surfaceMaterialIds.Remove(surfaceNumber);
        }
        else
        {
            _surfaceMaterialFills[surfaceNumber] = fill;
            if (materialId.HasValue)
                _surfaceMaterialIds[surfaceNumber] = materialId.Value;
            else
                _surfaceMaterialIds.Remove(surfaceNumber);
        }
        RefreshSurfaceRegionOverlays();
    }

    /// <summary>Removes all Assign-button paint overrides from every surface.</summary>
    public void ClearAllSurfaceMaterialFills()
    {
        _surfaceMaterialFills.Clear();
        _surfaceMaterialIds.Clear();
        RefreshSurfaceRegionOverlays();
    }

    /// <summary>
    /// Snapshot of surface→material assignments painted via the Materials grid Assign button.
    /// Each entry exposes the surface number, the ARGB of the painted brush (for fallback reload
    /// when the source material is missing) and the material id (when known) for relinking.
    /// </summary>
    public IReadOnlyList<(int SurfaceNumber, uint Argb, int? MaterialId)> SurfaceMaterialFillsSnapshot
    {
        get
        {
            var list = new List<(int, uint, int?)>(_surfaceMaterialFills.Count);
            foreach (var kv in _surfaceMaterialFills)
            {
                uint argb = 0;
                if (kv.Value is SolidColorBrush scb)
                    argb = ColorToArgb(scb.Color);
                int? mid = _surfaceMaterialIds.TryGetValue(kv.Key, out var v) ? v : null;
                list.Add((kv.Key, argb, mid));
            }
            return list;
        }
    }

    /// <summary>
    /// Replaces all Assign-button paint overrides from a persisted snapshot.
    /// Rebuilds the SolidColorBrush from the saved ARGB so the canvas re-renders identically without
    /// requiring the source material library to be loaded first.
    /// </summary>
    public void ReplaceSurfaceMaterialFillsFrom(IReadOnlyList<(int SurfaceNumber, uint Argb, int? MaterialId)>? entries)
    {
        _surfaceMaterialFills.Clear();
        _surfaceMaterialIds.Clear();
        if (entries != null)
        {
            foreach (var e in entries)
            {
                if (e.Argb == 0) continue;
                var brush = new SolidColorBrush(ColorFromArgb(e.Argb));
                if (brush.CanFreeze) brush.Freeze();
                _surfaceMaterialFills[e.SurfaceNumber] = brush;
                if (e.MaterialId.HasValue)
                    _surfaceMaterialIds[e.SurfaceNumber] = e.MaterialId.Value;
            }
        }
        RefreshSurfaceRegionOverlays();
    }

    /// <summary>Called by <see cref="MaterialPropertiesViewModel"/> to enter Assign-material-to-surface/layer mode.</summary>
    public void StartMaterialAssignMode(Action<int> onSurfaceClicked, Action<int>? onLayerClicked = null)
    {
        SurfaceClickedInAssignMode = onSurfaceClicked;
        LayerClickedInAssignMode = onLayerClicked;
        IsMaterialAssignMode = true;
    }

    /// <summary>Leaves Assign-material-to-surface/layer mode.</summary>
    public void StopMaterialAssignMode()
    {
        IsMaterialAssignMode = false;
        SurfaceClickedInAssignMode = null;
        LayerClickedInAssignMode = null;
    }

    /// <summary>
    /// Hit-tests <paramref name="modelCanvasPoint"/> against material layer strips (Layer Inputs) or
    /// geometry surfaces. Fires <see cref="LayerClickedInAssignMode"/> or <see cref="SurfaceClickedInAssignMode"/>.
    /// </summary>
    public void AssignMaterialAtPoint(Point modelCanvasPoint)
    {
        if (!IsMaterialAssignMode) return;

        if (LayerClickedInAssignMode != null && ShouldUseNonHorizontalLayerSummaries())
        {
            foreach (var kvp in _nonHorizontalLayerStripPolygons.OrderByDescending(k => k.Key))
            {
                var hitThisLayer = false;
                foreach (var axisPts in kvp.Value)
                {
                    if (axisPts == null || axisPts.Count < 3)
                        continue;

                    var canvasPoly = new PointCollection(axisPts.Count);
                    foreach (var p in axisPts)
                        canvasPoly.Add(ConvertAxisToCanvas(new Point(p.x, p.y)));

                    if (PointInPolygonModelCanvas(modelCanvasPoint, canvasPoly))
                    {
                        hitThisLayer = true;
                        break;
                    }
                }

                if (!hitThisLayer)
                    continue;

                LayerClickedInAssignMode(kvp.Key);
                return;
            }
        }

        if (LayerClickedInAssignMode != null && ShouldUseHorizontalMaterialLayerSummaries())
        {
            foreach (var summary in LayerCoordinateSummaries.OrderByDescending(s => s.DisplayLayerNumber))
            {
                if (!TryGetHorizontalStripPolygonAxis(summary.DisplayLayerNumber, out var axisPts)
                    || axisPts == null
                    || axisPts.Count < 3)
                    continue;

                var canvasPoly = new PointCollection(axisPts.Count);
                foreach (var p in axisPts)
                    canvasPoly.Add(ConvertAxisToCanvas(new Point(p.x, p.y)));

                if (!PointInPolygonModelCanvas(modelCanvasPoint, canvasPoly))
                    continue;

                LayerClickedInAssignMode(summary.DisplayLayerNumber);
                return;
            }
        }

        if (SurfaceClickedInAssignMode == null) return;

        GeometrySurface? hit = null;
        foreach (var surface in GeometrySurfaces.OrderBy(s => s.SurfaceNumber))
        {
            if (!TryBuildSurfaceOverlayGeometry(surface, out var canvasPoly, out _, out _) || canvasPoly.Count < 3)
                continue;
            if (!PointInPolygonModelCanvas(modelCanvasPoint, canvasPoly))
                continue;
            hit = surface; // keep the innermost (highest-numbered) surface that contains the point
        }

        if (hit == null) return;
        SurfaceClickedInAssignMode(hit.SurfaceNumber);
    }

    /// <summary>Rebuilds all surface region overlays from the Surface grid (every valid closed loop is shown; touching regions allowed).</summary>
    private void RefreshSurfaceRegionOverlays()
    {
        SurfaceRegionOverlays.Clear();

        if (_surfaceBooleanResultGeometry != null)
        {
            var b = _surfaceBooleanResultGeometry.Bounds;
            var cx = b.X + b.Width * 0.5;
            var cy = b.Y + b.Height * 0.5;
            var sz = MeasureSurfaceTagLabelSize(_surfaceBooleanDisplayName);
            var c = SurfaceHighlightPalette[0];
            var fill = new SolidColorBrush(c);
            if (fill.CanFreeze)
                fill.Freeze();
            SurfaceRegionOverlays.Add(new SurfaceRegionOverlayItem
            {
                PolygonPoints = new PointCollection(),
                FillGeometry = _surfaceBooleanResultGeometry,
                Fill = fill,
                DisplayName = _surfaceBooleanDisplayName,
                LabelLeft = cx - sz.Width * 0.5,
                LabelTop = cy - sz.Height * 0.5,
                ShowLabel = !UseLayerNamesOnMaterialCanvas,
                IsSelected = _selectedExcavationOperation != null
            });
            AppendMaterialLayerStripOverlays();
            AppendNonHorizontalMaterialLayerStripOverlays();
            OnPropertyChanged(nameof(SurfaceRegionOverlays));
            return;
        }

        if (UseLayerNamesOnMaterialCanvas &&
            (ShouldUseHorizontalMaterialLayerSummaries() || ShouldUseNonHorizontalLayerSummaries()))
        {
            AppendMaterialLayerStripOverlays();
            AppendNonHorizontalMaterialLayerStripOverlays();
            OnPropertyChanged(nameof(SurfaceRegionOverlays));
            RefreshPickedLineHighlights();
            return;
        }

        var surfaces = GeometrySurfaces.ToList();
        if (surfaces.Count == 0)
        {
            AppendMaterialLayerStripOverlays();
            AppendNonHorizontalMaterialLayerStripOverlays();
            OnPropertyChanged(nameof(SurfaceRegionOverlays));
            return;
        }

        // Draw non-selected surfaces first, selected last so shared edges show the active region on top (FE-style).
        if (_selectedGeometrySurface != null)
        {
            surfaces = surfaces
                .Where(s => !ReferenceEquals(s, _selectedGeometrySurface))
                .OrderBy(s => s.SurfaceNumber)
                .Concat(new[] { _selectedGeometrySurface })
                .ToList();
        }
        else
            surfaces = surfaces.OrderBy(s => s.SurfaceNumber).ToList();

        foreach (var surface in surfaces)
        {
            if (!TryBuildSurfaceOverlayGeometry(surface, out var canvasPoints, out var cx, out var cy))
                continue;

            Brush fill;
            if (_materialAssignmentPreviewSurfaceNumber == surface.SurfaceNumber && _materialAssignmentPreviewFill != null)
            {
                fill = _materialAssignmentPreviewFill;
            }
            else if (_surfaceMaterialFills.TryGetValue(surface.SurfaceNumber, out var assignedFill))
            {
                fill = assignedFill;
            }
            else
            {
                var c = SurfaceHighlightPalette[ResolveSurfacePaletteSlot(surface)];
                var sb = new SolidColorBrush(c);
                if (sb.CanFreeze)
                    sb.Freeze();
                fill = sb;
            }

            var tagText = surface.Tag?.Trim() ?? "";
            var displayName = string.IsNullOrEmpty(tagText) ? $"S{surface.SurfaceNumber}" : tagText;
            var sz = MeasureSurfaceTagLabelSize(displayName);
            var isSelected = ReferenceEquals(surface, _selectedGeometrySurface);

            SurfaceRegionOverlays.Add(new SurfaceRegionOverlayItem
            {
                PolygonPoints = canvasPoints,
                Fill = fill,
                DisplayName = displayName,
                LabelLeft = cx - sz.Width * 0.5,
                LabelTop = cy - sz.Height * 0.5,
                ShowLabel = !UseLayerNamesOnMaterialCanvas,
                IsSelected = isSelected
            });
        }

        AppendMaterialLayerStripOverlays();
        AppendNonHorizontalMaterialLayerStripOverlays();
        OnPropertyChanged(nameof(SurfaceRegionOverlays));
        RefreshPickedLineHighlights();
    }

    private void AppendMaterialLayerStripOverlays()
    {
        if (!ShouldUseHorizontalMaterialLayerSummaries() || !UseLayerNamesOnMaterialCanvas)
            return;

        foreach (var summary in LayerCoordinateSummaries)
            AppendOneMaterialLayerStripOverlay(summary.DisplayLayerNumber, showLabel: true);
    }

    private void AppendOneMaterialLayerStripOverlay(int displayLayerNumber, bool showLabel)
    {
        if (!TryGetHorizontalStripPolygonAxis(displayLayerNumber, out var axisPts) || axisPts == null || axisPts.Count < 3)
            return;

        var canvasPts = new PointCollection();
        foreach (var p in axisPts)
            canvasPts.Add(ConvertAxisToCanvas(new Point(p.x, p.y)));

        var isSelected = _materialLayerStripHighlightDisplayNumber == displayLayerNumber;
        var c = GetLayerDisplayColor(displayLayerNumber);
        bool hasAssignment = GetLayerMaterialAssignmentMaterialId(displayLayerNumber) is int;
        byte fillAlpha;
        if (UseLayerNamesOnMaterialCanvas)
        {
            if (hasAssignment)
                fillAlpha = isSelected ? (byte)170 : MeshElementMaterialFillResolver.FillAlpha;
            else
                fillAlpha = _materialLayerStripHighlightDisplayNumber is int
                    ? (isSelected ? (byte)210 : (byte)50)
                    : MeshElementMaterialFillResolver.FillAlpha;
        }
        else
        {
            fillAlpha = isSelected ? (byte)190 : MeshElementMaterialFillResolver.FillAlpha;
        }

        var fillC = Color.FromArgb(fillAlpha, c.R, c.G, c.B);
        var brush = new SolidColorBrush(fillC);
        if (brush.CanFreeze)
            brush.Freeze();

        var layerName = GetLayerDisplayName(displayLayerNumber, null);
        double stripCx = 0, stripCy = 0;
        var np = canvasPts.Count;
        for (var i = 0; i < np; i++)
        {
            stripCx += canvasPts[i].X;
            stripCy += canvasPts[i].Y;
        }
        stripCx /= np;
        stripCy /= np;
        var layerSz = MeasureSurfaceTagLabelSize(layerName);

        SurfaceRegionOverlays.Add(new SurfaceRegionOverlayItem
        {
            PolygonPoints = canvasPts,
            Fill = brush,
            DisplayName = layerName,
            LabelLeft = stripCx - layerSz.Width * 0.5,
            LabelTop = stripCy - layerSz.Height * 0.5,
            ShowLabel = showLabel,
            IsSelected = isSelected
        });
    }

    /// <summary>
    /// Renders filled, named strips for non-horizontal layer zones on the Material Properties canvas,
    /// mirroring <see cref="AppendMaterialLayerStripOverlays"/> for horizontal layers. The zone outlines
    /// are captured in <c>_nonHorizontalLayerStripPolygons</c> while summaries are built.
    /// </summary>
    private void AppendNonHorizontalMaterialLayerStripOverlays()
    {
        if (!UseLayerNamesOnMaterialCanvas || !ShouldUseNonHorizontalLayerSummaries())
            return;

        foreach (var kvp in _nonHorizontalLayerStripPolygons)
        {
            var displayLayerNumber = kvp.Key;
            var polygons = kvp.Value;
            if (polygons == null || polygons.Count == 0)
                continue;

            var isSelected = _materialLayerStripHighlightDisplayNumber == displayLayerNumber;
            var c = GetLayerDisplayColor(displayLayerNumber);
            bool hasAssignment = GetLayerMaterialAssignmentMaterialId(displayLayerNumber) is int;
            byte fillAlpha;
            if (hasAssignment)
                fillAlpha = isSelected ? (byte)170 : MeshElementMaterialFillResolver.FillAlpha;
            else
                fillAlpha = _materialLayerStripHighlightDisplayNumber is int
                    ? (isSelected ? (byte)210 : (byte)50)
                    : MeshElementMaterialFillResolver.FillAlpha;

            var fillC = Color.FromArgb(fillAlpha, c.R, c.G, c.B);
            var brush = new SolidColorBrush(fillC);
            if (brush.CanFreeze)
                brush.Freeze();

            var layerName = GetLayerDisplayName(displayLayerNumber, null);
            var layerSz = MeasureSurfaceTagLabelSize(layerName);

            // A zone can be multiply-connected; render each pocket, but label only the largest one
            // (polygons are stored largest-first) to avoid duplicate labels for the same layer.
            var isFirstPocket = true;
            foreach (var axisPts in polygons)
            {
                if (axisPts == null || axisPts.Count < 3)
                    continue;

                var canvasPts = new PointCollection();
                foreach (var p in axisPts)
                    canvasPts.Add(ConvertAxisToCanvas(new Point(p.x, p.y)));

                double stripCx = 0, stripCy = 0;
                var np = canvasPts.Count;
                for (var i = 0; i < np; i++)
                {
                    stripCx += canvasPts[i].X;
                    stripCy += canvasPts[i].Y;
                }
                stripCx /= np;
                stripCy /= np;

                SurfaceRegionOverlays.Add(new SurfaceRegionOverlayItem
                {
                    PolygonPoints = canvasPts,
                    Fill = brush,
                    DisplayName = layerName,
                    LabelLeft = stripCx - layerSz.Width * 0.5,
                    LabelTop = stripCy - layerSz.Height * 0.5,
                    ShowLabel = isFirstPocket,
                    IsSelected = isSelected
                });
                isFirstPocket = false;
            }
        }
    }

    /// <summary>Builds ordered vertex labels around a closed loop from locked polygon, draft clicks, or selected line numbers.</summary>
    private bool TryGetSurfacePolygonOrderedLabels(GeometrySurface surface, out List<string> orderedLabels)
    {
        orderedLabels = new List<string>();
        if (surface.IsPolygonLocked && surface.LockedPolygonVertexLabels.Count >= 3)
        {
            foreach (var label in surface.LockedPolygonVertexLabels)
            {
                if (GeometryVertexRows.All(r => r.PointLabel != label))
                    return false;
                orderedLabels.Add(label);
            }

            return orderedLabels.Count >= 3;
        }

        if (!surface.IsPolygonLocked && surface.DraftPolygonVertexLabels.Count >= 3)
        {
            foreach (var label in surface.DraftPolygonVertexLabels)
            {
                if (GeometryVertexRows.All(r => r.PointLabel != label))
                    return false;
                orderedLabels.Add(label);
            }

            return orderedLabels.Count >= 3;
        }

        var lineNums = new List<int>();
        CollectSurfaceLineNumbersForPolygon(surface, lineNums);

        if (lineNums.Count < 2)
            return false;

        var edges = new List<(string A, string B)>();
        foreach (var ln in lineNums)
        {
            var seg = GeometryLineSegments.FirstOrDefault(s => s.LineNumber == ln);
            if (seg == null)
                return false;
            edges.Add((seg.FromPoint, seg.ToPoint));
        }

        return TryBuildPolygonVertexLabelsFromEdges(edges, out orderedLabels) && orderedLabels.Count >= 3;
    }

    /// <summary>
    /// Combines L1–L5 picks with <see cref="GeometrySurface.ConnectedLineNumbers"/>. The grid only has five line
    /// columns; a closed loop may need six or more segment numbers (e.g. edges 5–6–7–8–9 plus closing 9→5).
    /// When the persisted full list is longer than the five picks, use the full list so mesh/RS2D match the popup.
    /// </summary>
    private static void CollectSurfaceLineNumbersForPolygon(GeometrySurface surface, List<int> lineNums)
    {
        lineNums.Clear();
        lineNums.AddRange(surface.PickedLineNumbers);
        if (surface.ConnectedLineNumbers.Count < 2)
            return;
        if (lineNums.Count < 2)
        {
            lineNums.Clear();
            lineNums.AddRange(surface.ConnectedLineNumbers);
            return;
        }

        if (surface.ConnectedLineNumbers.Count > lineNums.Count)
        {
            lineNums.Clear();
            lineNums.AddRange(surface.ConnectedLineNumbers);
        }
    }

    private bool TryBuildSurfaceOverlayGeometry(GeometrySurface surface, out PointCollection canvasPoints, out double centroidX, out double centroidY)
    {
        canvasPoints = new PointCollection();
        centroidX = 0;
        centroidY = 0;

        if (IsCircleOnlySurface(surface))
        {
            var seg = TryGetCircleOnlySegment(surface);
            if (seg == null)
                return false;
            return TryBuildCircleSurfaceCanvasPoints(seg, out canvasPoints, out centroidX, out centroidY);
        }

        if (!TryGetSurfacePolygonOrderedLabels(surface, out var orderedLabels))
            return false;

        var n = orderedLabels.Count;
        if (n < 3)
            return false;

        for (var i = 0; i < n; i++)
        {
            var va = orderedLabels[i];
            var vb = orderedLabels[(i + 1) % n];
            var seg = FindGeometrySegmentForSurfaceEdge(surface, va, vb);
            AppendSurfaceOverlayEdge(canvasPoints, seg, va, vb);
        }

        if (canvasPoints.Count < 3)
            return false;

        double sumX = 0, sumY = 0;
        foreach (Point p in canvasPoints)
        {
            sumX += p.X;
            sumY += p.Y;
        }
        var nPts = canvasPoints.Count;
        centroidX = sumX / nPts;
        centroidY = sumY / nPts;
        return true;
    }

    private static IEnumerable<int> EnumerateSurfaceSelectedLineNumbers(GeometrySurface surface)
        => surface.PickedLineNumbers;

    /// <summary>True when exactly one L# is selected and that row is a full circle (not a chord between vertices).</summary>
    private bool IsCircleOnlySurface(GeometrySurface surface)
    {
        var nums = EnumerateSurfaceSelectedLineNumbers(surface).ToList();
        if (nums.Count != 1)
            return false;
        var seg = GeometryLineSegments.FirstOrDefault(s => s.LineNumber == nums[0]);
        return seg?.IsCircle == true;
    }

    private GeometryLineSegment? TryGetCircleOnlySegment(GeometrySurface surface)
    {
        if (!IsCircleOnlySurface(surface))
            return null;
        var ln = EnumerateSurfaceSelectedLineNumbers(surface).First();
        return GeometryLineSegments.FirstOrDefault(s => s.LineNumber == ln);
    }

    private bool TryBuildCircleSurfaceCanvasPoints(GeometryLineSegment seg, out PointCollection canvasPoints, out double centroidX, out double centroidY)
    {
        canvasPoints = new PointCollection();
        centroidX = 0;
        centroidY = 0;
        if (!seg.IsCircle)
            return false;

        var centerCanvas = ConvertAxisToCanvas(new Point(seg.CircleCenterAxisX, seg.CircleCenterAxisY));
        var rimCanvas = ConvertAxisToCanvas(new Point(seg.CircleCenterAxisX + seg.CircleRadiusAxis, seg.CircleCenterAxisY));
        var rdx = rimCanvas.X - centerCanvas.X;
        var rdy = rimCanvas.Y - centerCanvas.Y;
        var rCanvas = Math.Sqrt(rdx * rdx + rdy * rdy);
        for (var i = 0; i <= 64; i++)
        {
            var ang = i * 2.0 * Math.PI / 64;
            canvasPoints.Add(new Point(centerCanvas.X + rCanvas * Math.Cos(ang), centerCanvas.Y + rCanvas * Math.Sin(ang)));
        }

        centroidX = centerCanvas.X;
        centroidY = centerCanvas.Y;
        return canvasPoints.Count >= 3;
    }

    /// <summary>Resolves straight or arc segment for one boundary step (va → vb) along the surface loop.</summary>
    private GeometryLineSegment? FindGeometrySegmentForSurfaceEdge(GeometrySurface surface, string va, string vb)
    {
        static bool EndpointsMatch(GeometryLineSegment s, string a, string b) =>
            (s.FromPoint == a && s.ToPoint == b) || (s.FromPoint == b && s.ToPoint == a);

        foreach (var ln in EnumerateSurfaceSelectedLineNumbers(surface))
        {
            var seg = GeometryLineSegments.FirstOrDefault(s => s.LineNumber == ln);
            if (seg != null && EndpointsMatch(seg, va, vb))
                return seg;
        }

        foreach (var seg in GeometryLineSegments)
        {
            if (EndpointsMatch(seg, va, vb))
                return seg;
        }

        return null;
    }

    private void AppendSurfaceOverlayEdge(PointCollection canvasPoints, GeometryLineSegment? seg, string va, string vb)
    {
        var vA = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == va);
        var vB = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == vb);
        if (vA == null || vB == null)
            return;

        if (seg is { IsArc: true } && !string.IsNullOrEmpty(seg.ThroughPoint))
        {
            var vFrom = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == seg.FromPoint);
            var vMid = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == seg.ThroughPoint);
            var vTo = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == seg.ToPoint);
            if (vFrom != null && vMid != null && vTo != null)
            {
                var pFrom = ConvertAxisToCanvas(new Point(vFrom.AxisX, vFrom.AxisY));
                var pThrough = ConvertAxisToCanvas(new Point(vMid.AxisX, vMid.AxisY));
                var pTo = ConvertAxisToCanvas(new Point(vTo.AxisX, vTo.AxisY));

                var arcPts = SampleArcPoints(pFrom, pThrough, pTo, 64);
                if (va == seg.ToPoint && vb == seg.FromPoint)
                    arcPts = ReversePointCollection(arcPts);

                foreach (Point p in arcPts)
                {
                    if (canvasPoints.Count > 0 && CanvasPointsNearlyEqual(canvasPoints[^1], p))
                        continue;
                    canvasPoints.Add(p);
                }

                return;
            }
        }

        var pa = ConvertAxisToCanvas(new Point(vA.AxisX, vA.AxisY));
        var pb = ConvertAxisToCanvas(new Point(vB.AxisX, vB.AxisY));
        if (canvasPoints.Count == 0)
        {
            canvasPoints.Add(pa);
            canvasPoints.Add(pb);
        }
        else
        {
            if (!CanvasPointsNearlyEqual(canvasPoints[^1], pb))
                canvasPoints.Add(pb);
        }
    }

    private static PointCollection ReversePointCollection(PointCollection pts)
    {
        var rev = new PointCollection(pts.Count);
        for (var i = pts.Count - 1; i >= 0; i--)
            rev.Add(pts[i]);
        return rev;
    }

    private static bool CanvasPointsNearlyEqual(Point a, Point b) =>
        Math.Abs(a.X - b.X) <= SurfaceOverlayPointMergeEps && Math.Abs(a.Y - b.Y) <= SurfaceOverlayPointMergeEps;

    private static (int Min, int Max) StableUnorderedPair(int a, int b) => a <= b ? (a, b) : (b, a);

    private Dictionary<(int Min, int Max), (string Tag, string UnitWeight, bool IsSaturated, string SaturatedUnitWeight, string Cohesion, string FrictionAngle, string YoungsModulus, string PoissonsRatio, string Density, bool ConsiderDensityForFE, bool PlaneStrain)> BuildEdgeMaterialMapByStablePair()
    {
        var map = new Dictionary<(int Min, int Max), (string Tag, string UnitWeight, bool IsSaturated, string SaturatedUnitWeight, string Cohesion, string FrictionAngle, string YoungsModulus, string PoissonsRatio, string Density, bool ConsiderDensityForFE, bool PlaneStrain)>();
        foreach (var seg in GeometryLineSegments)
        {
            var fromV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
            var toV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
            if (fromV == null || toV == null) continue;
            var key = StableUnorderedPair(fromV.StablePointId, toV.StablePointId);
            map[key] = CaptureSegmentMaterial(seg);
        }
        return map;
    }

    private void ApplyEdgeMaterialsFromStablePairMap(Dictionary<(int Min, int Max), (string Tag, string UnitWeight, bool IsSaturated, string SaturatedUnitWeight, string Cohesion, string FrictionAngle, string YoungsModulus, string PoissonsRatio, string Density, bool ConsiderDensityForFE, bool PlaneStrain)> map)
    {
        foreach (var seg in GeometryLineSegments)
        {
            var fromV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
            var toV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
            if (fromV == null || toV == null) continue;
            var key = StableUnorderedPair(fromV.StablePointId, toV.StablePointId);
            if (map.TryGetValue(key, out var p))
                ApplySegmentMaterialSnapshot(seg, p);
        }
    }

    private static Dictionary<(int Min, int Max), int> BuildStablePairToLineNumberMap(GeometryDefinitionViewModel g)
    {
        var pairToNewLine = new Dictionary<(int Min, int Max), int>();
        foreach (var seg in g.GeometryLineSegments)
        {
            var fromV = g.GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
            var toV = g.GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
            if (fromV == null || toV == null) continue;
            var key = StableUnorderedPair(fromV.StablePointId, toV.StablePointId);
            pairToNewLine[key] = seg.LineNumber;
        }
        return pairToNewLine;
    }

    private static Dictionary<int, (int Min, int Max)> BuildLineNumberToStablePairMap(GeometryDefinitionViewModel g)
    {
        var lineToPair = new Dictionary<int, (int Min, int Max)>();
        foreach (var seg in g.GeometryLineSegments)
        {
            var fromV = g.GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
            var toV = g.GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
            if (fromV == null || toV == null) continue;
            lineToPair[seg.LineNumber] = StableUnorderedPair(fromV.StablePointId, toV.StablePointId);
        }
        return lineToPair;
    }

    /// <summary>
    /// After <see cref="GeometryLineSegments"/> is rebuilt, L# values change but each edge is still identified by
    /// (stable point id) pair. Remap surface picks so they still refer to the same physical edges.
    /// </summary>
    private void RemapGeometrySurfaceLineNumbersFromStablePairs(
        Dictionary<int, (int Min, int Max)> lineToStablePair,
        Dictionary<(int Min, int Max), int> pairToNewLine)
    {
        _suppressSurfaceGeometryRotation = true;
        try
        {
            foreach (var surface in GeometrySurfaces)
            {
                if (surface.IsPolygonLocked) continue;
                var remapped = new List<int>();
                foreach (var oldL in surface.PickedLineNumbers)
                {
                    if (!lineToStablePair.TryGetValue(oldL, out var key)) continue;
                    if (pairToNewLine.TryGetValue(key, out var newL))
                        remapped.Add(newL);
                }
                surface.PickedLineNumbers.Clear();
                foreach (var n in remapped)
                    surface.PickedLineNumbers.Add(n);
            }
        }
        finally
        {
            _suppressSurfaceGeometryRotation = false;
        }
    }

    /// <summary>True when the selected surface is not locked: canvas clicks append draft polygon corners.</summary>
    public bool IsSurfacePolygonPickMode =>
        SelectedGeometrySurface != null && !SelectedGeometrySurface.IsPolygonLocked;

    /// <summary>Locks the surface polygon from the current draft (≥3 clicks), from L1–L5 chain, or from a single circle L#. Clears line picks and draft when locking vertex loops.</summary>
    private void CommitSurfacePolygonOnAdd(GeometrySurface surface)
    {
        if (surface.IsPolygonLocked)
            return;

        List<string>? toLock = null;
        if (surface.DraftPolygonVertexLabels.Count >= 3)
        {
            toLock = surface.DraftPolygonVertexLabels.ToList();
        }
        else if (TryGetLineOnlySurfacePolygonOrderedLabels(surface, out var fromLines))
        {
            toLock = fromLines;
        }
        else if (IsCircleOnlySurface(surface))
        {
            _suppressSurfaceGeometryRotation = true;
            try
            {
                surface.LockedPolygonVertexLabels.Clear();
                surface.DraftPolygonVertexLabels.Clear();
                surface.IsPolygonLocked = true;
            }
            finally
            {
                _suppressSurfaceGeometryRotation = false;
            }

            OnPropertyChanged(nameof(IsSurfacePolygonPickMode));
            RefreshSurfaceRegionOverlays();
            return;
        }

        if (toLock == null || toLock.Count < 3)
            return;

        _suppressSurfaceGeometryRotation = true;
        try
        {
            surface.LockedPolygonVertexLabels.Clear();
            foreach (var l in toLock)
                surface.LockedPolygonVertexLabels.Add(l);
            surface.DraftPolygonVertexLabels.Clear();

            var lineNums = new List<int>();
            CollectSurfaceLineNumbersForPolygon(surface, lineNums);
            if (lineNums.Count > 0)
            {
                surface.ConnectedLineNumbers.Clear();
                foreach (var n in lineNums)
                    surface.ConnectedLineNumbers.Add(n);
            }

            // Lock before clearing picks so LineLabelsDisplay can fall back to ConnectedLineNumbers in the grid.
            surface.IsPolygonLocked = true;
            surface.PickedLineNumbers.Clear();
        }
        finally
        {
            _suppressSurfaceGeometryRotation = false;
        }

        OnPropertyChanged(nameof(IsSurfacePolygonPickMode));
        RefreshSurfaceRegionOverlays();
    }

    /// <summary>Polygon from L1–L5 only (ignores draft/locked collections).</summary>
    private bool TryGetLineOnlySurfacePolygonOrderedLabels(GeometrySurface surface, out List<string> orderedLabels)
    {
        orderedLabels = new List<string>();
        var lineNums = new List<int>();
        CollectSurfaceLineNumbersForPolygon(surface, lineNums);

        if (lineNums.Count < 2)
            return false;

        var edges = new List<(string A, string B)>();
        foreach (var ln in lineNums)
        {
            var seg = GeometryLineSegments.FirstOrDefault(s => s.LineNumber == ln);
            if (seg == null)
                return false;
            edges.Add((seg.FromPoint, seg.ToPoint));
        }

        return TryBuildPolygonVertexLabelsFromEdges(edges, out orderedLabels) && orderedLabels.Count >= 3;
    }

    /// <summary>After a geometry vertex is placed/changed, append nearest point to the active draft surface polygon.</summary>
    private void TryAppendDraftSurfaceVertexAfterGeometryClick(Point axisPoint)
    {
        if (!IsSurfacePolygonPickMode || SelectedGeometrySurface == null)
            return;

        var tol = ComputeAxisHitTolerance();
        GeometryVertexDisplay? best = null;
        var bestD2 = double.PositiveInfinity;
        foreach (var v in GeometryVertexRows)
        {
            var dx = v.AxisX - axisPoint.X;
            var dy = v.AxisY - axisPoint.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = v;
            }
        }

        if (best == null || bestD2 > tol * tol)
            return;

        var label = best.PointLabel;
        if (SelectedGeometrySurface.DraftPolygonVertexLabels.Count > 0 &&
            SelectedGeometrySurface.DraftPolygonVertexLabels[^1] == label)
            return;

        SelectedGeometrySurface.DraftPolygonVertexLabels.Add(label);
        RefreshSurfaceRegionOverlays();
    }

    /// <summary>Called from the canvas when <see cref="IsSurfacePolygonPickMode"/> is active. Toggles the nearest line segment into/out of <see cref="GeometrySurface.PickedLineNumbers"/> on the active surface.</summary>
    public void HandleSurfaceLinePickClick(Point canvasPoint)
    {
        if (!IsSurfacePolygonPickMode || SelectedGeometrySurface == null) return;

        const double HitTolerancePx = 12.0;
        int? bestLineNum = null;
        double bestDist = double.MaxValue;

        foreach (var seg in GeometryLineSegments)
        {
            double dist;
            if (seg.IsCircle)
            {
                var centerCanvas = ConvertAxisToCanvas(new Point(seg.CircleCenterAxisX, seg.CircleCenterAxisY));
                var rimCanvas = ConvertAxisToCanvas(new Point(seg.CircleCenterAxisX + seg.CircleRadiusAxis, seg.CircleCenterAxisY));
                var rdx = rimCanvas.X - centerCanvas.X;
                var rdy = rimCanvas.Y - centerCanvas.Y;
                var rPx = Math.Sqrt(rdx * rdx + rdy * rdy);
                var dx = canvasPoint.X - centerCanvas.X;
                var dy = canvasPoint.Y - centerCanvas.Y;
                dist = Math.Abs(Math.Sqrt(dx * dx + dy * dy) - rPx);
            }
            else if (seg.IsArc && !string.IsNullOrEmpty(seg.ThroughPoint))
            {
                var fromV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
                var midV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ThroughPoint);
                var toV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
                if (fromV == null || midV == null || toV == null) continue;
                var pFrom = ConvertAxisToCanvas(new Point(fromV.AxisX, fromV.AxisY));
                var pMid = ConvertAxisToCanvas(new Point(midV.AxisX, midV.AxisY));
                var pTo = ConvertAxisToCanvas(new Point(toV.AxisX, toV.AxisY));
                var arcPts = SampleArcPoints(pFrom, pMid, pTo, 32);
                dist = double.MaxValue;
                for (var i = 0; i + 1 < arcPts.Count; i++)
                {
                    var d = DistancePointToSegmentAxis(canvasPoint, arcPts[i], arcPts[i + 1], out _, out _);
                    if (d < dist) dist = d;
                }
            }
            else
            {
                var fromV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
                var toV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
                if (fromV == null || toV == null) continue;
                dist = DistancePointToSegmentAxis(canvasPoint, new Point(fromV.X, fromV.Y), new Point(toV.X, toV.Y), out _, out _);
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                bestLineNum = seg.LineNumber;
            }
        }

        if (bestLineNum == null || bestDist > HitTolerancePx) return;
        ToggleSurfaceLinePick(SelectedGeometrySurface, bestLineNum.Value);
        RefreshSurfaceRegionOverlays();
    }

    private static void ToggleSurfaceLinePick(GeometrySurface surface, int lineNum)
    {
        if (surface.PickedLineNumbers.Contains(lineNum))
            surface.PickedLineNumbers.Remove(lineNum);
        else
            surface.PickedLineNumbers.Add(lineNum);
    }

    private void RefreshPickedLineHighlights()
    {
        PickedSurfaceLineHighlightPolylines.Clear();
        if (SelectedGeometrySurface == null || SelectedGeometrySurface.IsPolygonLocked) return;

        foreach (var lineNum in EnumerateSurfaceSelectedLineNumbers(SelectedGeometrySurface))
        {
            var seg = GeometryLineSegments.FirstOrDefault(s => s.LineNumber == lineNum);
            if (seg == null) continue;

            if (seg.IsCircle)
            {
                var centerCanvas = ConvertAxisToCanvas(new Point(seg.CircleCenterAxisX, seg.CircleCenterAxisY));
                var rimCanvas = ConvertAxisToCanvas(new Point(seg.CircleCenterAxisX + seg.CircleRadiusAxis, seg.CircleCenterAxisY));
                var rdx = rimCanvas.X - centerCanvas.X;
                var rdy = rimCanvas.Y - centerCanvas.Y;
                var rPx = Math.Sqrt(rdx * rdx + rdy * rdy);
                var pts = new PointCollection();
                for (var i = 0; i <= 64; i++)
                {
                    var ang = i * 2.0 * Math.PI / 64;
                    pts.Add(new Point(centerCanvas.X + rPx * Math.Cos(ang), centerCanvas.Y + rPx * Math.Sin(ang)));
                }
                PickedSurfaceLineHighlightPolylines.Add(pts);
            }
            else if (seg.IsArc && !string.IsNullOrEmpty(seg.ThroughPoint))
            {
                var fromV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
                var midV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ThroughPoint);
                var toV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
                if (fromV != null && midV != null && toV != null)
                {
                    var pFrom = ConvertAxisToCanvas(new Point(fromV.AxisX, fromV.AxisY));
                    var pMid = ConvertAxisToCanvas(new Point(midV.AxisX, midV.AxisY));
                    var pTo = ConvertAxisToCanvas(new Point(toV.AxisX, toV.AxisY));
                    PickedSurfaceLineHighlightPolylines.Add(SampleArcPoints(pFrom, pMid, pTo, 48));
                }
            }
            else
            {
                var fromV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
                var toV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
                if (fromV == null || toV == null) continue;
                PickedSurfaceLineHighlightPolylines.Add(new PointCollection { new Point(fromV.X, fromV.Y), new Point(toV.X, toV.Y) });
            }
        }
    }

    /// <summary>
    /// When a surface row forms a valid closed loop, rotate boundary vertex order so the surface's start vertex is at list index 0.
    /// Only runs when there is a single surface row — with two or more surfaces, rotation is skipped so boundary order (and L# From/To in the grid) stay fixed.
    /// Point labels use stable ids; materials and line picks are remapped by edge identity when rotation does run.
    /// </summary>
    private void TryRotateGeometryToMatchSurfaceStart(GeometrySurface surface)
    {
        if (_suppressSurfaceGeometryRotation || _hasAddedElevationLayer) return;
        if (GeometrySurfaces.Count > 1) return;
        var n = GeometryVertexRows.Count;
        if (n < 3) return;
        if (!TryGetSurfacePolygonOrderedLabels(surface, out var orderedLabels) || orderedLabels.Count < 3)
            return;

        var startLabel = orderedLabels[0];
        var startIdx = -1;
        for (var i = 0; i < n; i++)
        {
            if (GeometryVertexRows[i].PointLabel == startLabel)
            {
                startIdx = i;
                break;
            }
        }
        if (startIdx <= 0) return;

        var edgeMaterialByStablePair = BuildEdgeMaterialMapByStablePair();

        var newOrder = new List<GeometryVertexDisplay>(n);
        for (var k = 0; k < n; k++)
            newOrder.Add(GeometryVertexRows[(startIdx + k) % n]);

        GeometryVertexRows.Clear();
        foreach (var v in newOrder)
            GeometryVertexRows.Add(v);
        for (var i = 0; i < GeometryVertexRows.Count; i++)
        {
            GeometryVertexRows[i].Index = i;
            GeometryVertexRows[i].Owner = this;
        }

        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments();
        ApplyEdgeMaterialsFromStablePairMap(edgeMaterialByStablePair);
    }

    private static Size MeasureSurfaceTagLabelSize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new Size(0, 0);
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var formattedText = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            14.0,
            Brushes.Black,
            1.0);
        return new Size(formattedText.Width, formattedText.Height);
    }

    /// <summary>
    /// Orders segment edges into a vertex loop for polygon fill.
    /// First tries cyclic rotations of the Line1..Line5 column order (cheap); if that fails, tries all permutations
    /// of edges (n≤8) so the surface still highlights when line numbers are correct but not listed in traverse order.
    /// </summary>
    private static bool TryBuildPolygonVertexLabelsFromEdges(List<(string A, string B)> edges, out List<string> orderedLabels)
    {
        orderedLabels = new List<string>();
        if (edges.Count == 0)
            return false;

        for (var start = 0; start < edges.Count; start++)
        {
            var rotated = new List<(string A, string B)>(edges.Count);
            for (var i = 0; i < edges.Count; i++)
                rotated.Add(edges[(start + i) % edges.Count]);
            if (TryChainEdgeLabels(rotated, out orderedLabels) && orderedLabels.Count >= 3)
                return true;
        }

        if (edges.Count < 3 || edges.Count > 8)
            return false;

        var perm = Enumerable.Range(0, edges.Count).ToArray();
        do
        {
            var permuted = perm.Select(i => edges[i]).ToList();
            if (TryChainEdgeLabels(permuted, out orderedLabels) && orderedLabels.Count >= 3)
                return true;
        } while (NextPermutation(perm));

        return false;
    }

    private static bool NextPermutation(int[] p)
    {
        var i = p.Length - 2;
        while (i >= 0 && p[i] >= p[i + 1]) i--;
        if (i < 0) return false;
        var j = p.Length - 1;
        while (p[j] <= p[i]) j--;
        (p[i], p[j]) = (p[j], p[i]);
        Array.Reverse(p, i + 1, p.Length - i - 1);
        return true;
    }

    private static bool TryChainEdgeLabels(List<(string A, string B)> edges, out List<string> path)
    {
        path = new List<string>();
        if (edges.Count == 0)
            return false;
        var remaining = new List<(string A, string B)>(edges);
        var (a0, b0) = remaining[0];
        remaining.RemoveAt(0);
        path.Add(a0);
        path.Add(b0);
        var guard = 0;
        const int maxIter = 256;
        while (remaining.Count > 0 && guard++ < maxIter)
        {
            var last = path[^1];
            var idx = -1;
            for (var i = 0; i < remaining.Count; i++)
            {
                var e = remaining[i];
                if (e.A == last)
                {
                    path.Add(e.B);
                    idx = i;
                    break;
                }
                if (e.B == last)
                {
                    path.Add(e.A);
                    idx = i;
                    break;
                }
            }
            if (idx >= 0)
            {
                remaining.RemoveAt(idx);
                continue;
            }
            var first = path[0];
            idx = -1;
            for (var i = 0; i < remaining.Count; i++)
            {
                var e = remaining[i];
                if (e.A == first)
                {
                    path.Insert(0, e.B);
                    idx = i;
                    break;
                }
                if (e.B == first)
                {
                    path.Insert(0, e.A);
                    idx = i;
                    break;
                }
            }
            if (idx >= 0)
            {
                remaining.RemoveAt(idx);
                continue;
            }
            return false;
        }
        if (remaining.Count > 0)
            return false;
        if (path.Count >= 2 && path[0] == path[^1])
            path.RemoveAt(path.Count - 1);
        return path.Count >= 3;
    }

    public void SetHighlightedTunnelCircle(int? circleNumber)
    {
        foreach (var row in TunnelExcavationCircleRows)
            row.IsHighlighted = row.CircleNumber == circleNumber;
    }
    public void AddBoundaryVertex(Point p) { }

    public void AddPlaceholderStages(IEnumerable<(int StageNumber, string StageName)> stages) { }
    public void RemovePlaceholderStagesNotInList(IEnumerable<int> stageNumbers) { }
    public void ClearPlaceholderStages() { }

    internal void UpdateAxisTicks()
    {
        if (IsGeometryBulkUpdate)
            return;
        UpdateAxisTicksForView(_viewMatrix);
    }

    private void UpdateAxisTicksForView(Matrix viewMatrix)
    {
        XAxisTicks.Clear();
        YAxisTicks.Clear();
        XAxisMinorTicks.Clear();
        YAxisMinorTicks.Clear();
        GridLines.Clear();

        if (CanvasWidth <= 0 || CanvasHeight <= 0) return;

        var (visibleXMin, visibleXMax, visibleYMin, visibleYMax) = GetVisibleAxisBounds(viewMatrix);
        var xRange = visibleXMax - visibleXMin;
        var yRange = visibleYMax - visibleYMin;
        if (xRange <= 0 || yRange <= 0) return;

        var baseSpacing = _gridSpacing > 0 ? _gridSpacing : DefaultGridSpacing;
        var xStep = CalculateAdaptiveStep(baseSpacing, xRange, CanvasWidth);
        var yStep = CalculateAdaptiveStep(baseSpacing, yRange, CanvasHeight);
        var xDecimals = DetermineDecimalPlaces(xStep);
        var yDecimals = DetermineDecimalPlaces(yStep);
        var xFormat = BuildLabelFormat(xDecimals);
        var yFormat = BuildLabelFormat(yDecimals);

        PopulateXAxisTicks(viewMatrix, xStep, xFormat, xDecimals, visibleXMin, visibleXMax);
        PopulateYAxisTicks(viewMatrix, yStep, yFormat, yDecimals, visibleYMin, visibleYMax);
        UpdateGridLinesForView(viewMatrix, xStep, yStep, visibleXMin, visibleXMax, visibleYMin, visibleYMax);
        NotifyOriginAxisOverlayChanged();
    }

    private (double XMin, double XMax, double YMin, double YMax) GetVisibleAxisBounds(Matrix viewMatrix)
    {
        if (CanvasWidth <= 0 || CanvasHeight <= 0)
            return (AxisXMin, AxisXMax, AxisYMin, AxisYMax);

        var inv = viewMatrix;
        if (!inv.HasInverse)
            return (AxisXMin, AxisXMax, AxisYMin, AxisYMax);

        inv.Invert();
        var corners = new[]
        {
            ConvertCanvasToAxis(inv.Transform(new Point(0, 0))),
            ConvertCanvasToAxis(inv.Transform(new Point(CanvasWidth, 0))),
            ConvertCanvasToAxis(inv.Transform(new Point(0, CanvasHeight))),
            ConvertCanvasToAxis(inv.Transform(new Point(CanvasWidth, CanvasHeight)))
        };

        var xMin = Math.Max(AxisXMin, corners.Min(p => p.X));
        var xMax = Math.Min(AxisXMax, corners.Max(p => p.X));
        var yMin = Math.Max(AxisYMin, corners.Min(p => p.Y));
        var yMax = Math.Min(AxisYMax, corners.Max(p => p.Y));

        if (xMax <= xMin) { xMin = AxisXMin; xMax = AxisXMax; }
        if (yMax <= yMin) { yMin = AxisYMin; yMax = AxisYMax; }

        return (xMin, xMax, yMin, yMax);
    }

    private Point AxisToScreen(Point axisPoint, Matrix viewMatrix) =>
        viewMatrix.Transform(ConvertAxisToCanvas(axisPoint));

    /// <summary>
    /// Aligns the first major tick to a multiple of <paramref name="step"/> in world coordinates
    /// (0, ±step, ±2·step, …) — same convention as Plaxis / RS2 / MIDAS rulers.
    /// </summary>
    private static double AlignNiceStepStart(double visibleMin, double step)
    {
        if (step <= 0 || double.IsNaN(step) || double.IsInfinity(step))
            return visibleMin;
        return Math.Floor(visibleMin / step + 1e-12) * step;
    }

    internal void UpdateGridLines()
    {
        if (IsGeometryBulkUpdate)
            return;
        if (CanvasWidth <= 0 || CanvasHeight <= 0)
            return;

        var (visibleXMin, visibleXMax, visibleYMin, visibleYMax) = GetVisibleAxisBounds(_viewMatrix);
        var xRange = visibleXMax - visibleXMin;
        var yRange = visibleYMax - visibleYMin;
        if (xRange <= 0 || yRange <= 0)
            return;

        var baseSpacing = _gridSpacing > 0 ? _gridSpacing : DefaultGridSpacing;
        UpdateGridLinesForView(
            _viewMatrix,
            CalculateAdaptiveStep(baseSpacing, xRange, CanvasWidth),
            CalculateAdaptiveStep(baseSpacing, yRange, CanvasHeight),
            visibleXMin, visibleXMax, visibleYMin, visibleYMax);
    }

    private void UpdateGridLinesForView(
        Matrix viewMatrix,
        double xStep,
        double yStep,
        double visibleXMin,
        double visibleXMax,
        double visibleYMin,
        double visibleYMax)
    {
        GridLines.Clear();
        if (!ShowGrid || CanvasWidth <= 0 || CanvasHeight <= 0)
            return;

        var xRange = visibleXMax - visibleXMin;
        var yRange = visibleYMax - visibleYMin;
        if (xRange <= 0 || yRange <= 0)
            return;

        if (GridLinePixelSpacing(xStep, xRange, CanvasWidth) < MinGridPixelSpacing
            || GridLinePixelSpacing(yStep, yRange, CanvasHeight) < MinGridPixelSpacing)
            return;

        var tolerance = Math.Max(xStep, yStep) * 0.01;
        var startX = AlignNiceStepStart(visibleXMin, xStep);
        for (var x = startX; x <= visibleXMax + tolerance; x += xStep)
        {
            var clampedX = Math.Clamp(x, AxisXMin, AxisXMax);
            var screenX = AxisToScreen(new Point(clampedX, AxisYMin), viewMatrix).X;
            if (double.IsNaN(screenX) || double.IsInfinity(screenX))
                continue;
            if (screenX < -1 || screenX > CanvasWidth + 1)
                continue;

            GridLines.Add(new AxisTickInfo
            {
                X1 = screenX,
                Y1 = 0,
                X2 = screenX,
                Y2 = CanvasHeight
            });
        }

        var startY = AlignNiceStepStart(visibleYMin, yStep);
        for (var y = startY; y <= visibleYMax + tolerance; y += yStep)
        {
            var clampedY = Math.Clamp(y, AxisYMin, AxisYMax);
            var screenY = AxisToScreen(new Point(AxisXMin, clampedY), viewMatrix).Y;
            if (double.IsNaN(screenY) || double.IsInfinity(screenY))
                continue;
            if (screenY < -1 || screenY > CanvasHeight + 1)
                continue;

            GridLines.Add(new AxisTickInfo
            {
                X1 = 0,
                Y1 = screenY,
                X2 = CanvasWidth,
                Y2 = screenY
            });
        }
    }

    private double GridLinePixelSpacing(double step, double axisRange, double canvasLength)
    {
        if (axisRange <= 0 || canvasLength <= 0 || step <= 0)
            return 0;
        return step / axisRange * canvasLength * Math.Max(_currentZoomFactor, 1e-3);
    }

    private void PopulateXAxisTicks(Matrix viewMatrix, double step, string labelFormat, int decimals, double visibleXMin, double visibleXMax)
    {
        var xRange = visibleXMax - visibleXMin;
        if (step <= 0 || xRange <= 0) return;

        var tolerance = Math.Max(step * 0.1, xRange * 1e-6);
        var values = new List<double>();
        var startX = AlignNiceStepStart(visibleXMin, step);
        for (var value = startX; value <= visibleXMax + tolerance; value += step)
            values.Add(Math.Clamp(value, AxisXMin, AxisXMax));

        var majorPositions = new List<double>();
        double? previous = null;
        foreach (var axisValue in values)
        {
            var clampedValue = Math.Clamp(axisValue, AxisXMin, AxisXMax);
            if (previous.HasValue && Math.Abs(clampedValue - previous.Value) <= tolerance * 0.5) continue;

            var screenX = AxisToScreen(new Point(clampedValue, AxisYMin), viewMatrix).X;
            if (double.IsNaN(screenX) || double.IsInfinity(screenX)) continue;
            // Keep labels inside the plot so ClipToBounds does not hide them when the Viewbox is small.
            if (screenX < 4 || screenX > CanvasWidth - 4) continue;

            var roundingDecimals = Math.Min(decimals + 2, MaxAxisDecimalPlaces);
            var displayValue = Math.Round(clampedValue, roundingDecimals, MidpointRounding.AwayFromZero);
            if (Math.Abs(displayValue) < 1e-9) displayValue = 0;
            var label = displayValue.ToString(labelFormat, CultureInfo.CurrentCulture);

            var y2MajorValue = Math.Max(CanvasHeight - MajorTickLength, 0);
            var labelYValue = Math.Max(CanvasHeight - 18, 0);

            XAxisTicks.Add(new AxisTickInfo
            {
                X1 = screenX, Y1 = CanvasHeight, X2 = screenX, Y2 = y2MajorValue,
                LabelX = screenX + 2, LabelY = labelYValue, Label = label
            });
            majorPositions.Add(clampedValue);
            previous = clampedValue;
        }
        PopulateXAxisMinorTicks(viewMatrix, majorPositions, visibleXMin, visibleXMax);
    }

    private void PopulateYAxisTicks(Matrix viewMatrix, double step, string labelFormat, int decimals, double visibleYMin, double visibleYMax)
    {
        var yRange = visibleYMax - visibleYMin;
        if (step <= 0 || yRange <= 0 || double.IsNaN(CanvasWidth) || double.IsNaN(CanvasHeight) || CanvasWidth <= 0 || CanvasHeight <= 0) return;

        var tolerance = Math.Max(step * 0.1, yRange * 1e-6);
        var values = new List<double>();
        var startY = AlignNiceStepStart(visibleYMin, step);
        for (var value = startY; value <= visibleYMax + tolerance; value += step)
            values.Add(Math.Clamp(value, AxisYMin, AxisYMax));

        var majorPositions = new List<double>();
        double? previous = null;
        foreach (var axisValue in values)
        {
            var clampedValue = Math.Clamp(axisValue, AxisYMin, AxisYMax);
            if (previous.HasValue && Math.Abs(clampedValue - previous.Value) <= tolerance * 0.5) continue;

            var screenY = AxisToScreen(new Point(AxisXMin, clampedValue), viewMatrix).Y;
            if (double.IsNaN(screenY) || double.IsInfinity(screenY)) continue;
            if (screenY < 4 || screenY > CanvasHeight - 4) continue;

            var roundingDecimals = Math.Min(decimals + 2, MaxAxisDecimalPlaces);
            var displayValue = Math.Round(clampedValue, roundingDecimals, MidpointRounding.AwayFromZero);
            if (Math.Abs(displayValue) < 1e-9) displayValue = 0;
            var label = displayValue.ToString(labelFormat, CultureInfo.CurrentCulture);

            var x2Value = Math.Min(MajorTickLength, CanvasWidth);
            YAxisTicks.Add(new AxisTickInfo
            {
                X1 = 0, Y1 = screenY, X2 = x2Value, Y2 = screenY,
                LabelX = 6, LabelY = Math.Max(0, screenY - 10), Label = label
            });
            majorPositions.Add(clampedValue);
            previous = clampedValue;
        }
        PopulateYAxisMinorTicks(viewMatrix, majorPositions, visibleYMin, visibleYMax);
    }

    private void PopulateXAxisMinorTicks(Matrix viewMatrix, IReadOnlyList<double> majorPositions, double visibleXMin, double visibleXMax)
    {
        if (majorPositions.Count < 2) return;

        var subdivisions = Math.Max(2, MinorTickDivisionsPerMajor);
        var hasHalfTick = subdivisions % 2 == 0;
        var halfTickIndex = subdivisions / 2;
        var halfTickLength = (MajorTickLength + MinorTickLength) / 2.0;

        for (var i = 0; i < majorPositions.Count - 1; i++)
        {
            var start = majorPositions[i];
            var end = majorPositions[i + 1];
            var span = end - start;
            if (span <= double.Epsilon) continue;

            var increment = span / subdivisions;
            for (var j = 1; j < subdivisions; j++)
            {
                var value = start + increment * j;
                if (value <= visibleXMin || value >= visibleXMax) continue;
                if (value <= AxisXMin || value >= AxisXMax) continue;

                var screenX = AxisToScreen(new Point(value, AxisYMin), viewMatrix).X;
                if (double.IsNaN(screenX) || double.IsInfinity(screenX)) continue;
                if (screenX < -1 || screenX > CanvasWidth + 1) continue;

                var isHalfTick = hasHalfTick && j == halfTickIndex;
                var tickLength = isHalfTick ? halfTickLength : MinorTickLength;
                var y2Value = Math.Max(CanvasHeight - tickLength, 0);

                XAxisMinorTicks.Add(new AxisTickInfo
                {
                    X1 = screenX, Y1 = CanvasHeight, X2 = screenX, Y2 = y2Value,
                    LabelX = 0, LabelY = 0, Label = string.Empty
                });
            }
        }
    }

    private void PopulateYAxisMinorTicks(Matrix viewMatrix, IReadOnlyList<double> majorPositions, double visibleYMin, double visibleYMax)
    {
        if (majorPositions.Count < 2) return;

        var subdivisions = Math.Max(2, MinorTickDivisionsPerMajor);
        var hasHalfTick = subdivisions % 2 == 0;
        var halfTickIndex = subdivisions / 2;
        var horizontalHalfTickLength = (MajorTickLength + HorizontalMinorTickLength) / 2.0;

        for (var i = 0; i < majorPositions.Count - 1; i++)
        {
            var start = majorPositions[i];
            var end = majorPositions[i + 1];
            var span = end - start;
            if (span <= double.Epsilon) continue;

            var increment = span / subdivisions;
            for (var j = 1; j < subdivisions; j++)
            {
                var value = start + increment * j;
                if (value <= visibleYMin || value >= visibleYMax) continue;
                if (value <= AxisYMin || value >= AxisYMax) continue;

                var screenY = AxisToScreen(new Point(AxisXMin, value), viewMatrix).Y;
                if (double.IsNaN(screenY) || double.IsInfinity(screenY)) continue;
                if (screenY < -1 || screenY > CanvasHeight + 1) continue;

                var isHalfTick = hasHalfTick && j == halfTickIndex;
                var tickLength = isHalfTick ? horizontalHalfTickLength : HorizontalMinorTickLength;
                var x2MinorValue = Math.Min(tickLength, CanvasWidth);

                YAxisMinorTicks.Add(new AxisTickInfo
                {
                    X1 = 0, Y1 = screenY, X2 = x2MinorValue, Y2 = screenY,
                    LabelX = 0, LabelY = 0, Label = string.Empty
                });
            }
        }
    }

    private double CalculateAdaptiveStep(double baseStep, double axisRange, double canvasLength)
    {
        if (axisRange <= 0 || canvasLength <= 0)
            return NiceNumber(baseStep > 0 ? baseStep : DefaultGridSpacing, round: true);

        var zoom = Math.Max(_currentZoomFactor, 1e-3);
        var roughStep = axisRange * (TargetAxisTickPixelSpacing / (canvasLength * zoom));
        if (!(roughStep > 0) || double.IsNaN(roughStep) || double.IsInfinity(roughStep))
            roughStep = axisRange / Math.Max(MinimumAxisTickCount, 1);

        var step = NiceNumber(roughStep, round: true);
        if (step <= 0)
            step = NiceNumber(Math.Max(baseStep, DefaultGridSpacing), round: true);

        double Pixels(double candidate) =>
            candidate / axisRange * canvasLength * zoom;

        var maxStep = NiceNumber(axisRange / Math.Max(MinimumAxisTickCount - 1, 1), round: true);
        if (maxStep < step)
            maxStep = step;

        var guard = 0;
        while (Pixels(step) < MinAxisTickPixelSpacing && guard++ < 24)
        {
            var next = BumpNiceStep(step, +1);
            if (next <= step) break;
            if (next > axisRange && Pixels(step) > 0) break;
            step = next;
        }

        guard = 0;
        while (Pixels(step) > MaxAxisTickPixelSpacing && guard++ < 24)
        {
            var next = BumpNiceStep(step, -1);
            if (next >= step - 1e-15 || next <= 0) break;
            if (axisRange / next < MinimumAxisTickCount - 0.25) break;
            step = next;
        }

        if (maxStep > 0 && step > maxStep && Pixels(maxStep) >= MinAxisTickPixelSpacing * 0.5)
            step = maxStep;

        return step;
    }

    /// <summary>1–2–5×10ⁿ “nice” step used by professional FEM/CAD axis rulers.</summary>
    private static double NiceNumber(double value, bool round)
    {
        if (!(value > 0) || double.IsNaN(value) || double.IsInfinity(value))
            return 1.0;

        var exp = Math.Floor(Math.Log10(value));
        var pow10 = Math.Pow(10, exp);
        var fraction = value / pow10;
        double niceFraction;
        if (round)
        {
            if (fraction < 1.5) niceFraction = 1;
            else if (fraction < 3.0) niceFraction = 2;
            else if (fraction < 7.0) niceFraction = 5;
            else niceFraction = 10;
        }
        else
        {
            if (fraction <= 1.0) niceFraction = 1;
            else if (fraction <= 2.0) niceFraction = 2;
            else if (fraction <= 5.0) niceFraction = 5;
            else niceFraction = 10;
        }

        return niceFraction * pow10;
    }

    private static double BumpNiceStep(double step, int direction)
    {
        if (!(step > 0) || double.IsNaN(step) || double.IsInfinity(step))
            return 1.0;

        var exp = Math.Floor(Math.Log10(step));
        var pow10 = Math.Pow(10, exp);
        var fraction = step / pow10;
        int nice;
        if (fraction < 1.5) nice = 1;
        else if (fraction < 3.5) nice = 2;
        else if (fraction < 7.5) nice = 5;
        else nice = 10;

        if (direction >= 0)
        {
            if (nice < 2) nice = 2;
            else if (nice < 5) nice = 5;
            else { nice = 1; exp++; }
        }
        else
        {
            if (nice > 5) nice = 5;
            else if (nice > 2) nice = 2;
            else if (nice > 1) nice = 1;
            else { nice = 5; exp--; }
        }

        return nice * Math.Pow(10, exp);
    }

    private static double NiceFloor(double value, double step)
    {
        if (!(step > 0)) return value;
        return Math.Floor(value / step) * step;
    }

    private static double NiceCeil(double value, double step)
    {
        if (!(step > 0)) return value;
        return Math.Ceiling(value / step) * step;
    }

    private static int DetermineDecimalPlaces(double step)
    {
        var absStep = Math.Abs(step);
        if (absStep <= double.Epsilon) return 0;
        var decimals = 0;
        var tolerance = Math.Max(absStep, 1.0) * 1e-6;
        while (decimals < MaxAxisDecimalPlaces)
        {
            var rounded = Math.Round(absStep, decimals, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded - absStep) <= tolerance) break;
            decimals++;
        }
        return Math.Min(decimals, MaxAxisDecimalPlaces);
    }

    private static string BuildLabelFormat(int decimals)
    {
        if (decimals <= 0) return "0";
        return $"0.{new string('#', Math.Min(decimals, MaxAxisDecimalPlaces))}";
    }

    private static string FormatCoordinate(Point point)
    {
        static string Format(double value) => double.IsNaN(value) || double.IsInfinity(value) ? "--" : value.ToString("0.00", CultureInfo.InvariantCulture);
        return $"X: {Format(point.X)}, Y: {Format(point.Y)}";
    }

    /// <summary>Converts canvas coordinates (0..CanvasWidth, 0..CanvasHeight) to axis coordinates.</summary>
    public Point ConvertCanvasToAxis(Point canvasPoint)
    {
        var normalizedX = CanvasWidth <= 0 ? 0 : canvasPoint.X / CanvasWidth;
        var normalizedY = CanvasHeight <= 0 ? 0 : canvasPoint.Y / CanvasHeight;
        var axisX = AxisXMin + normalizedX * (AxisXMax - AxisXMin);
        var axisY = AxisYMax - normalizedY * (AxisYMax - AxisYMin);
        return new Point(axisX, axisY);
    }

    /// <summary>Converts axis coordinates to canvas coordinates (0..CanvasWidth, 0..CanvasHeight).</summary>
    public Point ConvertAxisToCanvas(Point axisPoint)
    {
        var xRange = Math.Max(1e-6, AxisXMax - AxisXMin);
        var yRange = Math.Max(1e-6, AxisYMax - AxisYMin);
        var normalizedX = (axisPoint.X - AxisXMin) / xRange;
        var normalizedY = (AxisYMax - axisPoint.Y) / yRange;
        return new Point(normalizedX * CanvasWidth, normalizedY * CanvasHeight);
    }

    private double ConvertAxisRadiusToCanvas(Point axisCenter, double axisRadius)
    {
        if (axisRadius <= 0 || double.IsNaN(axisRadius)) return 0;
        var centerCanvas = ConvertAxisToCanvas(axisCenter);
        var axisEdgeX = new Point(axisCenter.X + axisRadius, axisCenter.Y);
        var edgeCanvasX = ConvertAxisToCanvas(axisEdgeX);
        var radiusX = Math.Abs(edgeCanvasX.X - centerCanvas.X);
        var axisEdgeY = new Point(axisCenter.X, axisCenter.Y + axisRadius);
        var edgeCanvasY = ConvertAxisToCanvas(axisEdgeY);
        var radiusY = Math.Abs(edgeCanvasY.Y - centerCanvas.Y);
        return Math.Min(radiusX, radiusY);
    }

    private double ConvertCanvasRadiusToAxis(Point centerCanvas, double canvasRadius)
    {
        if (canvasRadius <= 0 || double.IsNaN(canvasRadius) || double.IsNaN(centerCanvas.X) || double.IsNaN(centerCanvas.Y))
            return double.NaN;
        var centerAxis = ConvertCanvasToAxis(centerCanvas);
        var edgeCanvasX = new Point(centerCanvas.X + canvasRadius, centerCanvas.Y);
        var edgeAxisX = ConvertCanvasToAxis(edgeCanvasX);
        var radiusX = Math.Abs(edgeAxisX.X - centerAxis.X);
        var edgeCanvasY = new Point(centerCanvas.X, centerCanvas.Y + canvasRadius);
        var edgeAxisY = ConvertCanvasToAxis(edgeCanvasY);
        var radiusY = Math.Abs(edgeAxisY.Y - centerAxis.Y);
        return (radiusX + radiusY) / 2.0;
    }

    private void RefreshAvailablePointLabels()
    {
        AvailablePointLabels.Clear();
        foreach (var row in GeometryVertexRows)
            AvailablePointLabels.Add(row.PointLabel);
    }

    /// <summary>
    /// Keeps L1..Ln in sync with <see cref="GeometryLineSegments"/> count. Uses add/remove only — never
    /// <see cref="ObservableCollection{T}.Clear"/> — so canvas picks are not reset when unrelated UI actions run.
    /// </summary>
    private void RefreshAvailableLineNumbers()
    {
        var n = GeometryLineSegments.Count;
        while (AvailableLineNumbers.Count > n)
            AvailableLineNumbers.RemoveAt(AvailableLineNumbers.Count - 1);
        for (var i = AvailableLineNumbers.Count; i < n; i++)
            AvailableLineNumbers.Add(new LineOrCircleItem($"L{i + 1}", i + 1));
    }

    private void RemoveVertexAt(int index)
    {
        if (index < 0 || index >= GeometryVertexRows.Count) return;
        RecordUndo("Remove geometry vertex");
        var removedStable = GeometryVertexRows[index].StablePointId;
        GeometryVertexRows.RemoveAt(index);
        for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
        {
            if (GeometryInternalEdges[i].TouchesStableId(removedStable))
                GeometryInternalEdges.RemoveAt(i);
        }
        for (int i = 0; i < GeometryVertexRows.Count; i++) GeometryVertexRows[i].Index = i;
        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments();
    }

    private void RemoveVertexByDisplay(object? parameter)
    {
        if (parameter is not GeometryVertexDisplay display) return;
        var index = GeometryVertexRows.IndexOf(display);
        if (index >= 0) RemoveVertexAt(index);
    }

    /// <summary>
    /// Delete a Geometry Explorer / Details payload using the same Actions commands as Geometry Definition Details.
    /// </summary>
    public void DeleteExplorerPayload(object? payload)
    {
        switch (payload)
        {
            case GeometryVertexDisplay vertex:
                RemoveGeometryVertexCommand.Execute(vertex);
                return;
            case GeometryLineSegment line:
                SetHighlightedLineSegment(line.LineNumber);
                DeleteLineSegmentCommand.Execute(null);
                if (GeometryLineSegments.Contains(line))
                    GeometryLineSegments.Remove(line);
                return;
            case GeometrySurface surface:
                SelectedGeometrySurface = surface;
                DeleteSelectedSurfaceCommand.Execute(null);
                return;
        }
    }

    private void DeleteSelectedLineSegment()
    {
        if (!HighlightedLineNumber.HasValue) return;
        RemoveLineSegmentAt(HighlightedLineNumber.Value);
        SetHighlightedLineSegment(null);
    }

    private void RemoveLineSegmentAt(int lineNumber)
    {
        if (lineNumber <= 0) return;
        RecordUndo("Delete line segment");
        var seg = GeometryLineSegments.FirstOrDefault(s => s.LineNumber == lineNumber);
        if (seg == null) return;

        if (seg.IsCircle)
        {
            for (var i = GeometryInternalCircles.Count - 1; i >= 0; i--)
            {
                if (GeometryInternalCircles[i].Id == seg.InternalCircleId)
                {
                    GeometryInternalCircles.RemoveAt(i);
                    break;
                }
            }

            RefreshGeometryLineSegments();
            return;
        }

        if (lineNumber <= _boundarySegmentCount)
        {
            var toVertex = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
            if (toVertex == null) return;
            var index = GeometryVertexRows.IndexOf(toVertex);
            if (index >= 0) RemoveVertexAt(index);
            return;
        }

        var fromV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
        var toV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
        if (fromV == null || toV == null) return;

        if (seg.IsArc && !string.IsNullOrEmpty(seg.ThroughPoint))
        {
            var midV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ThroughPoint);
            if (midV == null) return;
            for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
            {
                var ie = GeometryInternalEdges[i];
                if (!ie.IsArc) continue;
                if (ie.StableStart == fromV.StablePointId && ie.StableMid == midV.StablePointId && ie.StableEnd == toV.StablePointId)
                    GeometryInternalEdges.RemoveAt(i);
            }
        }
        else
        {
            var min = Math.Min(fromV.StablePointId, toV.StablePointId);
            var max = Math.Max(fromV.StablePointId, toV.StablePointId);
            for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
            {
                var ie = GeometryInternalEdges[i];
                if (ie.IsArc) continue;
                if (ie.StableMin == min && ie.StableMax == max)
                    GeometryInternalEdges.RemoveAt(i);
            }
        }

        RefreshGeometryLineSegments();
    }

    /// <summary>
    /// Adds a vertex: if a boundary line segment is highlighted (Geometry Line Segments grid), inserts on that edge
    /// at midpoint so the boundary becomes …→A→new→B→… and the grid shows A→new and new→B (e.g. 3→5 and 5→4).
    /// Otherwise appends at default axis position (legacy behavior).
    /// </summary>
    private void AddVertexAtEnd()
    {
        if (TryAddVertexSplittingHighlightedBoundaryEdge())
            return;

        var idx = GeometryVertexRows.Count;
        var display = new GeometryVertexDisplay { Index = idx, AxisX = AxisXMin, AxisY = AxisYMin };
        display.StablePointId = AllocateStablePointId();
        display.Owner = this;
        GeometryVertexRows.Add(display);
        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments();
    }

    /// <summary>
    /// If <see cref="HighlightedLineNumber"/> refers to a boundary segment Lk, inserts a new vertex at the axis midpoint
    /// of that edge so the boundary order is …, Vi, Vnew, Vj, … (same as clicking on that edge on the canvas).
    /// </summary>
    private bool TryAddVertexSplittingHighlightedBoundaryEdge()
    {
        if (!HighlightedLineNumber.HasValue || GeometryVertexRows.Count < 2)
            return false;

        var hl = HighlightedLineNumber.Value;
        var seg = GeometryLineSegments.FirstOrDefault(s => s.LineNumber == hl);
        if (seg == null || !seg.IsBoundaryEdge)
            return false;

        var splitSegmentIndex = hl - 1;
        if (splitSegmentIndex < 0 || splitSegmentIndex >= _boundarySegmentCount)
            return false;

        var count = GeometryVertexRows.Count;
        var isClosed = ShouldAutoCloseBoundary(count);
        var next = isClosed ? (splitSegmentIndex + 1) % count : splitSegmentIndex + 1;
        if (next >= count)
            return false;

        var va = GeometryVertexRows[splitSegmentIndex];
        var vb = GeometryVertexRows[next];
        var mid = new Point(
            (va.AxisX + vb.AxisX) * 0.5,
            (va.AxisY + vb.AxisY) * 0.5);

        var insertIndex = isClosed && splitSegmentIndex == count - 1 ? count : splitSegmentIndex + 1;
        AddVertexAtInsertIndex(insertIndex, mid, splitSegmentIndex);
        return true;
    }

    private void AddSharedGeometryEdge()
    {
        var fromLabel = SharedEdgeFromPointLabel.Trim();
        var toLabel = SharedEdgeToPointLabel.Trim();
        if (string.IsNullOrEmpty(fromLabel) || string.IsNullOrEmpty(toLabel) || fromLabel == toLabel)
            return;
        var fromV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == fromLabel);
        var toV = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == toLabel);
        if (fromV == null || toV == null) return;
        var sa = fromV.StablePointId;
        var sb = toV.StablePointId;
        if (HasInternalEdgeStablePair(sa, sb)) return;
        GeometryInternalEdges.Add(new GeometryInternalEdge(sa, sb));
        RefreshGeometryLineSegments();
    }

    /// <summary>
    /// Adds one shared (non-boundary) line segment as a new grid row: first vertex pair (i,j) that is not already
    /// a boundary edge or an existing shared edge. Matches legacy "add line segment" intent without using the combos.
    /// </summary>
    private void AddLineSegmentRow()
    {
        if (GeometryVertexRows.Count < 2) return;
        for (var i = 0; i < GeometryVertexRows.Count; i++)
        {
            for (var j = i + 1; j < GeometryVertexRows.Count; j++)
            {
                var sa = GeometryVertexRows[i].StablePointId;
                var sb = GeometryVertexRows[j].StablePointId;
                if (HasInternalEdgeStablePair(sa, sb)) continue;
                GeometryInternalEdges.Add(new GeometryInternalEdge(sa, sb));
                RefreshGeometryLineSegments();
                return;
            }
        }
    }

    private bool IsBoundaryUndirectedEdge(int stableA, int stableB)
    {
        var key = StableUnorderedPair(stableA, stableB);
        var n = GeometryVertexRows.Count;
        if (n < 2) return false;
        if (!ShouldAutoCloseBoundary(n))
            return false;
        for (var i = 0; i < n; i++)
        {
            var next = (i + 1) % n;
            var a = GeometryVertexRows[i].StablePointId;
            var b = GeometryVertexRows[next].StablePointId;
            if (StableUnorderedPair(a, b) == key)
                return true;
        }
        return false;
    }

    private static bool InternalEdgeListContainsPair(IReadOnlyList<GeometryInternalEdge> edges, int stableA, int stableB)
    {
        var key = StableUnorderedPair(stableA, stableB);
        foreach (var ie in edges)
        {
            if (ie.StableMin == key.Min && ie.StableMax == key.Max)
                return true;
        }
        return false;
    }

    private bool HasInternalEdgeStablePair(int stableA, int stableB) =>
        InternalEdgeListContainsPair(GeometryInternalEdges, stableA, stableB);

    private void AddTunnelCircleFromInputs()
    {
        if (!double.TryParse(TunnelCenterXInput, out var cx) || !double.TryParse(TunnelCenterYInput, out var cy) || !double.TryParse(TunnelRadiusInput, out var r) || r <= 0) return;
        AddTunnelCircle(cx, cy, r);
        TunnelCenterXInput = TunnelCenterYInput = TunnelRadiusInput = "";
    }

    /// <summary>
    /// Public helper used by the Tunnel Wizard to register a circular tunnel programmatically.
    /// Mirrors <see cref="AddTunnelCircleFromInputs"/> without requiring the input-textbox dance.
    /// </summary>
    public TunnelExcavationCircleRow AddTunnelCircle(double centerX, double centerY, double radius)
    {
        var circle = new TunnelExcavationCircle { Center = new Point(centerX, centerY), Radius = radius };
        FilteredTunnelExcavationCircles.Add(circle);
        var row = new TunnelExcavationCircleRow
        {
            CircleNumber = TunnelExcavationCircleRows.Count + 1,
            CenterX = centerX,
            CenterY = centerY,
            Radius = radius,
            Circle = circle,
            UnitLabel = ProjectUnitSystem.LengthLabel(ProjectSettings?.Project.Units),
        };
        TunnelExcavationCircleRows.Add(row);
        FilteredTunnelExcavationCircleRows.Add(row);
        return row;
    }

    /// <summary>
    /// Registers a non-circular tunnel polygon (horseshoe, rectangular, NATM arch, DXF) for the
    /// Tunnel Wizard. Adds it to <see cref="FilteredTunnelExcavationPolygons"/> so the mesher
    /// picks it up as a polygon hole and the canvas overlay can render it.
    /// </summary>
    /// <param name="axisPolygon">Closed CCW polygon in axis space.</param>
    /// <param name="kind">Shape family for tooltips and labels.</param>
    public TunnelExcavationPolygon AddTunnelPolygon(IReadOnlyList<Point> axisPolygon, TunnelShapeKind kind)
    {
        if (axisPolygon == null || axisPolygon.Count < 3)
            throw new ArgumentException("Tunnel polygon needs at least 3 points.", nameof(axisPolygon));

        double sumX = 0, sumY = 0;
        var canvasPts = new PointCollection(axisPolygon.Count);
        foreach (var p in axisPolygon)
        {
            sumX += p.X;
            sumY += p.Y;
            canvasPts.Add(ConvertAxisToCanvas(p));
        }

        var poly = new TunnelExcavationPolygon
        {
            Kind = kind,
            AxisPoints = new List<Point>(axisPolygon),
            CanvasPoints = canvasPts,
            CenterX = sumX / axisPolygon.Count,
            CenterY = sumY / axisPolygon.Count,
        };
        FilteredTunnelExcavationPolygons.Add(poly);
        return poly;
    }

    private void RemoveTunnelCircleAt(int index)
    {
        if (index < 0 || index >= TunnelExcavationCircleRows.Count) return;
        var row = TunnelExcavationCircleRows[index];
        if (row.Circle != null) FilteredTunnelExcavationCircles.Remove(row.Circle);
        TunnelExcavationCircleRows.RemoveAt(index);
        FilteredTunnelExcavationCircleRows.Remove(row);
        for (int i = 0; i < TunnelExcavationCircleRows.Count; i++) TunnelExcavationCircleRows[i].CircleNumber = i + 1;
    }

    private void AddSurface()
    {
        var n = GeometrySurfaces.Count + 1;
        var s = new GeometrySurface(n);
        GeometrySurfaces.Add(s);
        SelectedGeometrySurface = s;
        RefreshAvailableLineNumbers();
    }

    /// <summary>
    /// Geometry Explorer → Surfaces: reuse an unlocked draft or create one so canvas line clicks form the surface.
    /// </summary>
    public void BeginExplorerSurfaceDefinition()
    {
        CancelDrawLineMode();
        CancelDrawArcMode();
        CancelDrawCircleMode();
        var draft = GeometrySurfaces.FirstOrDefault(s => !s.IsPolygonLocked);
        if (draft == null)
        {
            AddSurface();
            return;
        }

        SelectedGeometrySurface = draft;
    }

    private void SetClosedPolygonMode()
    {
        IsOpenPolygonBoundaryMode = false;
        IsNewPointMode = true;
        GeometryImportStatusMessage = "Closed Polygon mode: add points; boundary edges close automatically after three or more points.";
    }

    private void SetOpenPolygonMode()
    {
        IsOpenPolygonBoundaryMode = true;
        IsNewPointMode = true;
        GeometryImportStatusMessage = "Open Polygon mode: add points first, select point pairs to create line segments, then create the surface from the Surface panel.";
    }

    private void AddPointFromInput()
    {
        if (!double.TryParse(GeometryXInput, out var x) || !double.TryParse(GeometryYInput, out var y)) return;
        var axisPoint = new Point(x, y);
        // Typed coordinates are authoritative: place the point exactly where entered.
        // Only split an existing edge when the typed point lies genuinely ON that edge;
        // do NOT relocate interior points onto the boundary (that snap makes precisely
        // typed points appear to "disappear" into the outline).
        if (TryFindInsertOnExistingSegment(axisPoint, out var insertIndex, out var splitSegmentIndex, out var snappedAxis))
            AddVertexAtInsertIndex(insertIndex, snappedAxis, splitSegmentIndex);
        else
        {
            var idx = GeometryVertexRows.Count;
            var display = new GeometryVertexDisplay { Index = idx, AxisX = x, AxisY = y };
            display.StablePointId = AllocateStablePointId();
            display.Owner = this;
            GeometryVertexRows.Add(display);
            RefreshGeometryPath();
            RefreshAvailablePointLabels();
            RefreshGeometryLineSegments();
        }
        GeometryXInput = GeometryYInput = "";
    }

    private double ComputeAxisHitTolerance()
    {
        var rx = Math.Max(AxisXMax - AxisXMin, 1e-9);
        var ry = Math.Max(AxisYMax - AxisYMin, 1e-9);
        return Math.Max(rx, ry) * 2e-3;
    }

    /// <summary>Distance from p to segment AB; tParam is position on infinite line (0=a, 1=b); projection is closest point on the segment.</summary>
    private static double DistancePointToSegmentAxis(Point p, Point a, Point b, out double tParam, out Point projection)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 < 1e-18)
        {
            tParam = 0;
            projection = a;
            var ddx = p.X - a.X;
            var ddy = p.Y - a.Y;
            return Math.Sqrt(ddx * ddx + ddy * ddy);
        }
        tParam = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        var t = Math.Clamp(tParam, 0, 1);
        projection = new Point(a.X + t * dx, a.Y + t * dy);
        var px = p.X - projection.X;
        var py = p.Y - projection.Y;
        return Math.Sqrt(px * px + py * py);
    }

    /// <summary>If axisPoint lies near an interior point of an edge, returns insert index and segment index to split (material copy). Closing edge (last→first) inserts at end.</summary>
    /// <param name="tolMultiplier">Multiplies distance tolerance (e.g. 2.5 when re-testing after a failed first hit).</param>
    /// <param name="relaxedEndpointEpsilon">Uses segment-length–scaled endpoint band so near-miss clicks still count as interior.</param>
    private bool TryFindInsertOnExistingSegment(Point axisPoint, out int insertIndex, out int splitSegmentIndex, out Point snappedAxis)
    {
        return TryFindInsertOnExistingSegment(axisPoint, out insertIndex, out splitSegmentIndex, out snappedAxis, 1.0, false);
    }

    private bool TryFindInsertOnExistingSegment(Point axisPoint, out int insertIndex, out int splitSegmentIndex, out Point snappedAxis, double tolMultiplier, bool relaxedEndpointEpsilon)
    {
        insertIndex = -1;
        splitSegmentIndex = -1;
        snappedAxis = axisPoint;
        var count = GeometryVertexRows.Count;
        if (count < 2) return false;
        var isClosed = ShouldAutoCloseBoundary(count);
        var tol = ComputeAxisHitTolerance() * tolMultiplier;

        double bestDist = double.MaxValue;
        var bestInsert = -1;
        var bestSegIdx = -1;
        var bestSnap = axisPoint;

        for (var i = 0; i < count; i++)
        {
            var next = isClosed ? (i + 1) % count : i + 1;
            if (next >= count) break;

            var va = GeometryVertexRows[i];
            var vb = GeometryVertexRows[next];
            var a = new Point(va.AxisX, va.AxisY);
            var b = new Point(vb.AxisX, vb.AxisY);
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len2 = dx * dx + dy * dy;
            var segLen = len2 > 1e-18 ? Math.Sqrt(len2) : 0.0;
            // Reject near-endpoints so we only split edges; scale with segment length (fixed 1e-3 was too strict in axis units).
            var endpointEps = relaxedEndpointEpsilon
                ? Math.Max(1e-9, Math.Min(0.02, segLen * 1e-4))
                : Math.Max(1e-9, Math.Min(1e-3, segLen * 1e-4));

            var dist = DistancePointToSegmentAxis(axisPoint, a, b, out var tParam, out var proj);
            var tOnSeg = Math.Clamp(tParam, 0, 1);
            if (tOnSeg <= endpointEps || tOnSeg >= 1.0 - endpointEps) continue;
            if (dist > tol) continue;
            if (dist >= bestDist) continue;
            bestDist = dist;
            bestSegIdx = i;
            bestSnap = proj;
            // Closing edge V_{n-1} → V_0: append so order stays V0..V_{n-1}, NEW and edges …,(n-1)→NEW, NEW→V0
            if (isClosed && i == count - 1 && next == 0)
                bestInsert = count;
            else
                bestInsert = next;
        }

        if (bestInsert < 0) return false;
        insertIndex = bestInsert;
        splitSegmentIndex = bestSegIdx;
        snappedAxis = bestSnap;
        return true;
    }

    /// <summary>When a vertex was appended but the click actually lay on a boundary edge, move it into the vertex ring between the two endpoints (same as a successful edge hit).</summary>
    private bool TryRelocateLastVertexOntoBoundaryIfInterior(out Point snappedAxis)
    {
        snappedAxis = default;
        if (GeometryVertexRows.Count < 3) return false;

        var lastIdx = GeometryVertexRows.Count - 1;
        var ax = GeometryVertexRows[lastIdx].AxisX;
        var ay = GeometryVertexRows[lastIdx].AxisY;
        var stable = GeometryVertexRows[lastIdx].StablePointId;

        var removedStable = GeometryVertexRows[lastIdx].StablePointId;
        GeometryVertexRows.RemoveAt(lastIdx);
        for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
        {
            if (GeometryInternalEdges[i].TouchesStableId(removedStable))
                GeometryInternalEdges.RemoveAt(i);
        }
        for (var j = 0; j < GeometryVertexRows.Count; j++)
            GeometryVertexRows[j].Index = j;

        var axisPt = new Point(ax, ay);
        if (!TryFindInsertOnExistingSegment(axisPt, out var insertIndex, out var splitSegmentIndex, out snappedAxis, 2.5, true))
        {
            var display = new GeometryVertexDisplay { Index = GeometryVertexRows.Count, AxisX = ax, AxisY = ay };
            display.StablePointId = stable;
            display.Owner = this;
            GeometryVertexRows.Add(display);
            for (var j = 0; j < GeometryVertexRows.Count; j++)
                GeometryVertexRows[j].Index = j;
            snappedAxis = axisPt;
            return false;
        }

        AddVertexAtInsertIndex(insertIndex, snappedAxis, splitSegmentIndex);
        GeometryVertexRows[insertIndex].StablePointId = stable;
        return true;
    }

    private static (string Tag, string UnitWeight, bool IsSaturated, string SaturatedUnitWeight, string Cohesion, string FrictionAngle, string YoungsModulus, string PoissonsRatio, string Density, bool ConsiderDensityForFE, bool PlaneStrain) CaptureSegmentMaterial(GeometryLineSegment s)
        => (s.Tag ?? "", s.UnitWeight, s.IsSaturatedUnitWeightEnabled, s.SaturatedUnitWeight, s.Cohesion, s.FrictionAngle, s.YoungsModulus, s.PoissonsRatio, s.Density, s.ConsiderDensityForFE, s.PlaneStrain);

    private static void ApplySegmentMaterialSnapshot(GeometryLineSegment seg, (string Tag, string UnitWeight, bool IsSaturated, string SaturatedUnitWeight, string Cohesion, string FrictionAngle, string YoungsModulus, string PoissonsRatio, string Density, bool ConsiderDensityForFE, bool PlaneStrain) p)
    {
        seg.Tag = p.Tag;
        seg.UnitWeight = p.UnitWeight;
        seg.IsSaturatedUnitWeightEnabled = p.IsSaturated;
        seg.SaturatedUnitWeight = p.SaturatedUnitWeight;
        seg.Cohesion = p.Cohesion;
        seg.FrictionAngle = p.FrictionAngle;
        seg.YoungsModulus = p.YoungsModulus;
        seg.PoissonsRatio = p.PoissonsRatio;
        seg.Density = p.Density;
        seg.ConsiderDensityForFE = p.ConsiderDensityForFE;
        seg.PlaneStrain = p.PlaneStrain;
    }

    /// <summary>Applies saved material props to both line segments that meet at the inserted vertex.</summary>
    private void ApplySplitMaterialToIncidentSegments(string newPointLabel, (string Tag, string UnitWeight, bool IsSaturated, string SaturatedUnitWeight, string Cohesion, string FrictionAngle, string YoungsModulus, string PoissonsRatio, string Density, bool ConsiderDensityForFE, bool PlaneStrain) snapshot)
    {
        foreach (var seg in GeometryLineSegments)
        {
            if (seg.FromPoint != newPointLabel && seg.ToPoint != newPointLabel) continue;
            ApplySegmentMaterialSnapshot(seg, snapshot);
        }
    }

    private void AddVertexAtInsertIndex(int insertIndex, Point snappedAxis, int splitSegmentIndex)
    {
        int? splitLineNumber = null;
        (string Tag, string UnitWeight, bool IsSaturated, string SaturatedUnitWeight, string Cohesion, string FrictionAngle, string YoungsModulus, string PoissonsRatio, string Density, bool ConsiderDensityForFE, bool PlaneStrain)? snapshot = null;
        if (splitSegmentIndex >= 0 && splitSegmentIndex < _boundarySegmentCount)
        {
            var seg = GeometryLineSegments[splitSegmentIndex];
            splitLineNumber = seg.LineNumber;
            snapshot = CaptureSegmentMaterial(seg);
        }

        var display = new GeometryVertexDisplay { Index = insertIndex, AxisX = snappedAxis.X, AxisY = snappedAxis.Y };
        display.StablePointId = AllocateStablePointId();
        if (splitLineNumber.HasValue)
            display.SplitFromLineNumber = splitLineNumber.Value;
        display.Owner = this;
        GeometryVertexRows.Insert(insertIndex, display);
        for (var j = 0; j < GeometryVertexRows.Count; j++)
            GeometryVertexRows[j].Index = j;

        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments();

        if (snapshot.HasValue)
        {
            var newLabel = GeometryVertexRows[insertIndex].PointLabel;
            ApplySplitMaterialToIncidentSegments(newLabel, snapshot.Value);
        }
    }

    private void DeleteLastVertex()
    {
        if (GeometryVertexRows.Count == 0) return;
        RemoveVertexAt(GeometryVertexRows.Count - 1);
    }

    private void ClearVertices()
    {
        CancelDrawCircleMode();
        ClearGeometryImportStatusMessage();
        GeometryVertexRows.Clear();
        GeometryVertices.Clear();
        GeometryLineSegments.Clear();
        GeometryInternalEdges.Clear();
        GeometryInternalCircles.Clear();
        _nextInternalCircleId = 1;
        ExcavationOperations.Clear();
        _selectedExcavationOperation = null;
        OnPropertyChanged(nameof(SelectedExcavationOperation));
        ClearSurfaceBooleanCanvasResult();
        _hasAddedElevationLayer = false;
        _materialLayerCutElevations.Clear();
        ClearNonHorizontalLayerCuts();
        _layerMaterialAssignmentByDisplayNumber.Clear();
        _layerDisplayNameByDisplayNumber.Clear();
        _layerDisplayColorArgb.Clear();
        _nextStablePointId = 1;
        RefreshLayerCoordinateSummaries();
        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshAvailableLineNumbers();
        RefreshGeometryLineSegments();
    }

    public void RefreshGeometryPath() => RefreshGeometryPath(ensureGroundwaterAxisBounds: true);

    private void RefreshGeometryPath(bool ensureGroundwaterAxisBounds)
    {
        var pts = new PointCollection();
        foreach (var row in GeometryVertexRows)
            pts.Add(ConvertAxisToCanvas(new Point(row.AxisX, row.AxisY)));
        GeometryPathPoints = pts;
        OnPropertyChanged(nameof(GeometryPathPoints));
        RefreshBoundaryOutlinePoints(pts);
        if (IsGeometryBulkUpdate)
            return;
        UpdateAxisTicks();
        RefreshHighlightedSegmentPoints();
        RebuildExplorerHighlight();
        RefreshSurfaceRegionOverlays();
        RefreshInternalEdgeCanvasLines();
        RefreshGroundwaterOverlays(ensureAxisBounds: ensureGroundwaterAxisBounds);
        RefreshNonHorizontalLayerPreview();
        RefreshNonHorizontalLayerCanvasLines();
        try { RefreshGeologicalOverlays(); } catch { /* overlay partial may be mid-init */ }
        try { RefreshSlopeImageCanvasPlacement(); } catch { /* overlay partial may be mid-init */ }
        try { RefreshFieldStationOverlays(); } catch { /* overlay partial may be mid-init */ }
    }

    private void RefreshBoundaryOutlinePoints(PointCollection vertexPath)
    {
        var outline = new PointCollection();
        foreach (var p in vertexPath)
            outline.Add(p);
        if (ShowBoundaryPolylineOutline && ShouldAutoCloseBoundary(GeometryVertexRows.Count))
            outline.Add(outline[0]);
        BoundaryOutlinePoints = outline;
        OnPropertyChanged(nameof(BoundaryOutlinePoints));
        OnPropertyChanged(nameof(ShowBoundaryPolylineOutline));
    }

    /// <summary>Imports points, lines, and surfaces from a CSV or DXF file.</summary>
    private void ImportGeometryFromFile()
    {
        if (!GeometryImportService.TryPickImportFile(out var filePath) || string.IsNullOrWhiteSpace(filePath))
            return;

        ClearGeometryImportStatusMessage();

        try
        {
            var options = new GeometryImportOptions
            {
                ReplaceExisting = true,
                CoordinateSystem = _projectSettings?.Project.CoordinateSystem
            };

            if (GeometryImportService.IsDxfFile(filePath))
            {
                var dialogVm = new GeometryImportOptionsDialogViewModel
                {
                    ReplaceExisting = GeometryVertexRows.Count == 0
                        || AppDialogs.Show(
                            "Replace existing geometry with imported DXF entities?",
                            "Import Geometry",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) == MessageBoxResult.Yes
                };
                var dialog = new GeometryImportOptionsDialog(dialogVm)
                {
                    Owner = Application.Current?.MainWindow
                };
                if (dialog.ShowDialog() != true)
                    return;
                options = dialogVm.ToOptions();
            }
            else if (GeometryVertexRows.Count > 0 && options.ReplaceExisting)
            {
                var sourceLabel = GeometryImportService.IsKernelFormat(filePath) ? "imported geometry" : "imported CSV data";
                if (AppDialogs.Show(
                        $"Replace existing geometry with {sourceLabel}?",
                        "Import Geometry",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }

            RecordUndo("Import geometry");

            if (!options.ImportPoints && !options.ImportLines && !options.ImportSurfaces)
            {
                var validationMessage = "Select at least one entity type to import (points, lines/polylines, or closed surfaces).";
                GeometryImportStatusMessage = $"No importable geometry found in {Path.GetFileName(filePath)}.";
                AppDialogs.Show(
                    validationMessage,
                    "Import Geometry",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var parseResult = GeometryImportService.ParseFileWithDiagnostics(filePath, options);
            if (parseResult.Document.IsEmpty)
            {
                var message = parseResult.DxfDiagnostics?.FormatEmptyMessage(options)
                    ?? "No geometry entities were found in the selected file.";
                var summary = parseResult.DxfDiagnostics?.FormatStatusSummary();
                GeometryImportStatusMessage = string.IsNullOrWhiteSpace(summary)
                    ? $"No importable geometry found in {Path.GetFileName(filePath)}."
                    : $"No importable geometry in {Path.GetFileName(filePath)}. Found: {summary}.";
                AppDialogs.Show(
                    message,
                    "Import Geometry",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = GeometryImportApplicator.ApplyToXfemGeometry(this, parseResult.Document, options);
            RefreshGeometryPath();
            GeometryImportStatusMessage =
                $"Imported {result.PointCount} point(s), {result.LineCount} line(s), {result.SurfaceCount} surface(s) from {Path.GetFileName(filePath)}.";
        }
        catch (Exception ex)
        {
            GeometryImportStatusMessage = $"Could not import geometry from {Path.GetFileName(filePath)}.";
            AppDialogs.Show(
                $"Could not import geometry:{Environment.NewLine}{ex.Message}",
                "Import Geometry",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ExportGeometryToCsv()
    {
        if (GeometryVertexRows.Count == 0 && GeologicalFeatures.Count == 0)
        {
            AppDialogs.Show(
                "There is no geometry to export.",
                "Export Geometry",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!GeometryImportService.TryPickExportFile(out var path) || string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var ext = Path.GetExtension(path);
            if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var document = GeometryImportApplicator.BuildExportDocument(this);
                CsvGeometryImporter.Export(path, document);
            }
            else if (ext.Equals(".dxf", StringComparison.OrdinalIgnoreCase))
            {
                DxfGeometryExporter.Export(this, path);
            }
            else if (ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                LandXmlGeometryImporter.Export(BuildPlanarKernel(), path);
            }
            else if (ext.Equals(".geojson", StringComparison.OrdinalIgnoreCase)
                     || ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                GeoJsonGeometryInterchange.Export(BuildPlanarKernel(), path);
            }
            else
            {
                throw new NotSupportedException($"Unsupported export type: {ext}");
            }

            GeometryImportStatusMessage = $"Exported geometry to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            AppDialogs.Show(
                $"Could not export geometry:{Environment.NewLine}{ex.Message}",
                "Export Geometry",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Builds geometry corner points and boundary line segments (L1..Ln) from the
    /// outer boundary of the discretized mesh on the Mesh tab canvas.
    /// </summary>
    public void ImportBoundaryFromMesh()
    {
        if (_meshSource is not { IsDiscretized: true })
        {
            GeometryImportStatusMessage = "Load or discretize a mesh on the Mesh tab first.";
            return;
        }

        if (_meshSource.DiscretizationNodes.Count < 3 || _meshSource.MeshInformationRows.Count == 0)
        {
            GeometryImportStatusMessage = "Mesh has no nodes or elements to extract a boundary from.";
            return;
        }

        var disc = _meshSource.DiscretizationNodes;
        _meshSource.EnsureDiscretizationNodeIds();
        int maxNodeId = disc.Max(n => n.NodeId > 0 ? n.NodeId : 0);
        if (maxNodeId < disc.Count)
            maxNodeId = disc.Count;
        var coords = new (double x, double y)[maxNodeId];
        for (int i = 0; i < disc.Count; i++)
        {
            var n = disc[i];
            int id = n.NodeId > 0 ? n.NodeId : i + 1;
            if (id < 1 || id > maxNodeId) continue;
            coords[id - 1] = (n.Location.X, n.Location.Y);
        }

        string meshType = "T3";
        var conn = new List<int[]>(_meshSource.MeshInformationRows.Count);
        foreach (var row in _meshSource.MeshInformationRows)
        {
            if (row.IsQ8) meshType = "Q8";
            else if (row.IsQ4) meshType = "Q4";
            else if (row.IsT6) meshType = "T6";

            var nodes = new List<int> { row.Node1, row.Node2, row.Node3 };
            if (row.Node4 > 0) nodes.Add(row.Node4);
            if (row.Node5 > 0) nodes.Add(row.Node5);
            if (row.Node6 > 0) nodes.Add(row.Node6);
            if (row.Node7 > 0) nodes.Add(row.Node7);
            if (row.Node8 > 0) nodes.Add(row.Node8);
            conn.Add(nodes.ToArray());
        }

        bool smithQ8 = ResolveSmithQ8BoundaryOrder(meshType);

        if (!MeshBoundaryGeometryExtractor.TryExtractOuterBoundary(
                coords, conn, meshType, smithQ8, out var boundary))
        {
            GeometryImportStatusMessage = "Could not find a closed outer boundary on the mesh.";
            return;
        }

        if (!MeshBoundaryGeometryExtractor.TryMergeCollinearBoundaryVertices(boundary, coords, out var importNodes)
            || importNodes.Count < 3)
        {
            GeometryImportStatusMessage = "Could not simplify mesh boundary (collinear merge).";
            return;
        }

        GeometryVertexRows.Clear();
        GeometryLineSegments.Clear();
        GeometryInternalEdges.Clear();
        GeometryInternalCircles.Clear();
        HasAddedElevationLayer = false;

        for (int i = 0; i < importNodes.Count; i++)
        {
            int nid = importNodes[i];
            if (nid < 1 || nid > coords.Length)
            {
                GeometryImportStatusMessage = $"Boundary node {nid} is out of range (mesh has {coords.Length} nodes).";
                return;
            }
            var (x, y) = coords[nid - 1];
            var display = new GeometryVertexDisplay
            {
                Index = i,
                AxisX = x,
                AxisY = y,
                StablePointId = nid,
            };
            display.Owner = this;
            GeometryVertexRows.Add(display);
        }

        if (importNodes.Count > 0)
            _nextStablePointId = Math.Max(_nextStablePointId, importNodes.Max() + 1);

        FitAxisBoundsToImportedVertices();
        RebuildGeometryAfterProjectLoad();
        LockImportedBoundaryAsSurfaceS1();
        RefreshMaterialLayerCanvasOverlays();
        GeometryImportStatusMessage =
            $"Imported {importNodes.Count} boundary point(s) and {GeometryLineSegments.Count} segment(s) from the mesh boundary ({boundary.Count} boundary nodes → {importNodes.Count} after collinear merge).";
        _projectSettings?.PullAxisFromGeometry();
    }

    /// <summary>Smith emb_2d perimeter vs serendipity edges for Q8 outer-boundary extraction.</summary>
    private bool ResolveSmithQ8BoundaryOrder(string meshType)
    {
        if (_meshSource == null || !string.Equals(meshType, "Q8", StringComparison.OrdinalIgnoreCase))
            return false;

        if (_meshSource.MeshInformationRows.Any(r => r.SmithEmb2dLocalOrder))
            return true;
        if (_meshSource.MeshElements.Any(e => e.Q8SmithEmb2dLocalOrder))
            return true;

        var q8Rows = _meshSource.MeshInformationRows.Where(r => r.IsQ8).ToList();
        if (q8Rows.Count == 0)
            return true;

        return !q8Rows.All(r => !r.SmithEmb2dLocalOrder);
    }

    private void FitAxisBoundsToImportedVertices()
    {
        if (!TryCollectContentAxisExtents(out var minX, out var maxX, out var minY, out var maxY))
            return;
        ApplyNiceFittedAxisBounds(minX, maxX, minY, maxY, refreshPath: true);
    }

    /// <summary>Fits axis limits to geometry + groundwater overlays (public for project load / view reset).</summary>
    public void FitAxisBoundsToVertexExtents() => FitAxisBoundsToImportedVertices();

    /// <summary>
    /// Fits axis limits to an explicit world-space bounding box (mesh fit, etc.), using nice outward rounding.
    /// </summary>
    public void FitAxisBoundsToWorldExtents(double minX, double maxX, double minY, double maxY)
    {
        ApplyNiceFittedAxisBounds(minX, maxX, minY, maxY, refreshPath: false);
    }

    /// <summary>
    /// Expands the current axis box if geometry or WT/ponded content lies outside it
    /// (does not shrink a user-zoomed/panned frame).
    /// </summary>
    public void EnsureAxisBoundsIncludeContent()
    {
        if (!TryCollectContentAxisExtents(out var minX, out var maxX, out var minY, out var maxY))
            return;

        const double edgeEps = 1e-6;
        if (minX >= AxisXMin - edgeEps
            && maxX <= AxisXMax + edgeEps
            && minY >= AxisYMin - edgeEps
            && maxY <= AxisYMax + edgeEps)
            return;

        var rangeX = Math.Max(maxX - minX, AxisXMax - AxisXMin);
        var rangeY = Math.Max(maxY - minY, AxisYMax - AxisYMin);
        var pad = 0.08 * Math.Max(Math.Max(rangeX, rangeY), 1.0);

        var x0 = Math.Min(AxisXMin, minX - pad);
        var x1 = Math.Max(AxisXMax, maxX + pad);
        var y0 = Math.Min(AxisYMin, minY - pad);
        var y1 = Math.Max(AxisYMax, maxY + pad);
        ApplyNiceAxisLimits(x0, x1, y0, y1, refreshPath: false);
    }

    private bool TryCollectContentAxisExtents(
        out double minX, out double maxX, out double minY, out double maxY)
    {
        minX = double.PositiveInfinity;
        maxX = double.NegativeInfinity;
        minY = double.PositiveInfinity;
        maxY = double.NegativeInfinity;
        var any = false;

        foreach (var v in GeometryVertexRows)
        {
            if (v.AxisX < minX) minX = v.AxisX;
            if (v.AxisX > maxX) maxX = v.AxisX;
            if (v.AxisY < minY) minY = v.AxisY;
            if (v.AxisY > maxY) maxY = v.AxisY;
            any = true;
        }

        if (_groundwaterVm?.IsEnabled == true && GroundwaterSurfPoints != null)
        {
            foreach (var p in GroundwaterSurfPoints)
            {
                if (p.AxisX < minX) minX = p.AxisX;
                if (p.AxisX > maxX) maxX = p.AxisX;
                if (p.AxisY < minY) minY = p.AxisY;
                if (p.AxisY > maxY) maxY = p.AxisY;
                any = true;
            }

            if (_groundwaterVm.IsPondedFreeWaterAllowed
                && _groundwaterVm.IsPondedTotalHeadMethod
                && double.IsFinite(_groundwaterVm.PondedTotalHead))
            {
                var h = _groundwaterVm.PondedTotalHead;
                if (h < minY) minY = h;
                if (h > maxY) maxY = h;
                any = true;
            }
        }

        return any
            && !double.IsInfinity(minX) && !double.IsInfinity(maxX)
            && !double.IsInfinity(minY) && !double.IsInfinity(maxY);
    }

    private void ApplyNiceFittedAxisBounds(
        double minX, double maxX, double minY, double maxY, bool refreshPath)
    {
        if (maxX < minX || maxY < minY) return;
        if (Math.Abs(maxX - minX) < 1e-12) { minX -= 0.5; maxX += 0.5; }
        if (Math.Abs(maxY - minY) < 1e-12) { minY -= 0.5; maxY += 0.5; }

        var rangeX = maxX - minX;
        var rangeY = maxY - minY;
        var pad = 0.08 * Math.Max(Math.Max(rangeX, rangeY), 1.0);
        ApplyNiceAxisLimits(minX - pad, maxX + pad, minY - pad, maxY + pad, refreshPath);
    }

    private void ApplyNiceAxisLimits(
        double x0, double x1, double y0, double y1, bool refreshPath)
    {
        if (x1 < x0 || y1 < y0) return;

        var xStep = NiceNumber((x1 - x0) / 8.0, round: true);
        var yStep = NiceNumber((y1 - y0) / 8.0, round: true);
        x0 = NiceFloor(x0, xStep);
        x1 = NiceCeil(x1, xStep);
        y0 = NiceFloor(y0, yStep);
        y1 = NiceCeil(y1, yStep);
        if (x1 <= x0) x1 = x0 + Math.Max(xStep, 1.0);
        if (y1 <= y0) y1 = y0 + Math.Max(yStep, 1.0);

        using (BeginGeometryBulkUpdate())
        {
            AxisXMin = x0;
            AxisXMax = x1;
            AxisYMin = y0;
            AxisYMax = y1;
        }

        OnPropertyChanged(nameof(AxisXMin));
        OnPropertyChanged(nameof(AxisXMax));
        OnPropertyChanged(nameof(AxisYMin));
        OnPropertyChanged(nameof(AxisYMax));
        UpdateAxisTicks();
        if (refreshPath)
            RefreshGeometryPath();
    }

    /// <summary>
    /// After mesh-boundary import or project load: fit the viewport and rebuild S1 from embankment macro corners
    /// when the locked surface does not span the full vertex bounding box.
    /// </summary>
    public void EnsureEmbankmentMacroSurfaceAndAxisFit()
    {
        FitAxisBoundsToVertexExtents();
        if (ShouldRebuildEmbankmentMacroSurface())
            TryAutoLockEmbankmentMacroSurface();
        PruneSpuriousInternalEdgesForDisplay();
    }

    /// <summary>
    /// Drops stale interior chords saved in older .xfem files (layer cuts, mesh-import artifacts)
    /// so the Geometry Definition canvas shows only the macro surface outline.
    /// </summary>
    public void PruneSpuriousInternalEdgesForDisplay()
    {
        if (GeometryVertexRows.Count < 3 || GeometryInternalEdges.Count == 0)
            return;

        var lockedCount = GeometrySurfaces.FirstOrDefault(s => s.IsPolygonLocked)?.LockedPolygonVertexLabels.Count ?? 0;
        var macroOnlyRing = lockedCount >= 3 && GeometryVertexRows.Count <= Math.Max(lockedCount, 12);

        for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
        {
            var ie = GeometryInternalEdges[i];
            if (ie.IsArc)
                continue;

            if (IsMaterialLayerCutInternalEdge(ie))
            {
                if (_materialLayerCutElevations.Count == 0)
                    GeometryInternalEdges.RemoveAt(i);
                continue;
            }

            if (IsConsecutiveBoundaryStablePair(ie.StableMin, ie.StableMax))
                continue;

            if (macroOnlyRing || GeometryVertexRows.Count <= 12)
                GeometryInternalEdges.RemoveAt(i);
        }

        RefreshInternalEdgeCanvasLines();
        RefreshGeometryLineSegments();
    }

    private bool IsMacroOnlyBoundaryRing()
    {
        var locked = GeometrySurfaces.FirstOrDefault(s => s.IsPolygonLocked)?.LockedPolygonVertexLabels.Count ?? 0;
        return locked >= 3 && GeometryVertexRows.Count == locked;
    }

    private bool ShouldRebuildEmbankmentMacroSurface()
    {
        if (GeometryVertexRows.Count < 12)
            return false;

        var (xMin, xMax, yMin, yMax) = GetBoundingBoxBounds();
        var xRange = xMax - xMin;
        var yRange = yMax - yMin;
        if (yRange <= AxisEqualityTolerance)
            return false;

        if (GeometrySurfaces.Count == 0)
            return TryResolveEmbankmentMacroCornerLabels(out _);

        var surface = GeometrySurfaces[0];
        if (!surface.IsPolygonLocked || surface.LockedPolygonVertexLabels.Count < 3)
            return TryResolveEmbankmentMacroCornerLabels(out _);

        if (!TryGetSurfacePolygonOrderedLabels(surface, out var labels) || labels.Count < 3)
            return true;

        double surfYMax = double.NegativeInfinity;
        double surfXMax = double.NegativeInfinity;
        foreach (var label in labels)
        {
            var v = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == label);
            if (v == null)
                return true;
            if (v.AxisY > surfYMax) surfYMax = v.AxisY;
            if (v.AxisX > surfXMax) surfXMax = v.AxisX;
        }

        if (surfYMax < yMax - yRange * 0.04)
            return true;
        if (xRange > AxisEqualityTolerance && surfXMax < xMax - xRange * 0.04)
            return true;

        return false;
    }

    private bool TryAutoLockEmbankmentMacroSurface()
    {
        if (!TryResolveEmbankmentMacroCornerLabels(out var labels) || labels.Count < 4)
            return false;

        var surface = GeometrySurfaces.FirstOrDefault();
        if (surface == null)
        {
            surface = new GeometrySurface(1);
            GeometrySurfaces.Add(surface);
        }

        _suppressSurfaceGeometryRotation = true;
        try
        {
            surface.IsPolygonLocked = false;
            surface.PickedLineNumbers.Clear();
            surface.ConnectedLineNumbers.Clear();
            surface.DraftPolygonVertexLabels.Clear();
            surface.LockedPolygonVertexLabels.Clear();
            foreach (var label in labels)
                surface.LockedPolygonVertexLabels.Add(label);
            surface.IsPolygonLocked = true;
        }
        finally
        {
            _suppressSurfaceGeometryRotation = false;
        }

        SelectedGeometrySurface = surface;
        RefreshSurfaceRegionOverlays();
        return true;
    }

    private bool TryResolveEmbankmentMacroCornerLabels(out List<string> labels)
    {
        labels = new List<string>();
        // Imported macro corners are already in boundary walk order (L1..Ln).
        if (GeometryVertexRows.Count is >= 4 and <= 12)
        {
            foreach (var v in GeometryVertexRows)
                labels.Add(v.PointLabel);
            return true;
        }

        int[] smithCornerIds = { 193, 173, 303, 293, 269, 259 };
        if (TryResolveCornerLabelsByStableIds(smithCornerIds, labels))
            return true;

        return TryResolveEmbankmentCornerLabelsByGeometry(out labels);
    }

    private bool TryResolveCornerLabelsByStableIds(IReadOnlyList<int> stableIds, List<string> labels)
    {
        labels.Clear();
        foreach (var id in stableIds)
        {
            var v = GeometryVertexRows.FirstOrDefault(r => r.StablePointId == id);
            if (v == null)
                return false;
            labels.Add(v.PointLabel);
        }

        return labels.Count >= 4;
    }

    /// <summary>6-corner embankment loop in boundary walk order (CCW from bottom-left).</summary>
    private bool TryResolveEmbankmentCornerLabelsByGeometry(out List<string> labels)
    {
        labels = new List<string>();
        if (GeometryVertexRows.Count < 4)
            return false;

        var verts = GeometryVertexRows.ToList();
        var (xMin, xMax, yMin, yMax) = GetBoundingBoxBounds();
        var eps = Math.Max(AxisEqualityTolerance, (xMax - xMin) * 1e-4);

        var leftCrest = verts.Where(v => Math.Abs(v.AxisX - xMin) <= eps).OrderByDescending(v => v.AxisY).FirstOrDefault();
        var leftToe = verts.Where(v => Math.Abs(v.AxisX - xMin) <= eps).OrderBy(v => v.AxisY).FirstOrDefault();
        var rightToe = verts.Where(v => Math.Abs(v.AxisX - xMax) <= eps).OrderBy(v => v.AxisY).FirstOrDefault();
        if (leftCrest == null || leftToe == null || rightToe == null)
            return false;

        var interiorY = verts
            .Select(v => v.AxisY)
            .Where(y => y > yMin + eps && y < yMax - eps)
            .GroupBy(y => Math.Round(y / eps))
            .OrderByDescending(g => g.Count())
            .Select(g => g.Average())
            .FirstOrDefault();

        GeometryVertexDisplay? rightBench = null;
        GeometryVertexDisplay? slopeBreak = null;
        GeometryVertexDisplay? crestBreak = null;
        if (interiorY > yMin + eps)
        {
            rightBench = verts
                .Where(v => Math.Abs(v.AxisX - xMax) <= eps && Math.Abs(v.AxisY - interiorY) <= eps * 2)
                .FirstOrDefault();
            slopeBreak = verts
                .Where(v => Math.Abs(v.AxisY - interiorY) <= eps * 2 && v.AxisX < xMax - eps && v.AxisX > xMin + eps)
                .OrderByDescending(v => v.AxisX)
                .FirstOrDefault();
        }

        crestBreak = verts
            .Where(v => Math.Abs(v.AxisY - yMax) <= eps * 2 && v.AxisX > xMin + eps && v.AxisX < xMax - eps)
            .OrderBy(v => v.AxisX)
            .FirstOrDefault();

        var candidateLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in new[] { leftToe, leftCrest, crestBreak, slopeBreak, rightBench, rightToe })
        {
            if (v == null) continue;
            candidateLabels.Add(v.PointLabel);
        }

        if (candidateLabels.Count < 4)
            return false;

        foreach (var v in GeometryVertexRows)
        {
            if (candidateLabels.Contains(v.PointLabel))
                labels.Add(v.PointLabel);
        }

        return labels.Count >= 4;
    }

    /// <summary>Resets stable point id counter when replacing geometry during import.</summary>
    public void ResetStablePointIdCounter() => _nextStablePointId = 1;

    /// <summary>Locks S1 to imported boundary vertices in walk order (matches L1..Ln).</summary>
    public void LockImportedBoundaryAsSurfaceS1Public() => LockImportedBoundaryAsSurfaceS1();

    public void RefreshSurfaceRegionOverlaysPublic() => RefreshSurfaceRegionOverlays();

    /// <summary>Locks S1 to imported boundary vertices in walk order (matches L1..Ln).</summary>
    private void LockImportedBoundaryAsSurfaceS1()
    {
        if (GeometryVertexRows.Count < 3)
            return;

        var surface = GeometrySurfaces.FirstOrDefault();
        if (surface == null)
        {
            surface = new GeometrySurface(1);
            GeometrySurfaces.Add(surface);
        }

        _suppressSurfaceGeometryRotation = true;
        try
        {
            surface.IsPolygonLocked = false;
            surface.PickedLineNumbers.Clear();
            surface.ConnectedLineNumbers.Clear();
            surface.DraftPolygonVertexLabels.Clear();
            surface.LockedPolygonVertexLabels.Clear();
            foreach (var v in GeometryVertexRows)
                surface.LockedPolygonVertexLabels.Add(v.PointLabel);
            surface.IsPolygonLocked = true;
        }
        finally
        {
            _suppressSurfaceGeometryRotation = false;
        }

        SelectedGeometrySurface = surface;
        RefreshSurfaceRegionOverlays();
    }

    private void RefreshInternalEdgeCanvasLines()
    {
        InternalEdgeCanvasLines.Clear();
        var hideLayerCutChords = UseLayerNamesOnMaterialCanvas && ShouldUseHorizontalMaterialLayerSummaries();
        foreach (var ie in GeometryInternalEdges)
        {
            if (ie.IsArc)
            {
                var vS = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableStart);
                var vM = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMid);
                var vE = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableEnd);
                if (vS == null || vM == null || vE == null) continue;
                var p1 = ConvertAxisToCanvas(new Point(vS.AxisX, vS.AxisY));
                var p2 = ConvertAxisToCanvas(new Point(vM.AxisX, vM.AxisY));
                var p3 = ConvertAxisToCanvas(new Point(vE.AxisX, vE.AxisY));
                var pts = SampleArcPoints(p1, p2, p3, 64);
                InternalEdgeCanvasLines.Add(new Models.InternalEdgeCanvasLine { Points = pts, IsArc = true });
            }
            else
            {
                if (hideLayerCutChords && IsMaterialLayerCutInternalEdge(ie))
                    continue;

                var va = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMin);
                var vb = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMax);
                if (va == null || vb == null) continue;
                var p1 = ConvertAxisToCanvas(new Point(va.AxisX, va.AxisY));
                var p2 = ConvertAxisToCanvas(new Point(vb.AxisX, vb.AxisY));
                var pts = new System.Windows.Media.PointCollection { p1, p2 };
                InternalEdgeCanvasLines.Add(new Models.InternalEdgeCanvasLine { Points = pts, IsArc = false });
            }
        }

        foreach (var c in GeometryInternalCircles)
        {
            var centerCanvas = ConvertAxisToCanvas(new Point(c.CenterAxisX, c.CenterAxisY));
            var rimCanvas = ConvertAxisToCanvas(new Point(c.CenterAxisX + c.RadiusAxis, c.CenterAxisY));
            var rdx = rimCanvas.X - centerCanvas.X;
            var rdy = rimCanvas.Y - centerCanvas.Y;
            var rCanvas = Math.Sqrt(rdx * rdx + rdy * rdy);
            var pts = new PointCollection();
            for (var i = 0; i <= 64; i++)
            {
                var ang = i * 2.0 * Math.PI / 64;
                pts.Add(new Point(centerCanvas.X + rCanvas * Math.Cos(ang), centerCanvas.Y + rCanvas * Math.Sin(ang)));
            }

            InternalEdgeCanvasLines.Add(new Models.InternalEdgeCanvasLine { Points = pts, IsArc = false });
        }

        RefreshHorizontalLayerCanvasLines();
    }

    private double ComputeArcRadiusAxis(GeometryVertexDisplay vStart, GeometryVertexDisplay vMid, GeometryVertexDisplay vEnd)
    {
        var p1 = new Point(vStart.AxisX, vStart.AxisY);
        var p2 = new Point(vMid.AxisX, vMid.AxisY);
        var p3 = new Point(vEnd.AxisX, vEnd.AxisY);
        if (!TryCircleFromThreePoints(p1, p2, p3, out _, out _, out var r)) return 0;
        return Math.Round(r, 4);
    }

    private void AddLayerLine()
    {
        if (IsNonHorizontalLayerMode)
        {
            FinishNonHorizontalLayerDraw();
            return;
        }

        if (!TryResolveHorizontalCutElevation(out var elevation, out var error))
        {
            LayeringControlsStatus = error ?? "Enter an elevation above the base (in project units).";
            return;
        }

        AddFullWidthHorizontalLayerCut(elevation);
    }

    /// <summary>
    /// Inserts a full-width horizontal material interface at <paramref name="elevation"/>.
    /// </summary>
    private void AddFullWidthHorizontalLayerCut(double elevation)
    {
        if (!TryInsertHorizontalLayerCutVertices(elevation))
        {
            LayeringControlsStatus = "Elevation must lie inside the slope polygon (above base, below crest).";
            LayerElevationInput = "";
            return;
        }

        _hasAddedElevationLayer = true;
        if (!_materialLayerCutElevations.Any(e => Math.Abs(e - elevation) < AxisEqualityTolerance))
            _materialLayerCutElevations.Add(elevation);
        _hasAddedElevationLayer = _materialLayerCutElevations.Count > 0;
        LayerElevationInput = "";
        LayeringControlsStatus = $"Horizontal layer added at elevation Y = {elevation:G6}.";
        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments();
    }

    /// <summary>Finalizes a layer cut as a polyline through <see cref="PendingLayerPoints"/> (≥ 2).</summary>
    private void AddLayerCutPolylineFromPendingPoints()
    {
        if (PendingLayerPoints.Count < 2)
            return;

        var cut = PendingLayerPoints.Select(p => (p.AxisX, p.AxisY)).ToList();
        _nonHorizontalLayerCuts.Add(cut);
        ClearPendingLayerPointsInternal();
        IsNonHorizontalLayerDrawMode = false;
        NonHorizontalLayerDrawStatus = "";
        LayeringControlsStatus = $"Layer cut added through {cut.Count} point(s).";
        RefreshNonHorizontalLayerPreview();
        RefreshNonHorizontalLayerCanvasLines();
        RefreshLayerCoordinateSummaries();
        RefreshSurfaceRegionOverlays();
        BumpLayerMaterialVisualRevision();
    }

    private bool CanAddLayerPointFromInput() =>
        IsNonHorizontalLayerMode && TryParseLayerPointInputs(out _);

    private void AddLayerPointFromInput()
    {
        if (!IsNonHorizontalLayerMode)
            return;

        if (!TryParseLayerPointInputs(out var axis))
        {
            LayeringControlsStatus = "Enter valid X and Y coordinates.";
            return;
        }

        AddPendingLayerPointAxis(axis);
        LayerPointXInput = "";
        LayerPointYInput = "";
    }

    private bool TryParseLayerPointInputs(out Point axis)
    {
        axis = default;
        if (!double.TryParse(LayerPointXInput, NumberStyles.Float, CultureInfo.CurrentCulture, out var x))
            return false;
        if (!double.TryParse(LayerPointYInput, NumberStyles.Float, CultureInfo.CurrentCulture, out var y))
            return false;
        axis = new Point(x, y);
        return true;
    }

    private bool TryResolveHorizontalCutElevation(out double elevation, out string? error)
    {
        elevation = 0;
        error = null;

        if (double.TryParse(LayerElevationInput, NumberStyles.Float, CultureInfo.CurrentCulture, out elevation))
            return true;

        error = "Enter an elevation above the base (in project units).";
        return false;
    }

    private void ClearPendingLayerPoints()
    {
        ClearPendingLayerPointsInternal();
        LayeringControlsStatus = "";
    }

    private void ClearPendingLayerPointsInternal()
    {
        PendingLayerPoints.Clear();
        SelectedPendingLayerPoint = null;
        RefreshNonHorizontalLayerPreview();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RemovePendingLayerPoint(LayerPendingPointDisplay? point)
    {
        if (point == null || !PendingLayerPoints.Remove(point))
            return;

        for (var i = 0; i < PendingLayerPoints.Count; i++)
            PendingLayerPoints[i].PointNumber = i + 1;

        SelectedPendingLayerPoint = null;
        if (PendingLayerPoints.Count == 0 && IsNonHorizontalLayerDrawMode)
            IsNonHorizontalLayerDrawMode = false;

        LayeringControlsStatus = PendingLayerPoints.Count == 0
            ? ""
            : $"{PendingLayerPoints.Count} point(s) queued — click Add Layer when ready.";
        RefreshNonHorizontalLayerPreview();
        CommandManager.InvalidateRequerySuggested();
    }

    private bool CanRemoveSelectedLayer(LayerCoordinateSummary? summary) =>
        summary != null && TryResolveLayerRemoveTarget(summary, out _, out _);

    private void RemoveSelectedLayer(LayerCoordinateSummary? summary)
    {
        if (summary == null || !TryResolveLayerRemoveTarget(summary, out var horizontalCutElevation, out var nonHorizontalCut))
            return;

        RecordUndo("Delete material layer");
        var deletedDisplayNumber = summary.DisplayLayerNumber;
        var deletedWasHighlighted = _materialLayerStripHighlightDisplayNumber == deletedDisplayNumber;

        if (horizontalCutElevation is double cutY)
        {
            var eps = AxisEqualityTolerance;
            _materialLayerCutElevations.RemoveAll(c => Math.Abs(c - cutY) < eps);
            _hasAddedElevationLayer = _materialLayerCutElevations.Count > 0;
            ShiftLayerMetadataAfterDelete(deletedDisplayNumber);
            if (deletedWasHighlighted)
                SetMaterialLayerStripHighlight(null);
            RemoveMaterialLayerCutVerticesNearElevation(cutY);
            RefreshGeometryPath();
            RefreshAvailablePointLabels();
            RefreshGeometryLineSegments(resyncLayerCutChordEdges: true);
            BumpLayerMaterialVisualRevision();
            return;
        }

        if (nonHorizontalCut != null)
        {
            _nonHorizontalLayerCuts.Remove(nonHorizontalCut);
            ShiftLayerMetadataAfterDelete(deletedDisplayNumber);
            if (deletedWasHighlighted)
                SetMaterialLayerStripHighlight(null);
            ClearPendingLayerPointsInternal();
            RefreshNonHorizontalLayerCanvasLines();
            RefreshLayerCoordinateSummaries();
            RefreshSurfaceRegionOverlays();
            RefreshInternalEdgeCanvasLines();
            BumpLayerMaterialVisualRevision();
        }
    }

    private bool TryResolveLayerRemoveTarget(
        LayerCoordinateSummary summary,
        out double? horizontalCutElevation,
        out List<(double X, double Y)>? nonHorizontalCut)
    {
        horizontalCutElevation = null;
        nonHorizontalCut = null;
        if (summary == null)
            return false;

        var displayNumber = summary.DisplayLayerNumber;
        var horizontalRowCount = CountHorizontalLayerRows();

        if (horizontalRowCount > 0 && displayNumber <= horizontalRowCount)
        {
            if (!TryGetHorizontalStripElevationRange(displayNumber, out var yLo, out var yHi))
                return false;

            var eps = AxisEqualityTolerance;
            if (TryPickHorizontalCutElevation(yHi, eps, out var cutAtTop))
            {
                horizontalCutElevation = cutAtTop;
                return true;
            }

            if (TryPickHorizontalCutElevation(yLo, eps, out var cutAtBottom))
            {
                horizontalCutElevation = cutAtBottom;
                return true;
            }

            return false;
        }

        if (displayNumber > horizontalRowCount)
            return TryGetNonHorizontalCutToRemove(displayNumber, horizontalRowCount, out nonHorizontalCut);

        return false;
    }

    private bool TryPickHorizontalCutElevation(double y, double eps, out double cutElevation)
    {
        cutElevation = 0;
        foreach (var c in _materialLayerCutElevations)
        {
            if (Math.Abs(c - y) > eps)
                continue;
            cutElevation = c;
            return true;
        }

        return false;
    }

    private int CountHorizontalLayerRows()
    {
        if (!ShouldUseHorizontalMaterialLayerSummaries())
            return 0;
        return TryBuildHorizontalMaterialLayerSummaries(out var rows) ? rows.Count : 0;
    }

    private void ShiftLayerMetadataAfterDelete(int deletedDisplayNumber)
    {
        ShiftLayerMetadataDictionary(_layerMaterialAssignmentByDisplayNumber, deletedDisplayNumber);
        ShiftLayerMetadataDictionary(_layerDisplayNameByDisplayNumber, deletedDisplayNumber);
        ShiftLayerMetadataDictionary(_layerDisplayColorArgb, deletedDisplayNumber);
        BumpLayerMaterialVisualRevision();
    }

    private static void ShiftLayerMetadataDictionary<T>(Dictionary<int, T> map, int deletedDisplayNumber)
    {
        map.Remove(deletedDisplayNumber);
        var keysToShift = map.Keys.Where(k => k > deletedDisplayNumber).OrderBy(k => k).ToList();
        foreach (var key in keysToShift)
        {
            var value = map[key];
            map.Remove(key);
            map[key - 1] = value;
        }
    }

    private void RefreshGeometryLineSegments(bool resyncLayerCutChordEdges = false)
    {
        var savedMaterials = BuildEdgeMaterialMapByStablePair();
        var lineToStablePair = BuildLineNumberToStablePairMap(this);

        // Always sync when forced: otherwise deleting the last horizontal layer leaves its dashed
        // internal chord on the Material Properties canvas (_materialLayerCutElevations is already empty).
        if (resyncLayerCutChordEdges || _materialLayerCutElevations.Count > 0)
            SyncMaterialLayerCutInternalEdges(resyncLayerCutChordEdges);

        // Drop Add Layer cut vertices when no cuts remain (before rebuilding L1..Ln from the ring).
        if (resyncLayerCutChordEdges && _materialLayerCutElevations.Count == 0)
            RemoveOrphanedMaterialLayerCutVertices();

        GeometryLineSegments.Clear();
        _boundarySegmentCount = 0;

        var count = GeometryVertexRows.Count;
        var autoCloseBoundary = ShouldAutoCloseBoundary(count);
        if (autoCloseBoundary)
        {
            int segmentCount = count;
            for (int i = 0; i < segmentCount; i++)
            {
                int j = (i + 1) % count;
                var vStart = GeometryVertexRows[i];
                var vEnd = GeometryVertexRows[j];
                var seg = new GeometryLineSegment(GeometryLineSegments.Count + 1, vStart.PointLabel, vEnd.PointLabel, isBoundaryEdge: true);
                GeometryLineSegments.Add(seg);
            }
            _boundarySegmentCount = segmentCount;
        }

        if (count < 2)
            GeometryInternalEdges.Clear();
        else
        {
            // Remove internal edges/arcs whose vertices have been deleted
            for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
            {
                var ie = GeometryInternalEdges[i];
                var hasA = GeometryVertexRows.Any(v => v.StablePointId == ie.StableMin);
                var hasB = GeometryVertexRows.Any(v => v.StablePointId == ie.StableMax);
                var hasMid = !ie.IsArc || GeometryVertexRows.Any(v => v.StablePointId == ie.StableMid);
                if (!hasA || !hasB || !hasMid) GeometryInternalEdges.RemoveAt(i);
            }

            foreach (var ie in GeometryInternalEdges)
            {
                if (ie.IsArc)
                {
                    var vStart = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableStart);
                    var vMid   = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMid);
                    var vEnd   = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableEnd);
                    if (vStart == null || vMid == null || vEnd == null) continue;
                    var seg = new GeometryLineSegment(GeometryLineSegments.Count + 1, vStart.PointLabel, vEnd.PointLabel, isBoundaryEdge: false)
                    {
                        IsArc = true,
                        ThroughPoint = vMid.PointLabel,
                        ArcRadius = ComputeArcRadiusAxis(vStart, vMid, vEnd)
                    };
                    GeometryLineSegments.Add(seg);
                }
                else
                {
                    var va = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMin);
                    var vb = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMax);
                    if (va == null || vb == null) continue;
                    var seg = new GeometryLineSegment(GeometryLineSegments.Count + 1, va.PointLabel, vb.PointLabel, isBoundaryEdge: false);
                    GeometryLineSegments.Add(seg);
                }
            }

            ApplyEdgeMaterialsFromStablePairMap(savedMaterials);
            var pairToNewLine = BuildStablePairToLineNumberMap(this);
            RemapGeometrySurfaceLineNumbersFromStablePairs(lineToStablePair, pairToNewLine);
        }

        if (count < 2)
            RemapGeometrySurfaceLineNumbersFromStablePairs(lineToStablePair, new Dictionary<(int Min, int Max), int>());

        foreach (var c in GeometryInternalCircles)
        {
            var seg = new GeometryLineSegment(GeometryLineSegments.Count + 1, "—", "—", false);
            seg.IsCircle = true;
            seg.InternalCircleId = c.Id;
            seg.SetCircleRowDisplay(c.CenterAxisX, c.CenterAxisY, c.RadiusAxis);
            GeometryLineSegments.Add(seg);
        }

        RefreshAvailableLineNumbers();
        RefreshLayerCoordinateSummaries();
        RefreshSurfaceRegionOverlays();
        PruneDuplicateBoundaryInternalEdges();
        RefreshInternalEdgeCanvasLines();
        RefreshHighlightedSegmentPoints();
    }

    private void OnGeometrySurfacesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (GeometrySurface s in e.NewItems)
                s.PropertyChanged += OnGeometrySurfacePropertyChanged;
        }
        if (e.OldItems != null)
        {
            foreach (GeometrySurface s in e.OldItems)
            {
                s.PropertyChanged -= OnGeometrySurfacePropertyChanged;
                if (_selectedGeometrySurface == s)
                    SelectedGeometrySurface = null;
            }
        }

        RefreshSurfaceRegionOverlays();
        OnPropertyChanged(nameof(CanDeleteSelectedSurface));
    }

    private void OnGeometrySurfacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null ||
            e.PropertyName == nameof(GeometrySurface.PickedLineNumbers) ||
            e.PropertyName == nameof(GeometrySurface.Tag) ||
            e.PropertyName == nameof(GeometrySurface.ColorIndex) ||
            e.PropertyName == nameof(GeometrySurface.IsPolygonLocked))
        {
            RefreshSurfaceRegionOverlays();
            if (e.PropertyName == nameof(GeometrySurface.IsPolygonLocked))
                OnPropertyChanged(nameof(IsSurfacePolygonPickMode));
        }

        if (!_suppressSurfaceGeometryRotation &&
            sender is GeometrySurface surface &&
            e.PropertyName == nameof(GeometrySurface.PickedLineNumbers))
            TryRotateGeometryToMatchSurfaceStart(surface);
    }

    /// <summary>Rebuilds canvas path, point labels, and line segments after bulk-loading vertices (e.g. .xfem project file).</summary>
    public void RebuildGeometryAfterProjectLoad()
    {
        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments();
    }

    /// <summary>
    /// Removes internal edges that duplicate a consecutive boundary walk segment.
    /// </summary>
    public void PruneDuplicateBoundaryInternalEdges()
    {
        for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
        {
            var ie = GeometryInternalEdges[i];
            if (ie.IsArc) continue;
            if (IsConsecutiveBoundaryStablePair(ie.StableMin, ie.StableMax))
                GeometryInternalEdges.RemoveAt(i);
        }
    }

    /// <summary>
    /// Drops internal edges that were incorrectly promoted from saved boundary segments after bulk delete.
    /// </summary>
    public void RemoveStaleInternalEdgesFromSavedBoundarySegments(
        IEnumerable<(string FromPoint, string ToPoint, bool? IsBoundaryEdge)> savedSegments)
    {
        foreach (var (fromPoint, toPoint, isBoundaryEdge) in savedSegments)
        {
            if (isBoundaryEdge == false)
                continue;

            var vFrom = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == fromPoint);
            var vTo = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == toPoint);
            if (vFrom == null || vTo == null)
                continue;

            if (IsConsecutiveBoundaryStablePair(vFrom.StablePointId, vTo.StablePointId))
                continue;

            var min = Math.Min(vFrom.StablePointId, vTo.StablePointId);
            var max = Math.Max(vFrom.StablePointId, vTo.StablePointId);
            for (var i = GeometryInternalEdges.Count - 1; i >= 0; i--)
            {
                var ie = GeometryInternalEdges[i];
                if (!ie.IsArc && ie.StableMin == min && ie.StableMax == max)
                    GeometryInternalEdges.RemoveAt(i);
            }
        }
    }

    private bool IsConsecutiveBoundaryStablePair(int stableA, int stableB)
    {
        var count = GeometryVertexRows.Count;
        if (!ShouldAutoCloseBoundary(count))
            return false;

        for (var i = 0; i < count; i++)
        {
            var j = (i + 1) % count;
            var sa = GeometryVertexRows[i].StablePointId;
            var sb = GeometryVertexRows[j].StablePointId;
            if ((sa == stableA && sb == stableB) || (sa == stableB && sb == stableA))
                return true;
        }

        return false;
    }

    /// <summary>Material Properties Layering Controls: one row per horizontal strip between distinct vertex elevations (P1–P8 corners).</summary>
    private bool TryBuildHorizontalMaterialLayerSummaries(out List<LayerCoordinateSummary> rows)
    {
        rows = new List<LayerCoordinateSummary>();
        var n = GeometryVertexRows.Count;
        if (n < 3) return false;

        var eps = Math.Max(AxisEqualityTolerance, (AxisYMax - AxisYMin) * 1e-9);
        var poly = new List<(double x, double y, string label)>(n);
        for (var i = 0; i < n; i++)
        {
            var v = GeometryVertexRows[i];
            poly.Add((v.AxisX, v.AxisY, v.PointLabel));
        }

        if (!TryCollectHorizontalBandElevations(out var merged) || merged.Count < 2)
            return false;

        const int maxLayers = 32;
        var layerDisplay = 0;
        for (var i = 0; i < merged.Count - 1 && layerDisplay < maxLayers; i++)
        {
            var yLo = merged[i];
            var yHi = merged[i + 1];
            if (yHi - yLo < eps) continue;

            var clipped = ClipPolygonToHorizontalStrip(poly, yLo, yHi, eps);
            if (clipped == null || clipped.Count < 3) continue;

            SnapStripPointsToNearestVertices(clipped, eps);
            var labels = DedupeConsecutivePolygonLabels(clipped);
            if (labels.Count < 3) continue;

            layerDisplay++;
            var arr = new string[8];
            for (var j = 0; j < 8; j++)
                arr[j] = j < labels.Count ? labels[j] : "";

            var rep = PickRepresentativeSegmentForLayer(yLo, yHi, eps);
            var repLine = rep?.LineNumber ?? layerDisplay;
            rows.Add(new LayerCoordinateSummary(layerDisplay, repLine, arr, rep, this, this));
        }

        return rows.Count > 0;
    }

    private static List<(double x, double y, string label)>? ClipPolygonToHorizontalStrip(
        IReadOnlyList<(double x, double y, string label)> poly,
        double yLo,
        double yHi,
        double eps)
    {
        var a = ClipPolygonYHalfPlane(poly.ToList(), yLo, keepAbove: true, eps);
        if (a.Count < 3) return null;
        var b = ClipPolygonYHalfPlane(a, yHi, keepAbove: false, eps);
        return b.Count >= 3 ? b : null;
    }

    private static List<(double x, double y, string label)> ClipPolygonYHalfPlane(
        List<(double x, double y, string label)> input,
        double yLine,
        bool keepAbove,
        double eps)
    {
        var output = new List<(double x, double y, string label)>();
        if (input.Count == 0) return output;

        bool Inside((double x, double y, string label) p) =>
            keepAbove ? p.y >= yLine - eps : p.y <= yLine + eps;

        (double x, double y, string label) Intersect(
            (double x, double y, string label) a,
            (double x, double y, string label) b)
        {
            var dy = b.y - a.y;
            if (Math.Abs(dy) < 1e-14)
                return ((a.x + b.x) * 0.5, yLine, string.IsNullOrEmpty(a.label) ? b.label : a.label);
            var t = (yLine - a.y) / dy;
            var x = a.x + t * (b.x - a.x);
            return (x, yLine, string.IsNullOrEmpty(a.label) ? b.label : a.label);
        }

        for (var i = 0; i < input.Count; i++)
        {
            var s = input[i];
            var e = input[(i + 1) % input.Count];
            var sIn = Inside(s);
            var eIn = Inside(e);
            if (sIn && eIn)
                output.Add(e);
            else if (sIn && !eIn)
                output.Add(Intersect(s, e));
            else if (!sIn && eIn)
            {
                output.Add(Intersect(s, e));
                output.Add(e);
            }
        }

        return output;
    }

    private void SnapStripPointsToNearestVertices(List<(double x, double y, string label)> clipped, double eps)
    {
        var snapTol = Math.Max(eps * 100, ComputeAxisHitTolerance() * 2);
        var snapTol2 = snapTol * snapTol;
        for (var i = 0; i < clipped.Count; i++)
        {
            var (x, y, _) = clipped[i];
            double best = snapTol2;
            string? bestLabel = null;
            foreach (var v in GeometryVertexRows)
            {
                var dx = x - v.AxisX;
                var dy = y - v.AxisY;
                var d2 = dx * dx + dy * dy;
                if (d2 < best)
                {
                    best = d2;
                    bestLabel = v.PointLabel;
                }
            }

            if (bestLabel != null && best <= snapTol2)
                clipped[i] = (x, y, bestLabel);
        }
    }

    private static List<string> DedupeConsecutivePolygonLabels(IReadOnlyList<(double x, double y, string label)> clipped)
    {
        var labels = new List<string>();
        foreach (var p in clipped)
        {
            var lab = string.IsNullOrWhiteSpace(p.label) ? "" : p.label.Trim();
            if (labels.Count > 0 && lab == labels[^1]) continue;
            labels.Add(lab);
        }

        if (labels.Count >= 3 && labels[0] == labels[^1])
            labels.RemoveAt(labels.Count - 1);
        return labels;
    }

    private GeometryLineSegment? PickRepresentativeSegmentForLayer(double yLo, double yHi, double eps)
    {
        GeometryLineSegment? best = null;
        double bestScore = double.MaxValue;
        foreach (var seg in GeometryLineSegments)
        {
            if (seg.IsCircle) continue;
            var va = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.FromPoint);
            var vb = GeometryVertexRows.FirstOrDefault(v => v.PointLabel == seg.ToPoint);
            if (va == null || vb == null) continue;
            var my = (va.AxisY + vb.AxisY) * 0.5;
            if (my < yLo - eps || my > yHi + eps) continue;
            var midY = (yLo + yHi) * 0.5;
            var score = Math.Abs(my - midY);
            if (score < bestScore)
            {
                bestScore = score;
                best = seg;
            }
        }

        return best ?? GeometryLineSegments.FirstOrDefault(s => !s.IsCircle);
    }

    /// <summary>
    /// Horizontal strip rows (P1–P8) are only for explicit "Add layer" cuts — not for every band between
    /// arbitrary vertex elevations (which would split a simple quad into spurious triangle "layers").
    /// </summary>
    private bool ShouldUseHorizontalMaterialLayerSummaries() =>
        _materialLayerCutElevations.Count > 0;

    private int _refreshLayerCoordinateSummariesDepth;

    private void RefreshLayerCoordinateSummaries()
    {
        if (_refreshLayerCoordinateSummariesDepth > 0)
            return;

        _refreshLayerCoordinateSummariesDepth++;
        try
        {
            LayerCoordinateSummaries.Clear();
            if (GeometryVertexRows.Count < 3)
            {
                SelectedLayerSummary = null;
                OnPropertyChanged(nameof(LayerCoordinateSummaries));
                HorizontalLayerCanvasLines.Clear();
                NonHorizontalLayerCanvasLines.Clear();
                return;
            }

            if (ShouldUseHorizontalMaterialLayerSummaries()
                && TryBuildHorizontalMaterialLayerSummaries(out var horizontal) && horizontal.Count > 0)
            {
                foreach (var r in horizontal)
                    LayerCoordinateSummaries.Add(r);
            }
            else
            {
                // Grid empty but a previous Add Layer chord / cut vertices may remain on the canvas.
                RemoveOrphanedFullWidthHorizontalLayerCutEdges();
                if (RemoveOrphanedMaterialLayerCutVertices())
                {
                    RefreshGeometryPath();
                    RefreshAvailablePointLabels();
                    RefreshGeometryLineSegments(resyncLayerCutChordEdges: true);
                    return;
                }
            }

            if (ShouldUseNonHorizontalLayerSummaries()
                && TryBuildNonHorizontalLayerSummaries(LayerCoordinateSummaries.Count, out var nonHoriz) && nonHoriz.Count > 0)
            {
                foreach (var r in nonHoriz)
                    LayerCoordinateSummaries.Add(r);
            }

            if (LayerCoordinateSummaries.Count == 0)
                SetMaterialLayerStripHighlight(null);

            OnPropertyChanged(nameof(LayerCoordinateSummaries));
            RefreshHorizontalLayerCanvasLines();
            RefreshNonHorizontalLayerCanvasLines();
        }
        finally
        {
            _refreshLayerCoordinateSummariesDepth--;
        }
    }

    /// <summary>
    /// Rebuilds <see cref="HorizontalLayerCanvasLines"/> — one orange chord per <see cref="_materialLayerCutElevations"/>
    /// entry (Add Layer), spanning the widest polygon intersection at that Y.
    /// Skipped when <see cref="UseLayerNamesOnMaterialCanvas"/> already shows filled material strips.
    /// </summary>
    private void RefreshHorizontalLayerCanvasLines()
    {
        HorizontalLayerCanvasLines.Clear();
        if (_materialLayerCutElevations.Count == 0)
            return;

        if (UseLayerNamesOnMaterialCanvas && ShouldUseHorizontalMaterialLayerSummaries())
            return;

        var n = GeometryVertexRows.Count;
        if (n < 3) return;

        var eps = AxisEqualityTolerance;

        var poly = GeometryVertexRows.Select(v => (x: v.AxisX, y: v.AxisY)).ToList();

        // Orange lines are only for explicit "Add layer" elevations — not every interior vertex Y
        // (otherwise a plain quad with two different bottom Ys would show chords automatically).
        var cutYs = BuildMaterialLayerCutYsForCanvas(poly, eps);
        if (cutYs.Count == 0) return;

        foreach (var cutY in cutYs)
        {
            if (!TryGetWidestHorizontalCutSpan(poly, n, cutY, eps, out var xLo, out var xHi))
                continue;

            var p1 = ConvertAxisToCanvas(new Point(xLo, cutY));
            var p2 = ConvertAxisToCanvas(new Point(xHi, cutY));
            HorizontalLayerCanvasLines.Add(new InternalEdgeCanvasLine
            {
                Points = new System.Windows.Media.PointCollection { p1, p2 },
                IsArc = false
            });
        }
    }

    /// <summary>True when <paramref name="ie"/> is a horizontal chord at an Add Layer cut elevation.</summary>
    private bool IsMaterialLayerCutInternalEdge(GeometryInternalEdge ie)
    {
        if (ie.IsArc)
            return false;

        var va = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMin);
        var vb = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == ie.StableMax);
        if (va == null || vb == null)
            return false;

        var eps = AxisEqualityTolerance;
        if (Math.Abs(va.AxisY - vb.AxisY) > eps)
            return false;

        return _materialLayerCutElevations.Any(c => Math.Abs(va.AxisY - c) <= eps);
    }

    /// <summary>
    /// Leftmost/rightmost polygon intersection at a horizontal cut — avoids spurious short chord stubs
    /// when pairing every two X hits on sloped boundaries.
    /// </summary>
    private static bool TryGetWidestHorizontalCutSpan(
        IReadOnlyList<(double x, double y)> poly,
        int n,
        double cutY,
        double eps,
        out double xLo,
        out double xHi)
    {
        xLo = xHi = 0;
        var xs = CollectXsForHorizontalPolygonCut(poly, n, cutY, eps);
        if (xs.Count < 2)
            return false;

        xLo = xs[0];
        xHi = xs[^1];
        return xHi - xLo > eps;
    }

    /// <summary>
    /// Y levels at which to draw horizontal layer chords on the canvas: explicit "Add layer" elevations,
    /// or (legacy) interior vertex Y when the project had layers but no saved elevation list.
    /// </summary>
    private List<double> BuildMaterialLayerCutYsForCanvas(IReadOnlyList<(double x, double y)> poly, double eps)
    {
        if (_materialLayerCutElevations.Count == 0)
            return new List<double>();

        var ys = poly.Select(p => p.y).OrderBy(y => y).ToList();
        if (ys.Count == 0) return new List<double>();
        var yMin = ys[0];
        var yMax = ys[^1];
        var merged = new List<double>();
        foreach (var cutY in _materialLayerCutElevations.OrderBy(y => y))
        {
            if (cutY <= yMin + eps || cutY >= yMax - eps) continue;
            if (merged.Count > 0 && Math.Abs(cutY - merged[^1]) < eps) continue;
            merged.Add(cutY);
        }

        return merged;
    }

    /// <summary>
    /// Sorted, merged X coordinates where the horizontal line y = cutY meets the closed polygon boundary
    /// (vertices on the line, horizontal edges along the line, and strict interior crossings).
    /// </summary>
    private static List<double> CollectXsForHorizontalPolygonCut(
        IReadOnlyList<(double x, double y)> poly,
        int n,
        double cutY,
        double eps)
    {
        var raw = new List<double>();
        for (var i = 0; i < n; i++)
        {
            var (x1, y1) = poly[i];
            var (x2, y2) = poly[(i + 1) % n];
            var on1 = Math.Abs(y1 - cutY) < eps;
            var on2 = Math.Abs(y2 - cutY) < eps;
            var horizontalOnCut = Math.Abs(y1 - y2) < eps && on1 && on2;
            if (horizontalOnCut)
            {
                raw.Add(x1);
                raw.Add(x2);
                continue;
            }

            if (on1 ^ on2)
            {
                raw.Add(on1 ? x1 : x2);
                continue;
            }

            if ((y1 < cutY && cutY < y2) || (y2 < cutY && cutY < y1))
            {
                var t = (cutY - y1) / (y2 - y1);
                raw.Add(x1 + t * (x2 - x1));
            }
        }

        raw.Sort();
        var merged = new List<double>();
        foreach (var x in raw)
        {
            if (merged.Count == 0 || Math.Abs(x - merged[^1]) > eps)
                merged.Add(x);
        }

        return merged;
    }

    /// <summary>
    /// Returns n+1 points along the current path at equal path-length intervals, without modifying geometry.
    /// Used by 1D Mesh subtab to build mesh in MeshViewModel.
    /// </summary>
    public IReadOnlyList<(double X, double Y)> GetPointsAlongPath(double length, int n)
    {
        var result = new List<(double, double)>();
        var count = GeometryVertexRows.Count;
        if (count < 2 || n < 1 || length <= 0) return result;

        var pathX = new List<double>(count);
        var pathY = new List<double>(count);
        for (int i = 0; i < count; i++)
        {
            pathX.Add(GeometryVertexRows[i].AxisX);
            pathY.Add(GeometryVertexRows[i].AxisY);
        }
        var cumDist = new List<double>(count) { 0 };
        for (int i = 0; i < count - 1; i++)
        {
            double dx = pathX[i + 1] - pathX[i];
            double dy = pathY[i + 1] - pathY[i];
            cumDist.Add(cumDist[i] + Math.Sqrt(dx * dx + dy * dy));
        }

        for (int i = 0; i <= n; i++)
        {
            double s = (i * length) / n;
            if (s >= length) s = length;
            double x, y;
            if (s <= 0) { x = pathX[0]; y = pathY[0]; }
            else if (s >= cumDist[count - 1]) { x = pathX[count - 1]; y = pathY[count - 1]; }
            else
            {
                int seg = 0;
                while (seg < count - 1 && cumDist[seg + 1] < s) seg++;
                double s0 = cumDist[seg]; double s1 = cumDist[seg + 1];
                double t = (s1 > s0) ? (s - s0) / (s1 - s0) : 0;
                x = pathX[seg] + t * (pathX[seg + 1] - pathX[seg]);
                y = pathY[seg] + t * (pathY[seg + 1] - pathY[seg]);
            }
            result.Add((x, y));
        }
        return result;
    }

    /// <summary>
    /// Returns the total path length of the current vertex chain (sum of segment lengths between consecutive vertices).
    /// Used by 1D Mesh subtab to show length from geometry.
    /// </summary>
    public double GetTotalPathLength()
    {
        var count = GeometryVertexRows.Count;
        if (count < 2) return 0;
        double length = 0;
        for (int i = 0; i < count - 1; i++)
        {
            var v1 = GeometryVertexRows[i];
            var v2 = GeometryVertexRows[i + 1];
            double dx = v2.AxisX - v1.AxisX;
            double dy = v2.AxisY - v1.AxisY;
            length += Math.Sqrt(dx * dx + dy * dy);
        }
        return length;
    }

    /// <summary>Axis-aligned bounding box of all vertices (axis coordinates).</summary>
    public (double MinX, double MaxX, double MinY, double MaxY) GetBoundingBoxBounds()
    {
        if (GeometryVertexRows.Count == 0) return (0, 0, 0, 0);
        double minX = GeometryVertexRows.Min(v => v.AxisX);
        double maxX = GeometryVertexRows.Max(v => v.AxisX);
        double minY = GeometryVertexRows.Min(v => v.AxisY);
        double maxY = GeometryVertexRows.Max(v => v.AxisY);
        return (minX, maxX, minY, maxY);
    }

    /// <summary>Width and height of the axis-aligned bounding box (non-negative).</summary>
    public (double Width, double Height) GetBoundingBoxExtent()
    {
        var (minX, maxX, minY, maxY) = GetBoundingBoxBounds();
        return (Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    /// <summary>
    /// Axis-aligned bounds for mesh extent: same closed polygon as <see cref="TryGetTriangleMeshPolygonAxisPoints"/> (S1, etc.),
    /// otherwise all vertices. This matches the filled surface region instead of mixing surface logic with a global fallback.
    /// </summary>
    public (double MinX, double MaxX, double MinY, double MaxY) GetMeshBoundingBoxBounds()
    {
        if (TryGetTriangleMeshPolygonAxisPoints(out var poly) && poly.Count >= 3)
        {
            double minX = poly.Min(p => p.X);
            double maxX = poly.Max(p => p.X);
            double minY = poly.Min(p => p.Y);
            double maxY = poly.Max(p => p.Y);
            return (minX, maxX, minY, maxY);
        }
        return GetBoundingBoxBounds();
    }

    /// <summary>Width and height of <see cref="GetMeshBoundingBoxBounds"/>.</summary>
    public (double Width, double Height) GetMeshBoundingBoxExtent()
    {
        var (minX, maxX, minY, maxY) = GetMeshBoundingBoxBounds();
        return (Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    /// <summary>
    /// Closed surface polygon in axis coordinates for unstructured triangle mesh (selected surface first, else tightest polygon).
    /// </summary>
    public bool TryGetTriangleMeshPolygonAxisPoints(out List<Point> axisPoints)
    {
        axisPoints = new List<Point>();
        static double AbsArea(List<Point> pts)
        {
            double a = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += pts[i].X * pts[j].Y - pts[j].X * pts[i].Y;
            }
            return Math.Abs(a * 0.5);
        }

        bool TryFromSurface(GeometrySurface surface, out List<Point> pts)
        {
            if (TryGetSurfaceAxisPolygonDenseForSurface(surface, out pts))
                return true;

            pts = new List<Point>();
            if (!TryGetSurfacePolygonOrderedLabels(surface, out var labels) || labels.Count < 3)
                return false;
            foreach (var label in labels)
            {
                var v = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == label);
                if (v == null)
                    return false;
                pts.Add(new Point(v.AxisX, v.AxisY));
            }
            return pts.Count >= 3;
        }

        if (SelectedGeometrySurface != null && TryFromSurface(SelectedGeometrySurface, out var sel))
        {
            axisPoints = sel;
            return true;
        }

        var candidates = new List<(List<Point> Pts, double Area)>();
        foreach (var s in GeometrySurfaces)
        {
            if (TryFromSurface(s, out var pts))
                candidates.Add((pts, AbsArea(pts)));
        }
        if (candidates.Count == 0)
            return false;
        axisPoints = candidates.OrderBy(c => c.Area).First().Pts;
        return true;
    }

    /// <summary>
    /// Closed boundary for structured mesh: surface polygon when available, else vertex loop (L1..Ln order).
    /// </summary>
    public bool TryGetClosedBoundaryAxisPolygon(out List<Point> axisPoints)
    {
        if (TryGetTriangleMeshPolygonAxisPoints(out axisPoints))
            return true;

        axisPoints = new List<Point>();
        if (GeometryVertexRows.Count < 3 || BoundarySegmentCount < 3)
            return false;

        foreach (var row in GeometryVertexRows)
            axisPoints.Add(new Point(row.AxisX, row.AxisY));
        return axisPoints.Count >= 3;
    }

    /// <summary>
    /// Returns the axis-space polygon for a specific surface by surface number.
    /// Arc boundaries are sampled into a dense polyline so the enclosed region matches the true curve
    /// (not the chord between arc endpoints). Falls back to one vertex per polygon label if needed.
    /// </summary>
    public bool TryGetSurfaceAxisPolygon(int surfaceNumber, out List<Point> axisPoints)
    {
        axisPoints = new List<Point>();
        var surface = GeometrySurfaces.FirstOrDefault(s => s.SurfaceNumber == surfaceNumber);
        if (surface == null) return false;

        if (TryGetSurfaceAxisPolygonDenseForSurface(surface, out axisPoints))
            return true;

        if (!TryGetSurfacePolygonOrderedLabels(surface, out var labels) || labels.Count < 3)
            return false;
        foreach (var label in labels)
        {
            var v = GeometryVertexRows.FirstOrDefault(row => row.PointLabel == label);
            if (v == null) return false;
            axisPoints.Add(new Point(v.AxisX, v.AxisY));
        }
        return axisPoints.Count >= 3;
    }

    /// <summary>
    /// Walks the closed surface perimeter in axis space the same way as the material overlay:
    /// straight segments use endpoints; arc segments use From–Through–To in traversal order (not the chord).
    /// Circle-only surfaces return false (use <see cref="TryGetSurfaceAxisPolygon"/> for sampled rim).
    /// </summary>
    public bool TryGetSurfacePerimeterAxisSegments(int surfaceNumber, out List<SurfaceAxisPerimeterSegment> segments)
    {
        segments = new List<SurfaceAxisPerimeterSegment>();
        var surface = GeometrySurfaces.FirstOrDefault(s => s.SurfaceNumber == surfaceNumber);
        if (surface == null) return false;

        if (IsCircleOnlySurface(surface))
            return false;

        if (!TryGetSurfacePolygonOrderedLabels(surface, out var orderedLabels) || orderedLabels.Count < 3)
            return false;

        var n = orderedLabels.Count;
        for (var i = 0; i < n; i++)
        {
            var va = orderedLabels[i];
            var vb = orderedLabels[(i + 1) % n];
            var seg = FindGeometrySegmentForSurfaceEdge(surface, va, vb);
            var vA = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == va);
            var vB = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == vb);
            if (vA == null || vB == null)
                return false;

            if (seg is { IsArc: true } && !string.IsNullOrEmpty(seg.ThroughPoint))
            {
                var vFrom = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == seg.FromPoint);
                var vMid = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == seg.ThroughPoint);
                var vTo = GeometryVertexRows.FirstOrDefault(r => r.PointLabel == seg.ToPoint);
                if (vFrom != null && vMid != null && vTo != null)
                {
                    var pFrom = new Point(vFrom.AxisX, vFrom.AxisY);
                    var pThrough = new Point(vMid.AxisX, vMid.AxisY);
                    var pTo = new Point(vTo.AxisX, vTo.AxisY);
                    Point p1, p2, p3;
                    if (va == seg.FromPoint && vb == seg.ToPoint)
                    {
                        p1 = pFrom;
                        p2 = pThrough;
                        p3 = pTo;
                    }
                    else if (va == seg.ToPoint && vb == seg.FromPoint)
                    {
                        p1 = pTo;
                        p2 = pThrough;
                        p3 = pFrom;
                    }
                    else
                    {
                        segments.Add(SurfaceAxisPerimeterSegment.Line(
                            new Point(vA.AxisX, vA.AxisY), new Point(vB.AxisX, vB.AxisY)));
                        continue;
                    }

                    segments.Add(SurfaceAxisPerimeterSegment.Arc(p1, p2, p3));
                    continue;
                }
            }

            segments.Add(SurfaceAxisPerimeterSegment.Line(
                new Point(vA.AxisX, vA.AxisY), new Point(vB.AxisX, vB.AxisY)));
        }

        return segments.Count > 0;
    }

    /// <summary>
    /// Path distance (from first vertex) to the closest point on the polyline to (axisX, axisY).
    /// Used to map load positions from Load Conditions to 1D node indices for .dat loaded_nodes.
    /// </summary>
    public double GetPathDistanceAtPoint(double axisX, double axisY)
    {
        var count = GeometryVertexRows.Count;
        if (count < 2) return 0;
        var cumDist = new List<double>(count) { 0 };
        for (int i = 0; i < count - 1; i++)
        {
            double dx = GeometryVertexRows[i + 1].AxisX - GeometryVertexRows[i].AxisX;
            double dy = GeometryVertexRows[i + 1].AxisY - GeometryVertexRows[i].AxisY;
            cumDist.Add(cumDist[i] + Math.Sqrt(dx * dx + dy * dy));
        }
        double bestPathDist = 0;
        double bestDistSq = double.MaxValue;
        for (int i = 0; i < count - 1; i++)
        {
            double ax = GeometryVertexRows[i].AxisX, ay = GeometryVertexRows[i].AxisY;
            double bx = GeometryVertexRows[i + 1].AxisX, by = GeometryVertexRows[i + 1].AxisY;
            double dx = bx - ax, dy = by - ay;
            double segLen = Math.Sqrt(dx * dx + dy * dy);
            if (segLen < 1e-12)
            {
                double distSq = BestDistSq(axisX, axisY, ax, ay);
                if (distSq < bestDistSq) { bestDistSq = distSq; bestPathDist = cumDist[i]; }
                continue;
            }
            double t = ((axisX - ax) * dx + (axisY - ay) * dy) / (segLen * segLen);
            t = Math.Clamp(t, 0, 1);
            double px = ax + t * dx, py = ay + t * dy;
            double dSq = (axisX - px) * (axisX - px) + (axisY - py) * (axisY - py);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestPathDist = cumDist[i] + t * segLen;
            }
        }
        return bestPathDist;
    }

    private static double BestDistSq(double x, double y, double ax, double ay) =>
        (x - ax) * (x - ax) + (y - ay) * (y - ay);

    /// <summary>
    /// Cumulative path length at each vertex: [0, L1, L1+L2, ...]. Used for etype (element type by layer) in mesh and .dat.
    /// </summary>
    public IReadOnlyList<double> GetSegmentBounds()
    {
        var bounds = new List<double> { 0 };
        var verts = GeometryVertexRows;
        if (verts.Count < 2) return bounds;
        double cum = 0;
        for (int i = 0; i < verts.Count - 1; i++)
        {
            double dx = verts[i + 1].AxisX - verts[i].AxisX;
            double dy = verts[i + 1].AxisY - verts[i].AxisY;
            cum += Math.Sqrt(dx * dx + dy * dy);
            bounds.Add(cum);
        }
        return bounds;
    }

    /// <summary>
    /// Replaces geometry with n+1 vertices placed along the current path at equal path-length intervals.
    /// Nodes and elements follow the geometry direction (e.g. vertical Y or horizontal X). Used by 1D Mesh subtab.
    /// </summary>
    /// <param name="length">Total path length (axis units).</param>
    /// <param name="n">Number of elements (segments); creates n+1 nodes.</param>
    public void Set1DLineGeometry(double length, int n)
    {
        if (n < 1 || length <= 0) return;
        var count = GeometryVertexRows.Count;
        if (count < 2) return;

        // Copy current path (x,y) before clearing
        var pathX = new List<double>(count);
        var pathY = new List<double>(count);
        for (int i = 0; i < count; i++)
        {
            pathX.Add(GeometryVertexRows[i].AxisX);
            pathY.Add(GeometryVertexRows[i].AxisY);
        }

        // Cumulative path distance at each vertex (cumDist[0]=0, cumDist[i]=distance from v0 to vi)
        var cumDist = new List<double>(count) { 0 };
        for (int i = 0; i < count - 1; i++)
        {
            double dx = pathX[i + 1] - pathX[i];
            double dy = pathY[i + 1] - pathY[i];
            cumDist.Add(cumDist[i] + Math.Sqrt(dx * dx + dy * dy));
        }

        GeometryVertexRows.Clear();
        GeometryLineSegments.Clear();
        GeometryInternalEdges.Clear();
        _hasAddedElevationLayer = false;
        _materialLayerCutElevations.Clear();
        _layerMaterialAssignmentByDisplayNumber.Clear();
        _layerDisplayNameByDisplayNumber.Clear();
        _layerDisplayColorArgb.Clear();
        _nextStablePointId = 1;

        for (int i = 0; i <= n; i++)
        {
            double s = (i * length) / n;
            if (s >= length) s = length;
            // Find segment containing path distance s and interpolate
            double x, y;
            if (s <= 0)
            {
                x = pathX[0];
                y = pathY[0];
            }
            else if (s >= cumDist[count - 1])
            {
                x = pathX[count - 1];
                y = pathY[count - 1];
            }
            else
            {
                int seg = 0;
                while (seg < count - 1 && cumDist[seg + 1] < s)
                    seg++;
                double s0 = cumDist[seg];
                double s1 = cumDist[seg + 1];
                double t = (s1 > s0) ? (s - s0) / (s1 - s0) : 0;
                x = pathX[seg] + t * (pathX[seg + 1] - pathX[seg]);
                y = pathY[seg] + t * (pathY[seg + 1] - pathY[seg]);
            }
            var display = new GeometryVertexDisplay { Index = i, AxisX = x, AxisY = y };
            display.StablePointId = AllocateStablePointId();
            display.Owner = this;
            GeometryVertexRows.Add(display);
        }

        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments();
        RefreshLayerCoordinateSummaries();
    }

    /// <summary>
    /// Copies geometry data from another instance (vertices, axis bounds, layer state).
    /// Used to create a separate copy for Material Properties popup.
    /// </summary>
    public void CopyGeometryFrom(GeometryDefinitionViewModel source)
    {
        if (ReferenceEquals(this, source)) return;

        GeometryVertexRows.Clear();
        foreach (var v in source.GeometryVertexRows)
        {
            var display = new GeometryVertexDisplay { Index = GeometryVertexRows.Count, AxisX = v.AxisX, AxisY = v.AxisY };
            display.StablePointId = v.StablePointId;
            display.SplitFromLineNumber = v.SplitFromLineNumber;
            display.IsMaterialLayerCutVertex = v.IsMaterialLayerCutVertex;
            display.Owner = this;
            GeometryVertexRows.Add(display);
        }
        for (int i = 0; i < GeometryVertexRows.Count; i++)
            GeometryVertexRows[i].Index = i;

        GeometryInternalEdges.Clear();
        foreach (var ie in source.GeometryInternalEdges)
        {
            if (ie.IsArc)
                GeometryInternalEdges.Add(new GeometryInternalEdge(ie.StableStart, ie.StableMid, ie.StableEnd));
            else
                GeometryInternalEdges.Add(new GeometryInternalEdge(ie.StableMin, ie.StableMax));
        }

        GeometryInternalCircles.Clear();
        foreach (var c in source.GeometryInternalCircles)
        {
            GeometryInternalCircles.Add(new GeometryInternalCircle
            {
                Id = c.Id,
                CenterAxisX = c.CenterAxisX,
                CenterAxisY = c.CenterAxisY,
                RadiusAxis = c.RadiusAxis
            });
        }

        _nextInternalCircleId = source.GeometryInternalCircles.Count == 0
            ? 1
            : source.GeometryInternalCircles.Max(c => c.Id) + 1;

        CanvasWidth = source.CanvasWidth;
        CanvasHeight = source.CanvasHeight;
        AxisXMin = source.AxisXMin;
        AxisYMin = source.AxisYMin;
        AxisXMax = source.AxisXMax;
        AxisYMax = source.AxisYMax;
        LayerElevationInput = source.LayerElevationInput;
        HasAddedElevationLayer = source.HasAddedElevationLayer;
        SetMaterialLayerCutElevationsFromRestore(source.MaterialLayerCutElevationsSnapshot);
        ReplaceLayerMaterialAssignmentsFrom(source.LayerMaterialAssignmentsSnapshot);
        ReplaceLayerDisplayNamesFrom(source.LayerDisplayNamesSnapshot);
        ReplaceLayerDisplayColorsFrom(source.LayerDisplayColorsSnapshot);
        ReplaceSurfaceMaterialFillsFrom(
            source.SurfaceMaterialFillsSnapshot.Count > 0
                ? source.SurfaceMaterialFillsSnapshot.ToList()
                : null);

        RefreshGeometryPath();
        RefreshAvailablePointLabels();
        RefreshGeometryLineSegments();

        var segByLine = source.GeometryLineSegments
            .Where(s => !s.IsCircle)
            .GroupBy(s => s.LineNumber)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var dst in GeometryLineSegments.Where(s => !s.IsCircle))
        {
            if (!segByLine.TryGetValue(dst.LineNumber, out var src))
                continue;
            CopySegmentFieldsFrom(dst, src);
        }

        NormalizeStablePointIdsAfterLoad();

        GeometrySurfaces.Clear();
        foreach (var srcS in source.GeometrySurfaces)
        {
            var dstS = new GeometrySurface(srcS.SurfaceNumber, srcS.SurfaceName, srcS.ConnectedLineNumbers);
            dstS.Tag = srcS.Tag;
            dstS.ColorIndex = srcS.ColorIndex;
            foreach (var n in srcS.PickedLineNumbers)
                dstS.PickedLineNumbers.Add(n);
            dstS.IsPolygonLocked = srcS.IsPolygonLocked;
            foreach (var l in srcS.LockedPolygonVertexLabels)
                dstS.LockedPolygonVertexLabels.Add(l);
            foreach (var l in srcS.DraftPolygonVertexLabels)
                dstS.DraftPolygonVertexLabels.Add(l);
            dstS.AssignedMaterialId = srcS.AssignedMaterialId;
            GeometrySurfaces.Add(dstS);
        }

        if (source.SelectedGeometrySurface != null)
        {
            var n = source.SelectedGeometrySurface.SurfaceNumber;
            SelectedGeometrySurface = GeometrySurfaces.FirstOrDefault(s => s.SurfaceNumber == n);
        }
        else
            SelectedGeometrySurface = null;

        ExcavationOperations.Clear();
        _selectedExcavationOperation = null;
        OnPropertyChanged(nameof(SelectedExcavationOperation));
        foreach (var r in source.ExcavationOperations)
        {
            var row = new BooleanOperationRow
            {
                OperationId = r.OperationId,
                OperationType = r.OperationType,
                TypeEnum = r.TypeEnum,
                RegionAName = r.RegionAName,
                RegionBName = r.RegionBName,
                ResultRegionName = r.ResultRegionName,
                RegionASurfaceNumber = r.RegionASurfaceNumber,
                RegionBSurfaceNumber = r.RegionBSurfaceNumber,
                RegionAArea = r.RegionAArea,
                RegionBArea = r.RegionBArea,
                ResultArea = r.ResultArea,
                IsComplete = r.IsComplete,
                AppliedInStageNumber = r.AppliedInStageNumber
            };
            if (r.IsMultiRegion && r.ParticipatingSurfaceNumbers.Count > 0)
            {
                row.IsMultiRegion = true;
                foreach (var n in r.ParticipatingSurfaceNumbers)
                    row.ParticipatingSurfaceNumbers.Add(n);
            }

            ExcavationOperations.Add(row);
        }

        RenumberExcavationOperationIds();
        RecomputeSurfaceBooleanFromLastOperation();
    }

    private static void CopySegmentFieldsFrom(GeometryLineSegment dst, GeometryLineSegment src)
    {
        dst.IsArc = src.IsArc;
        dst.ThroughPoint = src.ThroughPoint;
        dst.ArcRadius = src.ArcRadius;
        dst.IsApplied = src.IsApplied;
        dst.Tag = src.Tag;
        dst.UnitWeight = src.UnitWeight;
        dst.IsSaturatedUnitWeightEnabled = src.IsSaturatedUnitWeightEnabled;
        dst.SaturatedUnitWeight = src.SaturatedUnitWeight;
        dst.Cohesion = src.Cohesion;
        dst.FrictionAngle = src.FrictionAngle;
        dst.YoungsModulus = src.YoungsModulus;
        dst.PoissonsRatio = src.PoissonsRatio;
        dst.Density = src.Density;
        dst.ConsiderDensityForFE = src.ConsiderDensityForFE;
        dst.PlaneStrain = src.PlaneStrain;
    }

    /// <summary>
    /// Copies this geometry data to another instance. Inverse of CopyGeometryFrom.
    /// </summary>
    public void CopyGeometryTo(GeometryDefinitionViewModel target)
    {
        if (ReferenceEquals(this, target)) return;
        target.CopyGeometryFrom(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
