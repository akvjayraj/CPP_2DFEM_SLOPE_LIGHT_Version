using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Services;

namespace GeotechSlopeApp.ViewModels;

public class BoundaryConditionsViewModel : INotifyPropertyChanged
{
    public const string ImportModeBoundary = "Boundary";
    public const string ImportModeVertices = "Vertices";
    public const string ImportModeNodes = "Nodes";

    private static readonly string[] StructuralBcTypes =
    {
        "Fixed", "Roller X", "Roller Y", "Prescribed Displacement", "Force"
    };

    private static readonly string[] SeepageBcTypes =
    {
        "Total Head (H)",
        "Zero Pressure (P=0)",
        "Nodal Flow Rate (Q)",
        "Normal Infiltration (q)",
        "Vertical Infiltration (q)",
        "Unknown (P=0 or Q=0)",
        "None / Remove BC",
        "Pressure Head",
        "Pore Pressure",
        "Constant"
    };

    private string _statusMessage = "Choose Import Mode: Boundary (import L1..Ln), or Vertices/Nodes (drag-select on the canvas), then Apply BC.";
    private OuterBoundaryInfo? _selectedOuterBoundary;
    private string _nodesText = "";
    private string? _selectedBCTypeForBoundary = "Fixed";
    private string _bcValueForBoundary = "0";
    private object? _selectedBoundaryCondition;
    private string? _selectedImportMode;
    private MaterialPropertiesViewModel? _materialPropertiesVM;
    private ProjectSettingsViewModel? _projectSettingsVM;
    private IProjectUndoHost? _undoHost;

    public void SetUndoHost(IProjectUndoHost? host) => _undoHost = host;
    private void RecordUndo(string description) => _undoHost?.RecordUndoPoint(description);

    public BoundaryConditionsViewModel()
    {
        ImportOuterBoundariesCommand = new RelayCommand(OnImportOuterBoundaries);
        ApplyBCToSelectedBoundaryCommand = new RelayCommand(OnApplyBCToSelectedBoundary, () => SelectedOuterBoundary != null || !string.IsNullOrWhiteSpace(NodesText));
        AddBoundaryConditionCommand = new RelayCommand(OnAddBoundaryCondition);
        ClearBoundaryConditionsCommand = new RelayCommand(OnClearBoundaryConditions);
        DeleteBoundaryConditionCommand = new RelayCommand(OnDeleteBoundaryCondition, () => SelectedBoundaryCondition != null);
        UpdateBoundaryConditionCommand = new RelayCommand(OnUpdateBoundaryCondition);
        AutoDetectBoundaryConditionsCommand = new RelayCommand(OnAutoDetectBoundaryConditions);
        ShowMarkersCommand = new RelayCommand(OnToggleShowMarkers);
        BoundaryConditions.CollectionChanged += (_, _) => RefreshBcMarkerLegendItems();
        RefreshBcMarkerLegendItems();
        // AvailableBCTypes filled by RefreshAvailableBcTypes once Project Settings category ON is known.
    }

    /// <summary>Set by MainWindowViewModel so Import Boundaries can read geometry and mesh.</summary>
    public GeometryDefinitionViewModel? GeometryDefinitionVM { get; set; }

    /// <summary>Set by MainWindowViewModel so connected nodes come from mesh.</summary>
    public MeshViewModel? MeshVM { get; set; }

    /// <summary>
    /// Set by MainWindowViewModel so BC Type options switch when Project Settings → Analysis
    /// selects Flow-Seepage/Groundwater.
    /// </summary>
    public MaterialPropertiesViewModel? MaterialPropertiesVM
    {
        get => _materialPropertiesVM;
        set
        {
            if (ReferenceEquals(_materialPropertiesVM, value)) return;
            if (_materialPropertiesVM != null)
                _materialPropertiesVM.PropertyChanged -= OnMaterialPropertiesChanged;
            _materialPropertiesVM = value;
            if (_materialPropertiesVM != null)
                _materialPropertiesVM.PropertyChanged += OnMaterialPropertiesChanged;
            RefreshAvailableBcTypes();
        }
    }

    /// <summary>
    /// Set by MainWindowViewModel so BC Type options respect Project Settings → Analysis
    /// category ON switches.
    /// </summary>
    public ProjectSettingsViewModel? ProjectSettingsVM
    {
        get => _projectSettingsVM;
        set
        {
            if (ReferenceEquals(_projectSettingsVM, value)) return;
            if (_projectSettingsVM != null)
                _projectSettingsVM.PropertyChanged -= OnProjectSettingsChanged;
            _projectSettingsVM = value;
            if (_projectSettingsVM != null)
                _projectSettingsVM.PropertyChanged += OnProjectSettingsChanged;
            RefreshAvailableBcTypes();
        }
    }

    public ObservableCollection<OuterBoundaryInfo> AvailableOuterBoundaries { get; } = new();
    public ObservableCollection<BoundaryConditionRow> BoundaryConditions { get; } = new();
    public ObservableCollection<string> AvailableBCTypes { get; } = new();
    public ObservableCollection<string> AvailableDOFs { get; } = new() { "0 (X)", "1 (Y)" };
    public ObservableCollection<int> AvailableNodeIds { get; } = new();
    public ObservableCollection<string> AvailableImportModes { get; } = new()
    {
        ImportModeBoundary,
        ImportModeVertices,
        ImportModeNodes
    };

