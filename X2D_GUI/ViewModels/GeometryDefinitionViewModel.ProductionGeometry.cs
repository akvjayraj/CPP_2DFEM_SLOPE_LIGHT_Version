using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services.Geometry;

namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// Production geometry kernel, geological features, true booleans, heal/CAD tools, and interchange hooks.
/// </summary>
public partial class GeometryDefinitionViewModel
{
    private GeologicalFeatureKind _drawGeologicalKind = GeologicalFeatureKind.WeakPlane;
    private GeologicalFeature? _activeGeologicalDraft;
    private readonly List<Point> _geologicalDraftPoints = new();
    private GeometryCadEditService.GeometryClipboard? _geometryClipboard;
    private JointWizardDraftProperties? _pendingJointProperties;

    public ObservableCollection<GeologicalFeature> GeologicalFeatures { get; } = new();
    public ObservableCollection<FeJointPropertySet> JointPropertySets { get; } = new();
    public ObservableCollection<FeJointNetworkDefinition> JointNetworks { get; } = new();
    public ObservableCollection<KernelBooleanOperation> KernelBooleanHistory { get; } = new();
    public ObservableCollection<GeologicalFeatureOverlayItem> GeologicalFeatureOverlays { get; } = new();
    public ObservableCollection<GeologicalDraftMarker> GeologicalDraftMarkers { get; } = new();

    public PointCollection GeologicalDraftPreviewPoints { get; private set; } = new();
    public bool IsGeologicalDraftPreviewVisible =>
        IsDrawingGeologicalFeature && GeologicalDraftPreviewPoints.Count >= 1;

    public ICommand StartDrawWeakPlaneCommand { get; private set; } = null!;
    public ICommand StartDrawBeddingCommand { get; private set; } = null!;
    public ICommand StartDrawTensionCrackCommand { get; private set; } = null!;
    public ICommand FinishGeologicalFeatureCommand { get; private set; } = null!;
    public ICommand CancelGeologicalFeatureCommand { get; private set; } = null!;
    public ICommand HealGeometryCommand { get; private set; } = null!;
    public ICommand CommitKernelBooleansCommand { get; private set; } = null!;
    public ICommand CopyGeometryCommand { get; private set; } = null!;
    public ICommand PasteGeometryCommand { get; private set; } = null!;

    public bool IsDrawingGeologicalFeature => _activeGeologicalDraft != null;

    public string GeologicalDrawStatus
    {
        get
        {
            if (_activeGeologicalDraft == null) return "";
            return $"Drawing {_activeGeologicalDraft.Kind}: {_geologicalDraftPoints.Count} point(s). Click to add; Geometry → Finish Geological Feature to commit.";
        }
    }

    private void InitProductionGeometryCommands()
    {
        StartDrawWeakPlaneCommand = new RelayCommand(() => BeginGeologicalDraw(GeologicalFeatureKind.WeakPlane));
        StartDrawBeddingCommand = new RelayCommand(() => BeginGeologicalDraw(GeologicalFeatureKind.Bedding));
        StartDrawTensionCrackCommand = new RelayCommand(() => BeginGeologicalDraw(GeologicalFeatureKind.TensionCrack));
        FinishGeologicalFeatureCommand = new RelayCommand(FinishGeologicalDraw, () => _geologicalDraftPoints.Count >= 2);
        CancelGeologicalFeatureCommand = new RelayCommand(() =>
        {
            if (_activeGeologicalDraft != null)
                GeometryImportStatusMessage = "Geological feature drawing cancelled.";
            CancelGeologicalDraw();
        });
        HealGeometryCommand = new RelayCommand(() => HealGeometry(tolerance: 0.05));
        CommitKernelBooleansCommand = new RelayCommand(CommitPendingKernelBooleansFromExcavationHistory);
        CopyGeometryCommand = new RelayCommand(CopyGeometryToClipboard);
        PasteGeometryCommand = new RelayCommand(PasteGeometryFromClipboard, () => _geometryClipboard != null);
        EnsureDefaultJointPropertySet();
    }

    public void EnsureDefaultJointPropertySet()
    {
        if (JointPropertySets.Count == 0)
            JointPropertySets.Add(FeJointPropertySet.CreateDefault(1));
    }

