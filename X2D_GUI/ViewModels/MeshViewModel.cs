using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services;
using GeotechSlopeApp.Services.Geometry;
using GeotechSlopeApp.Util;

namespace GeotechSlopeApp.ViewModels;

public class MeshViewModel : INotifyPropertyChanged
{
    private string _oneDMeshLength = "";
    private string _oneDMeshN = "5";
    private string _oneDMeshStatus = "";
    private bool _isDiscretized;
    private bool _showMeshElements;
    private bool _showMeshNodes;
    private bool _isExcavateElementsMode;
    private string _excavateElementsStatusMessage = "";
    private object? _selectedMeshInformationRow;
    private readonly HashSet<int> _userDefinedHighlightedNodeIds = new();
    private IProjectUndoHost? _undoHost;

    public void SetUndoHost(IProjectUndoHost? host) => _undoHost = host;
    private void RecordUndo(string description) => _undoHost?.RecordUndoPoint(description);

    private readonly List<Point> _highlightedMidsideAxisPoints = new();
    private object? _selectedBoundaryEdge;
    private ProjectSettingsViewModel? _projectSettings;
    private MaterialPropertiesViewModel? _materialPropertiesVM;
    private ScriptViewModel? _scriptVm;
    private FEAnalysisViewModel? _feAnalysisViewModel;
    private Q8MeshMaterialLayerPlanner.LayerPlan? _activeLayerPlan;
    private NonHorizontalLayerMeshPlan? _activeNonHorizontalPlan;
    private string _selectedTwoDElementFamily = "Triangles";
    private string _selectedTwoDNodeOption = "3-node";
    private string _selectedMeshNumberingDirection = "dir = x";
    /// <summary>Element order for Surface Mesh and Tunnel Mesh tabs (always visible, independent of project dimension).</summary>
    private string _meshElementOrder = "3-node";
    /// <summary>Mesh popup tab indices: Standard=0, Unstructured Mesh=1, Create triangle=2, Rect=3, 1D=4, Structured=5.</summary>
    private int _meshPopupSelectedTabIndex = 5; // default to Structured Mesh; Standard/Create Triangle/Rectangular are deactivated.

    private string _triangleMeshStatus = "";
    private string _triangleMeshStructuredExtentHint = "";

    private string _rectangularMeshL = "1";
    private string _rectangularMeshH = "0.2";
    private string _rectangularMeshNx = "10";
    private string _rectangularMeshNy = "2";
    private string _rectangularMeshStatus = "";

    // ── Tunnel Meshing (CDT + Ring Refinement) backing fields ─────────────────
    private Models.TunnelMeshGenerationMethod _tunnelMeshGenerationMethod = Models.TunnelMeshGenerationMethod.CDT;
    private Models.MeshSmoothingMethod _meshSmoothingMethod = Models.MeshSmoothingMethod.Laplacian;
    private string _smoothingIterations    = "3";
    private bool   _autoRegenerateMesh     = false;
    private bool   _refineNearExcavation   = true;
    private bool   _refineNearLoads        = false;
    private bool   _refineNearMaterialInterfaces = false;
    private bool   _refineNearStiffnessContrast  = false;
    private string _tunnelDiameter              = "";
    private string _farFieldEdgeLength          = "5.0";
    private string _nearTunnelEdgeLength        = "0.2";
    private string _ringCount                   = "4";
    private string _ringGrowthFactor            = "1.2";
    private string _firstRingThickness          = "0.3";
    private string _pointLoadRefinementRadius   = "";
    private string _minAngleDegrees             = "20";
    private string _maxRefinementIterations     = "100";
    private string _mergeTolerance              = "1e-6";
    private string _tunnelMeshStatus            = "";
    private int    _tunnelMeshNodeCount, _tunnelMeshElementCount;
    private int    _tunnelInteriorElementCount, _outerBoundaryNodeCount;
    private int    _outerBoundaryEdgeCount, _totalElementsTouchingOuterEdges;
    private string _importBoundaryStatusMessage = "";
    private readonly RelayCommand _importBoundaryToGeometryCommand;

    /// <summary>
    /// Set by MainWindowViewModel so 1D mesh generation can read geometry and create mesh in this tab.
    /// </summary>
    public GeometryDefinitionViewModel? GeometryDefinitionVM { get; set; }

    /// <summary>Set by MainWindowViewModel to switch to Geometry Definition after a successful mesh boundary import.</summary>
    public Action? RequestShowGeometryTab { get; set; }

    /// <summary>Status from the last Mesh popup "Import boundary to Geometry Definition" action.</summary>
    public string ImportBoundaryStatusMessage
    {
        get => _importBoundaryStatusMessage;
        private set { _importBoundaryStatusMessage = value ?? ""; OnPropertyChanged(); }
    }

    public ICommand ImportBoundaryToGeometryCommand => _importBoundaryToGeometryCommand;

    /// <summary>Material library used for mesh canvas element fill colors (by etype).</summary>
    public MaterialPropertiesViewModel? MaterialPropertiesVM => _materialPropertiesVM;

    /// <summary>Set by MainWindowViewModel so element/node options follow Material Properties analysis type (e.g. axisymmetric 8-node, thermal strain).</summary>
    public void SetMaterialProperties(MaterialPropertiesViewModel? materialPropertiesVM)
    {
        if (_materialPropertiesVM != null)
            _materialPropertiesVM.PropertyChanged -= OnMaterialPropertiesPropertyChanged;
        _materialPropertiesVM = materialPropertiesVM;
        if (_materialPropertiesVM != null)
            _materialPropertiesVM.PropertyChanged += OnMaterialPropertiesPropertyChanged;
        UpdateElementAndNodeOptions();
    }

