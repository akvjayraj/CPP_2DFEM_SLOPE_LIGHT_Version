using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// DataContext for Mesh and Boundary tabs' slope canvas. Forwards the same geometry surface/edge/vertex state
/// as <see cref="GeometryDefinitionViewModel"/> plus mesh overlays from <see cref="MeshViewModel"/> so sketches
/// stay in sync with the Geometry Definition tab.
/// </summary>
public class MeshTabCanvasWrapper : INotifyPropertyChanged
{
    private readonly GeometryDefinitionViewModel _geometry;
    private readonly MeshViewModel _mesh;
    private readonly BoundaryConditionsViewModel? _bc;
    private MaterialPropertiesViewModel? _materialProperties;
    private readonly HashSet<MaterialModel> _materialColourHooked = new();
    private readonly ObservableCollection<MeshDiscretizationNodeDisplay> _convertedDiscretizationNodes = new();
    private readonly ObservableCollection<MeshDiscretizationNodeDisplay> _highlightedMeshDiscretizationNodes = new();
    private readonly ObservableCollection<MeshElementDisplay> _convertedMeshElements = new();
    private readonly ObservableCollection<MeshNodeDisplayItem> _meshNodeDisplayItems = new();
    private readonly ObservableCollection<BcCanvasMarker> _bcCanvasMarkers = new();
    private static readonly ObservableCollection<MeshNodeDisplayItem> EmptyMeshNodeLabels = new();
    private static readonly ObservableCollection<MeshElementDisplay> EmptyMeshElementLabels = new();

    // ── Stage-aware filtering (Mesh tab + downstream tabs) ──────────────────────
    private StagedConstructionViewModel? _staged;
    private bool _stageAwareFilter;
    // Re-entry guard: prevents the geometry axis update in EnsureAxisFitsNodes from
    // triggering a second RefreshConvertedMesh → EnsureAxisFitsNodes cycle.
    private bool _isFittingAxis;
    private bool _refreshScheduled;
    private MeshElementSpatialIndex? _spatialIndex;

    private int MeshInstanceKey => RuntimeHelpers.GetHashCode(_mesh);

    private long ComputeGeometryRevision() =>
        HashCode.Combine(_geometry.AxisXMin, _geometry.AxisXMax, _geometry.AxisYMin, _geometry.AxisYMax, _geometry.CanvasWidth, _geometry.CanvasHeight);

    private int ComputeMeshRevision() =>
        HashCode.Combine(_mesh.DiscretizationNodes.Count, _mesh.MeshElements.Count, _mesh.MeshInformationRows.Count);

    private static int ComputeHiddenIdsHash(IReadOnlySet<int>? hiddenIds)
    {
        if (hiddenIds == null || hiddenIds.Count == 0) return 0;
        var hash = new HashCode();
        foreach (var id in hiddenIds.OrderBy(x => x))
            hash.Add(id);
        return hash.ToHashCode();
    }

    public MeshTabCanvasWrapper(GeometryDefinitionViewModel geometry, MeshViewModel mesh)
        : this(geometry, mesh, null, null) { }

    /// <summary>
    /// Three-arg overload used by the Boundary Conditions tab. When <paramref name="bc"/> is non-null,
    /// the wrapper additionally exposes <see cref="BcCanvasMarkers"/> (canvas-pixel space) computed from
    /// mesh nodes + <paramref name="bc"/>.BoundaryConditions, gated by
    /// <see cref="ShowBcMarkers"/> (which mirrors <see cref="BoundaryConditionsViewModel.MarkersVisible"/>).
    /// The Mesh tab uses the two-arg overload and never sees BC markers.
    /// </summary>
    public MeshTabCanvasWrapper(
        GeometryDefinitionViewModel geometry,
        MeshViewModel mesh,
        BoundaryConditionsViewModel? bc,
        MaterialPropertiesViewModel? materialProperties = null)
    {
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        _bc = bc;
        BindMaterialProperties(materialProperties ?? mesh.MaterialPropertiesVM);
        _geometry.PropertyChanged += (_, e) =>
        {
            // Null/empty PropertyName means "refresh all" on the source VM — bubble the same for WPF bindings.
            OnPropertyChanged(string.IsNullOrEmpty(e.PropertyName) ? string.Empty : e.PropertyName);
            // Only reproject mesh when the axis/viewport changes — not for SurfaceRegionOverlays (avoids stack overflow).
            if (_mesh.IsDiscretized && !_mesh.IsMeshCanvasBulkUpdate && GeometryAxisOrCanvasChanged(e.PropertyName))
            {
                MeshCanvasConversionCache.InvalidateMesh(MeshInstanceKey);
                RefreshConvertedMesh();
            }
            else if (e.PropertyName is nameof(GeometryDefinitionViewModel.SurfaceRegionOverlays)
                     or nameof(GeometryDefinitionViewModel.LayerMaterialVisualRevision))
            {
                MeshCanvasConversionCache.InvalidateMesh(MeshInstanceKey);
                RefreshConvertedMesh();
            }
            RefreshBcMarkers();
            if (e.PropertyName is null or "" or nameof(GeometryDefinitionViewModel.CurrentZoomFactor))
            {
                OnPropertyChanged(nameof(MeshLabelInverseScale));
            }
        };
        _mesh.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(string.IsNullOrEmpty(e.PropertyName) ? string.Empty : e.PropertyName);
            if (e.PropertyName is nameof(MeshViewModel.IsDiscretized) or nameof(MeshViewModel.DiscretizationNodes) or nameof(MeshViewModel.MeshElements) or nameof(MeshViewModel.SelectedMeshInformationRow) or nameof(MeshViewModel.UserDefinedHighlightedNodeIds) or nameof(MeshViewModel.HighlightedMidsideAxisPoints))
            {
                if (!_mesh.IsMeshCanvasBulkUpdate)
                {
                    MeshCanvasConversionCache.InvalidateMesh(MeshInstanceKey);
                    RefreshConvertedMesh();
                }
            }
            if (e.PropertyName is nameof(MeshViewModel.ShowMeshNodes) or nameof(MeshViewModel.ShowMeshElements) or nameof(MeshViewModel.ShowBoundaryDiscretizationMarkers))
                OnPropertyChanged(e.PropertyName);
            if (e.PropertyName is nameof(MeshViewModel.IsDiscretized) or nameof(MeshViewModel.ShowMeshElements))
            {
                OnPropertyChanged(nameof(MaterialLayerOverlayOpacity));
                OnPropertyChanged(nameof(ShowMaterialLayerLabelsOnCanvas));
            }
            if (e.PropertyName is nameof(MeshViewModel.ShowMeshNodes))
                OnPropertyChanged(nameof(MeshNodeLabelItems));
            if (e.PropertyName is nameof(MeshViewModel.ShowMeshElements))
                OnPropertyChanged(nameof(MeshElementLabelItems));
            if (e.PropertyName is nameof(MeshViewModel.IsExcavateElementsMode) or nameof(MeshViewModel.ExcavateElementsButtonLabel))
            {
                OnPropertyChanged(nameof(IsExcavateElementsMode));
                OnPropertyChanged(nameof(ExcavateElementsButtonLabel));
            }
            if (e.PropertyName is nameof(MeshViewModel.ExcavateElementsStatusMessage))
                OnPropertyChanged(nameof(ExcavateElementsStatusMessage));
            if (e.PropertyName is nameof(MeshViewModel.IsDiscretized))
                RefreshBcMarkers();
        };
        _mesh.DiscretizationNodes.CollectionChanged += (_, _) =>
        {
            if (_mesh.IsMeshCanvasBulkUpdate) return;
            MeshCanvasConversionCache.InvalidateMesh(MeshInstanceKey);
            EnsureAxisFitsNodes();
            RefreshConvertedMesh();
        };
        _mesh.MeshElements.CollectionChanged += (_, _) =>
        {
            if (_mesh.IsMeshCanvasBulkUpdate) return;
            MeshCanvasConversionCache.InvalidateMesh(MeshInstanceKey);
            RefreshConvertedMesh();
        };
        _mesh.MeshInformationRows.CollectionChanged += OnMeshInformationRowsCollectionChanged;