    public void ReplaceJointPropertySets(IEnumerable<FeJointPropertySet> sets)
    {
        JointPropertySets.Clear();
        if (sets != null)
        {
            foreach (var s in sets)
                JointPropertySets.Add(s);
        }
        EnsureDefaultJointPropertySet();
        RefreshGeologicalOverlays();
    }

    public FeJointPropertySet? FindJointPropertySet(int? id)
        => id is int i ? JointPropertySets.FirstOrDefault(s => s.Id == i) : null;

    /// <summary>
    /// True when a joint is in play and its property-set slip criterion is Mohr–Coulomb:
    /// an enabled geological feature with at least two vertices whose set resolves to
    /// <see cref="FeJointSlipCriterion.MohrCoulomb"/>, or generated interface elements
    /// with the default / first joint property set still Mohr–Coulomb.
    /// </summary>
    public bool HasMohrCoulombJointSlip(IReadOnlyCollection<FeJointInterfaceElement>? jointInterfaces = null)
    {
        foreach (var feature in GeologicalFeatures)
        {
            if (!feature.IsEnabled || feature.Vertices.Count < 2)
                continue;
            var set = FindJointPropertySet(feature.PropertySetId) ?? JointPropertySets.FirstOrDefault();
            if (set?.Criterion == FeJointSlipCriterion.MohrCoulomb)
                return true;
        }

        if (jointInterfaces is { Count: > 0 })
        {
            var fallback = JointPropertySets.FirstOrDefault();
            if (fallback?.Criterion == FeJointSlipCriterion.MohrCoulomb)
                return true;
        }

        return false;
    }