    private void OnMaterialPropertiesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaterialPropertiesViewModel.SelectedAnalysisCategory) ||
            e.PropertyName == nameof(MaterialPropertiesViewModel.SelectedDetailedAnalysisType))
        {
            UpdateElementAndNodeOptions();
        }
    }

    /// <summary>Current Structural – static elasticity Type when category is Structural; null otherwise.</summary>
    private string? GetStructuralAnalysisType()
    {
        if (_materialPropertiesVM?.SelectedAnalysisCategory != "Structural – static elasticity")
            return null;
        return _materialPropertiesVM.SelectedDetailedAnalysisType;
    }

    /// <summary>Mesh popup TabControl selected index (synced when project dimension changes).</summary>
    public int MeshPopupSelectedTabIndex
    {
        get => _meshPopupSelectedTabIndex;
        set
        {
            var v = value;
            // The Surface/Tunnel/Studio/Guide subtabs were removed; any persisted/legacy index beyond
            // Structured Mesh routes back to a valid tab (Structured for 2D, 1D Mesh for 1D problems).
            if (v > MeshPopupUserDefinedTabIndex)
                v = Is1DProblem ? MeshPopupOneDTabIndex : MeshPopupUserDefinedTabIndex;
            // Deactivated tabs (Standard / Create Triangle / Rectangular) route to Structured Mesh so 2D users
            // see a useful workspace; 1D users route to the 1D tab via SyncMeshPopupTabToDimension.
            if (v is MeshPopupStandardTabIndex or MeshPopupCreateTriangleTabIndex or MeshPopupRectangularMeshTabIndex)
                v = Is1DProblem ? MeshPopupOneDTabIndex : MeshPopupUserDefinedTabIndex;
            if (v == MeshPopupCreateTriangleTabIndex && !IsCreateTriangleMeshSubtabVisible)
                v = Is1DProblem ? MeshPopupOneDTabIndex : MeshPopupUserDefinedTabIndex;
            if (Is1DProblem && v != MeshPopupOneDTabIndex && v != MeshPopupUserDefinedTabIndex)
                v = MeshPopupOneDTabIndex;
            else if (!Is1DProblem && v == MeshPopupOneDTabIndex)
                v = MeshPopupUserDefinedTabIndex;
            if (_meshPopupSelectedTabIndex == v) return;
            _meshPopupSelectedTabIndex = v;
            OnPropertyChanged();
            if (v == MeshPopupCreateTriangleTabIndex && IsCreateTriangleMeshSubtabVisible)
                RefreshTriangleMeshStructuredExtentsFromGeometry();
            if (v == MeshPopupUserDefinedTabIndex)
                UserDefinedMeshVM.RefreshRectangularExtentsFromGeometry();
        }
    }

    public const int MeshPopupStandardTabIndex = 0;
    public const int MeshPopupRs2dTabIndex = 1;
    public const int MeshPopupCreateTriangleTabIndex = 2;
    public const int MeshPopupRectangularMeshTabIndex = 3;
    /// <summary>1D mesh tab index in Mesh popup.</summary>
    public const int MeshPopupOneDTabIndex = 4;
    /// <summary>Structured Mesh (Q4/Q8 parametric + manual node/element tables).</summary>
    public const int MeshPopupUserDefinedTabIndex = 5;

    /// <summary>Maps legacy mesh popup tab indices from older .xfem files to the current tab order.</summary>
    public static int MigrateMeshPopupTabIndex(int legacyIndex) => legacyIndex switch
    {
        0 => MeshPopupStandardTabIndex,
        1 or 2 => MeshPopupRs2dTabIndex, // Surface (removed) or RS2D
        3 => MeshPopupCreateTriangleTabIndex,
        4 or 7 or 8 or 9 => MeshPopupUserDefinedTabIndex, // Tunnel/Studio/Geotech/Structured (removed or moved)
        5 => MeshPopupRectangularMeshTabIndex,
        6 => MeshPopupOneDTabIndex,
        _ => MeshPopupUserDefinedTabIndex
    };

    private UserDefinedMeshViewModel? _userDefinedMeshVM;
    /// <summary>Lazy-built Structured Mesh subtab (Q4/Q8 parametric + manual node/element tables).</summary>
    public UserDefinedMeshViewModel UserDefinedMeshVM => _userDefinedMeshVM ??= new UserDefinedMeshViewModel(this);

    public void SetScriptAndBoundary(
        ScriptViewModel? script,
        BoundaryConditionsViewModel? boundaryConditions,
        MaterialPropertiesViewModel? materialProperties = null,
        FEAnalysisViewModel? feAnalysis = null)
    {
        _scriptVm = script;
        _feAnalysisViewModel = feAnalysis;
        UserDefinedMeshVM.SetGenerateContext(script, boundaryConditions, materialProperties, feAnalysis);
    }

    /// <summary>Visible when 2D Problem and Element Type → Element = Triangles.</summary>
    public bool IsCreateTriangleMeshSubtabVisible =>
        Is2DProblem && string.Equals(_selectedTwoDElementFamily, "Triangles", StringComparison.Ordinal);

    /// <summary>Set by MainWindowViewModel so project dimension drives mesh tabs and element/node option lists.</summary>
    public void SetProjectSettings(ProjectSettingsViewModel? projectSettings)
    {
        if (_projectSettings != null)
            _projectSettings.PropertyChanged -= OnProjectSettingsPropertyChanged;
        _projectSettings = projectSettings;
        if (_projectSettings != null)
            _projectSettings.PropertyChanged += OnProjectSettingsPropertyChanged;
        OnPropertyChanged(nameof(Is2DProblem));
        OnPropertyChanged(nameof(Is1DProblem));
        OnPropertyChanged(nameof(Is3DProblem));
        OnPropertyChanged(nameof(IsCreateTriangleMeshSubtabVisible));
        UpdateElementAndNodeOptions();
        SyncMeshPopupTabToDimension();
    }

    private void OnProjectSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettingsViewModel.SelectedDimension))
        {
            OnPropertyChanged(nameof(Is2DProblem));
            OnPropertyChanged(nameof(Is1DProblem));
            OnPropertyChanged(nameof(Is3DProblem));
            OnPropertyChanged(nameof(IsCreateTriangleMeshSubtabVisible));
            UpdateElementAndNodeOptions();
            SyncMeshPopupTabToDimension();
        }
    }

    /// <summary>When 1D Problem, select 1D Mesh tab; when 2D/3D, move off hidden 1D tab to Structured Mesh.</summary>
    private void SyncMeshPopupTabToDimension()
    {
        if (Is1DProblem)
            MeshPopupSelectedTabIndex = MeshPopupOneDTabIndex;
        else if (_meshPopupSelectedTabIndex == MeshPopupOneDTabIndex)
            MeshPopupSelectedTabIndex = MeshPopupUserDefinedTabIndex;
    }

    /// <summary>True when Project Settings → Analysis Type → 2D Problem is selected.</summary>
    public bool Is2DProblem => _projectSettings?.SelectedDimension == ProblemDimension.TwoD;

    /// <summary>True when Project Settings → Analysis Type → 1D Problem is selected.</summary>
    public bool Is1DProblem => _projectSettings?.SelectedDimension == ProblemDimension.OneD;

    /// <summary>True when Project Settings → Analysis Type → 3D Problem is selected.</summary>
    public bool Is3DProblem => _projectSettings?.SelectedDimension == ProblemDimension.ThreeD;

    /// <summary>User-selected numbering direction label; maps to <see cref="MeshNumberingDirectionChar"/> for .dat output.</summary>
    public string SelectedMeshNumberingDirection
    {
        get => _selectedMeshNumberingDirection;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "dir = x" : value.Trim();
            if (v != "dir = x" && v != "dir = y" && v != "dir = r")
                v = "dir = x";
            if (_selectedMeshNumberingDirection == v) return;
            _selectedMeshNumberingDirection = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MeshNumberingDirectionChar));
        }
    }

    /// <summary>Single character for Program 5.1 line 2 (<c>'x'</c>, <c>'y'</c>, or <c>'r'</c>).</summary>
    public char MeshNumberingDirectionChar => _selectedMeshNumberingDirection switch
    {
        "dir = y" => 'y',
        "dir = r" => 'r',
        _ => 'x'
    };

    /// <summary>Options for 2D element family (Triangles and/or Quadrilaterals). Filtered by Analysis Type (e.g. Thermal strain = Triangles only, Axisymmetric 8-node = Quadrilaterals only).</summary>
    public ObservableCollection<string> TwoDElementFamilyOptions { get; } = new();

    /// <summary>Selected 2D element family. Drives Node options when Type allows both elements.</summary>
    public string SelectedTwoDElementFamily
    {
        get => _selectedTwoDElementFamily;
        set
        {
            if (_selectedTwoDElementFamily == value) return;
            _selectedTwoDElementFamily = value ?? "Triangles";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCreateTriangleMeshSubtabVisible));
            if (!IsCreateTriangleMeshSubtabVisible && _meshPopupSelectedTabIndex == MeshPopupCreateTriangleTabIndex)
            {
                _meshPopupSelectedTabIndex = Is1DProblem ? MeshPopupOneDTabIndex : MeshPopupUserDefinedTabIndex;
                OnPropertyChanged(nameof(MeshPopupSelectedTabIndex));
            }
            UpdateTwoDNodeOptions();
        }
    }

    /// <summary>Node count options for current 2D element family and Analysis Type.</summary>
    public ObservableCollection<string> TwoDNodeOptions { get; } = new();

    /// <summary>Selected node option (e.g. "3-node", "8-node").</summary>
    public string SelectedTwoDNodeOption
    {
        get => _selectedTwoDNodeOption;
        set { _selectedTwoDNodeOption = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>
    /// Element order for Surface Mesh and Tunnel Mesh tabs.
    /// Always visible regardless of project dimension — toggles between "3-node" and "6-node".
    /// </summary>
    public string MeshElementOrder
    {
        get => _meshElementOrder;
        set { _meshElementOrder = value ?? "3-node"; OnPropertyChanged(); }
    }

    /// <summary>Call after restoring state from a .xfem file so mesh Element Type UI matches analysis dimension.</summary>
    public void RefreshAfterProjectFileLoad() => UpdateElementAndNodeOptions();

    /// <summary>
    /// After .xfem mesh tables are restored, rebuild Goodman interfaces. A saved
    /// split mesh already has coincident node pairs — recover those without
    /// splitting again.
    /// </summary>
    public void RestoreJointInterfacesAfterProjectLoad() => ApplyJointInterfacesAfterPopulate();

    /// <summary>Refreshes Element and Node options from Material Properties Analysis category/type (Structural, Flow).</summary>
    private void UpdateElementAndNodeOptions()
    {
        try
        {
        var category = _materialPropertiesVM?.SelectedAnalysisCategory;
        var type = _materialPropertiesVM?.SelectedDetailedAnalysisType;

        // 1D: Element and Node options
        if (Is1DProblem)
        {
            TwoDElementFamilyOptions.Clear();
            TwoDNodeOptions.Clear();
            if (category == "Consolidation / diffusion / transient" &&
                     (type == "1D consolidation" || type == "1D consolidation (settlement)" || type == "1D consolidation (explicit)"))
            {
                TwoDElementFamilyOptions.Add("2-node line");
                _selectedTwoDElementFamily = "2-node line";
                TwoDNodeOptions.Add("2-node");
                _selectedTwoDNodeOption = "2-node";
            }
            else if (category == "Structural – static elasticity")
            {
                TwoDElementFamilyOptions.Add("2-node rod");
                TwoDElementFamilyOptions.Add("2-node beam");
                _selectedTwoDElementFamily = TwoDElementFamilyOptions.Contains(_selectedTwoDElementFamily) ? _selectedTwoDElementFamily : "2-node rod";
                TwoDNodeOptions.Add("2-node");
                _selectedTwoDNodeOption = "2-node";
            }
            else
            {
                TwoDElementFamilyOptions.Add("2-node line");
                TwoDElementFamilyOptions.Add("2-node rod");
                TwoDElementFamilyOptions.Add("2-node beam");
                _selectedTwoDElementFamily = TwoDElementFamilyOptions.Contains(_selectedTwoDElementFamily) ? _selectedTwoDElementFamily : "2-node line";
                TwoDNodeOptions.Add("2-node");
                _selectedTwoDNodeOption = "2-node";
            }
            OnPropertyChanged(nameof(SelectedTwoDElementFamily));
            OnPropertyChanged(nameof(TwoDElementFamilyOptions));
            OnPropertyChanged(nameof(TwoDNodeOptions));
            OnPropertyChanged(nameof(SelectedTwoDNodeOption));
            return;
        }

        // 3D: Element and Node options (hex)
        if (Is3DProblem)
        {
            TwoDElementFamilyOptions.Clear();
            TwoDElementFamilyOptions.Add("8-node hex");
            TwoDElementFamilyOptions.Add("14-node hex");
            TwoDElementFamilyOptions.Add("20-node hex");
            _selectedTwoDElementFamily = TwoDElementFamilyOptions.Contains(_selectedTwoDElementFamily) ? _selectedTwoDElementFamily : "8-node hex";
            TwoDNodeOptions.Clear();
            TwoDNodeOptions.Add("8-node");
            TwoDNodeOptions.Add("14-node");
            TwoDNodeOptions.Add("20-node");
            _selectedTwoDNodeOption = TwoDNodeOptions.Contains(_selectedTwoDNodeOption) ? _selectedTwoDNodeOption : "8-node";
            OnPropertyChanged(nameof(SelectedTwoDElementFamily));
            OnPropertyChanged(nameof(TwoDElementFamilyOptions));
            OnPropertyChanged(nameof(TwoDNodeOptions));
            OnPropertyChanged(nameof(SelectedTwoDNodeOption));
            return;
        }

        if (!Is2DProblem)
            return;

        TwoDElementFamilyOptions.Clear();

        // 2D + Groundwater
        if (MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(category))
        {
            if (type == MaterialPropertiesViewModel.GroundwaterSeepagePlaneAxisymType
                || type == MaterialPropertiesViewModel.GroundwaterSeepageFreeSurfaceType)
            {
                TwoDElementFamilyOptions.Add("Quadrilaterals");
                _selectedTwoDElementFamily = "Quadrilaterals";
                TwoDNodeOptions.Clear();
                TwoDNodeOptions.Add("4-node");
                _selectedTwoDNodeOption = "4-node";
            }
            else if (type == MaterialPropertiesViewModel.GroundwaterSeepageGeneralType)
            {
                TwoDElementFamilyOptions.Add("Quadrilaterals");
                _selectedTwoDElementFamily = "Quadrilaterals";
                TwoDNodeOptions.Clear();
                TwoDNodeOptions.Add("4-node");
                TwoDNodeOptions.Add("8-node");
                _selectedTwoDNodeOption = TwoDNodeOptions.Contains(_selectedTwoDNodeOption) ? _selectedTwoDNodeOption : "4-node";
            }
            else
            {
                ApplyDefault2DElementAndNodeOptions();
                OnPropertyChanged(nameof(TwoDElementFamilyOptions));
                OnPropertyChanged(nameof(TwoDNodeOptions));
                OnPropertyChanged(nameof(SelectedTwoDNodeOption));
                return;
            }
            OnPropertyChanged(nameof(SelectedTwoDElementFamily));
            OnPropertyChanged(nameof(TwoDElementFamilyOptions));
            OnPropertyChanged(nameof(TwoDNodeOptions));
            OnPropertyChanged(nameof(SelectedTwoDNodeOption));
            return;
        }

        // 2D + Structural – static elasticity
        var structuralType = GetStructuralAnalysisType();
        if (structuralType == "Axisymmetric 8-node")
        {
            TwoDElementFamilyOptions.Add("Quadrilaterals");
            _selectedTwoDElementFamily = "Quadrilaterals";
            OnPropertyChanged(nameof(SelectedTwoDElementFamily));
            TwoDNodeOptions.Clear();
            TwoDNodeOptions.Add("8-node");
            _selectedTwoDNodeOption = "8-node";
        }
        else if (structuralType == "Thermal strain")
        {
            TwoDElementFamilyOptions.Add("Triangles");
            _selectedTwoDElementFamily = "Triangles";
            OnPropertyChanged(nameof(SelectedTwoDElementFamily));
            TwoDNodeOptions.Clear();
            TwoDNodeOptions.Add("3-node");
            TwoDNodeOptions.Add("6-node");
            TwoDNodeOptions.Add("10-node");
            TwoDNodeOptions.Add("15-node");
            if (!TwoDNodeOptions.Contains(_selectedTwoDNodeOption))
                _selectedTwoDNodeOption = "3-node";
        }
        else
        {
            ApplyDefault2DElementAndNodeOptions();
        }
        OnPropertyChanged(nameof(TwoDElementFamilyOptions));
        OnPropertyChanged(nameof(TwoDNodeOptions));
        OnPropertyChanged(nameof(SelectedTwoDNodeOption));
        }
        finally
        {
            OnPropertyChanged(nameof(IsCreateTriangleMeshSubtabVisible));
            if (!IsCreateTriangleMeshSubtabVisible && _meshPopupSelectedTabIndex == MeshPopupCreateTriangleTabIndex)
            {
                _meshPopupSelectedTabIndex = Is1DProblem ? MeshPopupOneDTabIndex : MeshPopupUserDefinedTabIndex;
                OnPropertyChanged(nameof(MeshPopupSelectedTabIndex));
            }
        }
    }

    private void ApplyDefault2DElementAndNodeOptions()
    {
        TwoDElementFamilyOptions.Add("Triangles");
        TwoDElementFamilyOptions.Add("Quadrilaterals");
        if (!TwoDElementFamilyOptions.Contains(_selectedTwoDElementFamily))
            _selectedTwoDElementFamily = "Triangles";
        OnPropertyChanged(nameof(SelectedTwoDElementFamily));
        UpdateTwoDNodeOptions();
    }

    private void UpdateTwoDNodeOptions()
    {
        if (!Is2DProblem)
            return;
        var type = GetStructuralAnalysisType();
        if (type == "Axisymmetric 8-node")
        {
            TwoDNodeOptions.Clear();
            TwoDNodeOptions.Add("8-node");
            _selectedTwoDNodeOption = "8-node";
        }
        else if (type == "Thermal strain")
        {
            TwoDNodeOptions.Clear();
            TwoDNodeOptions.Add("3-node");
            TwoDNodeOptions.Add("6-node");
            TwoDNodeOptions.Add("10-node");
            TwoDNodeOptions.Add("15-node");
            if (!TwoDNodeOptions.Contains(_selectedTwoDNodeOption))
                _selectedTwoDNodeOption = "3-node";
        }
        else
        {
            TwoDNodeOptions.Clear();
            if (_selectedTwoDElementFamily == "Quadrilaterals")
            {
                TwoDNodeOptions.Add("4-node");
                TwoDNodeOptions.Add("8-node");
                _selectedTwoDNodeOption = TwoDNodeOptions.Contains(_selectedTwoDNodeOption) ? _selectedTwoDNodeOption : "4-node";
            }
            else
            {
                TwoDNodeOptions.Add("3-node");
                TwoDNodeOptions.Add("6-node");
                TwoDNodeOptions.Add("10-node");
                TwoDNodeOptions.Add("15-node");
                _selectedTwoDNodeOption = TwoDNodeOptions.Contains(_selectedTwoDNodeOption) ? _selectedTwoDNodeOption : "3-node";
            }
        }
        OnPropertyChanged(nameof(TwoDNodeOptions));
        OnPropertyChanged(nameof(SelectedTwoDNodeOption));
    }

    public string OneDMeshLength
    {
        get => _oneDMeshLength;
        set { _oneDMeshLength = value ?? ""; OnPropertyChanged(); }
    }

    public string OneDMeshN
    {
        get => _oneDMeshN;
        set { _oneDMeshN = value ?? ""; OnPropertyChanged(); }
    }

    public string OneDMeshStatus
    {
        get => _oneDMeshStatus;
        set { _oneDMeshStatus = value ?? ""; OnPropertyChanged(); }
    }

    public ICommand Generate1DMeshCommand { get; }
    public ICommand GenerateTriangleMeshStructuredCommand { get; }
    public ICommand GenerateTriangleMeshUnstructuredCommand { get; }
    public ICommand GenerateRectangularMeshCommand { get; }
    public ICommand DiscretizeCommand { get; }
    public ICommand GenerateMeshCommand { get; }
    public ICommand ClearMeshCommand { get; }
    public ICommand ToggleExcavateElementsModeCommand { get; }
    public ICommand Rs2dDiscretizeCommand { get; }
    public ICommand Rs2dMeshCommand { get; }
    public ICommand Rs2dOkCommand { get; }
    public ICommand Rs2dCancelCommand { get; }

    public string RectangularMeshL
    {
        get => _rectangularMeshL;
        set { _rectangularMeshL = value ?? ""; OnPropertyChanged(); }
    }

    public string RectangularMeshH
    {
        get => _rectangularMeshH;
        set { _rectangularMeshH = value ?? ""; OnPropertyChanged(); }
    }

    public string RectangularMeshNx
    {
        get => _rectangularMeshNx;
        set { _rectangularMeshNx = value ?? ""; OnPropertyChanged(); }
    }

    public string RectangularMeshNy
    {
        get => _rectangularMeshNy;
        set { _rectangularMeshNy = value ?? ""; OnPropertyChanged(); }
    }

    public string RectangularMeshStatus
    {
        get => _rectangularMeshStatus;
        set { _rectangularMeshStatus = value ?? ""; OnPropertyChanged(); }
    }

    public string TriangleMeshStatus
    {
        get => _triangleMeshStatus;
        set { _triangleMeshStatus = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Read-only hint for Create triangle mesh → structured: L/H taken from geometry extent (not the rectangular mesh textboxes).</summary>
    public string TriangleMeshStructuredExtentHint
    {
        get => _triangleMeshStructuredExtentHint;
        private set { _triangleMeshStructuredExtentHint = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Updates the structured-mesh extent hint (surface polygon bounds when available, not the full model bbox).</summary>
    public void RefreshTriangleMeshStructuredExtentsFromGeometry()
    {
        if (GeometryDefinitionVM == null)
        {
            TriangleMeshStructuredExtentHint = "Geometry not linked.";
            return;
        }
        if (GeometryDefinitionVM.TryGetTriangleMeshPolygonAxisPoints(out var poly) && poly.Count >= 3)
        {
            double w = poly.Max(p => p.X) - poly.Min(p => p.X);
            double h = poly.Max(p => p.Y) - poly.Min(p => p.Y);
            if (w > 1e-12 && h > 1e-12)
            {
                TriangleMeshStructuredExtentHint =
                    $"Structured mesh will use the surface region only: L = {w.ToString("G6", CultureInfo.InvariantCulture)}, H = {h.ToString("G6", CultureInfo.InvariantCulture)} (axis units, same as S1 polygon).";
                return;
            }
        }
        if (GeometryDefinitionVM.GeometrySurfaces.Count > 0)
        {
            TriangleMeshStructuredExtentHint =
                "Lock or complete the surface polygon (e.g. Add / lock S1 corners) so structured mesh matches the filled region — not the whole model box.";
            return;
        }
        var (wg, hg) = GeometryDefinitionVM.GetBoundingBoxExtent();
        if (wg > 1e-12 && hg > 1e-12)
        {
            TriangleMeshStructuredExtentHint =
                $"No surface polygon yet — using all vertices: L = {wg.ToString("G6", CultureInfo.InvariantCulture)}, H = {hg.ToString("G6", CultureInfo.InvariantCulture)}.";
        }
        else
            TriangleMeshStructuredExtentHint = "Add vertices or define a surface so a positive width and height can be computed.";
    }

    /// <summary>Nodes created by discretization / 1D mesh (for Mesh tab statistics and display).</summary>
    public BulkObservableCollection<MeshDiscretizationNodeDisplay> DiscretizationNodes { get; } = new();

    /// <summary>Elements created by mesh generation (for Mesh tab statistics and display).</summary>
    public BulkObservableCollection<MeshElementDisplay> MeshElements { get; } = new();

    /// <summary>Rows for Mesh Information DataGrid.</summary>
    public BulkObservableCollection<MeshInformationRow> MeshInformationRows { get; } = new();

    /// <summary>Boundary edges (empty for 1D mesh; used by Standard Meshing).</summary>
    public ObservableCollection<object> BoundaryEdgeNodes { get; } = new();

    public bool IsDiscretized
    {
        get => _isDiscretized;
        set
        {
            if (_isDiscretized == value) return;
            _isDiscretized = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveElementCount));
            OnPropertyChanged(nameof(DeactivatedElementCount));
            _importBoundaryToGeometryCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>When true and mesh exists, element numbers are shown on the Mesh tab canvas.</summary>
    public bool ShowMeshElements
    {
        get => _showMeshElements;
        set { _showMeshElements = value; OnPropertyChanged(); }
    }

    /// <summary>When true and mesh exists, discretization nodes are shown on the Mesh tab canvas.</summary>
    public bool ShowMeshNodes
    {
        get => _showMeshNodes;
        set { _showMeshNodes = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Mesh-canvas excavation mode: clicking an active element deactivates it and forwards
    /// its element ID to the active staged-construction stage's ExcavatedElementIds.
    /// </summary>
    public bool IsExcavateElementsMode
    {
        get => _isExcavateElementsMode;
        set
        {
            var next = value && IsDiscretized;
            if (_isExcavateElementsMode == next) return;
            _isExcavateElementsMode = next;
            if (_isExcavateElementsMode)
            {
                // Avoid interaction clash with surface excavation mode in Geometry canvas flow.
                if (GeometryDefinitionVM != null)
                    GeometryDefinitionVM.IsExcavationMode = false;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExcavateElementsButtonLabel));
        }
    }

    public string ExcavateElementsButtonLabel =>
        IsExcavateElementsMode ? "Exit Excavate Elements" : "Excavate Elements";

    /// <summary>Temporary click-path diagnostics for Mesh popup excavation mode.</summary>
    public string ExcavateElementsStatusMessage
    {
        get => _excavateElementsStatusMessage;
        private set
        {
            if (_excavateElementsStatusMessage == value) return;
            _excavateElementsStatusMessage = value;
            OnPropertyChanged();
        }
    }

    public object? SelectedMeshInformationRow
    {
        get => _selectedMeshInformationRow;
        set
        {
            if (_selectedMeshInformationRow == value) return;
            _selectedMeshInformationRow = value;
            ApplyMeshElementHighlightFromInfoRow(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Clears element highlights, then highlights the mesh element matching <see cref="MeshInformationRow.ElementId"/>.</summary>
    private void ApplyMeshElementHighlightFromInfoRow(object? row)
    {
        ClearUserDefinedTableHighlight();
        foreach (var el in MeshElements)
            el.IsHighlighted = false;
        if (row is MeshInformationRow r)
        {
            ShowMeshElements = false;
            ShowMeshNodes = false;
            var match = MeshElements.FirstOrDefault(e => e.DisplayId == r.ElementId);
            if (match != null)
                match.IsHighlighted = true;

            foreach (var (x, y, nodeId) in r.EnumerateMidsideNodes())
            {
                _highlightedMidsideAxisPoints.Add(new Point(x, y));
                if (nodeId > 0)
                    _userDefinedHighlightedNodeIds.Add(nodeId);
            }

            if (_userDefinedHighlightedNodeIds.Count > 0)
            {
                EnsureDiscretizationNodeIds();
                foreach (var n in DiscretizationNodes)
                {
                    if (n.NodeId > 0 && _userDefinedHighlightedNodeIds.Contains(n.NodeId))
                        n.IsHighlighted = true;
                }
            }

            if (_highlightedMidsideAxisPoints.Count > 0 || _userDefinedHighlightedNodeIds.Count > 0)
            {
                OnPropertyChanged(nameof(UserDefinedHighlightedNodeIds));
                OnPropertyChanged(nameof(HighlightedMidsideAxisPoints));
            }
        }
        NotifyMeshCanvasDataChanged();
    }

    /// <summary>Node ids highlighted from the User-Defined Mesh node/element tables.</summary>
    public IReadOnlyCollection<int> UserDefinedHighlightedNodeIds => _userDefinedHighlightedNodeIds;

    /// <summary>Axis-space midside positions for T6/Q8 mesh-info row selection (canvas overlay).</summary>
    public IReadOnlyList<Point> HighlightedMidsideAxisPoints => _highlightedMidsideAxisPoints;

    /// <summary>
    /// Highlights a node and/or element on the mesh canvas when a row is selected in the
    /// User-Defined Mesh subtab tables.
    /// </summary>
    public void ApplyUserDefinedTableHighlight(int? nodeId, int? elementId, IEnumerable<int>? elementNodeIds)
    {
        if (_selectedMeshInformationRow != null)
        {
            _selectedMeshInformationRow = null;
            OnPropertyChanged(nameof(SelectedMeshInformationRow));
        }

        foreach (var el in MeshElements)
            el.IsHighlighted = false;

        _userDefinedHighlightedNodeIds.Clear();
        _highlightedMidsideAxisPoints.Clear();
        if (nodeId is > 0)
            _userDefinedHighlightedNodeIds.Add(nodeId.Value);
        else if (elementNodeIds != null)
        {
            foreach (int id in elementNodeIds)
            {
                if (id > 0)
                    _userDefinedHighlightedNodeIds.Add(id);
            }
        }

        if (elementId is > 0)
        {
            var match = MeshElements.FirstOrDefault(e => e.DisplayId == elementId.Value);
            if (match != null)
                match.IsHighlighted = true;
        }

        int fallbackId = 0;
        foreach (var n in DiscretizationNodes)
        {
            int id = n.NodeId > 0 ? n.NodeId : ++fallbackId;
            n.IsHighlighted = _userDefinedHighlightedNodeIds.Contains(id);
        }

        OnPropertyChanged(nameof(UserDefinedHighlightedNodeIds));
        OnPropertyChanged(nameof(HighlightedMidsideAxisPoints));
        NotifyMeshCanvasDataChanged();
    }

    private void ClearUserDefinedTableHighlight()
    {
        if (_userDefinedHighlightedNodeIds.Count == 0
            && _highlightedMidsideAxisPoints.Count == 0
            && !DiscretizationNodes.Any(n => n.IsHighlighted))
            return;

        _userDefinedHighlightedNodeIds.Clear();
        _highlightedMidsideAxisPoints.Clear();
        foreach (var n in DiscretizationNodes)
            n.IsHighlighted = false;
        OnPropertyChanged(nameof(UserDefinedHighlightedNodeIds));
        OnPropertyChanged(nameof(HighlightedMidsideAxisPoints));
    }

    public object? SelectedBoundaryEdge
    {
        get => _selectedBoundaryEdge;
        set { _selectedBoundaryEdge = value; OnPropertyChanged(); }
    }

    public int ActiveElementCount       => MeshElements.Count(e => e.IsActive);
    public int DeactivatedElementCount  => MeshElements.Count(e => !e.IsActive);
    public int TunnelBoundaryEdgeCount  => 0;
    public double TotalBoundaryLength   => 0;

    // ── Tunnel Meshing properties ─────────────────────────────────────────────

    public Models.TunnelMeshGenerationMethod TunnelMeshGenerationMethod
    {
        get => _tunnelMeshGenerationMethod;
        set { _tunnelMeshGenerationMethod = value; OnPropertyChanged(); }
    }
    public Models.MeshSmoothingMethod MeshSmoothingMethod
    {
        get => _meshSmoothingMethod;
        set { _meshSmoothingMethod = value; OnPropertyChanged(); }
    }
    public string SmoothingIterations  { get => _smoothingIterations;  set { _smoothingIterations  = value; OnPropertyChanged(); } }
    public bool AutoRegenerateMesh     { get => _autoRegenerateMesh;   set { _autoRegenerateMesh   = value; OnPropertyChanged(); } }
    public bool RefineNearExcavation   { get => _refineNearExcavation; set { _refineNearExcavation = value; OnPropertyChanged(); } }
    public bool RefineNearLoads        { get => _refineNearLoads;      set { _refineNearLoads      = value; OnPropertyChanged(); } }
    public bool RefineNearMaterialInterfaces { get => _refineNearMaterialInterfaces; set { _refineNearMaterialInterfaces = value; OnPropertyChanged(); } }
    public bool RefineNearStiffnessContrast  { get => _refineNearStiffnessContrast;  set { _refineNearStiffnessContrast  = value; OnPropertyChanged(); } }
    public string TunnelDiameter             { get => _tunnelDiameter;            set { _tunnelDiameter            = value; OnPropertyChanged(); } }
    public string FarFieldEdgeLength         { get => _farFieldEdgeLength;         set { _farFieldEdgeLength         = value; OnPropertyChanged(); } }
    public string NearTunnelEdgeLength       { get => _nearTunnelEdgeLength;       set { _nearTunnelEdgeLength       = value; OnPropertyChanged(); } }
    public string RingCount                  { get => _ringCount;                  set { _ringCount                  = value; OnPropertyChanged(); } }
    public string RingGrowthFactor           { get => _ringGrowthFactor;           set { _ringGrowthFactor           = value; OnPropertyChanged(); } }
    public string FirstRingThickness         { get => _firstRingThickness;         set { _firstRingThickness         = value; OnPropertyChanged(); } }
    public string PointLoadRefinementRadius  { get => _pointLoadRefinementRadius;  set { _pointLoadRefinementRadius  = value; OnPropertyChanged(); } }
    public string MinAngleDegrees            { get => _minAngleDegrees;            set { _minAngleDegrees            = value; OnPropertyChanged(); } }
    public string MaxRefinementIterations    { get => _maxRefinementIterations;    set { _maxRefinementIterations    = value; OnPropertyChanged(); } }
    public string MergeTolerance             { get => _mergeTolerance;             set { _mergeTolerance             = value; OnPropertyChanged(); } }

    public string TunnelMeshStatus     { get => _tunnelMeshStatus;    set { _tunnelMeshStatus    = value; OnPropertyChanged(); } }
    public int TunnelMeshNodeCount     { get => _tunnelMeshNodeCount; set { _tunnelMeshNodeCount = value; OnPropertyChanged(); } }
    public int TunnelMeshElementCount  { get => _tunnelMeshElementCount;  set { _tunnelMeshElementCount  = value; OnPropertyChanged(); } }
    public int TunnelInteriorElementCount { get => _tunnelInteriorElementCount; set { _tunnelInteriorElementCount = value; OnPropertyChanged(); } }
    public int OuterBoundaryNodeCount  { get => _outerBoundaryNodeCount;  set { _outerBoundaryNodeCount  = value; OnPropertyChanged(); } }
    public int OuterBoundaryEdgeCount  { get => _outerBoundaryEdgeCount;  set { _outerBoundaryEdgeCount  = value; OnPropertyChanged(); } }
    public int TotalElementsTouchingOuterEdges { get => _totalElementsTouchingOuterEdges; set { _totalElementsTouchingOuterEdges = value; OnPropertyChanged(); } }
    public double AverageElementsPerOuterEdge  => OuterBoundaryEdgeCount > 0 ? (double)TotalElementsTouchingOuterEdges / OuterBoundaryEdgeCount : 0;

    public System.Collections.ObjectModel.ObservableCollection<Models.EdgeConnectivityRow> EdgeElementConnectivity { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<Models.EdgeConnectivityRow> EdgeNodeConnectivity    { get; } = new();

    public ICommand GenerateTunnelMeshCommand        { get; private set; } = null!;
    public ICommand ResetTunnelMeshingDefaultsCommand { get; private set; } = null!;
    public ICommand ExportEdgeElementConnectivityToExcelCommand { get; private set; } = null!;
    public ICommand ExportEdgeNodeConnectivityToExcelCommand    { get; private set; } = null!;

    public MeshViewModel()
    {
        _importBoundaryToGeometryCommand = new RelayCommand(
            ExecuteImportBoundaryToGeometry,
            CanImportBoundaryToGeometry);
        Generate1DMeshCommand = new RelayCommand(OnGenerate1DMesh);
        GenerateTriangleMeshStructuredCommand   = new RelayCommand(OnGenerateTriangleMeshStructured);
        GenerateTriangleMeshUnstructuredCommand = new RelayCommand(OnGenerateTriangleMeshUnstructured);
        GenerateRectangularMeshCommand          = new RelayCommand(OnGenerateRectangularMesh);
        DiscretizeCommand                       = new RelayCommand(() => { });
        GenerateMeshCommand                     = new RelayCommand(() => { });
        ClearMeshCommand                        = new RelayCommand(ClearMesh);
        ToggleExcavateElementsModeCommand       = new RelayCommand(ToggleExcavateElementsMode);
        // Surface Mesh subtab
        RefreshSurfaceListCommand       = new RelayCommand(RefreshSurfaceMeshRows);
        GenerateSurfaceMeshCommand      = new RelayCommand(() => ExecuteGenerateSurfaceMesh());
        ResetSurfaceMeshDefaultsCommand = new RelayCommand(ExecuteResetSurfaceMeshDefaults);
        Rs2dDiscretizeCommand = new RelayCommand(ExecuteRs2dDiscretize);
        Rs2dMeshCommand       = new RelayCommand(ExecuteRs2dMesh, () => Rs2dDiscretized);
        Rs2dOkCommand         = new RelayCommand(ExecuteRs2dOk);
        Rs2dCancelCommand     = new RelayCommand(ExecuteRs2dCancel);
        // Tunnel Meshing subtab
        GenerateTunnelMeshCommand               = new RelayCommand(ExecuteGenerateTunnelMesh);
        ResetTunnelMeshingDefaultsCommand       = new RelayCommand(ExecuteResetTunnelMeshingDefaults);
        ExportEdgeElementConnectivityToExcelCommand = new RelayCommand(ExportEdgeElementConnectivity);
        ExportEdgeNodeConnectivityToExcelCommand    = new RelayCommand(ExportEdgeNodeConnectivity);
        UpdateTwoDNodeOptions();
        MeshElements.CollectionChanged += OnMeshElementsCollectionChanged;
        MeshInformationRows.CollectionChanged += (_, _) => _importBoundaryToGeometryCommand.RaiseCanExecuteChanged();
        DiscretizationNodes.CollectionChanged += (_, _) => _importBoundaryToGeometryCommand.RaiseCanExecuteChanged();
        // Mesh popup "Mesh Canvas Display": both off until the user enables them (new sessions / before project load).
        ShowMeshElements = false;
        ShowMeshNodes = false;
    }

    private bool CanImportBoundaryToGeometry() =>
        IsDiscretized
        && GeometryDefinitionVM != null
        && MeshInformationRows.Count > 0
        && DiscretizationNodes.Count >= 3;

    private void ExecuteImportBoundaryToGeometry()
    {
        if (GeometryDefinitionVM == null)
        {
            ImportBoundaryStatusMessage = "Geometry Definition is not available.";
            return;
        }

        GeometryDefinitionVM.ImportBoundaryFromMesh();
        ImportBoundaryStatusMessage = GeometryDefinitionVM.GeometryImportStatusMessage;

        if (GeometryDefinitionVM.GeometryLineSegments.Count > 0)
        {
            GeometryDefinitionVM.RefreshMaterialLayerCanvasOverlays();
            RequestShowGeometryTab?.Invoke();
        }
    }

    private void OnMeshElementsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_selectedMeshInformationRow != null)
            ApplyMeshElementHighlightFromInfoRow(_selectedMeshInformationRow);
    }

    private int _meshCanvasBulkUpdateDepth;

    /// <summary>Suppresses per-node mesh canvas refresh while tables are populated in bulk.</summary>
    public bool IsMeshCanvasBulkUpdate => _meshCanvasBulkUpdateDepth > 0;

    public IDisposable BeginMeshCanvasBulkUpdate()
    {
        _meshCanvasBulkUpdateDepth++;
        return new MeshCanvasBulkUpdateScope(this);
    }

    private sealed class MeshCanvasBulkUpdateScope : IDisposable
    {
        private MeshViewModel? _owner;

        public MeshCanvasBulkUpdateScope(MeshViewModel owner) => _owner = owner;

        public void Dispose()
        {
            if (_owner == null) return;
            _owner._meshCanvasBulkUpdateDepth = Math.Max(0, _owner._meshCanvasBulkUpdateDepth - 1);
            if (_owner._meshCanvasBulkUpdateDepth == 0)
                _owner.NotifyMeshCanvasDataChanged();
            _owner = null;
        }
    }

    /// <summary>Notify mesh canvas wrappers to reproject nodes/elements after axis or mesh data changes.</summary>
    public void NotifyMeshCanvasDataChanged()
    {
        if (IsMeshCanvasBulkUpdate) return;
        EnsureDiscretizationNodeIds();
        EnsureMeshInformationElementIds();
        OnPropertyChanged(nameof(DiscretizationNodes));
        OnPropertyChanged(nameof(MeshElements));
        OnPropertyChanged(nameof(MeshInformationRows));
        OnPropertyChanged(nameof(IsDiscretized));
    }

    /// <summary>
    /// Legacy paths and .xfem load leave <see cref="MeshDiscretizationNodeDisplay.NodeId"/> at 0.
    /// Assign contiguous 1-based ids so lookups and BC markers stay stable.
    /// </summary>
    public void EnsureDiscretizationNodeIds()
    {
        for (int i = 0; i < DiscretizationNodes.Count; i++)
        {
            if (DiscretizationNodes[i].NodeId <= 0)
                DiscretizationNodes[i].NodeId = i + 1;
        }
    }

    /// <summary>Fill missing 1-based element ids from canvas display ids or row order.</summary>
    public void EnsureMeshInformationElementIds()
    {
        for (int i = 0; i < MeshInformationRows.Count; i++)
        {
            var row = MeshInformationRows[i];
            if (row.ElementId > 0) continue;
            if (i < MeshElements.Count && MeshElements[i].DisplayId > 0)
                row.ElementId = MeshElements[i].DisplayId;
            else
                row.ElementId = i + 1;
        }
    }

    /// <summary>
    /// Refreshes OneDMeshLength from the geometry in the Geometry Definition tab. Call when opening 1D Mesh subtab.
    /// </summary>
    public void RefreshLengthFromGeometry()
    {
        if (GeometryDefinitionVM == null)
        {
            OneDMeshLength = "";
            return;
        }
        var length = GeometryDefinitionVM.GetTotalPathLength();
        OneDMeshLength = length > 0 ? length.ToString("G6", CultureInfo.InvariantCulture) : "";
    }

    // =========================================================================
    //  Script → GUI: apply mesh_type (T3/T6/Q4/Q8) from SRF .dat AnalysisOptions.
    // =========================================================================
    public void ApplyElementTypeFromScript(string meshKind)
    {
        var kind = (meshKind ?? "T3").Trim().ToUpperInvariant();
        if (kind is not ("T3" or "T6" or "Q4" or "Q8"))
            kind = "T3";

        UserDefinedMeshVM.ElementType = kind;

        Rs2dElementType = kind switch
        {
            "T6" => "6-node triangle",
            "Q4" => "4-node quadrilateral",
            "Q8" => "8-node quadrilateral",
            _ => "3-node triangle",
        };

        if (kind is "Q4" or "Q8")
        {
            if (TwoDElementFamilyOptions.Count == 0)
                UpdateElementAndNodeOptions();
            if (TwoDElementFamilyOptions.Contains("Quadrilaterals"))
            {
                SelectedTwoDElementFamily = "Quadrilaterals";
                SelectedTwoDNodeOption = kind == "Q8" ? "8-node" : "4-node";
            }
        }
        else
        {
            if (TwoDElementFamilyOptions.Count == 0)
                UpdateElementAndNodeOptions();
            if (TwoDElementFamilyOptions.Contains("Triangles"))
            {
                SelectedTwoDElementFamily = "Triangles";
                MeshElementOrder = kind == "T6" ? "6-node" : "3-node";
                SelectedTwoDNodeOption = kind == "T6" ? "6-node" : "3-node";
            }
        }
    }

    // =========================================================================
    //  Material layer etype refresh (multi-material p610 export)
    // =========================================================================

    /// <summary>
    /// Reassigns <see cref="MeshInformationRow.Etype"/> from geometry layer cuts when ≥2 materials are defined.
    /// Called before .dat export / FE run so the etype block matches Material Properties layer assignments.
    /// </summary>
    public void RefreshMaterialLayerEtypesFromGeometry()
    {
        if (MeshInformationRows.Count == 0)
            return;
        var materials = MaterialPropertiesVM;
        if (materials == null || materials.Materials.Count < 2)
            return;

        if (Q8MeshMaterialLayerPlanner.TryRefreshMeshInformationEtypes(
                MeshInformationRows,
                GeometryDefinitionVM,
                materials,
                out _))
            return;

        // Inclined (non-horizontal) layer zones: assign etype by element-centroid point-in-zone so
        // post-generation material reassignments propagate to the .dat export without remeshing.
        if (NonHorizontalLayerMeshPlan.TryCreatePlan(GeometryDefinitionVM, materials, out var nhPlan, out _)
            && nhPlan.HasZones)
        {
            foreach (var row in MeshInformationRows)
            {
                double cx = MeshRowCentroidX(row);
                double cy = MeshRowCentroidY(row);
                if (!double.IsFinite(cx) || !double.IsFinite(cy))
                    continue;
                row.Etype = nhPlan.ResolveEtype(cx, cy);
            }
        }
    }

    private static double MeshRowCentroidX(Models.MeshInformationRow row)
    {
        double sum = 0; int count = 0;
        void Add(double v) { if (double.IsFinite(v)) { sum += v; count++; } }
        Add(row.N1X); Add(row.N2X); Add(row.N3X);
        if (row.Node4 > 0 || row.IsQuad) Add(row.N4X);
        return count > 0 ? sum / count : double.NaN;
    }

    private static double MeshRowCentroidY(Models.MeshInformationRow row)
    {
        double sum = 0; int count = 0;
        void Add(double v) { if (double.IsFinite(v)) { sum += v; count++; } }
        Add(row.N1Y); Add(row.N2Y); Add(row.N3Y);
        if (row.Node4 > 0 || row.IsQuad) Add(row.N4Y);
        return count > 0 ? sum / count : double.NaN;
    }

    // =========================================================================
    //  Load mesh from a p610_t3 / p610_t3_elastic .dat file content string.
    //  Parses nodes + elements and populates DiscretizationNodes, MeshElements,
    //  MeshInformationRows so the mesh canvas shows the loaded geometry.
    // =========================================================================
    public bool LoadMeshFromDatContent(string datContent)
    {
        try
        {
            // Detect mesh kind from optional AnalysisOptions block (T3/T6/Q4/Q8); default T3.
            string meshType = "T3";
            P610AnalysisOptions? ao = null;
            if (P610AnalysisOptionsHelper.TryParse(datContent, out ao))
                meshType = ao.MeshType;
            int connNod = meshType.Equals("Q8", StringComparison.OrdinalIgnoreCase) ? 8
                       : meshType.Equals("T6", StringComparison.OrdinalIgnoreCase) ? 6
                       : meshType.Equals("Q4", StringComparison.OrdinalIgnoreCase) ? 4
                       : 3;
            bool isQuadMesh = (connNod == 4 || connNod == 8);
            bool isQ8 = (connNod == 8);
            bool isT6 = (connNod == 6);
            bool isExplicitDeck = DatMeshBoundaryImporter.IsExplicitMeshSourceDeck(datContent);
            bool isSmithEmbankmentQ8 = isQ8 && DatMeshBoundaryImporter.IsSmithEmbankmentQ8Deck(ao, datContent);
            bool smithQ8Deck = isSmithEmbankmentQ8;

            var tokens = new Queue<string>();
            foreach (var raw in datContent.Split('\n'))
            {
                var line = raw;
                var bang = line.IndexOf('!');
                if (bang >= 0) line = line.Substring(0, bang);
                foreach (var tok in line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    tokens.Enqueue(tok);
            }

            if (!TryDequeueInt(tokens, out int nels) ||
                !TryDequeueInt(tokens, out int nn)   ||
                !TryDequeueInt(tokens, out int npTypes)) return false;

            // skip 7 x np_types material properties
            for (int i = 0; i < 7 * npTypes; i++)
                if (!TryDequeueDouble(tokens, out _)) return false;

            // etype (omitted when np_types == 1)
            var etype = new int[nels];
            if (npTypes > 1)
                for (int i = 0; i < nels; i++)
                    if (!TryDequeueInt(tokens, out etype[i])) return false;

            // nodal coordinates
            var coords = new Point[nn];
            for (int i = 0; i < nn; i++)
            {
                if (!TryDequeueDouble(tokens, out double x) ||
                    !TryDequeueDouble(tokens, out double y)) return false;
                coords[i] = new Point(x, y);
            }

            // connectivity (1-based) — variable connectivity by mesh type
            var conn = new int[nels][];
            for (int i = 0; i < nels; i++)
            {
                var nodes = new int[connNod];
                for (int k = 0; k < connNod; k++)
                    if (!TryDequeueInt(tokens, out nodes[k])) return false;
                conn[i] = nodes;
            }

            double meshCoordYOffset = 0;
            DatMeshBoundaryImporter.TryGetMeshCoordYOffset(datContent, out meshCoordYOffset);
            if (isSmithEmbankmentQ8 && !isExplicitDeck && meshCoordYOffset <= 0)
            {
                DatMeshBoundaryImporter.TryGetEmbankmentMeshParams(
                    datContent, out int nx1, out int nx2, out int ny1, out int ny2,
                    out double w1, out double s1, out double w2, out double h1, out double h2);
                var cx = new double[nn];
                var cy = new double[nn];
                P64EmbankmentMeshBuilder.ApplyAveragedEmbankmentNodeCoords(
                    cx, cy, conn, nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2);
                for (int ni = 0; ni < nn; ni++)
                    coords[ni] = new Point(cx[ni], cy[ni]);
            }

            // populate collections
            SelectedMeshInformationRow = null;

            int embNx1 = 20, embNx2 = 10, embNy1 = 10, embNy2 = 5;
            double embW1 = 20, embS1 = 20, embW2 = 20, embH1 = 10, embH2 = 5;
            bool useEmbElementCoords = isSmithEmbankmentQ8 && !isExplicitDeck;
            if (useEmbElementCoords)
                DatMeshBoundaryImporter.TryGetEmbankmentMeshParams(
                    datContent, out embNx1, out embNx2, out embNy1, out embNy2,
                    out embW1, out embS1, out embW2, out embH1, out embH2);
            double embYOffset = meshCoordYOffset;

            using var bulkMeshCanvas = BeginMeshCanvasBulkUpdate();
            DiscretizationNodes.Clear();
            MeshElements.Clear();
            MeshInformationRows.Clear();
            BoundaryEdgeNodes.Clear();

            for (int ni = 0; ni < coords.Length; ni++)
                DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { NodeId = ni + 1, Location = coords[ni] });

            for (int ti = 0; ti < nels; ti++)
            {
                var nodes = conn[ti];
                Point p1, p2, p3, p4, p5, p6, p7, p8;
                if (useEmbElementCoords)
                {
                    var pts = P64EmbankmentMeshBuilder.GetElementPointsFromConnectivity(
                        ti + 1, nodes, embNx1, embNx2, embNy1, embNy2, embW1, embS1, embW2, embH1, embH2, embYOffset);
                    p1 = new Point(pts[0].x, pts[0].y);
                    p2 = new Point(pts[1].x, pts[1].y);
                    p3 = new Point(pts[2].x, pts[2].y);
                    p4 = new Point(pts[3].x, pts[3].y);
                    p5 = new Point(pts[4].x, pts[4].y);
                    p6 = new Point(pts[5].x, pts[5].y);
                    p7 = new Point(pts[6].x, pts[6].y);
                    p8 = new Point(pts[7].x, pts[7].y);
                }
                else
                {
                    p1 = coords[nodes[0] - 1];
                    p2 = coords[nodes[1] - 1];
                    p3 = coords[nodes[2] - 1];
                    p4 = connNod >= 4 ? coords[nodes[3] - 1] : default;
                    p5 = connNod >= 5 ? coords[nodes[4] - 1] : default;
                    p6 = connNod >= 6 ? coords[nodes[5] - 1] : default;
                    p7 = connNod >= 7 ? coords[nodes[6] - 1] : default;
                    p8 = connNod >= 8 ? coords[nodes[7] - 1] : default;
                }

                double cx, cy;
                PointCollection outline;
                bool isQuadDisplay = false, isQ8Display = false, isT6Display = false;

                if (isQ8)
                {
                    outline = Q8MeshDisplayHelper.BuildCornerQuadOutline(
                        p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, p4.X, p4.Y,
                        p5.X, p5.Y, p6.X, p6.Y, p7.X, p7.Y, p8.X, p8.Y,
                        smithQ8Deck);
                    (cx, cy) = Q8MeshDisplayHelper.CornerCentroid(smithQ8Deck, p1, p2, p3, p4, p5, p6, p7, p8);
                    isQuadDisplay = true; isQ8Display = true;
                }
                else if (isQuadMesh)
                {
                    outline = new PointCollection { p1, p2, p3, p4 };
                    cx = (p1.X + p2.X + p3.X + p4.X) / 4.0;
                    cy = (p1.Y + p2.Y + p3.Y + p4.Y) / 4.0;
                    isQuadDisplay = true;
                }
                else if (isT6)
                {
                    outline = new PointCollection { p1, p4, p2, p5, p3, p6 };
                    cx = (p1.X + p2.X + p3.X) / 3.0;
                    cy = (p1.Y + p2.Y + p3.Y) / 3.0;
                    isT6Display = true;
                }
                else
                {
                    outline = new PointCollection { p1, p2, p3 };
                    cx = (p1.X + p2.X + p3.X) / 3.0;
                    cy = (p1.Y + p2.Y + p3.Y) / 3.0;
                }
                EnsureCounterClockwisePolygon(outline);

                MeshElements.Add(new MeshElementDisplay
                {
                    Nodes = outline,
                    CenterX = cx, CenterY = cy,
                    DisplayId = ti + 1,
                    IsActive = true,
                    IsHighlighted = false,
                    IsQuadrilateral = isQuadDisplay,
                    IsQuadraticQuad = isQ8Display,
                    IsQuadraticTriangle = isT6Display,
                    Q8SmithEmb2dLocalOrder = isQ8Display && smithQ8Deck,
                });
                MeshInformationRows.Add(new MeshInformationRow
                {
                    SmithEmb2dLocalOrder = isQ8 && smithQ8Deck,
                    ElementId = ti + 1,
                    Node1 = nodes[0], Node2 = nodes[1], Node3 = nodes[2],
                    Node4 = connNod >= 4 ? nodes[3] : 0,
                    Node5 = connNod >= 5 ? nodes[4] : 0,
                    Node6 = connNod >= 6 ? nodes[5] : 0,
                    Node7 = connNod >= 7 ? nodes[6] : 0,
                    Node8 = connNod >= 8 ? nodes[7] : 0,
                    Etype = npTypes > 1 ? etype[ti] : 1,
                    N1X = p1.X, N1Y = p1.Y,
                    N2X = p2.X, N2Y = p2.Y,
                    N3X = p3.X, N3Y = p3.Y,
                    N4X = p4.X, N4Y = p4.Y,
                    N5X = p5.X, N5Y = p5.Y,
                    N6X = p6.X, N6Y = p6.Y,
                    N7X = p7.X, N7Y = p7.Y,
                    N8X = p8.X, N8Y = p8.Y,
                    Kind = isQ8 ? "Q8" : isQuadMesh ? "Q4" : isT6 ? "T6" : "T3",
                    Status = "Active"
                });
            }

            IsDiscretized = true;

            MeshCanvasAxisFitter.FitToDiscretizationNodes(GeometryDefinitionVM, DiscretizationNodes);

            GeometryDefinitionVM?.ImportBoundaryFromMesh();
            // NotifyMeshCanvasDataChanged runs when bulkMeshCanvas is disposed.

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDequeueInt(Queue<string> q, out int value)
    {
        value = 0;
        return q.Count > 0 && int.TryParse(q.Dequeue(), out value);
    }

    private static bool TryDequeueDouble(Queue<string> q, out double value)
    {
        value = 0;
        return q.Count > 0 && double.TryParse(
            q.Dequeue(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    private void ClearMesh()
    {
        RecordUndo("Clear mesh");
        IsExcavateElementsMode = false;
        SelectedMeshInformationRow = null;
        DiscretizationNodes.Clear();
        MeshElements.Clear();
        MeshInformationRows.Clear();
        BoundaryEdgeNodes.Clear();
        IsDiscretized = false;
        Rs2dDiscretized = false;
        ShowBoundaryDiscretizationMarkers = false;
        OneDMeshStatus = "Mesh cleared.";
        SurfaceMeshStatus = "Mesh cleared.";
        RectangularMeshStatus = "";
        TriangleMeshStatus = "";
    }

    private void ToggleExcavateElementsMode()
    {
        if (!IsDiscretized || MeshElements.Count == 0)
        {
            SetExcavateElementsStatusMessage("Cannot enter element excavation mode: mesh is not generated yet.");
            return;
        }
        IsExcavateElementsMode = !IsExcavateElementsMode;
        SetExcavateElementsStatusMessage(IsExcavateElementsMode
            ? "Excavate Elements mode ON. Click an active element in canvas."
            : "Excavate Elements mode OFF.");
    }

    /// <summary>Set element active state + status row and raise dependent counters.</summary>
    public bool SetElementActiveState(int elementId, bool isActive, string? statusOverride = null)
    {
        var el = MeshElements.FirstOrDefault(e => e.DisplayId == elementId);
        if (el == null) return false;
        if (el.IsActive == isActive && string.IsNullOrWhiteSpace(statusOverride))
            return false;

        if (!isActive && !string.Equals(statusOverride, "Excavated", StringComparison.Ordinal))
            RecordUndo("Deactivate mesh element");
        el.IsActive = isActive;
        int eIdx = MeshElements.IndexOf(el);
        if (eIdx >= 0)
            MeshElements[eIdx] = el;

        var row = MeshInformationRows.FirstOrDefault(r => r.ElementId == elementId);
        if (row != null)
        {
            row.Status = statusOverride ?? (isActive ? "Active" : "Excavated");
            int rIdx = MeshInformationRows.IndexOf(row);
            if (rIdx >= 0)
                MeshInformationRows[rIdx] = row;
        }

        OnPropertyChanged(nameof(ActiveElementCount));
        OnPropertyChanged(nameof(DeactivatedElementCount));
        return true;
    }

    /// <summary>
    /// Stage-aware bulk sync: every element's active state is derived from the cumulative excavated set
    /// up to the active footer stage. Later-stage excavations become visible again on earlier stages.
    /// </summary>
    public void SyncElementActiveStatesToExcavatedSet(IReadOnlySet<int> excavatedIds)
    {
        if (MeshElements.Count == 0) return;
        excavatedIds ??= new HashSet<int>();

        var rowIndexByElementId = new Dictionary<int, int>(MeshInformationRows.Count);
        for (int i = 0; i < MeshInformationRows.Count; i++)
        {
            if (!rowIndexByElementId.ContainsKey(MeshInformationRows[i].ElementId))
                rowIndexByElementId[MeshInformationRows[i].ElementId] = i;
        }

        var changedIndices = new List<int>();
        for (int i = 0; i < MeshElements.Count; i++)
        {
            var el = MeshElements[i];
            bool targetActive = !excavatedIds.Contains(el.DisplayId);
            if (el.IsActive != targetActive)
                changedIndices.Add(i);
        }
        if (changedIndices.Count == 0) return;

        using (BeginMeshCanvasBulkUpdate())
        {
            foreach (int i in changedIndices)
            {
                var el = MeshElements[i];
                bool targetActive = !excavatedIds.Contains(el.DisplayId);

                el.IsActive = targetActive;
                MeshElements[i] = el;

                if (rowIndexByElementId.TryGetValue(el.DisplayId, out int rowIdx))
                {
                    var row = MeshInformationRows[rowIdx];
                    row.Status = targetActive ? "Active" : "Excavated";
                    MeshInformationRows[rowIdx] = row;
                }
            }
        }

        OnPropertyChanged(nameof(ActiveElementCount));
        OnPropertyChanged(nameof(DeactivatedElementCount));
    }

    /// <summary>
    /// Called by MeshTabCanvasWrapper after click hit-test in canvas space.
    /// Commits this element ID into the active staged-construction stage.
    /// </summary>
    public bool TryExcavateElementById(int elementId)
    {
        if (!IsExcavateElementsMode || !IsDiscretized || elementId <= 0)
        {
            SetExcavateElementsStatusMessage("Element click ignored: excavation mode is OFF or mesh is not ready.");
            return false;
        }

        var staged = GeometryDefinitionVM?.StagedConstruction;
        if (staged == null)
        {
            SetExcavateElementsStatusMessage("Staged construction context is unavailable.");
            return false;
        }

        bool ok = staged.CommitElementExcavationFromMesh(elementId);
        SetExcavateElementsStatusMessage(staged.StatusMessage);
        return ok;
    }

    public void SetExcavateElementsStatusMessage(string message)
        => ExcavateElementsStatusMessage = message ?? "";

    private static bool IsLinearThreeNodeTriangles(MeshViewModel vm) =>
        string.Equals(vm.SelectedTwoDNodeOption, "3-node", StringComparison.Ordinal);

    private void OnGenerateTriangleMeshStructured()
    {
        RefreshTriangleMeshStructuredExtentsFromGeometry();
        GenerateStructuredRectangularTriangleMesh(msg => TriangleMeshStatus = msg, useGeometryExtentsForLAndH: true);
    }

    private void OnGenerateRectangularMesh()
    {
        GenerateStructuredRectangularTriangleMesh(msg => RectangularMeshStatus = msg, useGeometryExtentsForLAndH: false);
    }

    /// <summary>Structured rectangle split into linear triangles (geom_rect topology). Respects Node = 3-node only.</summary>
    /// <param name="useGeometryExtentsForLAndH">When true (Create triangle mesh subtab), L and H come from geometry canvas extent, not L/h textboxes.</param>
    private void GenerateStructuredRectangularTriangleMesh(Action<string> setStatus, bool useGeometryExtentsForLAndH)
    {
        if (!Is2DProblem || !string.Equals(_selectedTwoDElementFamily, "Triangles", StringComparison.Ordinal))
        {
            setStatus("Select 2D Problem and Element: Triangles.");
            return;
        }
        if (!IsLinearThreeNodeTriangles(this))
        {
            setStatus("Structured mesh generation supports 3-node linear triangles only. Select Node: 3-node.");
            return;
        }
        if (!int.TryParse(RectangularMeshNx, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nxe) || nxe < 1)
        {
            setStatus("Enter nx ≥ 1 (elements in x).");
            return;
        }
        if (!int.TryParse(RectangularMeshNy, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nye) || nye < 1)
        {
            setStatus("Enter ny ≥ 1 (elements in y).");
            return;
        }

        var xCoords = new List<double>(nxe + 1);
        var yCoords = new List<double>(nye + 1);
        bool structuredMeshUsedPolygonForExtent = false;

        if (useGeometryExtentsForLAndH)
        {
            if (GeometryDefinitionVM == null)
            {
                setStatus("Geometry is not linked.");
                return;
            }
            structuredMeshUsedPolygonForExtent = GeometryDefinitionVM.TryGetTriangleMeshPolygonAxisPoints(out _);
            if (!structuredMeshUsedPolygonForExtent &&
                GeometryDefinitionVM.GeometrySurfaces.Count > 0)
            {
                setStatus(
                    "Complete the surface polygon for S1 (lock vertices or pick boundary lines) so structured mesh fits the filled region only — not the full model extent.");
                return;
            }
            var (minX, maxX, minY, maxY) = GeometryDefinitionVM.GetMeshBoundingBoxBounds();
            double Lg = maxX - minX;
            double Hg = maxY - minY;
            if (Lg <= 1e-12 || Hg <= 1e-12)
            {
                setStatus("Geometry has no positive extent (add vertices or define a surface polygon).");
                return;
            }
            for (int i = 0; i <= nxe; i++)
                xCoords.Add(minX + Lg * i / nxe);
            for (int j = 0; j <= nye; j++)
                yCoords.Add(maxY - Hg * j / nye);
        }
        else
        {
            if (!double.TryParse(RectangularMeshL, NumberStyles.Float, CultureInfo.InvariantCulture, out var L) || L <= 0)
            {
                setStatus("Enter a positive length L.");
                return;
            }
            if (!double.TryParse(RectangularMeshH, NumberStyles.Float, CultureInfo.InvariantCulture, out var H) || H <= 0)
            {
                setStatus("Enter a positive height H.");
                return;
            }
            for (int i = 0; i <= nxe; i++)
                xCoords.Add(L * i / nxe);
            for (int j = 0; j <= nye; j++)
                yCoords.Add(-H * j / nye);
        }

        try
        {
            var result = GeomRectTriangleMeshBuilder.Build(xCoords, yCoords, MeshNumberingDirectionChar);
            RecordUndo(useGeometryExtentsForLAndH ? "Generate structured triangle mesh" : "Generate rectangular mesh");
            ApplyGeomRectResultToMesh(result);
            var msg = $"Created {result.Triangles.Count} triangles, {result.NodePoints.Length} nodes (structured).";
            if (useGeometryExtentsForLAndH && !structuredMeshUsedPolygonForExtent)
            {
                msg +=
                    " Extent used the axis-aligned box of all vertices in Geometry Definition — not only corners (1,1)–(2,2). Add and lock a surface on your rectangle so the mesh matches that region only.";
            }
            setStatus(msg);
        }
        catch (Exception ex)
        {
            setStatus(ex.Message);
        }
    }

    private void OnGenerateTriangleMeshUnstructured()
    {
        if (!Is2DProblem || !string.Equals(_selectedTwoDElementFamily, "Triangles", StringComparison.Ordinal))
        {
            TriangleMeshStatus = "Select 2D Problem and Element: Triangles.";
            return;
        }
        if (!IsLinearThreeNodeTriangles(this))
        {
            TriangleMeshStatus = "Unstructured mesh generation supports 3-node linear triangles only. Select Node: 3-node.";
            return;
        }
        if (GeometryDefinitionVM == null)
        {
            TriangleMeshStatus = "Geometry is not linked.";
            return;
        }
        if (!GeometryDefinitionVM.TryGetTriangleMeshPolygonAxisPoints(out var poly) || poly.Count < 3)
        {
            TriangleMeshStatus = "Define a closed surface polygon (Geometry Definition) with at least three vertices.";
            return;
        }

        if (!PolygonEarTriangulation.TryTriangulate(poly, out var tris) || tris.Count == 0)
        {
            TriangleMeshStatus = "Could not triangulate the polygon (self-intersection or degenerate boundary?).";
            return;
        }

        var nodePoints = poly.ToArray();
        RecordUndo("Generate unstructured triangle mesh");
        ApplyLinearTriangleMesh(nodePoints, tris);
        TriangleMeshStatus = $"Created {tris.Count} triangles, {nodePoints.Length} nodes (unstructured, ear clipping).";
    }

    private void ApplyGeomRectResultToMesh(GeomRectTriangleMeshResult result)
    {
        DiscretizationNodes.Clear();
        MeshElements.Clear();
        MeshInformationRows.Clear();
        BoundaryEdgeNodes.Clear();
        ShowBoundaryDiscretizationMarkers = false;

        foreach (var p in result.NodePoints)
            DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { Location = p });

        for (int ti = 0; ti < result.Triangles.Count; ti++)
        {
            var (n1, n2, n3) = result.Triangles[ti];
            var p1 = result.NodePoints[n1 - 1];
            var p2 = result.NodePoints[n2 - 1];
            var p3 = result.NodePoints[n3 - 1];
            var nodes = new PointCollection { p1, p2, p3 };
            double cx = (p1.X + p2.X + p3.X) / 3.0;
            double cy = (p1.Y + p2.Y + p3.Y) / 3.0;
            MeshElements.Add(new MeshElementDisplay
            {
                Nodes = nodes,
                CenterX = cx,
                CenterY = cy,
                DisplayId = ti + 1,
                IsActive = true,
                IsHighlighted = false
            });
            MeshInformationRows.Add(new MeshInformationRow
            {
                ElementId = ti + 1,
                Node1 = n1,
                Node2 = n2,
                Node3 = n3,
                Etype = 1,
                N1X = p1.X, N1Y = p1.Y,
                N2X = p2.X, N2Y = p2.Y,
                N3X = p3.X, N3Y = p3.Y,
                Status = "Active"
            });
        }

        IsDiscretized = true;
    }

    private void ApplyLinearTriangleMesh(Point[] nodePoints, IReadOnlyList<(int i, int j, int k)> trianglesZb)
    {
        DiscretizationNodes.Clear();
        MeshElements.Clear();
        MeshInformationRows.Clear();
        BoundaryEdgeNodes.Clear();
        ShowBoundaryDiscretizationMarkers = false;

        foreach (var p in nodePoints)
            DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { Location = p });

        for (int ti = 0; ti < trianglesZb.Count; ti++)
        {
            var (ia, ib, ic) = trianglesZb[ti];
            var p1 = nodePoints[ia];
            var p2 = nodePoints[ib];
            var p3 = nodePoints[ic];
            var nodes = new PointCollection { p1, p2, p3 };
            double cx = (p1.X + p2.X + p3.X) / 3.0;
            double cy = (p1.Y + p2.Y + p3.Y) / 3.0;
            int n1 = ia + 1, n2 = ib + 1, n3 = ic + 1;
            MeshElements.Add(new MeshElementDisplay
            {
                Nodes = nodes,
                CenterX = cx,
                CenterY = cy,
                DisplayId = ti + 1,
                IsActive = true,
                IsHighlighted = false
            });
            MeshInformationRows.Add(new MeshInformationRow
            {
                ElementId = ti + 1,
                Node1 = n1,
                Node2 = n2,
                Node3 = n3,
                Etype = 1,
                N1X = p1.X, N1Y = p1.Y,
                N2X = p2.X, N2Y = p2.Y,
                N3X = p3.X, N3Y = p3.Y,
                Status = "Active"
            });
        }

        IsDiscretized = true;
    }

    private void OnGenerate1DMesh()
    {
        if (GeometryDefinitionVM == null)
        {
            OneDMeshStatus = "Geometry not linked.";
            return;
        }
        double length = GeometryDefinitionVM.GetTotalPathLength();
        if (length <= 0)
        {
            OneDMeshStatus = "Create geometry in the Geometry Definition tab first (at least 2 points).";
            return;
        }
        if (!int.TryParse(OneDMeshN, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1)
        {
            OneDMeshStatus = "Enter a positive integer for n (number of elements).";
            return;
        }

        var points = GeometryDefinitionVM.GetPointsAlongPath(length, n);
        if (points.Count == 0)
        {
            OneDMeshStatus = "Could not get points along path.";
            return;
        }

        RecordUndo("Generate 1D mesh");
        DiscretizationNodes.Clear();
        MeshElements.Clear();
        MeshInformationRows.Clear();
        BoundaryEdgeNodes.Clear();
        ShowBoundaryDiscretizationMarkers = false;

        var segmentBounds = GeometryDefinitionVM.GetSegmentBounds();
        var boundarySegCount = GeometryDefinitionVM.GeometryLineSegments.Count(s => s.IsBoundaryEdge);
        int npTypes = Math.Max(1, boundarySegCount);

        foreach (var (x, y) in points)
            DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { Location = new Point(x, y) });

        for (int i = 0; i < n; i++)
        {
            double x1 = points[i].X, y1 = points[i].Y;
            double x2 = points[i + 1].X, y2 = points[i + 1].Y;
            var nodes = new PointCollection { new Point(x1, y1), new Point(x2, y2) };
            MeshElements.Add(new MeshElementDisplay
            {
                Nodes = nodes,
                CenterX = (x1 + x2) / 2,
                CenterY = (y1 + y2) / 2,
                DisplayId = i + 1,
                IsActive = true,
                IsHighlighted = false
            });

            double elemCenter = (i + 0.5) * (length / n);
            int layer = 0;
            if (segmentBounds.Count >= 2)
            {
                for (int k = 0; k < segmentBounds.Count - 1; k++)
                {
                    if (elemCenter < segmentBounds[k + 1])
                    {
                        layer = k;
                        break;
                    }
                    layer = segmentBounds.Count - 2;
                }
            }
            int etype = Math.Min(layer + 1, npTypes);

            MeshInformationRows.Add(new MeshInformationRow
            {
                ElementId = i + 1,
                Node1 = i + 1,
                Node2 = i + 2,
                Node3 = 0,
                Etype = etype,
                N1X = x1, N1Y = y1,
                N2X = x2, N2Y = y2,
                N3X = 0, N3Y = 0,
                Status = "Active"
            });
        }

        IsDiscretized = true;
        OneDMeshStatus = $"1D mesh created: length = {length}, {n} elements, {n + 1} nodes. Geometry Definition tab unchanged.";
    }

    // ══════════════════════════════════════════════════════════════════════════
    // RS2D subtab — RS2-style mesh setup (discretize / mesh / OK / Cancel)

    private string _rs2dMeshType = "Graded";
    private string _rs2dElementType = "3-node triangle";
    private string _rs2dGradationFactor = "1.3";
    private string _rs2dExcavationNodeCount = "110";
    private string _rs2dBoundaryNodeCount = "";
    private string _rs2dTargetElementCount = "";
    private bool _rs2dAdvancedExpanded;
    private bool _rs2dDiscretized;
    private bool _showBoundaryDiscretizationMarkers;

    /// <summary>When true, Mesh tab draws red crosses at discretization nodes (RS2D boundary preview; cleared after full mesh generation).</summary>
    public bool ShowBoundaryDiscretizationMarkers
    {
        get => _showBoundaryDiscretizationMarkers;
        set
        {
            if (_showBoundaryDiscretizationMarkers == value) return;
            _showBoundaryDiscretizationMarkers = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> Rs2dMeshTypeOptions { get; } = new[]
    {
        "Graded", "Uniform", "Radial", "No Internal Nodes"
    };

    public IReadOnlyList<string> Rs2dElementTypeOptions { get; } = new[]
    {
        "3-node triangle", "6-node triangle", "4-node quadrilateral", "8-node quadrilateral"
    };

    /// <summary>Maps persisted or legacy RS2D element labels to supported unstructured mesh types.</summary>
    public static string NormalizeRs2dElementType(string? elementType)
    {
        return MapRs2dElementTypeToKind(elementType) switch
        {
            "T6" => "6-node triangle",
            "Q4" => "4-node quadrilateral",
            "Q8" => "8-node quadrilateral",
            _ => "3-node triangle",
        };
    }

    public string Rs2dMeshType
    {
        get => _rs2dMeshType;
        set { _rs2dMeshType = value ?? "Graded"; OnPropertyChanged(); }
    }

    public string Rs2dElementType
    {
        get => _rs2dElementType;
        set
        {
            _rs2dElementType = NormalizeRs2dElementType(value);
            OnPropertyChanged();
        }
    }

    public string Rs2dGradationFactor
    {
        get => _rs2dGradationFactor;
        set { _rs2dGradationFactor = value ?? ""; OnPropertyChanged(); }
    }

    public string Rs2dExcavationNodeCount
    {
        get => _rs2dExcavationNodeCount;
        set { _rs2dExcavationNodeCount = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>
    /// Optional outer-boundary discretization node count for Unstructured Mesh.
    /// When &gt; 0, Discretize uses average spacing ≈ perimeter / Nb.
    /// </summary>
    public string Rs2dBoundaryNodeCount
    {
        get => _rs2dBoundaryNodeCount;
        set { _rs2dBoundaryNodeCount = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>
    /// Optional approximate target element count for Unstructured Mesh.
    /// When &gt; 0, Global/Near sizes are derived from domain area before Discretize/Mesh.
    /// </summary>
    public string Rs2dTargetElementCount
    {
        get => _rs2dTargetElementCount;
        set { _rs2dTargetElementCount = value ?? ""; OnPropertyChanged(); }
    }

    public bool Rs2dAdvancedExpanded
    {
        get => _rs2dAdvancedExpanded;
        set { _rs2dAdvancedExpanded = value; OnPropertyChanged(); }
    }

    /// <summary>After Discretize, Mesh becomes enabled (RS2D workflow).</summary>
    public bool Rs2dDiscretized
    {
        get => _rs2dDiscretized;
        set { _rs2dDiscretized = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Optional RS2D quality tools (all opt-in via toggles; default = off)
    // ──────────────────────────────────────────────────────────────────────

    private bool _rs2dShowBestPracticeWarnings;
    private bool _rs2dStrictWarningBlockers;
    private bool _rs2dShowQualityDashboard;
    private bool _rs2dQualityEnableSlider;
    private double _rs2dQuadConversionSlider = 0.6;
    private bool _rs2dHighlightUnmatched;
    private bool _rs2dRequireAllQuads;
    private bool _rs2dShowSolverReadiness;
    private bool _rs2dEnableStructuredHalo;
    private List<Models.UnmatchedTriangleInfo> _rs2dUnmatchedTriangles = new();
    private List<Models.Rs2dWarning> _rs2dWarnings = new();
    private string _rs2dSolverReadinessStatus = "";
    private string _rs2dSolverReadinessSeverity = "Ready"; // Ready | Approximate | Blocked
    private List<Point> _rs2dDiscretizationPoints = new();
    private List<Backend.Rs2dBoundarySegment> _rs2dBoundarySegments = new();
    private Backend.MeshJacobianValidator.Result? _lastJacobianResult;

    public bool Rs2dShowBestPracticeWarnings
    {
        get => _rs2dShowBestPracticeWarnings;
        set { _rs2dShowBestPracticeWarnings = value; OnPropertyChanged(); RefreshRs2dWarnings(); }
    }
    public bool Rs2dStrictWarningBlockers
    {
        get => _rs2dStrictWarningBlockers;
        set { _rs2dStrictWarningBlockers = value; OnPropertyChanged(); }
    }
    public bool Rs2dShowQualityDashboard
    {
        get => _rs2dShowQualityDashboard;
        set { _rs2dShowQualityDashboard = value; OnPropertyChanged(); }
    }
    public bool Rs2dQualityEnableSlider
    {
        get => _rs2dQualityEnableSlider;
        set { _rs2dQualityEnableSlider = value; OnPropertyChanged(); }
    }
    public double Rs2dQuadConversionSlider
    {
        get => _rs2dQuadConversionSlider;
        set { _rs2dQuadConversionSlider = Math.Max(0.0, Math.Min(1.0, value)); OnPropertyChanged(); }
    }
    public bool Rs2dHighlightUnmatched
    {
        get => _rs2dHighlightUnmatched;
        set
        {
            _rs2dHighlightUnmatched = value;
            OnPropertyChanged();
            ApplyUnmatchedHighlight(value);
        }
    }
    public bool Rs2dRequireAllQuads
    {
        get => _rs2dRequireAllQuads;
        set { _rs2dRequireAllQuads = value; OnPropertyChanged(); RefreshRs2dWarnings(); }
    }
    public bool Rs2dShowSolverReadiness
    {
        get => _rs2dShowSolverReadiness;
        set { _rs2dShowSolverReadiness = value; OnPropertyChanged(); RefreshSolverReadiness(); }
    }
    public bool Rs2dEnableStructuredHalo
    {
        get => _rs2dEnableStructuredHalo;
        set { _rs2dEnableStructuredHalo = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<Models.UnmatchedTriangleInfo> Rs2dUnmatchedTrianglesReadonly => _rs2dUnmatchedTriangles;
    public IReadOnlyList<Models.Rs2dWarning> Rs2dWarningsReadonly => _rs2dWarnings;
    public string Rs2dSolverReadinessStatus
    {
        get => _rs2dSolverReadinessStatus;
        private set { _rs2dSolverReadinessStatus = value ?? ""; OnPropertyChanged(); }
    }
    public string Rs2dSolverReadinessSeverity
    {
        get => _rs2dSolverReadinessSeverity;
        private set { _rs2dSolverReadinessSeverity = value ?? "Ready"; OnPropertyChanged(); }
    }

    public List<Models.UnmatchedTriangleInfo> Rs2dUnmatchedTriangles
    {
        get => _rs2dUnmatchedTriangles;
        private set
        {
            _rs2dUnmatchedTriangles = value ?? new List<Models.UnmatchedTriangleInfo>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Rs2dUnmatchedTrianglesReadonly));
            OnPropertyChanged(nameof(Rs2dUnmatchedCount));
        }
    }
    public int Rs2dUnmatchedCount => _rs2dUnmatchedTriangles.Count;

    private void ApplyUnmatchedHighlight(bool highlight)
    {
        var unmatchedIds = new HashSet<int>(_rs2dUnmatchedTriangles.Select(u => u.ElementId));
        bool changed = false;
        foreach (var el in MeshElements)
        {
            if (el.IsUnmatchedTriangle && unmatchedIds.Contains(el.DisplayId))
            {
                if (el.IsHighlighted != highlight)
                {
                    el.IsHighlighted = highlight;
                    changed = true;
                }
            }
        }
        if (changed)
            OnPropertyChanged(nameof(MeshElements));
    }

    /// <summary>
    /// Compute mesh-quality + best-practice warnings. Triggered on toggle and after every RS2D mesh step.
    /// </summary>
    public void RefreshRs2dWarnings()
    {
        var list = new List<Models.Rs2dWarning>();
        if (Rs2dShowBestPracticeWarnings)
        {
            if (int.TryParse(Rs2dExcavationNodeCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nExc) && nExc > 0 && nExc < 24)
                list.Add(new Models.Rs2dWarning("Warning",
                    $"Too few nodes ({nExc}) around excavation: stresses near opening may be under-resolved.",
                    "Increase 'Default Number of Nodes on All Excavations' to >= 24, or set per-excavation count."));

            if (double.TryParse(Rs2dGradationFactor, NumberStyles.Float, CultureInfo.InvariantCulture, out var gf) && gf > 1.6)
                list.Add(new Models.Rs2dWarning("Warning",
                    $"Large gradation factor ({gf:F2}): element-size jumps near boundary may degrade convergence.",
                    "Reduce Gradation Factor to 1.2–1.4 for production runs."));

            if (MapRs2dElementTypeToKind(Rs2dElementType) is "Q4" or "Q8" && MeshQualityMaxAspectRatio > 5.0)
                list.Add(new Models.Rs2dWarning("Warning",
                    $"Poor element shape detected (whole-mesh max aspect ratio {MeshQualityMaxAspectRatio:F1}).",
                    "Tighten the Quality slider toward 'Prefer higher quality', reduce gradation factor, or switch to T6."));

            if (MapRs2dElementTypeToKind(Rs2dElementType) is "Q4" or "Q8" && MeshQualityMinAngle > 0 && MeshQualityMinAngle < 10.0)
                list.Add(new Models.Rs2dWarning("Warning",
                    $"Very small interior angle in mesh ({MeshQualityMinAngle:F1}°).",
                    "Reduce gradation factor to 1.2–1.4, increase near-boundary size, or use T6."));

            if (MapRs2dElementTypeToKind(Rs2dElementType) is "Q4" or "Q8" && MeshQualityMaxSkew > 0.7)
                list.Add(new Models.Rs2dWarning("Warning",
                    $"High quad skew detected (max skew {MeshQualityMaxSkew:F2}).",
                    "Move the Quality slider toward 'Prefer higher quality' or switch to T6 in transition zones."));

            if (MapRs2dElementTypeToKind(Rs2dElementType) == "Q4" && Rs2dUnmatchedCount > 0)
                list.Add(new Models.Rs2dWarning("Warning",
                    $"{Rs2dUnmatchedCount} triangle fallback element(s) remain after quad conversion (Kind=T3 in the Mesh elements table).",
                    "Relax the Quality slider toward 'Prefer more quads', or reduce near-boundary size and re-mesh."));

            if (GeometryDefinitionVM != null && GeometryDefinitionVM.GeometryVertexRows.Count >= 3)
            {
                double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
                double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
                foreach (var v in GeometryDefinitionVM.GeometryVertexRows)
                {
                    if (v.AxisX < minX) minX = v.AxisX;
                    if (v.AxisX > maxX) maxX = v.AxisX;
                    if (v.AxisY < minY) minY = v.AxisY;
                    if (v.AxisY > maxY) maxY = v.AxisY;
                }
                double width = maxX - minX, height = maxY - minY;
                if (width > 0 && height > 0 && Math.Max(width, height) / Math.Min(width, height) > 6.0)
                    list.Add(new Models.Rs2dWarning("Info",
                        "Outer boundary aspect ratio is high; verification-style problems usually need a far field >= 5 tunnel diameters.",
                        "Increase the outer boundary radius or symmetric extent."));
            }

            if (_lastJacobianResult is { IsValid: false } jac)
                list.Add(new Models.Rs2dWarning("Warning",
                    jac.Message,
                    "Refine the mesh near distorted elements or switch to T6 in transition zones."));

            if (Rs2dStrictWarningBlockers && list.Any(w => w.Severity is "Warning" or "Error"))
                list.Add(new Models.Rs2dWarning("Error",
                    "Mesh quality blockers are active — resolve warnings before export.",
                    "Disable 'Treat best-practice errors as blockers' for exploratory runs only."));

            var kind = MapRs2dElementTypeToKind(Rs2dElementType);
            if (kind == "T6" && _materialPropertiesVM?.SelectedFailureCriterion == FailureCriterion.GeneralizedHoekBrown)
                list.Add(new Models.Rs2dWarning("Info",
                    "T6 with Generalized Hoek-Brown uses corner-only plastic flow at Gauss points.",
                    "Verify results against T3 or Mohr-Coulomb benchmarks."));
        }

        _rs2dWarnings = list;
        OnPropertyChanged(nameof(Rs2dWarningsReadonly));
        OnPropertyChanged(nameof(Rs2dWarningCount));
    }

    public int Rs2dWarningCount => _rs2dWarnings.Count;

    public void RefreshSolverReadiness()
    {
        if (!Rs2dShowSolverReadiness) return;
        var kind = MapRs2dElementTypeToKind(Rs2dElementType);
        var failure = _materialPropertiesVM?.SelectedFailureCriterion ?? FailureCriterion.Elastic;
        var matType = _materialPropertiesVM?.SelectedMaterialType ?? MaterialType.Isotropic;
        var exe = Backend.P610AnalysisOptionsHelper.ResolveExecutableName(failure, matType, kind);

        string severity = "Ready";
        string message = $"Mesh: {kind} | Failure: {failure} | Material: {matType} | Exe: {exe}";

        // Approximate cases
        if (matType is MaterialType.NonlinearIsotropic or MaterialType.DuncanChangHyperbolic or MaterialType.ViscoElastic)
        {
            severity = "Approximate";
            message += " — nonlinear material runs use simplified isotropic stress-update; verify against material curves.";
        }

        if (_lastJacobianResult is { IsValid: false } jacBad)
        {
            severity = "Blocked";
            message += $" | Jacobian: {jacBad.Message}";
        }
        else if (_lastJacobianResult is { IsValid: true } jacOk)
        {
            message += $" | Jacobian OK (min det J = {jacOk.MinJacobian:E2})";
        }

        if (MeshQualityMinAngle > 0 && MeshQualityMinAngle < 10.0)
        {
            severity = severity == "Ready" ? "Approximate" : severity;
            message += $" | Min angle {MeshQualityMinAngle:F1}° (below 10° threshold)";
        }

        if (MeshQualityMaxAspectRatio > 5.0)
        {
            severity = severity == "Ready" ? "Approximate" : severity;
            message += $" | Max AR {MeshQualityMaxAspectRatio:F1} (above 5.0 threshold)";
        }

        Rs2dSolverReadinessSeverity = severity;
        Rs2dSolverReadinessStatus = message;
    }

    /// <summary>Runs the same boundary discretization as the Unstructured Mesh "Discretize" button
    /// and returns the resulting status text (for the Structured Mesh subtab button).</summary>
    public string RunRs2dDiscretize()
    {
        ExecuteRs2dDiscretize();
        return SurfaceMeshStatus;
    }

    private void ExecuteRs2dDiscretize()
    {
        if (GeometryDefinitionVM == null)
        {
            SurfaceMeshStatus = "Unstructured Mesh: geometry is not linked.";
            Rs2dDiscretized = false;
            ShowBoundaryDiscretizationMarkers = false;
            return;
        }

        RefreshSurfaceMeshRows();
        ApplyTargetElementCountToSizesIfSet();

        var pts = CollectRs2dDiscretizedBoundaryPoints(GeometryDefinitionVM, out var err);
        if (!string.IsNullOrEmpty(err) || pts.Count == 0)
        {
            SurfaceMeshStatus = string.IsNullOrEmpty(err)
                ? "Unstructured Mesh: no boundary points produced — check geometry."
                : $"Unstructured Mesh: {err}";
            Rs2dDiscretized = false;
            ShowBoundaryDiscretizationMarkers = false;
            _rs2dDiscretizationPoints.Clear();
            _rs2dBoundarySegments.Clear();
            return;
        }

        _rs2dDiscretizationPoints = new List<Point>(pts);

        // Bulk-update: avoid per-node canvas refresh (MeshTabCanvasWrapper reprojects on every CollectionChanged).
        RecordUndo("RS2D discretize boundary");
        using (BeginMeshCanvasBulkUpdate())
        {
            MeshElements.Clear();
            MeshInformationRows.Clear();
            BoundaryEdgeNodes.Clear();
            DiscretizationNodes.Clear();
            // Boundary preview: red crosses only; hide element/node ID overlays (may stay on from prior mesh or UI).
            ShowMeshElements = false;
            ShowMeshNodes = false;
            SelectedMeshInformationRow = null;
            foreach (var p in pts)
                DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { Location = p });
        }

        IsDiscretized = true;
        Rs2dDiscretized = true;
        ShowBoundaryDiscretizationMarkers = true;
        int layerCuts = GeometryDefinitionVM.MaterialLayerCutElevations.Count;
        SurfaceMeshStatus = layerCuts > 0
            ? $"Unstructured Mesh: discretized model boundary + {layerCuts} material layer interface(s) — {pts.Count} constraint nodes. Click Mesh to generate the finite element mesh."
            : $"Unstructured Mesh: discretized model boundaries — {pts.Count} boundary nodes. Click Mesh to generate the finite element mesh.";
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// RS2D Element Type combo: detect quadratic triangles. Handles Unicode hyphens, extra spaces, and "6 noded" wording.
    /// </summary>
    internal static bool IsRs2dSixNodeTriangleElementType(string? elementType)
    {
        if (string.IsNullOrWhiteSpace(elementType)) return false;
        var t = NormalizeElementTypeText(elementType);
        var lower = t.ToLowerInvariant();
        if (lower.Contains("quad", StringComparison.Ordinal)) return false;
        if (!lower.Contains("triang", StringComparison.Ordinal)) return false;
        return Regex.IsMatch(lower, @"^6[-\s]*(?:noded|node)\b");
    }

    private static string NormalizeElementTypeText(string elementType)
    {
        var t = (elementType ?? "").Trim();
        foreach (char c in "\u2010\u2011\u2012\u2013\u2014\u2212")
            t = t.Replace(c, '-');
        t = Regex.Replace(t, @"\s+", " ");
        return t;
    }

    /// <summary>Map RS2D element-type combo text to a canonical kind: "T3" | "T6" | "Q4" | "Q8".</summary>
    public static string MapRs2dElementTypeToKind(string? elementType)
    {
        if (string.IsNullOrWhiteSpace(elementType)) return "T3";
        var lower = NormalizeElementTypeText(elementType).ToLowerInvariant();
        bool isQuad = lower.Contains("quad");
        bool isTri  = lower.Contains("triang");
        if (isQuad)
            return Regex.IsMatch(lower, @"^8[-\s]*(?:noded|node)\b") ? "Q8" : "Q4";
        if (isTri)
            return Regex.IsMatch(lower, @"^6[-\s]*(?:noded|node)\b") ? "T6" : "T3";
        return "T3";
    }

    private void ExecuteRs2dMesh()
    {
        // Workspace / ribbon Generate Mesh is always enabled; auto-discretize so one click
        // matches the scripting API (FeProjectApi.GenerateMesh) instead of a silent no-op.
        if (!Rs2dDiscretized)
        {
            ExecuteRs2dDiscretize();
            if (!Rs2dDiscretized)
                return;
        }

        // Closed outline + material layers can exist without a locked surface. Surface meshing
        // requires at least one WillBeMeshed row — ensure S1 from the outer vertex ring.
        EnsureMeshableBoundarySurface();

        ApplyTargetElementCountToSizesIfSet();

        var prevGrowth     = BoundaryGrowthFactor;
        var prevGradation  = BoundaryGradationEnabled;
        var prevLayers     = BoundaryLayerCount;
        var prevNearSize   = NearBoundaryMeshSize;

        var kind = MapRs2dElementTypeToKind(Rs2dElementType);
        bool rs2dSixNode = string.Equals(kind, "T6", StringComparison.Ordinal);
        bool wantsQuad = kind is "Q4" or "Q8";
        bool wantsQ8 = kind == "Q8";
        var prevMeshElementOrder = MeshElementOrder;

        if (double.TryParse(Rs2dGradationFactor, NumberStyles.Float, CultureInfo.InvariantCulture, out var gf) && gf > 1.05)
            BoundaryGrowthFactor = gf.ToString(CultureInfo.InvariantCulture);

        switch (Rs2dMeshType)
        {
            case "Uniform":
                BoundaryGradationEnabled = false;
                break;
            case "No Internal Nodes":
                BoundaryGradationEnabled = true;
                BoundaryLayerCount = "0";
                break;
            default:
                BoundaryGradationEnabled = true;
                break;
        }

        if (int.TryParse(Rs2dExcavationNodeCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nExc) &&
            nExc > 0 && BoundaryGradationEnabled)
        {
            var layers = Math.Clamp(nExc / 25, 1, 12);
            BoundaryLayerCount = layers.ToString(CultureInfo.InvariantCulture);
        }

        try
        {
            if (wantsQuad)
            {
                GeneratePureQuadPopupMesh(wantsQ8);
            }
            else
            {
                MeshElementOrder = rs2dSixNode ? "6-node" : "3-node";
                ExecuteGenerateSurfaceMesh(rs2dRequestSixNodeTriangle: rs2dSixNode);
            }

            if (Rs2dUnmatchedCount > 0)
                Rs2dHighlightUnmatched = true;

            if (MeshInformationRows.Count > 0)
            {
                ShowMeshElements = true;
                ShowBoundaryDiscretizationMarkers = false;
            }

            GeometryDefinitionVM?.StagedConstruction?.RederiveSurfaceExcavationElementIds();
            NotifyMeshCanvasDataChanged();
        }
        finally
        {
            MeshElementOrder = prevMeshElementOrder;
            BoundaryGrowthFactor     = prevGrowth;
            BoundaryGradationEnabled = prevGradation;
            BoundaryLayerCount       = prevLayers;
            NearBoundaryMeshSize     = prevNearSize;
        }

        RefreshRs2dWarnings();
        RefreshSolverReadiness();
    }

    /// <summary>
    /// When the model has a closed outer ring but no locked surface yet, lock S1 to the
    /// boundary vertices so Generate Mesh can run (same as import path).
    /// </summary>
    private void EnsureMeshableBoundarySurface()
    {
        if (GeometryDefinitionVM == null)
            return;

        RefreshSurfaceMeshRows();
        if (SurfaceMeshRows.Any(r => r.WillBeMeshed))
            return;

        bool canLock = GeometryDefinitionVM.HasClosedSoilPolygon
            || (GeometryDefinitionVM.TryGetClosedBoundaryAxisPolygon(out var pts) && pts.Count >= 3)
            || GeometryDefinitionVM.GeometryVertexRows.Count >= 3;
        if (!canLock)
            return;

        GeometryDefinitionVM.LockImportedBoundaryAsSurfaceS1Public();
        RefreshSurfaceMeshRows();
    }

    private string GeneratePureQuadPopupMesh(bool useQ8)
    {
        if (GeometryDefinitionVM == null)
            return SurfaceMeshStatus = "Error: no geometry loaded.";

        RefreshSurfaceMeshRows();

        if (!SurfaceMeshRows.Any(r => r.WillBeMeshed))
            return SurfaceMeshStatus = "Error: no surfaces to mesh (assign a material to filled regions).";

        if (!double.TryParse(GlobalMeshSize, NumberStyles.Any, CultureInfo.InvariantCulture, out var globalSize) || globalSize <= 0)
            return SurfaceMeshStatus = "Error: invalid Global Mesh Size value.";

        double nearSize = globalSize * 0.2;
        if (BoundaryGradationEnabled
            && double.TryParse(NearBoundaryMeshSize, NumberStyles.Any, CultureInfo.InvariantCulture, out var ns)
            && ns > 0)
        {
            nearSize = ns;
        }

        double growth = 1.3;
        if (BoundaryGradationEnabled
            && double.TryParse(BoundaryGrowthFactor, NumberStyles.Any, CultureInfo.InvariantCulture, out var gf)
            && gf > 1)
        {
            growth = gf;
        }

        var activeSurfaces = SurfaceMeshRows.Where(r => r.WillBeMeshed).ToList();
        var regionParams = new List<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize, double GlobalSize, int Layers, double Growth)>();
        var meshPolygons = new List<IReadOnlyList<Point>>();
        foreach (var row in activeSurfaces)
        {
            if (!GeometryDefinitionVM.TryGetSurfaceAxisPolygon(row.SurfaceNumber, out var polygon) || polygon.Count < 3)
                continue;
            regionParams.Add((row.SurfaceNumber, polygon, nearSize, globalSize, 0, growth));
            meshPolygons.Add(polygon);
        }

        if (regionParams.Count == 0)
            return SurfaceMeshStatus = "Error: no valid surface polygons found.";

        Q8MeshMaterialLayerPlanner.LayerPlan? layerPlan = null;
        if (GeometryDefinitionVM.MaterialLayerCutElevations.Count > 0)
        {
            if (!Q8MeshMaterialLayerPlanner.TryCreatePlan(
                    GeometryDefinitionVM, _materialPropertiesVM, out layerPlan, out var layerErr))
            {
                return SurfaceMeshStatus = $"Error: {layerErr}";
            }
        }
        _activeLayerPlan = layerPlan;

        var layerSegments = GeometryDefinitionVM.CollectMaterialLayerBoundarySegmentsForMeshes(meshPolygons)
            .Select(s => (s.A, s.B))
            .ToList();

        if (!TryPrepareNonHorizontalLayerConstraints(meshPolygons, out var inclinedSegments, out var nhErr))
            return SurfaceMeshStatus = $"Error: {nhErr}";

        var jointSegments = CollectJointConstraintSegments(meshPolygons, nearSize);
        IReadOnlyList<(Point A, Point B)> constraintSegments = layerSegments;
        if (inclinedSegments.Count > 0 || jointSegments.Count > 0)
        {
            var combined = new List<(Point A, Point B)>(layerSegments.Count + inclinedSegments.Count + jointSegments.Count);
            combined.AddRange(layerSegments);
            combined.AddRange(inclinedSegments);
            combined.AddRange(jointSegments);
            constraintSegments = combined;
        }

        List<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize)>? holeParams = null;
        var holePolys = new List<IReadOnlyList<Point>>();
        foreach (var row in SurfaceMeshRows.Where(r => r.ExcavatedAsOpenVoid))
        {
            if (!GeometryDefinitionVM.TryGetSurfaceAxisPolygon(row.SurfaceNumber, out var holePoly) || holePoly.Count < 3)
                continue;
            holeParams ??= new List<(int, IReadOnlyList<Point>, double)>();
            holeParams.Add((row.SurfaceNumber, holePoly, nearSize));
            holePolys.Add(holePoly);
        }

        var holesOrEmpty = holeParams is { } holes
            ? holes
            : new List<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize)>();

        if (useQ8)
        {
            var nativeQ8 = Backend.NativeQ8PopupMesher.Generate(
                new Backend.NativeQ8PopupMesher.Request
                {
                    Regions = regionParams,
                    HoleOnlyRegions = holesOrEmpty,
                    HolePolygons = holePolys,
                    MaterialLayerSegments = constraintSegments,
                    NonHorizontalPlan = _activeNonHorizontalPlan,
                    GlobalSize = globalSize,
                    NearSize = nearSize,
                });

            if (!nativeQ8.Success)
                return SurfaceMeshStatus = nativeQ8.ErrorMessage;

            Rs2dUnmatchedTriangles = new List<Models.UnmatchedTriangleInfo>();
            Rs2dHighlightUnmatched = false;

            string q8Status = PopulateFromNativeQ8Result(
                nativeQ8.Mesh,
                layerPlan,
                "Native Q8 popup mesh");

            if (!string.IsNullOrWhiteSpace(nativeQ8.Diagnostics))
                q8Status = SurfaceMeshStatus = q8Status + "  " + nativeQ8.Diagnostics;

            return q8Status;
        }

        var pureQuad = Backend.PureQuadPopupMesher.Generate(
            new Backend.PureQuadPopupMesher.Request
            {
                Regions = regionParams,
                HoleOnlyRegions = holesOrEmpty,
                HolePolygons = holePolys,
                MaterialLayerSegments = constraintSegments,
                NonHorizontalPlan = _activeNonHorizontalPlan,
                GlobalSize = globalSize,
                NearSize = nearSize,
            });

        if (!pureQuad.Success)
            return SurfaceMeshStatus = pureQuad.ErrorMessage;

        Rs2dUnmatchedTriangles = new List<Models.UnmatchedTriangleInfo>();
        Rs2dHighlightUnmatched = false;

        string status = PopulateFromHybridResult(
            pureQuad.Mesh,
            layerPlan,
            "Pure Q4 popup mesh",
            useQ8: false);

        if (!string.IsNullOrWhiteSpace(pureQuad.Diagnostics))
            status = SurfaceMeshStatus = status + "  " + pureQuad.Diagnostics;

        return status;
    }


    private string PopulateFromNativeQ8Result(
        Backend.NativeQ8PopupMesher.NativeQ8MeshResult result,
        Q8MeshMaterialLayerPlanner.LayerPlan? layerPlan,
        string meshLabel = "Native Q8 mesh")
    {
        RecordUndo($"Generate {meshLabel.ToLowerInvariant()}");
        SelectedMeshInformationRow = null;
        DiscretizationNodes.Clear();
        MeshElements.Clear();
        MeshInformationRows.Clear();
        IsDiscretized = false;
        ShowBoundaryDiscretizationMarkers = false;
        SurfaceMeshNodeCount = 0;
        SurfaceMeshElementCount = 0;
        MeshQualityMinAngle = 0;
        MeshQualityMaxAspectRatio = 0;
        MeshQualityMaxSkew = 0;
        MeshQualityMinJacobian = 0;
        _lastJacobianResult = null;

        var nodes = result.Nodes as List<Point> ?? new List<Point>(result.Nodes);
        int quadCount = 0;

        using (BeginMeshCanvasBulkUpdate())
        {
            foreach (var pt in nodes)
                DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { Location = pt });

            int globalElemId = 1;
            foreach (var region in result.Regions)
            {
                int regionFallbackEtype = region.SurfaceNumber < 1 ? 1 : region.SurfaceNumber;
                foreach (var (a, b, c, d, e5, e6, e7, e8) in region.Elements)
                {
                    if (!IsValidZeroBasedConnectivity(nodes.Count, a, b, c, d, e5, e6, e7, e8))
                    {
                        AgentDebugRuntimeLog.Write("post-fix", "Q8POP", "MeshViewModel.cs:PopulateFromNativeQ8Result",
                            "skipped invalid native popup Q8 tuple during mesh population",
                            new
                            {
                                meshLabel,
                                region.SurfaceNumber,
                                nodes = new[] { a, b, c, d, e5, e6, e7, e8 },
                                sharedNodeCount = nodes.Count
                            });
                        continue;
                    }

                    var p1 = nodes[a]; var p2 = nodes[b]; var p3 = nodes[c]; var p4 = nodes[d];
                    var p5 = nodes[e5]; var p6 = nodes[e6]; var p7 = nodes[e7]; var p8 = nodes[e8];
                    double cx = (p1.X + p2.X + p3.X + p4.X) * 0.25;
                    double cy = (p1.Y + p2.Y + p3.Y + p4.Y) * 0.25;
                    var outline = Q8MeshDisplayHelper.BuildSerendipityOutline(
                        p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, p4.X, p4.Y,
                        p5.X, p5.Y, p6.X, p6.Y, p7.X, p7.Y, p8.X, p8.Y);
                    EnsureCounterClockwisePolygon(outline);
                    MeshElements.Add(new MeshElementDisplay
                    {
                        Nodes = outline,
                        CenterX = cx,
                        CenterY = cy,
                        DisplayId = globalElemId,
                        IsActive = true,
                        IsQuadrilateral = true,
                        IsQuadraticQuad = true,
                        Q8SmithEmb2dLocalOrder = false,
                        PreserveQ8MidEdgeOutline = true,
                    });
                    MeshInformationRows.Add(new MeshInformationRow
                    {
                        Kind = "Q8",
                        ElementId = globalElemId,
                        Node1 = a + 1, Node2 = b + 1, Node3 = c + 1, Node4 = d + 1,
                        Node5 = e5 + 1, Node6 = e6 + 1, Node7 = e7 + 1, Node8 = e8 + 1,
                        Etype = ResolveElementEtype(cx, cy, regionFallbackEtype),
                        N1X = p1.X, N1Y = p1.Y, N2X = p2.X, N2Y = p2.Y,
                        N3X = p3.X, N3Y = p3.Y, N4X = p4.X, N4Y = p4.Y,
                        N5X = p5.X, N5Y = p5.Y, N6X = p6.X, N6Y = p6.Y,
                        N7X = p7.X, N7Y = p7.Y, N8X = p8.X, N8Y = p8.Y,
                        SmithEmb2dLocalOrder = false,
                        Status = "Active",
                    });
                    globalElemId++;
                    quadCount++;
                }
            }

            if (quadCount == 0)
                return SurfaceMeshStatus = "Error: native Q8 popup mesher produced no elements.";

            IsDiscretized = true;
            SurfaceMeshNodeCount = DiscretizationNodes.Count;
            SurfaceMeshElementCount = MeshInformationRows.Count;
            MeshQualityMinAngle = result.MinAngleDeg;
            MeshQualityMaxAspectRatio = result.MaxAspectRatio;
            MeshQualityMaxSkew = 0;
            _lastJacobianResult = ValidateMeshJacobianFromInformationRows(useT6: false);
            MeshQualityMinJacobian = _lastJacobianResult?.MinJacobian ?? 0;

            string gateNote = result.Quality.GatesPassed
                ? "  Quality gates: OK"
                : $"  \u26A0 Quality gates: {result.Quality.ElementsBelowMinAngle} small-angle, " +
                  $"{result.Quality.ElementsAboveMaxAspect} high-AR, {result.Quality.InvertedElements} inverted";

            string pathNote = string.IsNullOrWhiteSpace(result.Notes) ? "" : $"  [{result.Notes}]";

            SurfaceMeshStatus =
                $"\u2713 {meshLabel}: {result.RegionCount} region(s), {result.StructuredRegionCount} structured.  " +
                $"{SurfaceMeshNodeCount} nodes, {SurfaceMeshElementCount} elements ({quadCount} quad / 0 tri).  Quads: Q8 (8-node)  " +
                $"Min angle: {MeshQualityMinAngle:F1}\u00B0  Max AR: {MeshQualityMaxAspectRatio:F2}  " +
                $"Min scaled-J: {result.Quality.MinScaledJacobian:F3}.{gateNote}{pathNote}" +
                (layerPlan?.HasLayers == true ? $"  Material layers: {layerPlan.LayerCount} band(s)." : "");
            AppendLayerBoundaryValidationToStatus();
        }

        ApplyJointInterfacesAfterPopulate();
        GeometryDefinitionVM?.StagedConstruction?.RederiveSurfaceExcavationElementIds();
        return SurfaceMeshStatus;
    }


    /// <summary>
    /// Converts the just-generated T3 mesh in MeshElements/MeshInformationRows into Q4 (or Q8 by midside upgrade)
    /// using <see cref="Backend.Rs2dQuadConverter"/>. Triangles that cannot be paired into a quad of acceptable
    /// shape are kept as T3 fallbacks so mesh quality is never silently degraded.
    /// </summary>
    private void ConvertRs2dTrianglesToQuads(bool produceQ8)
    {
        var rows = MeshInformationRows.ToList();
        if (rows.Count == 0) return;

        var nodeIndex = new Dictionary<int, int>();
        var pts = new List<System.Windows.Point>();
        void RegisterNode(int id, double x, double y)
        {
            if (id <= 0 || nodeIndex.ContainsKey(id)) return;
            nodeIndex[id] = pts.Count;
            pts.Add(new System.Windows.Point(x, y));
        }
        foreach (var r in rows)
        {
            RegisterNode(r.Node1, r.N1X, r.N1Y);
            RegisterNode(r.Node2, r.N2X, r.N2Y);
            RegisterNode(r.Node3, r.N3X, r.N3Y);
        }

        var tris = new List<Backend.Rs2dQuadConverter.TriRef>(rows.Count);
        foreach (var r in rows)
        {
            tris.Add(new Backend.Rs2dQuadConverter.TriRef
            {
                Region = r.Etype,
                Etype = r.Etype < 1 ? 1 : r.Etype,
                A = nodeIndex[r.Node1],
                B = nodeIndex[r.Node2],
                C = nodeIndex[r.Node3],
            });
        }

        var quality = Rs2dQualityEnableSlider
            ? Backend.Rs2dQuadConverter.Quality.FromSlider(Rs2dQuadConversionSlider)
            : Backend.Rs2dQuadConverter.Quality.Default;
        var gradationPolicy = TryBuildRs2dGradationPolicy();
        var conversion = Backend.Rs2dQuadConverter.PairTrianglesToQuads(pts, tris, quality, gradationPolicy);

        SelectedMeshInformationRow = null;
        var nextDiscretizationNodes = new List<Models.MeshDiscretizationNodeDisplay>();
        var nextMeshElements = new List<Models.MeshElementDisplay>();
        var nextMeshInformationRows = new List<Models.MeshInformationRow>();

        var usedIndex = new HashSet<int>();
        foreach (var q in conversion.Quads)
        {
            usedIndex.Add(q.Q1); usedIndex.Add(q.Q2); usedIndex.Add(q.Q3); usedIndex.Add(q.Q4);
        }
        foreach (var u in conversion.Unmatched)
        {
            usedIndex.Add(u.A); usedIndex.Add(u.B); usedIndex.Add(u.C);
        }

        List<(int n1, int n2, int n3, int n4, int n5, int n6, int n7, int n8)>? q8 = null;
        if (produceQ8)
        {
            q8 = Backend.Rs2dQuadConverter.UpgradeQ4ToQ8(pts, conversion.Quads, _rs2dBoundarySegments);
            foreach (var rec in q8)
            {
                usedIndex.Add(rec.n5); usedIndex.Add(rec.n6); usedIndex.Add(rec.n7); usedIndex.Add(rec.n8);
            }
        }

        var idMap = new Dictionary<int, int>();
        int nextId = 1;
        for (int i = 0; i < pts.Count; i++)
            if (usedIndex.Contains(i))
                idMap[i] = nextId++;

        for (int i = 0; i < pts.Count; i++)
        {
            if (!usedIndex.Contains(i)) continue;
            nextDiscretizationNodes.Add(new Models.MeshDiscretizationNodeDisplay { Location = pts[i] });
        }

        int globalId = 1;
        if (produceQ8 && q8 != null)
        {
            foreach (var rec in q8)
            {
                var p1 = pts[rec.n1]; var p2 = pts[rec.n2]; var p3 = pts[rec.n3]; var p4 = pts[rec.n4];
                var m12 = pts[rec.n5]; var m23 = pts[rec.n6]; var m34 = pts[rec.n7]; var m41 = pts[rec.n8];
                double cx = (p1.X + p2.X + p3.X + p4.X) * 0.25;
                double cy = (p1.Y + p2.Y + p3.Y + p4.Y) * 0.25;
                var outline = new System.Windows.Media.PointCollection { p1, p2, p3, p4 };
                EnsureCounterClockwisePolygon(outline);
                nextMeshElements.Add(new Models.MeshElementDisplay
                {
                    Nodes = outline, CenterX = cx, CenterY = cy,
                    DisplayId = globalId, IsActive = true,
                    IsQuadrilateral = true, IsQuadraticQuad = true,
                    Q8SmithEmb2dLocalOrder = false,
                });
                nextMeshInformationRows.Add(new Models.MeshInformationRow
                {
                    SmithEmb2dLocalOrder = false,
                    ElementId = globalId,
                    Node1 = idMap[rec.n1], Node2 = idMap[rec.n2], Node3 = idMap[rec.n3], Node4 = idMap[rec.n4],
                    Node5 = idMap[rec.n5], Node6 = idMap[rec.n6], Node7 = idMap[rec.n7], Node8 = idMap[rec.n8],
                    Etype = ResolveElementEtype(cx, cy, 1),
                    N1X = p1.X, N1Y = p1.Y, N2X = p2.X, N2Y = p2.Y,
                    N3X = p3.X, N3Y = p3.Y, N4X = p4.X, N4Y = p4.Y,
                    N5X = m12.X, N5Y = m12.Y, N6X = m23.X, N6Y = m23.Y,
                    N7X = m34.X, N7Y = m34.Y, N8X = m41.X, N8Y = m41.Y,
                    Kind = "Q8",
                    Status = "Active",
                });
                globalId++;
            }
        }
        else
        {
            foreach (var q in conversion.Quads)
            {
                var p1 = pts[q.Q1]; var p2 = pts[q.Q2]; var p3 = pts[q.Q3]; var p4 = pts[q.Q4];
                double cx = (p1.X + p2.X + p3.X + p4.X) * 0.25;
                double cy = (p1.Y + p2.Y + p3.Y + p4.Y) * 0.25;
                var outline = new System.Windows.Media.PointCollection { p1, p2, p3, p4 };
                EnsureCounterClockwisePolygon(outline);
                nextMeshElements.Add(new Models.MeshElementDisplay
                {
                    Nodes = outline, CenterX = cx, CenterY = cy,
                    DisplayId = globalId, IsActive = true,
                    IsQuadrilateral = true,
                });
                nextMeshInformationRows.Add(new Models.MeshInformationRow
                {
                    SmithEmb2dLocalOrder = false,
                    ElementId = globalId,
                    Node1 = idMap[q.Q1], Node2 = idMap[q.Q2], Node3 = idMap[q.Q3], Node4 = idMap[q.Q4],
                    Etype = ResolveElementEtype(cx, cy, q.Etype < 1 ? 1 : q.Etype),
                    N1X = p1.X, N1Y = p1.Y, N2X = p2.X, N2Y = p2.Y,
                    N3X = p3.X, N3Y = p3.Y, N4X = p4.X, N4Y = p4.Y,
                    Kind = "Q4",
                    Status = "Active",
                });
                globalId++;
            }
        }

        var unmatchedRecords = new List<Models.UnmatchedTriangleInfo>();
        foreach (var u in conversion.Unmatched)
        {
            var pa = pts[u.A]; var pb = pts[u.B]; var pc = pts[u.C];
            double cx = (pa.X + pb.X + pc.X) / 3.0;
            double cy = (pa.Y + pb.Y + pc.Y) / 3.0;
            var outline = new System.Windows.Media.PointCollection { pa, pb, pc };
            EnsureCounterClockwisePolygon(outline);
            nextMeshElements.Add(new Models.MeshElementDisplay
            {
                Nodes = outline, CenterX = cx, CenterY = cy,
                DisplayId = globalId, IsActive = true,
                IsUnmatchedTriangle = true,
                IsHighlighted = Rs2dHighlightUnmatched,
            });
            nextMeshInformationRows.Add(new Models.MeshInformationRow
            {
                ElementId = globalId,
                Node1 = idMap[u.A], Node2 = idMap[u.B], Node3 = idMap[u.C],
                Etype = u.Etype < 1 ? 1 : u.Etype,
                N1X = pa.X, N1Y = pa.Y, N2X = pb.X, N2Y = pb.Y, N3X = pc.X, N3Y = pc.Y,
                Kind = "T3",
                Status = "Active",
            });
            unmatchedRecords.Add(new Models.UnmatchedTriangleInfo { ElementId = globalId, Reason = u.Reason });
            globalId++;
        }

        Rs2dUnmatchedTriangles = unmatchedRecords;
        DiscretizationNodes.ReplaceAll(nextDiscretizationNodes);
        MeshElements.ReplaceAll(nextMeshElements);
        MeshInformationRows.ReplaceAll(nextMeshInformationRows);

        IsDiscretized = true;
        SurfaceMeshNodeCount = DiscretizationNodes.Count;
        SurfaceMeshElementCount = MeshInformationRows.Count;
        MeshQualityMinAngle = conversion.OverallMinAngleDeg;
        MeshQualityMaxAspectRatio = conversion.OverallMaxAspectRatio;
        MeshQualityMaxSkew = conversion.OverallMaxSkew;
        _lastJacobianResult = Backend.MeshJacobianValidator.Validate(pts, conversion.Quads, conversion.Unmatched, q8);
        MeshQualityMinJacobian = _lastJacobianResult.MinJacobian;

        string label = produceQ8 ? "Q8" : "Q4";
        string transitionNote = conversion.TransitionZoneTriangles > 0
            ? $", {conversion.TransitionZoneTriangles} transition-zone T3"
            : "";
        SurfaceMeshStatus =
            $"✓ {label} mesh: {conversion.QuadsOut} quad(s), {conversion.TrianglesOut} triangle fallback(s){transitionNote}, " +
            $"{DiscretizationNodes.Count} nodes. Whole mesh — min angle: {conversion.OverallMinAngleDeg:F1}°  max AR: {conversion.OverallMaxAspectRatio:F2}  max skew: {conversion.OverallMaxSkew:F2}";
        AppendLayerBoundaryValidationToStatus();
    }

    private Backend.Rs2dQuadConverter.GradationPolicy? TryBuildRs2dGradationPolicy()
    {
        IReadOnlyList<double>? layerCutYs = null;
        if (_activeLayerPlan?.HasLayers == true && _activeLayerPlan.BandBreaks.Count >= 2)
            layerCutYs = _activeLayerPlan.BandBreaks;

        double.TryParse(NearBoundaryMeshSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var near);
        double.TryParse(GlobalMeshSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var global);
        if (near <= 0 || global <= 0)
        {
            if (layerCutYs == null) return null;
            return new Backend.Rs2dQuadConverter.GradationPolicy
            {
                NearSize = 1.0,
                GlobalSize = 1.0,
                LayerCutYs = layerCutYs,
            };
        }

        double gf = 1.3;
        if (double.TryParse(Rs2dGradationFactor, NumberStyles.Float, CultureInfo.InvariantCulture, out var gfp) && gfp > 0)
            gf = gfp;
        return new Backend.Rs2dQuadConverter.GradationPolicy
        {
            NearSize = near,
            GlobalSize = global,
            GrowthFactor = gf,
            BoundaryPoints = _rs2dDiscretizationPoints.Count > 0 ? _rs2dDiscretizationPoints : null,
            LayerCutYs = layerCutYs,
        };
    }

    private int ResolveElementEtype(double centroidX, double centroidY, int fallbackEtype)
    {
        if (_activeNonHorizontalPlan?.HasZones == true)
            return _activeNonHorizontalPlan.ResolveEtype(centroidX, centroidY);
        if (_activeLayerPlan?.HasLayers == true)
            return _activeLayerPlan.ResolveEtype(centroidY);
        return fallbackEtype < 1 ? 1 : fallbackEtype;
    }

    private void AppendLayerBoundaryValidationToStatus()
    {
        if (_activeLayerPlan?.HasLayers != true || MeshInformationRows.Count == 0)
            return;

        var check = Q8MeshLayerValidator.ValidateMeshInformationRows(MeshInformationRows, _activeLayerPlan);
        if (check.IsValid)
            return;

        SurfaceMeshStatus += $"  Warning: {check.Message}";
        if (check.CrossingElementIds.Count > 0)
            Rs2dHighlightUnmatched = true;
    }

    private void ExecuteRs2dOk()
    {
        if (double.TryParse(Rs2dGradationFactor, NumberStyles.Float, CultureInfo.InvariantCulture, out var gf) && gf > 1.05)
            BoundaryGrowthFactor = gf.ToString(CultureInfo.InvariantCulture);
        SurfaceMeshStatus = "Unstructured Mesh settings applied (Gradation Factor → Surface Mesh boundary growth).";
    }

    private void ExecuteRs2dCancel()
    {
        Rs2dMeshType = "Graded";
        Rs2dElementType = "3-node triangle";
        Rs2dGradationFactor = "1.3";
        Rs2dExcavationNodeCount = "110";
        Rs2dAdvancedExpanded = false;
        Rs2dDiscretized = false;
        ShowBoundaryDiscretizationMarkers = false;
        if (MeshElements.Count == 0)
        {
            DiscretizationNodes.Clear();
            IsDiscretized = false;
        }
        SurfaceMeshStatus = "Unstructured Mesh inputs reset.";
    }

    // Surface Mesh subtab — professional RS2/Plaxis-style graded triangle mesh
    // ══════════════════════════════════════════════════════════════════════════

    // ── Global density preset ─────────────────────────────────────────────────
    private string _globalDensityPreset = "Medium";
    private string _globalMeshSize      = "0.5";
    private bool _suppressDensityUserDefinedSwitch;

    public IReadOnlyList<string> GlobalDensityPresets { get; } = new[]
        { "Very Coarse", "Coarse", "Medium", "Fine", "Very Fine", "User-defined" };

    public string GlobalDensityPreset
    {
        get => _globalDensityPreset;
        set
        {
            var normalized = NormalizeDensityPreset(value);
            if (string.Equals(_globalDensityPreset, normalized, StringComparison.Ordinal)) return;
            _globalDensityPreset = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUserDefinedDensity));
            if (!string.Equals(normalized, "User-defined", StringComparison.Ordinal))
                ApplyDomainRelativeDensityPreset(normalized);
        }
    }

    /// <summary>True when Mesh Density is User-defined (typed sizes / counts).</summary>
    public bool IsUserDefinedDensity =>
        string.Equals(_globalDensityPreset, "User-defined", StringComparison.Ordinal);

    /// <summary>
    /// Restore density preset + sizes from a .xfem file without running the preset combo's auto-fill logic,
    /// so saved User-defined (or legacy Custom) global / near-boundary sizes are preserved exactly.
    /// </summary>
    public void RestorePersistedGlobalDensity(string? preset, string? globalSize, string? nearBoundary)
    {
        _suppressDensityUserDefinedSwitch = true;
        try
        {
            if (!string.IsNullOrEmpty(preset))
            {
                var normalized = NormalizeDensityPreset(preset);
                if (!string.Equals(_globalDensityPreset, normalized, StringComparison.Ordinal))
                {
                    _globalDensityPreset = normalized;
                    OnPropertyChanged(nameof(GlobalDensityPreset));
                    OnPropertyChanged(nameof(IsUserDefinedDensity));
                }
            }
            if (!string.IsNullOrEmpty(globalSize))
                GlobalMeshSize = globalSize;
            if (!string.IsNullOrEmpty(nearBoundary))
                NearBoundaryMeshSize = nearBoundary;
        }
        finally
        {
            _suppressDensityUserDefinedSwitch = false;
        }
    }

    /// <summary>Maps legacy "Custom" label to User-defined.</summary>
    public static string NormalizeDensityPreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset)) return "Medium";
        if (string.Equals(preset, "Custom", StringComparison.OrdinalIgnoreCase))
            return "User-defined";
        return preset.Trim();
    }

    public string GlobalMeshSize
    {
        get => _globalMeshSize;
        set
        {
            if (string.Equals(_globalMeshSize, value, StringComparison.Ordinal)) return;
            _globalMeshSize = value ?? "";
            OnPropertyChanged();
            MarkDensityUserDefinedIfEditing();
        }
    }

    // ── Boundary gradation ────────────────────────────────────────────────────
    private bool   _boundaryGradationEnabled = true;
    private string _nearBoundaryMeshSize     = "0.1";
    private string _boundaryLayerCount       = "3";
    private string _boundaryGrowthFactor     = "1.3";

    public bool BoundaryGradationEnabled
    {
        get => _boundaryGradationEnabled;
        set { _boundaryGradationEnabled = value; OnPropertyChanged(); }
    }

    public string NearBoundaryMeshSize
    {
        get => _nearBoundaryMeshSize;
        set
        {
            if (string.Equals(_nearBoundaryMeshSize, value, StringComparison.Ordinal)) return;
            _nearBoundaryMeshSize = value ?? "";
            OnPropertyChanged();
            MarkDensityUserDefinedIfEditing();
        }
    }

    private void MarkDensityUserDefinedIfEditing()
    {
        if (_suppressDensityUserDefinedSwitch) return;
        if (string.Equals(_globalDensityPreset, "User-defined", StringComparison.Ordinal)) return;
        _globalDensityPreset = "User-defined";
        OnPropertyChanged(nameof(GlobalDensityPreset));
        OnPropertyChanged(nameof(IsUserDefinedDensity));
    }

    /// <summary>Characteristic length L = max(Δx, Δy) of the mesh domain bbox (fallback: project axes).</summary>
    private double GetMeshCharacteristicLength()
    {
        if (GeometryDefinitionVM != null)
        {
            var (minX, maxX, minY, maxY) = GeometryDefinitionVM.GetMeshBoundingBoxBounds();
            double L = Math.Max(maxX - minX, maxY - minY);
            if (L > 1e-12) return L;
        }

        var project = _projectSettings?.Project;
        if (project != null)
        {
            double L = Math.Max(
                Math.Abs(project.AxisXMax - project.AxisXMin),
                Math.Abs(project.AxisYMax - project.AxisYMin));
            if (L > 1e-12) return L;
        }

        // Keeps Medium ≈ 0.5 when no geometry is loaded (legacy absolute default).
        return 15.0;
    }

    private void ApplyDomainRelativeDensityPreset(string preset)
    {
        double L = GetMeshCharacteristicLength();
        double globalDiv = preset switch
        {
            "Very Coarse" => 8.0,
            "Coarse" => 15.0,
            "Medium" => 30.0,
            "Fine" => 60.0,
            "Very Fine" => 120.0,
            _ => 30.0
        };
        double nearDiv = preset switch
        {
            "Very Coarse" => 32.0,
            "Coarse" => 60.0,
            "Medium" => 120.0,
            "Fine" => 240.0,
            "Very Fine" => 480.0,
            _ => 120.0
        };

        double hGlobal = ClampMeshSize(L / globalDiv);
        double hNear = ClampMeshSize(L / nearDiv);
        if (hNear > hGlobal) hNear = hGlobal;

        _suppressDensityUserDefinedSwitch = true;
        try
        {
            GlobalMeshSize = FormatMeshSize(hGlobal);
            NearBoundaryMeshSize = FormatMeshSize(hNear);
        }
        finally
        {
            _suppressDensityUserDefinedSwitch = false;
        }
    }

    private static double ClampMeshSize(double h) => Math.Clamp(h, 1e-6, 1e6);

    private static string FormatMeshSize(double h)
    {
        if (h >= 100) return h.ToString("0.###", CultureInfo.InvariantCulture);
        if (h >= 1) return h.ToString("0.####", CultureInfo.InvariantCulture);
        return h.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private double EstimateMeshDomainArea()
    {
        if (GeometryDefinitionVM != null
            && GeometryDefinitionVM.TryGetTriangleMeshPolygonAxisPoints(out var poly)
            && poly.Count >= 3)
        {
            double sum = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var p0 = poly[i];
                var p1 = poly[(i + 1) % poly.Count];
                sum += p0.X * p1.Y - p1.X * p0.Y;
            }
            return Math.Abs(sum) * 0.5;
        }

        if (GeometryDefinitionVM != null)
        {
            var (minX, maxX, minY, maxY) = GeometryDefinitionVM.GetMeshBoundingBoxBounds();
            return Math.Max(0.0, (maxX - minX) * (maxY - minY));
        }

        return 0.0;
    }

    private double EstimateOuterPerimeter()
    {
        if (GeometryDefinitionVM == null) return 0.0;

        if (GeometryDefinitionVM.TryGetTriangleMeshPolygonAxisPoints(out var poly) && poly.Count >= 2)
        {
            double peri = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                peri += Math.Sqrt(dx * dx + dy * dy);
            }
            return peri;
        }

        var (minX, maxX, minY, maxY) = GeometryDefinitionVM.GetMeshBoundingBoxBounds();
        double w = Math.Max(0.0, maxX - minX);
        double h = Math.Max(0.0, maxY - minY);
        return 2.0 * (w + h);
    }

    /// <summary>
    /// When <see cref="Rs2dTargetElementCount"/> &gt; 0, set Global/Near sizes from domain area
    /// (triangles ≈ 2A/h² ⇒ h ≈ √(2A/N)).
    /// </summary>
    private void ApplyTargetElementCountToSizesIfSet()
    {
        if (!int.TryParse(Rs2dTargetElementCount?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ne)
            || ne <= 0)
            return;

        ne = Math.Clamp(ne, 1, 2_000_000);
        double area = EstimateMeshDomainArea();
        if (area <= 1e-18) return;

        double h = ClampMeshSize(Math.Sqrt(2.0 * area / ne));
        double gf = 1.3;
        if (double.TryParse(Rs2dGradationFactor, NumberStyles.Float, CultureInfo.InvariantCulture, out var gfp) && gfp > 0)
            gf = gfp;
        double hNear = gf <= 1.0 ? h * gf : h / gf;
        hNear = ClampMeshSize(Math.Min(hNear, h));

        _suppressDensityUserDefinedSwitch = true;
        try
        {
            GlobalMeshSize = FormatMeshSize(h);
            NearBoundaryMeshSize = FormatMeshSize(hNear);
            if (!IsUserDefinedDensity)
            {
                _globalDensityPreset = "User-defined";
                OnPropertyChanged(nameof(GlobalDensityPreset));
                OnPropertyChanged(nameof(IsUserDefinedDensity));
            }
        }
        finally
        {
            _suppressDensityUserDefinedSwitch = false;
        }
    }

    public string BoundaryLayerCount
    {
        get => _boundaryLayerCount;
        set { _boundaryLayerCount = value; OnPropertyChanged(); }
    }

    public string BoundaryGrowthFactor
    {
        get => _boundaryGrowthFactor;
        set { _boundaryGrowthFactor = value; OnPropertyChanged(); }
    }

    // ── Surface mesh rows (one per surface in geometry) ───────────────────────
    public ObservableCollection<SurfaceMeshRow> SurfaceMeshRows { get; } = new();

    private SurfaceMeshRow? _selectedSurfaceMeshRow;
    public SurfaceMeshRow? SelectedSurfaceMeshRow
    {
        get => _selectedSurfaceMeshRow;
        set { _selectedSurfaceMeshRow = value; OnPropertyChanged(); }
    }

    // ── Mesh quality statistics ───────────────────────────────────────────────
    private string _surfaceMeshStatus    = "No mesh generated.";
    private double _meshQualityMinAngle;
    private double _meshQualityMaxAR;
    private double _meshQualityMaxSkew;
    private double _meshQualityMinJacobian;
    private int    _surfaceMeshNodeCount;
    private int    _surfaceMeshElemCount;

    public string SurfaceMeshStatus
    {
        get => _surfaceMeshStatus;
        set { _surfaceMeshStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(SurfaceMeshStatusIsError)); }
    }

    public bool SurfaceMeshStatusIsError => _surfaceMeshStatus.StartsWith("Error", StringComparison.OrdinalIgnoreCase);

    public double MeshQualityMinAngle
    {
        get => _meshQualityMinAngle;
        set { _meshQualityMinAngle = value; OnPropertyChanged(); OnPropertyChanged(nameof(MeshQualityGrade)); }
    }

    public double MeshQualityMaxAspectRatio
    {
        get => _meshQualityMaxAR;
        set { _meshQualityMaxAR = value; OnPropertyChanged(); OnPropertyChanged(nameof(MeshQualityGrade)); }
    }

    public double MeshQualityMaxSkew
    {
        get => _meshQualityMaxSkew;
        set { _meshQualityMaxSkew = value; OnPropertyChanged(); }
    }

    public double MeshQualityMinJacobian
    {
        get => _meshQualityMinJacobian;
        set { _meshQualityMinJacobian = value; OnPropertyChanged(); }
    }

    public int SurfaceMeshNodeCount    { get => _surfaceMeshNodeCount; set { _surfaceMeshNodeCount = value; OnPropertyChanged(); } }
    public int SurfaceMeshElementCount { get => _surfaceMeshElemCount; set { _surfaceMeshElemCount = value; OnPropertyChanged(); } }

    /// <summary>4-node Goodman interfaces emitted after the last mesh populate (out of nels).</summary>
    public List<FeJointInterfaceElement> JointInterfaceElements { get; } = new();

    public string MeshQualityGrade =>
        _surfaceMeshElemCount == 0             ? "—" :
        _meshQualityMinAngle  >= 25            ? "Excellent" :
        _meshQualityMinAngle  >= 18            ? "Good" :
        _meshQualityMinAngle  >= 10            ? "Acceptable" :
                                                 "Poor — consider finer mesh";

    public string MeshQualityGradeColor =>
        _surfaceMeshElemCount == 0       ? "#FF6B7280" :
        _meshQualityMinAngle  >= 25      ? "#FF16A34A" :
        _meshQualityMinAngle  >= 18      ? "#FF2563EB" :
        _meshQualityMinAngle  >= 10      ? "#FFD97706" :
                                           "#FFDC2626";

    // ── Commands ──────────────────────────────────────────────────────────────
    public ICommand RefreshSurfaceListCommand     { get; }
    public ICommand GenerateSurfaceMeshCommand    { get; }
    public ICommand ResetSurfaceMeshDefaultsCommand { get; }

    // ── Constructor additions (called from MeshViewModel()) ───────────────────
    private void InitSurfaceMesh()
    {
        // commands are wired in constructor
    }

    // ── Refresh surface list from geometry ────────────────────────────────────
    public void RefreshSurfaceMeshRows()
    {
        if (GeometryDefinitionVM == null) { SurfaceMeshRows.Clear(); return; }

        var excavated = GeometryDefinitionVM.StagedConstruction?.ExcavatedSurfacesUpToActiveStage
                        ?? new System.Collections.Generic.HashSet<int>();

        // Preserve existing user settings (size override, exclusion) by surface number
        var existing = new Dictionary<int, SurfaceMeshRow>();
        foreach (var r in SurfaceMeshRows)
            existing[r.SurfaceNumber] = r;

        SurfaceMeshRows.Clear();
        foreach (var surface in GeometryDefinitionVM.GeometrySurfaces.OrderBy(s => s.SurfaceNumber))
        {
            string matName = "—";
            if (surface.AssignedMaterialId.HasValue)
            {
                // material lookup handled below via _materialPropertiesVM
                // Try to get material name from MaterialPropertiesVM (passed via reference)
                if (_materialPropertiesVM?.Materials.FirstOrDefault(m => m.Id == surface.AssignedMaterialId.Value) is { } m2)
                    matName = m2.Name;
            }

            var isExcav = excavated.Contains(surface.SurfaceNumber);
            existing.TryGetValue(surface.SurfaceNumber, out var prev);

            SurfaceMeshRows.Add(new SurfaceMeshRow
            {
                SurfaceNumber        = surface.SurfaceNumber,
                SurfaceName          = string.IsNullOrWhiteSpace(surface.Tag) ? $"S{surface.SurfaceNumber}" : surface.Tag,
                AssignedMaterialName = matName,
                HasAssignedMaterial  = surface.AssignedMaterialId.HasValue,
                IsExcavated          = isExcav,
                IsExcluded           = prev?.IsExcluded ?? false,
                MeshSizeOverride     = prev?.MeshSizeOverride ?? "",
                Refinement           = prev?.Refinement ?? MeshRefinementLevel.Default,
            });
        }
    }

    // ── Mesh generation ───────────────────────────────────────────────────────

    /// <summary>
    /// Native all-Q4 unstructured mesh via structured lattice clipped to surface polygons (no T3 intermediate).
    /// </summary>

    /// <param name="rs2dRequestSixNodeTriangle">
    /// When true (RS2D tab Mesh with Element Type = 6-node triangle), build T6 connectivity regardless of
    /// the Surface Mesh tab <see cref="MeshElementOrder"/> radio state.
    /// </param>
    private void ExecuteGenerateSurfaceMesh(bool rs2dRequestSixNodeTriangle = false)
    {
        if (GeometryDefinitionVM == null)
        {
            SurfaceMeshStatus = "Error: no geometry loaded.";
            return;
        }

        RefreshSurfaceMeshRows();

        if (!SurfaceMeshRows.Any(r => r.WillBeMeshed))
        {
            SurfaceMeshStatus = "Error: no surfaces to mesh (all excluded, or all excavated voids with no material). Assign a material to filled regions such as caverns.";
            return;
        }

        if (!double.TryParse(GlobalMeshSize, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var globalSize) || globalSize <= 0)
        {
            SurfaceMeshStatus = "Error: invalid Global Mesh Size value.";
            return;
        }

        double nearSize = globalSize * 0.2; // fallback
        if (BoundaryGradationEnabled &&
            double.TryParse(NearBoundaryMeshSize, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var ns) && ns > 0)
            nearSize = ns;

        int layers = 3;
        if (BoundaryGradationEnabled &&
            int.TryParse(BoundaryLayerCount, out var lc) && lc > 0)
            layers = lc;

        double growth = 1.3;
        if (BoundaryGradationEnabled &&
            double.TryParse(BoundaryGrowthFactor, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var gf) && gf > 1)
            growth = gf;

        // Preserve RS2D discretize constraint nodes (boundary + material layer interfaces) before clearing.
        var pinnedBoundaryNodes = CollectRs2dPinnedDiscretizationForMesh();

        // Clear existing mesh
        RecordUndo("Generate surface mesh");
        SelectedMeshInformationRow = null;
        DiscretizationNodes.Clear();
        MeshElements.Clear();
        MeshInformationRows.Clear();
        JointInterfaceElements.Clear();
        IsDiscretized = false;
        ShowBoundaryDiscretizationMarkers = false;
        SurfaceMeshNodeCount    = 0;
        SurfaceMeshElementCount = 0;
        MeshQualityMinAngle     = 0;
        MeshQualityMaxAspectRatio = 0;
        MeshQualityMaxSkew = 0;
        MeshQualityMinJacobian = 0;
        _lastJacobianResult = null;

        // Mesh every closed polygonal surface except excavated ones. Excavations are passed as voids so
        // parent regions are not filled inside the excavation boundary.
        var activeSurfaces = SurfaceMeshRows.Where(r => r.WillBeMeshed).ToList();

        // ── Build region descriptors for the conforming joint mesher ──────────
        // All regions are triangulated in a SINGLE Bowyer-Watson pass so the
        // interface boundary (e.g. arch cavern boundary shared with rockmass)
        // uses identical node positions → conforming mesh, no overlap, no gaps.
        var regions = new List<(int, IReadOnlyList<Point>, double, double, int, double)>();
        double minNearAcrossRegions = nearSize;
        foreach (var row in activeSurfaces)
        {
            if (!GeometryDefinitionVM.TryGetSurfaceAxisPolygon(row.SurfaceNumber, out var polygon) || polygon.Count < 3)
                continue;

            double rowSize = globalSize;
            if (!string.IsNullOrWhiteSpace(row.MeshSizeOverride) &&
                double.TryParse(row.MeshSizeOverride, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var ovr) && ovr > 0)
                rowSize = ovr;

            double refineMult = row.Refinement switch
            {
                MeshRefinementLevel.VeryFine => 0.25,
                MeshRefinementLevel.Fine     => 0.5,
                MeshRefinementLevel.Medium   => 0.75,
                MeshRefinementLevel.Coarse   => 1.5,
                _                            => 1.0,
            };
            rowSize *= refineMult;
            double rowNear = BoundaryGradationEnabled ? nearSize * refineMult : rowSize;
            minNearAcrossRegions = Math.Min(minNearAcrossRegions, rowNear);

            regions.Add((row.SurfaceNumber, polygon, rowNear, rowSize,
                BoundaryGradationEnabled ? layers : 0, growth));
        }

        if (regions.Count == 0)
        {
            SurfaceMeshStatus = "Error: no valid surface polygons found.";
            return;
        }

        Q8MeshMaterialLayerPlanner.LayerPlan? layerPlan = null;
        if (GeometryDefinitionVM.MaterialLayerCutElevations.Count > 0)
        {
            if (!Q8MeshMaterialLayerPlanner.TryCreatePlan(
                    GeometryDefinitionVM, _materialPropertiesVM, out layerPlan, out var layerErr))
            {
                SurfaceMeshStatus = $"Error: {layerErr}";
                return;
            }
        }
        _activeLayerPlan = layerPlan;
        _activeNonHorizontalPlan = null;

        var meshPolygons = new List<IReadOnlyList<Point>>();
        foreach (var row in activeSurfaces)
        {
            if (GeometryDefinitionVM.TryGetSurfaceAxisPolygon(row.SurfaceNumber, out var polygon) && polygon.Count >= 3)
                meshPolygons.Add(polygon);
        }

        var layerSegments = GeometryDefinitionVM.CollectMaterialLayerBoundarySegmentsForMeshes(meshPolygons)
            .Select(s => (s.A, s.B))
            .ToList();
        var jointSegments = CollectJointConstraintSegments(meshPolygons, minNearAcrossRegions);

        List<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize)>? excavationHoles = null;
        foreach (var row in SurfaceMeshRows.Where(r => r.ExcavatedAsOpenVoid))
        {
            if (!GeometryDefinitionVM.TryGetSurfaceAxisPolygon(row.SurfaceNumber, out var holePoly) || holePoly.Count < 3)
                continue;
            excavationHoles ??= new List<(int, IReadOnlyList<Point>, double)>();
            excavationHoles.Add((row.SurfaceNumber, holePoly, minNearAcrossRegions));
        }

        // ── Single conforming triangulation covering ALL regions ───────────────
        var conformingResults = Backend.GradedSurfaceMeshGenerator.GenerateConforming(
            regions, excavationHoles,
            layerSegments.Count > 0 ? layerSegments : null,
            pinnedBoundaryNodes: pinnedBoundaryNodes,
            maxNodes: 40_000,
            extraConstraintSegments: jointSegments.Count > 0 ? jointSegments : null);

        if (conformingResults.Count == 0 || !conformingResults.Any(r => r.Mesh.ElementCount > 0))
        {
            SurfaceMeshStatus = "Error: mesh generation produced no elements. Check that surfaces are closed polygons with sufficient size relative to mesh settings.";
            return;
        }

        // Shared global node list — same object reference across all region results.
        // Cast to mutable List<Point> so T6 upgrader can append midside nodes.
        var sharedNodes = conformingResults[0].Mesh.Nodes as List<Point>
                          ?? new List<Point>(conformingResults[0].Mesh.Nodes);

        // ── Optional T6 upgrade: insert midside nodes on every edge ───────────
        bool useT6 = rs2dRequestSixNodeTriangle
                     || string.Equals(MeshElementOrder, "6-node", StringComparison.OrdinalIgnoreCase);
        List<(int n1, int n2, int n3, int n4, int n5, int n6)>? t6All = null;
        int t6RegionStart = 0; // cursor into t6All per region

        if (useT6)
        {
            // Collect all T3 triangles across regions in a flat list (same order as iteration below)
            var allT3 = conformingResults
                .SelectMany(r => r.Mesh.Triangles)
                .ToList();
            t6All = Backend.T6Upgrader.Upgrade(sharedNodes, allT3);
            // sharedNodes now has corner + midside nodes appended
        }

        // Bulk-update: mesh can be 10k+ nodes/elements; per-item CollectionChanged would reproject the canvas each time.
        using (BeginMeshCanvasBulkUpdate())
        {
            // Add physical nodes (corner + midside if T6)
            foreach (var pt in sharedNodes)
                DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { Location = pt });

            int totalNodes    = sharedNodes.Count;
            int totalElems    = 0;
            int globalElemId  = 1;
            int processed     = 0;
            double overallMinAngle = 180, overallMaxAR = 0;

            foreach (var (sn, mesh) in conformingResults)
            {
                if (mesh.ElementCount == 0) continue;
                int regionEtype = sn;

                if (useT6 && t6All != null)
                {
                    // T6 path: use pre-computed midside indices
                    for (int ti = 0; ti < mesh.Triangles.Count; ti++)
                    {
                        var (n1, n2, n3, n4, n5, n6) = t6All[t6RegionStart + ti];
                        var pA = sharedNodes[n1]; var pB = sharedNodes[n2]; var pC = sharedNodes[n3];
                        var p4 = sharedNodes[n4]; var p5 = sharedNodes[n5]; var p6 = sharedNodes[n6];
                        double cx = (pA.X + pB.X + pC.X) / 3;
                        double cy = (pA.Y + pB.Y + pC.Y) / 3;
                        int elemEtype = layerPlan?.HasLayers == true ? layerPlan.ResolveEtype(cy) : regionEtype;
                        // T6: boundary polygon corner → midside around the triangle (canvas outline)
                        var t6Outline = new System.Windows.Media.PointCollection { pA, p4, pB, p5, pC, p6 };
                        EnsureCounterClockwisePolygon(t6Outline);
                        MeshElements.Add(new MeshElementDisplay
                        {
                            Nodes = t6Outline,
                            IsQuadraticTriangle = true,
                            CenterX = cx, CenterY = cy,
                            DisplayId = globalElemId, IsActive = true, IsHighlighted = false,
                        });
                        MeshInformationRows.Add(new MeshInformationRow
                        {
                            Kind = "T6",
                            ElementId = globalElemId,
                            Node1 = n1 + 1, Node2 = n2 + 1, Node3 = n3 + 1,
                            Node4 = n4 + 1, Node5 = n5 + 1, Node6 = n6 + 1,
                            Etype = elemEtype,
                            N1X = pA.X, N1Y = pA.Y, N2X = pB.X, N2Y = pB.Y,
                            N3X = pC.X, N3Y = pC.Y, N4X = p4.X, N4Y = p4.Y,
                            N5X = p5.X, N5Y = p5.Y, N6X = p6.X, N6Y = p6.Y,
                            Status = "Active"
                        });
                        globalElemId++;
                        totalElems++;
                    }
                    t6RegionStart += mesh.Triangles.Count;
                }
                else
                {
                    // T3 path (default)
                    foreach (var (ai, bi, ci) in mesh.Triangles)
                    {
                        var pA = sharedNodes[ai]; var pB = sharedNodes[bi]; var pC = sharedNodes[ci];
                        double cx = (pA.X + pB.X + pC.X) / 3;
                        double cy = (pA.Y + pB.Y + pC.Y) / 3;
                        int elemEtype = layerPlan?.HasLayers == true ? layerPlan.ResolveEtype(cy) : regionEtype;
                        MeshElements.Add(new MeshElementDisplay
                        {
                            Nodes = new System.Windows.Media.PointCollection { pA, pB, pC },
                            CenterX = cx, CenterY = cy,
                            DisplayId = globalElemId, IsActive = true, IsHighlighted = false,
                        });
                        MeshInformationRows.Add(new MeshInformationRow
                        {
                            Kind = "T3",
                            ElementId = globalElemId,
                            Node1 = ai + 1, Node2 = bi + 1, Node3 = ci + 1,
                            Etype = elemEtype,
                            N1X = pA.X, N1Y = pA.Y, N2X = pB.X, N2Y = pB.Y,
                            N3X = pC.X, N3Y = pC.Y,
                            Status = "Active"
                        });
                        globalElemId++;
                        totalElems++;
                    }
                }

                overallMinAngle = Math.Min(overallMinAngle, mesh.MinAngleDeg);
                overallMaxAR    = Math.Max(overallMaxAR,   mesh.MaxAspectRatio);
                processed++;
            }

            if (totalElems == 0)
            {
                SurfaceMeshStatus = "Error: mesh generation produced no elements. Check that surfaces are closed polygons with sufficient size relative to mesh settings.";
                return;
            }

            if (useT6)
                ResyncT6MeshElementOutlinesFromInformationRows();

            string nodeLabel = useT6 ? "T6 (6-node)" : "T3 (3-node)";
            IsDiscretized             = true;
            SurfaceMeshNodeCount      = totalNodes;
            SurfaceMeshElementCount   = totalElems;
            MeshQualityMinAngle       = overallMinAngle < 180 ? overallMinAngle : 0;
            MeshQualityMaxAspectRatio = overallMaxAR;
            MeshQualityMaxSkew = 0;
            _lastJacobianResult = ValidateMeshJacobianFromInformationRows(useT6);
            MeshQualityMinJacobian = _lastJacobianResult?.MinJacobian ?? 0;

            SurfaceMeshStatus =
                $"✓ {nodeLabel} mesh: {processed} surface(s), {totalNodes} nodes, {totalElems} elements.  " +
                $"Min angle: {MeshQualityMinAngle:F1}°  Max AR: {MeshQualityMaxAspectRatio:F2}" +
                (layerPlan?.HasLayers == true
                    ? $"  Material layers: {layerPlan.LayerCount} horizontal band(s), etype from layer assignments."
                    : "");
            AppendLayerBoundaryValidationToStatus();
            ApplyJointInterfacesAfterPopulate();
            GeometryDefinitionVM.StagedConstruction?.RederiveSurfaceExcavationElementIds();
        }
    }

    /// <summary>
    /// Phase 1 hybrid mesh entry point: a single sizing-field-driven pass that emits structured
    /// quads for clean mappable regions and graded conforming triangles elsewhere, then populates
    /// the canonical mesh collections (canvas + .dat export). Reuses the existing material-layer
    /// planner, T6 upgrader and Jacobian/layer validators. Returns a user-facing status string.
    /// Existing Surface / RS2D / Tunnel paths are untouched.
    /// </summary>
    public string GenerateHybridMesh(bool useQ8 = false)
    {
        if (GeometryDefinitionVM == null)
            return SurfaceMeshStatus = "Error: no geometry loaded.";

        EnsureMeshableBoundarySurface();
        RefreshSurfaceMeshRows();

        if (!SurfaceMeshRows.Any(r => r.WillBeMeshed))
            return SurfaceMeshStatus = "Error: no surfaces to mesh (assign a material to filled regions).";

        if (!double.TryParse(GlobalMeshSize, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var globalSize) || globalSize <= 0)
            return SurfaceMeshStatus = "Error: invalid Global Mesh Size value.";

        double nearSize = globalSize * 0.2;
        if (BoundaryGradationEnabled &&
            double.TryParse(NearBoundaryMeshSize, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var ns) && ns > 0)
            nearSize = ns;

        double growth = 1.3;
        if (BoundaryGradationEnabled &&
            double.TryParse(BoundaryGrowthFactor, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var gf) && gf > 1)
            growth = gf;

        var activeSurfaces = SurfaceMeshRows.Where(r => r.WillBeMeshed).ToList();

        var regionInputs = new List<Backend.HybridRegionInput>();
        var meshPolygons = new List<IReadOnlyList<Point>>();
        foreach (var row in activeSurfaces)
        {
            if (!GeometryDefinitionVM.TryGetSurfaceAxisPolygon(row.SurfaceNumber, out var polygon) || polygon.Count < 3)
                continue;
            regionInputs.Add(new Backend.HybridRegionInput(row.SurfaceNumber, polygon, HasHoles: false));
            meshPolygons.Add(polygon);
        }

        if (regionInputs.Count == 0)
            return SurfaceMeshStatus = "Error: no valid surface polygons found.";

        // Material-layer plan for etype resolution (identical to the surface mesher).
        Q8MeshMaterialLayerPlanner.LayerPlan? layerPlan = null;
        if (GeometryDefinitionVM.MaterialLayerCutElevations.Count > 0)
        {
            if (!Q8MeshMaterialLayerPlanner.TryCreatePlan(
                    GeometryDefinitionVM, _materialPropertiesVM, out layerPlan, out var layerErr))
                return SurfaceMeshStatus = $"Error: {layerErr}";
        }
        _activeLayerPlan = layerPlan;

        var layerSegments = GeometryDefinitionVM.CollectMaterialLayerBoundarySegmentsForMeshes(meshPolygons)
            .Select(s => (s.A, s.B))
            .ToList();

        // Inclined (non-horizontal) material interfaces: resolve per-zone etype and obtain clipped
        // interface segments. They are added below as conforming CDT constraints, but kept OUT of the
        // sizing field so the diagonal is honored without over-refining the whole band.
        if (!TryPrepareNonHorizontalLayerConstraints(meshPolygons, out var inclinedLayerSegments, out var nhErr))
            return SurfaceMeshStatus = $"Error: {nhErr}";

        List<Backend.HybridHoleInput>? holeOnly = null;
        foreach (var row in SurfaceMeshRows.Where(r => r.ExcavatedAsOpenVoid))
        {
            if (!GeometryDefinitionVM.TryGetSurfaceAxisPolygon(row.SurfaceNumber, out var holePoly) || holePoly.Count < 3)
                continue;
            holeOnly ??= new List<Backend.HybridHoleInput>();
            holeOnly.Add(new Backend.HybridHoleInput(row.SurfaceNumber, holePoly));
        }

        // Shared sizing field built from the user's Global/Near sizes + geometric features.
        var field = new Backend.MeshSizingField(baseSize: globalSize, minSize: 0.0, growthFactor: growth);
        foreach (var r in regionInputs)
            field.AddPolyline(r.Polygon, nearSize, closed: true);
        if (holeOnly != null)
            foreach (var h in holeOnly)
                field.AddPolyline(h.Polygon, nearSize, closed: true);
        foreach (var (a, b) in layerSegments)
            field.AddSegment(a, b, nearSize);
        var jointSegments = CollectJointConstraintSegments(meshPolygons, nearSize);
        foreach (var (a, b) in jointSegments)
            field.AddSegment(a, b, nearSize);

        // Conforming constraints = horizontal layer cuts + inclined interface cuts (inclined ones are
        // intentionally not in the sizing field above, to avoid the diagonal refinement blow-up).
        var constraintSegments = layerSegments;
        if (inclinedLayerSegments.Count > 0)
        {
            constraintSegments = new List<(Point A, Point B)>(layerSegments.Count + inclinedLayerSegments.Count);
            constraintSegments.AddRange(layerSegments);
            constraintSegments.AddRange(inclinedLayerSegments);
        }

        var result = Backend.HybridMeshDriver.Generate(new Backend.HybridMeshRequest
        {
            Regions = regionInputs,
            HoleOnlyRegions = holeOnly,
            MaterialLayerSegments = constraintSegments.Count > 0 ? constraintSegments : null,
            ExtraConstraintSegments = jointSegments.Count > 0 ? jointSegments : null,
            SizingField = field,
            BaseSize = globalSize,
            AllowStructured = true,
            RecombineToQuads = true,
            MaxNodes = 40_000,
        });

        if (result.IsEmpty)
            return SurfaceMeshStatus =
                "Error: hybrid mesh generation produced no elements. Check that surfaces are closed polygons sized relative to the mesh settings.";

        return PopulateFromHybridResult(result, layerPlan, "Hybrid mesh", useQ8);
    }

    /// <summary>
    /// All-quad vertical-sweep mesh: imaginary vertical lines (shared X columns) and horizontal rows
    /// decompose the slope/complex domain into rectangle/trapezium columns, each filled with Q4 quads
    /// only (sloped tops become trapezoidal quads; material-layer bands become structured rows;
    /// excavations are carved as holes). Reuses the shared <c>GradedSurfaceQuadGenerator</c> sweep and
    /// the canonical adapter. The result is validated for hanging nodes and coverage; any issue is
    /// surfaced as a warning rather than silently shipped. Existing mesh paths are untouched.
    /// </summary>
    public string GenerateQuadSweepMesh(bool useQ8 = false)
    {
        if (GeometryDefinitionVM == null)
            return SurfaceMeshStatus = "Error: no geometry loaded.";

        EnsureMeshableBoundarySurface();
        RefreshSurfaceMeshRows();

        if (!SurfaceMeshRows.Any(r => r.WillBeMeshed))
            return SurfaceMeshStatus = "Error: no surfaces to mesh (assign a material to filled regions).";

        if (!double.TryParse(GlobalMeshSize, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var globalSize) || globalSize <= 0)
            return SurfaceMeshStatus = "Error: invalid Global Mesh Size value.";

        double nearSize = globalSize * 0.2;
        if (BoundaryGradationEnabled &&
            double.TryParse(NearBoundaryMeshSize, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var ns) && ns > 0)
            nearSize = ns;

        double growth = 1.3;
        if (BoundaryGradationEnabled &&
            double.TryParse(BoundaryGrowthFactor, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var gf) && gf > 1)
            growth = gf;

        var activeSurfaces = SurfaceMeshRows.Where(r => r.WillBeMeshed).ToList();

        var regionParams = new List<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize, double GlobalSize, int Layers, double Growth)>();
        var meshPolygons = new List<IReadOnlyList<Point>>();
        foreach (var row in activeSurfaces)
        {
            if (!GeometryDefinitionVM.TryGetSurfaceAxisPolygon(row.SurfaceNumber, out var polygon) || polygon.Count < 3)
                continue;
            regionParams.Add((row.SurfaceNumber, polygon, nearSize, globalSize, 0, growth));
            meshPolygons.Add(polygon);
        }

        if (regionParams.Count == 0)
            return SurfaceMeshStatus = "Error: no valid surface polygons found.";

        // Material-layer plan for etype resolution (identical to the surface mesher).
        Q8MeshMaterialLayerPlanner.LayerPlan? layerPlan = null;
        if (GeometryDefinitionVM.MaterialLayerCutElevations.Count > 0)
        {
            if (!Q8MeshMaterialLayerPlanner.TryCreatePlan(
                    GeometryDefinitionVM, _materialPropertiesVM, out layerPlan, out var layerErr))
                return SurfaceMeshStatus = $"Error: {layerErr}";
        }
        _activeLayerPlan = layerPlan;

        var layerSegments = GeometryDefinitionVM.CollectMaterialLayerBoundarySegmentsForMeshes(meshPolygons)
            .Select(s => (s.A, s.B))
            .ToList();
        layerSegments.AddRange(CollectJointConstraintSegments(meshPolygons, nearSize));

        // Inclined (non-horizontal) material interfaces: resolve the per-zone etype plan. When zones
        // exist we use a dedicated quad-only clipping builder below so the generated quad edges follow
        // the inclined boundaries exactly.
        if (!TryPrepareNonHorizontalLayerConstraints(meshPolygons, out _, out var nhErr))
            return SurfaceMeshStatus = $"Error: {nhErr}";

        List<(int SurfaceNumber, IReadOnlyList<Point> Polygon, double NearSize)>? holeParams = null;
        var holePolys = new List<IReadOnlyList<Point>>();
        foreach (var row in SurfaceMeshRows.Where(r => r.ExcavatedAsOpenVoid))
        {
            if (!GeometryDefinitionVM.TryGetSurfaceAxisPolygon(row.SurfaceNumber, out var holePoly) || holePoly.Count < 3)
                continue;
            holeParams ??= new List<(int, IReadOnlyList<Point>, double)>();
            holeParams.Add((row.SurfaceNumber, holePoly, nearSize));
            holePolys.Add(holePoly);
        }

        if (_activeNonHorizontalPlan?.HasZones == true)
        {
            var exactQuadResult = Backend.NonHorizontalLayerConformingQuadMeshBuilder.Generate(
                new Backend.NonHorizontalLayerConformingQuadMeshBuilder.Request
                {
                    Regions = regionParams
                        .Select(r => (r.SurfaceNumber, r.Polygon))
                        .ToList(),
                    HolePolygons = holePolys,
                    LayerPlan = _activeNonHorizontalPlan,
                    GlobalSize = globalSize,
                    MaxNodes = 40_000,
                });

            if (exactQuadResult.IsEmpty)
                return SurfaceMeshStatus =
                    "Error: exact non-horizontal quad mesh produced no elements. Try a larger Global element size or simplify the inclined layer.";

            string exactStatus = PopulateFromHybridResult(
                exactQuadResult,
                layerPlan,
                "Quad sweep mesh (non-horizontal conforming)",
                useQ8);

            var exactQuads = exactQuadResult.Regions.SelectMany(r => r.Quads).ToList();
            bool exactHanging = Backend.GradedSurfaceQuadGenerator.HasHangingNode(
                exactQuadResult.Nodes,
                exactQuads,
                Math.Max(nearSize * 0.1, 1e-6));
            if (exactHanging)
                exactStatus = SurfaceMeshStatus = exactStatus + "  Warning: hanging nodes detected near the inclined layer interface.";
            return exactStatus;
        }

        var quadResults = Backend.GradedSurfaceQuadGenerator.GenerateConforming(
            regionParams,
            holeParams,
            layerSegments.Count > 0 ? layerSegments : null,
            pinnedBoundaryNodes: null,
            maxNodes: 40_000);

        if (quadResults == null || quadResults.Count == 0)
            return SurfaceMeshStatus =
                "Error: quad sweep produced no elements. Check that surfaces are closed polygons sized relative to the mesh settings.";

        // All per-region results share one node pool, so cross-region interfaces are conforming.
        IReadOnlyList<Point> sharedNodes = quadResults[0].Mesh.Nodes;
        var allQuads = new List<(int a, int b, int c, int d)>();
        var allTris = new List<(int a, int b, int c)>();
        var regions = new List<Backend.HybridRegionMesh>(quadResults.Count);
        foreach (var (surfaceNumber, mesh) in quadResults)
        {
            if (mesh.Quads.Count == 0 && mesh.Triangles.Count == 0) continue;
            allQuads.AddRange(mesh.Quads);
            allTris.AddRange(mesh.Triangles);
            regions.Add(new Backend.HybridRegionMesh
            {
                SurfaceNumber = surfaceNumber,
                Strategy = Backend.RegionMeshStrategy.StructuredTransfinite,
                Quads = mesh.Quads,
                Triangles = mesh.Triangles,
                Reason = "vertical-sweep all-quad column (T3 at material-layer wedge tips)"
            });
        }

        if (regions.Count == 0)
            return SurfaceMeshStatus = "Error: quad sweep produced no elements.";

        var quadResult = new Backend.HybridMeshResult
        {
            Nodes = sharedNodes,
            Regions = regions,
            Notes = "vertical-sweep all-quad"
        };

        // FE-validity + coverage diagnostics (no silent fallback; surfaced as a warning).
        double tol = Math.Max(nearSize * 0.1, 1e-6);
        bool hanging = Backend.GradedSurfaceQuadGenerator.HasHangingNode(sharedNodes, allQuads, tol);

        double netArea = 0;
        foreach (var (_, poly, _, _, _, _) in regionParams) netArea += PolygonArea(poly);
        foreach (var h in holePolys) netArea -= PolygonArea(h);
        double meshedArea = 0;
        foreach (var (a, b, c, d) in allQuads) meshedArea += QuadAreaAbs(sharedNodes[a], sharedNodes[b], sharedNodes[c], sharedNodes[d]);
        foreach (var (a, b, c) in allTris) meshedArea += TriangleAreaAbs(sharedNodes[a], sharedNodes[b], sharedNodes[c]);
        double coverage = netArea > 1e-9 ? meshedArea / netArea : 0;

        string status = PopulateFromHybridResult(quadResult, layerPlan, "Quad sweep mesh", useQ8);

        var warnings = new List<string>();
        if (hanging)
            warnings.Add("hanging nodes detected (mesh may be FE-incompatible)");
        if (coverage < 0.98)
            warnings.Add($"coverage {coverage:P0} (unmeshed gaps near boundaries)");
        if (warnings.Count > 0)
            status = SurfaceMeshStatus = status + "  \u26A0 " + string.Join("; ", warnings) + ".";

        return status;
    }

    private static double PolygonArea(IReadOnlyList<Point> poly)
    {
        if (poly == null || poly.Count < 3) return 0;
        double a = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p0 = poly[i];
            var p1 = poly[(i + 1) % poly.Count];
            a += p0.X * p1.Y - p1.X * p0.Y;
        }
        return Math.Abs(a) * 0.5;
    }

    private static double QuadAreaAbs(Point p1, Point p2, Point p3, Point p4)
    {
        double a = 0;
        a += p1.X * p2.Y - p2.X * p1.Y;
        a += p2.X * p3.Y - p3.X * p2.Y;
        a += p3.X * p4.Y - p4.X * p3.Y;
        a += p4.X * p1.Y - p1.X * p4.Y;
        return Math.Abs(a) * 0.5;
    }

    private static double TriangleAreaAbs(Point p1, Point p2, Point p3)
        => Math.Abs((p2.X - p1.X) * (p3.Y - p1.Y) - (p3.X - p1.X) * (p2.Y - p1.Y)) * 0.5;

    /// <summary>
    /// Builds the active non-horizontal layer plan (per-zone etype) and appends the inclined cut
    /// polyline segments — clipped to the meshed surface polygons — to <paramref name="layerSegments"/>
    /// so the structured/hybrid generators conform to them. No-op when no inclined cuts exist.
    /// </summary>
    private bool TryPrepareNonHorizontalLayerConstraints(
        IReadOnlyList<IReadOnlyList<Point>> meshPolygons,
        out List<(Point A, Point B)> inclinedSegments,
        out string? error)
    {
        error = null;
        inclinedSegments = new List<(Point A, Point B)>();
        _activeNonHorizontalPlan = null;
        if (GeometryDefinitionVM == null)
            return true;

        var polylines = GeometryDefinitionVM.GetNonHorizontalLayerCutPolylines();
        if (polylines.Count == 0)
            return true;

        if (!NonHorizontalLayerMeshPlan.TryCreatePlan(
                GeometryDefinitionVM, _materialPropertiesVM, out var nhPlan, out var planErr))
        {
            error = planErr;
            return false;
        }
        _activeNonHorizontalPlan = nhPlan.HasZones ? nhPlan : null;

        // Clipped inclined interface segments are returned separately so the caller can decide whether
        // to use them as conforming constraints (hybrid/CDT) without injecting them into the sizing
        // field (which would refine a dense band along the whole diagonal and explode element count).
        inclinedSegments = ClipPolylineSegmentsToPolygons(polylines, meshPolygons);
        return true;
    }

    private List<(Point A, Point B)> CollectJointConstraintSegments(
        IReadOnlyList<IReadOnlyList<Point>> meshPolygons, double nearSize)
    {
        if (GeometryDefinitionVM == null)
            return new List<(Point A, Point B)>();
        return FeJointConstraintCollector.Collect(
            GeometryDefinitionVM.GeologicalFeatures, meshPolygons, nearSize);
    }

    private void ApplyJointInterfacesAfterPopulate()
    {
        JointInterfaceElements.Clear();
        if (GeometryDefinitionVM == null || MeshInformationRows.Count == 0)
            return;
        var features = GeometryDefinitionVM.GeologicalFeatures;
        if (features.Count == 0 || features.All(f => !f.IsEnabled || f.Vertices.Count < 2))
            return;

        var nodes = FeJointMeshSplitter.CollectNodesFromRows(MeshInformationRows);
        for (int i = 0; i < DiscretizationNodes.Count; i++)
        {
            int id = DiscretizationNodes[i].NodeId > 0 ? DiscretizationNodes[i].NodeId : i + 1;
            if (!nodes.ContainsKey(id))
                nodes[id] = DiscretizationNodes[i].Location;
        }

        if (FeJointMeshSplitter.LooksAlreadySplit(nodes, features))
        {
            foreach (var iface in FeJointMeshSplitter.RecoverInterfaces(
                         MeshInformationRows.ToList(), nodes, features, GeometryDefinitionVM.JointPropertySets))
                JointInterfaceElements.Add(iface);
            if (JointInterfaceElements.Count > 0)
                SurfaceMeshStatus += $"  Joints: {JointInterfaceElements.Count} recovered interface(s).";
            return;
        }

        var split = FeJointMeshSplitter.Split(
            MeshInformationRows.ToList(), nodes, features, GeometryDefinitionVM.JointPropertySets);
        if (split.Interfaces.Count == 0 && split.AddedNodes.Count == 0)
            return;

        MeshInformationRows.Clear();
        foreach (var row in split.Elements)
            MeshInformationRows.Add(row);
        foreach (var (id, pt) in split.AddedNodes)
            DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { NodeId = id, Location = pt });
        foreach (var iface in split.Interfaces)
            JointInterfaceElements.Add(iface);
        SurfaceMeshNodeCount = DiscretizationNodes.Count;
        if (JointInterfaceElements.Count > 0)
            SurfaceMeshStatus += $"  Joints: {JointInterfaceElements.Count} interface(s), {split.AddedNodes.Count} split node(s).";
    }

    /// <summary>Clips each polyline segment to the union of the supplied polygons, returning the in-domain pieces.</summary>
    private static List<(Point A, Point B)> ClipPolylineSegmentsToPolygons(
        IReadOnlyList<IReadOnlyList<Point>> polylines,
        IReadOnlyList<IReadOnlyList<Point>> polygons)
    {
        var result = new List<(Point A, Point B)>();
        foreach (var pl in polylines)
        {
            for (int i = 0; i + 1 < pl.Count; i++)
            {
                foreach (var poly in polygons)
                    ClipSegmentToPolygon(pl[i], pl[i + 1], poly, result);
            }
        }
        return result;
    }

    private static void ClipSegmentToPolygon(Point a, Point b, IReadOnlyList<Point> poly, List<(Point A, Point B)> outSegs)
    {
        if (poly.Count < 3) return;
        var polyXY = poly.Select(p => (p.X, p.Y)).ToList();
        double dx = b.X - a.X, dy = b.Y - a.Y;
        var ts = new List<double> { 0.0, 1.0 };
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            if (TrySegmentParam(a, b, poly[i], poly[(i + 1) % n], out double t))
                ts.Add(t);
        }
        ts.Sort();
        for (int i = 0; i + 1 < ts.Count; i++)
        {
            double t0 = ts[i], t1 = ts[i + 1];
            if (t1 - t0 < 1e-9) continue;
            double tm = 0.5 * (t0 + t1);
            double mx = a.X + tm * dx, my = a.Y + tm * dy;
            if (!PolygonElementMatcher.PointInPolygon(mx, my, polyXY)) continue;
            outSegs.Add((new Point(a.X + t0 * dx, a.Y + t0 * dy), new Point(a.X + t1 * dx, a.Y + t1 * dy)));
        }
    }

    /// <summary>Parameter t in [0,1] along segment ab where it intersects segment cd, if a proper crossing exists.</summary>
    private static bool TrySegmentParam(Point a, Point b, Point c, Point d, out double t)
    {
        t = 0;
        double rx = b.X - a.X, ry = b.Y - a.Y;
        double sx = d.X - c.X, sy = d.Y - c.Y;
        double denom = rx * sy - ry * sx;
        if (Math.Abs(denom) < 1e-12) return false;
        double tNum = (c.X - a.X) * sy - (c.Y - a.Y) * sx;
        double uNum = (c.X - a.X) * ry - (c.Y - a.Y) * rx;
        t = tNum / denom;
        double u = uNum / denom;
        return t >= -1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9;
    }

    /// <summary>Adapter: write a <see cref="Backend.HybridMeshResult"/> into the canonical mesh
    /// collections. Quads become Q4 rows; triangles become T3 (or T6 when 6-node order is selected).
    /// In Phase 1 a result is all-quad or all-triangle, but this handles a mixed result generically
    /// so Phase 2 stitching needs no adapter change.</summary>
    private string PopulateFromHybridResult(Backend.HybridMeshResult result, Q8MeshMaterialLayerPlanner.LayerPlan? layerPlan, string meshLabel = "Hybrid mesh", bool useQ8 = false)
    {
        // When Q8 quads are requested, any CDT triangles are emitted as quadratic T6 too so the
        // mesh is uniformly quadratic; otherwise the triangle order follows MeshElementOrder.
        bool useT6 = useQ8 || string.Equals(MeshElementOrder, "6-node", StringComparison.OrdinalIgnoreCase);

        RecordUndo($"Generate {meshLabel.ToLowerInvariant()}");
        SelectedMeshInformationRow = null;
        DiscretizationNodes.Clear();
        MeshElements.Clear();
        MeshInformationRows.Clear();
        IsDiscretized = false;
        ShowBoundaryDiscretizationMarkers = false;
        SurfaceMeshNodeCount = 0;
        SurfaceMeshElementCount = 0;
        MeshQualityMinAngle = 0;
        MeshQualityMaxAspectRatio = 0;
        MeshQualityMaxSkew = 0;
        MeshQualityMinJacobian = 0;
        _lastJacobianResult = null;

        // Mutable shared node pool so the T6 upgrader can append midside nodes.
        var sharedNodes = result.Nodes as List<Point> ?? new List<Point>(result.Nodes);

        bool anyTriangles = result.Regions.Any(r => r.Triangles.Count > 0);
        bool anyQuads = result.Regions.Any(r => r.Quads.Count > 0);
        bool emitT6 = useT6 && anyTriangles;
        bool emitQ8 = useQ8 && anyQuads;

        // Upgrade quads to Q8 first (so corner indices are untouched), then triangles to T6.
        // Both upgraders append shared midside nodes to the same node pool, staying conforming.
        List<(int n1, int n2, int n3, int n4, int n5, int n6, int n7, int n8)>? q8All = null;
        int q8Cursor = 0;
        if (emitQ8)
        {
            var allQ4 = result.Regions.SelectMany(r => r.Quads).ToList();
            q8All = Backend.Q8Upgrader.Upgrade(sharedNodes, allQ4); // appends midside nodes to sharedNodes
        }

        List<(int n1, int n2, int n3, int n4, int n5, int n6)>? t6All = null;
        int t6Cursor = 0;
        if (emitT6)
        {
            var allT3 = result.Regions.SelectMany(r => r.Triangles).ToList();
            t6All = Backend.T6Upgrader.Upgrade(sharedNodes, allT3); // appends midside nodes to sharedNodes
        }

        int quadCount = 0, triCount = 0;
        using (BeginMeshCanvasBulkUpdate())
        {
            foreach (var pt in sharedNodes)
                DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { Location = pt });

            int globalElemId = 1;
            foreach (var region in result.Regions)
            {
                int regionFallbackEtype = region.SurfaceNumber < 1 ? 1 : region.SurfaceNumber;

                // Structured transfinite quads -> Q8 (serendipity, if requested) else Q4
                if (emitQ8 && q8All != null)
                {
                    for (int qi = 0; qi < region.Quads.Count; qi++)
                    {
                        var (a, b, c, d, e5, e6, e7, e8) = q8All[q8Cursor + qi];
                        if (!IsValidZeroBasedConnectivity(sharedNodes.Count, a, b, c, d, e5, e6, e7, e8))
                        {
                            // #region agent log
                            AgentDebugRuntimeLog.Write("post-fix", "H3", "MeshViewModel.cs:PopulateFromHybridResult",
                                "skipped invalid Q8 tuple during mesh population",
                                new
                                {
                                    meshLabel,
                                    region.SurfaceNumber,
                                    qi,
                                    nodes = new[] { a, b, c, d, e5, e6, e7, e8 },
                                    sharedNodeCount = sharedNodes.Count
                                });
                            // #endregion
                            continue;
                        }
                        var p1 = sharedNodes[a]; var p2 = sharedNodes[b]; var p3 = sharedNodes[c]; var p4 = sharedNodes[d];
                        var p5 = sharedNodes[e5]; var p6 = sharedNodes[e6]; var p7 = sharedNodes[e7]; var p8 = sharedNodes[e8];
                        double cx = (p1.X + p2.X + p3.X + p4.X) * 0.25;
                        double cy = (p1.Y + p2.Y + p3.Y + p4.Y) * 0.25;
                        // Canvas draws Q8 as a 4-corner straight-edged polygon (serendipity ordering).
                        var outline = new System.Windows.Media.PointCollection { p1, p2, p3, p4 };
                        EnsureCounterClockwisePolygon(outline);
                        MeshElements.Add(new MeshElementDisplay
                        {
                            Nodes = outline, CenterX = cx, CenterY = cy,
                            DisplayId = globalElemId, IsActive = true,
                            IsQuadrilateral = true, IsQuadraticQuad = true, Q8SmithEmb2dLocalOrder = false,
                        });
                        MeshInformationRows.Add(new MeshInformationRow
                        {
                            Kind = "Q8",
                            ElementId = globalElemId,
                            Node1 = a + 1, Node2 = b + 1, Node3 = c + 1, Node4 = d + 1,
                            Node5 = e5 + 1, Node6 = e6 + 1, Node7 = e7 + 1, Node8 = e8 + 1,
                            Etype = ResolveElementEtype(cx, cy, regionFallbackEtype),
                            N1X = p1.X, N1Y = p1.Y, N2X = p2.X, N2Y = p2.Y,
                            N3X = p3.X, N3Y = p3.Y, N4X = p4.X, N4Y = p4.Y,
                            N5X = p5.X, N5Y = p5.Y, N6X = p6.X, N6Y = p6.Y,
                            N7X = p7.X, N7Y = p7.Y, N8X = p8.X, N8Y = p8.Y,
                            SmithEmb2dLocalOrder = false,
                            Status = "Active",
                        });
                        globalElemId++; quadCount++;
                    }
                    q8Cursor += region.Quads.Count;
                }
                else
                {
                    foreach (var (a, b, c, d) in region.Quads)
                    {
                        if (!IsValidZeroBasedConnectivity(sharedNodes.Count, a, b, c, d))
                        {
                            // #region agent log
                            AgentDebugRuntimeLog.Write("post-fix", "H3", "MeshViewModel.cs:PopulateFromHybridResult",
                                "skipped invalid Q4 tuple during mesh population",
                                new
                                {
                                    meshLabel,
                                    region.SurfaceNumber,
                                    nodes = new[] { a, b, c, d },
                                    sharedNodeCount = sharedNodes.Count
                                });
                            // #endregion
                            continue;
                        }
                        var p1 = sharedNodes[a]; var p2 = sharedNodes[b]; var p3 = sharedNodes[c]; var p4 = sharedNodes[d];
                        double cx = (p1.X + p2.X + p3.X + p4.X) * 0.25;
                        double cy = (p1.Y + p2.Y + p3.Y + p4.Y) * 0.25;
                        var outline = new System.Windows.Media.PointCollection { p1, p2, p3, p4 };
                        EnsureCounterClockwisePolygon(outline);
                        MeshElements.Add(new MeshElementDisplay
                        {
                            Nodes = outline, CenterX = cx, CenterY = cy,
                            DisplayId = globalElemId, IsActive = true, IsQuadrilateral = true,
                        });
                        MeshInformationRows.Add(new MeshInformationRow
                        {
                            Kind = "Q4",
                            ElementId = globalElemId,
                            Node1 = a + 1, Node2 = b + 1, Node3 = c + 1, Node4 = d + 1,
                            Etype = ResolveElementEtype(cx, cy, regionFallbackEtype),
                            N1X = p1.X, N1Y = p1.Y, N2X = p2.X, N2Y = p2.Y,
                            N3X = p3.X, N3Y = p3.Y, N4X = p4.X, N4Y = p4.Y,
                            Status = "Active",
                        });
                        globalElemId++; quadCount++;
                    }
                }

                // Graded CDT triangles -> T6 (if requested) else T3
                if (emitT6 && t6All != null)
                {
                    for (int ti = 0; ti < region.Triangles.Count; ti++)
                    {
                        var (n1, n2, n3, n4, n5, n6) = t6All[t6Cursor + ti];
                        var pA = sharedNodes[n1]; var pB = sharedNodes[n2]; var pC = sharedNodes[n3];
                        var p4 = sharedNodes[n4]; var p5 = sharedNodes[n5]; var p6 = sharedNodes[n6];
                        double cx = (pA.X + pB.X + pC.X) / 3;
                        double cy = (pA.Y + pB.Y + pC.Y) / 3;
                        var t6Outline = new System.Windows.Media.PointCollection { pA, p4, pB, p5, pC, p6 };
                        EnsureCounterClockwisePolygon(t6Outline);
                        MeshElements.Add(new MeshElementDisplay
                        {
                            Nodes = t6Outline, IsQuadraticTriangle = true,
                            CenterX = cx, CenterY = cy,
                            DisplayId = globalElemId, IsActive = true,
                        });
                        MeshInformationRows.Add(new MeshInformationRow
                        {
                            Kind = "T6",
                            ElementId = globalElemId,
                            Node1 = n1 + 1, Node2 = n2 + 1, Node3 = n3 + 1,
                            Node4 = n4 + 1, Node5 = n5 + 1, Node6 = n6 + 1,
                            Etype = ResolveElementEtype(cx, cy, regionFallbackEtype),
                            N1X = pA.X, N1Y = pA.Y, N2X = pB.X, N2Y = pB.Y,
                            N3X = pC.X, N3Y = pC.Y, N4X = p4.X, N4Y = p4.Y,
                            N5X = p5.X, N5Y = p5.Y, N6X = p6.X, N6Y = p6.Y,
                            Status = "Active",
                        });
                        globalElemId++; triCount++;
                    }
                    t6Cursor += region.Triangles.Count;
                }
                else
                {
                    foreach (var (ai, bi, ci) in region.Triangles)
                    {
                        var pA = sharedNodes[ai]; var pB = sharedNodes[bi]; var pC = sharedNodes[ci];
                        double cx = (pA.X + pB.X + pC.X) / 3;
                        double cy = (pA.Y + pB.Y + pC.Y) / 3;
                        MeshElements.Add(new MeshElementDisplay
                        {
                            Nodes = new System.Windows.Media.PointCollection { pA, pB, pC },
                            CenterX = cx, CenterY = cy,
                            DisplayId = globalElemId, IsActive = true,
                        });
                        MeshInformationRows.Add(new MeshInformationRow
                        {
                            Kind = "T3",
                            ElementId = globalElemId,
                            Node1 = ai + 1, Node2 = bi + 1, Node3 = ci + 1,
                            Etype = ResolveElementEtype(cx, cy, regionFallbackEtype),
                            N1X = pA.X, N1Y = pA.Y, N2X = pB.X, N2Y = pB.Y,
                            N3X = pC.X, N3Y = pC.Y,
                            Status = "Active",
                        });
                        globalElemId++; triCount++;
                    }
                }
            }

            if (quadCount + triCount == 0)
                return SurfaceMeshStatus = "Error: hybrid mesh generation produced no elements.";

            if (emitT6)
                ResyncT6MeshElementOutlinesFromInformationRows();

            IsDiscretized = true;
            SurfaceMeshNodeCount = DiscretizationNodes.Count;
            SurfaceMeshElementCount = MeshInformationRows.Count;
            MeshQualityMinAngle = result.MinAngleDeg;
            MeshQualityMaxAspectRatio = result.MaxAspectRatio;
            MeshQualityMaxSkew = 0;
            _lastJacobianResult = ValidateMeshJacobianFromInformationRows(emitT6);
            MeshQualityMinJacobian = _lastJacobianResult?.MinJacobian ?? 0;

            string stitchNote = result.StitchApplied ? ", conformally stitched" : "";
            string gateNote = result.Quality.GatesPassed
                ? "  Quality gates: OK"
                : $"  \u26A0 Quality gates: {result.Quality.ElementsBelowMinAngle} small-angle, " +
                  $"{result.Quality.ElementsAboveMaxAspect} high-AR, {result.Quality.InvertedElements} inverted";

            string pathNote = string.IsNullOrWhiteSpace(result.Notes) ? "" : $"  [{result.Notes}]";
            string orderNote = quadCount > 0
                ? (emitQ8 ? "  Quads: Q8 (8-node)" : "  Quads: Q4 (4-node)")
                : "";

            SurfaceMeshStatus =
                $"\u2713 {meshLabel}: {result.Regions.Count} region(s), {result.StructuredRegionCount} structured{stitchNote}.  " +
                $"{SurfaceMeshNodeCount} nodes, {SurfaceMeshElementCount} elements ({quadCount} quad / {triCount} tri).{orderNote}  " +
                $"Min angle: {MeshQualityMinAngle:F1}\u00B0  Max AR: {MeshQualityMaxAspectRatio:F2}  " +
                $"Min scaled-J: {result.Quality.MinScaledJacobian:F3}.{gateNote}{pathNote}" +
                (layerPlan?.HasLayers == true ? $"  Material layers: {layerPlan.LayerCount} band(s)." : "");
            AppendLayerBoundaryValidationToStatus();
        }

        ApplyJointInterfacesAfterPopulate();
        GeometryDefinitionVM?.StagedConstruction?.RederiveSurfaceExcavationElementIds();
        TrySyncSrfScriptAfterMeshGeneration();
        return SurfaceMeshStatus;
    }

    private void TrySyncSrfScriptAfterMeshGeneration()
    {
        if (_materialPropertiesVM?.IsSrfAnalysisMode != true || MeshInformationRows.Count == 0)
            return;

        var meshType = P610AnalysisOptionsHelper.ResolveMeshType(this);
        if (meshType is not ("Q4" or "Q8"))
            return;

        _scriptVm?.TrySyncDiscretizedMeshToScriptDat(ignoreLoadedDeckPreserve: true);
    }

    private static bool IsValidZeroBasedConnectivity(int nodeCount, params int[] nodes)
    {
        if (nodes.Length == 0)
            return false;
        if (nodes.Any(n => n < 0 || n >= nodeCount))
            return false;
        return nodes.Distinct().Count() == nodes.Length;
    }

    private Backend.MeshJacobianValidator.Result? ValidateMeshJacobianFromInformationRows(bool useT6)
    {
        var rows = MeshInformationRows.ToList();
        if (rows.Count == 0) return null;

        var nodeIndex = new Dictionary<int, int>();
        var pts = new List<Point>();
        void RegisterNode(int id, double x, double y)
        {
            if (id <= 0 || nodeIndex.ContainsKey(id)) return;
            nodeIndex[id] = pts.Count;
            pts.Add(new Point(x, y));
        }

        foreach (var r in rows)
        {
            RegisterNode(r.Node1, r.N1X, r.N1Y);
            RegisterNode(r.Node2, r.N2X, r.N2Y);
            RegisterNode(r.Node3, r.N3X, r.N3Y);
            RegisterNode(r.Node4, r.N4X, r.N4Y);
            RegisterNode(r.Node5, r.N5X, r.N5Y);
            RegisterNode(r.Node6, r.N6X, r.N6Y);
            RegisterNode(r.Node7, r.N7X, r.N7Y);
            RegisterNode(r.Node8, r.N8X, r.N8Y);
        }

        // Partition rows by element kind so each is validated with the correct shape functions.
        // (Previously every row was treated as a T3/T6 triangle, which mis-validated quads.)
        var q8 = new List<(int n1, int n2, int n3, int n4, int n5, int n6, int n7, int n8)>();
        var q4 = new List<Backend.Rs2dQuadConverter.QuadResult>();
        var t6 = new List<Backend.Rs2dQuadConverter.TriRef>();
        var t3 = new List<Backend.Rs2dQuadConverter.TriRef>();

        foreach (var r in rows)
        {
            if (r.IsQ8)
            {
                q8.Add((nodeIndex[r.Node1], nodeIndex[r.Node2], nodeIndex[r.Node3], nodeIndex[r.Node4],
                        nodeIndex[r.Node5], nodeIndex[r.Node6], nodeIndex[r.Node7], nodeIndex[r.Node8]));
            }
            else if (r.IsQ4)
            {
                q4.Add(new Backend.Rs2dQuadConverter.QuadResult
                {
                    Q1 = nodeIndex[r.Node1], Q2 = nodeIndex[r.Node2],
                    Q3 = nodeIndex[r.Node3], Q4 = nodeIndex[r.Node4],
                });
            }
            else
            {
                var tri = new Backend.Rs2dQuadConverter.TriRef
                {
                    A = nodeIndex[r.Node1], B = nodeIndex[r.Node2], C = nodeIndex[r.Node3],
                };
                if (r.IsT6 || (useT6 && !r.IsT3)) t6.Add(tri);
                else t3.Add(tri);
            }
        }

        // Validate each group and merge into a single result (min Jacobian, summed counts).
        var noQuads = new List<Backend.Rs2dQuadConverter.QuadResult>();
        var noUnmatched = new List<Backend.Rs2dQuadConverter.UnmatchedTri>();
        var results = new List<Backend.MeshJacobianValidator.Result>();
        if (q8.Count > 0) results.Add(Backend.MeshJacobianValidator.Validate(pts, noQuads, noUnmatched, q8));
        if (q4.Count > 0) results.Add(Backend.MeshJacobianValidator.Validate(pts, q4, noUnmatched));
        if (t6.Count > 0) results.Add(Backend.MeshJacobianValidator.ValidateT6(pts, t6));
        if (t3.Count > 0) results.Add(Backend.MeshJacobianValidator.ValidateT3(pts, t3));

        if (results.Count == 0) return null;
        if (results.Count == 1) return results[0];

        double minJ = double.PositiveInfinity;
        int bad = 0, checkedCount = 0;
        foreach (var res in results)
        {
            minJ = Math.Min(minJ, res.MinJacobian);
            bad += res.BadElementCount;
            checkedCount += res.ElementsChecked;
        }
        bool valid = bad == 0 && minJ > 0;
        return new Backend.MeshJacobianValidator.Result
        {
            MinJacobian = minJ,
            BadElementCount = bad,
            ElementsChecked = checkedCount,
            IsValid = valid,
            Message = valid
                ? $"All {checkedCount} element(s) have positive Jacobian (min det J = {minJ:E3})."
                : $"{bad} of {checkedCount} element(s) with non-positive/near-zero Jacobian (min det J = {minJ:E3}).",
        };
    }

    /// <summary>Reverses vertex order if winding is clockwise (negative signed area).</summary>
    private static void EnsureCounterClockwisePolygon(System.Windows.Media.PointCollection pts)
    {
        if (pts == null || pts.Count < 3) return;
        double s = 0;
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % n];
            s += p.X * q.Y - q.X * p.Y;
        }
        if (s >= 0) return;
        var arr = new Point[n];
        for (int i = 0; i < n; i++)
            arr[i] = pts[i];
        Array.Reverse(arr);
        pts.Clear();
        foreach (var pt in arr)
            pts.Add(pt);
    }

    /// <summary>
    /// Align <see cref="MeshElements"/> outlines with T6 connectivity in <see cref="MeshInformationRows"/> when a display element still has only three corners.
    /// Safe to call after mesh generation or project load.
    /// </summary>
    public void ResyncT6MeshElementOutlinesFromInformationRows()
    {
        var byId = MeshInformationRows.GroupBy(r => r.ElementId).ToDictionary(g => g.Key, g => g.First());
        foreach (var el in MeshElements)
        {
            if (!byId.TryGetValue(el.DisplayId, out var row) || !row.IsT6) continue;
            if (el.Nodes != null && el.Nodes.Count >= 6)
            {
                el.IsQuadraticTriangle = true;
                continue;
            }

            var t6Outline = new System.Windows.Media.PointCollection
            {
                new(row.N1X, row.N1Y), new(row.N4X, row.N4Y), new(row.N2X, row.N2Y),
                new(row.N5X, row.N5Y), new(row.N3X, row.N3Y), new(row.N6X, row.N6Y)
            };
            EnsureCounterClockwisePolygon(t6Outline);
            el.Nodes = t6Outline;
            el.IsQuadraticTriangle = true;
        }
        // MeshTabCanvasWrapper refreshes on collection changes, not on in-place MeshElementDisplay mutations.
        if (IsDiscretized)
            OnPropertyChanged(nameof(MeshElements));
    }

    private void ExecuteResetSurfaceMeshDefaults()
    {
        GlobalDensityPreset       = "Medium";
        BoundaryGradationEnabled  = true;
        BoundaryLayerCount        = "3";
        BoundaryGrowthFactor      = "1.3";
        Rs2dBoundaryNodeCount     = "";
        Rs2dTargetElementCount    = "";
        foreach (var row in SurfaceMeshRows)
        {
            row.MeshSizeOverride = "";
            row.Refinement       = MeshRefinementLevel.Default;
            row.IsExcluded       = false;
        }
        SurfaceMeshStatus = "Defaults restored.";
    }

    // ── Tunnel Meshing (CDT + Ring Refinement) ────────────────────────────────

    private void ExecuteGenerateTunnelMesh()
    {
        if (GeometryDefinitionVM == null)
        {
            TunnelMeshStatus = "Error: no geometry loaded.";
            return;
        }

        // ── Parse parameters ──────────────────────────────────────────────────
        if (!double.TryParse(FarFieldEdgeLength, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double h0) || h0 <= 0)
        {
            TunnelMeshStatus = "Error: invalid Far Field Edge Length.";
            return;
        }
        if (!double.TryParse(NearTunnelEdgeLength, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double ht) || ht <= 0)
        {
            TunnelMeshStatus = "Error: invalid Near Tunnel Edge Length.";
            return;
        }
        if (!int.TryParse(RingCount, out int rings) || rings < 0) rings = 4;
        if (!double.TryParse(RingGrowthFactor, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double gf) || gf <= 1) gf = 1.2;
        if (!double.TryParse(FirstRingThickness, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double rt) || rt <= 0) rt = ht * 0.5;

        // ── Resolve outer domain polygon ──────────────────────────────────────
        // Use the largest-area surface polygon; fall back to bounding box of all vertices.
        var outerPolygon = ResolveOuterPolygon();
        if (outerPolygon == null || outerPolygon.Count < 3)
        {
            TunnelMeshStatus = "Error: no closed outer boundary found in geometry. Define at least one surface polygon.";
            return;
        }

        // ── Collect tunnel circles (axis coordinates) ─────────────────────────
        // Circular tunnel excavations
        var tunnels = GeometryDefinitionVM.TunnelExcavationCircleRows
            .Where(r => r.Radius > 1e-9)
            .Select(r => (Center: new Point(r.CenterX, r.CenterY), Radius: r.Radius))
            .ToList();

        // Polygon-based excavated surfaces (arch cavern, rectangular trench, etc.)
        // These are all non-outermost surfaces that are NOT the outer domain itself.
        // The outer polygon was already resolved as the largest-area surface; all others are holes.
        var polygonHoles = new List<IReadOnlyList<Point>>();
        foreach (var surf in GeometryDefinitionVM.GeometrySurfaces)
        {
            if (!GeometryDefinitionVM.TryGetSurfaceAxisPolygon(surf.SurfaceNumber, out var holePoly) || holePoly.Count < 3)
                continue;
            // Skip the outer polygon itself (same vertices / same area within tolerance)
            double area = Backend.SurfaceMeshHierarchy.PolygonArea(holePoly);
            double outerArea = Backend.SurfaceMeshHierarchy.PolygonArea(outerPolygon);
            if (Math.Abs(area - outerArea) < outerArea * 0.01) continue; // same polygon = outer boundary
            polygonHoles.Add(holePoly);
        }

        // Tunnel Wizard polygons (horseshoe, rectangular, NATM arch, DXF). These live alongside
        // FilteredTunnelExcavationCircles and feed straight into the mesher's polygonHoles arg.
        foreach (var tp in GeometryDefinitionVM.FilteredTunnelExcavationPolygons)
        {
            if (tp?.AxisPoints == null || tp.AxisPoints.Count < 3) continue;
            polygonHoles.Add(tp.AxisPoints);
        }

        // ── Clear existing mesh ───────────────────────────────────────────────
        RecordUndo("Generate tunnel mesh");
        DiscretizationNodes.Clear();
        MeshElements.Clear();
        MeshInformationRows.Clear();
        EdgeElementConnectivity.Clear();
        EdgeNodeConnectivity.Clear();
        IsDiscretized = false;
        ShowBoundaryDiscretizationMarkers = false;
        TunnelMeshNodeCount = 0;
        TunnelMeshElementCount = 0;
        TunnelInteriorElementCount = 0;
        OuterBoundaryNodeCount = 0;
        OuterBoundaryEdgeCount = 0;
        TotalElementsTouchingOuterEdges = 0;
        OnPropertyChanged(nameof(AverageElementsPerOuterEdge));

        // ── Generate ──────────────────────────────────────────────────────────
        var meshResult = Backend.TunnelMeshGenerator.Generate(
            outerPolygon,
            circleHoles:         tunnels,
            polygonHoles:        polygonHoles,
            farFieldEdgeLength:  h0,
            nearBoundarySize:    ht,
            ringCount:           rings,
            ringGrowthFactor:    gf,
            firstRingThickness:  rt,
            maxNodes:            60_000);

        if (meshResult.Nodes.Count == 0 ||
            (meshResult.ActiveTriangles.Count == 0 && meshResult.TunnelTriangles.Count == 0))
        {
            TunnelMeshStatus = "Error: mesh generation produced no elements. Adjust parameters or check geometry.";
            return;
        }

        // ── Optional T6 upgrade ───────────────────────────────────────────────
        bool useT6Tunnel = string.Equals(MeshElementOrder, "6-node", StringComparison.Ordinal);
        var mutableNodes = new List<Point>(meshResult.Nodes); // T6Upgrader mutates this list

        List<(int n1, int n2, int n3, int n4, int n5, int n6)>? t6Active  = null;
        List<(int n1, int n2, int n3, int n4, int n5, int n6)>? t6Tunnel2 = null;

        if (useT6Tunnel)
        {
            var allT3 = meshResult.ActiveTriangles.Concat(meshResult.TunnelTriangles).ToList();
            var t6All = Backend.T6Upgrader.Upgrade(mutableNodes, allT3);
            t6Active  = t6All.Take(meshResult.ActiveTriangles.Count).ToList();
            t6Tunnel2 = t6All.Skip(meshResult.ActiveTriangles.Count).ToList();
        }

        // ── Populate nodes ────────────────────────────────────────────────────
        var nodes = (IReadOnlyList<Point>)mutableNodes;
        foreach (var pt in nodes)
            DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { Location = pt });

        // ── Populate elements ─────────────────────────────────────────────────
        int globalElemId = 1;

        void AddTrisT3(IReadOnlyList<(int a, int b, int c)> tris, bool isActive, int etype)
        {
            foreach (var (ai, bi, ci) in tris)
            {
                var pA = nodes[ai]; var pB = nodes[bi]; var pC = nodes[ci];
                double cx = (pA.X + pB.X + pC.X) / 3;
                double cy = (pA.Y + pB.Y + pC.Y) / 3;
                MeshElements.Add(new MeshElementDisplay
                {
                    Nodes = new System.Windows.Media.PointCollection { pA, pB, pC },
                    CenterX = cx, CenterY = cy,
                    DisplayId = globalElemId, IsActive = isActive, IsHighlighted = false,
                });
                MeshInformationRows.Add(new MeshInformationRow
                {
                    ElementId = globalElemId,
                    Node1 = ai + 1, Node2 = bi + 1, Node3 = ci + 1,
                    Etype = etype,
                    N1X = pA.X, N1Y = pA.Y,
                    N2X = pB.X, N2Y = pB.Y,
                    N3X = pC.X, N3Y = pC.Y,
                    Status = isActive ? "Active" : "Tunnel"
                });
                globalElemId++;
            }
        }

        void AddTrisT6(List<(int n1, int n2, int n3, int n4, int n5, int n6)> t6Tris, bool isActive, int etype)
        {
            foreach (var (n1, n2, n3, n4, n5, n6) in t6Tris)
            {
                var pA = nodes[n1]; var pB = nodes[n2]; var pC = nodes[n3];
                var p4 = nodes[n4]; var p5 = nodes[n5]; var p6 = nodes[n6];
                double cx = (pA.X + pB.X + pC.X) / 3;
                double cy = (pA.Y + pB.Y + pC.Y) / 3;
                // T6: interleave midside nodes so canvas polygon shows all 6 vertices
                MeshElements.Add(new MeshElementDisplay
                {
                    Nodes = new System.Windows.Media.PointCollection { pA, p4, pB, p5, pC, p6 },
                    IsQuadraticTriangle = true,
                    CenterX = cx, CenterY = cy,
                    DisplayId = globalElemId, IsActive = isActive, IsHighlighted = false,
                });
                MeshInformationRows.Add(new MeshInformationRow
                {
                    ElementId = globalElemId,
                    Node1 = n1 + 1, Node2 = n2 + 1, Node3 = n3 + 1,
                    Node4 = n4 + 1, Node5 = n5 + 1, Node6 = n6 + 1,
                    Etype = etype,
                    N1X = pA.X, N1Y = pA.Y, N2X = pB.X, N2Y = pB.Y,
                    N3X = pC.X, N3Y = pC.Y, N4X = p4.X, N4Y = p4.Y,
                    N5X = p5.X, N5Y = p5.Y, N6X = p6.X, N6Y = p6.Y,
                    Status = isActive ? "Active" : "Tunnel"
                });
                globalElemId++;
            }
        }

        if (useT6Tunnel && t6Active != null && t6Tunnel2 != null)
        {
            AddTrisT6(t6Active,  isActive: true,  etype: 1);
            AddTrisT6(t6Tunnel2, isActive: false, etype: 0);
        }
        else
        {
            AddTrisT3(meshResult.ActiveTriangles, isActive: true,  etype: 1);
            AddTrisT3(meshResult.TunnelTriangles, isActive: false, etype: 0);
        }

        IsDiscretized = true;

        // ── Statistics ────────────────────────────────────────────────────────
        TunnelMeshNodeCount        = nodes.Count;
        TunnelMeshElementCount     = meshResult.ActiveTriangles.Count;
        TunnelInteriorElementCount = meshResult.TunnelTriangles.Count;

        // ── Edge connectivity for rectangular outer boundary ───────────────────
        ComputeEdgeConnectivity(outerPolygon, nodes, meshResult.ActiveTriangles);

        OnPropertyChanged(nameof(ActiveElementCount));

        string tunnelElemLabel = useT6Tunnel ? "T6 (6-node)" : "T3 (3-node)";
        TunnelMeshStatus =
            $"✓ {tunnelElemLabel}  {meshResult.ActiveTriangles.Count} active + {meshResult.TunnelTriangles.Count} tunnel elements, " +
            $"{nodes.Count} nodes.  " +
            $"Min angle: {meshResult.MinAngleDeg:F1}°  Max AR: {meshResult.MaxAspectRatio:F2}";

        // Re-link stage excavation zones to the freshly numbered tunnel-mesh elements.
        GeometryDefinitionVM?.StagedConstruction?.RederiveSurfaceExcavationElementIds();
    }

    /// <summary>
    /// Compute edge-element and edge-node connectivity for the 4 sides of a rectangular outer boundary.
    /// Works correctly even when the boundary is slightly non-axis-aligned.
    /// </summary>
    private void ComputeEdgeConnectivity(
        IReadOnlyList<Point>                    outerPoly,
        IReadOnlyList<Point>                    nodes,
        IReadOnlyList<(int a, int b, int c)>    activeTris)
    {
        double minX = outerPoly.Min(p => p.X), maxX = outerPoly.Max(p => p.X);
        double minY = outerPoly.Min(p => p.Y), maxY = outerPoly.Max(p => p.Y);
        double eps  = Math.Max(maxX - minX, maxY - minY) * 0.01;

        // Side names and which nodes belong to each
        var sideNames = new[] { "Left", "Right", "Bottom", "Top" };
        var sideNodes = new Dictionary<string, HashSet<int>>();
        foreach (var name in sideNames) sideNodes[name] = new HashSet<int>();

        for (int i = 0; i < nodes.Count; i++)
        {
            var p = nodes[i];
            if (Math.Abs(p.X - minX) < eps) sideNodes["Left"].Add(i);
            if (Math.Abs(p.X - maxX) < eps) sideNodes["Right"].Add(i);
            if (Math.Abs(p.Y - minY) < eps) sideNodes["Bottom"].Add(i);
            if (Math.Abs(p.Y - maxY) < eps) sideNodes["Top"].Add(i);
        }

        // Find active triangles that have ≥2 nodes on each side (= boundary edge on that side)
        var sideElems = new Dictionary<string, List<int>>();
        foreach (var name in sideNames) sideElems[name] = new List<int>();

        int elemId = 1;
        foreach (var (ai, bi, ci) in activeTris)
        {
            foreach (var name in sideNames)
            {
                var ns     = sideNodes[name];
                int onSide = (ns.Contains(ai) ? 1 : 0) +
                             (ns.Contains(bi) ? 1 : 0) +
                             (ns.Contains(ci) ? 1 : 0);
                if (onSide >= 2) { sideElems[name].Add(elemId); break; }
            }
            elemId++;
        }

        // Populate observable collections
        int totalElems = 0, totalNodes = 0;
        foreach (var name in sideNames)
        {
            var en = sideNodes[name];
            var ee = sideElems[name];
            EdgeElementConnectivity.Add(new Models.EdgeConnectivityRow
            {
                EdgeName   = name,
                ElementIds = ee,
            });
            EdgeNodeConnectivity.Add(new Models.EdgeConnectivityRow
            {
                EdgeName = name,
                NodeIds  = en.OrderBy(i => i).Select(i => i + 1).ToList(),
            });
            totalElems += ee.Count;
            totalNodes += en.Count;
        }

        OuterBoundaryEdgeCount          = sideNames.Length;
        OuterBoundaryNodeCount          = totalNodes;
        TotalElementsTouchingOuterEdges = totalElems;
        OnPropertyChanged(nameof(AverageElementsPerOuterEdge));
    }

    /// <summary>
    /// Returns the largest-area surface polygon from geometry.
    /// If no surfaces are defined, builds a bounding-box polygon from geometry vertices.
    /// </summary>
    private IReadOnlyList<Point>? ResolveOuterPolygon()
    {
        if (GeometryDefinitionVM == null) return null;

        // Try surfaces: use the largest-area polygon
        if (GeometryDefinitionVM.GeometrySurfaces.Count > 0)
        {
            IReadOnlyList<Point>? best = null;
            double bestArea = 0;
            foreach (var surf in GeometryDefinitionVM.GeometrySurfaces)
            {
                if (!GeometryDefinitionVM.TryGetSurfaceAxisPolygon(surf.SurfaceNumber, out var poly) || poly.Count < 3) continue;
                double area = Backend.SurfaceMeshHierarchy.PolygonArea(poly);
                if (area > bestArea) { bestArea = area; best = poly; }
            }
            if (best != null) return best;
        }

        // Fallback: bounding box of all geometry vertices
        var verts = GeometryDefinitionVM.GeometryVertexRows;
        if (verts.Count < 2) return null;

        double x0 = verts.Min(v => v.AxisX), x1 = verts.Max(v => v.AxisX);
        double y0 = verts.Min(v => v.AxisY), y1 = verts.Max(v => v.AxisY);
        if (Math.Abs(x1 - x0) < 1e-9 || Math.Abs(y1 - y0) < 1e-9) return null;

        return new List<Point>
        {
            new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)
        };
    }

    private void ExecuteResetTunnelMeshingDefaults()
    {
        FarFieldEdgeLength        = "5.0";
        NearTunnelEdgeLength      = "0.2";
        RingCount                 = "4";
        RingGrowthFactor          = "1.2";
        FirstRingThickness        = "0.3";
        MinAngleDegrees           = "20";
        MaxRefinementIterations   = "100";
        MergeTolerance            = "1e-6";
        PointLoadRefinementRadius = "";
        SmoothingIterations       = "3";
        RefineNearExcavation      = true;
        RefineNearLoads           = false;
        RefineNearMaterialInterfaces = false;
        RefineNearStiffnessContrast  = false;
        TunnelMeshStatus = "Defaults restored.";
    }

    private void ExportEdgeElementConnectivity()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Export Edge-Element Connectivity",
            Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = "csv",
            FileName   = "EdgeElementConnectivity.csv",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            using var sw = new System.IO.StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);
            sw.WriteLine("Edge,ElementCount,Elements");
            foreach (var row in EdgeElementConnectivity)
                sw.WriteLine($"{row.EdgeName},{row.ElementCount},\"{row.ElementIdsString}\"");
            TunnelMeshStatus = $"Exported edge-element connectivity to {System.IO.Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            TunnelMeshStatus = $"Export failed: {ex.Message}";
        }
    }

    private void ExportEdgeNodeConnectivity()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Export Edge-Node Connectivity",
            Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = "csv",
            FileName   = "EdgeNodeConnectivity.csv",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            using var sw = new System.IO.StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8);
            sw.WriteLine("Edge,NodeCount,Nodes");
            foreach (var row in EdgeNodeConnectivity)
                sw.WriteLine($"{row.EdgeName},{row.NodeCount},\"{row.NodeIdsString}\"");
            TunnelMeshStatus = $"Exported edge-node connectivity to {System.IO.Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            TunnelMeshStatus = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Constraint nodes for mesh generation: uses the RS2D Discretize preview when available,
    /// otherwise re-samples boundaries and material layer interfaces from current geometry/settings.
    /// </summary>
    private List<Point>? CollectRs2dPinnedDiscretizationForMesh()
    {
        if (Rs2dDiscretized && DiscretizationNodes.Count > 0)
            return DiscretizationNodes.Select(n => n.Location).ToList();

        if (GeometryDefinitionVM == null)
            return null;

        var pts = CollectRs2dDiscretizedBoundaryPoints(GeometryDefinitionVM, out _);
        return pts.Count > 0 ? pts : null;
    }

    /// <summary>RS2D Discretize: sample all outer polyline edges, internal constraint edges, and excavation circles in axis units.</summary>
    private List<Point> CollectRs2dDiscretizedBoundaryPoints(GeometryDefinitionViewModel g, out string? error)
    {
        error = null;
        var result = new List<Point>();
        var seen = new HashSet<(long qx, long qy)>();
        _rs2dBoundarySegments.Clear();
        var boundarySegments = new List<Backend.Rs2dBoundarySegment>();
        const double quant = 1e6;
        void AddUnique(Point p)
        {
            var k = ((long)Math.Round(p.X * quant), (long)Math.Round(p.Y * quant));
            if (!seen.Add(k)) return;
            result.Add(p);
        }

        bool TryVertexAxis(string label, out Point p)
        {
            p = default;
            if (string.IsNullOrWhiteSpace(label) || label == "—") return false;
            var v = g.GeometryVertexRows.FirstOrDefault(r => r.PointLabel == label);
            if (v == null) return false;
            p = new Point(v.AxisX, v.AxisY);
            return true;
        }

        var rows = g.GeometryVertexRows;
        if (rows.Count < 2)
        {
            error = "Add at least two vertices on the outer boundary.";
            return result;
        }

        double minX = rows[0].AxisX, maxX = rows[0].AxisX, minY = rows[0].AxisY, maxY = rows[0].AxisY;
        foreach (var v in rows)
        {
            minX = Math.Min(minX, v.AxisX); maxX = Math.Max(maxX, v.AxisX);
            minY = Math.Min(minY, v.AxisY); maxY = Math.Max(maxY, v.AxisY);
        }

        var circles = new List<(Point c, double r)>();
        void AddCircle(Point c, double r)
        {
            if (r < 1e-9 || double.IsNaN(r) || double.IsInfinity(r)) return;
            foreach (var o in circles)
            {
                if (Math.Abs(o.c.X - c.X) < 1e-6 && Math.Abs(o.c.Y - c.Y) < 1e-6 && Math.Abs(o.r - r) < 1e-6)
                    return;
            }
            circles.Add((c, r));
            minX = Math.Min(minX, c.X - r); maxX = Math.Max(maxX, c.X + r);
            minY = Math.Min(minY, c.Y - r); maxY = Math.Max(maxY, c.Y + r);
        }

        foreach (var tc in g.FilteredTunnelExcavationCircles)
            AddCircle(tc.Center, tc.Radius);
        foreach (var ic in g.GeometryInternalCircles)
            AddCircle(new Point(ic.CenterAxisX, ic.CenterAxisY), ic.RadiusAxis);

        double dxb = maxX - minX, dyb = maxY - minY;
        double Ldiag = Math.Sqrt(dxb * dxb + dyb * dyb);
        double Lchar = Math.Max(Ldiag, 1e-9);

        if (!double.TryParse(GlobalMeshSize, NumberStyles.Any, CultureInfo.InvariantCulture, out var hFarGuess) || hFarGuess <= 0)
            hFarGuess = Lchar / 15.0;
        double hFar = hFarGuess;

        // User-defined boundary node count: average spacing ≈ perimeter / N_b.
        if (int.TryParse(Rs2dBoundaryNodeCount?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nBound)
            && nBound > 0)
        {
            nBound = Math.Clamp(nBound, 4, 50_000);
            double peri = EstimateOuterPerimeter();
            if (peri > 1e-12)
                hFar = peri / nBound;
        }

        double gf = 0.3;
        if (double.TryParse(Rs2dGradationFactor, NumberStyles.Float, CultureInfo.InvariantCulture, out var gfp) && gfp > 0)
            gf = gfp;
        double hNear = gf <= 1 ? hFar * gf : hFar / gf;

        int nExc = 64;
        if (int.TryParse(Rs2dExcavationNodeCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ne) && ne > 0)
            nExc = Math.Clamp(ne, 8, 720);

        var meshMode = Rs2dMeshType ?? "Graded";
        bool noInternal = string.Equals(meshMode, "No Internal Nodes", StringComparison.OrdinalIgnoreCase);
        bool uniform = string.Equals(meshMode, "Uniform", StringComparison.OrdinalIgnoreCase);
        bool radial = string.Equals(meshMode, "Radial", StringComparison.OrdinalIgnoreCase);
        var radialCenter = new Point((minX + maxX) / 2, (minY + maxY) / 2);

        double GradingDistance(Point mid)
        {
            if (uniform) return 1e12;
            if (radial)
            {
                var ddx = mid.X - radialCenter.X;
                var ddy = mid.Y - radialCenter.Y;
                return Math.Sqrt(ddx * ddx + ddy * ddy);
            }
            if (circles.Count == 0) return Lchar;
            double best = double.MaxValue;
            foreach (var (c, r) in circles)
            {
                var d = Math.Sqrt((mid.X - c.X) * (mid.X - c.X) + (mid.Y - c.Y) * (mid.Y - c.Y));
                best = Math.Min(best, Math.Abs(d - r));
            }
            return best;
        }

        void SubdivideLinear(Point a, Point b, bool treatAsExcavationBoundary)
        {
            var ddx = b.X - a.X;
            var ddy = b.Y - a.Y;
            var segLen = Math.Sqrt(ddx * ddx + ddy * ddy);
            if (segLen < 1e-12) return;

            int n;
            if (noInternal && !treatAsExcavationBoundary)
                n = 1;
            else
            {
                var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                double dist = GradingDistance(mid);
                double w = 1.0 / (1.0 + dist / (Lchar * 0.08 + 1e-12));
                double h = uniform ? hFar : hFar + (hNear - hFar) * w;
                n = Math.Max(1, (int)Math.Ceiling(segLen / Math.Max(h, 1e-12)));
                n = Math.Min(n, 500);
            }

            var tempPts = new List<Point>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                tempPts.Add(new Point(a.X + t * ddx, a.Y + t * ddy));
            }
            foreach (var tp in tempPts)
                AddUnique(tp);
            for (int i = 0; i < tempPts.Count - 1; i++)
                boundarySegments.Add(Backend.Rs2dBoundarySegment.Line(tempPts[i], tempPts[i + 1]));
        }

        void SubdivideArcAxis(Point p1, Point p2, Point p3, bool treatAsExcavationBoundary)
        {
            if (!Rs2dTryCircleFromThreePoints(p1, p2, p3, out var cx, out var cy, out var r) || r < 1e-9)
            {
                SubdivideLinear(p1, p3, treatAsExcavationBoundary);
                return;
            }

            double a1 = Math.Atan2(p1.Y - cy, p1.X - cx);
            double a2 = Math.Atan2(p2.Y - cy, p2.X - cx);
            double a3 = Math.Atan2(p3.Y - cy, p3.X - cx);
            double sweep = Rs2dNormAngle(a3 - a1);
            double sweepMid = Rs2dNormAngle(a2 - a1);
            if (Math.Abs(sweepMid - sweep) > 1e-9 && sweepMid > sweep)
                sweep -= 2 * Math.PI;

            double arcLen = Math.Abs(sweep) * r;
            double midAng = a1 + sweep / 2;
            var midPt = new Point(cx + r * Math.Cos(midAng), cy + r * Math.Sin(midAng));

            int n;
            if (noInternal && !treatAsExcavationBoundary)
                n = 1;
            else
            {
                double dist = GradingDistance(midPt);
                double w = 1.0 / (1.0 + dist / (Lchar * 0.08 + 1e-12));
                double h = uniform ? hFar : hFar + (hNear - hFar) * w;
                n = Math.Max(1, (int)Math.Ceiling(arcLen / Math.Max(h, 1e-12)));
                n = Math.Min(n, 500);
            }

            var tempPts = new List<Point>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                double ang = a1 + t * sweep;
                tempPts.Add(new Point(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang)));
            }
            foreach (var tp in tempPts)
                AddUnique(tp);
            for (int i = 0; i < tempPts.Count - 1; i++)
            {
                double aStart = Math.Atan2(tempPts[i].Y - cy, tempPts[i].X - cx);
                double aEnd = Math.Atan2(tempPts[i + 1].Y - cy, tempPts[i + 1].X - cx);
                boundarySegments.Add(Backend.Rs2dBoundarySegment.Arc(
                    tempPts[i], tempPts[i + 1], cx, cy, r, aStart, aEnd));
            }
        }

        void SampleFullCircle(Point c, double rad)
        {
            double hCirc = Math.Max(hNear, 1e-12);
            int n = Math.Max(8, Math.Min(720, (int)Math.Round(2 * Math.PI * rad / hCirc)));
            n = Math.Max(n, Math.Min(720, nExc));
            var tempPts = new List<Point>(n);
            for (int i = 0; i < n; i++)
            {
                double ang = i * 2 * Math.PI / n;
                tempPts.Add(new Point(c.X + rad * Math.Cos(ang), c.Y + rad * Math.Sin(ang)));
            }
            foreach (var tp in tempPts)
                AddUnique(tp);
            for (int i = 0; i < tempPts.Count; i++)
            {
                int next = (i + 1) % tempPts.Count;
                double aStart = Math.Atan2(tempPts[i].Y - c.Y, tempPts[i].X - c.X);
                double aEnd = Math.Atan2(tempPts[next].Y - c.Y, tempPts[next].X - c.X);
                boundarySegments.Add(Backend.Rs2dBoundarySegment.Arc(
                    tempPts[i], tempPts[next], c.X, c.Y, rad, aStart, aEnd));
            }
        }

        // Prefer closed loops from the Surface grid (S1, S2, …) — same topology as meshing. Do not use
        // GeometryVertexRows list order or arbitrary GeometryLineSegments (L1–L9, …), which are often
        // interior/shared edges, not the closed polygon boundary.
        var usedSurfacePerimeters = false;
        foreach (var surface in g.GeometrySurfaces)
        {
            if (!g.TryHasClosedSurfacePolygon(surface)) continue;
            if (g.TryGetSurfacePerimeterAxisSegments(surface.SurfaceNumber, out var perimeter) && perimeter.Count > 0)
            {
                usedSurfacePerimeters = true;
                foreach (var seg in perimeter)
                {
                    if (seg.IsArc)
                        SubdivideArcAxis(seg.A, seg.B, seg.C, treatAsExcavationBoundary: true);
                    else
                        SubdivideLinear(seg.A, seg.B, treatAsExcavationBoundary: true);
                }

                continue;
            }

            if (!g.TryGetSurfaceAxisPolygon(surface.SurfaceNumber, out var poly) || poly.Count < 3)
                continue;
            usedSurfacePerimeters = true;
            int nv = poly.Count;
            for (int i = 0; i < nv; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % nv];
                SubdivideLinear(a, b, treatAsExcavationBoundary: true);
            }
        }

        if (!usedSurfacePerimeters)
        {
            int count = rows.Count;
            bool isClosed = count >= 3 && !g.HasAddedElevationLayer;
            for (int i = 0; i < count; i++)
            {
                int next = isClosed ? (i + 1) % count : i + 1;
                if (next >= count) break;
                var a = new Point(rows[i].AxisX, rows[i].AxisY);
                var b = new Point(rows[next].AxisX, rows[next].AxisY);
                SubdivideLinear(a, b, treatAsExcavationBoundary: true);
            }

            if (!noInternal)
            {
                foreach (var seg in g.GeometryLineSegments)
                {
                    if (seg.IsCircle) continue;
                    if (seg.IsArc)
                    {
                        if (!TryVertexAxis(seg.FromPoint, out var p1) ||
                            !TryVertexAxis(seg.ThroughPoint, out var p2) ||
                            !TryVertexAxis(seg.ToPoint, out var p3))
                            continue;
                        SubdivideArcAxis(p1, p2, p3, treatAsExcavationBoundary: true);
                    }
                    else
                    {
                        if (!TryVertexAxis(seg.FromPoint, out var pa) || !TryVertexAxis(seg.ToPoint, out var pb))
                            continue;
                        SubdivideLinear(pa, pb, treatAsExcavationBoundary: true);
                    }
                }
            }
            else
            {
                foreach (var seg in g.GeometryLineSegments)
                {
                    if (seg.IsCircle) continue;
                    if (seg.IsArc)
                    {
                        if (!TryVertexAxis(seg.FromPoint, out var p1) ||
                            !TryVertexAxis(seg.ThroughPoint, out var p2) ||
                            !TryVertexAxis(seg.ToPoint, out var p3))
                            continue;
                        SubdivideArcAxis(p1, p2, p3, treatAsExcavationBoundary: false);
                    }
                    else
                    {
                        if (!TryVertexAxis(seg.FromPoint, out var pa) || !TryVertexAxis(seg.ToPoint, out var pb))
                            continue;
                        SubdivideLinear(pa, pb, treatAsExcavationBoundary: false);
                    }
                }
            }
        }

        foreach (var (c, r) in circles)
            SampleFullCircle(c, r);

        // Material layer boundaries — horizontal chords at Geometry Definition cut elevations.
        var layerSegs = g.CollectMaterialLayerBoundarySegments();
        foreach (var (a, b) in layerSegs)
            SubdivideLinear(a, b, treatAsExcavationBoundary: true);

        // Inclined material interfaces — internal edges (L#) that are not horizontal layer-cut chords
        // (those are already added above). Endpoints resolve from stable point ids to axis coords.
        Point? StableAxis(int id)
        {
            var v = g.GeometryVertexRows.FirstOrDefault(r => r.StablePointId == id);
            return v == null ? (Point?)null : new Point(v.AxisX, v.AxisY);
        }
        const double horizEps = 1e-6;
        foreach (var ie in g.GeometryInternalEdges)
        {
            var pa = StableAxis(ie.IsArc ? ie.StableStart : ie.StableMin);
            var pb = StableAxis(ie.IsArc ? ie.StableEnd : ie.StableMax);
            if (pa is not Point ea || pb is not Point eb) continue;
            if (Math.Abs(ea.Y - eb.Y) <= horizEps) continue; // horizontal cuts handled above
            if (ie.IsArc && StableAxis(ie.StableMid) is Point em)
                SubdivideArcAxis(ea, em, eb, treatAsExcavationBoundary: true);
            else
                SubdivideLinear(ea, eb, treatAsExcavationBoundary: true);
        }

        // Non-horizontal material layers drawn directly on the Material Properties canvas are stored
        // as standalone polyline cuts (separate from internal edges). Sample nodes along each segment.
        foreach (var polyline in g.GetNonHorizontalLayerCutPolylines())
        {
            for (var i = 0; i + 1 < polyline.Count; i++)
                SubdivideLinear(polyline[i], polyline[i + 1], treatAsExcavationBoundary: true);
        }

        var meshPolys = new List<IReadOnlyList<Point>>();
        foreach (var row in SurfaceMeshRows.Where(r => r.WillBeMeshed))
        {
            if (g.TryGetSurfaceAxisPolygon(row.SurfaceNumber, out var poly) && poly.Count >= 3)
                meshPolys.Add(poly);
        }
        foreach (var (ja, jb) in FeJointConstraintCollector.Collect(g.GeologicalFeatures, meshPolys, hNear))
            SubdivideLinear(ja, jb, treatAsExcavationBoundary: true);

        if (result.Count == 0)
            error = "No discretization points were generated.";
        _rs2dBoundarySegments = boundarySegments;
        return result;
    }

    private static double Rs2dNormAngle(double a)
    {
        while (a < 0) a += 2 * Math.PI;
        while (a > 2 * Math.PI) a -= 2 * Math.PI;
        return a;
    }

    private static bool Rs2dTryCircleFromThreePoints(Point p1, Point p2, Point p3, out double cx, out double cy, out double r)
    {
        cx = cy = r = 0;
        double ax = p2.X - p1.X, ay = p2.Y - p1.Y;
        double bx = p3.X - p1.X, by = p3.Y - p1.Y;
        double d = 2 * (ax * by - ay * bx);
        if (Math.Abs(d) < 1e-10) return false;
        double ux = (by * (ax * ax + ay * ay) - ay * (bx * bx + by * by)) / d;
        double uy = (ax * (bx * bx + by * by) - bx * (ax * ax + ay * ay)) / d;
        cx = p1.X + ux;
        cy = p1.Y + uy;
        r = Math.Sqrt(ux * ux + uy * uy);
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
