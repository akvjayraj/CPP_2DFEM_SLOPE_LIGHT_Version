using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.Services;

namespace GeotechSlopeApp.ViewModels;

public class FEAnalysisViewModel : INotifyPropertyChanged
{
    private readonly CppAnalysisRunner _runner = new();
    private readonly ResultsViewModel _resultsVm;
    private MeshViewModel? _meshSource;
    private BoundaryConditionsViewModel? _boundaryConditionsSource;
    private MaterialPropertiesViewModel? _materialProperties;
    private ScriptViewModel? _scriptVm;
    private MainWindowViewModel? _mainWindow;
    private IProjectUndoHost? _undoHost;

    public void SetUndoHost(IProjectUndoHost? host) => _undoHost = host;
    private void RecordUndo(string description) => _undoHost?.RecordUndoPoint(description);

    /// <summary>Material Properties → Analysis matches this: run p41 with Script Editor .dat.</summary>
    public const string StructuralStaticElasticityCategory = "Structural – static elasticity";

    /// <summary>Material Properties → Type matches this: run p41 with Script Editor .dat.</summary>
    public const string OneDAxialRodType = "1D axial rod";

    /// <summary>Material Properties → Type (2D structural): Script Editor / p51 use type_2d + 'Plane' header lines.</summary>
    public const string PlaneStrainDetailedType = "Plane strain";
    private bool _isSolving;
    private CancellationTokenSource? _analysisCts;
    private readonly RelayCommand _stopAnalysisCommand;
    private string _statusMessage = "Load mesh data or run analysis.";
    private List<Node> _nodes = new();
    private List<ElementBase> _elements = new();
    private List<object> _boundaryConditions = new();
    private bool _stagedExcavationEnabled;
    private ObservableCollection<StageDefinition> _stageDefinitions = new();
    private readonly HashSet<StageDefinition> _wiredStageDefinitions = new();
    private StageDefinition? _selectedStageDefinition;
    private bool _gravityEnabled = true;
    private double _gravityAcceleration = 9.81;
    private bool _loadSteppingEnabled;
    private int _numberOfLoadSteps = 1;
    // Solver control block (p610_t3 .dat: tol  limit  incs  layers)
    private double _convergenceTolerance = 1.0e-5;
    private int _maxIterations = SrfExplicitDeckFreshness.DefaultSrfIterationLimit;
    private int _loadIncrements = 5;
    private int? _layersOverride = null;
    // SRF solver parameters (slope stability: tol  limit  nsrf + F list)
    private int _srfTrialCount = 3;
    private readonly ObservableCollection<SrfTrialEntry> _srfValues = new();
    // SRF bracket search parameters (alternative to the fixed nsrf/F-list sweep above)
    private SrfSearchMode _selectedSrfSearchMode = SrfSearchMode.Sweep;
    private SrfInsituMode _selectedSrfInsituMode = SrfInsituMode.Auto;
    private bool _autoDetectSrfBracket = true;
    private double _srfBracketSeed = 1.0;
    private double _srfBracketExpansionFactor = 1.5;
    private int _srfMaxExpansionSteps = 10;
    private double _srfFoSMin = 1.0;
    private double _srfFoSMax = 1.5;
    private double _srfBracketTolerance = 0.01;
    private int _srfMaxBracketIterations = 20;
    private double? _lastDetectedSrfFoSMin;
    private double? _lastDetectedSrfFoSMax;
    private object? _verificationResult;
    private List<object> _availableStagesForRun = new();
    private object? _selectedStageForRun;
    private bool _canRunSelectedStage;
    private string _lastExecutionStatus = "Not run";
    private string _lastTotalTime = "";
    private int _lastElementCount;
    private int _lastNodeCount;
    private double _runPseudoStaticKh;
    private double _runPseudoStaticKv;

    /// <summary>k_h from Script Editor / Load Conditions for the upcoming solver run.</summary>
    public double RunPseudoStaticKh
    {
        get => _runPseudoStaticKh;
        private set { _runPseudoStaticKh = value; OnPropertyChanged(); NotifyRunEnvironmentDisplayChanged(); }
    }

    /// <summary>k_v from Script Editor / Load Conditions for the upcoming solver run.</summary>
    public double RunPseudoStaticKv
    {
        get => _runPseudoStaticKv;
        private set { _runPseudoStaticKv = value; OnPropertyChanged(); NotifyRunEnvironmentDisplayChanged(); }
    }

    public bool HasRunPseudoStatic =>
        Math.Abs(_runPseudoStaticKh) > 1e-12 || Math.Abs(_runPseudoStaticKv) > 1e-12;

    public bool IsSeismicEnabledForRun =>
        _mainWindow?.LoadConditionsVM?.IsSeismicLoadUiEnabled == true;

    public string SeismicRunSummary =>
        IsSeismicEnabledForRun ? $"Seismic: {SeismicRunStatus}" : "Seismic: Off";

    /// <summary>Seismic status without the "Seismic:" label (for Execution Details grid).</summary>
    public string SeismicRunStatus
    {
        get
        {
            if (!IsSeismicEnabledForRun)
                return "Off";
            if (HasRunPseudoStatic)
                return $"On — k_h = {RunPseudoStaticKh:G4}, k_v = {RunPseudoStaticKv:G4}";
            return "On";
        }
    }

    /// <summary>Legacy alias for <see cref="SeismicRunSummary"/>.</summary>
    public string RunPseudoStaticSummary => SeismicRunSummary;

    public bool IsGroundwaterEnabledForRun =>
        _mainWindow?.GroundwaterVM?.IsEnabled == true;

    public string GroundwaterRunStatus
    {
        get
        {
            if (!IsGroundwaterEnabledForRun)
                return "Off";
            var gw = _mainWindow!.GroundwaterVM;
            if (gw.IsRuMode && gw.Ru > 0)
                return $"On — Ru = {gw.Ru:F2}";
            if (gw.IsBbarMode && gw.Bbar > 0)
                return $"On — B-bar = {gw.Bbar:F2}";
            var unit = string.Equals(_mainWindow?.ProjectSettingsVM?.Project?.Units, "Imperial",
                StringComparison.OrdinalIgnoreCase)
                ? "pcf"
                : "kN/m³";
            var poreGridPts = gw.WaterPressureGrids?
                .Where(g => g.GridType == WaterPressureGridType.PorePressure && g.Points != null)
                .Sum(g => g.Points.Count) ?? 0;
            if (poreGridPts <= 0)
                poreGridPts = gw.PorePressureGridPointCount;
            if (poreGridPts > 0)
            {
                var gridName = gw.WaterPressureGrids?
                    .FirstOrDefault(g => g.GridType == WaterPressureGridType.PorePressure
                                         && g.Points is { Count: > 0 })?.Name;
                var label = string.IsNullOrWhiteSpace(gridName) ? "pore-pressure grid" : gridName;
                return $"On — {label}, {poreGridPts} pts, γ_w = {gw.GammaW:G2} {unit}";
            }
            var n = gw.GroundwaterSurfPoints?.Count ?? 0;
            if (n > 0)
                return $"On — hydrostatic, γ_w = {gw.GammaW:G2} {unit}, {n} phreatic point(s)";
            return $"On — hydrostatic, γ_w = {gw.GammaW:G2} {unit} (no surface defined)";
        }
    }

    /// <summary>Project Settings → Flow-Seepage/Groundwater detailed type label for Overview.</summary>
    private string? GroundwaterSelectedMethodLabel
    {
        get
        {
            var cat = _materialProperties?.SelectedAnalysisCategory;
            if (!MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(cat))
                return null;
            var type = MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(
                _materialProperties?.SelectedDetailedAnalysisType);
            return string.IsNullOrWhiteSpace(type) ? null : type;
        }
    }