    private void CopyGeometryToClipboard()
    {
        _geometryClipboard = CopyGeometryClipboard();
        GeometryImportStatusMessage = "Geometry copied.";
        (PasteGeometryCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void PasteGeometryFromClipboard()
    {
        if (_geometryClipboard == null) return;
        PasteGeometryClipboard(_geometryClipboard, offsetX: 2, offsetY: 2);
        GeometryImportStatusMessage = "Geometry pasted.";
    }

    public PlanarGeometryKernel BuildPlanarKernel() => PlanarGeometryKernelBuilder.Build(this);

    public void BeginGeologicalDraw(GeologicalFeatureKind kind)
    {
        CancelBulkPointDeleteMode();
        CancelDrawLineMode();
        CancelDrawArcMode();
        CancelDrawCircleMode();
        IsMeasureMode = false;

        _drawGeologicalKind = kind;
        _geologicalDraftPoints.Clear();
        _activeGeologicalDraft = new GeologicalFeature
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = kind,
            Name = $"{kind} {GeologicalFeatures.Count(f => f.Kind == kind) + 1}"
        };
        GeometryImportStatusMessage = GeologicalDrawStatus;
        OnPropertyChanged(nameof(IsDrawingGeologicalFeature));
        OnPropertyChanged(nameof(GeologicalDrawStatus));
        RefreshGeologicalOverlays();
    }

    public void AddGeologicalDraftPoint(double axisX, double axisY)
    {
        if (_activeGeologicalDraft == null) return;
        var canvas = ConvertAxisToCanvas(new Point(axisX, axisY));
        var snappedCanvas = SnapCanvasPoint(canvas);
        var p = ConvertCanvasToAxis(snappedCanvas);
        _geologicalDraftPoints.Add(p);
        GeometryImportStatusMessage = GeologicalDrawStatus;
        OnPropertyChanged(nameof(GeologicalDrawStatus));
        (FinishGeologicalFeatureCommand as RelayCommand)?.RaiseCanExecuteChanged();
        RefreshGeologicalOverlays();
    }

    public JointWizardDraftProperties? PendingJointProperties
    {
        get => _pendingJointProperties;
        set => _pendingJointProperties = value;
    }

    public void FinishGeologicalDraw()
    {
        if (_activeGeologicalDraft == null || _geologicalDraftPoints.Count < 2) return;
        RecordUndo($"Add {_activeGeologicalDraft.Kind}");
        foreach (var p in _geologicalDraftPoints)
            _activeGeologicalDraft.Vertices.Add((p.X, p.Y));
        ApplyPendingJointProperties(_activeGeologicalDraft);
        GeologicalFeatures.Add(_activeGeologicalDraft);
        GeometryImportStatusMessage = $"Added {_activeGeologicalDraft.Name} ({_geologicalDraftPoints.Count} points).";
        CancelGeologicalDraw();
    }

    public void AddGeologicalFeature(GeologicalFeature feature)
    {
        if (feature == null || feature.Vertices.Count < 2) return;
        RecordUndo($"Add {feature.Kind}");
        GeologicalFeatures.Add(feature);
        GeometryImportStatusMessage = $"Added {feature.Name}.";
        RefreshGeologicalOverlays();
    }

    public void RemoveGeologicalFeature(GeologicalFeature feature)
    {
        if (feature == null || !GeologicalFeatures.Contains(feature)) return;
        RecordUndo($"Remove {feature.Kind}");
        GeologicalFeatures.Remove(feature);
        GeometryImportStatusMessage = $"Removed {feature.Name}.";
        RefreshGeologicalOverlays();
    }

    public void UpdateGeologicalFeature(GeologicalFeature target, GeologicalFeature source)
    {
        if (target == null || source == null || !GeologicalFeatures.Contains(target)) return;
        RecordUndo($"Update {target.Kind}");
        target.Kind = source.Kind;
        target.Name = source.Name;
        target.IsEnabled = source.IsEnabled;
        target.MaterialId = source.MaterialId;
        target.WaterElevation = source.WaterElevation;
        target.DipAngleDeg = source.DipAngleDeg;
        target.KnKpa = source.KnKpa;
        target.KsKpa = source.KsKpa;
        target.JointCohesionKpa = source.JointCohesionKpa;
        target.JointFrictionDeg = source.JointFrictionDeg;
        target.JointTensionCutoffKpa = source.JointTensionCutoffKpa;
        target.PropertySetId = source.PropertySetId;
        target.EndCondition = source.EndCondition;
        target.InstallStage = source.InstallStage;
        target.JointNetworkId = source.JointNetworkId;
        target.JointNetworkName = source.JointNetworkName;
        target.Vertices.Clear();
        foreach (var v in source.Vertices)
            target.Vertices.Add(v);
        GeometryImportStatusMessage = $"Updated {target.Name}.";
        RefreshGeologicalOverlays();
    }

    public void BeginGeologicalDrawFromWizard(JointWizardDraftProperties properties)
    {
        PendingJointProperties = properties;
        BeginGeologicalDraw(properties.Kind);
        if (!string.IsNullOrWhiteSpace(properties.Name) && _activeGeologicalDraft != null)
            _activeGeologicalDraft.Name = properties.Name;
        GeometryImportStatusMessage =
            $"Drawing {properties.Kind}: click points, then Geometry → Finish Geological Feature.";
    }

    private void ApplyPendingJointProperties(GeologicalFeature feature)
    {
        var p = _pendingJointProperties;
        _pendingJointProperties = null;
        if (p == null) return;
        feature.Kind = p.Kind;
        if (!string.IsNullOrWhiteSpace(p.Name))
            feature.Name = p.Name;
        feature.MaterialId = p.MaterialId;
        feature.DipAngleDeg = p.DipAngleDeg;
        feature.KnKpa = p.KnKpa;
        feature.KsKpa = p.KsKpa;
        feature.JointCohesionKpa = p.JointCohesionKpa;
        feature.JointFrictionDeg = p.JointFrictionDeg;
        feature.JointTensionCutoffKpa = p.JointTensionCutoffKpa;
        feature.WaterElevation = p.WaterElevation;
        feature.PropertySetId = p.PropertySetId;
        feature.EndCondition = p.EndCondition;
        feature.InstallStage = p.InstallStage;
    }

    public void CancelGeologicalDraw()
    {
        var wasDrawing = _activeGeologicalDraft != null;
        _activeGeologicalDraft = null;
        _geologicalDraftPoints.Clear();
        _pendingJointProperties = null;
        OnPropertyChanged(nameof(IsDrawingGeologicalFeature));
        OnPropertyChanged(nameof(GeologicalDrawStatus));
        (FinishGeologicalFeatureCommand as RelayCommand)?.RaiseCanExecuteChanged();
        RefreshGeologicalOverlays();
        if (wasDrawing && string.IsNullOrWhiteSpace(GeometryImportStatusMessage))
            GeometryImportStatusMessage = "";
    }

    public void HandleGeologicalFeatureCanvasClick(Point canvasPoint)
    {
        if (_activeGeologicalDraft == null) return;
        var snapped = SnapCanvasPoint(canvasPoint);
        var axis = ConvertCanvasToAxis(snapped);
        _geologicalDraftPoints.Add(axis);
        GeometryImportStatusMessage = GeologicalDrawStatus;
        OnPropertyChanged(nameof(GeologicalDrawStatus));
        (FinishGeologicalFeatureCommand as RelayCommand)?.RaiseCanExecuteChanged();
        RefreshGeologicalOverlays();
    }

    /// <summary>Rebuild canvas polylines/markers for draft + committed geological features.</summary>
    public void RefreshGeologicalOverlays()
    {
        var draftPts = new PointCollection();
        GeologicalDraftMarkers.Clear();
        foreach (var axis in _geologicalDraftPoints)
        {
            var canvas = ConvertAxisToCanvas(axis);
            draftPts.Add(canvas);
            GeologicalDraftMarkers.Add(new GeologicalDraftMarker { X = canvas.X, Y = canvas.Y });
        }

        GeologicalDraftPreviewPoints = draftPts;
        OnPropertyChanged(nameof(GeologicalDraftPreviewPoints));
        OnPropertyChanged(nameof(IsGeologicalDraftPreviewVisible));
        OnPropertyChanged(nameof(GeologicalDraftMarkers));

        GeologicalFeatureOverlays.Clear();
        foreach (var feature in GeologicalFeatures.Where(f => f.IsEnabled && f.Vertices.Count >= 2))
        {
            var pts = new PointCollection();
            foreach (var (x, y) in feature.Vertices)
                pts.Add(ConvertAxisToCanvas(new Point(x, y)));
            var mid = pts[pts.Count / 2];
            var set = FindJointPropertySet(feature.PropertySetId);
            GeologicalFeatureOverlays.Add(new GeologicalFeatureOverlayItem
            {
                Points = pts,
                Stroke = set != null
                    ? set.ColourBrush
                    : feature.Kind switch
                    {
                        GeologicalFeatureKind.WeakPlane => CreateFrozenBrush(0xB4, 0x53, 0x09),
                        GeologicalFeatureKind.Bedding => CreateFrozenBrush(0x7C, 0x3A, 0xED),
                        GeologicalFeatureKind.TensionCrack => CreateFrozenBrush(0xDC, 0x26, 0x26),
                        _ => Brushes.DarkOrange
                    },
                Name = feature.Name,
                LabelLeft = mid.X,
                LabelTop = mid.Y
            });
        }

        OnPropertyChanged(nameof(GeologicalFeatureOverlays));
    }

    /// <summary>Commit a true fill ring into the kernel history and create a soil face.</summary>
    public PlanarMapFace? CommitKernelFill(IReadOnlyList<Point> ring, int? materialId = null, string? label = null, int? stage = null)
    {
        if (ring == null || ring.Count < 3) return null;
        RecordUndo("Kernel fill");
        var op = new KernelBooleanOperation
        {
            Sequence = KernelBooleanHistory.Count,
            Kind = KernelBooleanKind.Fill,
            ResultMaterialId = materialId,
            StageNumber = stage,
            Label = label ?? "Fill"
        };
        foreach (var p in ring)
            op.FillRing.Add((p.X, p.Y));
        KernelBooleanHistory.Add(op);
        var kernel = BuildPlanarKernel();
        return KernelBooleanEngine.Apply(kernel, op);
    }

    /// <summary>Replays excavation boolean rows into kernel history (true topology commit).</summary>
    public void CommitPendingKernelBooleansFromExcavationHistory()
    {
        RecordUndo("Commit kernel booleans");
        KernelBooleanHistory.Clear();
        var seq = 0;
        foreach (var row in ExcavationOperations)
        {
            var kind = row.TypeEnum switch
            {
                BooleanOperationType.Union => KernelBooleanKind.Union,
                BooleanOperationType.Subtract => KernelBooleanKind.Subtract,
                BooleanOperationType.Intersect => KernelBooleanKind.Intersect,
                _ => KernelBooleanKind.Union
            };
            KernelBooleanHistory.Add(new KernelBooleanOperation
            {
                Sequence = seq++,
                Kind = kind,
                SubjectFaceId = row.RegionASurfaceNumber,
                ToolFaceId = row.RegionBSurfaceNumber,
                StageNumber = row.AppliedInStageNumber > 0 ? row.AppliedInStageNumber : null,
                Label = $"{kind} {row.RegionAName} / {row.RegionBName}"
            });
        }

        // Fill overlays become real fill faces.
        foreach (var fill in UnionFillPolygons)
        {
            if (fill == null || fill.Count < 3) continue;
            var op = new KernelBooleanOperation
            {
                Sequence = seq++,
                Kind = KernelBooleanKind.Fill,
                Label = "Fill"
            };
            foreach (var p in fill)
                op.FillRing.Add((p.X, p.Y));
            KernelBooleanHistory.Add(op);
        }

        _ = KernelBooleanEngine.RecomputeFromHistory(BuildPlanarKernel());
    }

    /// <summary>Snap near-coincident vertices and close small gaps on the outer boundary.</summary>
    public int HealGeometry(double tolerance = 0.05)
    {
        if (GeometryVertexRows.Count < 2) return 0;
        RecordUndo("Heal geometry");
        var merged = GeometryHealService.HealVertices(
            GeometryVertexRows.Select(v => (v.StablePointId, v.AxisX, v.AxisY)).ToList(),
            tolerance);
        var changed = 0;
        foreach (var (stableId, x, y) in merged)
        {
            var row = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == stableId);
            if (row == null) continue;
            if (Math.Abs(row.AxisX - x) > 1e-12 || Math.Abs(row.AxisY - y) > 1e-12)
            {
                row.AxisX = x;
                row.AxisY = y;
                changed++;
            }
        }

        RefreshGeometryPath();
        return changed;
    }

