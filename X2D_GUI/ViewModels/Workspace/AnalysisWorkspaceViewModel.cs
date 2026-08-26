using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Analysis;
using GeotechSlopeApp.Services.Analysis;
using GeotechSlopeApp.Services.Appearance;

namespace GeotechSlopeApp.ViewModels.Workspace;

/// <summary>
/// Optional Analysis Workspace chrome. Hosts existing view-models; does not own a second FEM stack.
/// </summary>
public sealed class AnalysisWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly MainWindowViewModel _host;
    private readonly StringBuilder _log = new();
    private string _ribbonTab = "ANALYSIS";
    private string _lastCapabilityMessage = "";
    private bool _syncingIntent;
    private bool _isExplorerExpanded = true;
    private bool _isAnalysisTypeExpanded = true;
    private bool _isSolverLogExpanded = true;
    private bool _isResultsInspectorExpanded = true;
    private bool _isViewOptionsExpanded = true;
    private bool _showGeometryLines = true;
    private string _centerView = CenterView2D;
    private string _analysisRibbonTool = "Type";
    private string _gatedLoadingType = AnalysisGatedOptionCatalog.LoadingTypes[0];
    private string _gatedGravityDirection = AnalysisGatedOptionCatalog.GravityDirections[0];
    private string _gatedTangentStiffness = AnalysisGatedOptionCatalog.TangentStiffness[0];
    private string _gatedConvergenceOption = AnalysisGatedOptionCatalog.ConvergenceOptions[0];
    private string _gatedK0Method = AnalysisGatedOptionCatalog.K0Methods[0];
    private string _gatedDampingType = AnalysisGatedOptionCatalog.DampingTypes[0];
    private string _gatedDampingRatio = AnalysisGatedOptionCatalog.DampingRatios[0];
    private string _gatedIncompatibleMode = AnalysisGatedOptionCatalog.IncompatibleModes[0];
    private string _gatedBoundaryStiffness = AnalysisGatedOptionCatalog.BoundaryStiffness[0];
    private string _gatedStressRecovery = AnalysisGatedOptionCatalog.StressRecovery[0];
    private string _gatedStiffnessMatrix = AnalysisGatedOptionCatalog.StiffnessMatrix[0];
    private string _gatedLineSearch = AnalysisGatedOptionCatalog.LineSearch[0];

    public const string CenterView2D = "2D";
    public const string CenterView3D = "3D";
    public const string CenterViewStages = "Stages";
    public const string CenterViewMonitor = "Monitor";
    public const string CenterViewLog = "Log";
    public const string PayloadAnalysisSettings = "analysis-settings";
    public const string PayloadSolverSettings = "solver-settings";
    public const string PayloadAnalysisStages = "analysis-stages";

    public AnalysisWorkspaceViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Settings = new AnalysisWorkspaceSettings();
        Explorer = new ProjectExplorerViewModel(host);
        Geometry = new GeometryWorkspaceViewModel(host);
        Materials = new MaterialsWorkspaceViewModel(host);
        Mesh = new MeshWorkspaceViewModel(host);
        Loads = new LoadsWorkspaceViewModel(host);
        Boundary = new BoundaryWorkspaceViewModel(host);
        Groundwater = new GroundwaterWorkspaceViewModel(host);
        Results = new ResultsWorkspaceViewModel(host);
        FileHome = new FileWorkspaceViewModel(host);
        ResultCategories = BuildResultCategories();
        Intents = AnalysisCapabilityCatalog.All.ToList();

        SelectRibbonTabCommand = new RelayCommand<string>(OnSelectRibbonTab);
        SelectIntentCommand = new RelayCommand<string>(OnSelectIntent);
        ShowCapabilityCommand = new RelayCommand<string>(ShowCapabilityByName);
        CheckModelCommand = new RelayCommand(() =>
        {
            _host.SelectedTabIndex = 7;
            _host.ModelHealthVM.RefreshCommand.Execute(null);
            AppendLog("Check Model: " + _host.ModelHealthVM.SummaryText);
        });
        InstallSupportCommand = new RelayCommand(() =>
            ShowCapabilityMessage("Feature not available in current FEM engine. Support installation is incomplete."));
        RunDynamicStepCommand = new RelayCommand(() =>
            ShowCapabilityMessage(AnalysisCapabilityCatalog.Get(AnalysisIntent.Dynamic).UnavailableReason));
        PauseAnalysisCommand = new RelayCommand(() => { }, () => false);
        SelectResultCategoryCommand = new RelayCommand<ResultCategoryItem>(item =>
        {
            if (item == null) return;
            _host.SelectedTabIndex = 9;
            _host.ResultsVM.VisualizationMode = item.VisualizationMode;
            _host.ResultsVM.SelectedOutputType = item.OutputType;
            if (item.SelectCriticalSrf)
                _host.ResultsVM.SelectCriticalSrfStageForVisualization();
        });
        Show3DViewCommand = new RelayCommand(() =>
            ShowCapabilityMessage("Feature not available in current FEM engine. A 3D solid viewport is not in this engine."));
        OpenLogViewerCommand = new RelayCommand(() => _host.SelectedTabIndex = 12);
        ToggleExplorerCommand = new RelayCommand(() => IsExplorerExpanded = !IsExplorerExpanded);
        ToggleAnalysisTypeCommand = new RelayCommand(() => IsAnalysisTypeExpanded = !IsAnalysisTypeExpanded);
        ToggleSolverLogCommand = new RelayCommand(() => IsSolverLogExpanded = !IsSolverLogExpanded);
        ToggleResultsInspectorCommand = new RelayCommand(() => IsResultsInspectorExpanded = !IsResultsInspectorExpanded);
        ToggleViewOptionsCommand = new RelayCommand(() => IsViewOptionsExpanded = !IsViewOptionsExpanded);
        SelectAnalysisRibbonToolCommand = new RelayCommand<string>(OnSelectAnalysisRibbonTool);
        SelectCenterViewCommand = new RelayCommand<string>(SelectCenterView);
        ApplySettingsCommand = new RelayCommand(ApplySettings);
        ClearLogCommand = new RelayCommand(ClearLog);
        OpenStageManagerCommand = new RelayCommand(() => SelectCenterView(CenterViewStages));
        OpenSolverMonitorCommand = new RelayCommand(() => SelectCenterView(CenterViewMonitor));
        OpenAnalysisLogCommand = new RelayCommand(() => SelectCenterView(CenterViewLog));
        VerifyBoundaryConditionsCommand = new RelayCommand(() =>
        {
            RibbonTab = "BOUNDARY";
            _host.SelectedTabIndex = 5;
            _host.ModelHealthVM.RefreshCommand.Execute(null);
            AppendLog("Verify Boundary Conditions: " + _host.ModelHealthVM.SummaryText);
        });
        VerifyLoadsCommand = new RelayCommand(() =>
        {
            RibbonTab = "LOADS";
            _host.SelectedTabIndex = 4;
            AppendLog("Verify Loads: opened Loads workspace.");
        });
        VerifyMaterialsCommand = new RelayCommand(() =>
        {
            RibbonTab = "MATERIALS";
            _host.SelectedTabIndex = 2;
            AppendLog("Verify Materials: opened Materials workspace.");
        });
        VerifyMeshCommand = new RelayCommand(() =>
        {
            RibbonTab = "MESH";
            _host.SelectedTabIndex = 3;
            AppendLog("Verify Mesh: opened Mesh workspace.");
        });
        RunTrialAnalysisCommand = new RelayCommand(() =>
        {
            if (!IsIntentAvailable)
            {
                ShowCapabilityMessage(CurrentCapability.UnavailableReason);
                return;
            }
            _host.SelectedTabIndex = 7;
            _host.RunFEAnalysisMenuCommand.Execute(null);
        });

        _host.FEAnalysisVM.PropertyChanged += OnFePropertyChanged;
        _host.ResultsVM.PropertyChanged += OnResultsPropertyChanged;
        _host.LoadConditionsVM.FieldStress.Global.PropertyChanged += OnFieldStressChanged;
        _host.MaterialPropertiesVM.PropertyChanged += OnMaterialsPropertyChanged;
        _host.StagedConstructionVM.Stages.CollectionChanged += (_, _) => RaiseSummaryChanged();
        _host.FEAnalysisVM.StageDefinitions.CollectionChanged += (_, _) => RaiseSummaryChanged();
        if (!string.IsNullOrWhiteSpace(_host.FEAnalysisVM.StatusMessage))
            AppendLog(_host.FEAnalysisVM.StatusMessage);

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AnalysisWorkspaceSettings.Intent))
            {
                ApplyIntentToProject();
                OnPropertyChanged(nameof(ShowGroundwaterRibbon));
                if (!ShowGroundwaterRibbon && IsGroundwaterRibbon)
                    RibbonTab = "BOUNDARY";
            }
            if (e.PropertyName == nameof(AnalysisWorkspaceSettings.StrengthReductionIncrement)
                || e.PropertyName == nameof(AnalysisWorkspaceSettings.InitialStrengthReductionFactor))
                WireSrfIncrement();
            OnPropertyChanged(nameof(IntentPanelKey));
            OnPropertyChanged(nameof(IsStaticFamilyIntent));
            OnPropertyChanged(nameof(IsSrfIntent));
            OnPropertyChanged(nameof(IsGroundwaterIntent));
            OnPropertyChanged(nameof(IsDynamicIntent));
            OnPropertyChanged(nameof(CapabilityBanner));
            OnPropertyChanged(nameof(IsIntentAvailable));
            RaiseSummaryChanged();
        };

        SyncIntentFromProject();
        WireSrfIncrement();
        Explorer.BindWorkspace(this);
    }

    public AnalysisWorkspaceSettings Settings { get; }
    public ProjectExplorerViewModel Explorer { get; }
    public GeometryWorkspaceViewModel Geometry { get; }
    public MaterialsWorkspaceViewModel Materials { get; }
    public MeshWorkspaceViewModel Mesh { get; }
    public LoadsWorkspaceViewModel Loads { get; }
    public BoundaryWorkspaceViewModel Boundary { get; }
    public GroundwaterWorkspaceViewModel Groundwater { get; }
    public ResultsWorkspaceViewModel Results { get; }
    public FileWorkspaceViewModel FileHome { get; }
    public IReadOnlyList<IAnalysisCapability> Intents { get; }
    public ObservableCollection<ResultCategoryItem> ResultCategories { get; }
    public IReadOnlyList<string> MethodChoices { get; } = new[] { "Strength Reduction", "Limit Equilibrium" };
    public IReadOnlyList<string> SsrTypeChoices { get; } = new[] { "Global", "Local" };
    public IReadOnlyList<string> PorePressureChoices { get; } = new[] { "None", "Prescribed", "Seepage" };
    public IReadOnlyList<string> FormulationChoices { get; } = ["Total Stress", "Effective Stress"];
    public IReadOnlyList<string> InitialStressMethodChoices { get; } =
        ["In-Situ", "Constant", "Direct", "Traction"];
    public IReadOnlyList<string> GatedLoadingTypes => AnalysisGatedOptionCatalog.LoadingTypes;
    public IReadOnlyList<string> GatedGravityDirections => AnalysisGatedOptionCatalog.GravityDirections;
    public IReadOnlyList<string> GatedTangentStiffness => AnalysisGatedOptionCatalog.TangentStiffness;
    public IReadOnlyList<string> GatedConvergenceOptions => AnalysisGatedOptionCatalog.ConvergenceOptions;
    public IReadOnlyList<string> GatedK0Methods => AnalysisGatedOptionCatalog.K0Methods;
    public IReadOnlyList<string> GatedDampingTypes => AnalysisGatedOptionCatalog.DampingTypes;
    public IReadOnlyList<string> GatedDampingRatios => AnalysisGatedOptionCatalog.DampingRatios;
    public IReadOnlyList<string> GatedIncompatibleModes => AnalysisGatedOptionCatalog.IncompatibleModes;
    public IReadOnlyList<string> GatedBoundaryStiffness => AnalysisGatedOptionCatalog.BoundaryStiffness;
    public IReadOnlyList<string> GatedStressRecovery => AnalysisGatedOptionCatalog.StressRecovery;
    public IReadOnlyList<string> GatedStiffnessMatrix => AnalysisGatedOptionCatalog.StiffnessMatrix;
    public IReadOnlyList<string> GatedLineSearch => AnalysisGatedOptionCatalog.LineSearch;

    public string GatedLoadingType
    {
        get => _gatedLoadingType;
        set => SetGated(ref _gatedLoadingType, value, GatedLoadingTypes);
    }
    public string GatedGravityDirection
    {
        get => _gatedGravityDirection;
        set => SetGated(ref _gatedGravityDirection, value, GatedGravityDirections);
    }
    public string GatedTangentStiffnessValue
    {
        get => _gatedTangentStiffness;
        set => SetGated(ref _gatedTangentStiffness, value, GatedTangentStiffness);
    }
    public string GatedConvergenceOption
    {
        get => _gatedConvergenceOption;
        set => SetGated(ref _gatedConvergenceOption, value, GatedConvergenceOptions);
    }
    public string GatedK0Method
    {
        get => _gatedK0Method;
        set => SetGated(ref _gatedK0Method, value, GatedK0Methods);
    }
    public string GatedDampingType
    {
        get => _gatedDampingType;
        set => SetGated(ref _gatedDampingType, value, GatedDampingTypes);
    }
    public string GatedDampingRatio
    {
        get => _gatedDampingRatio;
        set => SetGated(ref _gatedDampingRatio, value, GatedDampingRatios);
    }
    public string GatedIncompatibleMode
    {
        get => _gatedIncompatibleMode;
        set => SetGated(ref _gatedIncompatibleMode, value, GatedIncompatibleModes);
    }
    public string GatedBoundaryStiffnessValue
    {
        get => _gatedBoundaryStiffness;
        set => SetGated(ref _gatedBoundaryStiffness, value, GatedBoundaryStiffness);
    }
    public string GatedStressRecoveryValue
    {
        get => _gatedStressRecovery;
        set => SetGated(ref _gatedStressRecovery, value, GatedStressRecovery);
    }
    public string GatedStiffnessMatrixValue
    {
        get => _gatedStiffnessMatrix;
        set => SetGated(ref _gatedStiffnessMatrix, value, GatedStiffnessMatrix);
    }
    public string GatedLineSearchValue
    {
        get => _gatedLineSearch;
        set => SetGated(ref _gatedLineSearch, value, GatedLineSearch);
    }

    public string FeatureNotAvailable => AnalysisCapabilityCatalog.FeatureNotAvailable;
    public string ThermalUnavailableReason =>
        AnalysisCapabilityCatalog.Get(AnalysisIntent.Thermal).UnavailableReason;

    public bool GenerateInitialStress => true;

    public bool IsMaterialNonlinear =>
        _host.MaterialPropertiesVM.SelectedFailureCriterion != FailureCriterion.Elastic;

    public string MaterialNonlinearityLabel =>
        IsMaterialNonlinear
            ? _host.MaterialPropertiesVM.SelectedFailureCriterion.ToString()
            : "Linear (Elastic)";

    public string SelectedFormulation
    {
        get => _host.LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio
            ? "Effective Stress"
            : "Total Stress";
        set
        {
            var useEffective = string.Equals(value, "Effective Stress", StringComparison.Ordinal);
            if (_host.LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio == useEffective)
                return;
            _host.LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio = useEffective;
            OnPropertyChanged();
        }
    }

    public string SelectedInitialStressMethod
    {
        get => _host.LoadConditionsVM.FieldStress.Global.Mode switch
        {
            FieldStressMode.Constant => "Constant",
            FieldStressMode.Direct => "Direct",
            FieldStressMode.Traction => "Traction",
            _ => "In-Situ"
        };
        set
        {
            var mode = value switch
            {
                "Constant" => FieldStressMode.Constant,
                "Direct" => FieldStressMode.Direct,
                "Traction" => FieldStressMode.Traction,
                _ => FieldStressMode.Gravity
            };
            if (_host.LoadConditionsVM.FieldStress.Global.Mode == mode)
                return;
            _host.LoadConditionsVM.FieldStress.Global.Mode = mode;
            OnPropertyChanged();
        }
    }

    public string RibbonTab
    {
        get => _ribbonTab;
        set
        {
            if (_ribbonTab == value) return;
            _ribbonTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFileRibbon));
            OnPropertyChanged(nameof(IsGeometryRibbon));
            OnPropertyChanged(nameof(IsMaterialsRibbon));
            OnPropertyChanged(nameof(IsLoadsRibbon));
            OnPropertyChanged(nameof(IsMeshRibbon));
            OnPropertyChanged(nameof(IsBoundaryRibbon));
            OnPropertyChanged(nameof(IsGroundwaterRibbon));
            OnPropertyChanged(nameof(IsAnalysisRibbon));
            OnPropertyChanged(nameof(IsResultsRibbon));
            OnPropertyChanged(nameof(IsViewRibbon));
            _host.NotifyWorkspaceChromeChanged();
            Explorer.Refresh();
        }
    }

    public bool IsFileRibbon => RibbonTab == "FILE";
    public bool IsGeometryRibbon => RibbonTab == "GEOMETRY";
    public bool IsMaterialsRibbon => RibbonTab == "MATERIALS";
    public bool IsLoadsRibbon => RibbonTab == "LOADS";
    public bool IsMeshRibbon => RibbonTab == "MESH";
    public bool IsBoundaryRibbon => RibbonTab == "BOUNDARY";
    public bool IsGroundwaterRibbon => RibbonTab == "GROUNDWATER";
    public bool ShowGroundwaterRibbon => Settings.Intent == AnalysisIntent.Seepage;
    public bool IsAnalysisRibbon => RibbonTab == "ANALYSIS";
    public bool IsResultsRibbon => RibbonTab == "RESULTS";
    public bool IsViewRibbon => RibbonTab == "VIEW";

    public string AnalysisRibbonTool
    {
        get => _analysisRibbonTool;
        set
        {
            if (_analysisRibbonTool == value) return;
            _analysisRibbonTool = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRibbonToolType));
            OnPropertyChanged(nameof(IsRibbonToolInitial));
            OnPropertyChanged(nameof(IsRibbonToolStatic));
            OnPropertyChanged(nameof(IsRibbonToolSeepage));
            OnPropertyChanged(nameof(IsRibbonToolConsolidation));
            OnPropertyChanged(nameof(IsRibbonToolDynamic));
            OnPropertyChanged(nameof(IsRibbonToolThermal));
            OnPropertyChanged(nameof(IsRibbonToolCoupled));
            OnPropertyChanged(nameof(IsRibbonToolSrf));
            OnPropertyChanged(nameof(IsRibbonToolStages));
            OnPropertyChanged(nameof(IsRibbonToolManager));
        }
    }

    public bool IsRibbonToolType => AnalysisRibbonTool == "Type";
    public bool IsRibbonToolInitial => AnalysisRibbonTool == "InitialStress";
    public bool IsRibbonToolStatic => AnalysisRibbonTool == "Static";
    public bool IsRibbonToolSeepage => AnalysisRibbonTool == "Seepage";
    public bool IsRibbonToolConsolidation => AnalysisRibbonTool == "Consolidation";
    public bool IsRibbonToolDynamic => AnalysisRibbonTool == "Dynamic";
    public bool IsRibbonToolThermal => AnalysisRibbonTool == "Thermal";
    public bool IsRibbonToolCoupled => AnalysisRibbonTool == "Coupled";
    public bool IsRibbonToolSrf => AnalysisRibbonTool == "Srf";
    public bool IsRibbonToolStages => AnalysisRibbonTool == "Stages";
    public bool IsRibbonToolManager => AnalysisRibbonTool == "Manager";

    public bool IsExplorerExpanded
    {
        get => _isExplorerExpanded;
        set
        {
            if (_isExplorerExpanded == value) return;
            _isExplorerExpanded = value;
            OnPropertyChanged();
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsAnalysisTypeExpanded
    {
        get => _isAnalysisTypeExpanded;
        set
        {
            if (_isAnalysisTypeExpanded == value) return;
            _isAnalysisTypeExpanded = value;
            OnPropertyChanged();
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsSolverLogExpanded
    {
        get => _isSolverLogExpanded;
        set
        {
            if (_isSolverLogExpanded == value) return;
            _isSolverLogExpanded = value;
            OnPropertyChanged();
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsResultsInspectorExpanded
    {
        get => _isResultsInspectorExpanded;
        set
        {
            if (_isResultsInspectorExpanded == value) return;
            _isResultsInspectorExpanded = value;
            OnPropertyChanged();
            _host.NotifyWorkspaceChromeChanged();
        }
    }

    public bool IsViewOptionsExpanded
    {
        get => _isViewOptionsExpanded;
        set
        {
            if (_isViewOptionsExpanded == value) return;
            _isViewOptionsExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool ShowGeometryLines
    {
        get => _showGeometryLines;
        set
        {
            if (_showGeometryLines == value) return;
            _showGeometryLines = value;
            OnPropertyChanged();
            _host.ApplyGeometryCanvasDecorationsForTab(_host.SelectedTabIndex);
        }
    }

    public string SolverLog
    {
        get => _log.ToString();
        private set { _ = value; OnPropertyChanged(); }
    }

    public string LastCapabilityMessage
    {
        get => _lastCapabilityMessage;
        private set { _lastCapabilityMessage = value; OnPropertyChanged(); }
    }

    public string IntentPanelKey => Settings.Intent.ToString();
    public bool IsStaticFamilyIntent =>
        Settings.Intent is AnalysisIntent.InitialStress or AnalysisIntent.Static;
    public bool IsSrfIntent => Settings.Intent == AnalysisIntent.Srf;
    public bool IsGroundwaterIntent =>
        Settings.Intent is AnalysisIntent.Seepage or AnalysisIntent.Consolidation;
    public bool IsDynamicIntent => Settings.Intent == AnalysisIntent.Dynamic;
    public IAnalysisCapability CurrentCapability => AnalysisCapabilityCatalog.Get(Settings.Intent);
    public bool IsIntentAvailable => CurrentCapability.Status == AnalysisCapabilityStatus.Available;
    public string CapabilityBanner =>
        CurrentCapability.Status == AnalysisCapabilityStatus.Available
            ? ""
            : CurrentCapability.UnavailableReason;

    public string StatusUnits =>
        _host.ProjectSettingsVM.Project.Units ?? ProjectUnitSystem.Si;

    public string StatusIdealization =>
        _host.ProjectSettingsVM.IsAxisymmetricIdealization ? "axisymmetric" : "plane strain";

    public string StatusStrip
    {
        get
        {
            var fe = _host.FEAnalysisVM;
            var mesh = fe.ResolveActiveMeshTypeForStatus();
            var material = _host.MaterialPropertiesVM.SelectedFailureCriterion == FailureCriterion.Elastic
                ? "elastic"
                : _host.MaterialPropertiesVM.SelectedFailureCriterion.ToString();
            var loads = fe.HasEnabledSurfaceOrLineLoad ? "surface loads" : "field stress";
            return $"{IntentLabel(Settings.Intent)} · {StatusIdealization} · {mesh} · {material} · {loads}";
        }
    }

    public string StatusDimension =>
        _host.ProjectSettingsVM.IsAxisymmetricIdealization ? "2D Axisymmetric" : "2D Plane Strain";
    public string StatusReady => _host.FEAnalysisVM.IsSolving ? "Running" : "Ready";

    public string CenterView
    {
        get => _centerView;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? CenterView2D : value;
            if (_centerView == next) return;
            _centerView = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCenterView2D));
            OnPropertyChanged(nameof(IsCenterView3D));
            OnPropertyChanged(nameof(IsCenterViewStages));
            OnPropertyChanged(nameof(IsCenterViewMonitor));
            OnPropertyChanged(nameof(IsCenterViewLog));
            OnPropertyChanged(nameof(IsCenterOverlayVisible));
        }
    }

    public bool IsCenterView2D => CenterView == CenterView2D;
    public bool IsCenterView3D => CenterView == CenterView3D;
    public bool IsCenterViewStages => CenterView == CenterViewStages;
    public bool IsCenterViewMonitor => CenterView == CenterViewMonitor;
    public bool IsCenterViewLog => CenterView == CenterViewLog;
    public bool IsCenterOverlayVisible => CenterView != CenterView2D;

    public string AnalysisTypeSummary => Settings.Intent switch
    {
        AnalysisIntent.Srf => Settings.SsrType == "Local"
            ? "Strength Reduction (Local)"
            : "Strength Reduction (Global)",
        AnalysisIntent.InitialStress => "Initial Conditions",
        AnalysisIntent.Static => "Static",
        AnalysisIntent.Seepage => "Seepage",
        AnalysisIntent.Consolidation => "Consolidation",
        AnalysisIntent.Dynamic => "Dynamic",
        AnalysisIntent.Thermal => "Thermal",
        AnalysisIntent.Coupled => "Coupled THM",
        _ => CurrentCapability.DisplayName
    };

    public string CurrentStageSummary
    {
        get
        {
            if (_host.FEAnalysisVM.StageDefinitions.Count > 0)
            {
                var stage = _host.FEAnalysisVM.StageDefinitions[^1];
                var name = string.IsNullOrWhiteSpace(stage.StageName) ? stage.StageType : stage.StageName;
                return $"{stage.StageNumber} - {name}";
            }

            if (_host.StagedConstructionVM.Stages.Count > 0)
            {
                var stage = _host.StagedConstructionVM.ActiveStage ?? _host.StagedConstructionVM.Stages[^1];
                var name = string.IsNullOrWhiteSpace(stage.StageName) ? stage.StageType : stage.StageName;
                return $"{stage.StageNumber} - {name}";
            }

            return "—";
        }
    }

    public string LastSrfSummary
    {
        get
        {
            var fos = _host.ResultsVM.EstimatedSrfFoS;
            return fos is { } value && double.IsFinite(value) && value > 0
                ? value.ToString("0.000")
                : "—";
        }
    }

    public string AnalysisStatusSummary
    {
        get
        {
            if (_host.FEAnalysisVM.IsSolving)
                return "Running";
            var lastFailed = _host.ResultsVM.SrfTrialRows.LastOrDefault(r => r.Failed);
            if (lastFailed != null)
                return $"Failed (SRF = {lastFailed.Srf:0.000})";
            if (_host.ResultsVM.EstimatedSrfFoS is { } fos && fos > 0)
                return _host.FEAnalysisVM.ExecutionStatus;
            return string.IsNullOrWhiteSpace(_host.FEAnalysisVM.ExecutionStatus)
                ? "Ready"
                : _host.FEAnalysisVM.ExecutionStatus;
        }
    }

    public string AnalysisStatusBrush =>
        AnalysisStatusSummary.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) ? "#FFEF4444"
        : AnalysisStatusSummary.StartsWith("Running", StringComparison.OrdinalIgnoreCase) ? "#FFF59E0B"
        : "#FF22C55E";

    public string SettingsSummaryText
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Analysis Type: {AnalysisTypeSummary}");
            sb.AppendLine($"Method: {Settings.Method}");
            if (IsSrfIntent)
                sb.AppendLine($"SSR Type: {Settings.SsrType}");
            sb.AppendLine($"Initial RF: {Settings.InitialStrengthReductionFactor:0.###}");
            sb.AppendLine($"Increment: {Settings.StrengthReductionIncrement:0.###}");
            sb.AppendLine($"Pore Pressure: {Settings.PorePressureOption}");
            sb.Append($"Current Stage: {CurrentStageSummary}");
            return sb.ToString();
        }
    }

    public string SolverTypeLabel =>
        string.IsNullOrWhiteSpace(_host.FEAnalysisVM.ResolvedSolverForRun)
            ? "fem_cpp"
            : _host.FEAnalysisVM.ResolvedSolverForRun;

    public string SolutionSchemeLabel => IsSrfIntent ? "Strength reduction" : "Incremental / Newton";

    public string SolverToleranceLabel =>
        _host.FEAnalysisVM.ConvergenceTolerance.ToString("0.###e+0");

    public string SolverIterationLabel
    {
        get
        {
            if (_host.FEAnalysisVM.IsSolving)
                return "in progress";
            var last = _host.ResultsVM.SrfTrialRows.LastOrDefault();
            return last == null ? "—" : last.Iterations.ToString();
        }
    }

    public double SolverProgressPercent
    {
        get
        {
            if (_host.FEAnalysisVM.IsSolving)
                return 50;
            if (_host.ResultsVM.SrfTrialRows.Count > 0 || _host.ResultsVM.LastP610Run != null)
                return 100;
            return 0;
        }
    }

    public string SolverProgressLabel => $"{SolverProgressPercent:0}%";

    public IReadOnlyList<AnalysisStageRow> StageManagerRows
    {
        get
        {
            if (_host.FEAnalysisVM.StageDefinitions.Count > 0)
            {
                return _host.FEAnalysisVM.StageDefinitions
                    .Select(s => new AnalysisStageRow(
                        s.StageNumber,
                        string.IsNullOrWhiteSpace(s.StageName) ? s.StageType : s.StageName,
                        s.StageType,
                        ResolveStageStatus(s.IsActive)))
                    .ToList();
            }

            return _host.StagedConstructionVM.Stages
                .Select(s => new AnalysisStageRow(
                    s.StageNumber,
                    string.IsNullOrWhiteSpace(s.StageName) ? s.StageType : s.StageName,
                    s.StageType,
                    ResolveStageStatus(s.IsActive)))
                .ToList();
        }
    }

    private void OnSelectRibbonTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
            return;
        RibbonTab = tab;
        switch (tab)
        {
            case "GEOMETRY":
                _host.SelectedTabIndex = 1;
                break;
            case "MATERIALS":
                _host.SelectedTabIndex = 2;
                break;
            case "LOADS":
                _host.SelectedTabIndex = 4;
                Loads.EnsureSelectedLoad();
                break;
            case "MESH":
                _host.SelectedTabIndex = 3;
                break;
            case "BOUNDARY":
                _host.SelectedTabIndex = 5;
                break;
            case "GROUNDWATER":
                _host.SelectedTabIndex = 6;
                break;
            case "ANALYSIS":
                _host.SelectedTabIndex = 7;
                CenterView = CenterView2D;
                AnalysisRibbonTool = "Type";
                break;
            case "RESULTS":
                _host.SelectedTabIndex = 9;
                Results.ApplyStaticDefaults();
                break;
        }
    }

    public ICommand SelectRibbonTabCommand { get; }
    public ICommand SelectIntentCommand { get; }
    public ICommand ShowCapabilityCommand { get; }
    public ICommand CheckModelCommand { get; }
    public ICommand InstallSupportCommand { get; }
    public ICommand RunDynamicStepCommand { get; }
    public ICommand PauseAnalysisCommand { get; }
    public ICommand SelectResultCategoryCommand { get; }
    public ICommand Show3DViewCommand { get; }
    public ICommand OpenLogViewerCommand { get; }
    public ICommand ToggleExplorerCommand { get; }
    public ICommand ToggleAnalysisTypeCommand { get; }
    public ICommand ToggleSolverLogCommand { get; }
    public ICommand ToggleResultsInspectorCommand { get; }
    public ICommand ToggleViewOptionsCommand { get; }
    public ICommand SelectAnalysisRibbonToolCommand { get; }
    public ICommand SelectCenterViewCommand { get; }
    public ICommand ApplySettingsCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand OpenStageManagerCommand { get; }
    public ICommand OpenSolverMonitorCommand { get; }
    public ICommand OpenAnalysisLogCommand { get; }
    public ICommand VerifyBoundaryConditionsCommand { get; }
    public ICommand VerifyLoadsCommand { get; }
    public ICommand VerifyMaterialsCommand { get; }
    public ICommand VerifyMeshCommand { get; }
    public ICommand RunTrialAnalysisCommand { get; }

    public void ShowCapabilityMessage(string message)
    {
        LastCapabilityMessage = message;
        AppendLog(message);
        if (Application.Current != null)
            MessageBox.Show(message, "Analysis Workspace", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void PersistLayoutWidths(double explorer, double results, double logHeight)
    {
        var s = UiAppearanceService.Instance.Settings;
        if (explorer > 80) s.ExplorerWidth = explorer;
        if (results > 80) s.ResultsWidth = results;
        if (logHeight > 40) s.SolverLogHeight = logHeight;
        UiAppearanceService.Instance.Save();
    }

    public void SyncIntentFromProject()
    {
        _syncingIntent = true;
        try
        {
            var kind = _host.ProjectSettingsVM.ResolvedAnalysisKind;
            Settings.Intent = kind switch
            {
                AnalysisKind.Srf or AnalysisKind.CoupledStaticSrf => AnalysisIntent.Srf,
                AnalysisKind.GroundwaterSteady or AnalysisKind.CoupledSeepageSrf => AnalysisIntent.Seepage,
                AnalysisKind.GroundwaterTransient => AnalysisIntent.Consolidation,
                _ => AnalysisIntent.Static
            };
            Settings.PorePressureOption = _host.ProjectSettingsVM.WaterParticipation switch
            {
                WaterParticipation.Prescribed => "Prescribed",
                WaterParticipation.Seepage => "Seepage",
                _ => "None"
            };
        }
        finally
        {
            _syncingIntent = false;
        }
    }

    private void ApplyIntentToProject()
    {
        if (_syncingIntent)
            return;

        var cap = CurrentCapability;
        if (cap.Status == AnalysisCapabilityStatus.NotImplemented)
        {
            ShowCapabilityMessage(cap.UnavailableReason);
            return;
        }

        switch (Settings.Intent)
        {
            case AnalysisIntent.Srf:
                AnalysisKindMapper.ApplyAnalysisKindToLegacyCategory(_host.MaterialPropertiesVM, AnalysisKind.Srf);
                break;
            case AnalysisIntent.Seepage:
                AnalysisKindMapper.ApplyAnalysisKindToLegacyCategory(
                    _host.MaterialPropertiesVM, ResolveSeepageIntentKind());
                break;
            case AnalysisIntent.Consolidation:
                AnalysisKindMapper.ApplyAnalysisKindToLegacyCategory(_host.MaterialPropertiesVM, AnalysisKind.GroundwaterTransient);
                break;
            case AnalysisIntent.Coupled:
                AnalysisKindMapper.ApplyAnalysisKindToLegacyCategory(_host.MaterialPropertiesVM, AnalysisKind.CoupledSeepageSrf);
                ShowCapabilityMessage(cap.UnavailableReason);
                break;
            case AnalysisIntent.Dynamic:
                _host.SelectedTabIndex = 4;
                _host.LoadConditionsVM.IsSeismicExpanderExpanded = true;
                ShowCapabilityMessage(cap.UnavailableReason);
                break;
            default:
                AnalysisKindMapper.ApplyAnalysisKindToLegacyCategory(_host.MaterialPropertiesVM, AnalysisKind.Structural);
                break;
        }

        EnableCategoryForIntent(Settings.Intent);
        _host.FEAnalysisVM.RefreshResolvedRunRoutingDisplay();
        OnPropertyChanged(nameof(StatusStrip));
    }

    /// <summary>
    /// Analysis Type choice also turns the matching Project Settings category ON so Run FE is not blocked.
    /// Other categories stay as they were (not forced OFF).
    /// </summary>
    private void EnableCategoryForIntent(AnalysisIntent intent)
    {
        var settings = _host.ProjectSettingsVM;
        switch (intent)
        {
            case AnalysisIntent.Srf:
                settings.SetCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory, true);
                break;
            case AnalysisIntent.Seepage:
            case AnalysisIntent.Consolidation:
                settings.SetCategoryOn(MaterialPropertiesViewModel.GroundwaterAnalysisCategory, true);
                break;
            case AnalysisIntent.Coupled:
                settings.SetCategoryOn(MaterialPropertiesViewModel.GroundwaterAnalysisCategory, true);
                settings.SetCategoryOn(MaterialPropertiesViewModel.SrfAnalysisCategory, true);
                break;
            case AnalysisIntent.Dynamic:
            case AnalysisIntent.Thermal:
                break;
            default:
                settings.SetCategoryOn(FEAnalysisViewModel.StructuralStaticElasticityCategory, true);
                break;
        }
    }

    /// <summary>
    /// Seepage intent defaults to Steady. Keep Transient / Static when Define already wrote that leaf.
    /// </summary>
    private AnalysisKind ResolveSeepageIntentKind()
    {
        var materials = _host.MaterialPropertiesVM;
        var detailed = materials.SelectedDetailedAnalysisType;
        var mode = materials.LastGroundwaterTreeMode;
        if (MaterialPropertiesViewModel.IsTransientFlowAnalysisType(detailed)
            || MaterialPropertiesViewModel.IsGroundwaterTransientStateTreeType(mode))
            return AnalysisKind.GroundwaterTransient;
        if (string.Equals(detailed, MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal)
            || string.Equals(mode, MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal))
            return AnalysisKind.GroundwaterStatic;
        return AnalysisKind.GroundwaterSteady;
    }

    private void WireSrfIncrement()
    {
        if (Settings.StrengthReductionIncrement > 0
            && Math.Abs(_host.FEAnalysisVM.SrfBracketSeed - Settings.InitialStrengthReductionFactor) > 1e-9)
        {
            _host.FEAnalysisVM.SrfBracketSeed = Settings.InitialStrengthReductionFactor;
        }
    }

    private void OnSelectIntent(string? name)
    {
        if (Enum.TryParse<AnalysisIntent>(name, out var intent))
            Settings.Intent = intent;
    }

    private void OnSelectAnalysisRibbonTool(string? tool)
    {
        switch (tool)
        {
            case "Type":
                AnalysisRibbonTool = "Type";
                IsAnalysisTypeExpanded = true;
                break;
            case "InitialStress":
            case "Static":
            case "Seepage":
            case "Consolidation":
            case "Dynamic":
            case "Thermal":
            case "Coupled":
            case "Srf":
                AnalysisRibbonTool = tool;
                IsAnalysisTypeExpanded = true;
                OnSelectIntent(tool);
                break;
            case "Stages":
                AnalysisRibbonTool = "Stages";
                SelectCenterView(CenterViewStages);
                break;
            case "Manager":
                AnalysisRibbonTool = "Manager";
                SelectCenterView(CenterViewStages);
                break;
            default:
                AnalysisRibbonTool = "Type";
                break;
        }
    }

    private void ShowCapabilityByName(string? name)
    {
        if (Enum.TryParse<AnalysisIntent>(name, out var intent))
            ShowCapabilityMessage(AnalysisCapabilityCatalog.Describe(intent));
        else
            ShowCapabilityMessage(AnalysisCapabilityCatalog.FeatureNotAvailable);
    }

    public void SelectCenterView(string? view)
    {
        if (string.Equals(view, CenterView3D, StringComparison.OrdinalIgnoreCase))
        {
            CenterView = CenterView3D;
            AppendLog("3D View is not available in the current FEM engine.");
            return;
        }

        CenterView = view switch
        {
            CenterViewStages => CenterViewStages,
            CenterViewMonitor => CenterViewMonitor,
            CenterViewLog => CenterViewLog,
            _ => CenterView2D
        };
    }

    public void ApplyExplorerPayload(object? payload)
    {
        if (payload is not string token)
            return;
        switch (token)
        {
            case PayloadAnalysisSettings:
                IsAnalysisTypeExpanded = true;
                SelectCenterView(CenterView2D);
                break;
            case PayloadSolverSettings:
                SelectCenterView(CenterViewMonitor);
                break;
            case PayloadAnalysisStages:
                SelectCenterView(CenterViewStages);
                break;
        }
    }

    private void ApplySettings()
    {
        if (Settings.Method == "Limit Equilibrium")
        {
            ShowCapabilityMessage("Feature not available in current FEM engine. Limit equilibrium is not the live solver path.");
            Settings.Method = "Strength Reduction";
        }

        if (IsSrfIntent && Settings.SsrType == "Local")
            ShowCapabilityMessage("Local SSR is not available. Global strength reduction is the live path.");

        ApplyIntentToProject();
        WireSrfIncrement();
        RaiseSummaryChanged();
        AppendLog("Applied analysis settings: " + AnalysisTypeSummary);
    }

    private void ClearLog()
    {
        _log.Clear();
        OnPropertyChanged(nameof(SolverLog));
    }

    private void OnFePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FEAnalysisViewModel.StatusMessage)
            && !string.IsNullOrWhiteSpace(_host.FEAnalysisVM.StatusMessage))
            AppendLog(_host.FEAnalysisVM.StatusMessage);

        if (e.PropertyName is nameof(FEAnalysisViewModel.ActiveAnalysisModeSummary)
            or nameof(FEAnalysisViewModel.HasEnabledSurfaceOrLineLoad)
            or nameof(FEAnalysisViewModel.IsSolving)
            or nameof(FEAnalysisViewModel.ResolvedSolverForRun)
            or nameof(FEAnalysisViewModel.ExecutionStatus)
            or nameof(FEAnalysisViewModel.ConvergenceTolerance))
        {
            OnPropertyChanged(nameof(StatusStrip));
            OnPropertyChanged(nameof(StatusReady));
            RaiseSummaryChanged();
        }
    }

    private void OnResultsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ResultsViewModel.EstimatedSrfFoS)
            or nameof(ResultsViewModel.SrfTrialRows)
            or nameof(ResultsViewModel.LastP610Run))
            RaiseSummaryChanged();
    }

    private void OnFieldStressChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FieldStressSpec.UseEffectiveStressRatio))
            OnPropertyChanged(nameof(SelectedFormulation));
        if (e.PropertyName is nameof(FieldStressSpec.Mode))
            OnPropertyChanged(nameof(SelectedInitialStressMethod));
    }

    private void OnMaterialsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MaterialPropertiesViewModel.SelectedFailureCriterion))
            return;
        OnPropertyChanged(nameof(IsMaterialNonlinear));
        OnPropertyChanged(nameof(MaterialNonlinearityLabel));
    }

    private void RaiseSummaryChanged()
    {
        OnPropertyChanged(nameof(AnalysisTypeSummary));
        OnPropertyChanged(nameof(CurrentStageSummary));
        OnPropertyChanged(nameof(LastSrfSummary));
        OnPropertyChanged(nameof(AnalysisStatusSummary));
        OnPropertyChanged(nameof(AnalysisStatusBrush));
        OnPropertyChanged(nameof(SettingsSummaryText));
        OnPropertyChanged(nameof(SolverTypeLabel));
        OnPropertyChanged(nameof(SolutionSchemeLabel));
        OnPropertyChanged(nameof(SolverToleranceLabel));
        OnPropertyChanged(nameof(SolverIterationLabel));
        OnPropertyChanged(nameof(SolverProgressPercent));
        OnPropertyChanged(nameof(SolverProgressLabel));
        OnPropertyChanged(nameof(StageManagerRows));
    }

    private string ResolveStageStatus(bool isActive)
    {
        if (_host.FEAnalysisVM.IsSolving)
            return "Running";
        if (_host.ResultsVM.SrfTrialRows.Any(r => r.Failed))
            return "Failed";
        if (_host.ResultsVM.LastP610Run != null || _host.ResultsVM.SrfTrialRows.Count > 0)
            return isActive ? "Complete" : "Inactive";
        return isActive ? "Pending" : "Inactive";
    }

    private void AppendLog(string line)
    {
        if (_log.Length > 0)
            _log.AppendLine();
        _log.Append(DateTime.Now.ToString("HH:mm:ss"));
        _log.Append("  ");
        _log.Append(line.Trim());
        if (_log.Length > 32000)
            _log.Remove(0, _log.Length - 24000);
        OnPropertyChanged(nameof(SolverLog));
    }

    private static string IntentLabel(AnalysisIntent intent) => intent switch
    {
        AnalysisIntent.InitialStress => "Initial",
        AnalysisIntent.Static => "Stress",
        AnalysisIntent.Srf => "SRF",
        AnalysisIntent.Seepage => "Seepage",
        AnalysisIntent.Consolidation => "Consolidation",
        AnalysisIntent.Dynamic => "Dynamic",
        AnalysisIntent.Thermal => "Thermal",
        AnalysisIntent.Coupled => "Coupled",
        _ => "Stress"
    };

    private static ObservableCollection<ResultCategoryItem> BuildResultCategories() =>
        new()
        {
            new("Displacement", ResultsViewModel.OutputDisplacementMagnitude, ResultsViewModel.VisualizationModeContour2D),
            new("Displacement X", ResultsViewModel.OutputDisplacementX, ResultsViewModel.VisualizationModeContour2D),
            new("Displacement Y", ResultsViewModel.OutputDisplacementY, ResultsViewModel.VisualizationModeContour2D),
            new("σxx", ResultsViewModel.OutputStressXX, ResultsViewModel.VisualizationModeContour2D),
            new("σyy", ResultsViewModel.OutputStressYY, ResultsViewModel.VisualizationModeContour2D),
            new("τxy", ResultsViewModel.OutputStressXY, ResultsViewModel.VisualizationModeContour2D),
            new("εxx", ResultsViewModel.OutputStrainXX, ResultsViewModel.VisualizationModeContour2D),
            new("γxy", ResultsViewModel.OutputShearStrain, ResultsViewModel.VisualizationModeContour2D),
            new("Yield", ResultsViewModel.OutputYieldRatio, ResultsViewModel.VisualizationModeContour2D, true),
            new("Pore pressure", ResultsViewModel.OutputPorePressure, ResultsViewModel.VisualizationModeContour2D),
            new("Seepage head", ResultsViewModel.OutputSeepageTotalHead, ResultsViewModel.VisualizationModeContour2D),
            new("Joint σn", ResultsViewModel.OutputJointNormalStress, ResultsViewModel.VisualizationModeContour2D),
            new("Joint τ", ResultsViewModel.OutputJointShearStress, ResultsViewModel.VisualizationModeContour2D),
            new("Vectors", ResultsViewModel.OutputDisplacementMagnitude, ResultsViewModel.VisualizationModeVector),
            new("Deformed mesh", ResultsViewModel.OutputDisplacementMagnitude, ResultsViewModel.VisualizationModeDeformedMesh)
        };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void SetGated(ref string field, string? value, IReadOnlyList<string> choices, [CallerMemberName] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value) || !choices.Contains(value) || field == value)
            return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ResultCategoryItem
{
    public ResultCategoryItem(string title, string outputType, string visualizationMode, bool selectCriticalSrf = false)
    {
        Title = title;
        OutputType = outputType;
        VisualizationMode = visualizationMode;
        SelectCriticalSrf = selectCriticalSrf;
    }

    public string Title { get; }
    public string OutputType { get; }
    public string VisualizationMode { get; }
    public bool SelectCriticalSrf { get; }
}

public sealed class AnalysisStageRow
{
    public AnalysisStageRow(int stage, string name, string type, string status)
    {
        Stage = stage;
        Name = name;
        Type = type;
        Status = status;
    }

    public int Stage { get; }
    public string Name { get; }
    public string Type { get; }
    public string Status { get; }
}
