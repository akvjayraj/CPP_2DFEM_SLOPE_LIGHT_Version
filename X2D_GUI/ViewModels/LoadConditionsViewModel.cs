using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Services;
using System;
using System.Collections.Generic;

namespace GeotechSlopeApp.ViewModels;

public class LoadConditionsViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<LoadModel> _filteredLoads = new();
    private readonly ObservableCollection<LineLoadOverlayDisplay> _lineLoadOverlays = new();
    private readonly ObservableCollection<PointLoadOverlayDisplay> _pointLoadOverlays = new();
    private readonly HashSet<LoadModel> _hookedLoads = new();
    private readonly ObservableCollection<MeshNodeDisplayItem> _loadCanvasMeshNodeDisplayItems = new();
    private readonly ObservableCollection<MeshElementDisplay> _loadCanvasMeshElements = new();
    private PointCollection _highlightedTractionEdgePoints = new();
    private LoadModel? _selectedLoad;
    /// <summary>When non-null, next canvas click sets End (X,Y) for this line load. Cleared when End is set or selection changes.</summary>
    private LoadModel? _linePlacementAwaitingEnd;
    private MeshViewModel? _mesh;
    private ProjectSettingsViewModel? _projectSettings;
    private StagedConstructionViewModel? _stagedConstruction;
    private FEAnalysisViewModel? _feAnalysis;
    private bool _geometrySubscribed;
    private bool _refreshingLoadCanvasMesh;
    private bool _refreshingLineLoadOverlays;
    private bool _isSeismicExpanderExpanded;
    private StageDefinition? _selectedSeismicStageForEdit;
    private readonly ObservableCollection<StageDefinition> _seismicStageDefinitions = new();
    /// <summary>Non-staged SRF / script-only pseudo-static edit target when .dat has k_h/k_v but no Seismic / Dynamic stage row.</summary>
    private readonly StageDefinition _scriptPseudoStaticHolder = new()
    {
        StageName = "Script Editor (.dat)",
        StageType = "Seismic / Dynamic",
        StageNumber = 0,
    };
    private bool _scriptPseudoStaticCoeffsActive;
    private bool _seismicAnalysisEnabled;
    private bool _restoringSeismicState;
    private IProjectUndoHost? _undoHost;
    private readonly CoalescingUndoRecorder _loadEditUndo = new();

    /// <summary>Raised when script-level or staged k_h/k_v change (Script Editor sync).</summary>
    public event EventHandler? PseudoStaticCoefficientsChanged;

    /// <summary>Raised when the applied loads list or key load values change (FE Overview summary).</summary>
    public event EventHandler? AppliedLoadsChanged;

    /// <summary>
    /// Project-level field stress (Constant / Gravity; one global default + optional per-material
    /// overrides). Lives on the Load Conditions tab to mirror RS2's placement under the Loading menu.
    /// </summary>
    public FieldStressViewModel FieldStress { get; }

    /// <summary>Newmark sliding-block permanent displacement (alternative to pseudo-static).</summary>
    public FeNewmarkSettings Newmark { get; } = new();

    public LoadConditionsViewModel()
    {
        FieldStress = new FieldStressViewModel();
        FieldStress.SelectedTractionEdgeChanged += (_, _) => RefreshHighlightedTractionEdge();
        Loads = new ObservableCollection<LoadModel>();
        if (Loads.Count == 0)
            Loads.Add(new LoadModel { Type = LoadType.Point, Magnitude = 10 });
        // Keep FilteredLoads in sync with Loads even when external code (persistence, scripts) mutates the collection
        // without going through Add/Remove load commands.
        Loads.CollectionChanged += OnLoadsCollectionChanged;
        foreach (var load in Loads)
            HookLoad(load);
        RefreshFilteredLoads();
        AddLoadCommand = new RelayCommand(() => AddLoadOfType(LoadType.Point));
        AddLineLoadCommand = new RelayCommand(() => AddLoadOfType(LoadType.Line));
        RemoveLoadCommand = new RelayCommand<LoadModel?>(OnRemoveLoad, _ => SelectedLoad != null);
        _scriptPseudoStaticHolder.PropertyChanged += OnScriptPseudoStaticHolderChanged;
    }

    private void OnScriptPseudoStaticHolderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StageDefinition.PseudoStaticKh)
            or nameof(StageDefinition.PseudoStaticKv))
        {
            OnPropertyChanged(nameof(SeismicLegendKh));
            OnPropertyChanged(nameof(SeismicLegendKv));
            PseudoStaticCoefficientsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True when the Script Editor .dat declares k_h/k_v (non-staged SRF decks).</summary>
    public bool ScriptPseudoStaticCoeffsActive => _scriptPseudoStaticCoeffsActive;

    /// <summary>User toggle: enable pseudo-static seismic via Script Editor .dat (no stage row required).</summary>
    public bool SeismicAnalysisEnabled
    {
        get => _seismicAnalysisEnabled;
        set
        {
            if (_seismicAnalysisEnabled == value)
                return;
            _seismicAnalysisEnabled = value;
            OnPropertyChanged();
            if (_restoringSeismicState)
                return;
            if (value)
                ActivateSeismicFromCheckbox();
            else
                DeactivateSeismicFromCheckbox();
            RefreshSeismicUiState();
        }
    }

    /// <summary>Import script .dat k_h/k_v into the script-level holder (no staged seismic row required).</summary>
    public void SetScriptPseudoStaticCoefficients(double kh, double kv)
    {
        bool changed = !_scriptPseudoStaticCoeffsActive
            || Math.Abs(_scriptPseudoStaticHolder.PseudoStaticKh - kh) > 1e-12
            || Math.Abs(_scriptPseudoStaticHolder.PseudoStaticKv - kv) > 1e-12;
        _scriptPseudoStaticCoeffsActive = true;
        _scriptPseudoStaticHolder.PseudoStaticKh = kh;
        _scriptPseudoStaticHolder.PseudoStaticKv = kv;
        if (!_restoringSeismicState
            && (Math.Abs(kh) > 1e-12 || Math.Abs(kv) > 1e-12))
        {
            _seismicAnalysisEnabled = true;
            OnPropertyChanged(nameof(SeismicAnalysisEnabled));
        }
        if (changed)
        {
            RefreshSeismicUiState();
            PseudoStaticCoefficientsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ClearScriptPseudoStaticCoeffs()
    {
        if (!_scriptPseudoStaticCoeffsActive)
            return;
        _scriptPseudoStaticCoeffsActive = false;
        if (!_seismicAnalysisEnabled && _feAnalysis?.HasSeismicDynamicStage != true)
        {
            _scriptPseudoStaticHolder.PseudoStaticKh = 0;
            _scriptPseudoStaticHolder.PseudoStaticKv = 0;
        }
        RefreshSeismicUiState();
    }

    /// <summary>Project file restore for script-level seismic enable + coefficients.</summary>
    public void RestoreSeismicLoadState(bool enabled, double kh, double kv)
    {
        _restoringSeismicState = true;
        try
        {
            _seismicAnalysisEnabled = enabled;
            OnPropertyChanged(nameof(SeismicAnalysisEnabled));
            if (enabled || Math.Abs(kh) > 1e-12 || Math.Abs(kv) > 1e-12)
            {
                _scriptPseudoStaticCoeffsActive = true;
                _scriptPseudoStaticHolder.PseudoStaticKh = kh;
                _scriptPseudoStaticHolder.PseudoStaticKv = kv;
            }
            else
            {
                _scriptPseudoStaticCoeffsActive = false;
                _scriptPseudoStaticHolder.PseudoStaticKh = 0;
                _scriptPseudoStaticHolder.PseudoStaticKv = 0;
            }
            RefreshSeismicUiState();
        }
        finally
        {
            _restoringSeismicState = false;
        }
    }

    private void ActivateSeismicFromCheckbox()
    {
        _scriptPseudoStaticCoeffsActive = true;
        if (Math.Abs(_scriptPseudoStaticHolder.PseudoStaticKh) < 1e-12
            && Math.Abs(_scriptPseudoStaticHolder.PseudoStaticKv) < 1e-12)
        {
            _scriptPseudoStaticHolder.PseudoStaticKh = 0.1;
            _scriptPseudoStaticHolder.PseudoStaticKv = 0;
        }
        IsSeismicExpanderExpanded = true;
        PseudoStaticCoefficientsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DeactivateSeismicFromCheckbox()
    {
        _scriptPseudoStaticCoeffsActive = false;
        if (_feAnalysis?.HasSeismicDynamicStage != true)
        {
            _scriptPseudoStaticHolder.PseudoStaticKh = 0;
            _scriptPseudoStaticHolder.PseudoStaticKv = 0;
        }
        PseudoStaticCoefficientsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Wire the Field Stress override picker to the Material Properties materials list so the
    /// "Advanced…" dialog can offer the user's defined materials by name. Called once at
    /// MainWindowViewModel startup (materials are created before LoadConditions).
    /// </summary>
    public void SetMaterialsSource(MaterialPropertiesViewModel? materialsVM)
        => FieldStress.SetMaterialsSource(materialsVM);

    /// <summary>
    /// Subscribes to staged construction so the load grid / canvas / .dat show ONLY the active-stage loads
    /// (strict per-footer-tab isolation: a load applied in Stage N is invisible in every other stage).
    /// </summary>
    public void SetStagedConstruction(StagedConstructionViewModel? staged)
    {
        if (_stagedConstruction != null)
            _stagedConstruction.PropertyChanged -= OnStagedConstructionPropertyChanged;
        _stagedConstruction = staged;
        if (_stagedConstruction != null)
            _stagedConstruction.PropertyChanged += OnStagedConstructionPropertyChanged;
        MigrateLegacyLoadsIfStagingActive();
        RefreshSeismicUiState();
        RefreshFilteredLoads();
    }

    /// <summary>Wires Project Settings stage list for seismic load gating and per-stage k_h/k_v.</summary>
    public void SetFeAnalysisSource(FEAnalysisViewModel? feAnalysis)
    {
        if (_feAnalysis != null)
        {
            _feAnalysis.PropertyChanged -= OnFeAnalysisPropertyChanged;
            _feAnalysis.StageDefinitions.CollectionChanged -= OnStageDefinitionsCollectionChanged;
            foreach (var s in _feAnalysis.StageDefinitions)
                s.PropertyChanged -= OnStageDefinitionPropertyChanged;
        }
        _feAnalysis = feAnalysis;
        if (_feAnalysis != null)
        {
            _feAnalysis.PropertyChanged += OnFeAnalysisPropertyChanged;
            _feAnalysis.StageDefinitions.CollectionChanged += OnStageDefinitionsCollectionChanged;
            foreach (var s in _feAnalysis.StageDefinitions)
                s.PropertyChanged += OnStageDefinitionPropertyChanged;
        }
        RefreshSeismicUiState();
    }

    private void OnFeAnalysisPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FEAnalysisViewModel.StagedExcavationEnabled)
            or nameof(FEAnalysisViewModel.HasSeismicDynamicStage))
            RefreshSeismicUiState();
    }

    private void OnStageDefinitionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (StageDefinition s in e.OldItems)
                s.PropertyChanged -= OnStageDefinitionPropertyChanged;
        if (e.NewItems != null)
            foreach (StageDefinition s in e.NewItems)
                s.PropertyChanged += OnStageDefinitionPropertyChanged;
        RefreshSeismicUiState();
    }

    private void OnStageDefinitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StageDefinition.StageType)
            or nameof(StageDefinition.PseudoStaticKh)
            or nameof(StageDefinition.PseudoStaticKv))
            RefreshSeismicUiState();
    }

    private void OnStagedConstructionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StagedConstructionViewModel.ActiveStage)
            or nameof(StagedConstructionViewModel.ActiveStageNumber)
            or nameof(StagedConstructionViewModel.ActiveStageName))
        {
            MigrateLegacyLoadsIfStagingActive();
            RefreshSeismicUiState();
            RefreshFilteredLoads();
        }
    }

    private void RefreshSeismicUiState()
    {
        _seismicStageDefinitions.Clear();
        if (_feAnalysis != null)
        {
            foreach (var s in _feAnalysis.StageDefinitions.Where(s => s.IsSeismicDynamicStage))
                _seismicStageDefinitions.Add(s);
        }

        OnPropertyChanged(nameof(ActiveStageNumber));
        OnPropertyChanged(nameof(ActiveStageDisplay));
        OnPropertyChanged(nameof(IsStagedActive));
        OnPropertyChanged(nameof(SeismicAnalysisEnabled));
        OnPropertyChanged(nameof(IsSeismicLoadUiEnabled));
        OnPropertyChanged(nameof(AreSeismicCoefficientsEditable));
        OnPropertyChanged(nameof(ScriptPseudoStaticCoeffsActive));
        OnPropertyChanged(nameof(SeismicStageDefinitions));
        OnPropertyChanged(nameof(ActiveSeismicStageDefinition));
        OnPropertyChanged(nameof(IsEditingActiveSeismicStage));
        OnPropertyChanged(nameof(ShowSeismicStagePicker));
        OnPropertyChanged(nameof(SeismicDisabledHint));
        OnPropertyChanged(nameof(SeismicEditTarget));
        OnPropertyChanged(nameof(SeismicLegendKh));
        OnPropertyChanged(nameof(SeismicLegendKv));
        SyncSelectedSeismicStageForEdit();
    }

    private void SyncSelectedSeismicStageForEdit()
    {
        var seismic = SeismicStageDefinitions;
        if (seismic.Count == 0)
        {
            SelectedSeismicStageForEdit = null;
            return;
        }
        if (ActiveSeismicStageDefinition != null)
            SelectedSeismicStageForEdit = ActiveSeismicStageDefinition;
        else if (SelectedSeismicStageForEdit == null || !seismic.Contains(SelectedSeismicStageForEdit))
            SelectedSeismicStageForEdit = seismic[0];
    }

    /// <summary>Enabled when checkbox, Seismic / Dynamic stage, or imported .dat declares k_h/k_v.</summary>
    public bool IsSeismicLoadUiEnabled =>
        _seismicAnalysisEnabled
        || _feAnalysis?.HasSeismicDynamicStage == true
        || _scriptPseudoStaticCoeffsActive;

    /// <summary>True when k_h/k_v fields and canvas legend are active.</summary>
    public bool AreSeismicCoefficientsEditable => IsSeismicLoadUiEnabled;

    public ObservableCollection<StageDefinition> SeismicStageDefinitions => _seismicStageDefinitions;

    public StageDefinition? ActiveSeismicStageDefinition
    {
        get
        {
            if (_feAnalysis == null || ActiveStageNumber <= 0) return null;
            return _feAnalysis.StageDefinitions.FirstOrDefault(
                s => s.StageNumber == ActiveStageNumber && s.IsSeismicDynamicStage);
        }
    }

    public bool IsEditingActiveSeismicStage => ActiveSeismicStageDefinition != null;

    public bool ShowSeismicStagePicker => IsSeismicLoadUiEnabled && !IsEditingActiveSeismicStage && SeismicStageDefinitions.Count > 0;

    public string SeismicDisabledHint =>
        AreSeismicCoefficientsEditable
            ? string.Empty
            : "Check \"Enable seismic analysis\" to apply pseudo-static k_h/k_v to the solver (.dat), "
              + "or add a Seismic / Dynamic stage under Project Settings → Analysis Type → Stage Analysis.";

    public StageDefinition? SelectedSeismicStageForEdit
    {
        get => _selectedSeismicStageForEdit;
        set
        {
            _selectedSeismicStageForEdit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SeismicEditTarget));
            OnPropertyChanged(nameof(SeismicLegendKh));
            OnPropertyChanged(nameof(SeismicLegendKv));
        }
    }

    /// <summary>Stage whose k_h/k_v fields are bound in the popup (active seismic stage, picker, or script .dat).</summary>
    public StageDefinition? SeismicEditTarget =>
        ActiveSeismicStageDefinition
        ?? SelectedSeismicStageForEdit
        ?? (_seismicAnalysisEnabled || _scriptPseudoStaticCoeffsActive ? _scriptPseudoStaticHolder : null);

    public double SeismicLegendKh => SeismicEditTarget?.PseudoStaticKh ?? 0.0;
    public double SeismicLegendKv => SeismicEditTarget?.PseudoStaticKv ?? 0.0;

    public bool IsSeismicExpanderExpanded
    {
        get => _isSeismicExpanderExpanded;
        set { _isSeismicExpanderExpanded = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Strict isolation: any pre-stage load with <see cref="LoadModel.StageNumber"/> = null is parked on the
    /// lowest defined stage the instant staging becomes active, so it remains visible in exactly one footer tab
    /// instead of vanishing or leaking into every tab.
    /// </summary>
    private void MigrateLegacyLoadsIfStagingActive()
    {
        if (_stagedConstruction == null || _stagedConstruction.Stages.Count == 0) return;
        int firstStage = _stagedConstruction.Stages.Min(s => s.StageNumber);
        foreach (var load in Loads)
            if (load.StageNumber == null)
                load.StageNumber = firstStage;
    }

    /// <summary>Stage number of the currently active footer tab; 0 = no staging in effect (or no stages defined).</summary>
    public int ActiveStageNumber => _stagedConstruction?.ActiveStageNumber ?? 0;

    /// <summary>True when a real staged-construction stage is selected; loads then partition by stage.</summary>
    public bool IsStagedActive => ActiveStageNumber > 0;

    /// <summary>"Stage 2 — Excavation" style label for the load grid status banner; falls back when no staging.</summary>
    public string ActiveStageDisplay
    {
        get
        {
            if (_stagedConstruction == null || _stagedConstruction.ActiveStage == null || ActiveStageNumber <= 0)
                return "All loads (no staged construction active)";
            return $"Stage {ActiveStageNumber}: {_stagedConstruction.ActiveStage.StageName}";
        }
    }

    public GeometryDefinitionViewModel GeometryDefinition { get; set; } = new();
    public ObservableCollection<LoadModel> Loads { get; }
    public ObservableCollection<LoadModel> FilteredLoads => _filteredLoads;

    /// <summary>RS2D-style line load graphics for the active stage.</summary>
    public ObservableCollection<LineLoadOverlayDisplay> LineLoadOverlays => _lineLoadOverlays;

    /// <summary>RS2D-style point load graphics for the active stage.</summary>
    public ObservableCollection<PointLoadOverlayDisplay> PointLoadOverlays => _pointLoadOverlays;

    /// <summary>Mesh nodes on the load canvas (1D + discretized), canvas coordinates.</summary>
    public ObservableCollection<MeshNodeDisplayItem> LoadCanvasMeshNodeDisplayItems => _loadCanvasMeshNodeDisplayItems;

    /// <summary>Mesh elements on the load canvas (1D + discretized), canvas coordinates.</summary>
    public ObservableCollection<MeshElementDisplay> LoadCanvasMeshElements => _loadCanvasMeshElements;

    /// <summary>
    /// Canvas-space polyline (2 points for T3/Q4 linear edges, 3 points A→C→B for T6/Q8
    /// quadratic edges) for the traction edge currently selected in the Field Stress
    /// Traction DataGrid. Empty when no row is selected or the referenced node IDs are
    /// not present in <see cref="LoadCanvasMeshNodeDisplayItems"/>. The Load Conditions
    /// canvas binds a Polyline to this.
    /// </summary>
    public PointCollection HighlightedTractionEdgePoints
    {
        get => _highlightedTractionEdgePoints;
        private set
        {
            _highlightedTractionEdgePoints = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHighlightedTractionEdge));
        }
    }

    /// <summary>True when the canvas should render the highlighted traction edge overlay.</summary>
    public bool HasHighlightedTractionEdge => _highlightedTractionEdgePoints.Count >= 2;

    /// <summary>True when this problem uses the 2D load canvas (1D bar, 2D plane, or 3D with same 2D sketch).</summary>
    private bool IsLoadCanvasMeshDimension =>
        _projectSettings == null ||
        _projectSettings.SelectedDimension is ProblemDimension.OneD
            or ProblemDimension.TwoD
            or ProblemDimension.ThreeD;

    /// <summary>
    /// True whenever a mesh from the Mesh tab is available for the load canvas (dimension is supported and
    /// the Mesh tab has discretized). Drives the always-on mesh wireframe (element polygons + midside dots),
    /// matching the Mesh tab where the outline is shown regardless of the Show elements / Show nodes toggles.
    /// </summary>
    public bool HasLoadCanvasMesh =>
        IsLoadCanvasMeshDimension &&
        _mesh != null && _mesh.IsDiscretized;

    /// <summary>True when mesh exists, dimension is supported, and Mesh tab has nodes visible (same toggles as Mesh tab).</summary>
    public bool ShowLoadCanvasMeshNodes =>
        IsLoadCanvasMeshDimension &&
        _mesh != null && _mesh.IsDiscretized && _mesh.ShowMeshNodes;

    /// <summary>True when mesh exists, dimension is supported, and Mesh tab has elements visible.</summary>
    public bool ShowLoadCanvasMeshElements =>
        IsLoadCanvasMeshDimension &&
        _mesh != null && _mesh.IsDiscretized && _mesh.ShowMeshElements;

    /// <summary>Called from MainWindowViewModel so the load canvas can show 1D mesh and snap point loads to nodes.</summary>
    public void SetMeshAndProject(MeshViewModel? mesh, ProjectSettingsViewModel? projectSettings)
    {
        UnsubscribeMeshAndProject();
        _mesh = mesh;
        _projectSettings = projectSettings;
        if (_mesh != null)
        {
            _mesh.PropertyChanged += OnMeshPropertyChanged;
            _mesh.DiscretizationNodes.CollectionChanged += OnMeshCollectionChanged;
            _mesh.MeshElements.CollectionChanged += OnMeshCollectionChanged;
            _mesh.MeshInformationRows.CollectionChanged += OnMeshCollectionChanged;
        }
        if (_projectSettings != null)
            _projectSettings.PropertyChanged += OnProjectSettingsPropertyChanged;
        if (!_geometrySubscribed)
        {
            GeometryDefinition.PropertyChanged += OnGeometryPropertyChanged;
            _geometrySubscribed = true;
        }
        NotifyLoadCanvasMeshVisibility();
        RefreshLoadCanvasMeshDisplay();
    }

    public void SetUndoHost(IProjectUndoHost? host)
    {
        _undoHost = host;
        _loadEditUndo.SetHost(host);
    }

    private void RecordUndo(string description) => _undoHost?.RecordUndoPoint(description);

    private void UnsubscribeMeshAndProject()
    {
        if (_mesh != null)
        {
            _mesh.PropertyChanged -= OnMeshPropertyChanged;
            _mesh.DiscretizationNodes.CollectionChanged -= OnMeshCollectionChanged;
            _mesh.MeshElements.CollectionChanged -= OnMeshCollectionChanged;
            _mesh.MeshInformationRows.CollectionChanged -= OnMeshCollectionChanged;
        }
        if (_projectSettings != null)
            _projectSettings.PropertyChanged -= OnProjectSettingsPropertyChanged;
        _mesh = null;
        _projectSettings = null;
    }

    private void OnMeshPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MeshViewModel.IsDiscretized) or nameof(MeshViewModel.DiscretizationNodes) or nameof(MeshViewModel.MeshElements) or nameof(MeshViewModel.UserDefinedHighlightedNodeIds))
            RefreshLoadCanvasMeshDisplay();
        if (e.PropertyName is nameof(MeshViewModel.ShowMeshNodes) or nameof(MeshViewModel.ShowMeshElements) or nameof(MeshViewModel.IsDiscretized))
            NotifyLoadCanvasMeshVisibility();
    }

    private void OnMeshCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshLoadCanvasMeshDisplay();

    private void OnProjectSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettingsViewModel.SelectedDimension))
        {
            NotifyLoadCanvasMeshVisibility();
            RefreshLoadCanvasMeshDisplay();
        }
    }

    private void OnGeometryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // GeometryDefinitionVM re-notifies LineLoadOverlays when our overlays refresh;
        // ignore that echo or RefreshLoadCanvasMeshDisplay ↔ RefreshLineLoadOverlays recurse forever.
        if (e.PropertyName == nameof(GeometryDefinitionViewModel.LineLoadOverlays)
            || e.PropertyName == nameof(GeometryDefinitionViewModel.PointLoadOverlays))
            return;

        if (_mesh is { IsDiscretized: true })
            RefreshLoadCanvasMeshDisplay();
    }

    private void NotifyLoadCanvasMeshVisibility()
    {
        OnPropertyChanged(nameof(HasLoadCanvasMesh));
        OnPropertyChanged(nameof(ShowLoadCanvasMeshNodes));
        OnPropertyChanged(nameof(ShowLoadCanvasMeshElements));
    }

    private void RefreshLoadCanvasMeshDisplay()
    {
        if (_refreshingLoadCanvasMesh)
            return;

        _refreshingLoadCanvasMesh = true;
        try
        {
            _loadCanvasMeshNodeDisplayItems.Clear();
            _loadCanvasMeshElements.Clear();
            if (!IsLoadCanvasMeshDimension || _mesh is not { IsDiscretized: true })
            {
                OnPropertyChanged(nameof(LoadCanvasMeshNodeDisplayItems));
                OnPropertyChanged(nameof(LoadCanvasMeshElements));
                RefreshLineLoadOverlays();
                return;
            }
            var geom = GeometryDefinition;
            for (int i = 0; i < _mesh.DiscretizationNodes.Count; i++)
            {
                var n = _mesh.DiscretizationNodes[i];
                var canvasPt = geom.ConvertAxisToCanvas(new Point(n.Location.X, n.Location.Y));
                _loadCanvasMeshNodeDisplayItems.Add(new MeshNodeDisplayItem
                {
                    X = canvasPt.X,
                    Y = canvasPt.Y,
                    NodeId = n.NodeId > 0 ? n.NodeId : i + 1
                });
            }
            var rowByElementId = _mesh.MeshInformationRows
                .GroupBy(r => r.ElementId)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var el in _mesh.MeshElements)
            {
                rowByElementId.TryGetValue(el.DisplayId, out var infoRow);
                _loadCanvasMeshElements.Add(
                    MeshCanvasDisplayConverter.ToCanvasElement(el, infoRow, geom.ConvertAxisToCanvas));
            }
            OnPropertyChanged(nameof(LoadCanvasMeshNodeDisplayItems));
            OnPropertyChanged(nameof(LoadCanvasMeshElements));
            // Mesh/geometry just changed — re-resolve the selected traction edge so the
            // overlay tracks the new node coordinates instead of pointing into stale space.
            RefreshHighlightedTractionEdge();
            RefreshLineLoadOverlays();
        }
        finally
        {
            _refreshingLoadCanvasMesh = false;
        }
    }

    /// <summary>
    /// Rebuilds <see cref="HighlightedTractionEdgePoints"/> from the currently selected
    /// traction edge. Called both when the user picks a different row in the DataGrid
    /// (via <see cref="FieldStressViewModel.SelectedTractionEdgeChanged"/>) and when the
    /// mesh / geometry refreshes (so the overlay stays anchored to the right node).
    /// Node IDs are 1-based and resolved against <see cref="LoadCanvasMeshNodeDisplayItems"/>;
    /// when any referenced node is missing the overlay is cleared rather than emitting a
    /// dangling line into unmapped canvas space.
    /// </summary>
    private void RefreshHighlightedTractionEdge()
    {
        var edge = FieldStress.SelectedTractionEdge;
        if (edge == null || _loadCanvasMeshNodeDisplayItems.Count == 0)
        {
            if (_highlightedTractionEdgePoints.Count != 0)
                HighlightedTractionEdgePoints = new PointCollection();
            return;
        }

        bool TryGet(int nodeId, out Point pt)
        {
            int idx = nodeId - 1;
            if (idx < 0 || idx >= _loadCanvasMeshNodeDisplayItems.Count) { pt = default; return false; }
            var n = _loadCanvasMeshNodeDisplayItems[idx];
            pt = new Point(n.X, n.Y);
            return true;
        }

        if (!TryGet(edge.NodeA, out var a) || !TryGet(edge.NodeB, out var b))
        {
            if (_highlightedTractionEdgePoints.Count != 0)
                HighlightedTractionEdgePoints = new PointCollection();
            return;
        }

        var pts = new PointCollection();
        if (edge.NodeC > 0 && TryGet(edge.NodeC, out var c))
        {
            // Quadratic edge (T6 / Q8): walk through the mid-edge node so the overlay
            // visually matches the curved element side rather than straight-cutting the chord.
            pts.Add(a); pts.Add(c); pts.Add(b);
        }
        else
        {
            pts.Add(a); pts.Add(b);
        }
        HighlightedTractionEdgePoints = pts;
    }

    public LoadModel? SelectedLoad
    {
        get => _selectedLoad;
        set
        {
            _selectedLoad = value;
            if (_linePlacementAwaitingEnd != value)
                _linePlacementAwaitingEnd = null;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand AddLoadCommand { get; }
    public ICommand AddLineLoadCommand { get; }
    public ICommand RemoveLoadCommand { get; }

    private void RefreshFilteredLoads()
    {
        _filteredLoads.Clear();
        int active = ActiveStageNumber;
        foreach (var load in Loads)
        {
            // Strict per-footer-tab isolation:
            //  - staging active (active > 0): show ONLY loads tagged with that exact stage. Legacy null loads
            //    were already migrated to the first defined stage by MigrateLegacyLoadsIfStagingActive,
            //    so they show in exactly one tab.
            //  - no staging (active == 0): show everything (single bucket).
            if (active <= 0
                ? true
                : load.StageNumber == active)
                _filteredLoads.Add(load);
        }
        if (SelectedLoad != null && !_filteredLoads.Contains(SelectedLoad))
            SelectedLoad = null;
        OnPropertyChanged(nameof(FilteredLoads));
        RefreshLineLoadOverlays();
    }

    private void OnLoadsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (LoadModel load in e.NewItems)
                HookLoad(load);
        }

        if (e.OldItems != null)
        {
            foreach (LoadModel load in e.OldItems)
                UnhookLoad(load);
        }

        RefreshFilteredLoads();
        AppliedLoadsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HookLoad(LoadModel load)
    {
        if (!_hookedLoads.Add(load))
            return;
        load.PropertyChanged += OnLoadPropertyChanged;
    }

    private void UnhookLoad(LoadModel load)
    {
        if (!_hookedLoads.Remove(load))
            return;
        load.PropertyChanged -= OnLoadPropertyChanged;
    }

    private void OnLoadPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LoadModel.Magnitude)
            or nameof(LoadModel.EndMagnitude)
            or nameof(LoadModel.Direction)
            or nameof(LoadModel.StartX)
            or nameof(LoadModel.StartY)
            or nameof(LoadModel.EndX)
            or nameof(LoadModel.EndY)
            or nameof(LoadModel.Type)
            or nameof(LoadModel.StageNumber)
            or nameof(LoadModel.AppliedToNodeId)
            or nameof(LoadModel.IsEnabled)
            or nameof(LoadModel.Name)
            or nameof(LoadModel.Orientation)
            or nameof(LoadModel.AngleDeg))
        {
            RefreshLineLoadOverlays();
            AppliedLoadsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RefreshLineLoadOverlays()
    {
        if (_refreshingLineLoadOverlays)
            return;

        _refreshingLineLoadOverlays = true;
        try
        {
            _lineLoadOverlays.Clear();
            _pointLoadOverlays.Clear();
            var dim = _projectSettings?.SelectedDimension ?? ProblemDimension.TwoD;
            foreach (var load in _filteredLoads)
            {
                if (!load.IsEnabled)
                    continue;
                if (load.IsDistributedLineLoad)
                {
                    var overlay = LineLoadOverlayBuilder.Build(load, _mesh, GeometryDefinition, dim);
                    if (overlay != null)
                        _lineLoadOverlays.Add(overlay);
                }
                else if (load.IsPointLoad)
                {
                    var overlay = PointLoadOverlayBuilder.Build(load, _mesh, GeometryDefinition, dim);
                    if (overlay != null)
                        _pointLoadOverlays.Add(overlay);
                }
            }

            OnPropertyChanged(nameof(LineLoadOverlays));
            OnPropertyChanged(nameof(PointLoadOverlays));
        }
        finally
        {
            _refreshingLineLoadOverlays = false;
        }
    }

    /// <summary>Adds a Point or Line load of the existing <see cref="LoadType"/> set. Does not invent new FEM types.</summary>
    public void AddLoadOfType(LoadType type)
    {
        RecordUndo("Add load");
        int n = Loads.Count(l => l.Type == type) + 1;
        var name = type switch
        {
            LoadType.Line => $"Line Load {n}",
            LoadType.Triangular => $"Surcharge Load {n}",
            _ => $"Point Load {n}"
        };
        var prefix = type switch
        {
            LoadType.Line => "LL",
            LoadType.Triangular => "SL",
            _ => "NL"
        };
        var load = new LoadModel
        {
            Type = type,
            Name = name,
            IdCode = $"{prefix}-{n:00}",
            Direction = LoadDirection.Axial,
            Magnitude = type == LoadType.Triangular ? 20 : 10,
            Orientation = type == LoadType.Triangular
                ? LoadOrientation.VerticalDown
                : LoadOrientation.NormalToBoundary,
            Distribution = LoadDistribution.Uniform,
            LoadGroup = "LG-01 (Permanent)",
            CoordinateSystem = "Global",
            LoadProjection = "Normal to Surface",
            StageNumber = ActiveStageNumber > 0 ? ActiveStageNumber : (int?)null,
            Notes = type == LoadType.Triangular
                ? "Surface surcharge load due to equipment and construction activities."
                : ""
        };
        Loads.Add(load);
        RefreshFilteredLoads();
        SelectedLoad = load;
    }

    private void OnRemoveLoad(LoadModel? load)
    {
        if (load != null && Loads.Contains(load))
        {
            RecordUndo("Remove load");
            Loads.Remove(load);
            RefreshFilteredLoads();
            if (SelectedLoad == load)
                SelectedLoad = null;
        }
    }

    /// <summary>For point loads: snap click to nearest discretization node in canvas space when 1D mesh exists.</summary>
    private void ApplyNearestNodeToPointLoad(LoadModel load, Point clickCanvas)
    {
        if (load.Type != LoadType.Point || _mesh is not { IsDiscretized: true } || _mesh.DiscretizationNodes.Count == 0)
        {
            load.StartX = clickCanvas.X;
            load.StartY = clickCanvas.Y;
            load.AppliedToNodeId = null;
            load.SyncPointEndFromStart();
            return;
        }
        var geom = GeometryDefinition;
        double bestDistSq = double.MaxValue;
        int bestIndex = -1;
        Point bestCanvas = clickCanvas;
        for (int i = 0; i < _mesh.DiscretizationNodes.Count; i++)
        {
            var axis = _mesh.DiscretizationNodes[i].Location;
            var c = geom.ConvertAxisToCanvas(new Point(axis.X, axis.Y));
            double dx = c.X - clickCanvas.X;
            double dy = c.Y - clickCanvas.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestDistSq || (Math.Abs(d2 - bestDistSq) < 1e-12 && (bestIndex < 0 || i < bestIndex)))
            {
                bestDistSq = d2;
                bestIndex = i;
                bestCanvas = c;
            }
        }
        load.StartX = bestCanvas.X;
        load.StartY = bestCanvas.Y;
        load.AppliedToNodeId = bestIndex >= 0 ? bestIndex + 1 : null;
        load.SyncPointEndFromStart();
    }

    /// <summary>Called when the user clicks on the load conditions canvas. Point load selected: moves load to nearest node. Line load: start/end placement.</summary>
    public void RegisterLoadPoint(Point position)
    {
        if (SelectedLoad?.Type == LoadType.Point)
        {
            RecordUndo("Place point load");
            ApplyNearestNodeToPointLoad(SelectedLoad, position);
            return;
        }

        if (SelectedLoad?.IsDistributedLineLoad == true)
        {
            var load = SelectedLoad;
            if (_linePlacementAwaitingEnd == load)
            {
                load.EndX = position.X;
                load.EndY = position.Y;
                _linePlacementAwaitingEnd = null;
            }
            else if (load.EndX != 0 || load.EndY != 0)
            {
                load.StartX = position.X;
                load.StartY = position.Y;
                load.EndX = 0;
                load.EndY = 0;
                _linePlacementAwaitingEnd = load;
            }
            else if (load.StartX != 0 || load.StartY != 0)
            {
                load.EndX = position.X;
                load.EndY = position.Y;
            }
            else
            {
                load.StartX = position.X;
                load.StartY = position.Y;
                _linePlacementAwaitingEnd = load;
            }
            RefreshLineLoadOverlays();
        }
    }

    /// <summary>Call after replacing <see cref="Loads"/> from a project file so grids and mesh overlay refresh.</summary>
    public void NotifyLoadsReplacedFromProjectFile()
    {
        if (GeometryDefinition != null)
        {
            foreach (var load in Loads)
                LoadPlacementCoords.NormalizeToCanvas(load, GeometryDefinition);
        }
        RefreshFilteredLoads();
        RefreshLoadCanvasMeshDisplay();
        // Field Stress has its own replay path (LoadFromModel), but notify listeners so the
        // Expander/UI refreshes in case persistence loaded a new mode/spec.
        OnPropertyChanged(nameof(FieldStress));
        AppliedLoadsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