    public void UpsertJointNetwork(FeJointNetworkDefinition definition)
    {
        if (definition == null) return;
        var existing = JointNetworks.FirstOrDefault(n => n.Id == definition.Id);
        if (existing == null)
            JointNetworks.Add(definition);
        else if (!ReferenceEquals(existing, definition))
            existing.CopyFrom(definition);
    }

    public void RemoveJointNetwork(FeJointNetworkDefinition definition)
    {
        if (definition == null) return;
        ReplaceJointNetworkTraces(definition.Id, Array.Empty<GeologicalFeature>());
        JointNetworks.Remove(definition);
    }

    public void ReplaceJointNetworkTraces(string networkId, IReadOnlyList<GeologicalFeature> traces)
    {
        if (string.IsNullOrWhiteSpace(networkId)) return;
        RecordUndo("Update joint network");
        FeJointNetworkGenerator.ReplaceNetworkFeatures(GeologicalFeatures, networkId, traces);
        RefreshGeologicalOverlays();
    }

    public void RestoreJointNetworks(IEnumerable<FeJointNetworkDefinition>? networks)
    {
        JointNetworks.Clear();
        if (networks == null) return;
        foreach (var n in networks)
            JointNetworks.Add(n);
    }

    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> ResolveJointNetworkRegions(IEnumerable<int> surfaceNumbers)
    {
        var result = new List<IReadOnlyList<(double X, double Y)>>();
        var nums = surfaceNumbers?.ToList() ?? new List<int>();
        if (nums.Count == 0 || (nums.Count == 1 && nums[0] == 0))
        {
            if (TryGetClosedBoundaryAxisPolygon(out var poly) && poly.Count >= 3)
                result.Add(poly.Select(p => (p.X, p.Y)).ToList());
            return result;
        }

        foreach (var n in nums.Distinct())
        {
            if (n == 0)
            {
                if (TryGetClosedBoundaryAxisPolygon(out var poly) && poly.Count >= 3)
                    result.Add(poly.Select(p => (p.X, p.Y)).ToList());
                continue;
            }

            if (TryGetSurfaceAxisPolygon(n, out var ring) && ring.Count >= 3)
                result.Add(ring.Select(p => (p.X, p.Y)).ToList());
        }

        return result;
    }