    public string GroundwaterRunSummary
    {
        get
        {
            var method = GroundwaterSelectedMethodLabel;
            var ps = _mainWindow?.ProjectSettingsVM;
            bool staticSelected = ps?.IsGroundwaterStaticSelected == true
                || string.Equals(method, MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal);

            if (!IsGroundwaterEnabledForRun)
            {
                if (staticSelected)
                    return "Groundwater: Off — method: Static (enable Include Groundwater to apply)";
                if (method != null)
                    return $"Groundwater: Off — method: {method}";
                return "Groundwater: Off";
            }

            if (UseCoupledSeepageSrf)
            {
                var seepageType = ResolveCoupledSeepageSolverType();
                var prog = SeepageDatContentBuilder.ResolveProgramId(seepageType) ?? "?";
                var coupled = $"Groundwater: Coupled Steady State ({seepageType} / {prog}) → SRF pore pressures — {GroundwaterRunStatus}";
                if (_mainWindow?.GroundwaterVM != null)
                    coupled += $" | {SrfGroundwaterExport.DescribeExportStatus(_mainWindow.GroundwaterVM.BuildModel())}";
                return coupled;
            }

            if (UseCoupledStaticSrf)
            {
                var coupled = $"Groundwater: Coupled Static → SRF pore pressures — {GroundwaterRunStatus}";
                if (_mainWindow?.GroundwaterVM != null)
                    coupled += $" | {SrfGroundwaterExport.DescribeExportStatus(_mainWindow.GroundwaterVM.BuildModel())}";
                return coupled;
            }

            // Lead with the Project Settings method (Static / seepage type).
            var summary = staticSelected
                ? $"Groundwater: Static — {GroundwaterRunStatus}"
                : method != null
                    ? $"Groundwater: {method} — {GroundwaterRunStatus}"
                    : $"Groundwater: {GroundwaterRunStatus}";

            if (staticSelected && ps != null)
            {
                var s = ps.GroundwaterStaticSettings;
                var parts = new List<string>();
                if (s.IncludeWaterSurfaces) parts.Add("Water Surfaces");
                if (s.IncludeWaterSurfaces && s.IncludePondedFreeWater) parts.Add("Ponded");
                if (s.IncludeRuCoefficients) parts.Add("Ru");
                var grids = new List<string>();
                if (s.IncludeWaterPressureGridTotalHead) grids.Add("H");
                if (s.IncludeWaterPressureGridPressureHead) grids.Add("h_p");
                if (s.IncludeWaterPressureGridPorePressure) grids.Add("u");
                if (parts.Count > 0)
                    summary += $" | {string.Join(", ", parts)}";
                else
                    summary += " | (no Static components checked)";
                if (grids.Count > 0)
                    summary += $" | grids: {string.Join(", ", grids)}";
            }
            else if (ps?.IsSeepageAnalysisSelected == true
                     || string.Equals(
                         _materialProperties?.LastGroundwaterTreeMode,
                         MaterialPropertiesViewModel.GroundwaterSteadyStateType,
                         StringComparison.Ordinal))
            {
                var seepage = ps?.SeepageSettings;
                var type = ResolveCoupledSeepageSolverType();
                summary += $" | Steady State: {type}";
                if (ps?.IsActiveSeepageWaterHeadEnabled == true)
                    summary += " | Water Head";
                else if (seepage != null
                         && (seepage.IncludeWaterHeadPlaneAxisym
                             || seepage.IncludeWaterHeadFreeSurface
                             || seepage.IncludeWaterHeadGeneral2D
                             || seepage.IncludeWaterHeadGeneral3D))
                    summary += " | Water Head (inactive solver)";
            }

            // When SRF category is ON with Groundwater, show whether export will produce u_kPa.
            bool srfCategoryOn = ps?.IsCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory) == true
                || UseSrfFromProjectSettings;
            if (srfCategoryOn && _mainWindow?.GroundwaterVM != null)
            {
                var exportModel = _mainWindow.GroundwaterVM.BuildModel();
                summary += $" | {SrfGroundwaterExport.DescribeExportStatus(exportModel)}";
            }

            return summary;
        }
    }

    /// <summary>True when Load Conditions has at least one enabled point/line/triangular load.</summary>
    public bool HasLoadsForRun =>
        _mainWindow?.LoadConditionsVM?.Loads.Any(l => l.IsEnabled) == true;

    /// <summary>Load values from the Load Conditions popup (for Execution Details).</summary>
    public string LoadsRunStatus
    {
        get
        {
            var loads = _mainWindow?.LoadConditionsVM?.Loads
                .Where(l => l.IsEnabled)
                .ToList();
            if (loads == null || loads.Count == 0)
                return "None";

            const int maxShown = 4;
            var parts = loads.Take(maxShown).Select(FormatLoadForRunSummary);
            var text = string.Join("; ", parts);
            if (loads.Count > maxShown)
                text += $"; +{loads.Count - maxShown} more";
            return text;
        }
    }

    /// <summary>Overview line: loads defined in the Load Conditions popup, with magnitudes.</summary>
    public string LoadsRunSummary =>
        HasLoadsForRun
            ? $"Loads (Load Conditions): {LoadsRunStatus}"
            : "Loads (Load Conditions): None";

    private string FormatLoadForRunSummary(LoadModel load)
    {
        var name = string.IsNullOrWhiteSpace(load.Name) ? null : load.Name.Trim();
        string kindAndMag = load.Type switch
        {
            LoadType.Point =>
                $"{name ?? "Point"} {load.Magnitude:G4} {load.MagnitudeUnit}",
            LoadType.Triangular =>
                $"{name ?? "Triangular"} {load.Magnitude:G4}→{load.EndMagnitude:G4} {load.MagnitudeUnit}",
            _ =>
                $"{name ?? "Line"} {load.Magnitude:G4} {load.MagnitudeUnit}",
        };

        // LoadModel stores canvas pixels; Overview / Execution Details must show axis metres.
        var geom = _mainWindow?.GeometryDefinitionVM
                   ?? _mainWindow?.LoadConditionsVM?.GeometryDefinition;
        string where;
        if (load.IsPointLoad && load.AppliedToNodeId is { } nid)
        {
            where = $"@ node {nid}";
        }
        else if (geom != null)
        {
            var a = geom.ConvertCanvasToAxis(new Point(load.StartX, load.StartY));
            if (load.IsPointLoad)
            {
                where = $"@ ({a.X:G4}, {a.Y:G4}) m";
            }
            else
            {
                var b = geom.ConvertCanvasToAxis(new Point(load.EndX, load.EndY));
                where = $"({a.X:G4}, {a.Y:G4})→({b.X:G4}, {b.Y:G4}) m";
            }
        }
        else
        {
            where = load.IsPointLoad
                ? $"@ ({load.StartX:G4}, {load.StartY:G4})"
                : $"({load.StartX:G4}, {load.StartY:G4})→({load.EndX:G4}, {load.EndY:G4})";
        }

        return $"{kindAndMag} ({load.OrientationSummary}) {where}";
    }

    /// <summary>Suffix appended to the mesh stats line, e.g. ", k_h = 0.1, k_v = 0.05".</summary>
    public string MeshPseudoStaticInline =>
        HasRunPseudoStatic
            ? $", k_h = {RunPseudoStaticKh:G4}, k_v = {RunPseudoStaticKv:G4}"
            : string.Empty;

    /// <summary>Mesh counts for the Overview header (pseudo-static shown via <see cref="MeshPseudoStaticInline"/>).</summary>
    public string MeshStatsBaseLine =>
        $"Nodes: {Nodes.Count}, Elements: {Elements.Count}, BCs: {BoundaryConditions.Count}";

    /// <summary>Mesh type that Run FE Analysis will use (T3/T6/Q4/Q8, …).</summary>
    public string ResolvedMeshTypeForRun
    {
        get => _resolvedMeshTypeForRun;
        private set
        {
            if (string.Equals(_resolvedMeshTypeForRun, value, StringComparison.Ordinal))
                return;
            _resolvedMeshTypeForRun = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Branded/process solver executable selected for the next Run FE Analysis.</summary>
    public string ResolvedSolverForRun
    {
        get => _resolvedSolverForRun;
        private set
        {
            if (string.Equals(_resolvedSolverForRun, value, StringComparison.Ordinal))
                return;
            _resolvedSolverForRun = value;
            OnPropertyChanged();
        }
    }

    private string _resolvedMeshTypeForRun = "n/a";
    private string _resolvedSolverForRun = "n/a";

    public void RefreshResolvedRunRoutingDisplay()
    {
        try
        {
            if (_materialProperties is not { Is2DProblem: true })
            {
                ResolvedMeshTypeForRun = "n/a";
                ResolvedSolverForRun = "n/a";
                return;
            }

            if (UseCoupledSeepageSrf)
            {
                ResolvedMeshTypeForRun = ResolveActiveMeshTypeLabel();
                var seepageType = ResolveCoupledSeepageSolverType();
                var seepageProg = SeepageDatContentBuilder.ResolveProgramId(seepageType) ?? "?";
                var optionsCoupled = P610AnalysisOptionsHelper.BuildFromViewModels(_materialProperties, _meshSource);
                var meshTypeCoupled = P610AnalysisOptionsHelper.ResolveMeshType(_meshSource);
                var routeCoupled = SrfLiteratureOptimalRouting.Resolve(meshTypeCoupled, optionsCoupled.FailureCriterion, IsSrfAxisymmetric);
                var srfSolver = string.Equals(routeCoupled.Executable, routeCoupled.ProcessExecutable, StringComparison.OrdinalIgnoreCase)
                    ? routeCoupled.Executable
                    : $"{routeCoupled.Executable} (runs {routeCoupled.ProcessExecutable})";
                ResolvedSolverForRun = $"fem_cpp ({seepageProg}) → {srfSolver}";
                return;
            }

            if (UseCoupledStaticSrf)
            {
                ResolvedMeshTypeForRun = ResolveActiveMeshTypeLabel();
                var optionsStatic = P610AnalysisOptionsHelper.BuildFromViewModels(_materialProperties, _meshSource);
                var meshTypeStatic = P610AnalysisOptionsHelper.ResolveMeshType(_meshSource);
                var routeStatic = SrfLiteratureOptimalRouting.Resolve(meshTypeStatic, optionsStatic.FailureCriterion, IsSrfAxisymmetric);
                ResolvedSolverForRun = string.Equals(routeStatic.Executable, routeStatic.ProcessExecutable, StringComparison.OrdinalIgnoreCase)
                    ? routeStatic.Executable
                    : $"{routeStatic.Executable} (runs {routeStatic.ProcessExecutable})";
                return;
            }

            if (UseSeepageFromProjectSettings)
            {
                ResolvedMeshTypeForRun = ResolveActiveMeshTypeLabel();
                var seepageProg = SeepageDatContentBuilder.ResolveProgramId(
                    _materialProperties?.SelectedDetailedAnalysisType);
                ResolvedSolverForRun = seepageProg != null ? $"fem_cpp ({seepageProg})" : "fem_cpp";
                return;
            }

            // Static GW keeps the structural/slope solver path; mesh label still comes from the active mesh.
            if (UseGroundwaterStaticFromProjectSettings)
                ResolvedMeshTypeForRun = ResolveActiveMeshTypeLabel();

            if (UseP41FromScriptDat)
            {
                ResolvedMeshTypeForRun = "1D rod";
                ResolvedSolverForRun = "p41";
                return;
            }

            // Same order as OnRunAnalysis: p610 family (including p610_t3_elastic) wins over
            // the legacy p51 plane-strain path so the monitor/log match the launched exe.
            if (UseP610T3FromScriptDat)
            {
                var options = P610AnalysisOptionsHelper.BuildFromViewModels(_materialProperties, _meshSource);
                var meshType = P610AnalysisOptionsHelper.ResolveMeshType(_meshSource);
                ResolvedMeshTypeForRun = string.IsNullOrWhiteSpace(meshType) ? "n/a" : meshType;

                if (UseSrfFromProjectSettings)
                {
                    var route = SrfLiteratureOptimalRouting.Resolve(meshType, options.FailureCriterion, IsSrfAxisymmetric);
                    ResolvedSolverForRun = string.Equals(route.Executable, route.ProcessExecutable, StringComparison.OrdinalIgnoreCase)
                        ? route.Executable
                        : $"{route.Executable} (runs {route.ProcessExecutable})";
                    return;
                }

                ResolvedSolverForRun = ResolveStructuralP610ProgramName();
                return;
            }

            if (UseP51FromScriptDat)
            {
                ResolvedMeshTypeForRun = ResolveActiveMeshTypeLabel();
                ResolvedSolverForRun = "p51";
                return;
            }

            ResolvedMeshTypeForRun = ResolveActiveMeshTypeLabel();
            ResolvedSolverForRun = "n/a";
        }
        finally
        {
            NotifyActiveAnalysisModeBindings();
        }
    }

    private void NotifyActiveAnalysisModeBindings()
    {
        OnPropertyChanged(nameof(AnalysisType));
        OnPropertyChanged(nameof(ActiveAnalysisModeSummary));
        OnPropertyChanged(nameof(UseCoupledGroundwaterSrf));
        OnPropertyChanged(nameof(UseCoupledSeepageSrf));
        OnPropertyChanged(nameof(UseCoupledStaticSrf));
        OnPropertyChanged(nameof(IsCoupledSteadyGroundwaterMode));
        OnPropertyChanged(nameof(IsCoupledStaticGroundwaterMode));
        OnPropertyChanged(nameof(IsCoupledTransientGroundwaterMode));
        OnPropertyChanged(nameof(UseSeepageFromProjectSettings));
        OnPropertyChanged(nameof(UseTransientFromProjectSettings));
        OnPropertyChanged(nameof(UseGroundwaterStaticFromProjectSettings));
        OnPropertyChanged(nameof(GroundwaterRunSummary));
        OnPropertyChanged(nameof(ResolvedSrfInsituModeToken));
        OnPropertyChanged(nameof(SrfInsituModeSummary));
        OnPropertyChanged(nameof(HasMohrCoulombJointSlip));
        OnPropertyChanged(nameof(HasEnabledSurfaceOrLineLoad));
        OnPropertyChanged(nameof(UseP610T3ElasticFromScriptDat));
        OnPropertyChanged(nameof(UseP610T3FromScriptDat));
    }

    public string ResolveActiveMeshTypeForStatus() => ResolveActiveMeshTypeLabel();

    private string ResolveActiveMeshTypeLabel()
    {
        if (_meshSource is { IsDiscretized: true, MeshInformationRows.Count: > 0 })
            return P610AnalysisOptionsHelper.ResolveMeshType(_meshSource);
        return _meshSource?.UserDefinedMeshVM?.ElementType ?? "Plane strain";
    }

    public void RefreshRunPseudoStaticDisplay()
    {
        double kh = 0, kv = 0;
        bool found = false;

        var loads = _mainWindow?.LoadConditionsVM;
        if (loads?.IsSeismicLoadUiEnabled == true)
        {
            (kh, kv) = PseudoStaticSeismicRunHelper.ResolveCoefficientsForScriptEditor(this, loads);
            found = Math.Abs(kh) > 1e-12 || Math.Abs(kv) > 1e-12;
        }

        RunPseudoStaticKh = found ? kh : 0;
        RunPseudoStaticKv = found ? kv : 0;
        NotifyRunEnvironmentDisplayChanged();
    }

    private void NotifyRunEnvironmentDisplayChanged()
    {
        OnPropertyChanged(nameof(HasRunPseudoStatic));
        OnPropertyChanged(nameof(IsSeismicEnabledForRun));
        OnPropertyChanged(nameof(SeismicRunStatus));
        OnPropertyChanged(nameof(SeismicRunSummary));
        OnPropertyChanged(nameof(RunPseudoStaticSummary));
        OnPropertyChanged(nameof(IsGroundwaterEnabledForRun));
        OnPropertyChanged(nameof(GroundwaterRunStatus));
        OnPropertyChanged(nameof(GroundwaterRunSummary));
        OnPropertyChanged(nameof(HasLoadsForRun));
        OnPropertyChanged(nameof(LoadsRunStatus));
        OnPropertyChanged(nameof(LoadsRunSummary));
        OnPropertyChanged(nameof(MeshPseudoStaticInline));
        OnPropertyChanged(nameof(MeshStatsBaseLine));
    }

    /// <summary>Supply the Mesh tab ViewModel so Load Mesh Data can import discretization.</summary>
    public void SetMeshSource(MeshViewModel? meshViewModel)
    {
        if (_meshSource?.UserDefinedMeshVM != null)
            _meshSource.UserDefinedMeshVM.PropertyChanged -= OnUserDefinedMeshForFeRoutingChanged;
        _meshSource = meshViewModel;
        if (_meshSource?.UserDefinedMeshVM != null)
            _meshSource.UserDefinedMeshVM.PropertyChanged += OnUserDefinedMeshForFeRoutingChanged;
        RefreshResolvedRunRoutingDisplay();
    }

    private void OnUserDefinedMeshForFeRoutingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UserDefinedMeshViewModel.ElementType))
            RefreshResolvedRunRoutingDisplay();
    }

    /// <summary>
    /// Supply the Boundary Conditions tab ViewModel so Load Mesh Data also imports the
    /// rows defined there (Fixed / Roller / Prescribed / Force) into this tab. Rows are
    /// filtered to nodes present in the loaded mesh to avoid stale references.
    /// </summary>
    public void SetBoundaryConditionsSource(BoundaryConditionsViewModel? bcViewModel)
        => _boundaryConditionsSource = bcViewModel;

    /// <summary>Links Material Properties (analysis type) and Script Editor (.dat) for Run FE Analysis routing.</summary>
    public void SetAnalysisContext(MaterialPropertiesViewModel? materialProperties, ScriptViewModel? scriptVm)
    {
        if (_materialProperties != null)
            _materialProperties.PropertyChanged -= OnMaterialPropertiesForFeRoutingChanged;
        if (_scriptVm != null)
            _scriptVm.PropertyChanged -= OnScriptVmPropertyChanged;
        _materialProperties = materialProperties;
        _scriptVm = scriptVm;
        if (_materialProperties != null)
            _materialProperties.PropertyChanged += OnMaterialPropertiesForFeRoutingChanged;
        if (_scriptVm != null)
            _scriptVm.PropertyChanged += OnScriptVmPropertyChanged;
        RefreshRunPseudoStaticDisplay();
        RefreshResolvedRunRoutingDisplay();
    }

    public void SetMainWindow(MainWindowViewModel mainWindow)
    {
        if (_mainWindow?.LoadConditionsVM != null)
        {
            _mainWindow.LoadConditionsVM.PseudoStaticCoefficientsChanged -= OnPseudoStaticCoefficientsChanged;
            _mainWindow.LoadConditionsVM.AppliedLoadsChanged -= OnAppliedLoadsChanged;
            _mainWindow.LoadConditionsVM.PropertyChanged -= OnLoadConditionsVmPropertyChanged;
        }
        if (_mainWindow?.GroundwaterVM != null)
            _mainWindow.GroundwaterVM.PropertyChanged -= OnGroundwaterVmPropertyChanged;
        if (_mainWindow?.ProjectSettingsVM != null)
            _mainWindow.ProjectSettingsVM.PropertyChanged -= OnProjectSettingsForFeRoutingChanged;
        DetachGeometryJointRouting(_mainWindow);
        _mainWindow = mainWindow;
        if (_mainWindow?.LoadConditionsVM != null)
        {
            _mainWindow.LoadConditionsVM.PseudoStaticCoefficientsChanged += OnPseudoStaticCoefficientsChanged;
            _mainWindow.LoadConditionsVM.AppliedLoadsChanged += OnAppliedLoadsChanged;
            _mainWindow.LoadConditionsVM.PropertyChanged += OnLoadConditionsVmPropertyChanged;
        }
        if (_mainWindow?.GroundwaterVM != null)
            _mainWindow.GroundwaterVM.PropertyChanged += OnGroundwaterVmPropertyChanged;
        if (_mainWindow?.ProjectSettingsVM != null)
            _mainWindow.ProjectSettingsVM.PropertyChanged += OnProjectSettingsForFeRoutingChanged;
        AttachGeometryJointRouting(_mainWindow);
        RefreshRunPseudoStaticDisplay();
        RefreshResolvedRunRoutingDisplay();
    }

    private void DetachGeometryJointRouting(MainWindowViewModel? main)
    {
        if (main?.GeometryDefinitionVM == null)
            return;
        main.GeometryDefinitionVM.GeologicalFeatures.CollectionChanged -= OnJointsForFeRoutingChanged;
        main.GeometryDefinitionVM.JointPropertySets.CollectionChanged -= OnJointsForFeRoutingChanged;
    }

    private void AttachGeometryJointRouting(MainWindowViewModel? main)
    {
        if (main?.GeometryDefinitionVM == null)
            return;
        main.GeometryDefinitionVM.GeologicalFeatures.CollectionChanged += OnJointsForFeRoutingChanged;
        main.GeometryDefinitionVM.JointPropertySets.CollectionChanged += OnJointsForFeRoutingChanged;
    }

    private void OnJointsForFeRoutingChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshResolvedRunRoutingDisplay();

    private void OnProjectSettingsForFeRoutingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectSettingsViewModel.IsActiveAnalysisCategoryOn)
            or nameof(ProjectSettingsViewModel.AreSelectedCategoryTypeOptionsEnabled)
            or nameof(ProjectSettingsViewModel.IsGroundwaterStaticSelected)
            or nameof(ProjectSettingsViewModel.GroundwaterStaticSettings)
            or nameof(ProjectSettingsViewModel.SrfAxisymmetric)
            or null or "")
        {
            OnPropertyChanged(nameof(IsActiveAnalysisCategoryEnabled));
            OnPropertyChanged(nameof(UseSrfFromProjectSettings));
            OnPropertyChanged(nameof(IsSrfAxisymmetric));
            RefreshResolvedRunRoutingDisplay();
            OnPropertyChanged(nameof(UseCoupledGroundwaterSrf));
            OnPropertyChanged(nameof(UseCoupledSeepageSrf));
            OnPropertyChanged(nameof(UseCoupledStaticSrf));
            OnPropertyChanged(nameof(IsCoupledSteadyGroundwaterMode));
            OnPropertyChanged(nameof(IsCoupledStaticGroundwaterMode));
            OnPropertyChanged(nameof(IsCoupledTransientGroundwaterMode));
            OnPropertyChanged(nameof(UseSeepageFromProjectSettings));
            OnPropertyChanged(nameof(UseGroundwaterStaticFromProjectSettings));
            OnPropertyChanged(nameof(SolverControlVisibility));
            OnPropertyChanged(nameof(IsSrfSolverMode));
            OnPropertyChanged(nameof(ResolvedSrfInsituModeToken));
            OnPropertyChanged(nameof(SrfInsituModeSummary));
            RefreshResolvedRunRoutingDisplay();
            NotifyRunEnvironmentDisplayChanged();
        }
    }

    private void OnScriptVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScriptViewModel.ScriptContent))
            RefreshRunPseudoStaticDisplay();
    }

    private void OnPseudoStaticCoefficientsChanged(object? sender, EventArgs e)
        => RefreshRunPseudoStaticDisplay();

    private void OnAppliedLoadsChanged(object? sender, EventArgs e)
    {
        RefreshResolvedRunRoutingDisplay();
        NotifyRunEnvironmentDisplayChanged();
    }

    private void OnLoadConditionsVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LoadConditionsViewModel.SeismicAnalysisEnabled)
            or nameof(LoadConditionsViewModel.IsSeismicLoadUiEnabled))
            RefreshRunPseudoStaticDisplay();
    }

    private void OnGroundwaterVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GroundwaterViewModel.IsEnabled)
            or nameof(GroundwaterViewModel.GammaW)
            or nameof(GroundwaterViewModel.Method)
            or nameof(GroundwaterViewModel.Ru)
            or nameof(GroundwaterViewModel.IsRuMode)
            or nameof(GroundwaterViewModel.IsBbarMode)
            or nameof(GroundwaterViewModel.Bbar)
            or nameof(GroundwaterViewModel.GroundwaterSurfPoints))
        {
            RefreshRunPseudoStaticDisplay();
            if (e.PropertyName == nameof(GroundwaterViewModel.IsEnabled))
                RefreshResolvedRunRoutingDisplay();
        }
    }

    private void ApplyNewmarkPostProcess(double? srf)
    {
        var settings = _mainWindow?.LoadConditionsVM?.Newmark;
        if (settings == null)
            return;
        _resultsVm.ApplyNewmarkResult(settings, srf);
    }

    private void NavigateToResultsVisualization()
    {
        if (_mainWindow != null)
            _mainWindow.SelectedTabIndex = 9;
        // Force-Displacement hosts SRF curves and transient time-history charts.
        _resultsVm.ResultsSubTabIndex =
            _resultsVm.HasSrfConvergencePlot || _resultsVm.HasTransientResults ? 2 : 1;
        if (_resultsVm.HasPseudoStaticResults)
            _resultsVm.VisualizationMode = ResultsViewModel.VisualizationModeVector;
    }

    private void OnMaterialPropertiesForFeRoutingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaterialPropertiesViewModel.SelectedAnalysisCategory)
            or nameof(MaterialPropertiesViewModel.SelectedDetailedAnalysisType)
            or nameof(MaterialPropertiesViewModel.LastGroundwaterTreeMode)
            or nameof(MaterialPropertiesViewModel.LastSteadyStateSolverType)
            or nameof(MaterialPropertiesViewModel.LastTransientSolverType)
            or nameof(MaterialPropertiesViewModel.Is2DProblem)
            or nameof(MaterialPropertiesViewModel.IsSrfAnalysisMode)
            or nameof(MaterialPropertiesViewModel.IsSrfReadyForRun)
            or nameof(MaterialPropertiesViewModel.SelectedFailureCriterion)
            or nameof(MaterialPropertiesViewModel.SelectedMaterialType))
        {
            OnPropertyChanged(nameof(SolverControlVisibility));
            OnPropertyChanged(nameof(IsSrfSolverMode));
            OnPropertyChanged(nameof(UseGroundwaterStaticFromProjectSettings));
            RefreshResolvedRunRoutingDisplay();
            NotifyRunEnvironmentDisplayChanged();
        }
    }

    /// <summary>True when Run FE Analysis should execute fem_cpp.exe (or p41.exe) with the Script Editor .dat content.</summary>
    public bool UseP41FromScriptDat =>
        _materialProperties?.SelectedAnalysisCategory == StructuralStaticElasticityCategory
        && _materialProperties?.SelectedDetailedAnalysisType == OneDAxialRodType;

    /// <summary>2D + Structural static elasticity + Plane strain: Script Editor and p51 use <c>type_2d</c> / <c>'Plane'</c> header lines.</summary>
    public static bool UseP51PlaneStrainScriptFormat(MaterialPropertiesViewModel? mat) =>
        mat is { Is2DProblem: true }
        && mat.SelectedAnalysisCategory == StructuralStaticElasticityCategory
        && mat.SelectedDetailedAnalysisType == PlaneStrainDetailedType;

    /// <summary>
    /// Run FE Analysis should execute p51.exe with Script Editor .dat (same as
    /// <see cref="UseP51PlaneStrainScriptFormat"/>). Mohr–Coulomb joint slip takes the
    /// p610_t3_elastoplastic path instead.
    /// </summary>
    public bool UseP51FromScriptDat =>
        UseP51PlaneStrainScriptFormat(_materialProperties) && !HasMohrCoulombJointSlip;

    /// <summary>Project Settings → Analysis is SRF (2D only) and the SRF category ON switch is checked.</summary>
    public bool UseSrfFromProjectSettings =>
        _materialProperties is { Is2DProblem: true, IsSrfAnalysisMode: true }
        && IsActiveAnalysisCategoryEnabled;

    /// <summary>
    /// SRF category ON and Flow-Seepage/Groundwater ON (2D): FE Analysis transfers pore pressure u into SRF.
    /// Branches to Static prescribed-u or Steady seepage→SRF via <see cref="UseCoupledStaticSrf"/> /
    /// <see cref="UseCoupledSeepageSrf"/>.
    /// </summary>
    public bool UseCoupledGroundwaterSrf =>
        _materialProperties is { Is2DProblem: true }
        && _mainWindow?.ProjectSettingsVM?.IsCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory) == true
        && _mainWindow?.GroundwaterVM?.IsEnabled == true;

    /// <summary>
    /// Last GW tree mode is Steady State (seepage coupling path).
    /// </summary>
    public bool IsCoupledSteadyGroundwaterMode =>
        string.Equals(
            _materialProperties?.LastGroundwaterTreeMode,
            MaterialPropertiesViewModel.GroundwaterSteadyStateType,
            StringComparison.Ordinal);

    /// <summary>
    /// Both SRF and Flow-Seepage ON, and last GW tree mode is Steady State:
    /// Run FE solves seepage, installs pore pressures, then runs SRF.
    /// </summary>
    public bool UseCoupledSeepageSrf =>
        UseCoupledGroundwaterSrf
        && IsCoupledSteadyGroundwaterMode
        && SeepageDatContentBuilder.ResolveProgramId(ResolveCoupledSeepageSolverType()) != null;

    /// <summary>Last GW tree mode is Static (prescribed-u coupling path).</summary>
    public bool IsCoupledStaticGroundwaterMode =>
        string.Equals(
            _materialProperties?.LastGroundwaterTreeMode,
            MaterialPropertiesViewModel.GroundwaterStaticType,
            StringComparison.Ordinal)
        || (string.IsNullOrEmpty(_materialProperties?.LastGroundwaterTreeMode)
            && !IsCoupledSteadyGroundwaterMode
            && !IsCoupledTransientGroundwaterMode);

    /// <summary>Last GW tree mode is Transient State (θ-method dissipation — not SRF coupling).</summary>
    public bool IsCoupledTransientGroundwaterMode =>
        string.Equals(
            _materialProperties?.LastGroundwaterTreeMode,
            MaterialPropertiesViewModel.GroundwaterTransientStateType,
            StringComparison.Ordinal)
        || MaterialPropertiesViewModel.IsTransientFlowAnalysisType(_materialProperties?.LastGroundwaterTreeMode);

    /// <summary>
    /// Both SRF and Flow-Seepage ON with Static (prescribed) groundwater:
    /// Run FE syncs WT/Ru/grids pore pressure u into the SRF deck, then runs SRF (no seepage solve).
    /// Transient last-leaf is excluded — that routes to p84/p88, not coupled Static SRF.
    /// </summary>
    public bool UseCoupledStaticSrf =>
        UseCoupledGroundwaterSrf && IsCoupledStaticGroundwaterMode;

    /// <summary>
    /// Flow-Seepage/Groundwater with one of the steady seepage types selected:
    /// Unconfined / free-surface flow (p73), General 2D (p74).
    /// Run FE Analysis builds a Chapter-7 seepage deck from the mesh and runs fem_cpp.
    /// Requires the Flow-Seepage/Groundwater category ON switch.
    /// </summary>
    public bool UseSeepageFromProjectSettings =>
        _materialProperties is { Is2DProblem: true }
        && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_materialProperties.SelectedAnalysisCategory)
        && MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(_materialProperties.SelectedDetailedAnalysisType)
        && IsActiveAnalysisCategoryEnabled
        && !UseCoupledSeepageSrf;

    /// <summary>
    /// Flow-Seepage/Groundwater with a Transient State solver (p84 / p88).
    /// Run FE Analysis builds a Chapter-8 consolidation deck and runs fem_cpp.
    /// </summary>
    public bool UseTransientFromProjectSettings =>
        _materialProperties is { Is2DProblem: true }
        && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_materialProperties.SelectedAnalysisCategory)
        && MaterialPropertiesViewModel.IsTransientFlowAnalysisType(_materialProperties.SelectedDetailedAnalysisType)
        && IsActiveAnalysisCategoryEnabled
        && !UseCoupledSeepageSrf;

    /// <summary>Steady seepage solver id used for coupled seepage → SRF (current or remembered).</summary>
    public string ResolveCoupledSeepageSolverType()
    {
        var current = MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(
            _materialProperties?.SelectedDetailedAnalysisType);
        if (MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(current))
            return current!;

        var remembered = MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(
            _materialProperties?.LastSteadyStateSolverType);
        if (MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(remembered))
            return remembered!;

        return _materialProperties?.Is3DProblem == true
            ? MaterialPropertiesViewModel.GroundwaterSeepageGeneral3DType
            : MaterialPropertiesViewModel.GroundwaterSeepageGeneralType;
    }

    /// <summary>
    /// Flow-Seepage/Groundwater → Static selected (prescribed pore pressure / phreatic / Ru / grids).
    /// Not a seepage flow solve; Overview shows method Static alongside structural FE routing.
    /// </summary>
    public bool UseGroundwaterStaticFromProjectSettings =>
        _materialProperties is { Is2DProblem: true }
        && MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(_materialProperties.SelectedAnalysisCategory)
        && string.Equals(
            MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(_materialProperties.SelectedDetailedAnalysisType),
            MaterialPropertiesViewModel.GroundwaterStaticType,
            StringComparison.Ordinal)
        && IsActiveAnalysisCategoryEnabled;

    /// <summary>False when Project Settings → Analysis has the active category's ON checkbox unchecked.</summary>
    public bool IsActiveAnalysisCategoryEnabled =>
        _mainWindow?.ProjectSettingsVM?.IsActiveAnalysisCategoryOn != false;

    /// <summary>
    /// True when an enabled Mohr–Coulomb joint (geological feature or generated interfaces)
    /// is in play. Structural runs then use <c>p610_t3_elastoplastic</c> for the Goodman slip loop.
    /// </summary>
    public bool HasMohrCoulombJointSlip =>
        _mainWindow?.GeometryDefinitionVM?.HasMohrCoulombJointSlip(_meshSource?.JointInterfaceElements) == true;

    /// <summary>
    /// Line or triangular surface loads on the Load Conditions canvas. Kirsch
    /// <c>p610_t3_elastic</c> ignores these; Static runs must use elastoplastic.
    /// </summary>
    public bool HasEnabledSurfaceOrLineLoad =>
        _mainWindow?.LoadConditionsVM?.Loads.Any(l => l.IsEnabled && l.IsDistributedLineLoad) == true;

    /// <summary>
    /// Material Properties → Failure criterion = Elastic AND Material type = Isotropic (the
    /// Add Materials defaults). When this matches <em>and field stress is the sole load</em>,
    /// Run FE Analysis runs p610_t3_elastic.exe (Kirsch / far-field). Any enabled line or
    /// surface load routes to <c>p610_t3_elastoplastic</c> instead (EL-01). Mohr–Coulomb
    /// joint slip also overrides this. The 1D-axial-rod path still takes precedence.
    /// </summary>
    public bool UseP610T3ElasticFromScriptDat =>
        !UseP41FromScriptDat
        && !HasMohrCoulombJointSlip
        && !HasEnabledSurfaceOrLineLoad
        && _materialProperties?.SelectedFailureCriterion == FailureCriterion.Elastic
        && _materialProperties?.SelectedMaterialType == MaterialType.Isotropic;

    /// <summary>
    /// True when FE Analysis should run the p610_t3 solver family from Script .dat
    /// using GUI-selected failure criterion/material type routing.
    /// </summary>
    public bool UseP610T3FromScriptDat =>
        !UseP41FromScriptDat &&
        !UseSeepageFromProjectSettings &&
        !UseTransientFromProjectSettings &&
        _materialProperties is { Is2DProblem: true } &&
        (UseSrfFromProjectSettings
         || _materialProperties.SelectedFailureCriterion != FailureCriterion.Elastic
         || _materialProperties.SelectedMaterialType != MaterialType.Isotropic
         || UseP610T3ElasticFromScriptDat
         || HasEnabledSurfaceOrLineLoad
         || HasMohrCoulombJointSlip);

    /// <summary>
    /// Structural (non-SRF) p610 executable. Mohr–Coulomb joint slip uses the Goodman
    /// interface loop in <c>p610_t3_elastoplastic</c> even when rock is Elastic / Isotropic.
    /// </summary>
    public string ResolveStructuralP610ProgramName()
    {
        if (HasMohrCoulombJointSlip)
            return "p610_t3_elastoplastic";
        if (UseP610T3ElasticFromScriptDat)
            return "p610_t3_elastic";
        if (HasEnabledSurfaceOrLineLoad
            && _materialProperties?.SelectedFailureCriterion == FailureCriterion.Elastic
            && _materialProperties?.SelectedMaterialType == MaterialType.Isotropic)
            return "p610_t3_elastoplastic";
        var options = P610AnalysisOptionsHelper.BuildFromViewModels(_materialProperties, _meshSource);
        return P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(
            options,
            _meshSource,
            srfAnalysisMode: false);
    }

    /// <summary>
    /// Full p610_t3 (elastoplastic) deck for Mohr–Coulomb joint slip, including the Joints block.
    /// Used so Run FE does not feed a leftover p51 / elastic Script Editor buffer to the solver.
    /// </summary>
    public string? TryBuildMohrCoulombJointP610Dat()
    {
        if (!HasMohrCoulombJointSlip)
            return null;
        if (_meshSource is not { MeshInformationRows.Count: > 0 })
            return null;
        if (_materialProperties == null || _boundaryConditionsSource == null)
            return null;
        return P610T3DatContentBuilder.Build(
            _meshSource,
            _materialProperties,
            _boundaryConditionsSource,
            StagedConstruction,
            this,
            _mainWindow?.LoadConditionsVM,
            _mainWindow?.GroundwaterVM,
            _mainWindow?.SupportVM);
    }

    /// <summary>Available stage type options (RS2D / Plaxis style).</summary>
    public static readonly IReadOnlyList<string> StageTypeOptions = new List<string>
    {
        "Initial Stress",
        "Gravity Turn-on",
        "Excavation",
        "Construction",
        "Loading",
        "Consolidation",
        "Groundwater Change",
        "Seismic / Dynamic",
        "Reset Displacements",
    };

    public FEAnalysisViewModel(ResultsViewModel resultsVm)
    {
        _resultsVm = resultsVm;
        RunAnalysisCommand = new RelayCommand(OnRunAnalysis, () => !IsSolving);
        StopAnalysisCommand = _stopAnalysisCommand = new RelayCommand(StopAnalysis, () => IsSolving);
        LoadMeshDataCommand = new RelayCommand(OnLoadMeshData, () => !IsSolving);
        ClearMeshDataCommand = new RelayCommand(OnClearMeshData, () => !IsSolving);
        RunVerificationCommand = new RelayCommand(OnRunVerification, () => !IsSolving);
        RunSelectedStageCommand = new RelayCommand(OnRunSelectedStage, () => CanRunSelectedStage);
        NextStageCommand = new RelayCommand(OnNextStage, () => false);
        AddStageDefinitionCommand = new RelayCommand(OnAddStageDefinition);
        RemoveStageDefinitionCommand = new RelayCommand(OnRemoveStageDefinition, () => _selectedStageDefinition != null);
        MoveStageUpCommand = new RelayCommand(OnMoveStageUp, () => _selectedStageDefinition != null && _stageDefinitions.IndexOf(_selectedStageDefinition) > 0);
        MoveStageDownCommand = new RelayCommand(OnMoveStageDown, () => _selectedStageDefinition != null && _stageDefinitions.IndexOf(_selectedStageDefinition) < _stageDefinitions.Count - 1);
        RunStagedAnalysisCommand = new RelayCommand(async () => await RunStagedAnalysisAsync(), () => !IsSolving);
        ApplyEx2TwoLayerSrfPresetCommand = new RelayCommand(ApplyEx2TwoLayerSrfPreset, () => !IsSolving);
        _stageDefinitions.CollectionChanged += OnStageDefinitionsCollectionChanged;
        foreach (var s in _stageDefinitions)
            WireStageDefinition(s);
        ResizeSrfValues();
    }

    /// <summary>True when staged analysis is on and at least one stage row is Seismic / Dynamic.</summary>
    public bool HasSeismicDynamicStage =>
        _stagedExcavationEnabled &&
        _stageDefinitions.Any(s => s.IsSeismicDynamicStage);

    private void OnStageDefinitionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (StageDefinition s in e.OldItems)
            {
                s.PropertyChanged -= OnStageDefinitionPropertyChanged;
                _wiredStageDefinitions.Remove(s);
            }
        }
        if (e.NewItems != null)
        {
            foreach (StageDefinition s in e.NewItems)
                WireStageDefinition(s);
        }
        OnPropertyChanged(nameof(StageDefinitionCount));
        OnPropertyChanged(nameof(StageCount));
        OnPropertyChanged(nameof(HasSeismicDynamicStage));
        RefreshRunPseudoStaticDisplay();
    }

    private void WireStageDefinition(StageDefinition s)
    {
        if (!_wiredStageDefinitions.Add(s)) return;
        s.PropertyChanged += OnStageDefinitionPropertyChanged;
    }

    private void OnStageDefinitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StageDefinition.StageType)
            or nameof(StageDefinition.PseudoStaticKh)
            or nameof(StageDefinition.PseudoStaticKv))
        {
            OnPropertyChanged(nameof(HasSeismicDynamicStage));
            RefreshRunPseudoStaticDisplay();
        }
    }

    public ICommand RunAnalysisCommand { get; }
    public ICommand StopAnalysisCommand { get; }
    public ICommand LoadMeshDataCommand { get; }
    public ICommand ClearMeshDataCommand { get; }
    public ICommand RunVerificationCommand { get; }
    public ICommand RunSelectedStageCommand { get; }
    public ICommand NextStageCommand { get; }
    public ICommand AddStageDefinitionCommand { get; }
    public ICommand RemoveStageDefinitionCommand { get; }
    public ICommand MoveStageUpCommand { get; }
    public ICommand MoveStageDownCommand { get; }
    public ICommand RunStagedAnalysisCommand { get; }
    public ICommand ApplyEx2TwoLayerSrfPresetCommand { get; }

    /// <summary>
    /// Smith &amp; Griffiths Ex2 two-layer SRF preset: tol/limit/incs/layers + 6 SRF trials (1.0–1.5).
    /// Also applies Ex2 materials and horizontal layer-cut assignments when <see cref="_mainWindow"/> is linked.
    /// </summary>
    public void ApplyEx2TwoLayerSrfPreset()
    {
        ConvergenceTolerance = 0.0001;
        MaxIterations = 500;
        LoadIncrements = 20;
        LayersOverride = 1;
        SrfTrialCount = 6;
        var trials = new[] { 1.0, 1.1, 1.2, 1.3, 1.4, 1.5 };
        for (int i = 0; i < _srfValues.Count && i < trials.Length; i++)
            _srfValues[i].Value = trials[i];

        if (_mainWindow != null)
        {
            _mainWindow.MaterialPropertiesVM.ApplyEx2TwoLayerMaterialsPreset();
            _mainWindow.GeometryDefinitionVM.ApplyEx2TwoLayerLayerPreset(20.0);
            _mainWindow.MeshVM.RefreshMaterialLayerEtypesFromGeometry();
        }

        StatusMessage = "Applied Ex2 two-layer SRF preset (tol=1e-4, limit=500, incs=20, 6 trials 1.0–1.5).";
    }

    public bool IsSolving
    {
        get => _isSolving;
        set
        {
            if (_isSolving == value)
                return;
            _isSolving = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExecutionStatus));
            _stopAnalysisCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Cancels the in-flight solver process started by Run FE Analysis.</summary>
    public void StopAnalysis()
    {
        if (!IsSolving)
            return;

        StatusMessage = "Stopping solver...";
        try
        {
            _analysisCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private CancellationToken BeginAnalysisCancellation()
    {
        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();
        return _analysisCts.Token;
    }

    private void EndAnalysisCancellation()
    {
        _analysisCts?.Dispose();
        _analysisCts = null;
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<Node> Nodes => _nodes;
    public IReadOnlyList<ElementBase> Elements => _elements;
    public IReadOnlyList<object> BoundaryConditions => _boundaryConditions;

    public bool StagedExcavationEnabled
    {
        get => _stagedExcavationEnabled;
        set
        {
            _stagedExcavationEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StageCount));
            OnPropertyChanged(nameof(HasSeismicDynamicStage));
            RefreshRunPseudoStaticDisplay();
        }
    }

    public ObservableCollection<StageDefinition> StageDefinitions
    {
        get => _stageDefinitions;
        set
        {
            if (ReferenceEquals(_stageDefinitions, value)) return;
            if (_stageDefinitions is not null)
                _stageDefinitions.CollectionChanged -= OnStageDefinitionsCollectionChanged;
            _stageDefinitions = value ?? new ObservableCollection<StageDefinition>();
            _stageDefinitions.CollectionChanged += OnStageDefinitionsCollectionChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StageDefinitionCount));
        }
    }

    /// <summary>Stage list count for UI (avoid binding to <see cref="ObservableCollection{T}.Count"/> with TwoWay).</summary>
    public int StageDefinitionCount => _stageDefinitions.Count;

    public StageDefinition? SelectedStageDefinition
    {
        get => _selectedStageDefinition;
        set { _selectedStageDefinition = value; OnPropertyChanged(); }
    }

    public bool GravityEnabled
    {
        get => _gravityEnabled;
        set { _gravityEnabled = value; OnPropertyChanged(); }
    }

    public double GravityAcceleration
    {
        get => _gravityAcceleration;
        set { _gravityAcceleration = value; OnPropertyChanged(); }
    }

    public bool LoadSteppingEnabled
    {
        get => _loadSteppingEnabled;
        set { _loadSteppingEnabled = value; OnPropertyChanged(); }
    }

    public int NumberOfLoadSteps
    {
        get => _numberOfLoadSteps;
        set { _numberOfLoadSteps = value; OnPropertyChanged(); }
    }

    // ─── Solver control block (p610_t3 .dat: tol  limit  incs  layers) ───
    //
    // These four values back the "Solver control block:" groupbox on the
    // FE Analysis → Overview & Run subtab. They drive the SolverControl line
    // in the generated .dat (via P610T3DatContentBuilder) and are populated
    // by P610T3DatImporter when an existing .dat is loaded — bidirectional.

    /// <summary>Convergence tolerance for the elastoplastic Newton/viscoplastic loop (p610 .dat 'tol').</summary>
    public double ConvergenceTolerance
    {
        get => _convergenceTolerance;
        set
        {
            if (_convergenceTolerance.Equals(value)) return;
            _convergenceTolerance = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConvergenceToleranceText));
        }
    }

    /// <summary>UI-friendly text wrapper for <see cref="ConvergenceTolerance"/> so the textbox round-trips scientific notation.</summary>
    public string ConvergenceToleranceText
    {
        get => _convergenceTolerance.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                ConvergenceTolerance = v;
            }
        }
    }

    /// <summary>Maximum nonlinear iterations per load step (p610 .dat 'limit').</summary>
    public int MaxIterations
    {
        get => _maxIterations;
        set
        {
            if (_maxIterations == value) return;
            _maxIterations = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Number of load increments applied during the analysis (p610 .dat 'incs').</summary>
    public int LoadIncrements
    {
        get => _loadIncrements;
        set
        {
            if (_loadIncrements == value) return;
            _loadIncrements = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Optional manual override for the p610 .dat 'layers' field. Null means "auto" — derive from
    /// the staged-construction excavation count. 0 selects the infinite-medium MC path.
    /// </summary>
    public int? LayersOverride
    {
        get => _layersOverride;
        set
        {
            if (_layersOverride == value) return;
            _layersOverride = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LayersText));
        }
    }

    /// <summary>UI-friendly text wrapper for <see cref="LayersOverride"/>: empty / 'auto' = null, integer otherwise.</summary>
    public string LayersText
    {
        get => _layersOverride.HasValue
            ? _layersOverride.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "auto";
        set
        {
            var t = (value ?? "").Trim();
            if (t.Length == 0 || string.Equals(t, "auto", StringComparison.OrdinalIgnoreCase))
            {
                LayersOverride = null;
                return;
            }
            if (int.TryParse(t, System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out int v))
            {
                LayersOverride = v;
            }
        }
    }

    /// <summary>
    /// Resolves the effective layers count: user override when set, otherwise the auto-derived
    /// count provided by the caller (excavation layers from staged construction).
    /// </summary>
    public int ResolveEffectiveLayers(int autoLayersFromStaged)
        => _layersOverride ?? autoLayersFromStaged;

    /// <summary>
    /// Visibility for the Solver control block groupbox. Hidden when the elastic-only routing is
    /// active (the elastic solver ignores tol/limit/incs/layers — its .dat carries a comment-only stub).
    /// Always shown when Project Settings → Analysis is slope stability (SRF), including after
    /// geometry / .xfem import while failure criterion is still the default Elastic.
    /// </summary>
    public Visibility SolverControlVisibility
        => UseP610T3ElasticFromScriptDat && _materialProperties?.IsSrfAnalysisMode != true
            ? Visibility.Collapsed
            : Visibility.Visible;

    /// <summary>True when slope stability SRF analysis is active (or Coupled SRF+GW is armed); drives XAML visibility for nsrf/F-value rows.</summary>
    public bool IsSrfSolverMode => UseSrfFromProjectSettings || UseCoupledGroundwaterSrf;

    /// <summary>Mechanical continuum axisymmetric SRF from Project Settings (independent of seepage p72).</summary>
    public bool IsSrfAxisymmetric => _mainWindow?.ProjectSettingsVM?.SrfAxisymmetric == true;

    /// <summary>Two-way UI binding for mechanical axisymmetric SRF (Project Settings).</summary>
    public bool SrfAxisymmetric
    {
        get => IsSrfAxisymmetric;
        set
        {
            if (_mainWindow?.ProjectSettingsVM == null || _mainWindow.ProjectSettingsVM.SrfAxisymmetric == value)
                return;
            _mainWindow.ProjectSettingsVM.SrfAxisymmetric = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSrfAxisymmetric));
            RefreshResolvedRunRoutingDisplay();
        }
    }

    /// <summary>Number of SRF trial F-values (nsrf). Resizes <see cref="SrfValues"/> automatically.</summary>
    public int SrfTrialCount
    {
        get => _srfTrialCount;
        set
        {
            int clamped = Math.Max(1, value);
            if (_srfTrialCount == clamped) return;
            _srfTrialCount = clamped;
            ResizeSrfValues();
            OnPropertyChanged();
        }
    }

    /// <summary>Ordered list of SRF trial F-values; length is always <see cref="SrfTrialCount"/>.</summary>
    public ObservableCollection<SrfTrialEntry> SrfValues => _srfValues;

    // ─── SRF bracket search (alternative to the fixed-trial sweep above) ──────

    /// <summary>SRF search strategy: fixed sweep (nsrf + F list) or iterative bracket bisection.</summary>
    public SrfSearchMode SelectedSrfSearchMode
    {
        get => _selectedSrfSearchMode;
        set
        {
            if (_selectedSrfSearchMode == value) return;
            _selectedSrfSearchMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSrfBracketMode));
            OnPropertyChanged(nameof(IsSrfSweepMode));
        }
    }

    /// <summary>True when the SRF search mode is Bracket; drives XAML visibility for bracket controls.</summary>
    public bool IsSrfBracketMode => _selectedSrfSearchMode == SrfSearchMode.Bracket;

    /// <summary>True when the SRF search mode is Sweep (the original nsrf/F-list controls).</summary>
    public bool IsSrfSweepMode => _selectedSrfSearchMode == SrfSearchMode.Sweep;

    /// <summary>
    /// In-situ stress path for gravity-only SRF layers. Default <see cref="SrfInsituMode.Auto"/>
    /// resolves to K0 for Coupled GW / Ru and gravity turn-on for dry SRF.
    /// </summary>
    public SrfInsituMode SelectedSrfInsituMode
    {
        get => _selectedSrfInsituMode;
        set
        {
            if (_selectedSrfInsituMode == value) return;
            _selectedSrfInsituMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ResolvedSrfInsituModeToken));
            OnPropertyChanged(nameof(SrfInsituModeSummary));
        }
    }

    /// <summary>ComboBox source for <see cref="SelectedSrfInsituMode"/>.</summary>
    public Array SrfInsituModeChoices { get; } = Enum.GetValues(typeof(SrfInsituMode));

    /// <summary>
    /// Token written to <c>! srf_insitu_mode</c> after resolving Auto against Coupled/Ru.
    /// </summary>
    public string ResolvedSrfInsituModeToken => ResolveEffectiveSrfInsituModeToken();

    public string SrfInsituModeSummary =>
        SelectedSrfInsituMode == SrfInsituMode.Auto
            ? $"Auto → {ResolvedSrfInsituModeToken}"
            : ResolvedSrfInsituModeToken;

    /// <summary>
    /// Effective solver token: <c>k0</c> or <c>gravity</c>. Auto uses K0 when Coupled
    /// groundwater+SRF is armed or Static Ru groundwater is enabled.
    /// </summary>
    public string ResolveEffectiveSrfInsituModeToken()
    {
        if (_selectedSrfInsituMode == SrfInsituMode.K0)
            return "k0";
        if (_selectedSrfInsituMode == SrfInsituMode.Gravity)
            return "gravity";

        if (UseCoupledGroundwaterSrf)
            return "k0";

        var gw = _mainWindow?.GroundwaterVM;
        if (gw?.IsEnabled == true && gw.Method == GroundwaterMethod.Ru)
            return "k0";

        return "gravity";
    }

    /// <summary>
    /// When true (default), the bracket runner auto-detects [F_min, F_max] by expanding outward
    /// from <see cref="SrfBracketSeed"/>. When false, <see cref="SrfFoSMin"/>/<see cref="SrfFoSMax"/>
    /// are used directly as the manual override bracket.
    /// </summary>
    public bool AutoDetectSrfBracket
    {
        get => _autoDetectSrfBracket;
        set
        {
            if (_autoDetectSrfBracket == value) return;
            _autoDetectSrfBracket = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSrfBracketManualMode));
        }
    }

    /// <summary>True when the bracket search uses manual F_min/F_max instead of auto-detection.</summary>
    public bool IsSrfBracketManualMode => !_autoDetectSrfBracket;

    /// <summary>Starting FoS guess for auto-detect bracket expansion.</summary>
    public double SrfBracketSeed
    {
        get => _srfBracketSeed;
        set { if (_srfBracketSeed.Equals(value)) return; _srfBracketSeed = value; OnPropertyChanged(); }
    }

    /// <summary>Geometric growth factor applied per auto-expansion step (must be &gt; 1.0).</summary>
    public double SrfBracketExpansionFactor
    {
        get => _srfBracketExpansionFactor;
        set { if (_srfBracketExpansionFactor.Equals(value)) return; _srfBracketExpansionFactor = value; OnPropertyChanged(); }
    }

    /// <summary>Safety cap on auto-expansion steps before giving up with a clear error.</summary>
    public int SrfMaxExpansionSteps
    {
        get => _srfMaxExpansionSteps;
        set { if (_srfMaxExpansionSteps == value) return; _srfMaxExpansionSteps = value; OnPropertyChanged(); }
    }

    /// <summary>Manual bracket lower bound (F_min). Ignored when <see cref="AutoDetectSrfBracket"/> is true.</summary>
    public double SrfFoSMin
    {
        get => _srfFoSMin;
        set { if (_srfFoSMin.Equals(value)) return; _srfFoSMin = value; OnPropertyChanged(); }
    }

    /// <summary>Manual bracket upper bound (F_max). Ignored when <see cref="AutoDetectSrfBracket"/> is true.</summary>
    public double SrfFoSMax
    {
        get => _srfFoSMax;
        set { if (_srfFoSMax.Equals(value)) return; _srfFoSMax = value; OnPropertyChanged(); }
    }

    /// <summary>Bisection stops once the bracket width (F_hi − F_lo) falls below this tolerance.</summary>
    public double SrfBracketTolerance
    {
        get => _srfBracketTolerance;
        set { if (_srfBracketTolerance.Equals(value)) return; _srfBracketTolerance = value; OnPropertyChanged(); }
    }

    /// <summary>Safety cap on bisection iterations (separate from <see cref="SrfMaxExpansionSteps"/>).</summary>
    public int SrfMaxBracketIterations
    {
        get => _srfMaxBracketIterations;
        set { if (_srfMaxBracketIterations == value) return; _srfMaxBracketIterations = value; OnPropertyChanged(); }
    }

    /// <summary>Read-only preview of the auto-detected bracket, populated after the expansion phase completes.</summary>
    public string DetectedSrfBracketPreview =>
        _lastDetectedSrfFoSMin.HasValue && _lastDetectedSrfFoSMax.HasValue
            ? $"Detected bracket: F ∈ [{_lastDetectedSrfFoSMin.Value:G4}, {_lastDetectedSrfFoSMax.Value:G4}]"
            : "Detected bracket: (run analysis to detect)";

    /// <summary>
    /// Last auto-detected bracket bounds (Phase 0 result), or null before the first auto-detect
    /// run in this session / before restoring a saved project. When present and
    /// <see cref="AutoDetectSrfBracket"/> is true, <see cref="DatSolverControlExporter"/> persists
    /// these (rather than the manual-override <see cref="SrfFoSMin"/>/<see cref="SrfFoSMax"/>) as
    /// <c>srf_F_min</c>/<c>srf_F_max</c> so a saved/reloaded project reflects the last detected
    /// bracket without re-running the expansion phase.
    /// </summary>
    public double? LastDetectedSrfFoSMin => _lastDetectedSrfFoSMin;

    /// <summary>See <see cref="LastDetectedSrfFoSMin"/>.</summary>
    public double? LastDetectedSrfFoSMax => _lastDetectedSrfFoSMax;

    /// <summary>Records the auto-detected bracket for display; called by the bracket runner after Phase 0.</summary>
    public void SetDetectedSrfBracketPreview(double fMin, double fMax)
    {
        _lastDetectedSrfFoSMin = fMin;
        _lastDetectedSrfFoSMax = fMax;
        OnPropertyChanged(nameof(DetectedSrfBracketPreview));
        OnPropertyChanged(nameof(LastDetectedSrfFoSMin));
        OnPropertyChanged(nameof(LastDetectedSrfFoSMax));
    }

    private void SetDetectedSrfBracketPreviewFromAnyThread(double fMin, double fMax)
        => RunOnUiThread(() => SetDetectedSrfBracketPreview(fMin, fMax));

    private void SetStatusMessageFromAnyThread(string message)
        => RunOnUiThread(() => StatusMessage = message);

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    /// <summary>
    /// Validates the active SRF bracket settings before running. Returns a human-readable error
    /// (null when valid).
    /// </summary>
    public string? ValidateSrfBracketSettings()
    {
        if (_srfBracketTolerance <= 0)
            return "Bracket tolerance must be greater than 0.";
        if (_srfMaxBracketIterations < 1)
            return "Max bracket iterations must be at least 1.";

        if (_autoDetectSrfBracket)
        {
            if (_srfBracketSeed <= 0)
                return "Starting FoS guess must be greater than 0.";
            if (_srfBracketExpansionFactor <= 1.0)
                return "Expansion factor must be greater than 1.0.";
            if (_srfMaxExpansionSteps < 1)
                return "Max expansion steps must be at least 1.";
        }
        else
        {
            if (_srfFoSMin <= 0)
                return "Minimum FoS (F_min) must be greater than 0.";
            if (_srfFoSMax <= _srfFoSMin)
                return "Maximum FoS (F_max) must be greater than Minimum FoS (F_min).";
        }

        return null;
    }

    private static bool TryFindInvalidNativeQuadRow(
        MeshViewModel meshVm,
        string meshType,
        out string message)
    {
        foreach (var row in meshVm.MeshInformationRows)
        {
            if (meshType == "Q4")
            {
                var nodes = new[] { row.Node1, row.Node2, row.Node3, row.Node4 };
                if (nodes.Any(n => n <= 0) || nodes.Distinct().Count() != nodes.Length)
                {
                    message = $"Q4 element {row.ElementId} has invalid corner connectivity: [{string.Join(", ", nodes)}].";
                    return true;
                }
            }
            else if (meshType == "Q8")
            {
                var nodes = new[] { row.Node1, row.Node2, row.Node3, row.Node4, row.Node5, row.Node6, row.Node7, row.Node8 };
                if (nodes.Any(n => n <= 0) || nodes.Distinct().Count() != nodes.Length)
                {
                    message = $"Q8 element {row.ElementId} has invalid corner/midside connectivity: [{string.Join(", ", nodes)}].";
                    return true;
                }
            }
        }

        message = "";
        return false;
    }

    /// <summary>Snapshots the current bracket settings for <see cref="SlopeSrfBracketRunner"/>.</summary>
    public SlopeSrfBracketRunner.BracketSettings BuildSrfBracketSettings() => new()
    {
        AutoDetect = _autoDetectSrfBracket,
        Seed = _srfBracketSeed,
        ExpansionFactor = _srfBracketExpansionFactor,
        MaxExpansionSteps = _srfMaxExpansionSteps,
        ManualFMin = _srfFoSMin,
        ManualFMax = _srfFoSMax,
        Tolerance = _srfBracketTolerance,
        MaxBisectionIterations = _srfMaxBracketIterations,
    };

    /// <summary>FE Analysis inner TabControl index (0=Overview and Run, …).</summary>
    public int FeAnalysisSubTabIndex
    {
        get => _feAnalysisSubTabIndex;
        set { _feAnalysisSubTabIndex = value; OnPropertyChanged(); }
    }
    private int _feAnalysisSubTabIndex;

    private void ResizeSrfValues()
    {
        while (_srfValues.Count > _srfTrialCount)
            _srfValues.RemoveAt(_srfValues.Count - 1);
        while (_srfValues.Count < _srfTrialCount)
        {
            double next = _srfValues.Count == 0 ? 1.0 : Math.Round(_srfValues[^1].Value + 0.1, 4);
            _srfValues.Add(new SrfTrialEntry { Index = _srfValues.Count + 1, Value = next });
        }
        for (int i = 0; i < _srfValues.Count; i++)
            _srfValues[i].Index = i + 1;
    }

    /// <summary>Apply p64/Q8 SRF tail (tol, limit, nsrf, F list) from Script Editor import.</summary>
    public void ApplySrfSolverControlFromDat(
        double tol,
        int limit,
        int nsrf,
        IReadOnlyList<double>? fValues)
    {
        ConvergenceTolerance = tol;
        MaxIterations = limit;
        SrfTrialCount = Math.Max(1, nsrf);
        if (fValues == null) return;
        for (int i = 0; i < Math.Min(fValues.Count, _srfValues.Count); i++)
            _srfValues[i].Value = fValues[i];
    }

    private List<double> CollectSrfTrialValuesForRun()
    {
        var fValues = _srfValues.Select(e => e.Value).ToList();
        int nsrf = Math.Max(1, _srfTrialCount);
        while (fValues.Count < nsrf)
            fValues.Add(fValues.Count == 0 ? 1.0 : Math.Round(fValues[^1] + 0.1, 4));
        if (fValues.Count > nsrf)
            fValues = fValues.Take(nsrf).ToList();
        return fValues;
    }

    public object? VerificationResult
    {
        get => _verificationResult;
        set { _verificationResult = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<object> AvailableStagesForRun
    {
        get => _availableStagesForRun;
        set { _availableStagesForRun = value.ToList(); OnPropertyChanged(); }
    }

    public object? SelectedStageForRun
    {
        get => _selectedStageForRun;
        set { _selectedStageForRun = value; OnPropertyChanged(); }
    }

    public bool CanRunSelectedStage
    {
        get => _canRunSelectedStage;
        set { _canRunSelectedStage = value; OnPropertyChanged(); }
    }

    // FEAnalysisExecutionDetailsView bindings
    public string ExecutionStatus
    {
        get => IsSolving ? "Running..." : _lastExecutionStatus;
    }
    public string TotalExecutionTime => _lastTotalTime;
    public string AnalysisType =>
        UseCoupledSeepageSrf
            ? $"Coupled Steady State ({ResolveCoupledSeepageSolverType()} / {SeepageDatContentBuilder.ResolveProgramId(ResolveCoupledSeepageSolverType()) ?? "?"}) → Slope SRF"
            : UseCoupledStaticSrf
            ? "Coupled Static → Slope SRF"
            : UseSeepageFromProjectSettings
            ? (SeepageDatContentBuilder.ResolveProgramId(_materialProperties?.SelectedDetailedAnalysisType) is { } seepageProg
                ? $"Seepage / GW flow (fem_cpp {seepageProg})"
                : "Seepage / GW flow (fem_cpp)")
            : UseGroundwaterStaticFromProjectSettings
                ? $"Groundwater Static (prescribed pore pressure) + {ResolveStructuralAnalysisTypeLabel()}"
            : UseP41FromScriptDat
                ? "1D axial rod (fem_cpp / p41, Script Editor .dat)"
                : UseSrfFromProjectSettings
                    ? (_materialProperties?.IsSrfReadyForRun == true
                        ? "Slope SRF (native slope_stability_*_SRF, Script Editor .dat)"
                        : "Slope SRF (select MC, DP, HB, or GHB failure criterion)")
                : UseP610T3FromScriptDat
                    ? "p610_t3 family (criterion/material routed, Script Editor .dat)"
                    : UseP51FromScriptDat
                        ? "Plane strain (p51, Script Editor .dat)"
                        : "Plane strain (p51, demo mesh)";

    private string ResolveStructuralAnalysisTypeLabel()
    {
        if (UseP41FromScriptDat)
            return "1D axial rod (p41)";
        if (UseSrfFromProjectSettings)
            return _materialProperties?.IsSrfReadyForRun == true
                ? "Slope SRF"
                : "Slope SRF (select criterion)";
        if (UseP610T3FromScriptDat)
            return "p610_t3";
        if (UseP51FromScriptDat)
            return "Plane strain (p51)";
        return ResolvedSolverForRun;
    }

    /// <summary>
    /// Overview &amp; Run header: planned analysis mode from Project Settings routing
    /// (Seepage / Slope SRF / Structural), including the resolved solver when known.
    /// </summary>
    public string ActiveAnalysisModeSummary
    {
        get
        {
            // Coupled SRF+GW is armed by category ON switches — show it even if another
            // (possibly OFF) category node is currently selected in the Analysis tree.
            if (UseCoupledSeepageSrf)
            {
                var seepageType = ResolveCoupledSeepageSolverType();
                var prog = SeepageDatContentBuilder.ResolveProgramId(seepageType) ?? "?";
                return $"Analysis mode: Coupled Steady State ({seepageType} / {prog}) → Slope SRF — {ResolvedSolverForRun}";
            }

            if (UseCoupledStaticSrf)
                return $"Analysis mode: Coupled Static → Slope SRF — {ResolvedSolverForRun}";

            if (!IsActiveAnalysisCategoryEnabled)
            {
                var cat = _materialProperties?.SelectedAnalysisCategory ?? "analysis category";
                return $"Analysis mode: Off (\"{cat}\" is OFF in Project Settings)";
            }

            if (UseSeepageFromProjectSettings)
            {
                var prog = SeepageDatContentBuilder.ResolveProgramId(
                    _materialProperties?.SelectedDetailedAnalysisType);
                return prog != null
                    ? $"Analysis mode: Seepage (GW flow) — fem_cpp {prog}"
                    : "Analysis mode: Seepage (GW flow) — fem_cpp";
            }

            if (UseTransientFromProjectSettings)
            {
                var prog = TransientDatContentBuilder.ResolveProgramId(
                    _materialProperties?.SelectedDetailedAnalysisType);
                return prog != null
                    ? $"Analysis mode: Transient dissipation (θ-method) — fem_cpp {prog}"
                    : "Analysis mode: Transient dissipation (θ-method) — fem_cpp";
            }

            if (UseGroundwaterStaticFromProjectSettings)
            {
                var components = FormatGroundwaterStaticComponentsShort();
                var suffix = string.IsNullOrEmpty(components) ? "" : $" [{components}]";
                return $"Analysis mode: Groundwater — Static{suffix} — {ResolvedSolverForRun}";
            }

            if (UseP41FromScriptDat)
                return "Analysis mode: Structural (1D axial rod) — p41";

            if (UseSrfFromProjectSettings)
                return $"Analysis mode: Slope SRF — {ResolvedSolverForRun}";

            if (UseP610T3FromScriptDat)
                return $"Analysis mode: Structural (p610) — {ResolvedSolverForRun}";

            if (UseP51FromScriptDat)
                return "Analysis mode: Structural (plane strain) — p51";

            return $"Analysis mode: Structural — {ResolvedSolverForRun}";
        }
    }

    private string FormatGroundwaterStaticComponentsShort()
    {
        var s = _mainWindow?.ProjectSettingsVM?.GroundwaterStaticSettings;
        if (s == null) return "";
        var parts = new List<string>();
        if (s.IncludeWaterSurfaces) parts.Add("surfaces");
        if (s.IncludeWaterSurfaces && s.IncludePondedFreeWater) parts.Add("ponded");
        if (s.IncludeRuCoefficients) parts.Add("Ru");
        return string.Join(", ", parts);
    }

    public int ElementCount => _elements.Count > 0 ? _elements.Count : _lastElementCount;
    public int NodeCount => _nodes.Count > 0 ? _nodes.Count : _lastNodeCount;
    public int StageCount => StagedExcavationEnabled ? _stageDefinitions.Count : 0;
    public IReadOnlyList<object> ExecutionSteps { get; } = new List<object>();

    /// <summary>
    /// Path to directory containing p51.exe / p41.exe. Set before running.
    /// If empty, searches: AppDir, AppDir/build, AppDir/../build, AppDir/../cpp/build
    /// </summary>
    public string CppExecutableDirectory
    {
        get => _runner.ExecutableDirectory;
        set => _runner.ExecutableDirectory = value;
    }

    /// <summary>
    /// Resolve C++ executable directory if unset. Accepts folder containing fem_cpp.exe, p41.exe, and/or p51.exe
    /// (also checks Release/ and Debug/ subfolders for VS CMake layouts).
    /// </summary>
    public void ResolveCppExecutablePath()
    {
        if (!string.IsNullOrEmpty(_runner.ExecutableDirectory))
            return;
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            appDir,
            Path.Combine(appDir, "build"),
            Path.Combine(appDir, "..", "build"),
            Path.Combine(appDir, "..", "..", "..", "build")  // from X2D_GUI/bin/Debug/net8.0-windows -> cpp/build
        };
        foreach (var dir in candidates)
        {
            var full = Path.GetFullPath(dir);
            if (DirectoryHasAnyFeExe(full))
            {
                _runner.ExecutableDirectory = full;
                return;
            }
        }
    }

    private static bool DirectoryHasAnyFeExe(string dir)
    {
        if (!Directory.Exists(dir))
            return false;
        foreach (var sub in new[] { dir, Path.Combine(dir, "Release"), Path.Combine(dir, "Debug") })
        {
            if (File.Exists(Path.Combine(sub, "fem_cpp.exe"))
                || File.Exists(Path.Combine(sub, "p51.exe"))
                || File.Exists(Path.Combine(sub, "p41.exe")))
                return true;
        }
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Script Editor .dat with current solver-control values applied (SRF tail patched when active).</summary>
    private string? ResolveScriptDatForRun()
    {
        if (_scriptVm == null)
            return null;
        return _scriptVm.GetScriptContentForSolverRun();
    }

    private async void OnRunAnalysis()
    {
        ResolveCppExecutablePath();
        RefreshRunPseudoStaticDisplay();
        IsSolving = true;
        var cancellationToken = BeginAnalysisCancellation();
        StatusMessage = "Preparing FE analysis...";
        try
        {
            if (!IsActiveAnalysisCategoryEnabled && !UseCoupledGroundwaterSrf)
            {
                var cat = _materialProperties?.SelectedAnalysisCategory ?? "analysis category";
                StatusMessage = $"Analysis category is OFF: {cat}";
                MessageBox.Show(
                    $"The active analysis category \"{cat}\" is turned OFF in Project Settings → Analysis.\n\n" +
                    "Turn ON that category to run related analysis functions.",
                    "FE Analysis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (UseCoupledSeepageSrf)
            {
                var seepageType = ResolveCoupledSeepageSolverType();
                var seepageProg = SeepageDatContentBuilder.ResolveProgramId(seepageType) ?? "?";
                if (!ConfirmCoupledSeepageHeadsForSrf())
                    return;
                StatusMessage = $"Coupled: Seepage ({seepageProg}) -> Slope SRF...";
                var seepageOk = await RunSeepageAnalysisAsync(
                    cancellationToken,
                    detailedTypeOverride: seepageType,
                    navigateToResults: false);
                if (!seepageOk)
                    return;

                _scriptVm?.SyncGroundwaterToScript();
                if (!ConfirmCoupledSeepagePorePressureForSrf())
                    return;

                StatusMessage = $"Coupled: Seepage ({seepageProg}) complete - running Slope SRF...";
                await RunCoupledSrfWithTempCategoryAsync(cancellationToken, restoreSeepageType: seepageType);
            }
            else if (UseCoupledStaticSrf)
            {
                var gwVm = _mainWindow?.GroundwaterVM;
                if (gwVm == null)
                {
                    StatusMessage = "Coupled Static → SRF cancelled — Groundwater is unavailable.";
                    return;
                }

                var gwModel = gwVm.BuildModel();
                if (!SrfGroundwaterExport.WouldBeActive(gwModel))
                {
                    StatusMessage =
                        "Coupled Static → SRF cancelled — no WT / Ru / water-pressure grid to transfer as pore pressure u.";
                    MessageBox.Show(
                        "SRF and Flow-Seepage/Groundwater are both ON (Coupled Static), but no prescribed pore pressure is defined.\n\n" +
                        "Define a phreatic water surface (WT), Ru coefficient, or water-pressure grid, then run again.\n\n" +
                        "For seepage-solved pore pressures, select Flow-Seepage → Steady State instead.",
                        "Coupled Static → SRF",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                StatusMessage = "Coupled Static: transferring pore pressure u → Slope SRF...";
                // Mesh sync + GW merge happen inside RunP610OrSrfAnalysisAsync / P610T3DatContentBuilder.
                // Early SyncGroundwaterToScript is a no-op when ScriptContent is still empty.
                await RunCoupledSrfWithTempCategoryAsync(cancellationToken, restoreSeepageType: null);
            }
            else if (UseSeepageFromProjectSettings)
            {
                await RunSeepageAnalysisAsync(cancellationToken);
            }
            else if (UseTransientFromProjectSettings)
            {
                await RunTransientAnalysisAsync(cancellationToken);
            }
            else if (UseP41FromScriptDat)
            {
                StatusMessage = "Entered Structural FE analysis — running p41...";
                _scriptVm?.SyncAnalysisRoutingFromGui();
                var datContent = ResolveScriptDatForRun();
                if (string.IsNullOrWhiteSpace(datContent))
                {
                    StatusMessage = "No .dat content. Open Script Editor tab or set geometry/mesh/loads/BCs.";
                    MessageBox.Show(
                        "The Script Editor has no .dat to run. Define geometry, mesh, loads, and boundary conditions, or paste a p41 .dat in the Script Editor.",
                        "FE Analysis",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var workDir = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_fe_script");
                Directory.CreateDirectory(workDir);
                var p41Result = await _runner.RunScriptRodFemAsync(datContent, "fe_script_run", workDir, cancellationToken);
                ApplyP41FeResult(p41Result);
                StatusMessage = $"FE solver complete. {p41Result.Nodes.Count} nodes, {p41Result.Elements.Count} elements.";
            }
            else if (UseP610T3FromScriptDat)
            {
                await RunP610OrSrfAnalysisAsync(cancellationToken);
            }
            else if (UseP51FromScriptDat)
            {
                StatusMessage = "Entered Structural FE analysis — running p51...";
                _scriptVm?.RefreshDatContent();
                var datContent = ResolveScriptDatForRun();
                if (string.IsNullOrWhiteSpace(datContent))
                {
                    StatusMessage = "No .dat content. Open Script Editor or set Material Properties to 2D plane strain.";
                    MessageBox.Show(
                        "The Script Editor has no p51 .dat to run. Open the Script Editor tab or ensure Material Properties is Structural – static elasticity / Plane strain (2D).",
                        "FE Analysis",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var workDir = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_p51_script");
                Directory.CreateDirectory(workDir);
                var result = await _runner.RunP51FromDatContentAsync(datContent, "p51_script_run", workDir, cancellationToken);
                ApplyResult(result);
                StatusMessage = $"p51 complete. {result.Nodes.Count} nodes, {result.Elements.Count} elements.";
            }
            else
            {
                StatusMessage = "Entered Structural FE analysis — running p51 (demo mesh)...";
                var input = P51DatFileWriter.FromRectangularMesh(
                    width: 1.0, height: 1.0,
                    nxe: 2, nye: 2,
                    youngsModulus: 1e6, poissonsRatio: 0.3,
                    restrainedNodes: new List<(int, int, int)>
                    {
                        (1, 0, 1), (4, 0, 1), (7, 0, 0), (8, 1, 0), (9, 1, 0)
                    },
                    loadedNodes: new List<(int, double, double)>
                    {
                        (1, 0.0, -0.25), (2, 0.0, -0.50), (3, 0.0, -0.25)
                    },
                    dir: _meshSource?.MeshNumberingDirectionChar ?? 'x');

                var result = await _runner.RunP51Async(input, "p51_run", cancellationToken);
                ApplyResult(result);
                StatusMessage = $"Analysis complete. {result.Nodes.Count} nodes, {result.Elements.Count} elements.";
            }
        }
        catch (OperationCanceledException)
        {
            _lastExecutionStatus = "Stopped";
            StatusMessage = "Analysis stopped by user.";
            EndAnalysisRunState();
        }
        catch (Exception ex)
        {
            _lastExecutionStatus = "Failed";
            StatusMessage = $"Error: {ex.Message}";
            EndAnalysisRunState();
            MessageBox.Show($"Analysis failed:\n\n{ex.Message}", "FE Analysis", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndAnalysisCancellation();
            if (IsSolving)
                IsSolving = false;
        }
    }

    /// <summary>
    /// Structural / Slope SRF path from Script Editor .dat (p610 family / native SRF / p64).
    /// </summary>
    private async Task RunP610OrSrfAnalysisAsync(CancellationToken cancellationToken)
    {
                if (_materialProperties?.IsSrfAnalysisMode == true)
                    _materialProperties.ReconcileSrfFailureCriterion(_scriptVm?.ScriptContent);

                if (_materialProperties?.IsSrfAnalysisMode == true && !_materialProperties.IsSrfReadyForRun)
                {
                    StatusMessage = "SRF analysis requires a slope failure criterion.";
                    MessageBox.Show(
                        "Slope stability – strength reduction (SRF) requires a slope failure criterion in Material Properties:\n\n" +
                        "Mohr–Coulomb, Drucker–Prager, Hoek–Brown, or Generalized Hoek–Brown.\n\n" +
                        "Elastic and other non-slope criteria cannot run in SRF mode.",
                        "FE Analysis",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                RefreshResolvedRunRoutingDisplay();
                StatusMessage = UseSrfFromProjectSettings
                    ? $"Entered Slope SRF analysis — running {ResolvedSolverForRun}..."
                    : $"Entered Structural FE analysis — running {ResolvedSolverForRun}...";

                // #region agent log
                AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "FEAnalysisViewModel.cs:OnRunAnalysis",
                    "before SyncAnalysisRoutingFromGui",
                    new
                    {
                        mesh = AgentDebugRuntimeLog.SummarizeMesh(_meshSource),
                        script = AgentDebugRuntimeLog.SummarizeScript(_scriptVm)
                    });
                // #endregion
                _scriptVm?.SyncAnalysisRoutingFromGui();
                // #region agent log
                AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "FEAnalysisViewModel.cs:OnRunAnalysis",
                    "after SyncAnalysisRoutingFromGui",
                    new
                    {
                        mesh = AgentDebugRuntimeLog.SummarizeMesh(_meshSource),
                        script = AgentDebugRuntimeLog.SummarizeScript(_scriptVm)
                    });
                // #endregion
                var shouldSyncMeshToScript = _materialProperties?.IsSrfAnalysisMode == true
                    && _meshSource is { IsDiscretized: true, MeshInformationRows.Count: > 0 };
                var preSyncOptions = P610AnalysisOptionsHelper.BuildFromViewModels(_materialProperties, _meshSource);
                var preSyncMeshType = P610AnalysisOptionsHelper.ResolveMeshType(_meshSource);
                var preSyncRoute = SrfLiteratureOptimalRouting.Resolve(preSyncMeshType, preSyncOptions.FailureCriterion, IsSrfAxisymmetric);
                // #region agent log
                AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "FEAnalysisViewModel.cs:OnRunAnalysis",
                    "before TrySyncDiscretizedMeshToScriptDat",
                    new
                    {
                        shouldSyncMeshToScript,
                        mesh = AgentDebugRuntimeLog.SummarizeMesh(_meshSource),
                        script = AgentDebugRuntimeLog.SummarizeScript(_scriptVm)
                    });
                // #endregion
                if (shouldSyncMeshToScript)
                {
                    SrfExplicitDeckFreshness.EnsureSaneSrfSolverSettings(this);
                    var synced = _scriptVm?.TrySyncDiscretizedMeshToScriptDat(ignoreLoadedDeckPreserve: true) == true;
                    if (!synced
                        && preSyncRoute.Deck == SrfLiteratureOptimalRouting.DeckFormat.P64Explicit)
                    {
                        StatusMessage = "SRF run cancelled — could not sync Q4/Q8 mesh to Script Editor.";
                        MessageBox.Show(
                            "The mesh could not be exported to an explicit p64 SRF deck.\n\n" +
                            "Ensure Material Properties → SRF mode is enabled, the mesh is discretized, and boundary conditions are defined.",
                            "SRF mesh sync",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }
                // #region agent log
                AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "FEAnalysisViewModel.cs:OnRunAnalysis",
                    "after TrySyncDiscretizedMeshToScriptDat",
                    new
                    {
                        shouldSyncMeshToScript,
                        mesh = AgentDebugRuntimeLog.SummarizeMesh(_meshSource),
                        script = AgentDebugRuntimeLog.SummarizeScript(_scriptVm)
                    });
                // #endregion

                // Merge Groundwater into the .dat so SRF/p610 Spec.active() and u_kPa contours work.
                // CloneForSolverExport force-enables Static export flags for available surf/Ru/grid data.
                if (_mainWindow?.GroundwaterVM?.IsEnabled == true)
                {
                    var gwModel = _mainWindow.GroundwaterVM.BuildModel();
                    if (!SrfGroundwaterExport.WouldBeActive(gwModel))
                    {
                        StatusMessage =
                            "Groundwater is ON but no WT / Ru / water-pressure grid is defined — " +
                            "SRF pore pressure u will be ~0. Define a phreatic surface, Ru, or run Steady seepage.";
                    }
                }
                _scriptVm?.SyncGroundwaterToScript();

                var datContent = HasMohrCoulombJointSlip
                    ? (TryBuildMohrCoulombJointP610Dat() ?? ResolveScriptDatForRun())
                    : ResolveScriptDatForRun();
                // #region agent log
                AgentDebugRuntimeLog.Write("trace-q4-sync", "Q4SYNC", "FEAnalysisViewModel.cs:OnRunAnalysis",
                    "after ResolveScriptDatForRun",
                    new
                    {
                        mesh = AgentDebugRuntimeLog.SummarizeMesh(_meshSource),
                        script = AgentDebugRuntimeLog.SummarizeScript(_scriptVm),
                        dat = AgentDebugRuntimeLog.SummarizeDat(datContent)
                    });
                // #endregion
                if (string.IsNullOrWhiteSpace(datContent))
                {
                    StatusMessage = "No .dat content. Open Script Editor or define geometry/mesh/loads/BCs.";
                    MessageBox.Show(
                        UseSrfFromProjectSettings
                            ? "The Script Editor has no .dat to run. Define mesh, materials, and boundary conditions, or paste an SRF .dat in the Script Editor."
                            : "The Script Editor has no .dat to run. The active routing is p610_t3_elastic (Failure criterion = Elastic, Material type = Isotropic in Material Properties); the .dat must be in the linear-elastic T3 format (E, ν, traction groups). Generate or paste it in the Script Editor first.",
                        "FE Analysis",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var options = P610AnalysisOptionsHelper.BuildFromViewModels(_materialProperties, _meshSource);
                var resolvedMeshType = P610AnalysisOptionsHelper.ResolveMeshType(_meshSource);
                var srfRoute = SrfLiteratureOptimalRouting.Resolve(resolvedMeshType, options.FailureCriterion, IsSrfAxisymmetric);
                var programName = UseSrfFromProjectSettings || _materialProperties?.IsSrfAnalysisMode == true
                    ? srfRoute.ProcessExecutable
                    : ResolveStructuralP610ProgramName();

                if (_materialProperties?.IsSrfAnalysisMode == true
                    && P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(programName))
                {
                    datContent = P610AnalysisOptionsHelper.PrepareDatForNativeSlopeSrfExecutable(
                        datContent, srfRoute.Executable);
                }

                // #region agent log
                AgentDebugRuntimeLog.Write("pre-fix", "H1,H2,H3,H5", "FEAnalysisViewModel.cs:OnRunAnalysis",
                    "before native FE/SRF run",
                    new
                    {
                        programName,
                        resolvedMeshType,
                        isSrf = _materialProperties?.IsSrfAnalysisMode == true,
                        searchMode = IsSrfBracketMode ? "bracket" : "sweep",
                        maxIterations = _maxIterations,
                        srfTrialCount = _srfTrialCount,
                        mesh = AgentDebugRuntimeLog.SummarizeMesh(_meshSource)
                    });
                // #endregion

                if (P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(programName))
                {
                    if (srfRoute.Deck == SrfLiteratureOptimalRouting.DeckFormat.P64Explicit)
                    {
                        var deckCheck = SrfExplicitDatValidator.Validate(datContent);
                        if (!deckCheck.IsValid)
                        {
                            var detail = string.Join("\n", deckCheck.Issues);
                            StatusMessage = "SRF run cancelled — explicit Q4/Q8 deck validation failed.";
                            MessageBox.Show(
                                "The .dat deck for this Q4/Q8 SRF run failed pre-flight validation:\n\n" + detail
                                + "\n\nRegenerate the mesh and sync to Script Editor before running again.",
                                "SRF deck validation",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }
                    }

                    if (_meshSource is { IsDiscretized: true, MeshInformationRows.Count: > 0 })
                    {
                        var meshProbe = P610T3DatContentBuilder.ProbeMeshExportInventory(_meshSource, _materialProperties);
                        // #region agent log
                        DebugAgentLog.Write("H9", "FEAnalysisViewModel.cs:OnRunAnalysis",
                            "Pre-run mesh/material probe", meshProbe);
                        // #endregion
                        var activeMeshType = P610AnalysisOptionsHelper.ResolveMeshType(_meshSource);
                        var quadRows = _meshSource.MeshInformationRows.Count(r => r.IsQuad && r.Node4 > 0);
                        if (activeMeshType is "Q4" or "Q8"
                            && TryFindInvalidNativeQuadRow(_meshSource, activeMeshType, out var invalidQuadMessage))
                        {
                            // #region agent log
                            AgentDebugRuntimeLog.Write("post-fix", "H3", "FEAnalysisViewModel.cs:OnRunAnalysis",
                                "blocked invalid native Q4/Q8 mesh before solver run",
                                new
                                {
                                    activeMeshType,
                                    invalidQuadMessage,
                                    mesh = AgentDebugRuntimeLog.SummarizeMesh(_meshSource)
                                });
                            // #endregion
                            StatusMessage = "SRF run cancelled — native Q4/Q8 mesh connectivity is invalid.";
                            MessageBox.Show(
                                invalidQuadMessage + "\n\nRegenerate the Q4/Q8 mesh before running FE Analysis.",
                                "SRF mesh connectivity",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }
                        if (quadRows > 0 && string.Equals(activeMeshType, "T3", StringComparison.OrdinalIgnoreCase))
                        {
                            var regen = MessageBox.Show(
                                $"The mesh contains {quadRows} quadrilateral element row(s), but it is not a homogeneous Q4/Q8 mesh.\n\n" +
                                "Native Q4/Q8 SRF runs require all elements to have matching quadrilateral connectivity. Regenerate as all-Q4/all-Q8, or use native T3 triangles.\n\n" +
                                "Run anyway with the current Q4-to-T3 fallback export?",
                                "SRF mesh warning",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                            if (regen != MessageBoxResult.Yes)
                            {
                                StatusMessage = "SRF run cancelled — regenerate as all-Q4/all-Q8 or native T3 triangles.";
                                return;
                            }
                        }
                    }

                    var workDir = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_" + programName + "_script");
                    Directory.CreateDirectory(workDir);
                    P610T3RunResult routedResult;
                    string? solverHealthWarning = null;
                    var srfProgramKind = srfRoute.Core == SrfLiteratureOptimalRouting.SolverCore.CoreB
                        ? SlopeSrfTrialExecutor.SrfProgramKind.CoreBBranded
                        : SlopeSrfTrialExecutor.SrfProgramKind.T3Family;
                    var scriptBaseName = srfRoute.Core == SrfLiteratureOptimalRouting.SolverCore.CoreB
                        ? "fe_script_run"
                        : "p610t3_script_run";
                    var srfIterationLimit = ResolveSrfIterationLimitForRun(datContent);

                    if (IsSrfBracketMode)
                    {
                        var bracketError = ValidateSrfBracketSettings();
                        if (bracketError != null)
                        {
                            StatusMessage = $"SRF bracket settings invalid: {bracketError}";
                            MessageBox.Show(bracketError, "FE Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        SlopeSrfBracketRunner.BracketRunResult bracket;
                        try
                        {
                            bracket = await SlopeSrfBracketRunner.RunAsync(
                                _runner,
                                srfProgramKind,
                                programName,
                                datContent,
                                scriptBaseName,
                                BuildSrfBracketSettings(),
                                srfIterationLimit,
                                workDir,
                                onProgress: SetStatusMessageFromAnyThread,
                                onBracketDetected: SetDetectedSrfBracketPreviewFromAnyThread,
                                cancellationToken: cancellationToken,
                                brandedProgram: srfRoute.Executable);
                        }
                        catch (InvalidOperationException ex)
                        {
                            _lastExecutionStatus = "Failed";
                            var msg = AppendCoupledGwBracketHint(ex.Message);
                            StatusMessage = $"SRF bracket search failed: {msg}";
                            EndAnalysisRunState();
                            MessageBox.Show(msg, "SRF Bracket Search", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        routedResult = bracket.JsonResult;
                        _resultsVm.SetFemCppArtifactSummary(
                            P64ResFileParser.FormatSummaryText(bracket.ResSummary, workDir, scriptBaseName)
                            + "\n\n" + bracket.BracketSummary);
                        _resultsVm.ApplyP64ResSummary(bracket.ResSummary, datContent, srfIterationLimit);
                        _resultsVm.ApplySrfBracketSummary(bracket.CriticalFoS, bracket.BracketSummary);
                        ApplyNewmarkPostProcess(bracket.CriticalFoS);
                        solverHealthWarning = bracket.HasNonFiniteDisplacements
                            ? bracket.SolverHealthWarning ?? "SRF solver returned non-finite displacements; see Force-Displacement tab."
                            : bracket.BracketSummary;
                    }
                    else
                    {
                        var fValues = CollectSrfTrialValuesForRun();
                        if (fValues.Count > 1)
                        {
                            var multi = await SlopeT3MultiSrfRunner.RunTrialsAsync(
                                _runner,
                                srfProgramKind,
                                programName,
                                datContent,
                                scriptBaseName,
                                fValues,
                                srfIterationLimit,
                                workDir,
                                cancellationToken,
                                srfRoute.Executable);
                            routedResult = multi.JsonResult;
                            _resultsVm.SetFemCppArtifactSummary(
                                P64ResFileParser.FormatSummaryText(multi.ResSummary, workDir, scriptBaseName));
                            _resultsVm.ApplyP64ResSummary(multi.ResSummary, datContent, srfIterationLimit);
                            ApplyNewmarkPostProcess(_resultsVm.EstimatedSrfFoS);
                            if (multi.HasNonFiniteDisplacements)
                                solverHealthWarning = multi.SolverHealthWarning
                                    ?? "SRF solver returned non-finite displacements; see Force-Displacement tab.";
                        }
                        else
                        {
                            var trialDat = datContent;
                            if (fValues.Count == 1)
                                P610AnalysisOptionsHelper.TryPatchSrfFSingleTrial(datContent, fValues[0], out trialDat, out _);
                            routedResult = srfRoute.Core == SrfLiteratureOptimalRouting.SolverCore.CoreB
                                ? await _runner.RunNativeSlopeSrfFromDatContentAsync(
                                    programName, trialDat, scriptBaseName + "_single", workDir, cancellationToken, srfRoute.Executable)
                                : await _runner.RunP610T3VariantFromDatContentAsync(
                                    programName, trialDat, scriptBaseName + "_single", workDir, cancellationToken);
                            _resultsVm.ClearSrfConvergencePlot();
                        }
                    }

                    // #region agent log
                    AgentDebugRuntimeLog.Write("pre-fix", "H1,H2,H3,H5", "FEAnalysisViewModel.cs:OnRunAnalysis",
                        "after native SRF run before applying result",
                        new
                        {
                            programName,
                            result = AgentDebugRuntimeLog.SummarizeResult(routedResult),
                            meshAfterRun = AgentDebugRuntimeLog.SummarizeMesh(_meshSource)
                        });
                    // #endregion
                    ApplyP610T3Result(routedResult, programName, datContent);
                    StatusMessage = solverHealthWarning
                        ?? $"{programName} complete. {routedResult.Nodes.Count} nodes, {routedResult.Elements.Count} elements, {routedResult.Stages.Count} stage(s).";
                    NavigateToResultsVisualization();
                    return;
                }

                if (string.Equals(programName, "p64", StringComparison.OrdinalIgnoreCase))
                {
                    var femProgramId = FemCppProgramRouter.ResolveProgramId(datContent) ?? "p64";
                    var workDirFem = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_fem_cpp_script");
                    Directory.CreateDirectory(workDirFem);

                    if (IsSrfBracketMode)
                    {
                        var bracketError = ValidateSrfBracketSettings();
                        if (bracketError != null)
                        {
                            StatusMessage = $"SRF bracket settings invalid: {bracketError}";
                            MessageBox.Show(bracketError, "FE Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        var femSrfIterationLimit = ResolveSrfIterationLimitForRun(datContent);
                        SlopeSrfBracketRunner.BracketRunResult bracket;
                        try
                        {
                            bracket = await SlopeSrfBracketRunner.RunAsync(
                                _runner,
                                SlopeSrfTrialExecutor.SrfProgramKind.P64,
                                femProgramId,
                                datContent,
                                "fe_script_run",
                                BuildSrfBracketSettings(),
                                femSrfIterationLimit,
                                workDirFem,
                                onProgress: SetStatusMessageFromAnyThread,
                                onBracketDetected: SetDetectedSrfBracketPreviewFromAnyThread,
                                cancellationToken: cancellationToken);
                        }
                        catch (InvalidOperationException ex)
                        {
                            _lastExecutionStatus = "Failed";
                            var msg = AppendCoupledGwBracketHint(ex.Message);
                            StatusMessage = $"SRF bracket search failed: {msg}";
                            EndAnalysisRunState();
                            MessageBox.Show(msg, "SRF Bracket Search", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        var bracketArtifact = P64ResFileParser.FormatSummaryText(bracket.ResSummary, workDirFem, "fe_script_run")
                            + "\n\n" + bracket.BracketSummary;
                        _resultsVm.SetFemCppArtifactSummary(bracketArtifact);
                        _resultsVm.ApplyP64ResSummary(bracket.ResSummary, datContent, femSrfIterationLimit);
                        _resultsVm.ApplySrfBracketSummary(bracket.CriticalFoS, bracket.BracketSummary);
                        ApplyNewmarkPostProcess(bracket.CriticalFoS);
                        _resultsVm.LoadP610T3Result(bracket.JsonResult, bracket.BracketSummary, datContent: datContent, meshGeometrySource: _meshSource);
                        _resultsVm.StageSummary = bracketArtifact;
                        StatusMessage = bracket.HasNonFiniteDisplacements
                            ? (bracket.SolverHealthWarning ?? "SRF solver returned non-finite displacements; see Force-Displacement tab.")
                            : bracket.BracketSummary;
                        NavigateToResultsVisualization();
                        return;
                    }

                    var femResult = await _runner.RunFemCppFromDatContentAsync(
                        femProgramId, datContent, "fe_script_run", workDirFem, cancellationToken);
                    var femMsg = $"fem_cpp {femProgramId}: {femResult.JsonResult.Nodes.Count} nodes, " +
                                 $"{femResult.JsonResult.Elements.Count} elements, " +
                                 $"{femResult.JsonResult.Stages.Count} stage(s).";
                    var artifactSummary = P64ResFileParser.FormatSummaryText(
                        femResult.ResSummary, femResult.WorkDir, femResult.BaseName);
                    _resultsVm.SetFemCppArtifactSummary(artifactSummary);
                    if (femResult.ResSummary.Trials.Count > 0)
                    {
                        _resultsVm.ApplyP64ResSummary(femResult.ResSummary, datContent, _maxIterations);
                        ApplyNewmarkPostProcess(_resultsVm.EstimatedSrfFoS);
                    }
                    else
                        _resultsVm.ClearSrfConvergencePlot();
                    _resultsVm.LoadP610T3Result(femResult.JsonResult, femMsg, datContent: datContent, meshGeometrySource: _meshSource);
                    _resultsVm.StageSummary = artifactSummary;
                    StatusMessage = femMsg;
                    NavigateToResultsVisualization();
                    return;
                }

                var workDirP610 = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_" + programName + "_script");
                Directory.CreateDirectory(workDirP610);
                var p610Result = await _runner.RunP610T3VariantFromDatContentAsync(
                    programName, datContent, "p610t3_script_run", workDirP610, cancellationToken);
                ApplyP610T3Result(p610Result, programName, datContent);
                StatusMessage = $"{programName} complete. {p610Result.Nodes.Count} nodes, {p610Result.Elements.Count} elements, {p610Result.Stages.Count} stage(s).";
                NavigateToResultsVisualization();
    }

    /// <summary>
    /// Builds a Chapter-7 seepage deck (p72/p73/p74) from the current mesh + material
    /// permeabilities + Project Settings seepage options, runs fem_cpp, and installs
    /// Total Head / Pressure Head contours into Results. Feeds pore pressures into the
    /// Groundwater water-pressure grid for SRF coupling.
    /// </summary>
    /// <returns>True when seepage solved and results were installed.</returns>
    private async Task<bool> RunSeepageAnalysisAsync(
        CancellationToken cancellationToken,
        string? detailedTypeOverride = null,
        bool navigateToResults = true)
    {
        var detailedType = detailedTypeOverride
            ?? _materialProperties?.SelectedDetailedAnalysisType
            ?? "";
        var programId = SeepageDatContentBuilder.ResolveProgramId(detailedType);
        if (programId == null)
        {
            StatusMessage = "Select a seepage analysis type in Project Settings → Analysis.";
            return false;
        }

        if (_meshSource is not { IsDiscretized: true, MeshInformationRows.Count: > 0 })
        {
            StatusMessage = "Seepage analysis requires a discretized mesh.";
            MessageBox.Show(
                $"'{detailedType}' needs a mesh. Generate the mesh on the Mesh tab, then run the analysis again.",
                "Seepage Analysis",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var settings = _mainWindow?.ProjectSettingsVM?.SeepageSettings ?? new SeepageAnalysisSettings();
        var gw = _mainWindow?.GroundwaterVM;
        // When coupling from SRF selection, IsWaterHeadAllowed is false (GW type not selected);
        // still honor Water Head for the seepage solver being run.
        var waterHeadForSolver = settings.GetIncludeWaterHeadForType(detailedType);
        var useInteractiveHeads = waterHeadForSolver
            && gw != null
            && (gw.IsWaterHeadAllowed || gw.WaterHeads.Count > 0);
        if (useInteractiveHeads && gw!.WaterHeads.Count > 0)
            gw.SyncSeepageSettingsFromWaterHeads();
        string datContent;
        try
        {
            datContent = SeepageDatContentBuilder.Build(
                detailedType,
                _meshSource,
                _materialProperties!,
                settings,
                useInteractiveHeads ? gw!.WaterHeads.ToList() : null,
                useInteractiveHeads);
        }
        catch (InvalidOperationException ex)
        {
            _lastExecutionStatus = "Failed";
            StatusMessage = $"Seepage deck error: {ex.Message}";
            EndAnalysisRunState();
            MessageBox.Show(ex.Message, "Seepage Analysis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        StatusMessage = $"Entered Seepage (GW flow) analysis — running fem_cpp {programId}...";
        var workDir = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_seepage");
        var run = await _runner.RunSeepageFromDatContentAsync(
            programId, datContent, "seepage_run", workDir, cancellationToken);

        _resultsVm.ClearSrfConvergencePlot();
        _resultsVm.SetFemCppArtifactSummary(
            $"fem_cpp {programId} — steady seepage\nWork dir: {run.WorkDir}\n\n{run.ResText}");
        var originalDomainOutline = string.Equals(programId, "p73", StringComparison.OrdinalIgnoreCase)
            ? BuildOriginalSeepageDomainOutline(_meshSource.MeshInformationRows)
            : null;
        _resultsVm.LoadSeepageResult(run.Result, originalDomainOutline: originalDomainOutline);
        _resultsVm.StageSummary = _resultsVm.FeStatusMessage;
        StatusMessage = _resultsVm.FeStatusMessage;
        // Feed seepage pore pressures into Static water-pressure grid for subsequent SRF runs.
        _mainWindow?.GroundwaterVM?.ApplySeepageResultsAsPorePressureGrid(run.Result);

        _lastExecutionStatus = "Completed";
        _lastTotalTime = "-";
        _lastNodeCount = run.Result.NodeCount > 0 ? run.Result.NodeCount : run.Result.Nodes.Count;
        _lastElementCount = run.Result.ElementCount > 0 ? run.Result.ElementCount : run.Result.Elements.Count;
        OnPropertyChanged(nameof(ExecutionStatus));
        OnPropertyChanged(nameof(TotalExecutionTime));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(ElementCount));

        if (navigateToResults)
            NavigateToResultsVisualization();
        return true;
    }

    /// <summary>
    /// Builds a Chapter-8 p84/p88 transient consolidation deck, runs fem_cpp, and loads
    /// nodal pressures into Results (same JSON schema as seepage, pressure as total_head).
    /// </summary>
    private async Task<bool> RunTransientAnalysisAsync(CancellationToken cancellationToken)
    {
        var detailedType = _materialProperties?.SelectedDetailedAnalysisType ?? "";
        if (TransientDatContentBuilder.ResolveProgramId(detailedType) == null)
        {
            StatusMessage = "Select a Transient State solver in Project Settings → Analysis.";
            return false;
        }

        if (_meshSource is not { IsDiscretized: true, MeshInformationRows.Count: > 0 })
        {
            StatusMessage = "Transient analysis requires a discretized mesh.";
            MessageBox.Show(
                $"'{detailedType}' needs a mesh. Generate the mesh on the Mesh tab, then run the analysis again.",
                "Transient Analysis",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var settings = _mainWindow?.ProjectSettingsVM?.TransientSettings ?? new TransientAnalysisSettings();
        string programId;
        string datContent;
        try
        {
            (programId, datContent) = TransientDatContentBuilder.BuildWithProgramId(
                detailedType,
                _meshSource,
                _materialProperties!,
                settings,
                _mainWindow?.GroundwaterVM?.TransientFixedPressureNodes);
        }
        catch (InvalidOperationException ex)
        {
            _lastExecutionStatus = "Failed";
            StatusMessage = $"Transient deck error: {ex.Message}";
            EndAnalysisRunState();
            MessageBox.Show(ex.Message, "Transient Analysis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        StatusMessage = $"Entered Transient consolidation analysis — running fem_cpp {programId}...";
        var workDir = Path.Combine(Path.GetTempPath(), "GeotechSlopeApp_transient");
        var run = await _runner.RunSeepageFromDatContentAsync(
            programId, datContent, "transient_run", workDir, cancellationToken);

        _resultsVm.ClearSrfConvergencePlot();
        _resultsVm.SetFemCppArtifactSummary(
            $"fem_cpp {programId} — transient consolidation\nWork dir: {run.WorkDir}\n\n{run.ResText}");
        if (run.TransientResult != null && run.TransientResult.Steps.Count > 0)
            _resultsVm.LoadTransientResult(run.TransientResult);
        else
            _resultsVm.LoadSeepageResult(run.Result);
        _resultsVm.StageSummary = _resultsVm.FeStatusMessage;
        StatusMessage = _resultsVm.FeStatusMessage;
        _mainWindow?.GroundwaterVM?.ApplySeepageResultsAsPorePressureGrid(run.Result);

        _lastExecutionStatus = "Completed";
        _lastTotalTime = "-";
        _lastNodeCount = run.Result.NodeCount > 0 ? run.Result.NodeCount : run.Result.Nodes.Count;
        _lastElementCount = run.Result.ElementCount > 0 ? run.Result.ElementCount : run.Result.Elements.Count;
        OnPropertyChanged(nameof(ExecutionStatus));
        OnPropertyChanged(nameof(TotalExecutionTime));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(ElementCount));

        NavigateToResultsVisualization();
        return true;
    }

    private static IReadOnlyList<(double X, double Y)> BuildOriginalSeepageDomainOutline(
        IEnumerable<MeshInformationRow> rows)
    {
        var points = rows.SelectMany(r => new[]
        {
            (r.N1X, r.N1Y),
            (r.N2X, r.N2Y),
            (r.N3X, r.N3Y),
            (r.N4X, r.N4Y),
        }).ToList();
        if (points.Count == 0)
            return Array.Empty<(double X, double Y)>();

        double minX = points.Min(p => p.Item1);
        double maxX = points.Max(p => p.Item1);
        double minY = points.Min(p => p.Item2);
        double maxY = points.Max(p => p.Item2);
        return new[]
        {
            (minX, minY),
            (maxX, minY),
            (maxX, maxY),
            (minX, maxY),
        };
    }

    /// <summary>Marks the run finished so Execution Details popups update before modal dialogs.</summary>
    private void EndAnalysisRunState()
    {
        EndAnalysisCancellation();
        if (IsSolving)
            IsSolving = false;
        else
            OnPropertyChanged(nameof(ExecutionStatus));
    }

    /// <summary>
    /// Bracket/sweep convergence uses the larger of the GUI limit and the limit written in the .dat deck.
    /// </summary>
    private int ResolveSrfIterationLimitForRun(string? datContent)
    {
        var deckLimit = P64ResFileParser.TryParseIterationLimitFromDat(
            datContent,
            SrfExplicitDeckFreshness.DefaultSrfIterationLimit);
        return Math.Max(_maxIterations, deckLimit);
    }

    /// <summary>Mesh top Y used to sanity-check seepage total heads before coupled SRF.</summary>
    private double GetMeshMaxY()
    {
        var rows = _meshSource?.MeshInformationRows;
        if (rows == null || rows.Count == 0)
            return 0.0;
        double maxY = double.NegativeInfinity;
        foreach (var r in rows)
        {
            maxY = Math.Max(maxY, r.N1Y);
            maxY = Math.Max(maxY, r.N2Y);
            maxY = Math.Max(maxY, r.N3Y);
            if (r.Node4 > 0) maxY = Math.Max(maxY, r.N4Y);
        }
        return double.IsFinite(maxY) ? maxY : 0.0;
    }

    /// <summary>
    /// Temporarily select the SRF analysis category so <see cref="RunP610OrSrfAnalysisAsync"/>
    /// uses IsSrfAnalysisMode routing, then restore the previous category/type.
    /// </summary>
    /// <param name="restoreSeepageType">
    /// When restoring a Groundwater category after Steady coupling, preferred seepage solver type
    /// (falls back to saved type / LastSteadyStateSolverType). Null for Static coupling — restore Static leaf.
    /// </param>
    private async Task RunCoupledSrfWithTempCategoryAsync(
        CancellationToken cancellationToken,
        string? restoreSeepageType)
    {
        var mats = _materialProperties!;
        var savedCat = mats.SelectedAnalysisCategory;
        var savedType = mats.SelectedDetailedAnalysisType;
        try
        {
            mats.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
            await RunP610OrSrfAnalysisAsync(cancellationToken);
        }
        finally
        {
            mats.SelectedAnalysisCategory = savedCat;
            if (MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(savedCat))
            {
                if (restoreSeepageType != null
                    || MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(savedType)
                    || IsCoupledSteadyGroundwaterMode)
                {
                    mats.SelectedDetailedAnalysisType =
                        MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(savedType)
                            ? savedType
                            : (mats.LastSteadyStateSolverType ?? restoreSeepageType
                               ?? MaterialPropertiesViewModel.GroundwaterSeepageGeneralType);
                }
                else
                {
                    mats.SelectedDetailedAnalysisType =
                        string.Equals(
                            savedType,
                            MaterialPropertiesViewModel.GroundwaterStaticType,
                            StringComparison.Ordinal)
                            ? savedType
                            : MaterialPropertiesViewModel.GroundwaterStaticType;
                }
            }
            else if (savedType != null)
            {
                mats.SelectedDetailedAnalysisType = savedType;
            }
        }
    }

    /// <summary>
    /// Warn when Coupled Steady → SRF uses upstream total head above the mesh top (artesian).
    /// </summary>
    private bool ConfirmCoupledSeepageHeadsForSrf()
    {
        var settings = _mainWindow?.ProjectSettingsVM?.SeepageSettings;
        var gw = _mainWindow?.GroundwaterVM;
        if (settings != null && gw != null && gw.WaterHeads.Count > 0
            && (gw.IsWaterHeadAllowed || settings.GetIncludeWaterHeadForType(ResolveCoupledSeepageSolverType())))
            gw.SyncSeepageSettingsFromWaterHeads();

        double hUp = settings?.UpstreamHead ?? 0.0;
        double hDown = settings?.DownstreamHead ?? 0.0;
        if (gw?.WaterHeads is { Count: > 0 })
        {
            foreach (var wh in gw.WaterHeads.Where(h => h.IsEnabled))
            {
                if (wh.Role == WaterHeadRole.Upstream) hUp = Math.Max(hUp, wh.Head);
                if (wh.Role == WaterHeadRole.Downstream) hDown = Math.Max(hDown, wh.Head);
            }
        }

        double maxY = GetMeshMaxY();
        if (maxY <= 0 || hUp <= maxY * 1.01)
            return true;

        var answer = MessageBox.Show(
            $"Upstream total head H₁ = {hUp:G4} is above the mesh top (y = {maxY:G4}).\n\n" +
            "That drives artesian pore pressures into Slope SRF and often prevents every strength-reduction " +
            "trial from converging (bracket expands downward with no stable F).\n\n" +
            $"Downstream H₂ = {hDown:G4}.\n\n" +
            "Lower Water Head / H₁ toward the water-table elevation (typically ≤ mesh top), " +
            "or cancel and use Static + Water Surfaces instead of Coupled Steady seepage.\n\n" +
            "Continue with these heads anyway?",
            "Coupled seepage → SRF",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            StatusMessage = "Coupled SRF cancelled — reduce seepage Water Head (H₁) or use Static groundwater.";
            return false;
        }
        return true;
    }

    /// <summary>Warn after seepage→grid install when u_max is large vs strength / overburden.</summary>
    private bool ConfirmCoupledSeepagePorePressureForSrf()
    {
        var gw = _mainWindow?.GroundwaterVM;
        if (gw == null) return true;
        var model = gw.BuildModel();
        var mat = _materialProperties?.Materials?.FirstOrDefault();
        var hint = SrfGroundwaterExport.BuildHighPorePressureHint(
            model,
            GetMeshMaxY(),
            mat?.Cohesion,
            mat?.UnitWeight);
        if (hint == null)
            return true;

        var answer = MessageBox.Show(
            hint + "\n\nContinue with Slope SRF anyway?",
            "Coupled seepage → SRF",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            StatusMessage = "Coupled SRF cancelled — seepage pore pressures are too high for a stable SRF bracket.";
            return false;
        }
        return true;
    }

    private string AppendCoupledGwBracketHint(string message)
    {
        if (!UseCoupledGroundwaterSrf && _mainWindow?.GroundwaterVM?.IsEnabled != true)
            return message;
        var gw = _mainWindow?.GroundwaterVM;
        if (gw == null) return message;
        var hint = SrfGroundwaterExport.BuildHighPorePressureHint(
            gw.BuildModel(),
            GetMeshMaxY(),
            _materialProperties?.Materials?.FirstOrDefault()?.Cohesion,
            _materialProperties?.Materials?.FirstOrDefault()?.UnitWeight);
        if (hint == null)
            return message;
        return message + "\n\n" + hint;
    }

    private void ApplyP41FeResult(P41ResFileParser.P41ParseResult result)
    {
        _lastExecutionStatus = "Completed";
        _lastTotalTime = "-";
        _lastElementCount = result.Elements.Count;
        _lastNodeCount = result.Nodes.Count;
        _nodes = result.Nodes.ToList();
        _elements = result.Elements.ToList();
        OnPropertyChanged(nameof(ExecutionStatus));
        OnPropertyChanged(nameof(TotalExecutionTime));
        OnPropertyChanged(nameof(ElementCount));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(Nodes));
        OnPropertyChanged(nameof(Elements));

        _resultsVm.FENodes = _nodes;
        _resultsVm.FEElements = _elements;
        _resultsVm.FeElementStresses = result.ElementStresses.ToList();
        _resultsVm.FeNodalDisplacements = result.Nodes.Select(n =>
        {
            double u = n.Displacements.Length > 0 ? n.Displacements[0] : 0;
            double v = n.Displacements.Length > 1 ? n.Displacements[1] : 0;
            return (object)new
            {
                NodeId = n.Id,
                X = n.X,
                Y = n.Y,
                U = u,
                V = v,
                DisplacementMagnitude = Math.Sqrt(u * u + v * v)
            };
        }).ToList<object>();
        _resultsVm.FeStatusMessage = $"p41 results: {result.Nodes.Count} nodes, {result.Elements.Count} elements (from Script Editor .dat).";
        _resultsVm.StagedAnalysisResult = null;
    }

    /// <summary>
    /// Apply a p610_t3-family JSON result (single-stage <c>p610_t3_elastic</c> or multi-stage
    /// <c>p610_t3</c>) to this VM and the shared <see cref="ResultsViewModel"/>. Mirrors the
    /// hand-off the Script Editor uses for its Run-from-Script flow.
    /// </summary>
    private void ApplyP610T3Result(P610T3RunResult result, string solverLabel, string? datContent = null)
    {
        _lastExecutionStatus = "Completed";
        _lastTotalTime = "-";
        _lastElementCount = result.Elements.Count;
        _lastNodeCount = result.Nodes.Count;
        _nodes = result.Nodes.ToList();
        _elements = result.Elements.Cast<ElementBase>().ToList();
        OnPropertyChanged(nameof(ExecutionStatus));
        OnPropertyChanged(nameof(TotalExecutionTime));
        OnPropertyChanged(nameof(ElementCount));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(Nodes));
        OnPropertyChanged(nameof(Elements));

        var msg = $"{solverLabel} complete: {result.Nodes.Count} nodes, {result.Elements.Count} elements, {result.Stages.Count} stage(s) — see Results & Visualization.";
        _resultsVm.LoadP610T3Result(
            result, msg, datContent: datContent ?? _scriptVm?.ScriptContent, meshGeometrySource: _meshSource);
    }

    private void ApplyResult(P51ResFileParser.P51ParseResult result)
    {
        _lastExecutionStatus = "Completed";
        _lastTotalTime = "-";
        _lastElementCount = result.Elements.Count;
        _lastNodeCount = result.Nodes.Count;
        OnPropertyChanged(nameof(ExecutionStatus));
        OnPropertyChanged(nameof(TotalExecutionTime));
        OnPropertyChanged(nameof(ElementCount));
        OnPropertyChanged(nameof(NodeCount));

        _resultsVm.FENodes = result.Nodes;
        _resultsVm.FEElements = result.Elements.ToList<ElementBase>();
        _resultsVm.FeElementStresses = result.ElementStresses;
        _resultsVm.FeNodalDisplacements = result.Nodes.Select(n =>
        {
            double u = n.Displacements.Length > 0 ? n.Displacements[0] : 0;
            double v = n.Displacements.Length > 1 ? n.Displacements[1] : 0;
            return (object)new
            {
                NodeId = n.Id,
                X = n.X,
                Y = n.Y,
                U = u,
                V = v,
                DisplacementMagnitude = Math.Sqrt(u * u + v * v)
            };
        }).ToList<object>();
        _resultsVm.FeStatusMessage = $"Results: {result.Nodes.Count} nodes, {result.Elements.Count} elements.";
        _resultsVm.StagedAnalysisResult = null;
    }

    private void OnLoadMeshData()
    {
        if (_meshSource == null || !_meshSource.IsDiscretized || _meshSource.DiscretizationNodes.Count == 0)
        {
            StatusMessage = "No mesh to load. Open the Mesh popup on the Mesh tab and generate a mesh (e.g. 1D mesh along geometry).";
            MessageBox.Show(
                "Generate a mesh first in the Mesh tab (Mesh popup), then click Load Mesh Data again.",
                "Load Mesh Data",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var disc = _meshSource.DiscretizationNodes;
        var nodes = new List<Node>();
        for (int i = 0; i < disc.Count; i++)
        {
            var L = disc[i].Location;
            nodes.Add(new Node
            {
                Id = i + 1,
                X = L.X,
                Y = L.Y,
                Displacements = new[] { 0.0, 0.0 }
            });
        }

        var elements = new List<ElementBase>();
        foreach (var me in _meshSource.MeshElements)
        {
            if (!me.IsActive || me.Nodes == null || me.Nodes.Count == 0)
                continue;

            if (me.Nodes.Count == 2)
            {
                var p0 = me.Nodes[0];
                var p1 = me.Nodes[1];
                int i0 = FindDiscretizationNodeIndex(disc, p0.X, p0.Y);
                int i1 = FindDiscretizationNodeIndex(disc, p1.X, p1.Y);
                if (i0 < 0 || i1 < 0)
                    continue;
                elements.Add(new LinearRodElement
                {
                    Id = me.DisplayId,
                    NodeIds = new[] { i0, i1 }
                });
            }
            else if (me.Nodes.Count >= 3)
            {
                // T3: three corner vertices. T6 outline on the mesh tab is N1–N4–N2–N5–N3–N6 (corner, midside, …);
                // using Points[0..2] would tie a corner to a midside and a corner — wrong linear triangle for FE import / contours.
                Point p0, p1, p2;
                if (me.Nodes.Count >= 6)
                {
                    p0 = me.Nodes[0];
                    p1 = me.Nodes[2];
                    p2 = me.Nodes[4];
                }
                else
                {
                    p0 = me.Nodes[0];
                    p1 = me.Nodes[1];
                    p2 = me.Nodes[2];
                }
                int i0 = FindDiscretizationNodeIndex(disc, p0.X, p0.Y);
                int i1 = FindDiscretizationNodeIndex(disc, p1.X, p1.Y);
                int i2 = FindDiscretizationNodeIndex(disc, p2.X, p2.Y);
                if (i0 < 0 || i1 < 0 || i2 < 0)
                    continue;
                elements.Add(new LinearTriangleElement
                {
                    Id = me.DisplayId,
                    NodeIds = new[] { i0, i1, i2 }
                });
            }
        }

        if (elements.Count == 0)
        {
            StatusMessage = "Mesh has nodes but no importable elements (need 2-node lines or 3-node triangles matching node coordinates).";
            MessageBox.Show(
                "Could not build finite elements from the current mesh. Check that mesh elements reference the discretization nodes.",
                "Load Mesh Data",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _nodes = nodes;
        _elements = elements;
        _lastNodeCount = nodes.Count;
        _lastElementCount = elements.Count;
        OnPropertyChanged(nameof(Nodes));
        OnPropertyChanged(nameof(Elements));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(ElementCount));
        OnPropertyChanged(nameof(MeshStatsBaseLine));

        // Pull boundary conditions defined in the Boundary Conditions tab into the FE
        // Analysis tab. Rows are kept verbatim (live references) but skipped when their
        // NodeId is out of range for the just-loaded mesh, so a stale BC list from a
        // previous mesh doesn't silently leak into the analysis.
        int bcImported = 0, bcSkipped = 0;
        _boundaryConditions.Clear();
        if (_boundaryConditionsSource != null)
        {
            foreach (var row in _boundaryConditionsSource.BoundaryConditions)
            {
                if (row.NodeId >= 1 && row.NodeId <= nodes.Count)
                {
                    _boundaryConditions.Add(row);
                    bcImported++;
                }
                else
                {
                    bcSkipped++;
                }
            }
        }
        OnPropertyChanged(nameof(BoundaryConditions));
        OnPropertyChanged(nameof(MeshStatsBaseLine));

        _resultsVm.FENodes = nodes;
        _resultsVm.FEElements = elements;
        _resultsVm.FeNodalDisplacements = nodes.Select(n =>
        {
            double u = n.Displacements.Length > 0 ? n.Displacements[0] : 0;
            double v = n.Displacements.Length > 1 ? n.Displacements[1] : 0;
            return (object)new
            {
                NodeId = n.Id,
                X = n.X,
                Y = n.Y,
                U = u,
                V = v,
                DisplacementMagnitude = Math.Sqrt(u * u + v * v)
            };
        }).ToList<object>();
        _resultsVm.FeElementStresses = new List<ElementStress>();
        _resultsVm.StagedAnalysisResult = null;
        _resultsVm.FeStatusMessage = $"Mesh imported: {nodes.Count} nodes, {elements.Count} elements (from Mesh tab). Displacements zero until you run an analysis.";

        string bcSummary = _boundaryConditionsSource == null
            ? ""
            : (bcImported == 0 && bcSkipped == 0
                ? " No boundary conditions defined yet — set them in the Boundary Conditions tab."
                : bcSkipped == 0
                    ? $" Imported {bcImported} boundary condition(s) from the Boundary Conditions tab."
                    : $" Imported {bcImported} boundary condition(s) from the Boundary Conditions tab; skipped {bcSkipped} row(s) whose NodeId is out of range for this mesh.");
        StatusMessage = $"Loaded mesh from Mesh tab: {nodes.Count} nodes, {elements.Count} elements.{bcSummary}";
        RefreshRunPseudoStaticDisplay();
        RefreshResolvedRunRoutingDisplay();
        if (HasRunPseudoStatic)
        {
            StatusMessage += $" Pseudo-static: k_h = {RunPseudoStaticKh:G4}, k_v = {RunPseudoStaticKv:G4}.";
        }
    }

    /// <summary>Match axis coordinates to discretization node index (0-based).</summary>
    private static int FindDiscretizationNodeIndex(ObservableCollection<MeshDiscretizationNodeDisplay> disc, double x, double y)
    {
        const double tol = 1e-5;
        for (int i = 0; i < disc.Count; i++)
        {
            var L = disc[i].Location;
            if (Math.Abs(L.X - x) <= tol && Math.Abs(L.Y - y) <= tol)
                return i;
        }
        const double tolLoose = 1e-3;
        for (int i = 0; i < disc.Count; i++)
        {
            var L = disc[i].Location;
            if (Math.Abs(L.X - x) <= tolLoose && Math.Abs(L.Y - y) <= tolLoose)
                return i;
        }
        return -1;
    }

    private void OnAddStageDefinition()
    {
        RecordUndo("Add analysis stage");
        var next = _stageDefinitions.Count == 0 ? 1 : _stageDefinitions.Max(s => s.StageNumber) + 1;
        var stage = new StageDefinition
        {
            StageNumber = next,
            StageName = string.Empty,
            StageType = string.Empty,
        };
        _stageDefinitions.Add(stage);
        SelectedStageDefinition = stage;
    }

    private void OnRemoveStageDefinition()
    {
        if (_selectedStageDefinition == null) return;
        RecordUndo("Remove analysis stage");
        var idx = _stageDefinitions.IndexOf(_selectedStageDefinition);
        _stageDefinitions.Remove(_selectedStageDefinition);
        RenumberStages();
        SelectedStageDefinition = _stageDefinitions.Count > 0
            ? _stageDefinitions[Math.Min(idx, _stageDefinitions.Count - 1)]
            : null;
    }

    private void OnMoveStageUp()
    {
        if (_selectedStageDefinition == null) return;
        var idx = _stageDefinitions.IndexOf(_selectedStageDefinition);
        if (idx <= 0) return;
        _stageDefinitions.Move(idx, idx - 1);
        RenumberStages();
    }

    private void OnMoveStageDown()
    {
        if (_selectedStageDefinition == null) return;
        var idx = _stageDefinitions.IndexOf(_selectedStageDefinition);
        if (idx < 0 || idx >= _stageDefinitions.Count - 1) return;
        _stageDefinitions.Move(idx, idx + 1);
        RenumberStages();
    }

    private void RenumberStages()
    {
        for (int i = 0; i < _stageDefinitions.Count; i++)
            _stageDefinitions[i].StageNumber = i + 1;
    }

    private void OnClearMeshData()
    {
        _nodes.Clear();
        _elements.Clear();
        _boundaryConditions.Clear();
        OnPropertyChanged(nameof(Nodes));
        OnPropertyChanged(nameof(Elements));
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(ElementCount));
        OnPropertyChanged(nameof(BoundaryConditions));
        _resultsVm.FENodes = new List<Node>();
        _resultsVm.FEElements = new List<ElementBase>();
        _resultsVm.FeElementStresses = new List<ElementStress>();
        _resultsVm.FeNodalDisplacements = new List<object>();
        StatusMessage = "Mesh data cleared.";
    }

    private void OnRunVerification()
    {
        VerificationResult = new { Success = false, Summary = "Verification not implemented in Phase 1." };
    }

    private void OnRunSelectedStage() { }
    private void OnNextStage() { }

    // ════════════════════════════════════════════════════════════════════════
    // STAGED ANALYSIS SOLVER
    // Solves sequentially stage-by-stage (RS2 / Plaxis pattern):
    //   1. Build full stiffness matrix K from all elements
    //   2. For each stage, zero stiffness rows/cols for excavated elements
    //   3. Solve K·u = f, accumulate nodal displacements
    //   4. Carry stress increments forward to next stage
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Set by MainWindowViewModel so the staged solver can read excavation data.</summary>
    public StagedConstructionViewModel? StagedConstruction { get; set; }

    // ── Staged Analysis subtab hooks ────────────────────────────────────────
    /// <summary>Optional sink for structured convergence-log entries from the staged solver (set by Staged Analysis subtab).</summary>
    public Action<ConvergenceLogEntry>? StagedAnalysisLogger { get; set; }

    /// <summary>Optional cancellation poll; when it returns true the staged run stops after the current phase.</summary>
    public Func<bool>? StagedAnalysisStopRequested { get; set; }

    /// <summary>Optional restricted list of stage numbers to run. Null or empty = run all active stages.</summary>
    public List<int>? StagedAnalysisRunPlan { get; set; }

    /// <summary>
    /// Runs stage-by-stage FE analysis using the construction stages defined in
    /// <see cref="StagedConstruction"/>. Results are accumulated per stage.
    /// </summary>
    public async System.Threading.Tasks.Task RunStagedAnalysisAsync()
    {
        if (StagedConstruction != null && StagedExcavationEnabled && StageDefinitions.Count > 0)
            StagedConstruction.SyncConstructionStagesFromFeAnalysis(this, true);

        if (StagedConstruction == null || StagedConstruction.Stages.Count == 0)
        {
            StatusMessage = "No stages defined. Add stages in Project Settings → Analysis Type or Materials & Regions.";
            MessageBox.Show("No construction stages defined. Add stages first.",
                "Staged Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_nodes.Count == 0 || _elements.Count == 0)
        {
            StatusMessage = "No mesh loaded. Load mesh data before running staged analysis.";
            MessageBox.Show("Load mesh data first (Load Mesh Data button).",
                "Staged Analysis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsSolving = true;
        StatusMessage = "Running staged analysis...";

        try
        {
            // Accumulated displacements across stages [nodeId → (ux, uy)]
            var cumUx = new Dictionary<int, double>();
            var cumUy = new Dictionary<int, double>();
            foreach (var node in _nodes)
            {
                cumUx[node.Id] = 0.0;
                cumUy[node.Id] = 0.0;
            }

            // Accumulated stresses across stages [elementId → (σx, σy, τxy)]
            var cumSx  = new Dictionary<int, double>();
            var cumSy  = new Dictionary<int, double>();
            var cumTxy = new Dictionary<int, double>();
            foreach (var el in _elements)
            {
                cumSx[el.Id]  = 0.0;
                cumSy[el.Id]  = 0.0;
                cumTxy[el.Id] = 0.0;
            }

            var stages = StagedConstruction.Stages.ToList();
            var planFilter = StagedAnalysisRunPlan is { Count: > 0 } ? new HashSet<int>(StagedAnalysisRunPlan) : null;

            for (int si = 0; si < stages.Count; si++)
            {
                var stage = stages[si];
                if (!stage.IsActive) continue;
                if (planFilter != null && !planFilter.Contains(stage.StageNumber)) continue;

                if (StagedAnalysisStopRequested?.Invoke() == true)
                {
                    StatusMessage = "Staged analysis stopped by user.";
                    LogStaged("warn", $"Stopped before stage {stage.StageNumber}.", stage.StageNumber);
                    break;
                }

                // Correlate with rich stage parameters from FE Analysis → Staged Analysis subtab.
                var phase = _stageDefinitions.FirstOrDefault(d => d.StageNumber == stage.StageNumber);

                if (phase?.ResetDisplacementsAtStart == true)
                {
                    foreach (var n in _nodes) { cumUx[n.Id] = 0; cumUy[n.Id] = 0; }
                    foreach (var e in _elements) { cumSx[e.Id] = 0; cumSy[e.Id] = 0; cumTxy[e.Id] = 0; }
                    LogStaged("info", $"Stage {stage.StageNumber}: displacements reset.", stage.StageNumber);
                }

                StatusMessage = $"Solving Stage {stage.StageNumber}: {stage.StageName} ({si + 1}/{stages.Count})...";
                LogStaged("info", $"Stage {stage.StageNumber} — {stage.StageName}: solver={phase?.SolverMode ?? "default"}, λ={phase?.StressReleaseLambda ?? 1.0:0.##}, loadFactor={phase?.LoadFactor ?? 1.0:0.##}.", stage.StageNumber);
                await System.Threading.Tasks.Task.Delay(50); // yield to UI

                // Union of auto-excavations and any phase-scoped deactivations; subtract activations.
                var excavatedSurfaces = StagedConstruction.GetExcavatedSurfacesThroughStage(stage.StageNumber);
                if (phase != null)
                {
                    foreach (var n in phase.DeactivateSurfaceNumbers) excavatedSurfaces.Add(n);
                    foreach (var n in phase.ActivateSurfaceNumbers) excavatedSurfaces.Remove(n);
                }

                var ctx = phase == null ? null : new StagedAnalysisContext
                {
                    PhaseNumber = phase.StageNumber,
                    PhaseName = phase.StageName,
                    Lambda = phase.StressReleaseLambda,
                    LoadFactor = phase.LoadFactor,
                    ResetDisplacements = phase.ResetDisplacementsAtStart,
                    SolverMode = phase.SolverMode,
                    MaxIterations = phase.MaxIterations,
                    Tolerance = phase.Tolerance,
                    MaxLoadSteps = phase.MaxLoadSteps,
                    ArcLength = phase.ArcLength,
                    ExcavatedSurfaceNumbers = excavatedSurfaces,
                };

                // Run the C++ solver for this stage with the script dat (if available).
                P51ResFileParser.P51ParseResult? p51Result = null;
                P610T3RunResult? p610Result = null;
                string? routedSolverLabel = null;
                try
                {
                    ResolveCppExecutablePath();
                    var datContent = BuildStagedDatContent(stage, excavatedSurfaces, cumUx, cumUy, cumSx, cumSy, cumTxy, ctx);
                    if (!string.IsNullOrWhiteSpace(datContent))
                    {
                        var workDir = System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(), $"StagedFE_S{stage.StageNumber}");
                        System.IO.Directory.CreateDirectory(workDir);

                        var routed = await RunStagedSolverForDatAsync(datContent, stage.StageNumber, workDir);
                        routedSolverLabel = routed.SolverLabel;
                        p51Result = routed.P51Result;
                        p610Result = routed.P610Result;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Stage {stage.StageNumber} solver error: {ex.Message}. Continuing...";
                    LogStaged("error", $"Stage {stage.StageNumber} solver error: {ex.Message}", stage.StageNumber);
                    await System.Threading.Tasks.Task.Delay(20);
                    stage.IsSolved = false;
                    continue;
                }

                // Accumulate results
                if (p51Result != null)
                {
                    AccumulateP51StageResult(p51Result, cumUx, cumUy, cumSx, cumSy, cumTxy);
                }
                else if (p610Result != null)
                {
                    AccumulateP610StageResult(p610Result, cumUx, cumUy, cumSx, cumSy, cumTxy);
                }

                // Store per-stage results
                stage.NodalDisplacements.Clear();
                foreach (var node in _nodes)
                {
                    stage.NodalDisplacements[node.Id] = (cumUx.GetValueOrDefault(node.Id), cumUy.GetValueOrDefault(node.Id));
                }
                stage.ElementStresses.Clear();
                foreach (var el in _elements)
                {
                    stage.ElementStresses[el.Id] = (cumSx.GetValueOrDefault(el.Id), cumSy.GetValueOrDefault(el.Id), cumTxy.GetValueOrDefault(el.Id));
                }
                stage.IsSolved = true;
                LogStaged("converged", $"Stage {stage.StageNumber} solved{(string.IsNullOrWhiteSpace(routedSolverLabel) ? "" : $" with {routedSolverLabel}")}.", stage.StageNumber);
            }

            // Push per-stage results for the Results/Visualization stage dropdown.
            _resultsVm.LoadFromConstructionStages(stages, _nodes, _elements);
            if (StagedConstruction != null && StagedConstruction.ActiveStageNumber > 0)
                _resultsVm.SyncVisualizationToFooterStage(StagedConstruction.ActiveStageNumber);

            _resultsVm.FEElements = _elements;
            _resultsVm.FeStatusMessage =
                $"Staged analysis complete: {stages.Count} stages, {_nodes.Count} nodes, {_elements.Count} elements.";

            _lastExecutionStatus = "Completed (staged)";
            _lastNodeCount = _nodes.Count;
            _lastElementCount = _elements.Count;
            OnPropertyChanged(nameof(ExecutionStatus));
            OnPropertyChanged(nameof(NodeCount));
            OnPropertyChanged(nameof(ElementCount));

            StatusMessage = $"Staged analysis complete — {stages.Count} stage(s) solved.";
        }
        catch (OperationCanceledException)
        {
            _lastExecutionStatus = "Stopped";
            StatusMessage = "Staged analysis stopped by user.";
            EndAnalysisRunState();
        }
        catch (Exception ex)
        {
            _lastExecutionStatus = "Failed";
            StatusMessage = $"Staged analysis error: {ex.Message}";
            EndAnalysisRunState();
            MessageBox.Show($"Staged analysis failed:\n\n{ex.Message}",
                "Staged Analysis", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (IsSolving)
                IsSolving = false;
        }
    }

    private void LogStaged(string level, string message, int? phaseNumber = null)
    {
        var cb = StagedAnalysisLogger;
        if (cb == null) return;
        cb(new ConvergenceLogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            PhaseNumber = phaseNumber
        });
    }

    private async System.Threading.Tasks.Task<StagedSolverRunResult> RunStagedSolverForDatAsync(
        string datContent,
        int stageNumber,
        string workDir)
    {
        var kind = ClassifyStagedDatContent(datContent);
        var baseName = $"stage_{stageNumber}";

        if (kind == StagedDatKind.P51)
        {
            LogStaged("info", $"Stage {stageNumber}: routing staged deck to p51.", stageNumber);
            var result = await _runner.RunP51FromDatContentAsync(datContent, baseName, workDir);
            return new StagedSolverRunResult("p51", result, null);
        }

        var femProgramId = FemCppProgramRouter.ResolveProgramId(datContent);
        if (!string.IsNullOrWhiteSpace(femProgramId))
        {
            LogStaged("info", $"Stage {stageNumber}: routing staged deck to fem_cpp ({femProgramId}).", stageNumber);
            var femResult = await _runner.RunFemCppFromDatContentAsync(
                femProgramId,
                datContent,
                baseName,
                workDir);
            return new StagedSolverRunResult($"fem_cpp/{femProgramId}", null, femResult.JsonResult);
        }

        if (UseP610T3FromScriptDat)
        {
            var options = P610AnalysisOptionsHelper.BuildFromViewModels(_materialProperties, _meshSource);
            var programName = _materialProperties?.IsSrfAnalysisMode == true
                ? P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(
                    options,
                    _meshSource,
                    srfAnalysisMode: true)
                : ResolveStructuralP610ProgramName();

            if (string.Equals(programName, "p64", StringComparison.OrdinalIgnoreCase))
            {
                var programId = FemCppProgramRouter.ResolveProgramId(datContent) ?? "p64";
                LogStaged("info", $"Stage {stageNumber}: routing staged deck to fem_cpp ({programId}).", stageNumber);
                var femResult = await _runner.RunFemCppFromDatContentAsync(
                    programId,
                    datContent,
                    baseName,
                    workDir);
                return new StagedSolverRunResult($"fem_cpp/{programId}", null, femResult.JsonResult);
            }

            LogStaged("info", $"Stage {stageNumber}: routing staged deck to {programName}.", stageNumber);
            var result = await _runner.RunP610T3VariantFromDatContentAsync(
                programName,
                datContent,
                baseName,
                workDir);
            return new StagedSolverRunResult(programName, null, result);
        }

        throw new InvalidOperationException(
            $"Stage {stageNumber} deck is not p51-compatible; route requires p610/fem_cpp. " +
            "Check Material Properties analysis type and solver selection.");
    }

    private static StagedDatKind ClassifyStagedDatContent(string datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return StagedDatKind.Empty;

        var first = ReadFirstDataToken(datContent);
        if (string.IsNullOrWhiteSpace(first))
            return StagedDatKind.Empty;

        var normalized = first.Trim().Trim('\'', '"');
        if (normalized.Equals("type_2d", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("plane", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("axisymmetric", StringComparison.OrdinalIgnoreCase))
        {
            return StagedDatKind.P51;
        }

        if (FemCppProgramRouter.ResolveProgramId(datContent) != null)
            return StagedDatKind.FemCpp;

        return StagedDatKind.Unsupported;
    }

    private static string? ReadFirstDataToken(string datContent)
    {
        using var reader = new StringReader(CppAnalysisRunner.StripFortranFullLineComments(datContent));
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : null;
        }
        return null;
    }

    private static void AccumulateP51StageResult(
        P51ResFileParser.P51ParseResult result,
        Dictionary<int, double> cumUx,
        Dictionary<int, double> cumUy,
        Dictionary<int, double> cumSx,
        Dictionary<int, double> cumSy,
        Dictionary<int, double> cumTxy)
    {
        foreach (var node in result.Nodes)
        {
            double ux = node.Displacements.Length > 0 ? node.Displacements[0] : 0;
            double uy = node.Displacements.Length > 1 ? node.Displacements[1] : 0;
            if (cumUx.ContainsKey(node.Id))
            {
                cumUx[node.Id] += ux;
                cumUy[node.Id] += uy;
            }
        }

        foreach (var stress in result.ElementStresses)
            AccumulateElementStress(stress, cumSx, cumSy, cumTxy);
    }

    private static void AccumulateP610StageResult(
        P610T3RunResult result,
        Dictionary<int, double> cumUx,
        Dictionary<int, double> cumUy,
        Dictionary<int, double> cumSx,
        Dictionary<int, double> cumSy,
        Dictionary<int, double> cumTxy)
    {
        var stage = result.Stages.LastOrDefault();
        if (stage == null)
            return;

        for (int i = 0; i < stage.DisplacementsByNodeIndex.Count && i < result.Nodes.Count; i++)
        {
            var nodeId = result.Nodes[i].Id;
            if (!cumUx.ContainsKey(nodeId))
                continue;

            var (ux, uy) = stage.DisplacementsByNodeIndex[i];
            cumUx[nodeId] += ux;
            cumUy[nodeId] += uy;
        }

        foreach (var stress in stage.ElementStresses)
            AccumulateElementStress(stress, cumSx, cumSy, cumTxy);
    }

    private static void AccumulateElementStress(
        ElementStress stress,
        Dictionary<int, double> cumSx,
        Dictionary<int, double> cumSy,
        Dictionary<int, double> cumTxy)
    {
        if (!cumSx.ContainsKey(stress.ElementId))
            return;

        cumSx[stress.ElementId] += stress.StressXX;
        cumSy[stress.ElementId] += stress.StressYY;
        cumTxy[stress.ElementId] += stress.StressXY;
    }

    private enum StagedDatKind
    {
        Empty,
        P51,
        FemCpp,
        Unsupported
    }

    private sealed record StagedSolverRunResult(
        string SolverLabel,
        P51ResFileParser.P51ParseResult? P51Result,
        P610T3RunResult? P610Result);

    /// <summary>
    /// Builds a p51-compatible .dat string for one stage.
    /// Excavated elements have their stiffness (E) set to near-zero (1e-6 kPa).
    /// Also injects cumulative stresses from prior stages as initial stress body loads.
    /// </summary>
    private string BuildStagedDatContent(
        ConstructionStage stage,
        HashSet<int> excavatedSurfaces,
        Dictionary<int, double> cumUx,
        Dictionary<int, double> cumUy,
        Dictionary<int, double> cumSx,
        Dictionary<int, double> cumSy,
        Dictionary<int, double> cumTxy,
        StagedAnalysisContext? ctx = null)
    {
        // If a script editor .dat exists, use it as the base (preserving mesh, BCs, loads).
        // Otherwise fall back to the demo rectangular mesh.
        if (_scriptVm != null)
        {
            _scriptVm.RefreshDatContent();
            var baseDat = _scriptVm.GetScriptContentForSolverRun(stage.StageNumber);
            if (!string.IsNullOrWhiteSpace(baseDat))
            {
                var header = $"! Stage {stage.StageNumber}: {stage.StageName} | ExcavatedSurfaces=[{string.Join(",", excavatedSurfaces.Select(s => $"S{s}"))}]\r\n"
                           + $"! Carry-over stress: cumulative from prior {stage.StageNumber - 1} stage(s).\r\n";
                if (ctx != null)
                {
                    header += $"! Phase context: solver={ctx.SolverMode}, lambda={ctx.Lambda.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}, "
                           +  $"loadFactor={ctx.LoadFactor.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}, "
                           +  $"resetU={(ctx.ResetDisplacements ? 1 : 0)}, maxIter={ctx.MaxIterations}, tol={ctx.Tolerance:0.###e+00}, "
                           +  $"maxLoadSteps={ctx.MaxLoadSteps}, arcLength={(ctx.ArcLength ? 1 : 0)}\r\n";
                }
                return header + baseDat;
            }
        }

        // Fallback: demo 2×2 rectangular mesh with excavation-aware E values.
        var dir = _meshSource?.MeshNumberingDirectionChar ?? 'x';
        const double eBase = 1e6;
        const double eExcavated = 1e-6; // near-zero stiffness for excavated elements
        // If any surfaces are excavated, reduce E by factor for all elements
        // (full per-element excavation requires a richer .dat builder — this is a simplification).
        double eEff = excavatedSurfaces.Count > 0 ? eExcavated : eBase;

        var input = P51DatFileWriter.FromRectangularMesh(
            width: 1.0, height: 1.0,
            nxe: 2, nye: 2,
            youngsModulus: eEff, poissonsRatio: 0.3,
            restrainedNodes: new List<(int, int, int)>
            {
                (1, 0, 1), (4, 0, 1), (7, 0, 0), (8, 1, 0), (9, 1, 0)
            },
            loadedNodes: new List<(int, double, double)>
            {
                (1, 0.0, -0.25), (2, 0.0, -0.50), (3, 0.0, -0.25)
            },
            dir: dir);
        var sw = new StringWriter();
        new P51DatFileWriter().Write(sw, input);
        return sw.ToString();
    }
}

/// <summary>One SRF trial entry: a 1-based display index and the F (strength reduction factor) value.</summary>
public class SrfTrialEntry : INotifyPropertyChanged
{
    private int _index;
    private double _value;

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(); }
    }

    public double Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Per-phase context consumed by the staged .dat builder (λ, load factor, reset, excavated set, solver knobs).</summary>
public class StagedAnalysisContext
{
    public int PhaseNumber { get; set; }
    public string PhaseName { get; set; } = "";
    public double Lambda { get; set; } = 1.0;
    public double LoadFactor { get; set; } = 1.0;
    public bool ResetDisplacements { get; set; }
    public string SolverMode { get; set; } = "Plastic";
    public int MaxIterations { get; set; } = 100;
    public double Tolerance { get; set; } = 1e-3;
    public int MaxLoadSteps { get; set; } = 10;
    public bool ArcLength { get; set; }
    public HashSet<int> ExcavatedSurfaceNumbers { get; set; } = new();
}