    /// <summary>
    /// Import Mode dropdown: Boundary runs the legacy L1..Ln import immediately;
    /// Vertices/Nodes enter canvas marquee selection of boundary vertices or exterior mesh nodes.
    /// Null until the user picks a mode (avoids auto-import on panel open).
    /// </summary>
    public string? SelectedImportMode
    {
        get => _selectedImportMode;
        set
        {
            if (string.Equals(_selectedImportMode, value, StringComparison.Ordinal))
                return;
            _selectedImportMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVerticesImportMode));
            OnPropertyChanged(nameof(IsNodesImportMode));
            OnPropertyChanged(nameof(IsBcManualImportMode));
            ApplySelectedImportMode();
        }
    }

    public bool IsVerticesImportMode =>
        string.Equals(_selectedImportMode, ImportModeVertices, StringComparison.Ordinal);

    public bool IsNodesImportMode =>
        string.Equals(_selectedImportMode, ImportModeNodes, StringComparison.Ordinal);

    public bool IsBcManualImportMode => IsVerticesImportMode || IsNodesImportMode;

    public bool IsBcImportSelectionVisible { get; private set; }
    public double BcImportSelectionLeft { get; private set; }
    public double BcImportSelectionTop { get; private set; }
    public double BcImportSelectionWidth { get; private set; }
    public double BcImportSelectionHeight { get; private set; }

    public OuterBoundaryInfo? SelectedOuterBoundary
    {
        get => _selectedOuterBoundary;
        set
        {
            _selectedOuterBoundary = value;
            OnPropertyChanged();
            ConnectedNodesDisplayText = value == null || value.NodeIds.Count == 0
                ? ""
                : string.Join(", ", value.NodeIds);
            OnPropertyChanged(nameof(ConnectedNodesDisplayText));
            RefreshAvailableNodeIds();
            SyncBoundaryLineHighlightOnCanvas();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Connected node IDs for the selected boundary (e.g. "1, 2, 3") for display in BC popup.</summary>
    public string ConnectedNodesDisplayText { get; private set; } = "";

    /// <summary>Node number(s) to apply BC to when Apply BC is clicked (e.g. "5" or "5, 6"). Applied in addition to or instead of boundary selection.</summary>
    public string NodesText
    {
        get => _nodesText;
        set { _nodesText = value ?? ""; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public string? SelectedBCTypeForBoundary
    {
        get => _selectedBCTypeForBoundary;
        set { _selectedBCTypeForBoundary = value; OnPropertyChanged(); }
    }

    public string BcValueForBoundary
    {
        get => _bcValueForBoundary;
        set { _bcValueForBoundary = value ?? "0"; OnPropertyChanged(); }
    }

    /// <summary>X/Y chooser for Prescribed / Force when Apply writes a row (implied types ignore this).</summary>
    public int PendingDegreeOfFreedom { get; set; }

    public object? SelectedBoundaryCondition
    {
        get => _selectedBoundaryCondition;
        set { _selectedBoundaryCondition = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value ?? ""; OnPropertyChanged(); }
    }

    private bool _markersVisible;
    private bool _showStructuralMarkers = true;
    private bool _showSeepageMarkers = true;

    /// <summary>
    /// Visibility flag for the BC marker overlay on the Boundary Conditions tab's SlopeCanvasView.
    /// Toggled by <see cref="ShowMarkersCommand"/>; observed by <see cref="MeshTabCanvasWrapper"/>.
    /// </summary>
    public bool MarkersVisible
    {
        get => _markersVisible;
        set
        {
            if (_markersVisible == value) return;
            _markersVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowMarkersButtonLabel));
        }
    }

    /// <summary>When true, Structural-family BC glyphs are drawn on the canvas (chosen in the BC popup before Show Marker).</summary>
    public bool ShowStructuralMarkers
    {
        get => _showStructuralMarkers;
        set
        {
            if (_showStructuralMarkers == value) return;
            _showStructuralMarkers = value;
            OnPropertyChanged();
            RefreshBcMarkerLegendItems();
        }
    }

    /// <summary>When true, Flow-Seepage/Groundwater BC glyphs are drawn on the canvas (chosen in the BC popup before Show Marker).</summary>
    public bool ShowSeepageMarkers
    {
        get => _showSeepageMarkers;
        set
        {
            if (_showSeepageMarkers == value) return;
            _showSeepageMarkers = value;
            OnPropertyChanged();
            RefreshBcMarkerLegendItems();
        }
    }

    /// <summary>Swatches for types currently present in the BC table (legend list in the BC popup).</summary>
    public ObservableCollection<BcMarkerLegendItem> BcMarkerLegendItems { get; } = new();

    /// <summary>Button caption for the Show/Hide markers toggle.</summary>
    public string ShowMarkersButtonLabel => _markersVisible ? "Hide Marker" : "Show Marker";

    public ICommand ImportOuterBoundariesCommand { get; }
    public ICommand ApplyBCToSelectedBoundaryCommand { get; }
    public ICommand AddBoundaryConditionCommand { get; }
    public ICommand ClearBoundaryConditionsCommand { get; }
    public ICommand DeleteBoundaryConditionCommand { get; }
    public ICommand UpdateBoundaryConditionCommand { get; }
    public ICommand AutoDetectBoundaryConditionsCommand { get; }
    public ICommand ShowMarkersCommand { get; }

    public void SyncToFEAnalysisViewModel() { }

    private void ApplySelectedImportMode()
    {
        ClearBcImportSelection();
        ClearManualImportHighlights();

        if (string.Equals(_selectedImportMode, ImportModeBoundary, StringComparison.Ordinal))
        {
            OnImportOuterBoundaries();
            return;
        }

        if (IsVerticesImportMode)
        {
            StatusMessage = "Vertices mode: drag a box on the Boundary Conditions canvas to select boundary vertices (endpoints of Geometry Line Segments), or click near a vertex.";
            return;
        }

        if (IsNodesImportMode)
        {
            StatusMessage = "Nodes mode: drag a box on the Boundary Conditions canvas to select exterior mesh nodes, or click near a node.";
            return;
        }
    }

    public void UpdateBcImportSelection(Rect canvasRect)
    {
        if (!IsBcManualImportMode) return;
        var normalized = NormalizeRect(canvasRect);
        BcImportSelectionLeft = normalized.Left;
        BcImportSelectionTop = normalized.Top;
        BcImportSelectionWidth = normalized.Width;
        BcImportSelectionHeight = normalized.Height;
        IsBcImportSelectionVisible = normalized.Width > 0 && normalized.Height > 0;
        OnPropertyChanged(nameof(BcImportSelectionLeft));
        OnPropertyChanged(nameof(BcImportSelectionTop));
        OnPropertyChanged(nameof(BcImportSelectionWidth));
        OnPropertyChanged(nameof(BcImportSelectionHeight));
        OnPropertyChanged(nameof(IsBcImportSelectionVisible));

        if (IsVerticesImportMode)
        {
            var indices = CollectBoundaryVertexIndicesInCanvasRect(normalized);
            GeometryDefinitionVM?.SetHighlightedVertices(indices);
            StatusMessage = indices.Count == 0
                ? "Drag a box around boundary vertices, or click a vertex."
                : $"Release to select {indices.Count} boundary vertex(ices).";
        }
        else if (IsNodesImportMode)
        {
            var ids = CollectExteriorNodeIdsInCanvasRect(normalized);
            MeshVM?.ApplyUserDefinedTableHighlight(null, null, ids);
            StatusMessage = ids.Count == 0
                ? "Drag a box around exterior mesh nodes, or click a node."
                : $"Release to select {ids.Count} exterior mesh node(s).";
        }
    }

    public void ClearBcImportSelection()
    {
        if (!IsBcImportSelectionVisible
            && BcImportSelectionWidth == 0
            && BcImportSelectionHeight == 0)
            return;
        BcImportSelectionLeft = 0;
        BcImportSelectionTop = 0;
        BcImportSelectionWidth = 0;
        BcImportSelectionHeight = 0;
        IsBcImportSelectionVisible = false;
        OnPropertyChanged(nameof(BcImportSelectionLeft));
        OnPropertyChanged(nameof(BcImportSelectionTop));
        OnPropertyChanged(nameof(BcImportSelectionWidth));
        OnPropertyChanged(nameof(BcImportSelectionHeight));
        OnPropertyChanged(nameof(IsBcImportSelectionVisible));
    }

    /// <summary>Completes a drag-box or click selection in Vertices/Nodes import mode.</summary>
    public void CompleteBcImportSelection(Rect canvasRect, double clickToleranceCanvasPx)
    {
        if (!IsBcManualImportMode) return;
        ClearBcImportSelection();

        var normalized = NormalizeRect(canvasRect);
        bool isBox = Math.Abs(normalized.Width) >= 8.0 && Math.Abs(normalized.Height) >= 8.0;

        if (IsVerticesImportMode)
        {
            CompleteVerticesImportSelection(normalized, isBox, clickToleranceCanvasPx);
            return;
        }

        if (IsNodesImportMode)
            CompleteNodesImportSelection(normalized, isBox, clickToleranceCanvasPx);
    }

    private const string VerticesSelectionTag = "Vertices selection";
    private const string NodesSelectionTag = "Nodes selection";
    private const int ManualSelectionLineNumber = -1;

    private void CompleteVerticesImportSelection(Rect canvasRect, bool isBox, double clickToleranceCanvasPx)
    {
        if (GeometryDefinitionVM == null)
        {
            StatusMessage = "Geometry not available.";
            return;
        }

        List<int> vertexIndices;
        if (isBox)
        {
            vertexIndices = CollectBoundaryVertexIndicesInCanvasRect(canvasRect);
        }
        else
        {
            int nearest = FindNearestBoundaryVertexIndex(
                new Point(canvasRect.X + canvasRect.Width / 2, canvasRect.Y + canvasRect.Height / 2),
                clickToleranceCanvasPx);
            vertexIndices = nearest >= 0 ? new List<int> { nearest } : new List<int>();
        }

        GeometryDefinitionVM.SetHighlightedVertices(vertexIndices);
        if (vertexIndices.Count == 0)
        {
            ClearManualSelectionFromApplyPanel();
            StatusMessage = isBox
                ? "No boundary vertices inside the selection box."
                : "No boundary vertex near the click. Click closer, or drag a box.";
            return;
        }

        var labels = vertexIndices
            .Select(i => GeometryDefinitionVM.GeometryVertexRows[i].PointLabel)
            .ToList();
        var meshNodes = CollectAxisNodesFromMesh();
        var nodeIds = new List<int>();
        if (meshNodes.Count > 0)
        {
            double spacing = EstimateTypicalNodeSpacing(meshNodes);
            double maxDist = Math.Max(spacing * 0.55, ComputeBaseGeometricTolerance(meshNodes) * 20);
            foreach (int vi in vertexIndices)
            {
                var v = GeometryDefinitionVM.GeometryVertexRows[vi];
                int? id = FindNearestMeshNodeId(v.AxisX, v.AxisY, meshNodes, maxDist);
                if (id is int nid && !nodeIds.Contains(nid))
                    nodeIds.Add(nid);
            }
            MeshVM?.ApplyUserDefinedTableHighlight(null, null, nodeIds);
        }

        if (nodeIds.Count > 0)
        {
            PublishManualSelection(VerticesSelectionTag, nodeIds);
            StatusMessage =
                $"Selected {vertexIndices.Count} polygon vertex(ices) ({string.Join(", ", labels)}) → " +
                $"{nodeIds.Count} mesh node(s) in Apply BC. Click Apply BC to add them to the DataGrid.";
        }
        else
        {
            ClearManualSelectionFromApplyPanel();
            GeometryDefinitionVM.SetHighlightedVertices(vertexIndices);
            StatusMessage = meshNodes.Count == 0
                ? $"Selected {vertexIndices.Count} polygon vertex(ices) ({string.Join(", ", labels)}). Generate/discretize the mesh so each vertex can map to a node ID."
                : $"Selected {vertexIndices.Count} polygon vertex(ices) ({string.Join(", ", labels)}), but no mesh node was near those vertices.";
        }
    }

    private void CompleteNodesImportSelection(Rect canvasRect, bool isBox, double clickToleranceCanvasPx)
    {
        var meshExterior = TryBuildMeshExteriorContext();
        if (meshExterior == null)
        {
            ClearManualSelectionFromApplyPanel();
            StatusMessage = "No exterior mesh nodes available. Generate a 2-D mesh first.";
            return;
        }

        List<int> ids;
        if (isBox)
        {
            ids = CollectExteriorNodeIdsInCanvasRect(canvasRect);
        }
        else
        {
            var click = new Point(canvasRect.X + canvasRect.Width / 2, canvasRect.Y + canvasRect.Height / 2);
            if (canvasRect.Width == 0 && canvasRect.Height == 0)
                click = canvasRect.Location;
            int nearest = FindNearestExteriorNodeIdInCanvas(click, clickToleranceCanvasPx, meshExterior);
            ids = nearest > 0 ? new List<int> { nearest } : new List<int>();
        }

        MeshVM?.ApplyUserDefinedTableHighlight(null, null, ids);
        if (ids.Count == 0)
        {
            ClearManualSelectionFromApplyPanel();
            StatusMessage = isBox
                ? "No exterior mesh nodes inside the selection box."
                : "No exterior mesh node near the click. Click closer, or drag a box.";
            return;
        }

        PublishManualSelection(NodesSelectionTag, ids);
        StatusMessage = $"Selected {ids.Count} exterior mesh node(s) in Apply BC. Click Apply BC to add them to the DataGrid.";
    }

    /// <summary>
    /// Puts a drag-selection into the Apply BC Boundary combo + Nodes box so Apply BC can write the DataGrid.
    /// </summary>
    private void PublishManualSelection(string tag, IReadOnlyList<int> nodeIds)
    {
        var ordered = nodeIds.Where(id => id > 0).Distinct().OrderBy(id => id).ToList();
        for (int i = AvailableOuterBoundaries.Count - 1; i >= 0; i--)
        {
            var t = AvailableOuterBoundaries[i].BoundaryTag;
            if (string.Equals(t, VerticesSelectionTag, StringComparison.Ordinal)
                || string.Equals(t, NodesSelectionTag, StringComparison.Ordinal))
                AvailableOuterBoundaries.RemoveAt(i);
        }

        var info = new OuterBoundaryInfo
        {
            LineNumber = ManualSelectionLineNumber,
            BoundaryTag = tag,
            NodeIds = ordered
        };
        AvailableOuterBoundaries.Insert(0, info);
        SelectedOuterBoundary = info;
        NodesText = string.Join(", ", ordered);
        ConnectedNodesDisplayText = NodesText;
        OnPropertyChanged(nameof(ConnectedNodesDisplayText));
        RefreshAvailableNodeIds();
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearManualSelectionFromApplyPanel()
    {
        NodesText = "";
        if (SelectedOuterBoundary != null
            && (string.Equals(SelectedOuterBoundary.BoundaryTag, VerticesSelectionTag, StringComparison.Ordinal)
                || string.Equals(SelectedOuterBoundary.BoundaryTag, NodesSelectionTag, StringComparison.Ordinal)))
        {
            SelectedOuterBoundary = null;
        }
        for (int i = AvailableOuterBoundaries.Count - 1; i >= 0; i--)
        {
            var t = AvailableOuterBoundaries[i].BoundaryTag;
            if (string.Equals(t, VerticesSelectionTag, StringComparison.Ordinal)
                || string.Equals(t, NodesSelectionTag, StringComparison.Ordinal))
                AvailableOuterBoundaries.RemoveAt(i);
        }
        ConnectedNodesDisplayText = "";
        OnPropertyChanged(nameof(ConnectedNodesDisplayText));
        ClearManualImportHighlights();
    }

    private void ClearManualImportHighlights()
    {
        GeometryDefinitionVM?.SetHighlightedVertices(Array.Empty<int>());
        MeshVM?.ApplyUserDefinedTableHighlight(null, null, Array.Empty<int>());
    }

    private HashSet<int> GetBoundaryVertexIndexSet()
    {
        var set = new HashSet<int>();
        if (GeometryDefinitionVM == null) return set;
        var verts = GeometryDefinitionVM.GeometryVertexRows;
        foreach (var seg in GeometryDefinitionVM.GeometryLineSegments)
        {
            if (seg.IsCircle) continue;
            var from = ResolveSegmentVertex(verts, seg.FromPoint);
            var to = ResolveSegmentVertex(verts, seg.ToPoint);
            if (from != null)
            {
                int i = verts.IndexOf(from);
                if (i >= 0) set.Add(i);
            }
            if (to != null)
            {
                int i = verts.IndexOf(to);
                if (i >= 0) set.Add(i);
            }
        }
        if (set.Count == 0)
        {
            for (int i = 0; i < verts.Count; i++)
                set.Add(i);
        }
        return set;
    }

    private static int? FindNearestMeshNodeId(
        double axisX,
        double axisY,
        IReadOnlyDictionary<int, (double x, double y)> meshNodes,
        double maxDistance)
    {
        double best = double.MaxValue;
        int? bestId = null;
        double max2 = maxDistance * maxDistance;
        foreach (var (id, p) in meshNodes)
        {
            double dx = p.x - axisX, dy = p.y - axisY;
            double d2 = dx * dx + dy * dy;
            if (d2 < best)
            {
                best = d2;
                bestId = id;
            }
        }
        return best <= max2 ? bestId : null;
    }

    private List<int> CollectBoundaryVertexIndicesInCanvasRect(Rect canvasRect)
    {
        var result = new List<int>();
        if (GeometryDefinitionVM == null) return result;
        var boundary = GetBoundaryVertexIndexSet();
        var verts = GeometryDefinitionVM.GeometryVertexRows;
        foreach (int i in boundary.OrderBy(x => x))
        {
            if (i < 0 || i >= verts.Count) continue;
            var v = verts[i];
            var p = GeometryDefinitionVM.ConvertAxisToCanvas(new Point(v.AxisX, v.AxisY));
            if (canvasRect.Contains(p))
                result.Add(i);
        }
        return result;
    }

    private int FindNearestBoundaryVertexIndex(Point canvasPoint, double toleranceCanvasPx)
    {
        if (GeometryDefinitionVM == null) return -1;
        var boundary = GetBoundaryVertexIndexSet();
        var verts = GeometryDefinitionVM.GeometryVertexRows;
        double tol2 = toleranceCanvasPx * toleranceCanvasPx;
        double best = double.MaxValue;
        int bestIndex = -1;
        foreach (int i in boundary)
        {
            if (i < 0 || i >= verts.Count) continue;
            var v = verts[i];
            var p = GeometryDefinitionVM.ConvertAxisToCanvas(new Point(v.AxisX, v.AxisY));
            double dx = p.X - canvasPoint.X, dy = p.Y - canvasPoint.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 < best)
            {
                best = d2;
                bestIndex = i;
            }
        }
        return best <= tol2 ? bestIndex : -1;
    }

    private List<int> CollectExteriorNodeIdsInCanvasRect(Rect canvasRect)
    {
        var result = new List<int>();
        var meshExterior = TryBuildMeshExteriorContext();
        if (meshExterior == null || GeometryDefinitionVM == null) return result;
        foreach (int id in meshExterior.ExteriorNodeIds.OrderBy(x => x))
        {
            if (!meshExterior.Nodes.TryGetValue(id, out var axis)) continue;
            var p = GeometryDefinitionVM.ConvertAxisToCanvas(new Point(axis.x, axis.y));
            if (canvasRect.Contains(p))
                result.Add(id);
        }
        return result;
    }

    private int FindNearestExteriorNodeIdInCanvas(Point canvasPoint, double toleranceCanvasPx, MeshExteriorContext ctx)
    {
        if (GeometryDefinitionVM == null) return 0;
        double tol2 = toleranceCanvasPx * toleranceCanvasPx;
        double best = double.MaxValue;
        int bestId = 0;
        foreach (int id in ctx.ExteriorNodeIds)
        {
            if (!ctx.Nodes.TryGetValue(id, out var axis)) continue;
            var p = GeometryDefinitionVM.ConvertAxisToCanvas(new Point(axis.x, axis.y));
            double dx = p.X - canvasPoint.X, dy = p.Y - canvasPoint.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 < best)
            {
                best = d2;
                bestId = id;
            }
        }
        return best <= tol2 ? bestId : 0;
    }

    private static int? FindNearestExteriorNodeId(double axisX, double axisY, MeshExteriorContext ctx)
    {
        double best = double.MaxValue;
        int? bestId = null;
        double maxDist = Math.Max(ctx.AlignmentTolerance, ctx.BaseTolerance);
        double max2 = maxDist * maxDist;
        foreach (int id in ctx.ExteriorNodeIds)
        {
            if (!ctx.Nodes.TryGetValue(id, out var p)) continue;
            double dx = p.x - axisX, dy = p.y - axisY;
            double d2 = dx * dx + dy * dy;
            if (d2 < best)
            {
                best = d2;
                bestId = id;
            }
        }
        return best <= max2 ? bestId : null;
    }

    private static Rect NormalizeRect(Rect rect)
    {
        double x = Math.Min(rect.Left, rect.Right);
        double y = Math.Min(rect.Top, rect.Bottom);
        return new Rect(x, y, Math.Abs(rect.Width), Math.Abs(rect.Height));
    }

    private void OnImportOuterBoundaries()
    {
        RecordUndo("Import boundaries");
        AvailableOuterBoundaries.Clear();
        GeometryDefinitionVM?.SetHighlightedLineSegment(null);

        if (GeometryDefinitionVM == null)
        {
            StatusMessage = "Geometry not available. Open from main window.";
            return;
        }

        // All line segment rows (every L1, L2, …), not only those flagged as boundary edges.
        var allSegments = GeometryDefinitionVM.GeometryLineSegments.OrderBy(s => s.LineNumber).ToList();
        string? bootstrapPrefix = null;

        // When mesh exists but geometry was never defined (e.g. UDM Generate without a .xfem load),
        // derive L1..Ln from the mesh outer boundary so Import Boundaries can proceed.
        if (allSegments.Count == 0)
        {
            if (!TryBootstrapGeometryLineSegmentsFromMesh(out var bootstrapNote))
            {
                StatusMessage = bootstrapNote ?? "No Geometry Line Segments. Add segments in Geometry Definition popup (Layer Number & Name / Line Segments).";
                return;
            }

            bootstrapPrefix = bootstrapNote;
            allSegments = GeometryDefinitionVM.GeometryLineSegments.OrderBy(s => s.LineNumber).ToList();
            if (allSegments.Count == 0)
            {
                StatusMessage = "No Geometry Line Segments. Add segments in Geometry Definition popup (Layer Number & Name / Line Segments), or generate a mesh and click Import Boundaries again.";
                return;
            }
        }

        // Prefer geometric matching against the 2-D mesh when present — this works for any polygon
        // topology (closing edge, interior boundaries, tunnel inner circles). The legacy 1-D arc-length
        // path-edge logic only kicks in when the project has a 1-D polyline mesh (DiscretizationNodes)
        // and no T3 element table.
        var meshExterior = TryBuildMeshExteriorContext();
        bool has2DMesh = meshExterior != null;

        // Legacy 1-D fallback data (populated only when has2DMesh == false).
        var pathEdges = new List<(string from, string to)>();
        var nodeIdsForPathEdge = new List<List<int>>();
        if (!has2DMesh)
        {
            var verts = GeometryDefinitionVM.GeometryVertexRows;
            for (int i = 0; i < verts.Count - 1; i++)
                pathEdges.Add((verts[i].PointLabel, verts[i + 1].PointLabel));
            for (int i = 0; i < pathEdges.Count; i++)
                nodeIdsForPathEdge.Add(new List<int>());

            var segmentBounds = GeometryDefinitionVM.GetSegmentBounds().ToList();
            double totalLength = GeometryDefinitionVM.GetTotalPathLength();
            if (MeshVM != null && MeshVM.IsDiscretized && MeshVM.DiscretizationNodes.Count > 0 && pathEdges.Count > 0)
            {
                int nn = MeshVM.DiscretizationNodes.Count;
                double L = totalLength <= 0 ? 1e-6 : totalLength;
                for (int i = 1; i <= nn; i++)
                {
                    double position = (i - 1) * L / Math.Max(nn - 1, 1);
                    int segmentIndex = 0;
                    for (int k = 0; k < segmentBounds.Count - 1; k++)
                    {
                        if (position >= segmentBounds[k] && position < segmentBounds[k + 1])
                        {
                            segmentIndex = k;
                            break;
                        }
                        segmentIndex = segmentBounds.Count - 2;
                    }
                    if (segmentIndex >= 0 && segmentIndex < nodeIdsForPathEdge.Count)
                        nodeIdsForPathEdge[segmentIndex].Add(i);
                }
            }
        }

        foreach (var seg in allSegments)
        {
            var tag = !string.IsNullOrWhiteSpace(seg.Tag) ? seg.Tag : seg.LineNumberLabel;
            IReadOnlyList<int> ids;
            if (has2DMesh)
            {
                ids = CollectNodesOnSegment(seg, meshExterior!);
            }
            else
            {
                int? pathIdx = MatchSegmentToPathEdge(seg, pathEdges);
                ids = pathIdx is int pi && pi >= 0 && pi < nodeIdsForPathEdge.Count
                    ? nodeIdsForPathEdge[pi].ToList()
                    : Array.Empty<int>();
            }

            AvailableOuterBoundaries.Add(new OuterBoundaryInfo
            {
                LineNumber = seg.LineNumber,
                BoundaryTag = tag,
                NodeIds = ids
            });
        }

        SelectedOuterBoundary = AvailableOuterBoundaries.FirstOrDefault();
        RefreshAvailableNodeIds();
        if (has2DMesh)
        {
            var msg = $"Imported {AvailableOuterBoundaries.Count} line(s) from Geometry Line Segments. Matched exterior mesh nodes on {meshExterior!.ExteriorNodeIds.Count} boundary node(s) (tol = {meshExterior.BaseTolerance:G3}).";
            StatusMessage = bootstrapPrefix != null ? $"{bootstrapPrefix} {msg}" : msg;
        }
        else if (MeshVM != null && MeshVM.IsDiscretized)
            StatusMessage = $"Imported {AvailableOuterBoundaries.Count} line(s) from Geometry Line Segments. Connected nodes when the segment matches the vertex path; select a boundary to highlight it on the canvas.";
        else
            StatusMessage = $"Imported {AvailableOuterBoundaries.Count} line(s) from Geometry Line Segments. Discretize/mesh in the Mesh popup to list connected nodes; select a boundary to highlight it on the canvas.";
    }

    /// <summary>
    /// When the Mesh tab has a 2-D mesh but Geometry Definition has no L1..Ln rows yet, build them
    /// from the mesh outer boundary (same as Mesh popup → Import Boundary from Mesh).
    /// </summary>
    private bool TryBootstrapGeometryLineSegmentsFromMesh(out string? note)
    {
        note = null;
        if (GeometryDefinitionVM == null || MeshVM == null)
        {
            note = "Geometry not available. Open from main window.";
            return false;
        }

        if (GeometryDefinitionVM.GeometryLineSegments.Count > 0)
            return true;

        if (!MeshVM.IsDiscretized || MeshVM.MeshInformationRows.Count == 0)
        {
            note = "No geometry line segments. Define vertices in Geometry Definition, or generate a 2-D mesh on the Mesh tab first.";
            return false;
        }

        GeometryDefinitionVM.ImportBoundaryFromMesh();
        int segCount = GeometryDefinitionVM.GeometryLineSegments.Count;
        var importStatus = GeometryDefinitionVM.GeometryImportStatusMessage;

        if (segCount > 0)
        {
            note = $"Created {segCount} boundary segment(s) from mesh outline.";
            return true;
        }

        note = string.IsNullOrWhiteSpace(importStatus)
            ? "No geometry line segments. Could not derive a closed outer boundary from the current mesh."
            : importStatus;
        return false;
    }

    /// <summary>
    /// Collect unique (node id → axis-xy) entries from the 2-D mesh table (all corners/midside slots).
    /// Falls back to discretization nodes when the element table is empty so the caller can use the
    /// legacy 1-D path only when no 2-D mesh exists at all.
    /// </summary>
    private Dictionary<int, (double x, double y)> CollectAxisNodesFromMesh()
    {
        var map = new Dictionary<int, (double x, double y)>();
        if (MeshVM == null) return map;
        foreach (var r in MeshVM.MeshInformationRows)
        {
            AddMeshNode(map, r.Node1, r.N1X, r.N1Y);
            AddMeshNode(map, r.Node2, r.N2X, r.N2Y);
            AddMeshNode(map, r.Node3, r.N3X, r.N3Y);
            AddMeshNode(map, r.Node4, r.N4X, r.N4Y);
            AddMeshNode(map, r.Node5, r.N5X, r.N5Y);
            AddMeshNode(map, r.Node6, r.N6X, r.N6Y);
            AddMeshNode(map, r.Node7, r.N7X, r.N7Y);
            AddMeshNode(map, r.Node8, r.N8X, r.N8Y);
        }

        if (map.Count == 0 && MeshVM.DiscretizationNodes.Count > 0)
        {
            MeshVM.EnsureDiscretizationNodeIds();
            for (int i = 0; i < MeshVM.DiscretizationNodes.Count; i++)
            {
                var n = MeshVM.DiscretizationNodes[i];
                int id = n.NodeId > 0 ? n.NodeId : i + 1;
                if (id > 0)
                    map[id] = (n.Location.X, n.Location.Y);
            }
        }

        return map;
    }

    private static void AddMeshNode(Dictionary<int, (double x, double y)> map, int id, double x, double y)
    {
        if (id > 0)
            map[id] = (x, y);
    }

    private sealed class MeshExteriorContext
    {
        public required Dictionary<int, (double x, double y)> Nodes { get; init; }
        public required HashSet<int> ExteriorNodeIds { get; init; }
        public required List<(int[] conn, string meshType, bool smithQ8)> Elements { get; init; }
        public required double BaseTolerance { get; init; }
        /// <summary>Slack for matching mesh edges that lie slightly inside the ideal geometry polyline.</summary>
        public required double AlignmentTolerance { get; init; }
    }

    private MeshExteriorContext? TryBuildMeshExteriorContext()
    {
        var nodes = CollectAxisNodesFromMesh();
        if (nodes.Count == 0 || MeshVM == null) return null;

        var elements = new List<(int[] conn, string meshType, bool smithQ8)>();
        var edgeCount = new Dictionary<(int, int), int>();
        foreach (var row in MeshVM.MeshInformationRows)
        {
            bool smithQ8 = row.SmithEmb2dLocalOrder;
            string meshType = ResolveElementMeshType(row);
            var connNodes = new List<int> { row.Node1, row.Node2, row.Node3 };
            if (row.Node4 > 0) connNodes.Add(row.Node4);
            if (row.Node5 > 0) connNodes.Add(row.Node5);
            if (row.Node6 > 0) connNodes.Add(row.Node6);
            if (row.Node7 > 0) connNodes.Add(row.Node7);
            if (row.Node8 > 0) connNodes.Add(row.Node8);
            var conn = connNodes.ToArray();
            elements.Add((conn, meshType, smithQ8));
            foreach (var (a, b) in MeshBoundaryGeometryExtractor.ElementBoundarySegments(meshType, conn, smithQ8))
                CountUndirectedEdge(edgeCount, a, b);
        }

        var exterior = new HashSet<int>();
        foreach (var kvp in edgeCount)
        {
            if (kvp.Value != 1) continue;
            exterior.Add(kvp.Key.Item1);
            exterior.Add(kvp.Key.Item2);
        }

        double baseTol = ComputeBaseGeometricTolerance(nodes);
        double spacing = EstimateTypicalNodeSpacing(nodes);
        return new MeshExteriorContext
        {
            Nodes = nodes,
            ExteriorNodeIds = exterior,
            Elements = elements,
            BaseTolerance = baseTol,
            AlignmentTolerance = Math.Max(baseTol, spacing * 0.55),
        };
    }

    private static string ResolveElementMeshType(MeshInformationRow row)
    {
        if (row.IsQ8) return "Q8";
        if (row.IsQ4) return "Q4";
        if (row.IsT6) return "T6";
        return "T3";
    }

    /// <summary>
    /// Tight tolerance for floating-point noise only — not wide enough to pick up parallel interior rows.
    /// </summary>
    private static double ComputeBaseGeometricTolerance(IReadOnlyDictionary<int, (double x, double y)> meshNodes)
    {
        if (meshNodes.Count == 0) return 1e-6;
        double xmin = double.PositiveInfinity, ymin = double.PositiveInfinity;
        double xmax = double.NegativeInfinity, ymax = double.NegativeInfinity;
        foreach (var (x, y) in meshNodes.Values)
        {
            if (x < xmin) xmin = x;
            if (x > xmax) xmax = x;
            if (y < ymin) ymin = y;
            if (y > ymax) ymax = y;
        }
        double diag = Math.Sqrt((xmax - xmin) * (xmax - xmin) + (ymax - ymin) * (ymax - ymin));
        return Math.Max(diag * 5e-4, 1e-9);
    }

    /// <summary>Median nearest-neighbour distance over a sample of mesh nodes (axis units).</summary>
    private static double EstimateTypicalNodeSpacing(IReadOnlyDictionary<int, (double x, double y)> meshNodes)
    {
        if (meshNodes.Count < 2) return 0;
        var pts = meshNodes.Values.ToList();
        int step = Math.Max(1, pts.Count / 200);
        var nnDists = new List<double>();
        for (int i = 0; i < pts.Count; i += step)
        {
            var (xi, yi) = pts[i];
            double best = double.PositiveInfinity;
            for (int j = 0; j < pts.Count; j += step)
            {
                if (i == j) continue;
                var (xj, yj) = pts[j];
                double d = Math.Sqrt((xi - xj) * (xi - xj) + (yi - yj) * (yi - yj));
                if (d < best) best = d;
            }
            if (double.IsFinite(best))
                nnDists.Add(best);
        }
        if (nnDists.Count == 0) return 0;
        nnDists.Sort();
        return nnDists[nnDists.Count / 2];
    }

    /// <summary>
    /// Returns mesh node ids on the exterior boundary that correspond to a geometry line segment.
    /// Uses topological exterior edges first so parallel interior rows are never included.
    /// </summary>
    private IReadOnlyList<int> CollectNodesOnSegment(
        GeometryLineSegment seg,
        MeshExteriorContext ctx)
    {
        if (ctx.Nodes.Count == 0) return Array.Empty<int>();

        if (seg.IsCircle)
        {
            double cx = seg.CircleCenterAxisX;
            double cy = seg.CircleCenterAxisY;
            double r  = seg.CircleRadiusAxis;
            if (r <= 0) return Array.Empty<int>();
            var hits = new List<(int id, double ang)>();
            foreach (int id in ctx.ExteriorNodeIds)
            {
                if (!ctx.Nodes.TryGetValue(id, out var p)) continue;
                double dx = p.x - cx, dy = p.y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (Math.Abs(dist - r) <= ctx.AlignmentTolerance)
                    hits.Add((id, Math.Atan2(dy, dx)));
            }
            hits.Sort((a, b) => a.ang.CompareTo(b.ang));
            return hits.Select(h => h.id).ToList();
        }

        if (GeometryDefinitionVM == null) return Array.Empty<int>();
        var verts = GeometryDefinitionVM.GeometryVertexRows;
        var fromV = ResolveSegmentVertex(verts, seg.FromPoint);
        var toV   = ResolveSegmentVertex(verts, seg.ToPoint);
        if (fromV == null || toV == null) return Array.Empty<int>();

        double ax = fromV.AxisX, ay = fromV.AxisY;
        double bx = toV.AxisX,   by = toV.AxisY;

        // Primary: walk single-sided (exterior) mesh edges that overlap this geometry segment.
        var boundary = CollectNodesOnMeshBoundaryOverlap(ax, ay, bx, by, ctx);
        if (boundary.Count > 0)
            return boundary;

        // Fallback: perpendicular match on exterior nodes only (exact geometry alignment).
        return CollectNodesGeometricallyOnSegment(ax, ay, bx, by, ctx, ctx.BaseTolerance);
    }

    private static IReadOnlyList<int> CollectNodesGeometricallyOnSegment(
        double ax, double ay, double bx, double by,
        MeshExteriorContext ctx,
        double tol)
    {
        double dxAB = bx - ax, dyAB = by - ay;
        double L2 = dxAB * dxAB + dyAB * dyAB;
        if (L2 <= 0) return Array.Empty<int>();
        double L = Math.Sqrt(L2);
        double slack = tol / L;

        var matches = new List<(int id, double t)>();
        foreach (int id in ctx.ExteriorNodeIds)
        {
            if (!ctx.Nodes.TryGetValue(id, out var p)) continue;
            double dx = p.x - ax, dy = p.y - ay;
            double t = (dx * dxAB + dy * dyAB) / L2;
            if (t < -slack || t > 1.0 + slack) continue;
            double cross = Math.Abs(dx * dyAB - dy * dxAB) / L;
            if (cross > tol) continue;
            matches.Add((id, t));
        }
        matches.Sort((a, b) => a.t.CompareTo(b.t));
        return matches.Select(m => m.id).ToList();
    }

    private static GeometryDefinitionViewModel.GeometryVertexDisplay? ResolveSegmentVertex(
        IEnumerable<GeometryDefinitionViewModel.GeometryVertexDisplay> verts,
        string pointLabel)
    {
        var exact = verts.FirstOrDefault(v => string.Equals(v.PointLabel, pointLabel, StringComparison.Ordinal));
        if (exact != null) return exact;
        if (!TryParsePointNumber(pointLabel, out int pointNumber)) return null;
        return verts.FirstOrDefault(v => v.PointNumber == pointNumber);
    }

    private static bool TryParsePointNumber(string pointLabel, out int pointNumber)
    {
        pointNumber = 0;
        if (string.IsNullOrWhiteSpace(pointLabel)) return false;
        var trimmed = pointLabel.Trim();
        if (!trimmed.StartsWith("Point ", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = trimmed["Point ".Length..].Trim();
        int splitIdx = rest.IndexOf(' ');
        if (splitIdx > 0) rest = rest[..splitIdx];
        return int.TryParse(rest, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out pointNumber) && pointNumber > 0;
    }

    /// <summary>
    /// Collects node ids on exterior mesh edges whose endpoints both project onto the geometry segment.
    /// </summary>
    private IReadOnlyList<int> CollectNodesOnMeshBoundaryOverlap(
        double ax, double ay, double bx, double by,
        MeshExteriorContext ctx)
    {
        var meshNodes = ctx.Nodes;
        if (meshNodes.Count == 0) return Array.Empty<int>();

        var edgeCount = new Dictionary<(int, int), int>();
        foreach (var (conn, meshType, smithQ8) in ctx.Elements)
        {
            foreach (var (a, b) in MeshBoundaryGeometryExtractor.ElementBoundarySegments(meshType, conn, smithQ8))
                CountUndirectedEdge(edgeCount, a, b);
        }

        double dxAB = bx - ax, dyAB = by - ay;
        double L2 = dxAB * dxAB + dyAB * dyAB;
        if (L2 <= 0) return Array.Empty<int>();
        double L = Math.Sqrt(L2);
        double slack = ctx.AlignmentTolerance / L;
        double alignTol = ctx.AlignmentTolerance;

        var hits = new List<(int id, double t)>();
        var seen = new HashSet<int>();
        foreach (var kvp in edgeCount)
        {
            if (kvp.Value != 1) continue;
            var (na, nb) = kvp.Key;
            if (!meshNodes.ContainsKey(na) || !meshNodes.ContainsKey(nb)) continue;
            var pa = meshNodes[na];
            var pb = meshNodes[nb];
            if (!ProjectsOntoSegment(pa.x, pa.y, ax, ay, dxAB, dyAB, L2, slack, alignTol) ||
                !ProjectsOntoSegment(pb.x, pb.y, ax, ay, dxAB, dyAB, L2, slack, alignTol))
                continue;
            double cross = Math.Abs((pb.x - pa.x) * dyAB - (pb.y - pa.y) * dxAB) / L;
            if (cross > alignTol) continue;
            foreach (int id in new[] { na, nb })
            {
                if (!ctx.ExteriorNodeIds.Contains(id)) continue;
                if (!seen.Add(id)) continue;
                var p = meshNodes[id];
                double t = ((p.x - ax) * dxAB + (p.y - ay) * dyAB) / L2;
                hits.Add((id, t));
            }
        }

        hits.Sort((a, b) => a.t.CompareTo(b.t));
        return hits.Select(h => h.id).ToList();
    }

    private static bool ProjectsOntoSegment(
        double px, double py, double ax, double ay, double dxAB, double dyAB, double L2, double slack, double perpTol)
    {
        double t = ((px - ax) * dxAB + (py - ay) * dyAB) / L2;
        if (t < -slack || t > 1.0 + slack) return false;
        if (L2 <= 0) return false;
        double perp = Math.Abs((px - ax) * dyAB - (py - ay) * dxAB) / Math.Sqrt(L2);
        return perp <= perpTol;
    }

    private static void CountUndirectedEdge(Dictionary<(int, int), int> edgeCount, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        edgeCount.TryGetValue(key, out int c);
        edgeCount[key] = c + 1;
    }

    /// <summary>Maps a geometry line segment to a consecutive vertex-path edge index (for 1D-style mesh node lists).</summary>
    private static int? MatchSegmentToPathEdge(GeometryLineSegment seg, IReadOnlyList<(string from, string to)> pathEdges)
    {
        if (seg.IsCircle) return null;
        for (int i = 0; i < pathEdges.Count; i++)
        {
            var (a, b) = pathEdges[i];
            if (string.Equals(seg.FromPoint, a, StringComparison.Ordinal) && string.Equals(seg.ToPoint, b, StringComparison.Ordinal))
                return i;
            if (string.Equals(seg.FromPoint, b, StringComparison.Ordinal) && string.Equals(seg.ToPoint, a, StringComparison.Ordinal))
                return i;
        }
        return null;
    }

    /// <summary>Drives <see cref="GeometryDefinitionViewModel.HighlightedSegmentPoints"/> so the BC / Mesh canvas shows the selected line.</summary>
    private void SyncBoundaryLineHighlightOnCanvas()
    {
        if (GeometryDefinitionVM == null) return;
        int? line = SelectedOuterBoundary is { LineNumber: > 0 } b ? b.LineNumber : null;
        GeometryDefinitionVM.SetHighlightedLineSegment(line);
    }

    private void OnApplyBCToSelectedBoundary()
    {
        if (AvailableBCTypes.Count == 0)
        {
            StatusMessage = "No BC types available. Turn ON the active analysis category in Project Settings → Analysis.";
            MessageBox.Show(
                "No boundary-condition types are available for the current analysis category.\n\n" +
                "Turn ON that category in Project Settings → Analysis, then try again.",
                "Boundary Conditions",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RecordUndo("Apply boundary condition");
        string bcType = SelectedBCTypeForBoundary ?? DefaultBcType!;
        string value = BcValueForBoundary ?? "0";
        string boundaryName = SelectedOuterBoundary?.BoundaryTag ?? "Manual";

        var nodeIdsToApply = new List<int>();

        if (!string.IsNullOrWhiteSpace(NodesText))
        {
            foreach (var part in NodesText.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int nodeId) && nodeId > 0)
                    nodeIdsToApply.Add(nodeId);
            }
        }

        if (nodeIdsToApply.Count == 0 && SelectedOuterBoundary != null && SelectedOuterBoundary.NodeIds.Count > 0)
        {
            nodeIdsToApply.AddRange(SelectedOuterBoundary.NodeIds);
            boundaryName = SelectedOuterBoundary.BoundaryTag;
        }

        if (nodeIdsToApply.Count == 0)
        {
            StatusMessage = "Enter a node number in Nodes box or select a boundary with nodes, then click Apply BC.";
            return;
        }

        int dof = BcSymbolCatalog.ResolveApplyDegreeOfFreedom(bcType, PendingDegreeOfFreedom);
        foreach (int nodeId in nodeIdsToApply)
        {
            var analysisCategory = BoundaryConditionAnalysisCategories.Resolve(bcType);
            var existing = BoundaryConditions.FirstOrDefault(r =>
                r.NodeId == nodeId
                && r.BoundaryName == boundaryName
                && string.Equals(r.AnalysisCategory, analysisCategory, StringComparison.Ordinal));
            if (existing != null)
            {
                existing.BCType = bcType;
                existing.AnalysisCategory = analysisCategory;
                existing.DegreeOfFreedom = dof;
                existing.Value = value;
                existing.Description = $"{bcType} = {value}";
            }
            else
            {
                BoundaryConditions.Add(new BoundaryConditionRow
                {
                    NodeId = nodeId,
                    BoundaryName = boundaryName,
                    BCType = bcType,
                    AnalysisCategory = analysisCategory,
                    DegreeOfFreedom = dof,
                    Value = value,
                    Description = $"{bcType} = {value}"
                });
            }
        }

        // Drop temporary Vertices/Nodes drag-selection highlights; BC markers (Show Marker) stay if enabled.
        ClearManualImportHighlights();
        ClearBcImportSelection();
        RefreshAvailableNodeIds();
        CommandManager.InvalidateRequerySuggested();
        StatusMessage = $"Applied {bcType} to {nodeIdsToApply.Count} node(s) (boundary: {boundaryName}) — added to DataGrid.";
    }

    private void OnAddBoundaryCondition()
    {
        if (AvailableBCTypes.Count == 0)
        {
            StatusMessage = "No BC types available. Turn ON the active analysis category in Project Settings → Analysis.";
            MessageBox.Show(
                "No boundary-condition types are available for the current analysis category.\n\n" +
                "Turn ON that category in Project Settings → Analysis, then try again.",
                "Boundary Conditions",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RecordUndo("Add boundary condition");
        int nextId = BoundaryConditions.Count > 0 ? BoundaryConditions.Max(r => r.NodeId) + 1 : 1;
        var bcType = DefaultBcType!;
        BoundaryConditions.Add(new BoundaryConditionRow
        {
            NodeId = nextId,
            BoundaryName = "Manual",
            BCType = bcType,
            AnalysisCategory = BoundaryConditionAnalysisCategories.Resolve(bcType),
            DegreeOfFreedom = 0,
            Value = "0",
            Description = "Manual"
        });
        RefreshAvailableNodeIds();
    }

    private void OnClearBoundaryConditions()
    {
        RecordUndo("Clear boundary conditions");
        BoundaryConditions.Clear();
        SelectedBoundaryCondition = null;
        AvailableOuterBoundaries.Clear();
        SelectedOuterBoundary = null;
        NodesText = "";
        ConnectedNodesDisplayText = "";
        OnPropertyChanged(nameof(ConnectedNodesDisplayText));
        SelectedBCTypeForBoundary = DefaultBcType;
        BcValueForBoundary = "0";
        ClearManualImportHighlights();
        ClearBcImportSelection();
        GeometryDefinitionVM?.SetHighlightedLineSegment(null);
        RefreshAvailableNodeIds();
        CommandManager.InvalidateRequerySuggested();
        StatusMessage = "Cleared Apply BC to Outer Boundary and the boundary-conditions DataGrid.";
    }

    private void OnDeleteBoundaryCondition()
    {
        if (SelectedBoundaryCondition is BoundaryConditionRow row)
        {
            RecordUndo("Delete boundary condition");
            BoundaryConditions.Remove(row);
            RefreshAvailableNodeIds();
        }
    }

    private void OnUpdateBoundaryCondition()
    {
        if (SelectedBoundaryCondition is BoundaryConditionRow row)
        {
            row.AnalysisCategory = BoundaryConditionAnalysisCategories.Resolve(row.BCType);
            row.Description = $"{row.BCType} = {row.Value}";
        }
    }

    /// <summary>
    /// Fills empty <see cref="BoundaryConditionRow.AnalysisCategory"/> from BC type
    /// (legacy rows / imports).
    /// </summary>
    public void EnsureAnalysisCategoriesOnRows()
    {
        foreach (var row in BoundaryConditions)
        {
            if (string.IsNullOrWhiteSpace(row.AnalysisCategory))
                row.AnalysisCategory = BoundaryConditionAnalysisCategories.Resolve(row.BCType);
        }
    }

    private void OnAutoDetectBoundaryConditions()
    {
        StatusMessage = "Use Import Boundaries and Apply BC to assign conditions from mesh.";
    }

    /// <summary>
    /// Show/Hide Marker toggle. Draws selected family glyphs on the BC canvas using the
    /// Structural / Seepage checkboxes from the BC popup.
    /// </summary>
    private void OnToggleShowMarkers()
    {
        if (MarkersVisible)
        {
            MarkersVisible = false;
            StatusMessage = "Markers hidden.";
            return;
        }

        ShowMarkersOnCanvas();
    }

    /// <summary>After script mesh import, show BC markers on the Boundary Conditions canvas when rows exist.</summary>
    public void ShowMarkersAfterMeshImport()
    {
        if (MeshVM?.IsDiscretized != true || BoundaryConditions.Count == 0)
            return;
        ShowMarkersOnCanvas();
    }

    private void ShowMarkersOnCanvas()
    {
        var rows = MeshVM?.MeshInformationRows;
        if (rows == null || rows.Count == 0)
        {
            StatusMessage = "No 2D mesh available. Generate a mesh first, then click Show Marker.";
            return;
        }

        RefreshAvailableNodeIds();

        // Respect popup checkboxes; only auto-enable if the user cleared both.
        if (!ShowStructuralMarkers && !ShowSeepageMarkers)
        {
            ShowStructuralMarkers = true;
            ShowSeepageMarkers = true;
        }

        int markerCount = BoundaryConditions.Count(bc =>
        {
            if (bc.NodeId <= 0) return false;
            var kind = BcSymbolCatalog.ResolveKind(bc.BCType);
            var isSeepage = BcSymbolCatalog.IsSeepageKind(kind);
            if (isSeepage) return ShowSeepageMarkers;
            return ShowStructuralMarkers;
        });
        MarkersVisible = true;
        RefreshBcMarkerLegendItems();
        StatusMessage = markerCount == 0
            ? "Markers visible — no matching BCs for the selected families. Adjust Structural / Seepage above."
            : $"Showing markers for {markerCount} BC row(s) (selected families in BC Markers panel).";
    }

    /// <summary>Rebuild legend swatches from rows currently in the DataGrid.</summary>
    public void RefreshBcMarkerLegendItems()
    {
        var counts = new Dictionary<BcSymbolKind, int>();
        foreach (var row in BoundaryConditions)
        {
            if (row.NodeId <= 0) continue;
            var kind = BcSymbolCatalog.ResolveKind(row.BCType);
            var isSeepage = BcSymbolCatalog.IsSeepageKind(kind);
            if (isSeepage && !ShowSeepageMarkers) continue;
            if (!isSeepage && !ShowStructuralMarkers) continue;
            counts[kind] = counts.TryGetValue(kind, out var n) ? n + 1 : 1;
        }

        BcMarkerLegendItems.Clear();
        foreach (var kv in counts.OrderBy(k => (int)k.Key))
            BcMarkerLegendItems.Add(new BcMarkerLegendItem(kv.Key, kv.Value));
    }

    /// <summary>
    /// Node ID dropdown lists only mesh nodes on the currently selected outer boundary.
    /// When no boundary is selected (or it has no connected nodes), the list is empty.
    /// </summary>
    private void RefreshAvailableNodeIds()
    {
        AvailableNodeIds.Clear();
        if (SelectedOuterBoundary?.NodeIds is { Count: > 0 } ids)
        {
            foreach (int id in ids.Where(n => n > 0).Distinct().OrderBy(n => n))
                AvailableNodeIds.Add(id);
            return;
        }

        // Fallback: NodesText from Vertices/Nodes drag may still list targets before a boundary row is selected.
        if (!string.IsNullOrWhiteSpace(NodesText))
        {
            foreach (var part in NodesText.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int nodeId)
                    && nodeId > 0
                    && !AvailableNodeIds.Contains(nodeId))
                    AvailableNodeIds.Add(nodeId);
            }
            if (AvailableNodeIds.Count > 0)
            {
                var ordered = AvailableNodeIds.OrderBy(n => n).ToList();
                AvailableNodeIds.Clear();
                foreach (int id in ordered)
                    AvailableNodeIds.Add(id);
            }
        }
    }

    private void OnMaterialPropertiesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaterialPropertiesViewModel.SelectedAnalysisCategory)
            or null or "")
        {
            RefreshAvailableBcTypes();
            OnPropertyChanged(nameof(ShowDofColumn));
        }
    }

    private void OnProjectSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectSettingsViewModel.IsActiveAnalysisCategoryOn)
            or nameof(ProjectSettingsViewModel.AreSelectedCategoryTypeOptionsEnabled)
            or nameof(ProjectSettingsViewModel.AreGroundwaterTypeOptionsEnabled)
            or null or "")
        {
            RefreshAvailableBcTypes();
            OnPropertyChanged(nameof(ShowDofColumn));
        }
    }

    private bool IsAnalysisCategoryOn(string? category) =>
        _projectSettingsVM == null || _projectSettingsVM.IsCategoryOn(category);

    /// <summary>
    /// True when Flow-Seepage/Groundwater category ON is checked (seepage BC types are offered).
    /// </summary>
    private bool IsGroundwaterCategoryOn =>
        IsAnalysisCategoryOn(MaterialPropertiesViewModel.GroundwaterAnalysisCategory);

    /// <summary>
    /// True when any non-seepage analysis heading is ON (structural Fixed/Roller/… types are offered).
    /// </summary>
    private bool IsAnyStructuralCategoryOn
    {
        get
        {
            var cats = _materialPropertiesVM?.AnalysisCategories;
            if (cats == null || cats.Count == 0)
            {
                // No category list yet — fall back to the currently selected non-GW category.
                var selected = _materialPropertiesVM?.SelectedAnalysisCategory;
                return !string.IsNullOrEmpty(selected)
                    && !MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(selected)
                    && IsAnalysisCategoryOn(selected);
            }

            foreach (var cat in cats)
            {
                if (MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(cat))
                    continue;
                if (IsAnalysisCategoryOn(cat))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Seepage BC types are listed whenever the GW heading is ON (independent of tree selection).</summary>
    private bool IsSeepageBcModeActive => IsGroundwaterCategoryOn;

    /// <summary>Structural BC types are listed whenever any non-GW heading is ON.</summary>
    private bool UsesStructuralBcTypes => IsAnyStructuralCategoryOn;

    /// <summary>
    /// DOF (X/Y) applies to structural fixity. Shown whenever structural BC types are available
    /// (including when seepage types are also listed from another ON heading).
    /// </summary>
    public bool ShowDofColumn => UsesStructuralBcTypes;

    /// <summary>True when the BC Type dropdown has at least one type from any ON analysis heading.</summary>
    public bool HasAvailableBcTypes => AvailableBCTypes.Count > 0;

    public bool ShowStructuralBcHelp => UsesStructuralBcTypes;
    public bool ShowSeepageBcHelp => IsSeepageBcModeActive;
    public bool ShowNoBcTypesHelp => !HasAvailableBcTypes;

    public string NoBcTypesHelpMessage
    {
        get
        {
            if (_materialPropertiesVM?.AnalysisCategories is { Count: > 0 } cats
                && cats.All(c => !IsAnalysisCategoryOn(c)))
            {
                return "Turn ON at least one analysis category heading in Project Settings → Analysis to enable its boundary-condition types.";
            }

            var cat = _materialPropertiesVM?.SelectedAnalysisCategory;
            if (string.IsNullOrEmpty(cat))
                return "Select an analysis category in Project Settings → Analysis and turn it ON to see BC types.";
            if (!IsAnalysisCategoryOn(cat)
                && !IsAnyStructuralCategoryOn
                && !IsGroundwaterCategoryOn)
                return $"Turn ON \"{cat}\" (or another analysis heading) in Project Settings → Analysis to enable its boundary-condition types.";
            return "No boundary-condition types are available for the current analysis selection.";
        }
    }

    /// <summary>
    /// BC Type dropdown lists the union of BC types for every Project Settings → Analysis
    /// category heading that is ON (e.g. SRF + Flow-Seepage → structural and seepage types).
    /// When no heading is ON (typical Analysis Workspace until Apply on Analysis Type),
    /// keep the existing structural and seepage families listed so Type is not blank.
    /// </summary>
    public void RefreshAvailableBcTypes()
    {
        var combined = new List<string>();
        if (UsesStructuralBcTypes)
            combined.AddRange(StructuralBcTypes);
        if (IsSeepageBcModeActive)
            combined.AddRange(SeepageBcTypes);
        if (combined.Count == 0)
        {
            combined.AddRange(StructuralBcTypes);
            combined.AddRange(SeepageBcTypes);
        }

        AvailableBCTypes.Clear();
        foreach (var t in combined)
            AvailableBCTypes.Add(t);

        if (string.IsNullOrEmpty(SelectedBCTypeForBoundary)
            || !AvailableBCTypes.Contains(SelectedBCTypeForBoundary))
            SelectedBCTypeForBoundary = AvailableBCTypes.FirstOrDefault();

        OnPropertyChanged(nameof(AvailableBCTypes));
        OnPropertyChanged(nameof(ShowDofColumn));
        OnPropertyChanged(nameof(HasAvailableBcTypes));
        OnPropertyChanged(nameof(ShowStructuralBcHelp));
        OnPropertyChanged(nameof(ShowSeepageBcHelp));
        OnPropertyChanged(nameof(ShowNoBcTypesHelp));
        OnPropertyChanged(nameof(NoBcTypesHelpMessage));
    }

    private string? DefaultBcType => AvailableBCTypes.FirstOrDefault();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