    public void RestoreGeologicalFeatures(IEnumerable<GeologicalFeature>? features)
    {
        GeologicalFeatures.Clear();
        if (features != null)
        {
            foreach (var f in features)
                GeologicalFeatures.Add(f);
        }

        RefreshGeologicalOverlays();
    }

    public void RestoreKernelBooleanHistory(IEnumerable<KernelBooleanOperation>? ops)
    {
        KernelBooleanHistory.Clear();
        if (ops == null) return;
        foreach (var op in ops.OrderBy(o => o.Sequence))
            KernelBooleanHistory.Add(op);
    }

    public List<GeometryVerifier.Issue> VerifyProductionGeometry() => GeometryVerifier.Verify(this);

    /// <summary>Multi-select move of geometry vertices (CAD transform).</summary>
    public void TransformSelectedVertices(
        IReadOnlyCollection<int> selectedStableIds,
        double dx,
        double dy)
    {
        if (selectedStableIds == null || selectedStableIds.Count == 0) return;
        RecordUndo("Move selected vertices");
        var updated = GeometryCadEditService.Move(
            GeometryVertexRows.Select(v => (v.StablePointId, v.AxisX, v.AxisY)),
            selectedStableIds,
            dx,
            dy);
        ApplyTransformedVertices(updated);
    }