        if (_bc != null)
        {
            _bc.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(BoundaryConditionsViewModel.MarkersVisible)
                    or nameof(BoundaryConditionsViewModel.ShowStructuralMarkers)
                    or nameof(BoundaryConditionsViewModel.ShowSeepageMarkers)
                    or null or "")
                {
                    OnPropertyChanged(nameof(ShowBcMarkers));
                    RefreshBcMarkers();
                }
                if (e.PropertyName is nameof(BoundaryConditionsViewModel.IsBcManualImportMode)
                    or nameof(BoundaryConditionsViewModel.IsVerticesImportMode)
                    or nameof(BoundaryConditionsViewModel.IsNodesImportMode)
                    or nameof(BoundaryConditionsViewModel.IsBcImportSelectionVisible)
                    or nameof(BoundaryConditionsViewModel.BcImportSelectionLeft)
                    or nameof(BoundaryConditionsViewModel.BcImportSelectionTop)
                    or nameof(BoundaryConditionsViewModel.BcImportSelectionWidth)
                    or nameof(BoundaryConditionsViewModel.BcImportSelectionHeight)
                    or nameof(BoundaryConditionsViewModel.SelectedImportMode))
                {
                    OnPropertyChanged(e.PropertyName);
                    OnPropertyChanged(nameof(IsBcManualImportMode));
                    OnPropertyChanged(nameof(IsBcImportSelectionVisible));
                    OnPropertyChanged(nameof(BcImportSelectionLeft));
                    OnPropertyChanged(nameof(BcImportSelectionTop));
                    OnPropertyChanged(nameof(BcImportSelectionWidth));
                    OnPropertyChanged(nameof(BcImportSelectionHeight));
                }
            };
            _bc.BoundaryConditions.CollectionChanged += (_, _) => RefreshBcMarkers();
        }

        if (_mesh.IsDiscretized) { EnsureAxisFitsNodes(); RefreshConvertedMesh(); }
        RefreshBcMarkers();
    }

    private void OnMeshInformationRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MeshCanvasConversionCache.InvalidateMesh(MeshInstanceKey);
        RefreshConvertedMesh();
        RefreshBcMarkers();
    }

    /// <summary>
    /// Enable stage-aware mesh hiding for this canvas wrapper. When invoked, every element whose ID
    /// falls in <see cref="StagedConstructionViewModel.CumulativeExcavatedElementIdsUpToActiveStage"/>
    /// is dropped from <see cref="MeshElements"/> so the bound canvas does not draw it.
    /// <para>
    /// The filter is short-circuited (no hiding) when:
    ///   <list type="bullet">
    ///     <item><c>staged.Stages.Count &lt;= 1</c> (Initial Stress only — no excavation defined), OR</item>
    ///     <item><c>staged.ActiveStageNumber &lt; 2</c> (the user is viewing the Initial Stress stage).</item>
    ///   </list>
    /// </para>
    /// Idempotent: calling this twice is safe — the second call replaces the subscription target and
    /// re-runs the converter so the wrapper converges on the latest <paramref name="staged"/> instance.
    /// </summary>
    public void EnableStageAwareFilter(StagedConstructionViewModel staged)
    {
        if (staged == null) throw new ArgumentNullException(nameof(staged));

        // Detach previous subscriptions (idempotent re-wire).
        if (_staged != null)
        {
            _staged.PropertyChanged -= OnStagedPropertyChangedForFilter;
            _staged.ExcavatedElementIdsChanged -= OnStagedElementIdsChangedForFilter;
            _staged.Stages.CollectionChanged -= OnStagedStagesChangedForFilter;
        }

        _staged = staged;
        _stageAwareFilter = true;

        _staged.PropertyChanged += OnStagedPropertyChangedForFilter;
        _staged.ExcavatedElementIdsChanged += OnStagedElementIdsChangedForFilter;
        _staged.Stages.CollectionChanged += OnStagedStagesChangedForFilter;

        RefreshConvertedMesh();
    }

    private void OnStagedPropertyChangedForFilter(object? sender, PropertyChangedEventArgs e)
    {
        if (!_stageAwareFilter) return;
        if (e.PropertyName is nameof(StagedConstructionViewModel.ActiveStageNumber)
                              or nameof(StagedConstructionViewModel.CumulativeExcavatedElementIdsUpToActiveStage)
                              or nameof(StagedConstructionViewModel.ActiveStage))
            ScheduleRefreshConvertedMesh();
    }

    private void OnStagedElementIdsChangedForFilter(object? sender, System.EventArgs e)
    {
        if (_stageAwareFilter) ScheduleRefreshConvertedMesh();
    }

    private void OnStagedStagesChangedForFilter(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_stageAwareFilter) ScheduleRefreshConvertedMesh();
    }

    /// <summary>True when the cumulative excavated set should be applied to the next converter pass.</summary>
    private bool ShouldApplyStageFilter =>
        _stageAwareFilter
        && _staged is { } sc
        && sc.Stages.Count > 1
        && sc.ActiveStageNumber >= 2
        && sc.CumulativeExcavatedElementIdsUpToActiveStage.Count > 0;

    /// <summary>
    /// Recompute the BC marker overlay (canvas-pixel space) from the current mesh + BC rows.
    /// Cleared when no <see cref="BoundaryConditionsViewModel"/> was supplied,
    /// when the user has the markers hidden, or when there is no mesh yet.
    /// </summary>
    private void RefreshBcMarkers()
    {
        _bcCanvasMarkers.Clear();

        if (_bc == null || !_bc.MarkersVisible)
        {
            OnPropertyChanged(nameof(BcCanvasMarkers));
            return;
        }

        var nodes = _mesh.DiscretizationNodes;
        if (nodes.Count == 0)
        {
            OnPropertyChanged(nameof(BcCanvasMarkers));
            return;
        }

        var nodeById = new Dictionary<int, Point>();
        for (int i = 0; i < nodes.Count; i++)
        {
            int id = nodes[i].NodeId > 0 ? nodes[i].NodeId : i + 1;
            nodeById[id] = nodes[i].Location;
        }

        foreach (var bcRow in _bc.BoundaryConditions)
        {
            if (bcRow.NodeId <= 0) continue;
            var kind = BcSymbolCatalog.ResolveKind(bcRow.BCType);
            var isSeepage = BcSymbolCatalog.IsSeepageKind(kind);
            if (isSeepage && !_bc.ShowSeepageMarkers) continue;
            if (!isSeepage && !_bc.ShowStructuralMarkers) continue;

            if (!nodeById.TryGetValue(bcRow.NodeId, out var loc))
            {
                int idx = bcRow.NodeId - 1;
                if (idx < 0 || idx >= nodes.Count) continue;
                loc = nodes[idx].Location;
            }
            var canvasPt = _geometry.ConvertAxisToCanvas(new Point(loc.X, loc.Y));
            var category = string.IsNullOrWhiteSpace(bcRow.AnalysisCategory)
                ? BcSymbolCatalog.AnalysisFamily(kind)
                : bcRow.AnalysisCategory;
            _bcCanvasMarkers.Add(new BcCanvasMarker(kind, canvasPt, bcRow.NodeId, bcRow.BCType, category));
        }

        OnPropertyChanged(nameof(BcCanvasMarkers));
    }

    /// <summary>Used by SlopeCanvasView code-behind to get the geometry VM for interaction (zoom, add vertex, etc.).</summary>
    public GeometryDefinitionViewModel Geometry => _geometry;

    /// <summary>
    /// When node coordinates fall outside the current geometry axis bounds — typical for
    /// user-defined meshes and script-editor-loaded meshes that were never produced from the
    /// Geometry tab — expand the axis to encompass the mesh extents so
    /// <see cref="RefreshConvertedMesh"/> maps nodes to valid canvas positions.
    /// Skips the update when called re-entrantly (axis change triggers another refresh).
    /// </summary>
    private void EnsureAxisFitsNodes()
    {
        if (_isFittingAxis || _mesh.DiscretizationNodes.Count == 0) return;

        _isFittingAxis = true;
        try
        {
            MeshCanvasAxisFitter.FitToDiscretizationNodes(_geometry, _mesh.DiscretizationNodes);
        }
        finally
        {
            _isFittingAxis = false;
        }
    }

    private void ScheduleRefreshConvertedMesh()
    {
        if (_refreshScheduled) return;
        _refreshScheduled = true;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            _refreshScheduled = false;
            RefreshConvertedMeshCore();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _refreshScheduled = false;
            RefreshConvertedMeshCore();
        });
    }

    private void RefreshConvertedMesh()
    {
        _refreshScheduled = false;
        RefreshConvertedMeshCore();
    }

    private void RefreshConvertedMeshCore()
    {
        if (_isFittingAxis) return;
        if (!_mesh.IsDiscretized)
        {
            _convertedDiscretizationNodes.Clear();
            _highlightedMeshDiscretizationNodes.Clear();
            _convertedMeshElements.Clear();
            _meshNodeDisplayItems.Clear();
            _spatialIndex = null;
            NotifyConvertedMeshChanged();
            return;
        }

        var geomRevision = ComputeGeometryRevision();
        var meshRevision = ComputeMeshRevision();
        var meshKey = MeshInstanceKey;
        IReadOnlySet<int>? hiddenIds = ShouldApplyStageFilter
            ? _staged!.CumulativeExcavatedElementIdsUpToActiveStage
            : null;
        var stageKey = ShouldApplyStageFilter ? _staged!.ActiveStageNumber : 0;
        var hiddenHash = ComputeHiddenIdsHash(hiddenIds);

        if (MeshCanvasConversionCache.TryGetFiltered(meshKey, geomRevision, meshRevision, stageKey, hiddenHash, out var filteredCached)
            && filteredCached != null)
        {
            ApplySnapshot(filteredCached);
            return;
        }

        MeshCanvasConversionSnapshot baseSnapshot;
        if (!MeshCanvasConversionCache.TryGetBase(meshKey, geomRevision, meshRevision, out var baseCached)
            || baseCached == null)
        {
            baseSnapshot = BuildBaseSnapshot();
            MeshCanvasConversionCache.SetBase(meshKey, geomRevision, meshRevision, baseSnapshot);
        }
        else
        {
            baseSnapshot = baseCached;
        }

        var filteredSnapshot = MeshCanvasConversionCache.FilterSnapshot(baseSnapshot, hiddenIds);
        MeshCanvasConversionCache.SetFiltered(meshKey, geomRevision, meshRevision, stageKey, hiddenHash, filteredSnapshot);
        ApplySnapshot(filteredSnapshot);
    }

    private MeshCanvasConversionSnapshot BuildBaseSnapshot()
    {
        var nodes = new List<MeshDiscretizationNodeDisplay>();
        var highlighted = new List<MeshDiscretizationNodeDisplay>();
        var nodeItems = new List<MeshNodeDisplayItem>();
        var highlightedNodeIds = _mesh.UserDefinedHighlightedNodeIds;

        foreach (var n in _mesh.DiscretizationNodes)
        {
            var canvasPt = _geometry.ConvertAxisToCanvas(new Point(n.Location.X, n.Location.Y));
            int nodeId = n.NodeId > 0 ? n.NodeId : nodeItems.Count + 1;
            bool isHighlighted = n.IsHighlighted || highlightedNodeIds.Contains(nodeId);
            var display = new MeshDiscretizationNodeDisplay { NodeId = nodeId, Location = canvasPt, IsHighlighted = isHighlighted };
            nodes.Add(display);
            if (isHighlighted)
                highlighted.Add(display);
            nodeItems.Add(new MeshNodeDisplayItem { X = canvasPt.X, Y = canvasPt.Y, NodeId = nodeId });
        }

        const double midsideDupTol = 1e-6;
        foreach (var axisPt in _mesh.HighlightedMidsideAxisPoints)
        {
            var canvasPt = _geometry.ConvertAxisToCanvas(axisPt);
            bool duplicate = false;
            foreach (var existing in highlighted)
            {
                double dx = existing.Location.X - canvasPt.X;
                double dy = existing.Location.Y - canvasPt.Y;
                if (dx * dx + dy * dy <= midsideDupTol * midsideDupTol)
                {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate)
                highlighted.Add(new MeshDiscretizationNodeDisplay { Location = canvasPt, IsHighlighted = true });
        }

        var rowByElementId = _mesh.MeshInformationRows
            .GroupBy(r => r.ElementId)
            .ToDictionary(g => g.Key, g => g.First());

        // Build inclined-zone polygons once per snapshot (instead of per element) so canvas fills don't
        // rebuild/reallocate the zone set thousands of times during a redraw.
        var nonHorizontalZones = MeshElementMaterialFillResolver.BuildNonHorizontalFillZones(_geometry);

        var allElements = new List<MeshElementDisplay>();
        foreach (var el in _mesh.MeshElements)
        {
            rowByElementId.TryGetValue(el.DisplayId, out var infoRow);
            var canvasEl = MeshCanvasDisplayConverter.ToCanvasElement(el, infoRow, _geometry.ConvertAxisToCanvas);
            int etype = infoRow?.Etype ?? 0;
            var fill = MeshElementMaterialFillResolver.ResolveFillBrushForElement(
                infoRow, el.CenterY, etype, _materialProperties, _geometry, nonHorizontalZones);
            if (fill != null)
                canvasEl.Fill = fill;
            var (labelFontSize, labelWidth) = ComputeElementLabelMetrics(el.DisplayId);
            canvasEl.LabelFontSize = labelFontSize;
            canvasEl.LabelWidth = labelWidth;
            canvasEl.LabelOffsetX = -labelWidth * 0.5;
            canvasEl.LabelOffsetY = -labelFontSize * 0.6;
            allElements.Add(canvasEl);
        }

        return new MeshCanvasConversionSnapshot
        {
            ConvertedDiscretizationNodes = nodes,
            HighlightedMeshDiscretizationNodes = highlighted,
            MeshNodeDisplayItems = nodeItems,
            AllMeshElements = allElements
        };
    }

    private void ApplySnapshot(MeshCanvasConversionSnapshot snapshot)
    {
        _convertedDiscretizationNodes.Clear();
        foreach (var n in snapshot.ConvertedDiscretizationNodes)
            _convertedDiscretizationNodes.Add(n);

        _highlightedMeshDiscretizationNodes.Clear();
        foreach (var n in snapshot.HighlightedMeshDiscretizationNodes)
            _highlightedMeshDiscretizationNodes.Add(n);

        _meshNodeDisplayItems.Clear();
        foreach (var n in snapshot.MeshNodeDisplayItems)
            _meshNodeDisplayItems.Add(n);

        _convertedMeshElements.Clear();
        foreach (var el in snapshot.AllMeshElements)
            _convertedMeshElements.Add(el);

        _spatialIndex = new MeshElementSpatialIndex(_convertedMeshElements);
        NotifyConvertedMeshChanged();
    }

    private void NotifyConvertedMeshChanged()
    {
        OnPropertyChanged(nameof(MeshDiscretizationNodes));
        OnPropertyChanged(nameof(HighlightedMeshDiscretizationNodes));
        OnPropertyChanged(nameof(ShowHighlightedMeshNodeOverlay));
        OnPropertyChanged(nameof(MeshElements));
        OnPropertyChanged(nameof(MeshNodeDisplayItems));
        OnPropertyChanged(nameof(MeshNodeLabelItems));
        OnPropertyChanged(nameof(MeshElementLabelItems));
        OnPropertyChanged(nameof(ShowMeshNodes));
        OnPropertyChanged(nameof(ShowMeshElements));
        OnPropertyChanged(nameof(ShowMeshDiscretizationMarkers));
    }

    private static bool GeometryAxisOrCanvasChanged(string? propertyName) =>
        string.IsNullOrEmpty(propertyName)
        || propertyName is nameof(GeometryDefinitionViewModel.AxisXMin)
            or nameof(GeometryDefinitionViewModel.AxisXMax)
            or nameof(GeometryDefinitionViewModel.AxisYMin)
            or nameof(GeometryDefinitionViewModel.AxisYMax)
            or nameof(GeometryDefinitionViewModel.CanvasWidth)
            or nameof(GeometryDefinitionViewModel.CanvasHeight);

    // ---- Geometry (forward to _geometry) ----
    public double CanvasWidth => _geometry.CanvasWidth;
    public double CanvasHeight => _geometry.CanvasHeight;
    public ObservableCollection<AxisTickInfo> XAxisTicks => _geometry.XAxisTicks;
    public ObservableCollection<AxisTickInfo> XAxisMinorTicks => _geometry.XAxisMinorTicks;
    public ObservableCollection<AxisTickInfo> YAxisTicks => _geometry.YAxisTicks;
    public ObservableCollection<AxisTickInfo> YAxisMinorTicks => _geometry.YAxisMinorTicks;
    public PointCollection GeometryPathPoints => _geometry.GeometryPathPoints;
    public ObservableCollection<InternalEdgeCanvasLine> InternalEdgeCanvasLines => _geometry.InternalEdgeCanvasLines;
    public ObservableCollection<InternalEdgeCanvasLine> HorizontalLayerCanvasLines => _geometry.HorizontalLayerCanvasLines;
    /// <summary>Teal interface lines for non-horizontal material layers, mirrored from the Material
    /// Properties canvas so inclined layers drawn there are also visible on the Mesh canvas.</summary>
    public ObservableCollection<InternalEdgeCanvasLine> NonHorizontalLayerCanvasLines => _geometry.NonHorizontalLayerCanvasLines;
    /// <summary>Always show material-layer cut/interface lines on the Mesh canvas (the geometry VM
    /// gates these to the Material Properties tab; the Mesh tab opts in explicitly).</summary>
    public bool ShowMaterialLayerCutLinesOnCanvas => true;
    public PointCollection HighlightedSegmentPoints => _geometry.HighlightedSegmentPoints;
    public ObservableCollection<SurfaceRegionOverlayItem> SurfaceRegionOverlays => _geometry.SurfaceRegionOverlays;
    public ObservableCollection<MaterialLayerDisplay> Layers => _geometry.Layers;
    public ObservableCollection<PointCollection> ExcavationPolygons => _geometry.ExcavationPolygons;
    public ObservableCollection<StageExcavationZone> ExcavatedStageZones => _geometry.ExcavatedStageZones;
    public ObservableCollection<PointCollection> RegionBPolygons => _geometry.RegionBPolygons;
    public ObservableCollection<PointCollection> UnionFillPolygons => _geometry.UnionFillPolygons;
    public ObservableCollection<GeometryDefinitionViewModel.GeometryVertexDisplay> GeometryVertices => _geometry.GeometryVertices;
    public ObservableCollection<object> CirclePreviews => _geometry.CirclePreviews;
    public double TunnelCirclePreviewCenterX => _geometry.TunnelCirclePreviewCenterX;
    public double TunnelCirclePreviewCenterY => _geometry.TunnelCirclePreviewCenterY;
    public double TunnelCirclePreviewDiameter => _geometry.TunnelCirclePreviewDiameter;
    public double TunnelCirclePreviewNegativeRadius => _geometry.TunnelCirclePreviewNegativeRadius;
    public bool IsTunnelCirclePreviewVisible => _geometry.IsTunnelCirclePreviewVisible;
    public string TunnelCirclePreviewRadiusText => _geometry.TunnelCirclePreviewRadiusText;
    public double DrawCirclePreviewCenterX => _geometry.DrawCirclePreviewCenterX;
    public double DrawCirclePreviewCenterY => _geometry.DrawCirclePreviewCenterY;
    public double DrawCirclePreviewDiameter => _geometry.DrawCirclePreviewDiameter;
    public double DrawCirclePreviewNegativeRadius => _geometry.DrawCirclePreviewNegativeRadius;
    public bool IsDrawCirclePreviewVisible => _geometry.IsDrawCirclePreviewVisible;
    public string DrawCirclePreviewRadiusText => _geometry.DrawCirclePreviewRadiusText;

    // Draw line / arc preview (same bindings as SlopeCanvasView on GeometryDefinitionVM)
    public double DrawLinePreviewX1 => _geometry.DrawLinePreviewX1;
    public double DrawLinePreviewY1 => _geometry.DrawLinePreviewY1;
    public double DrawLinePreviewX2 => _geometry.DrawLinePreviewX2;
    public double DrawLinePreviewY2 => _geometry.DrawLinePreviewY2;
    public bool IsDrawLinePreviewVisible => _geometry.IsDrawLinePreviewVisible;
    public PointCollection DrawArcPreviewPoints => _geometry.DrawArcPreviewPoints;
    public bool IsDrawArcPreviewVisible => _geometry.IsDrawArcPreviewVisible;
    public bool IsBulkPointDeleteSelectionVisible => _geometry.IsBulkPointDeleteSelectionVisible;
    public double BulkPointDeleteSelectionLeft => _geometry.BulkPointDeleteSelectionLeft;
    public double BulkPointDeleteSelectionTop => _geometry.BulkPointDeleteSelectionTop;
    public double BulkPointDeleteSelectionWidth => _geometry.BulkPointDeleteSelectionWidth;
    public double BulkPointDeleteSelectionHeight => _geometry.BulkPointDeleteSelectionHeight;

    /// <summary>BC Import Mode Vertices/Nodes: canvas marquee active.</summary>
    public bool IsBcManualImportMode => _bc?.IsBcManualImportMode == true;
    public bool IsBcImportSelectionVisible => _bc?.IsBcImportSelectionVisible == true;
    public double BcImportSelectionLeft => _bc?.BcImportSelectionLeft ?? 0;
    public double BcImportSelectionTop => _bc?.BcImportSelectionTop ?? 0;
    public double BcImportSelectionWidth => _bc?.BcImportSelectionWidth ?? 0;
    public double BcImportSelectionHeight => _bc?.BcImportSelectionHeight ?? 0;
    public BoundaryConditionsViewModel? BoundaryConditions => _bc;

    public ObservableCollection<TunnelExcavationCircle> FilteredTunnelExcavationCircles => _geometry.FilteredTunnelExcavationCircles;
    public Point BoxMethodCircleCenter => _geometry.BoxMethodCircleCenter;
    public ObservableCollection<PointCollection> BoxMethodCircleSegments => _geometry.BoxMethodCircleSegments;
    public bool IsBoxMethodCircleVisible => _geometry.IsBoxMethodCircleVisible;
    // Slip surface / slice UI (may not exist on geometry VM; expose empty so bindings don't fail)
    private static readonly ObservableCollection<object> EmptyObjectCollection = new();
    public ObservableCollection<object> SelectedSliceRegions => EmptyObjectCollection;
    public ObservableCollection<object> CircleCandidateCenters => EmptyObjectCollection;
    public ObservableCollection<object> ColoredCenterMarkers => EmptyObjectCollection;
    public ObservableCollection<object> SlipCircleSlices => EmptyObjectCollection;
    public ObservableCollection<object> BoxMethodSliceLabels => EmptyObjectCollection;
    public ObservableCollection<GroundwaterLine> GroundwaterLines => _geometry.GroundwaterLines;
    public ObservableCollection<GroundwaterSurfPointMarker> GroundwaterSurfPointMarkers => _geometry.GroundwaterSurfPointMarkers;
    public ObservableCollection<GroundwaterZoneOverlay> GroundwaterZoneOverlays => _geometry.GroundwaterZoneOverlays;
    public ObservableCollection<PondedWaterOverlay> PondedWaterOverlays => _geometry.PondedWaterOverlays;
    public ObservableCollection<PondedWaterIsobarSegment> PondedWaterIsobars => _geometry.PondedWaterIsobars;
    public ObservableCollection<PondedWaterCallout> PondedWaterCallouts => _geometry.PondedWaterCallouts;
    public ObservableCollection<LineLoadOverlayDisplay> PondedFacePressureOverlays => _geometry.PondedFacePressureOverlays;
    public PointCollection GroundwaterPhreaticPolyline => _geometry.GroundwaterPhreaticPolyline;
    public PointCollection PondedWaterLevelPolyline => _geometry.PondedWaterLevelPolyline;
    public bool IsGroundwaterEnabled => _geometry.IsGroundwaterEnabled;
    public bool IsGroundwaterPhreaticVisible => _geometry.IsGroundwaterPhreaticVisible;
    public bool IsGroundwaterZoneOverlayVisible => _geometry.IsGroundwaterZoneOverlayVisible;
    public bool IsPondedFreeWaterOverlayVisible => _geometry.IsPondedFreeWaterOverlayVisible;
    public bool IsPondedHeadIsobarsVisible => _geometry.IsPondedHeadIsobarsVisible;
    public bool IsPondedFacePressureArrowsVisible => _geometry.IsPondedFacePressureArrowsVisible;

    // ---- Mesh (from _mesh, converted to canvas coords) ----
    public ObservableCollection<MeshDiscretizationNodeDisplay> MeshDiscretizationNodes => _convertedDiscretizationNodes;
    /// <summary>Selected user-defined / table-picked nodes (visible even when mesh node markers are off).</summary>
    public ObservableCollection<MeshDiscretizationNodeDisplay> HighlightedMeshDiscretizationNodes => _highlightedMeshDiscretizationNodes;
    public bool ShowHighlightedMeshNodeOverlay => _highlightedMeshDiscretizationNodes.Count > 0;
    public ObservableCollection<MeshElementDisplay> MeshElements => _convertedMeshElements;
    public ObservableCollection<MeshNodeDisplayItem> MeshNodeDisplayItems => _meshNodeDisplayItems;
    public ObservableCollection<MeshNodeDisplayItem> MeshNodeLabelItems =>
        ShowMeshNodes ? _meshNodeDisplayItems : EmptyMeshNodeLabels;
    public ObservableCollection<MeshElementDisplay> MeshElementLabelItems =>
        ShowMeshElements ? _convertedMeshElements : EmptyMeshElementLabels;
    public bool ShowMeshNodes => _mesh.IsDiscretized && _mesh.ShowMeshNodes;
    public bool ShowMeshElements => _mesh.IsDiscretized && _mesh.ShowMeshElements;

    /// <summary>
    /// Dims material-layer fills when a mesh is on the canvas so black element outlines and blue IDs stand out.
    /// Geometry Definition tab has no binding source and uses <see cref="SlopeCanvasView"/> FallbackValue instead.
    /// </summary>
    public double MaterialLayerOverlayOpacity => _mesh.IsDiscretized ? 0.14 : 0.65;

    /// <summary>
    /// Hides "Layer N" strip labels while element numbers are shown — avoids mistaking them for mesh IDs.
    /// Materials View Options can also turn labels off via <see cref="GeometryDefinitionViewModel.ShowMaterialLayerLabelsOnCanvas"/>.
    /// </summary>
    public bool ShowMaterialLayerLabelsOnCanvas =>
        _geometry.ShowMaterialLayerLabelsOnCanvas
        && !(_mesh.IsDiscretized && _mesh.ShowMeshElements);

    public bool IsExcavateElementsMode => _mesh.IsExcavateElementsMode;
    public string ExcavateElementsButtonLabel => _mesh.ExcavateElementsButtonLabel;
    public string ExcavateElementsStatusMessage => _mesh.ExcavateElementsStatusMessage;

    /// <summary>
    /// Inverse of the world transform's zoom factor. Bound to a ScaleTransform on every
    /// element/node label so labels stay constant on-screen pixel size at any zoom and
    /// stop overlapping when the user zooms tight on excavations or refined regions.
    /// Clamped to <c>[0.05, 1]</c> so labels neither balloon when zoomed-out nor become
    /// invisible at extreme zoom-in.
    /// </summary>
    public double MeshLabelInverseScale
    {
        get
        {
            var zoom = _geometry.CurrentZoomFactor;
            if (!double.IsFinite(zoom) || zoom <= 1e-3) zoom = 1.0;
            var inv = 1.0 / zoom;
            if (inv < 0.05) inv = 0.05;
            if (inv > 1.0) inv = 1.0;
            return inv;
        }
    }
    /// <summary>Red boundary crosses (RS2D discretize preview); hidden after full mesh to avoid thousands of markers.</summary>
    public bool ShowMeshDiscretizationMarkers =>
        _mesh.ShowBoundaryDiscretizationMarkers && _mesh.IsDiscretized && _mesh.DiscretizationNodes.Count > 0;
    public ObservableCollection<object> BoundaryEdgeNodes => _mesh.BoundaryEdgeNodes;

    // ── Boundary Conditions overlay (canvas-pixel space) ───────────────────────
    /// <summary>RS2-style BC glyphs at node locations (one entry per BC row).</summary>
    public ObservableCollection<BcCanvasMarker> BcCanvasMarkers => _bcCanvasMarkers;
    /// <summary>True when the BC popup's Show Marker toggle is on. Drives Visibility on the marker overlays.</summary>
    public bool ShowBcMarkers => _bc?.MarkersVisible == true;

    public ObservableCollection<LineLoadOverlayDisplay> LineLoadOverlays => _geometry.LineLoadOverlays;
    public ObservableCollection<PointLoadOverlayDisplay> PointLoadOverlays => _geometry.PointLoadOverlays;
    public bool ShowLineLoadOverlaysOnCanvas => _geometry.ShowLineLoadOverlaysOnCanvas;
    public bool ShowPointLoadOverlaysOnCanvas => _geometry.ShowPointLoadOverlaysOnCanvas;
    public bool ShowGeometryPointsOnCanvas => _geometry.ShowGeometryPointsOnCanvas;
    public bool ShowGeometrySurfaceFillsOnCanvas => _geometry.ShowGeometrySurfaceFillsOnCanvas;
    public bool ShowGeometryLabelsOnCanvas => _geometry.ShowGeometryLabelsOnCanvas;

    public bool ShowGrid => _geometry.ShowGrid;
    public bool ShowInternalEdgeLinesOnCanvas => _geometry.ShowInternalEdgeLinesOnCanvas;
    public bool ShowBoundaryPolylineOutline => _geometry.ShowBoundaryPolylineOutline;
    public bool ShowAxes => _geometry.ShowAxes;
    public double OriginAxisOriginCanvasX => _geometry.OriginAxisOriginCanvasX;
    public double OriginAxisOriginCanvasY => _geometry.OriginAxisOriginCanvasY;
    public double OriginAxisArrowLengthPx => _geometry.OriginAxisArrowLengthPx;
    public double OriginAxisYTipOffset => _geometry.OriginAxisYTipOffset;

    public bool ShowAxisOfSymmetry => _geometry.ShowAxisOfSymmetry;
    public double AxisOfSymmetryX1 => _geometry.AxisOfSymmetryX1;
    public double AxisOfSymmetryY1 => _geometry.AxisOfSymmetryY1;
    public double AxisOfSymmetryX2 => _geometry.AxisOfSymmetryX2;
    public double AxisOfSymmetryY2 => _geometry.AxisOfSymmetryY2;
    public double AxisOfSymmetryLabelX => _geometry.AxisOfSymmetryLabelX;
    public double AxisOfSymmetryLabelY => _geometry.AxisOfSymmetryLabelY;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Links Material Properties so mesh element fills track library swatch colors.</summary>
    public void BindMaterialProperties(MaterialPropertiesViewModel? materialProperties)
    {
        if (ReferenceEquals(_materialProperties, materialProperties))
            return;

        UnhookMaterialColourSync();
        _materialProperties = materialProperties;
        if (_materialProperties == null)
            return;

        _materialProperties.Materials.CollectionChanged += OnMaterialsCollectionChanged;
        foreach (var material in _materialProperties.Materials)
            HookMaterialColourSync(material);
    }

    private void OnMaterialsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (MaterialModel material in e.OldItems)
                UnhookMaterialColourSync(material);
        }

        if (e.NewItems != null)
        {
            foreach (MaterialModel material in e.NewItems)
                HookMaterialColourSync(material);
        }

        MeshCanvasConversionCache.InvalidateMesh(MeshInstanceKey);
        RefreshConvertedMesh();
    }

    private void HookMaterialColourSync(MaterialModel material)
    {
        if (!_materialColourHooked.Add(material))
            return;
        material.PropertyChanged += OnMaterialColourChanged;
    }

    private void UnhookMaterialColourSync(MaterialModel material)
    {
        if (!_materialColourHooked.Remove(material))
            return;
        material.PropertyChanged -= OnMaterialColourChanged;
    }

    private void UnhookMaterialColourSync()
    {
        if (_materialProperties != null)
            _materialProperties.Materials.CollectionChanged -= OnMaterialsCollectionChanged;
        foreach (var material in _materialColourHooked.ToList())
            UnhookMaterialColourSync(material);
    }

    private void OnMaterialColourChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaterialModel.Colour) or nameof(MaterialModel.ColourBrush))
        {
            MeshCanvasConversionCache.InvalidateMesh(MeshInstanceKey);
            RefreshConvertedMesh();
        }
    }

    /// <summary>
    /// Mesh-canvas click handler used by SlopeCanvasView in "Excavate Elements" mode.
    /// Returns true when a mesh element was hit-tested and excavation was committed.
    /// </summary>
    public bool TryExcavateElementAtPoint(Point modelCanvasPoint)
    {
        if (!IsExcavateElementsMode || !_mesh.IsDiscretized || _convertedMeshElements.Count == 0)
        {
            _mesh.SetExcavateElementsStatusMessage("Element click ignored: enable Excavate Elements mode first.");
            return false;
        }

        MeshElementDisplay? hit = null;
        bool hitInactive = false;
        if (_spatialIndex != null)
        {
            hit = _spatialIndex.TryHit(modelCanvasPoint, out hitInactive);
        }
        else
        {
            foreach (var el in _convertedMeshElements)
            {
                if (el.Nodes == null || el.Nodes.Count < 3)
                    continue;
                if (!PointInPolygonModelCanvas(modelCanvasPoint, el.Nodes))
                    continue;
                if (el.IsActive) hit = el;
                else hitInactive = true;
            }
        }

        if (hit == null)
        {
            _mesh.SetExcavateElementsStatusMessage(hitInactive
                ? "Clicked element is already excavated/inactive."
                : "No active mesh element found at clicked location.");
            return false;
        }
        return _mesh.TryExcavateElementById(hit.DisplayId);
    }

    private static (double FontSize, double LabelWidth) ComputeElementLabelMetrics(int displayId)
    {
        // Fixed canvas size matching GeometryVertices PointNumber (7) plus a one-step bump
        // so element IDs stay small, unboxed, and similar to the geometry screenshot.
        const double font = 8.0;
        var digits = displayId > 0 ? displayId.ToString().Length : 1;
        double width = System.Math.Max(10.0, digits * font * 0.62);
        return (font, width);
    }

    private static bool PointInPolygonModelCanvas(Point p, PointCollection poly)
    {
        int n = poly.Count;
        if (n < 3) return false;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = poly[i].X, yi = poly[i].Y;
            double xj = poly[j].X, yj = poly[j].Y;
            if (System.Math.Abs(yj - yi) < 1e-12)
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

    /// <summary>Uniform grid index for O(1) average mesh element hit-testing.</summary>
    private sealed class MeshElementSpatialIndex
    {
        private readonly List<MeshElementDisplay> _elements;
        private readonly double _cellSize;
        private readonly Dictionary<long, List<int>> _cells = new();

        public MeshElementSpatialIndex(IReadOnlyList<MeshElementDisplay> elements)
        {
            _elements = elements.ToList();
            _cellSize = ComputeCellSize(_elements);
            for (int i = 0; i < _elements.Count; i++)
                RegisterElement(i);
        }

        public MeshElementDisplay? TryHit(Point p, out bool hitInactive)
        {
            hitInactive = false;
            MeshElementDisplay? hit = null;
            var (cx, cy) = CellOf(p);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!_cells.TryGetValue(Pack(cx + dx, cy + dy), out var ids))
                        continue;
                    foreach (var id in ids)
                    {
                        var el = _elements[id];
                        if (el.Nodes == null || el.Nodes.Count < 3)
                            continue;
                        if (!PointInPolygonModelCanvas(p, el.Nodes))
                            continue;
                        if (el.IsActive) hit = el;
                        else hitInactive = true;
                    }
                }
            }
            return hit;
        }

        private void RegisterElement(int index)
        {
            var el = _elements[index];
            if (el.Nodes == null || el.Nodes.Count == 0)
                return;
            double minX = el.Nodes[0].X, maxX = el.Nodes[0].X;
            double minY = el.Nodes[0].Y, maxY = el.Nodes[0].Y;
            foreach (var pt in el.Nodes)
            {
                if (pt.X < minX) minX = pt.X;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.Y > maxY) maxY = pt.Y;
            }
            var minCell = CellOf(new Point(minX, minY));
            var maxCell = CellOf(new Point(maxX, maxY));
            for (int cx = minCell.cx; cx <= maxCell.cx; cx++)
            {
                for (int cy = minCell.cy; cy <= maxCell.cy; cy++)
                {
                    var key = Pack(cx, cy);
                    if (!_cells.TryGetValue(key, out var list))
                    {
                        list = new List<int>();
                        _cells[key] = list;
                    }
                    list.Add(index);
                }
            }
        }

        private (int cx, int cy) CellOf(Point p)
            => ((int)Math.Floor(p.X / _cellSize), (int)Math.Floor(p.Y / _cellSize));

        private long Pack(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;

        private static double ComputeCellSize(IReadOnlyList<MeshElementDisplay> elements)
        {
            if (elements.Count == 0) return 32.0;
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var el in elements)
            {
                if (el.Nodes == null) continue;
                foreach (var pt in el.Nodes)
                {
                    if (pt.X < minX) minX = pt.X;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }
            }
            var span = Math.Max(maxX - minX, maxY - minY);
            if (!double.IsFinite(span) || span <= 0) return 32.0;
            var targetCells = Math.Max(8, Math.Sqrt(elements.Count));
            return Math.Clamp(span / targetCells, 8.0, 256.0);
        }
    }
}