    public void RotateSelectedVertices(
        IReadOnlyCollection<int> selectedStableIds,
        double pivotX,
        double pivotY,
        double angleDeg)
    {
        if (selectedStableIds == null || selectedStableIds.Count == 0) return;
        RecordUndo("Rotate selected vertices");
        var updated = GeometryCadEditService.Rotate(
            GeometryVertexRows.Select(v => (v.StablePointId, v.AxisX, v.AxisY)),
            selectedStableIds,
            pivotX,
            pivotY,
            angleDeg);
        ApplyTransformedVertices(updated);
    }

    public void ScaleSelectedVertices(
        IReadOnlyCollection<int> selectedStableIds,
        double pivotX,
        double pivotY,
        double scale)
    {
        if (selectedStableIds == null || selectedStableIds.Count == 0) return;
        RecordUndo("Scale selected vertices");
        var updated = GeometryCadEditService.Scale(
            GeometryVertexRows.Select(v => (v.StablePointId, v.AxisX, v.AxisY)),
            selectedStableIds,
            pivotX,
            pivotY,
            scale);
        ApplyTransformedVertices(updated);
    }

    public GeometryCadEditService.GeometryClipboard CopyGeometryClipboard()
    {
        var clip = new GeometryCadEditService.GeometryClipboard();
        if (TryGetClosedBoundaryAxisPolygon(out var poly))
        {
            foreach (var p in poly)
                clip.Boundary.Add((p.X, p.Y));
        }

        foreach (var feature in GeologicalFeatures)
        {
            clip.Features.Add((feature.Kind.ToString(), feature.Vertices.ToList(), feature.MaterialId));
        }

        return clip;
    }

    public void PasteGeometryClipboard(GeometryCadEditService.GeometryClipboard clip, double offsetX = 0, double offsetY = 0)
    {
        if (clip == null) return;
        RecordUndo("Paste geometry");
        foreach (var (kindName, verts, materialId) in clip.Features)
        {
            if (!Enum.TryParse<GeologicalFeatureKind>(kindName, true, out var kind))
                kind = GeologicalFeatureKind.WeakPlane;
            var feature = new GeologicalFeature
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = kind,
                Name = $"{kind} (paste)",
                MaterialId = materialId
            };
            foreach (var (x, y) in verts)
                feature.Vertices.Add((x + offsetX, y + offsetY));
            GeologicalFeatures.Add(feature);
        }

        RefreshGeologicalOverlays();
    }

    private void ApplyTransformedVertices(IReadOnlyList<(int StableId, double X, double Y)> updated)
    {
        foreach (var (stableId, x, y) in updated)
        {
            var row = GeometryVertexRows.FirstOrDefault(v => v.StablePointId == stableId);
            if (row == null) continue;
            row.AxisX = x;
            row.AxisY = y;
        }

        RefreshGeometryPath();
    }

    private static Brush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
