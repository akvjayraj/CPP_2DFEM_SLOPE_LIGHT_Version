using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Appearance;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.Persistence;
using GeotechSlopeApp.Services;
using GeotechSlopeApp.Services.Appearance;
using GeotechSlopeApp.Services.Lem;
using GeotechSlopeApp.Services.Wave3;
using GeotechSlopeApp.ViewModels.Dialogs;
using GeotechSlopeApp.ViewModels.Workspace;
using GeotechSlopeApp.Views.Dialogs;
using Microsoft.Win32;

namespace GeotechSlopeApp.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged, IProjectUndoHost
{
    private int _selectedTabIndex;
    private int? _currentStage;
    private bool _zoomPanModeEnabled;
    private bool _zoomAreaModeEnabled;
    private bool _areMainTabsEnabled = true;
    private string? _currentXfemPath;
    private string _savedSnapshotJson = "";
    private readonly ProjectHistoryService _history = new();
    private ICommand _undoCommand = null!;
    private ICommand _redoCommand = null!;
    private DispatcherTimer? _stageFooterDebounceTimer;
    private int? _pendingFooterStageNumber;
    private readonly List<StageDefinition> _stageFooterSyncSubscriptions = new();
    private bool _useAnalysisWorkspace;

    /// <summary>Geometry Definition tab — user-drawn boundary/internal segment lines on the canvas.</summary>
    private const int GeometryDefinitionMainTabIndex = 1;
    /// <summary>Main tab index for Material Properties (see MainWindow TabControl order).</summary>
    private const int MaterialPropertiesMainTabIndex = 2;
    /// <summary>Mesh tab — same surface/layer labeling as Material Properties canvas.</summary>
    private const int MeshMainTabIndex = 3;
    /// <summary>Load Conditions tab — same surface/layer labeling as Material Properties canvas.</summary>
    private const int LoadConditionsMainTabIndex = 4;
    /// <summary>Boundary Conditions tab — toolbar shortcuts (import boundaries, markers, clear) light up here.</summary>
    private const int BoundaryConditionsMainTabIndex = 5;
    /// <summary>Groundwater tab — only tab that shows saturated/unsaturated zone fills on the canvas.</summary>
    private const int GroundwaterMainTabIndex = 6;
    private const int FeAnalysisMainTabIndex = 7;
    private const int ResultsVisualizationMainTabIndex = 9;

    public MainWindowViewModel()
    {
        ResultsVM = new ResultsViewModel();
        ResultsVM.FooterStageSelectionRequested += SelectFooterStageFromResults;
        FEAnalysisVM = new FEAnalysisViewModel(ResultsVM);
        // CppExecutableDirectory defaults to empty = app base dir; post-build copies p41.exe there when C++ is built
        ProjectSettingsVM = new ProjectSettingsViewModel();
        GeometryDefinitionVM = new GeometryDefinitionViewModel();
        MaterialPropertiesVM = new MaterialPropertiesViewModel { GeometryDefinition = GeometryDefinitionVM };
        LoadConditionsVM = new LoadConditionsViewModel { GeometryDefinition = GeometryDefinitionVM };
        MeshVM = new MeshViewModel { GeometryDefinitionVM = GeometryDefinitionVM };
        MeshVM.SetMaterialProperties(MaterialPropertiesVM);
        MeshVM.RequestShowGeometryTab = () => SelectedTabIndex = 1;
        GeometryDefinitionVM.MeshSource = MeshVM;
        GeometryDefinitionVM.ProjectSettings = ProjectSettingsVM;
        GeometryDefinitionVM.SyncGridSpacing(ProjectSettingsVM.Project.GridSpacingMeters);
        GeometryDefinitionVM.ShowGrid = ProjectSettingsVM.Project.ShowGrid;
        GeometryDefinitionVM.ShowAxes = ProjectSettingsVM.Project.ShowAxes;
        FEAnalysisVM.SetMeshSource(MeshVM);
        LoadConditionsVM.SetMeshAndProject(MeshVM, ProjectSettingsVM);
        LoadConditionsVM.SetMaterialsSource(MaterialPropertiesVM);
        GeometryDefinitionVM.SetLoadOverlaySource(LoadConditionsVM);
        BoundaryConditionsVM = new BoundaryConditionsViewModel
        {
            GeometryDefinitionVM = GeometryDefinitionVM,
            MeshVM = MeshVM,
            MaterialPropertiesVM = MaterialPropertiesVM,
            ProjectSettingsVM = ProjectSettingsVM
        };
        // Make the Boundary Conditions tab the source of BCs that the FE Analysis tab
        // picks up when the user clicks "Load Mesh Data".
        FEAnalysisVM.SetBoundaryConditionsSource(BoundaryConditionsVM);
        MeshTabCanvasContext = new MeshTabCanvasWrapper(GeometryDefinitionVM, MeshVM, null, MaterialPropertiesVM);
        // Dedicated wrapper for the BC tab. Same geometry+mesh source as the Mesh tab, plus the
        // BoundaryConditionsViewModel so SlopeCanvasView can render Show-Marker overlays
        // (red triangle = Fixed, blue cross = Roller X) only on the Boundary Conditions canvas.
        BoundaryConditionsCanvasContext = new MeshTabCanvasWrapper(GeometryDefinitionVM, MeshVM, BoundaryConditionsVM, MaterialPropertiesVM);
        // ── Staged construction ────────────────────────────────────────────────
        StagedConstructionVM = new StagedConstructionViewModel();
        StagedConstructionVM.SetGeometryViewModel(GeometryDefinitionVM);
        StagedConstructionVM.SetFeAnalysisViewModel(FEAnalysisVM);
        StagedConstructionVM.SetMeshViewModel(MeshVM);
        FEAnalysisVM.StagedConstruction = StagedConstructionVM;

        // Stage-aware mesh hide: when a Stage 2+ footer tab is active, the cumulative excavated
        // element IDs disappear from BOTH the Mesh tab canvas and the Boundary Conditions tab
        // canvas. Wired AFTER StagedConstructionVM is created so the wrappers get a non-null link.
        MeshTabCanvasContext.EnableStageAwareFilter(StagedConstructionVM);
        BoundaryConditionsCanvasContext.EnableStageAwareFilter(StagedConstructionVM);
        StagedConstructionVM.ResolveFooterStageNumber = () =>
        {
            if (!FEAnalysisVM.StagedExcavationEnabled) return null;
            var ps = ProjectSettingsVM;
            if (ps.SelectedStageFooterTabIndex is int idx && idx >= 0 && idx < ps.StageFooterTabs.Count)
                return ps.StageFooterTabs[idx].StageNumber;
            return null;
        };
        GeometryDefinitionVM.StagedConstruction = StagedConstructionVM;
        MaterialPropertiesVM.StagedConstruction  = StagedConstructionVM;
        // Loads are partitioned per stage: switching the footer stage rebuilds FilteredLoads / canvas overlay.
        LoadConditionsVM.SetStagedConstruction(StagedConstructionVM);
        LoadConditionsVM.SetFeAnalysisSource(FEAnalysisVM);
        // Forward IsExcavateMode property-changes so XAML bindings refresh
        StagedConstructionVM.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(StagedConstructionViewModel.IsExcavateMode)
                or nameof(StagedConstructionViewModel.ExcavateModeButtonLabel)
                or nameof(StagedConstructionViewModel.StatusMessage))
            {
                MaterialPropertiesVM.OnStagedConstructionPropertyChanged(e.PropertyName);
            }
        };

        ProjectSettingsVM.SetAxisSyncTargets(GeometryDefinitionVM, MaterialPropertiesVM);
        ProjectSettingsVM.SetFeAnalysisViewModel(FEAnalysisVM);
        MaterialPropertiesVM.SetProjectSettings(ProjectSettingsVM);
        MeshVM.SetProjectSettings(ProjectSettingsVM);
        MeshVM.SetMaterialProperties(MaterialPropertiesVM);
        GroundwaterVM = new GroundwaterViewModel { GeometryDefinition = GeometryDefinitionVM };
        SupportVM = new SupportViewModel { GeometryDefinition = GeometryDefinitionVM };
        GroundwaterVM.SetProjectSettingsViewModel(ProjectSettingsVM);
        GroundwaterVM.SetMaterialPropertiesViewModel(MaterialPropertiesVM);
        GroundwaterVM.SetMeshViewModel(MeshVM);
        ProjectSettingsVM.SetGroundwaterViewModel(GroundwaterVM);
        GeometryDefinitionVM.SetGroundwaterViewModel(GroundwaterVM);
        VerificationVM = new VerificationViewModel(ResultsVM);
        ReportGenerationVM = new ReportGenerationViewModel();
        SensitivityAnalysisVM = new SensitivityAnalysisViewModel();
        ProbabilisticAnalysisVM = new ProbabilisticAnalysisViewModel();
        BackAnalysisVM = new BackAnalysisViewModel();
        AnalysisCasesVM = new AnalysisCasesViewModel();
        ScriptVM = new ScriptViewModel(this);
        GroundwaterVM.SetScriptViewModel(ScriptVM);
        MeshVM.SetScriptAndBoundary(ScriptVM, BoundaryConditionsVM, MaterialPropertiesVM, FEAnalysisVM);
        // Model Health is intentionally constructed AFTER all the source view-models
        // it observes (mesh, BCs, loads, geometry, staging) so its constructor can
        // wire CollectionChanged handlers without racing against a half-built graph.
        ModelHealthVM = new ModelHealthViewModel(
            MeshVM, BoundaryConditionsVM, LoadConditionsVM, GeometryDefinitionVM, StagedConstructionVM);
        FEAnalysisVM.SetAnalysisContext(MaterialPropertiesVM, ScriptVM);
        FEAnalysisVM.SetMainWindow(this);
        MaterialPropertiesVM.SetFeAnalysisViewModel(FEAnalysisVM);

        History.HistoryChanged += OnProjectHistoryChanged;
        _undoCommand = new RelayCommand(OnUndo, () => History.CanUndo);
        _redoCommand = new RelayCommand(OnRedo, () => History.CanRedo);
        WireUndoHosts();

        StagedAnalysisVM = new StagedAnalysisViewModel(FEAnalysisVM, StagedConstructionVM, GeometryDefinitionVM);
        SubscribeStagedAnalysisFooterSync();
        SyncFooterWithStagedAnalysisGrid();

        SensitivityAnalysisVM.BindHost(this);
        ProbabilisticAnalysisVM.BindHost(this);
        BackAnalysisVM.BindHost(this);
        AnalysisCasesVM.BindHost(this);
        ReportGenerationVM.BindHost(this);

        AppearanceVM = new UiAppearanceViewModel(UiAppearanceService.Instance, on =>
        {
            if (_useAnalysisWorkspace == on) return;
            _useAnalysisWorkspace = on;
            OnPropertyChanged(nameof(UseAnalysisWorkspace));
            OnPropertyChanged(nameof(UseClassicTabs));
            NotifyWorkspaceChromeChanged();
        });
        RecentProjects = new RecentProjectsStore("xfem");
        RecentProjects.Load();
        Workspace = new AnalysisWorkspaceViewModel(this);
        _useAnalysisWorkspace = UiAppearanceService.Instance.Settings.LayoutMode == UiLayoutMode.AnalysisWorkspace;
        NotifyWorkspaceChromeChanged();
        Workspace.Explorer.Refresh();

        ProjectWorkspace = new ProjectWorkspaceManager();
        ProjectWorkspace.Initialize(this);

        ResultsVM.ProjectDimension = ProjectSettingsVM.SelectedDimension;
        ResultsVM.SetProjectSettingsViewModel(ProjectSettingsVM);
        ProjectSettingsVM.PropertyChanged += OnProjectSettingsPropertyChanged;

        OpenProjectCommand = new RelayCommand(OnOpenProject);
        SaveCommand = new RelayCommand(OnSave);
        SaveAsCommand = new RelayCommand(OnSaveAs);
        SaveCopyCommand = new RelayCommand(OnSaveCopy);
        NewProjectCommand = new RelayCommand(OnNewProject);
        CloseProjectCommand = new RelayCommand(OnCloseProject);

        ImportGeometryFileCommand = new RelayCommand(() => GeometryDefinitionVM.ImportGeometryFromCsvCommand.Execute(null));
        ImportSlopeImageCommand = new RelayCommand(() => GeometryDefinitionVM.ImportSlopeImageCommand.Execute(null));
        ImportMeshFileCommand = new RelayCommand(OnImportMesh);
        ImportBoundaryConditionsCommand = new RelayCommand(OnImportBoundaryConditions);

        ExportGeometryCommand = new RelayCommand(() => GeometryDefinitionVM.ExportGeometryToCsvCommand.Execute(null));
        ExportMeshCommand = new RelayCommand(OnExportMesh);
        ExportResultsCommand = new RelayCommand(OnExportResults);
        ExportReportCommand = new RelayCommand(OnExportReport);
        ExportToCsvCommand = new RelayCommand(() => GeometryDefinitionVM.ExportGeometryToCsvCommand.Execute(null));
        ExportToAbaqusCommand = new RelayCommand(OnExportToAbaqus);
        ExportToAnsysCommand = new RelayCommand(OnExportToAnsys);

        // Route Print through the app preview window so Windows print UI
        // does not show the "this app doesn't support print preview" panel.
        PrintCommand = new RelayCommand(() => RequestPrintPreview?.Invoke());
        PrintPreviewCommand = new RelayCommand(() => RequestPrintPreview?.Invoke());
        PageSetupCommand = new RelayCommand(() => RequestPageSetup?.Invoke());
        OpenRecentProjectCommand = new RelayCommand<string>(OnOpenRecentProject);
        OpenRecentByIndexCommand = new RelayCommand<object?>(OnOpenRecentByIndex);
        ClearRecentFilesCommand = new RelayCommand(() => RecentProjects.Clear());

        // ─── View ───
        TogglePanModeCommand = new RelayCommand(() => ZoomPanModeEnabled = !ZoomPanModeEnabled);
        ExitPanModeCommand = new RelayCommand(() => ZoomPanModeEnabled = false);
        ToggleZoomAreaModeCommand = new RelayCommand(() => ZoomAreaModeEnabled = !ZoomAreaModeEnabled);
        ZoomInCommand = new RelayCommand(() => ZoomInCanvases?.Invoke());
        ZoomOutCommand = new RelayCommand(() => ZoomOutCanvases?.Invoke());
        ZoomToFitCommand = new RelayCommand(() => ZoomToFitCanvases?.Invoke());
        ResetZoomCommand = new RelayCommand(() => ResetZoomCanvases?.Invoke());
        ToggleNodeNumbersCommand = new RelayCommand(() => MeshVM.ShowMeshNodes = !MeshVM.ShowMeshNodes);
        ToggleElementNumbersCommand = new RelayCommand(() => MeshVM.ShowMeshElements = !MeshVM.ShowMeshElements);
        MeshInformationPopupCommand = new RelayCommand(() => RequestOpenMeshToolbarPopup?.Invoke(MeshToolbarPopupKind.Information));
        UnstructuredMeshPopupCommand = new RelayCommand(() =>
        {
            MeshVM.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupRs2dTabIndex;
            RequestOpenMeshToolbarPopup?.Invoke(MeshToolbarPopupKind.Unstructured);
        });
        StructuredMeshPopupCommand = new RelayCommand(() =>
        {
            MeshVM.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupUserDefinedTabIndex;
            RequestOpenMeshToolbarPopup?.Invoke(MeshToolbarPopupKind.Structured);
        });

        // ─── Edit ───
        CutCommand = Stub("Cut");
        CopyCommand = new RelayCommand(() => GeometryDefinitionVM.CopyGeometryCommand.Execute(null));
        PasteCommand = new RelayCommand(() => GeometryDefinitionVM.PasteGeometryCommand.Execute(null));
        DeleteSelectionCommand = new RelayCommand(OnDeleteSelection);
        SelectAllCommand = Stub("Select All");
        DeselectAllCommand = Stub("Deselect All");
        PreferencesCommand = new RelayCommand(() => RequestPreferencesDialog?.Invoke());
        SetClassicLayoutCommand = new RelayCommand(() => UseAnalysisWorkspace = false);
        SetWorkspaceLayoutCommand = new RelayCommand(() => UseAnalysisWorkspace = true);
        WorkspaceF7Command = new RelayCommand(() =>
        {
            if (UseAnalysisWorkspace)
                SelectedTabIndex = MeshMainTabIndex;
            else
                GeometryDefinitionVM.ToggleShowGridCommand.Execute(null);
        });

        // ─── Geometry ───
        NavigateToGeometryCommand = new RelayCommand(() =>
        {
            if (UseAnalysisWorkspace)
                Workspace.RibbonTab = "GEOMETRY";
            SelectedTabIndex = 1;
        });
        MenuAddVertexCommand = new RelayCommand(() => { SelectedTabIndex = 1; GeometryDefinitionVM.RequestOpenPointsPanel?.Invoke(); });
        MenuAddLineSegmentCommand = new RelayCommand(() => { SelectedTabIndex = 1; GeometryDefinitionVM.RequestOpenLineSegmentsPanel?.Invoke(); });
        MenuAddArcCommand = new RelayCommand(() => { SelectedTabIndex = 1; GeometryDefinitionVM.RequestOpenArcPanel?.Invoke(); });
        MenuAddCircleCommand = new RelayCommand(() => { SelectedTabIndex = 1; GeometryDefinitionVM.RequestOpenCirclePanel?.Invoke(); });
        GeometryClosedPolygonCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 1;
            GeometryDefinitionVM.SetClosedPolygonModeCommand.Execute(null);
            GeometryDefinitionVM.RequestOpenPointsPanel?.Invoke();
        });
        GeometryOpenPolygonCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 1;
            GeometryDefinitionVM.SetOpenPolygonModeCommand.Execute(null);
            GeometryDefinitionVM.RequestOpenPointsPanel?.Invoke();
        });
        MenuAddSurfaceCommand = new RelayCommand(() => { SelectedTabIndex = 1; GeometryDefinitionVM.RequestOpenSurfacePanel?.Invoke(); });
        GeometryBooleanUnionCommand = new RelayCommand(() => GeometryDefinitionVM.StartExcavationModeCommand.Execute(null));
        GeometryBooleanIntersectCommand = new RelayCommand(() => GeometryDefinitionVM.StartExcavationModeCommand.Execute(null));
        GeometryBooleanSubtractCommand = new RelayCommand(() => GeometryDefinitionVM.StartExcavationModeCommand.Execute(null));
        VerifyGeometryCommand = new RelayCommand(ShowVerifyGeometry);
        SnapSettingsCommand = new RelayCommand(ShowSnapSettings);
        GridSettingsCommand = new RelayCommand(() =>
            OpenProjectSettings(ProjectSettingsTabKind.GridAxes));
        ProjectSettingsGeneralCommand = new RelayCommand(() =>
            OpenProjectSettings(ProjectSettingsTabKind.General));
        ProjectSettingsAnalysisTypeCommand = new RelayCommand(() =>
            OpenProjectSettings(ProjectSettingsTabKind.AnalysisType));
        OpenAnalysisTypeWizardCommand = new RelayCommand(() => RequestAnalysisTypeWizardDialog?.Invoke());
        OpenTunnelWizardCommand = new RelayCommand(OpenTunnelWizard);
        OpenJointWizardCommand = new RelayCommand(OpenJointWizard);
        OpenJointPropertiesCommand = new RelayCommand(OpenJointProperties);

        // ─── Material Properties ───
        NavigateToMaterialsCommand = new RelayCommand(() =>
        {
            if (UseAnalysisWorkspace)
                Workspace.RibbonTab = "MATERIALS";
            SelectedTabIndex = 2;
        });
        AddMaterialCommand              = new RelayCommand(() => MaterialPropertiesVM.OpenAddMaterialDialogCommand.Execute(null));
        EditMaterialCommand             = new RelayCommand(() => MaterialPropertiesVM.EditMaterialCommand.Execute(null));
        DeleteMaterialCommand           = new RelayCommand(() => MaterialPropertiesVM.RemoveMaterialCommand.Execute(null));
        DuplicateMaterialCommand        = new RelayCommand(() => MaterialPropertiesVM.DuplicateMaterialCommand.Execute(null));
        AssignMaterialToRegionCommand   = new RelayCommand(() => MaterialPropertiesVM.AssignMaterialToRegionCommand.Execute(null));
        ClearMaterialAssignmentsCommand = new RelayCommand(() => MaterialPropertiesVM.ClearMaterialAssignmentsCommand.Execute(null));
        MaterialLibraryCommand          = new RelayCommand(() => MaterialPropertiesVM.OpenMaterialLibraryCommand.Execute(null));
        ImportMaterialDatabaseCommand   = new RelayCommand(() => MaterialPropertiesVM.ImportMaterialDatabaseCommand.Execute(null));
        ExportMaterialsCommand          = new RelayCommand(() => MaterialPropertiesVM.ExportMaterialsCommand.Execute(null));
        MaterialLayeringControlsCommand = new RelayCommand(() =>
        {
            MaterialPropertiesVM.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.LayeringControlsSubtabIndex;
            RequestOpenMaterialPopupSubtab?.Invoke(MaterialPropertiesViewModel.LayeringControlsSubtabIndex);
        });
        MaterialProjectMaterialsCommand = new RelayCommand(() =>
        {
            MaterialPropertiesVM.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.ProjectMaterialsSubtabIndex;
            RequestOpenMaterialPopupSubtab?.Invoke(MaterialPropertiesViewModel.ProjectMaterialsSubtabIndex);
        });
        MaterialLayerMaterialAssignmentCommand = new RelayCommand(() =>
        {
            MaterialPropertiesVM.SelectedMaterialSubtabIndex = MaterialPropertiesViewModel.LayerMaterialAssignmentSubtabIndex;
            RequestOpenMaterialPopupSubtab?.Invoke(MaterialPropertiesViewModel.LayerMaterialAssignmentSubtabIndex);
        });

        // ─── Loading ───
        NavigateToLoadConditionsCommand = new RelayCommand(() =>
        {
            if (UseAnalysisWorkspace)
                Workspace.RibbonTab = "LOADS";
            SelectedTabIndex = 4;
        });
        MenuAddLoadCommand = new RelayCommand(() => { SelectedTabIndex = 4; LoadConditionsVM.AddLoadCommand.Execute(null); });
        LoadFieldStressPopupCommand = new RelayCommand(() => RequestOpenLoadToolbarPopup?.Invoke(LoadToolbarPopupKind.FieldStress));
        LoadSeismicLoadPopupCommand = new RelayCommand(() =>
        {
            LoadConditionsVM.IsSeismicExpanderExpanded = true;
            RequestOpenLoadToolbarPopup?.Invoke(LoadToolbarPopupKind.SeismicLoad);
        });
        LoadLoadsPopupCommand = new RelayCommand(() => RequestOpenLoadToolbarPopup?.Invoke(LoadToolbarPopupKind.Loads));
        BcImportBoundariesPopupCommand = new RelayCommand(() =>
        {
            BoundaryConditionsVM.ImportOuterBoundariesCommand.Execute(null);
            RequestOpenBcImportBoundariesPopup?.Invoke();
        });
        DeleteLoadCommand = Stub("Delete Load");
        ClearAllLoadsCommand = Stub("Clear All Loads");
        AddPointLoadCommand = Stub("Add Point Load");
        AddLineLoadCommand = Stub("Add Line Load");
        AddPressureLoadCommand = Stub("Add Pressure Load");
        AddBodyForceCommand = Stub("Add Body Force");
        AddGravityLoadCommand = Stub("Add Gravity Load");
        AddSeismicLoadCommand = new RelayCommand(OnAddSeismicLoad);
        LoadCombinationsCommand = Stub("Load Combinations");
        LoadCasesCommand = new RelayCommand(() => SelectedTabIndex = 10);
        ImportLoadsCommand = Stub("Import Loads");
        ExportLoadsCommand = Stub("Export Loads");

        // ─── Analysis ───
        NavigateToFEAnalysisCommand = new RelayCommand(() => SelectedTabIndex = 7);
        RunFEAnalysisMenuCommand = new RelayCommand(() => { SelectedTabIndex = 7; FEAnalysisVM.RunAnalysisCommand.Execute(null); });
        StopAnalysisCommand = FEAnalysisVM.StopAnalysisCommand;
        AnalysisSettingsCommand = Stub("Analysis Settings");
        SolverOptionsCommand = Stub("Solver Options");
        ConvergenceCriteriaCommand = Stub("Convergence Criteria");
        NavigateToMeshCommand = new RelayCommand(() =>
        {
            if (UseAnalysisWorkspace)
                Workspace.RibbonTab = "MESH";
            SelectedTabIndex = 3;
        });
        NavigateToBoundaryConditionsCommand = new RelayCommand(() => SelectedTabIndex = 5);
        NavigateToGroundwaterCommand = new RelayCommand(() =>
        {
            if (UseAnalysisWorkspace)
                Workspace.RibbonTab = "GROUNDWATER";
            SelectedTabIndex = 6;
        });
        NavigateToSupportCommand = new RelayCommand(() => SelectedTabIndex = 13);
        NavigateToFieldStationsCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 0;
            ProjectSettingsVM.SelectSettingsTab(ProjectSettingsTabKind.FieldStations);
        });
        NavigateToSensitivityCommand = new RelayCommand(() => SelectedTabIndex = 10);
        NavigateToVerificationCommand = new RelayCommand(() => SelectedTabIndex = 8);
        AnalysisHistoryCommand = Stub("Analysis History");
        ValidationCheckCommand = new RelayCommand(() =>
        {
            ModelHealthVM.RefreshCommand.Execute(null);
            if (UseAnalysisWorkspace)
                ShowCapabilityMessage("Check Model: " + ModelHealthVM.SummaryText);
            else
                MessageBox.Show(ModelHealthVM.SummaryText, "Check Model", MessageBoxButton.OK, MessageBoxImage.Information);
        });

        // ─── Results ───
        NavigateToResultsCommand = new RelayCommand(() =>
        {
            if (UseAnalysisWorkspace)
                Workspace.RibbonTab = "RESULTS";
            SelectedTabIndex = 9;
            Workspace.Results.ApplyStaticDefaults();
        });
        ContourPlotStressCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 9;
            ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeContour2D;
            ResultsVM.SelectedOutputType = ResultsViewModel.OutputVonMises;
        });
        ContourPlotStrainCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 9;
            ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeContour2D;
            ResultsVM.SelectedOutputType = ResultsViewModel.OutputMaximumShearStrain;
            ResultsVM.SelectCriticalSrfStageForVisualization();
        });
        ContourPlotDisplacementCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 9;
            ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeContour2D;
            ResultsVM.SelectedOutputType = ResultsViewModel.OutputDisplacementMagnitude;
        });
        ContourPlotPorePressureCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 9;
            ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeContour2D;
            ResultsVM.SelectedOutputType = ResultsViewModel.OutputPorePressure;
            ResultsVM.SelectCriticalSrfStageForVisualization();
        });
        ContourPlotYieldZonesCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 9;
            ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeContour2D;
            ResultsVM.SelectedOutputType = ResultsViewModel.OutputYieldRatio;
            ResultsVM.SelectCriticalSrfStageForVisualization();
        });
        VectorPlotDisplacementCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 9;
            ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeVector;
            ResultsVM.SelectedOutputType = ResultsViewModel.OutputDisplacementMagnitude;
        });
        VectorPlotFluxCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 9;
            ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeVector;
            ResultsVM.SelectedOutputType = ResultsViewModel.OutputSeepageDischarge;
        });
        DeformedShapeCommand = new RelayCommand(() =>
        {
            SelectedTabIndex = 9;
            ResultsVM.VisualizationMode = ResultsViewModel.VisualizationModeDeformedMesh;
        });
        QueryNodeCommand = Stub("Query Node");
        QueryElementCommand = Stub("Query Element");
        GenerateReportMenuCommand = new RelayCommand(() => SelectedTabIndex = 11);
        CopySummaryCommand = Stub("Copy Summary");
        AnimateResultsCommand = Stub("Animate Results");

        // ─── Window ───
        NavigateToTabCommand = new RelayCommand<object>(p =>
        {
            if (p is int i) SelectedTabIndex = i;
            else if (p is string s && int.TryParse(s, out var parsed)) SelectedTabIndex = parsed;
        });

        // ─── Help ───
        ViewHelpCommand = new RelayCommand(OnViewHelp);
        GettingStartedCommand = new RelayCommand(() => HelpWindow.ShowTopic(Application.Current?.MainWindow, "quick-start"));
        TutorialsCommand = new RelayCommand(OnTutorials);
        TheoryManualCommand = Stub("Theory Manual");
        UserManualCommand = new RelayCommand(OnUserManual);
        CheckForUpdatesCommand = Stub("Check for Updates");
        LicenseInfoCommand = Stub("License Information");
        AboutCommand = new RelayCommand(OnAbout);

        SelectedTabIndex = 0;
        ApplyGeometryCanvasDecorationsForTab(_selectedTabIndex);
        RefreshSavedBaseline();
    }

    private static RelayCommand Stub(string featureName) =>
        new RelayCommand(() => MessageBox.Show(
            $"{featureName} is not yet implemented.",
            featureName, MessageBoxButton.OK, MessageBoxImage.Information));

    private void OnAddSeismicLoad()
    {
        SelectedTabIndex = LoadConditionsMainTabIndex;
        LoadConditionsVM.IsSeismicExpanderExpanded = true;
        if (!LoadConditionsVM.IsSeismicLoadUiEnabled)
        {
            MessageBox.Show(
                "Enable staged analysis with a Seismic / Dynamic stage under Project Settings → Analysis Type → Stage Analysis, " +
                "or load a Script Editor .dat that contains ! k_h / ! k_v in AnalysisOptions.",
                "Seismic / Inertia Load",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// Tools → Tunnel Wizard. Constructs the wizard view-model with sane defaults, hands it to the
    /// MainWindow's dialog factory, and on Apply runs the orchestrator that mutates the geometry,
    /// mesh, BCs, field stress, and staged-construction view-models in one transaction.
    /// </summary>
    private void OpenTunnelWizard()
    {
        if (RequestTunnelWizardDialog == null)
        {
            MessageBox.Show("Tunnel Wizard dialog factory is not wired (MainWindow code-behind missing).",
                "Tunnel Wizard", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedTabIndex = 1; // Geometry tab so the wizard's effects (mesh, polygon overlay) are visible.

        var wizard = new TunnelWizardViewModel();
        wizard.ApplyRequested += (_, _) =>
        {
            RecordUndoPoint("Tunnel wizard apply");
            var applier = new TunnelWizardApplier(
                GeometryDefinitionVM, MeshVM, BoundaryConditionsVM, LoadConditionsVM.FieldStress, StagedConstructionVM);
            var report = applier.Apply(wizard);
            if (!report.Success)
            {
                MessageBox.Show(report.SummaryText, "Tunnel Wizard — apply", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ModelHealthVM.RefreshCommand?.Execute(null);
        };

        RequestTunnelWizardDialog(wizard);
    }

    private void OpenJointWizard()
    {
        if (RequestJointWizardDialog == null)
        {
            MessageBox.Show("Add Joint dialog factory is not wired (MainWindow code-behind missing).",
                "Add Joint", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedTabIndex = 1;
        var wizard = new JointWizardViewModel(GeometryDefinitionVM);
        var result = RequestJointWizardDialog(wizard);
        if (result == JointWizardDialogResult.ManualDraw)
        {
            GeometryDefinitionVM.BeginGeologicalDrawFromWizard(wizard.BuildDraftProperties());
            return;
        }

        if (result != JointWizardDialogResult.Applied)
            return;

        RecordUndoPoint("Add joint");
        var report = new JointWizardApplier(GeometryDefinitionVM).Apply(wizard);
        if (!report.Success)
            MessageBox.Show(report.SummaryText, "Add Joint", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            GeometryDefinitionVM.SetGeometryImportStatusMessage(report.SummaryText);
    }

    private void OpenJointProperties()
    {
        if (RequestDefineJointPropertiesDialog == null)
        {
            MessageBox.Show("Define Joint Properties dialog factory is not wired (MainWindow code-behind missing).",
                "Define Joint Properties", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedTabIndex = 1;
        GeometryDefinitionVM.EnsureDefaultJointPropertySet();
        var editor = new DefineJointPropertiesViewModel(GeometryDefinitionVM.JointPropertySets);
        if (!RequestDefineJointPropertiesDialog(editor))
            return;

        RecordUndoPoint("Define joint properties");
        GeometryDefinitionVM.ReplaceJointPropertySets(editor.WorkingSets);
        GeometryDefinitionVM.SetGeometryImportStatusMessage(
            $"Joint property library: {GeometryDefinitionVM.JointPropertySets.Count} set(s).");
    }

    public ResultsViewModel ResultsVM { get; }
    public FEAnalysisViewModel FEAnalysisVM { get; }
    public ProjectSettingsViewModel ProjectSettingsVM { get; }
    public GeometryDefinitionViewModel GeometryDefinitionVM { get; }
    public MaterialPropertiesViewModel MaterialPropertiesVM { get; }
    public StagedConstructionViewModel StagedConstructionVM { get; private set; } = null!;
    public StagedAnalysisViewModel StagedAnalysisVM { get; private set; } = null!;
    public LoadConditionsViewModel LoadConditionsVM { get; }
    public MeshViewModel MeshVM { get; }
    /// <summary>Wrapper for Mesh tab canvas only: geometry + mesh (mesh not shown on Geometry/Material/Load tabs).</summary>
    public MeshTabCanvasWrapper MeshTabCanvasContext { get; }
    public MeshTabCanvasWrapper BoundaryConditionsCanvasContext { get; }
    public BoundaryConditionsViewModel BoundaryConditionsVM { get; }
    public GroundwaterViewModel GroundwaterVM { get; }
    public SupportViewModel SupportVM { get; }
    public VerificationViewModel VerificationVM { get; }
    public ReportGenerationViewModel ReportGenerationVM { get; }
    public SensitivityAnalysisViewModel SensitivityAnalysisVM { get; }
    public ProbabilisticAnalysisViewModel ProbabilisticAnalysisVM { get; }
    public BackAnalysisViewModel BackAnalysisVM { get; }
    public AnalysisCasesViewModel AnalysisCasesVM { get; }
    public ScriptViewModel ScriptVM { get; }

    public bool TryGetFactorEvaluator(out IFemFactorEvaluator evaluator, out string error)
    {
        var srf = ResultsVM.EstimatedSrfFoS;
        if (srf is not { } fos || !double.IsFinite(fos) || fos <= 0)
        {
            evaluator = null!;
            error = "Run FE Analysis with strength reduction first so Wave 3 tools have a baseline SRF.";
            return false;
        }

        evaluator = new LastSrfScaledEvaluator(fos, MaterialPropertiesVM.Materials, LoadConditionsVM.SeismicLegendKh);
        error = "";
        return true;
    }
    public ModelHealthViewModel ModelHealthVM { get; }
    public AnalysisWorkspaceViewModel Workspace { get; private set; } = null!;
    public UiAppearanceViewModel AppearanceVM { get; private set; } = null!;
    public ProjectWorkspaceManager ProjectWorkspace { get; private set; } = null!;

    public bool UseAnalysisWorkspace
    {
        get => _useAnalysisWorkspace;
        set
        {
            if (_useAnalysisWorkspace == value) return;
            _useAnalysisWorkspace = value;
            UiAppearanceService.Instance.Settings.LayoutMode =
                value ? UiLayoutMode.AnalysisWorkspace : UiLayoutMode.ClassicTabs;
            UiAppearanceService.Instance.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseClassicTabs));
            NotifyWorkspaceChromeChanged();
            AppearanceVM?.NotifyLayoutBindings();
            Workspace?.Explorer.Refresh();
        }
    }

    public bool UseClassicTabs
    {
        get => !UseAnalysisWorkspace;
        set { if (value) UseAnalysisWorkspace = false; }
    }

    /// <summary>FILE ribbon: Backstage dashboard (recent projects, details). Ribbon toolbar stays visible.</summary>
    public bool IsFileWorkspace => UseAnalysisWorkspace && Workspace is { IsFileRibbon: true };

    /// <summary>Workspace chrome for the Geometry ribbon (explorer, points table, properties).</summary>
    public bool IsGeometryWorkspace => UseAnalysisWorkspace && Workspace is { IsGeometryRibbon: true };

    /// <summary>Workspace chrome for the MATERIALS ribbon (explorer, table, inspector).</summary>
    public bool IsMaterialsWorkspace => UseAnalysisWorkspace && Workspace is { IsMaterialsRibbon: true };

    /// <summary>Workspace chrome for the MESH ribbon (explorer, controls, information, quality).</summary>
    public bool IsMeshWorkspace => UseAnalysisWorkspace && Workspace is { IsMeshRibbon: true };

    /// <summary>Workspace chrome for the LOADS ribbon (explorer, properties, table, inspector).</summary>
    public bool IsLoadsWorkspace => UseAnalysisWorkspace && Workspace is { IsLoadsRibbon: true };

    /// <summary>Workspace chrome for the BOUNDARY ribbon (explorer, properties, table).</summary>
    public bool IsBoundaryWorkspace => UseAnalysisWorkspace && Workspace is { IsBoundaryRibbon: true };

    /// <summary>Workspace chrome for the GROUNDWATER ribbon (explorer, properties, table).</summary>
    public bool IsGroundwaterWorkspace => UseAnalysisWorkspace && Workspace is { IsGroundwaterRibbon: true };

    /// <summary>Workspace chrome for the RESULTS ribbon (manager, plot options, messages / min-max).</summary>
    public bool IsResultsWorkspace => UseAnalysisWorkspace && Workspace is { IsResultsRibbon: true };

    /// <summary>Overlay zoom bar on Geometry, Boundary, or Groundwater canvases.</summary>
    public bool ShowWorkspaceCanvasZoomBar =>
        IsGeometryWorkspace || IsBoundaryWorkspace || IsGroundwaterWorkspace;

    /// <summary>Analysis-side panels while FILE, Geometry, Materials, Mesh, Loads, Boundary, and Groundwater ribbons are not selected.</summary>
    public bool IsAnalysisChrome =>
        UseAnalysisWorkspace && !IsFileWorkspace && !IsGeometryWorkspace && !IsMaterialsWorkspace
        && !IsMeshWorkspace && !IsLoadsWorkspace && !IsBoundaryWorkspace && !IsGroundwaterWorkspace
        && !IsResultsWorkspace;

    /// <summary>ANALYSIS ribbon: summary, center tabs, tools/log, and solver dashboard.</summary>
    public bool IsAnalysisRibbonWorkspace =>
        UseAnalysisWorkspace && Workspace is { IsAnalysisRibbon: true };

    /// <summary>RESULTS / VIEW inspector (Results + Check) while ANALYSIS tools are hidden.</summary>
    public bool IsResultsInspectorChrome =>
        IsAnalysisChrome && !IsAnalysisRibbonWorkspace;

    /// <summary>Status strip for FILE dashboard and analysis chrome (not Geometry, Materials, Mesh, Loads, or Boundary).</summary>
    public bool IsFileOrAnalysisChrome =>
        UseAnalysisWorkspace && !IsGeometryWorkspace && !IsMaterialsWorkspace && !IsMeshWorkspace
        && !IsLoadsWorkspace && !IsBoundaryWorkspace && !IsGroundwaterWorkspace;

    /// <summary>Column 2: Analysis Type, Mesh Controls, Load Properties, or Boundary Properties when that panel is on.</summary>
    public bool ShowWorkspaceCenterToolsColumn =>
        (IsAnalysisChrome && Workspace is { IsAnalysisTypeExpanded: true })
        || (IsMeshWorkspace && Workspace.Mesh is { IsControlsExpanded: true })
        || (IsLoadsWorkspace && Workspace.Loads is { IsPropertiesExpanded: true })
        || (IsBoundaryWorkspace && Workspace.Boundary is { IsPropertiesExpanded: true })
        || (IsGroundwaterWorkspace && Workspace.Groundwater is { IsPropertiesExpanded: true });

    public void NotifyWorkspaceChromeChanged()
    {
        OnPropertyChanged(nameof(IsFileWorkspace));
        OnPropertyChanged(nameof(IsGeometryWorkspace));
        OnPropertyChanged(nameof(IsMaterialsWorkspace));
        OnPropertyChanged(nameof(IsMeshWorkspace));
        OnPropertyChanged(nameof(IsLoadsWorkspace));
        OnPropertyChanged(nameof(IsBoundaryWorkspace));
        OnPropertyChanged(nameof(IsGroundwaterWorkspace));
        OnPropertyChanged(nameof(IsResultsWorkspace));
        OnPropertyChanged(nameof(ShowWorkspaceCanvasZoomBar));
        OnPropertyChanged(nameof(IsAnalysisChrome));
        OnPropertyChanged(nameof(IsAnalysisRibbonWorkspace));
        OnPropertyChanged(nameof(IsResultsInspectorChrome));
        OnPropertyChanged(nameof(IsFileOrAnalysisChrome));
        OnPropertyChanged(nameof(ShowWorkspaceCenterToolsColumn));
        OnPropertyChanged(nameof(ShowWorkspaceLeftColumn));
        OnPropertyChanged(nameof(ShowWorkspaceRightColumn));
        OnPropertyChanged(nameof(ShowWorkspaceBottomSplitter));
        OnPropertyChanged(nameof(WorkspaceGeometryTableRow));
        OnPropertyChanged(nameof(WorkspaceLeftColumnWidth));
        OnPropertyChanged(nameof(WorkspaceRightColumnWidth));
        OnPropertyChanged(nameof(WorkspaceGeometryTableRowHeight));
        OnPropertyChanged(nameof(WorkspaceMaterialsTableRowHeight));
        OnPropertyChanged(nameof(WorkspaceMeshTableRowHeight));
        OnPropertyChanged(nameof(WorkspaceLoadsTableRowHeight));
        OnPropertyChanged(nameof(WorkspaceBoundaryTableRowHeight));
        OnPropertyChanged(nameof(WorkspaceGroundwaterTableRowHeight));
        OnPropertyChanged(nameof(WorkspaceResultsTableRowHeight));
        OnPropertyChanged(nameof(WorkspaceBottomChromeRowHeight));
        OnPropertyChanged(nameof(WorkspaceSolverLogRowHeight));
        Workspace?.FileHome.Refresh();
    }

    /// <summary>Points-table / solver-log row: 160 when Geometry ribbon is active, else the analysis log height.</summary>
    public bool WorkspaceGeometryTableRow => IsGeometryWorkspace;

    public const double WorkspaceExpandedLeftWidth = 240;
    public const double WorkspaceExpandedGroundwaterLeftWidth = 400;
    public const double WorkspaceExpandedResultsLeftWidth = 280;
    public const double WorkspaceExpandedResultsTableHeight = 200;
    public const double WorkspaceExpandedRightWidth = 280;
    public const double WorkspaceExpandedTableHeight = 160;
    public const double WorkspaceExpandedLogHeight = 140;
    public const double WorkspaceCollapsedChrome = 32;

    /// <summary>Left explorer column: 0 off workspace or when View hides Explorer, 400 for Groundwater, else 240.</summary>
    public GridLength WorkspaceLeftColumnWidth =>
        !ShowWorkspaceLeftColumn
            ? new GridLength(0)
            : IsGroundwaterWorkspace
                ? new GridLength(WorkspaceExpandedGroundwaterLeftWidth)
                : IsResultsWorkspace
                    ? new GridLength(WorkspaceExpandedResultsLeftWidth)
                    : new GridLength(WorkspaceExpandedLeftWidth);

    /// <summary>Left explorer column and its splitter. Off when View hides Explorer so the canvas is full-width.</summary>
    public bool ShowWorkspaceLeftColumn
    {
        get
        {
            if (!UseAnalysisWorkspace)
                return false;
            if (IsGeometryWorkspace)
                return Workspace.Geometry is { IsExplorerExpanded: true };
            if (IsMaterialsWorkspace)
                return Workspace.Materials is { IsExplorerExpanded: true };
            if (IsMeshWorkspace)
                return Workspace.Mesh is { IsExplorerExpanded: true };
            if (IsLoadsWorkspace)
                return Workspace.Loads is { IsExplorerExpanded: true };
            if (IsBoundaryWorkspace)
                return Workspace.Boundary is { IsExplorerExpanded: true };
            if (IsGroundwaterWorkspace)
                return Workspace.Groundwater is { IsExplorerExpanded: true };
            if (IsResultsWorkspace)
                return Workspace.Results is { IsExplorerExpanded: true };
            if (IsAnalysisChrome)
                return Workspace is { IsExplorerExpanded: true };
            return true;
        }
    }

    /// <summary>Right splitter and column stay off when View hides Properties / Inspector / Results.</summary>
    public bool ShowWorkspaceRightColumn
    {
        get
        {
            if (!UseAnalysisWorkspace || IsMeshWorkspace || IsBoundaryWorkspace || IsGroundwaterWorkspace)
                return false;
            if (IsGeometryWorkspace)
                return Workspace.Geometry is { IsPropertiesExpanded: true };
            if (IsMaterialsWorkspace)
                return Workspace.Materials is { IsInspectorExpanded: true };
            if (IsLoadsWorkspace)
                return Workspace.Loads is { IsInspectorExpanded: true };
            if (IsResultsWorkspace)
                return Workspace.Results is { IsPropertiesExpanded: true };
            if (IsAnalysisChrome)
                return Workspace is { IsResultsInspectorExpanded: true };
            return true;
        }
    }

    /// <summary>Bottom table / log splitter. Hidden when both the table and solver log are off.</summary>
    public bool ShowWorkspaceBottomSplitter =>
        WorkspaceBottomChromeRowHeight.Value > 0 || WorkspaceSolverLogRowHeight.Value > 0;

    /// <summary>Right results/properties column: 0 off workspace or MATERIALS, 32 when Geometry properties are collapsed, else 280.</summary>
    public GridLength WorkspaceRightColumnWidth
    {
        get
        {
            if (!UseAnalysisWorkspace || IsMeshWorkspace || IsBoundaryWorkspace || IsGroundwaterWorkspace)
                return new GridLength(0);
            if (IsMaterialsWorkspace)
                return Workspace.Materials is { IsInspectorExpanded: true }
                    ? new GridLength(WorkspaceExpandedRightWidth)
                    : new GridLength(0);
            if (IsGeometryWorkspace && Workspace.Geometry is { IsPropertiesExpanded: false })
                return new GridLength(0);
            if (IsLoadsWorkspace && Workspace.Loads is { IsInspectorExpanded: false })
                return new GridLength(0);
            if (IsResultsWorkspace)
                return Workspace.Results is { IsPropertiesExpanded: true }
                    ? new GridLength(WorkspaceExpandedRightWidth)
                    : new GridLength(0);
            if (IsAnalysisChrome && Workspace is { IsResultsInspectorExpanded: false })
                return new GridLength(0);
            return new GridLength(WorkspaceExpandedRightWidth);
        }
    }

    /// <summary>Points-table row: 0 off Geometry or when View hides Table, else 160.</summary>
    public GridLength WorkspaceGeometryTableRowHeight
    {
        get
        {
            if (!IsGeometryWorkspace || Workspace.Geometry is { IsPointsTableExpanded: false })
                return new GridLength(0);
            return new GridLength(WorkspaceExpandedTableHeight);
        }
    }

    /// <summary>Materials library table row: 160 when the table toggle is on, else 0.</summary>
    public GridLength WorkspaceMaterialsTableRowHeight =>
        IsMaterialsWorkspace && Workspace.Materials is { IsTableExpanded: true }
            ? new GridLength(WorkspaceExpandedTableHeight)
            : new GridLength(0);

    public const double WorkspaceExpandedDashboardHeight = 210;

    /// <summary>Solver log / ANALYSIS dashboard row: 210 on ANALYSIS, 140 on RESULTS/VIEW, else 0.</summary>
    public GridLength WorkspaceSolverLogRowHeight =>
        !UseAnalysisWorkspace || Workspace is not { IsSolverLogExpanded: true }
            ? new GridLength(0)
            : IsAnalysisRibbonWorkspace
                ? new GridLength(WorkspaceExpandedDashboardHeight)
                : IsAnalysisChrome
                    ? new GridLength(WorkspaceExpandedLogHeight)
                    : new GridLength(0);

    /// <summary>Mesh information table row: 160 when the Display toggle is on, else 0.</summary>
    public GridLength WorkspaceMeshTableRowHeight =>
        IsMeshWorkspace && Workspace.Mesh is { IsTableExpanded: true }
            ? new GridLength(WorkspaceExpandedTableHeight)
            : new GridLength(0);

    /// <summary>Load table row: 160 when the Loads table toggle is on, else 0.</summary>
    public GridLength WorkspaceLoadsTableRowHeight =>
        IsLoadsWorkspace && Workspace.Loads is { IsTableExpanded: true }
            ? new GridLength(WorkspaceExpandedTableHeight)
            : new GridLength(0);

    /// <summary>Boundary-condition table row: 160 when the Boundary table toggle is on, else 0.</summary>
    public GridLength WorkspaceBoundaryTableRowHeight =>
        IsBoundaryWorkspace && Workspace.Boundary is { IsTableExpanded: true }
            ? new GridLength(WorkspaceExpandedTableHeight)
            : new GridLength(0);

    /// <summary>Groundwater table row: 160 when the Groundwater table toggle is on, else 0.</summary>
    public GridLength WorkspaceGroundwaterTableRowHeight =>
        IsGroundwaterWorkspace && Workspace.Groundwater is { IsTableExpanded: true }
            ? new GridLength(WorkspaceExpandedTableHeight)
            : new GridLength(0);

    /// <summary>RESULTS messages / min-max row: 200 when the table toggle is on, else 0.</summary>
    public GridLength WorkspaceResultsTableRowHeight =>
        IsResultsWorkspace && Workspace.Results is { IsTableExpanded: true }
            ? new GridLength(WorkspaceExpandedResultsTableHeight)
            : new GridLength(0);

    /// <summary>Shared center bottom chrome: Geometry, Materials, Mesh, Loads, Boundary, Groundwater, or Results table.</summary>
    public GridLength WorkspaceBottomChromeRowHeight =>
        IsGeometryWorkspace ? WorkspaceGeometryTableRowHeight
        : IsMaterialsWorkspace ? WorkspaceMaterialsTableRowHeight
        : IsMeshWorkspace ? WorkspaceMeshTableRowHeight
        : IsLoadsWorkspace ? WorkspaceLoadsTableRowHeight
        : IsBoundaryWorkspace ? WorkspaceBoundaryTableRowHeight
        : IsResultsWorkspace ? WorkspaceResultsTableRowHeight
        : WorkspaceGroundwaterTableRowHeight;

    public void ShowCapabilityMessage(string message)
    {
        if (Workspace != null)
            Workspace.ShowCapabilityMessage(message);
        else
            MessageBox.Show(message, "Analysis Workspace", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    public ProjectHistoryService History => _history;

    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;
    public string UndoMenuText => CanUndo ? $"_Undo {History.UndoDescription}" : "_Undo";
    public string RedoMenuText => CanRedo ? $"_Redo {History.RedoDescription}" : "_Redo";

    /// <inheritdoc />
    public void RecordUndoPoint(string description) => History.RecordSnapshot(this, description);

    void IProjectUndoHost.Undo() => OnUndo();
    void IProjectUndoHost.Redo() => OnRedo();

    /// <summary>Refresh canvases and advisory panels after undo/redo restore.</summary>
    public void RefreshAllCanvasesAfterModelRestore()
    {
        GeometryDefinitionVM.RefreshGeometryPath();
        StagedConstructionVM.RefreshExcavationDisplay();
        StagedConstructionVM.NotifyExcavatedElementIdsChanged();
        FEAnalysisVM.RefreshRunPseudoStaticDisplay();
        ModelHealthVM.RefreshCommand?.Execute(null);
    }

    private void WireUndoHosts()
    {
        GeometryDefinitionVM.SetUndoHost(this);
        MaterialPropertiesVM.SetUndoHost(this);
        BoundaryConditionsVM.SetUndoHost(this);
        LoadConditionsVM.SetUndoHost(this);
        MeshVM.SetUndoHost(this);
        GroundwaterVM.SetUndoHost(this);
        StagedConstructionVM.SetUndoHost(this);
        FEAnalysisVM.SetUndoHost(this);
    }

    private void OnUndo()
    {
        var previousCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            History.Undo(this);
        }
        finally
        {
            Mouse.OverrideCursor = previousCursor;
        }
    }

    private void OnRedo()
    {
        var previousCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            History.Redo(this);
        }
        finally
        {
            Mouse.OverrideCursor = previousCursor;
        }
    }

    private void OnProjectHistoryChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoMenuText));
        OnPropertyChanged(nameof(RedoMenuText));
        if (_undoCommand is RelayCommand undo)
            undo.RaiseCanExecuteChanged();
        if (_redoCommand is RelayCommand redo)
            redo.RaiseCanExecuteChanged();
        StagedConstructionVM.NotifyUndoCommandsChanged();
    }

    /// <summary>Stage footer row visible when staged analysis is on and at least one stage exists.</summary>
    public bool ShowStageFooterRow =>
        FEAnalysisVM.StagedExcavationEnabled && ProjectSettingsVM.NumberOfStages > 0;

    /// <summary>Path of the last opened or saved .xfem project (File → Save uses this when set).</summary>
    public string? CurrentXfemProjectPath
    {
        get => _currentXfemPath;
        set { _currentXfemPath = value; OnPropertyChanged(); }
    }

    /// <summary>Persisted MRU list for File → Open Recent.</summary>
    public RecentProjectsStore RecentProjects { get; }

    private void AddToRecentFiles(string path) => RecentProjects.Add(path);

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex == value) return;
            // ComboBox/DataGrid SelectionChanged can bubble into MainTabs and set
            // SelectedIndex to 2 (Materials). Keep MESH/GEOMETRY/LOADS/BOUNDARY on their
            // ribbon unless the user actually chose MATERIALS.
            if (UseAnalysisWorkspace && Workspace != null
                && value == MaterialPropertiesMainTabIndex
                && !RibbonAllowsMaterialsTab(Workspace.RibbonTab))
            {
                OnPropertyChanged();
                return;
            }
            _selectedTabIndex = value;
            OnPropertyChanged();
            ApplyGeometryCanvasDecorationsForTab(value);
            OnPropertyChanged(nameof(IsGeometryTabActive));
            OnPropertyChanged(nameof(IsMeshTabActive));
            OnPropertyChanged(nameof(IsLoadConditionsTabActive));
            OnPropertyChanged(nameof(IsBoundaryConditionsTabActive));
            OnPropertyChanged(nameof(IsFeAnalysisTabActive));
            OnPropertyChanged(nameof(IsResultsVisualizationTabActive));
            OnPropertyChanged(nameof(ShowDisplayAnalysisToolbarGroup));
            OnPropertyChanged(nameof(IsProjectSettingsTabActive));
            OnPropertyChanged(nameof(IsMaterialPropertiesTabActive));
            OnPropertyChanged(nameof(ShowSettingsShortcutToolbarGroup));
            if (UseAnalysisWorkspace && value == GeometryDefinitionMainTabIndex)
                Workspace.RibbonTab = "GEOMETRY";
            if (UseAnalysisWorkspace && value == MaterialPropertiesMainTabIndex)
                Workspace.RibbonTab = "MATERIALS";
            if (UseAnalysisWorkspace && value == MeshMainTabIndex)
                Workspace.RibbonTab = "MESH";
            if (UseAnalysisWorkspace && value == LoadConditionsMainTabIndex)
                Workspace.RibbonTab = "LOADS";
            if (UseAnalysisWorkspace && value == BoundaryConditionsMainTabIndex)
                Workspace.RibbonTab = "BOUNDARY";
            if (UseAnalysisWorkspace && value == GroundwaterMainTabIndex)
                Workspace.RibbonTab = "GROUNDWATER";
        }
    }

    /// <summary>
    /// Only the MATERIALS ribbon (or File/View chrome) may select the Material Properties tab.
    /// Nested ComboBox events must not steal MESH into that tab.
    /// </summary>
    internal static bool RibbonAllowsMaterialsTab(string ribbonTab) =>
        ribbonTab is "MATERIALS" or "FILE" or "VIEW";

    public bool IsProjectSettingsTabActive => SelectedTabIndex == 0;
    public bool IsMaterialPropertiesTabActive => SelectedTabIndex == MaterialPropertiesMainTabIndex;
    public bool ShowSettingsShortcutToolbarGroup => IsProjectSettingsTabActive || IsMaterialPropertiesTabActive;
    public bool IsGeometryTabActive => SelectedTabIndex == GeometryDefinitionMainTabIndex;
    public bool IsMeshTabActive => SelectedTabIndex == MeshMainTabIndex;
    public bool IsLoadConditionsTabActive => SelectedTabIndex == LoadConditionsMainTabIndex;
    public bool IsBoundaryConditionsTabActive => SelectedTabIndex == BoundaryConditionsMainTabIndex;
    public bool IsFeAnalysisTabActive => SelectedTabIndex == FeAnalysisMainTabIndex;
    public bool IsResultsVisualizationTabActive => SelectedTabIndex == ResultsVisualizationMainTabIndex;
    public bool ShowDisplayAnalysisToolbarGroup => IsFeAnalysisTabActive || IsResultsVisualizationTabActive;

    /// <summary>Tab-specific slope canvas overlays (internal edges, micro boundary, GW phreatic).</summary>
    public void ApplyGeometryCanvasDecorationsForTab(int tabIndex)
    {
        GeometryDefinitionVM.UseLayerNamesOnMaterialCanvas =
            tabIndex == MaterialPropertiesMainTabIndex
            || tabIndex == MeshMainTabIndex
            || tabIndex == LoadConditionsMainTabIndex;
        GeometryDefinitionVM.ShowGroundwaterZoneOverlaysOnCanvas =
            tabIndex == GroundwaterMainTabIndex;
        GeometryDefinitionVM.ShowBoundaryMicroOutlineOnCanvas =
            tabIndex == MeshMainTabIndex;
        var geometryVo = Workspace?.Geometry;
        var materialsVo = Workspace?.Materials;
        var loadsVo = Workspace?.Loads;
        var onGeometry = tabIndex == GeometryDefinitionMainTabIndex;
        var onMaterials = tabIndex == MaterialPropertiesMainTabIndex;
        var loadish = tabIndex == LoadConditionsMainTabIndex
            || tabIndex == BoundaryConditionsMainTabIndex
            || tabIndex == GroundwaterMainTabIndex;

        GeometryDefinitionVM.ShowInternalAndLayerCutLinesOnCanvas =
            onMaterials && (materialsVo?.ShowInternalAndLayerCutLines ?? true);
        GeometryDefinitionVM.ShowGeometryEdgeLinesOnCanvas =
            (onGeometry && (geometryVo?.ShowLines ?? true))
            || (tabIndex == FeAnalysisMainTabIndex && (Workspace?.ShowGeometryLines ?? true));
        // Phreatic WT line whenever GW is enabled (any tab).
        // In-soil saturated wash + ponded depth field stay Groundwater-tab-only.
        GeometryDefinitionVM.ShowGroundwaterPhreaticOnCanvas =
            GeometryDefinitionVM.IsGroundwaterEnabled;
        GeometryDefinitionVM.ShowLineLoadOverlaysOnCanvas =
            loadish && (loadsVo?.ShowLineLoads ?? true);
        GeometryDefinitionVM.ShowPointLoadOverlaysOnCanvas =
            loadish && (loadsVo?.ShowPointLoads ?? true);
        GeometryDefinitionVM.ShowGeometryPointsOnCanvas =
            !onGeometry || (geometryVo?.ShowPoints ?? true);
        GeometryDefinitionVM.ShowGeometrySurfaceFillsOnCanvas =
            !onGeometry || (geometryVo?.ShowSurfaces ?? true) || (geometryVo?.ShowRegions ?? true);
        GeometryDefinitionVM.ShowGeometryLabelsOnCanvas =
            !onGeometry || (geometryVo?.ShowLabels ?? true);
        GeometryDefinitionVM.ShowMaterialLayerLabelsOnCanvas =
            !onMaterials || (materialsVo?.ShowLayerLabels ?? true);
    }

    public int? CurrentStage
    {
        get => _currentStage;
        set
        {
            if (_currentStage == value) return;
            _currentStage = value;
            OnPropertyChanged();
            ScheduleSyncActiveStageFromFooter(value);
        }
    }

    private void ScheduleSyncActiveStageFromFooter(int? footerStageNumber)
    {
        _pendingFooterStageNumber = footerStageNumber;
        _stageFooterDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _stageFooterDebounceTimer.Tick -= OnStageFooterDebounceTick;
        _stageFooterDebounceTimer.Tick += OnStageFooterDebounceTick;
        _stageFooterDebounceTimer.Stop();
        _stageFooterDebounceTimer.Start();
    }

    private void OnStageFooterDebounceTick(object? sender, EventArgs e)
    {
        _stageFooterDebounceTimer?.Stop();
        StagedConstructionVM?.SyncActiveStageFromFooter(_pendingFooterStageNumber);
        ResultsVM.SyncVisualizationToFooterStage(_pendingFooterStageNumber);
    }

    private void SelectFooterStageFromResults(int stageNumber)
    {
        if (stageNumber <= 0)
            return;

        SyncFooterWithStagedAnalysisGrid();
        var tabs = ProjectSettingsVM.StageFooterTabs;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].StageNumber != stageNumber)
                continue;

            ProjectSettingsVM.SelectedStageFooterTabIndex = i;
            CurrentStage = stageNumber;
            return;
        }
    }

    public bool ZoomPanModeEnabled
    {
        get => _zoomPanModeEnabled;
        set
        {
            if (_zoomPanModeEnabled == value) return;
            _zoomPanModeEnabled = value;
            if (value && _zoomAreaModeEnabled)
            {
                _zoomAreaModeEnabled = false;
                OnPropertyChanged(nameof(ZoomAreaModeEnabled));
            }
            OnPropertyChanged();
        }
    }

    /// <summary>When true, canvas left-drag zooms to a drawn rectangle (toolbar Zoom to Area).</summary>
    public bool ZoomAreaModeEnabled
    {
        get => _zoomAreaModeEnabled;
        set
        {
            if (_zoomAreaModeEnabled == value) return;
            _zoomAreaModeEnabled = value;
            if (value && _zoomPanModeEnabled)
            {
                _zoomPanModeEnabled = false;
                OnPropertyChanged(nameof(ZoomPanModeEnabled));
            }
            OnPropertyChanged();
        }
    }

    /// <summary>Set by <see cref="Views.MainWindow"/> to zoom all canvases from keyboard/toolbar.</summary>
    public Action? ZoomInCanvases { get; set; }
    public Action? ZoomOutCanvases { get; set; }
    public Action? ZoomToFitCanvases { get; set; }
    public Action? ResetZoomCanvases { get; set; }
    public Action<MeshToolbarPopupKind>? RequestOpenMeshToolbarPopup { get; set; }

    public enum MeshToolbarPopupKind
    {
        Information,
        Unstructured,
        Structured
    }

    public bool AreMainTabsEnabled
    {
        get => _areMainTabsEnabled;
        set { _areMainTabsEnabled = value; OnPropertyChanged(); }
    }

    public ICommand NewProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand SaveCopyCommand { get; }
    public ICommand CloseProjectCommand { get; }

    public ICommand ImportGeometryFileCommand { get; }
    public ICommand ImportSlopeImageCommand { get; }
    public ICommand ImportMeshFileCommand { get; }
    public ICommand ImportMaterialDatabaseCommand { get; }
    public ICommand ImportBoundaryConditionsCommand { get; }

    public ICommand ExportGeometryCommand { get; }
    public ICommand ExportMeshCommand { get; }
    public ICommand ExportResultsCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand ExportToCsvCommand { get; }
    public ICommand ExportToAbaqusCommand { get; }
    public ICommand ExportToAnsysCommand { get; }

    public ICommand PrintCommand { get; }
    public ICommand PrintPreviewCommand { get; }
    public ICommand PageSetupCommand { get; }
    public Action? RequestPrint { get; set; }
    public Action? RequestPrintPreview { get; set; }
    public Action? RequestPageSetup { get; set; }
    public ICommand OpenRecentProjectCommand { get; }
    public ICommand OpenRecentByIndexCommand { get; }
    public ICommand ClearRecentFilesCommand { get; }

    public ICommand ExitApplicationCommand { get; } = new RelayCommand(() => Application.Current.Shutdown());
    public ICommand UndoCommand => _undoCommand;
    public ICommand RedoCommand => _redoCommand;

    // ─── View ───
    public ICommand TogglePanModeCommand { get; }
    public ICommand ExitPanModeCommand { get; }
    public ICommand ToggleZoomAreaModeCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomToFitCommand { get; }
    public ICommand ResetZoomCommand { get; }
    public ICommand ToggleNodeNumbersCommand { get; }
    public ICommand ToggleElementNumbersCommand { get; }
    public ICommand MeshInformationPopupCommand { get; }
    public ICommand UnstructuredMeshPopupCommand { get; }
    public ICommand StructuredMeshPopupCommand { get; }

    // ─── Edit ───
    public ICommand CutCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand DeleteSelectionCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }
    public ICommand PreferencesCommand { get; }
    public ICommand SetClassicLayoutCommand { get; }
    public ICommand SetWorkspaceLayoutCommand { get; }
    public ICommand WorkspaceF7Command { get; }
    public Action? RequestPreferencesDialog { get; set; }

    // ─── Geometry ───
    public ICommand NavigateToGeometryCommand { get; }
    public ICommand MenuAddVertexCommand { get; }
    public ICommand MenuAddLineSegmentCommand { get; }
    public ICommand MenuAddArcCommand { get; }
    public ICommand MenuAddCircleCommand { get; }
    public ICommand GeometryClosedPolygonCommand { get; }
    public ICommand GeometryOpenPolygonCommand { get; }
    public ICommand MenuAddSurfaceCommand { get; }
    public ICommand GeometryBooleanUnionCommand { get; }
    public ICommand GeometryBooleanIntersectCommand { get; }
    public ICommand GeometryBooleanSubtractCommand { get; }
    public ICommand VerifyGeometryCommand { get; }
    public ICommand SnapSettingsCommand { get; }
    public ICommand GridSettingsCommand { get; }
    public ICommand ProjectSettingsGeneralCommand { get; }
    public ICommand ProjectSettingsAnalysisTypeCommand { get; }
    public ICommand OpenAnalysisTypeWizardCommand { get; }
    public Action? RequestAnalysisTypeWizardDialog { get; set; }
    public Action<ProjectSettingsTabKind>? RequestProjectSettingsDialog { get; set; }
    /// <summary>Opens the Tunnel Wizard dialog (six-step parametric model setup).</summary>
    public ICommand OpenTunnelWizardCommand { get; }
    public ICommand OpenJointWizardCommand { get; }
    public ICommand OpenJointPropertiesCommand { get; }
    /// <summary>
    /// Set by MainWindow code-behind: factory that constructs and shows the modal dialog.
    /// Receives the wizard view-model, returns true if the user clicked Apply (and the apply
    /// orchestration succeeded).
    /// </summary>
    public Func<TunnelWizardViewModel, bool>? RequestTunnelWizardDialog { get; set; }
    public Func<JointWizardViewModel, JointWizardDialogResult>? RequestJointWizardDialog { get; set; }
    public Func<DefineJointPropertiesViewModel, bool>? RequestDefineJointPropertiesDialog { get; set; }
    public Action<int>? RequestOpenMaterialPopupSubtab { get; set; }
    public Action<LoadToolbarPopupKind>? RequestOpenLoadToolbarPopup { get; set; }
    /// <summary>Raised by the Boundary Conditions toolbar "Import Boundaries" button to open the
    /// Apply BC to Outer Boundary + DataGrid popup. Wired in MainWindow code-behind.</summary>
    public Action? RequestOpenBcImportBoundariesPopup { get; set; }

    public enum LoadToolbarPopupKind
    {
        FieldStress,
        SeismicLoad,
        Loads
    }

    // ─── Material Properties ───
    public ICommand NavigateToMaterialsCommand { get; }
    public ICommand AddMaterialCommand { get; }
    public ICommand EditMaterialCommand { get; }
    public ICommand DeleteMaterialCommand { get; }
    public ICommand DuplicateMaterialCommand { get; }
    public ICommand AssignMaterialToRegionCommand { get; }
    public ICommand ClearMaterialAssignmentsCommand { get; }
    public ICommand MaterialLibraryCommand { get; }
    public ICommand ExportMaterialsCommand { get; }
    public ICommand MaterialLayeringControlsCommand { get; }
    public ICommand MaterialProjectMaterialsCommand { get; }
    public ICommand MaterialLayerMaterialAssignmentCommand { get; }

    // ─── Loading ───
    public ICommand NavigateToLoadConditionsCommand { get; }
    public ICommand MenuAddLoadCommand { get; }
    public ICommand LoadFieldStressPopupCommand { get; }
    public ICommand BcImportBoundariesPopupCommand { get; }
    public ICommand LoadSeismicLoadPopupCommand { get; }
    public ICommand LoadLoadsPopupCommand { get; }
    public ICommand DeleteLoadCommand { get; }
    public ICommand ClearAllLoadsCommand { get; }
    public ICommand AddPointLoadCommand { get; }
    public ICommand AddLineLoadCommand { get; }
    public ICommand AddPressureLoadCommand { get; }
    public ICommand AddBodyForceCommand { get; }
    public ICommand AddGravityLoadCommand { get; }
    public ICommand AddSeismicLoadCommand { get; }
    public ICommand LoadCombinationsCommand { get; }
    public ICommand LoadCasesCommand { get; }
    public ICommand ImportLoadsCommand { get; }
    public ICommand ExportLoadsCommand { get; }

    // ─── Analysis ───
    public ICommand NavigateToFEAnalysisCommand { get; }
    public ICommand RunFEAnalysisMenuCommand { get; }
    public ICommand StopAnalysisCommand { get; }
    public ICommand AnalysisSettingsCommand { get; }
    public ICommand SolverOptionsCommand { get; }
    public ICommand ConvergenceCriteriaCommand { get; }
    public ICommand NavigateToMeshCommand { get; }
    public ICommand NavigateToBoundaryConditionsCommand { get; }
    public ICommand NavigateToGroundwaterCommand { get; }
    public ICommand NavigateToSupportCommand { get; }
    public ICommand NavigateToFieldStationsCommand { get; }
    public ICommand NavigateToSensitivityCommand { get; }
    public ICommand NavigateToVerificationCommand { get; }
    public ICommand AnalysisHistoryCommand { get; }
    public ICommand ValidationCheckCommand { get; }

    // ─── Results ───
    public ICommand NavigateToResultsCommand { get; }
    public ICommand ContourPlotStressCommand { get; }
    public ICommand ContourPlotStrainCommand { get; }
    public ICommand ContourPlotDisplacementCommand { get; }
    public ICommand ContourPlotPorePressureCommand { get; }
    public ICommand ContourPlotYieldZonesCommand { get; }
    public ICommand VectorPlotDisplacementCommand { get; }
    public ICommand VectorPlotFluxCommand { get; }
    public ICommand DeformedShapeCommand { get; }
    public ICommand QueryNodeCommand { get; }
    public ICommand QueryElementCommand { get; }
    public ICommand GenerateReportMenuCommand { get; }
    public ICommand CopySummaryCommand { get; }
    public ICommand AnimateResultsCommand { get; }

    // ─── Window ───
    public ICommand NavigateToTabCommand { get; }   // CommandParameter = tab index (string or int)

    // ─── Help ───
    public ICommand ViewHelpCommand { get; }
    public ICommand GettingStartedCommand { get; }
    public ICommand TutorialsCommand { get; }
    public ICommand TheoryManualCommand { get; }
    public ICommand UserManualCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand LicenseInfoCommand { get; }
    public ICommand AboutCommand { get; }

    private void OnCloseProject()
    {
        if (!TryPromptSaveBeforeDiscard("closing the project"))
            return;

        ProjectWorkspace.CloseActiveWorkspace(this);
        RefreshSavedBaseline();
        SelectedTabIndex = 0;
    }

    private void OnDeleteSelection()
    {
        switch (SelectedTabIndex)
        {
            case GeometryDefinitionMainTabIndex:
            {
                if (Workspace.Geometry.TryDeleteSelection())
                    return;
                var g = GeometryDefinitionVM;
                if (g.IsBulkPointDeleteMode)
                    g.CancelBulkPointDeleteModeCommand.Execute(null);
                else
                    g.StartBulkPointDeleteModeCommand.Execute(null);
                return;
            }
            case MaterialPropertiesMainTabIndex:
                if (MaterialPropertiesVM.SelectedMaterialModel != null)
                    MaterialPropertiesVM.RemoveMaterialCommand.Execute(null);
                return;
            case LoadConditionsMainTabIndex:
                if (LoadConditionsVM.SelectedLoad != null)
                    LoadConditionsVM.RemoveLoadCommand.Execute(LoadConditionsVM.SelectedLoad);
                return;
            case 5: // Boundary conditions
                if (BoundaryConditionsVM.SelectedBoundaryCondition != null)
                    BoundaryConditionsVM.DeleteBoundaryConditionCommand.Execute(null);
                return;
            default:
                MessageBox.Show("Nothing selected to delete on this tab.", "Delete Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }
    }

    private void OnSaveCopy()
    {
        var dlg = new SaveFileDialog
        {
            Filter = XfemFilter,
            DefaultExt = XfemProjectPersistence.FileExtension.TrimStart('.'),
            Title = "Save a Copy",
            FileName = string.IsNullOrEmpty(CurrentXfemProjectPath)
                ? "project_copy" + XfemProjectPersistence.FileExtension
                : Path.GetFileNameWithoutExtension(CurrentXfemProjectPath) + "_copy" + XfemProjectPersistence.FileExtension
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var path = dlg.FileName;
            if (!path.EndsWith(XfemProjectPersistence.FileExtension, StringComparison.OrdinalIgnoreCase))
                path += XfemProjectPersistence.FileExtension;
            XfemProjectPersistence.Save(this, path);
            MessageBox.Show($"Copy saved to:\n{path}", "Save a Copy", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save copy:\n{ex.Message}", "Save a Copy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenRecentByIndex(object? parameter)
    {
        var index = parameter switch
        {
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0
        };
        if (index < 1) return;

        var path = RecentProjects.GetPathAt(index);
        if (!string.IsNullOrWhiteSpace(path))
            OnOpenRecentProject(path);
    }

    private void OnOpenRecentProject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!TryPromptSaveBeforeDiscard("opening another project"))
            return;

        if (!File.Exists(path))
        {
            var remove = MessageBox.Show($"File not found:\n{path}\n\nRemove from recent files?",
                "File Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (remove == MessageBoxResult.Yes) RecentProjects.Remove(path);
            return;
        }
        try
        {
            XfemProjectPersistence.Load(this, path);
            CurrentXfemProjectPath = path;
            ProjectWorkspace.UpdateActiveSlotFromVm(this, path);
            AddToRecentFiles(path);
            RefreshStageFooterAfterProjectLoad();
            History.Clear();
            RefreshSavedBaseline();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open project:\n{ex.Message}", "Open Recent", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void OnImportMesh()
        => MessageBox.Show("Import Mesh from external file (.msh/.inp) is not yet implemented.", "Import Mesh", MessageBoxButton.OK, MessageBoxImage.Information);

    private static void OnImportBoundaryConditions()
        => MessageBox.Show("Import Boundary Conditions from CSV is not yet implemented.", "Import Boundary Conditions", MessageBoxButton.OK, MessageBoxImage.Information);

    private static void OnExportMesh()
        => MessageBox.Show("Export Mesh to .msh / .inp is not yet implemented.", "Export Mesh", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnExportResults()
    {
        if (!TryGetExportMesh(out var nodes, out var elements, out var error))
        {
            MessageBox.Show(error, "Export Results", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|VTK files (*.vtk)|*.vtk",
            FileName = "fe_results.csv",
        };
        if (dlg.ShowDialog() != true) return;

        var stresses = ResultsVM.FeElementStresses;
        if (dlg.FileName.EndsWith(".vtk", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(dlg.FileName, FeResultsExportService.BuildVtk(nodes, elements, stresses));
        }
        else
        {
            var nodal = FeResultsExportService.BuildNodalCsv(nodes);
            var elem = FeResultsExportService.BuildElementCsv(elements, stresses);
            File.WriteAllText(dlg.FileName, nodal + Environment.NewLine + elem);
        }
        MessageBox.Show($"Wrote {dlg.FileName}", "Export Results", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExportReport()
        => ReportGenerationVM.ExportPdfCommand.Execute(null);

    private void OnExportToAbaqus()
    {
        if (!TryGetExportMesh(out var nodes, out var elements, out var error))
        {
            MessageBox.Show(error, "Export to Abaqus", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog { Filter = "Abaqus input (*.inp)|*.inp", FileName = "model.inp" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, FeAbaqusExporter.Build(nodes, elements, MaterialPropertiesVM.Materials.ToList()));
        MessageBox.Show($"Wrote {dlg.FileName}", "Export to Abaqus", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExportToAnsys()
    {
        if (!TryGetExportMesh(out var nodes, out var elements, out var error))
        {
            MessageBox.Show(error, "Export to ANSYS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog { Filter = "ANSYS CDB (*.cdb)|*.cdb", FileName = "model.cdb" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, FeAnsysExporter.Build(nodes, elements));
        MessageBox.Show($"Wrote {dlg.FileName}", "Export to ANSYS", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool TryGetExportMesh(out IReadOnlyList<Node> nodes, out IReadOnlyList<ElementBase> elements, out string error)
    {
        if (FeMeshExportAdapter.TryFromResults(ResultsVM.FENodes, ResultsVM.FEElements, out nodes, out elements)
            || FeMeshExportAdapter.TryFromMesh(MeshVM.DiscretizationNodes, MeshVM.MeshInformationRows, out nodes, out elements))
        {
            error = "";
            return true;
        }

        error = "Generate a mesh or run FE Analysis before exporting.";
        return false;
    }

    private void OpenProjectSettings(ProjectSettingsTabKind kind)
    {
        ProjectSettingsVM.RefreshStagedAnalysisBindings();
        ProjectSettingsVM.SelectSettingsTab(kind);
        if (UseAnalysisWorkspace)
            RequestProjectSettingsDialog?.Invoke(kind);
        else
            SelectedTabIndex = 0;
    }

    private void ShowSnapSettings()
    {
        var dialog = new SnapSettingsDialog(GeometryDefinitionVM.SnapSettings)
        {
            Owner = Application.Current?.MainWindow,
        };
        dialog.ShowDialog();
    }

    private void ShowVerifyGeometry()
    {
        var issues = LemGeometryVerifier.Verify(GeometryDefinitionVM);
        var text = LemGeometryVerifier.Format(issues);
        var icon = LemGeometryVerifier.HasErrors(issues)
            ? MessageBoxImage.Warning
            : MessageBoxImage.Information;
        MessageBox.Show(text, "Verify Geometry", MessageBoxButton.OK, icon);
    }

    private void OnViewHelp()
    {
        var owner = Application.Current?.MainWindow;
        HelpWindow.ShowContextHelp(owner, SelectedTabIndex);
    }

    private static void OnUserManual()
    {
        var owner = Application.Current?.MainWindow;
        HelpWindow.ShowManual(owner);
    }

    private static void OnTutorials()
    {
        var owner = Application.Current?.MainWindow;
        HelpWindow.ShowTopic(owner, "tutorials");
    }

    private static void OnAbout()
        => MessageBox.Show(
            "X2D FEM\nVersion 1.0\n\n" +
            "2D Finite Element Analysis for Geotechnical Engineering.\n\n" +
            "Supports plane-strain, axisymmetric, and 1D problems.\n" +
            "Mesh generation, boundary conditions, FE analysis, and results visualization.",
            "About X2D FEM", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnProjectSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettingsViewModel.SelectedDimension))
            ResultsVM.ProjectDimension = ProjectSettingsVM.SelectedDimension;
        if (e.PropertyName == nameof(ProjectSettingsViewModel.NumberOfStages))
            OnPropertyChanged(nameof(ShowStageFooterRow));
        if (e.PropertyName == nameof(ProjectSettingsViewModel.SelectedStageFooterTabIndex))
        {
            if (!FEAnalysisVM.StagedExcavationEnabled)
            {
                CurrentStage = null;
                return;
            }
            var ps = ProjectSettingsVM;
            if (ps.SelectedStageFooterTabIndex is int idx && idx >= 0 && idx < ps.StageFooterTabs.Count)
                CurrentStage = ps.StageFooterTabs[idx].StageNumber;
            else
                CurrentStage = null;
        }
    }

    private static string XfemFilter => $"XFEM project (*{XfemProjectPersistence.FileExtension})|*{XfemProjectPersistence.FileExtension}|All files|*.*";

    private void OnNewProject()
    {
        if (!TryPromptSaveBeforeDiscard("creating a new project"))
            return;

        ProjectWorkspace.AddBlankWorkspace(this);
        History.Clear();
        RefreshSavedBaseline();
        SelectedTabIndex = 0;
    }

    private void OnOpenProject()
    {
        if (!TryPromptSaveBeforeDiscard("opening another project"))
            return;

        var dlg = new OpenFileDialog
        {
            Filter = XfemFilter,
            DefaultExt = XfemProjectPersistence.FileExtension.TrimStart('.')
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            XfemProjectPersistence.Load(this, dlg.FileName);
            CurrentXfemProjectPath = dlg.FileName;
            ProjectWorkspace.UpdateActiveSlotFromVm(this, dlg.FileName);
            AddToRecentFiles(dlg.FileName);
            RefreshStageFooterAfterProjectLoad();
            History.Clear();
            RefreshSavedBaseline();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open project:\n{ex.Message}", "Open", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSave()
    {
        TrySaveCurrentProject();
    }

    private void OnSaveAs()
    {
        TrySaveAsCurrentProject();
    }

    private void RefreshSavedBaseline()
    {
        try
        {
            _savedSnapshotJson = XfemProjectPersistence.SerializeSnapshotForDirtyCheck(this);
        }
        catch
        {
            _savedSnapshotJson = "";
        }
    }

    private bool HasUnsavedChanges()
    {
        try
        {
            return _savedSnapshotJson != XfemProjectPersistence.SerializeSnapshotForDirtyCheck(this);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// When the active project has unsaved edits, asks Save / Don't Save / Cancel.
    /// Returns true when the caller may proceed (saved, discarded, or nothing to save).
    /// </summary>
    private bool TryPromptSaveBeforeDiscard(string operationDescription)
    {
        if (!HasUnsavedChanges())
            return true;

        var projectLabel = !string.IsNullOrEmpty(CurrentXfemProjectPath)
            ? Path.GetFileName(CurrentXfemProjectPath)
            : string.IsNullOrWhiteSpace(ProjectSettingsVM.Project.ProjectName)
                ? "Untitled Project"
                : ProjectSettingsVM.Project.ProjectName.Trim();

        var result = MessageBox.Show(
            $"Save changes to \"{projectLabel}\" before {operationDescription}?",
            "Save Project",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => TrySaveCurrentProject(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private bool TrySaveCurrentProject()
    {
        if (string.IsNullOrEmpty(CurrentXfemProjectPath))
            return TrySaveAsCurrentProject();

        try
        {
            XfemProjectPersistence.Save(this, CurrentXfemProjectPath);
            ProjectWorkspace.UpdateActiveSlotFromVm(this, CurrentXfemProjectPath);
            RefreshSavedBaseline();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save project:\n{ex.Message}", "Save", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool TrySaveAsCurrentProject()
    {
        var dlg = new SaveFileDialog
        {
            Filter = XfemFilter,
            DefaultExt = XfemProjectPersistence.FileExtension.TrimStart('.'),
            FileName = string.IsNullOrEmpty(ProjectSettingsVM.Project.ProjectName)
                ? "project" + XfemProjectPersistence.FileExtension
                : Path.GetFileNameWithoutExtension(ProjectSettingsVM.Project.ProjectName) + XfemProjectPersistence.FileExtension
        };
        if (dlg.ShowDialog() != true)
            return false;

        try
        {
            var path = dlg.FileName;
            if (!path.EndsWith(XfemProjectPersistence.FileExtension, StringComparison.OrdinalIgnoreCase))
                path += XfemProjectPersistence.FileExtension;
            XfemProjectPersistence.Save(this, path);
            CurrentXfemProjectPath = path;
            ProjectWorkspace.UpdateActiveSlotFromVm(this, path);
            AddToRecentFiles(path);
            RefreshSavedBaseline();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save project:\n{ex.Message}", "Save As", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Keeps main-window stage footer tabs in sync with FE Analysis → staged analysis stage grid when staged analysis is enabled.</summary>
    private void SubscribeStagedAnalysisFooterSync()
    {
        FEAnalysisVM.PropertyChanged += OnFeAnalysisVmPropertyChangedForFooter;
        FEAnalysisVM.StageDefinitions.CollectionChanged += OnStageDefinitionsCollectionChangedForFooter;
        ResubscribeStageDefinitionPropertyHandlers();
    }

    private void OnFeAnalysisVmPropertyChangedForFooter(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FEAnalysisViewModel.StagedExcavationEnabled))
        {
            SyncFooterWithStagedAnalysisGrid();
            OnPropertyChanged(nameof(ShowStageFooterRow));
        }
    }

    private void OnStageDefinitionsCollectionChangedForFooter(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResubscribeStageDefinitionPropertyHandlers();
        SyncFooterWithStagedAnalysisGrid();
    }

    private void ResubscribeStageDefinitionPropertyHandlers()
    {
        foreach (var sd in _stageFooterSyncSubscriptions.ToList())
        {
            sd.PropertyChanged -= OnStageDefinitionPropertyChangedForFooter;
            _stageFooterSyncSubscriptions.Remove(sd);
        }

        foreach (var sd in FEAnalysisVM.StageDefinitions)
        {
            sd.PropertyChanged += OnStageDefinitionPropertyChangedForFooter;
            _stageFooterSyncSubscriptions.Add(sd);
        }
    }

    private void OnStageDefinitionPropertyChangedForFooter(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StageDefinition.StageName) && e.PropertyName != nameof(StageDefinition.StageNumber))
            return;
        SyncFooterWithStagedAnalysisGrid();
    }

    internal void SyncFooterWithStagedAnalysisGrid()
    {
        var fe = FEAnalysisVM;
        var ps = ProjectSettingsVM;
        if (fe == null || ps == null)
            return;

        if (!fe.StagedExcavationEnabled)
        {
            ps.ClearStageFooterTabs();
            CurrentStage = null;
            StagedConstructionVM.SyncConstructionStagesFromFeAnalysis(fe, false);
            OnPropertyChanged(nameof(ShowStageFooterRow));
            return;
        }

        if (fe.StageDefinitions.Count == 0)
        {
            ps.UpdateStageFooterTabs(Array.Empty<(int, string)>());
            ps.NumberOfStages = 0;
            ps.Project.NumberOfStages = 0;
            ps.SelectedStageFooterTabIndex = null;
            StagedConstructionVM.SyncConstructionStagesFromFeAnalysis(fe, true);
            OnPropertyChanged(nameof(ShowStageFooterRow));
            return;
        }

        var pairs = fe.StageDefinitions.Select(s =>
            (s.StageNumber, string.IsNullOrWhiteSpace(s.StageName) ? $"Stage {s.StageNumber}" : s.StageName.Trim()));
        ps.UpdateStageFooterTabs(pairs);
        var n = fe.StageDefinitions.Count;
        ps.NumberOfStages = n;
        ps.Project.NumberOfStages = n;
        var idx = ps.SelectedStageFooterTabIndex ?? 0;
        if (idx < 0 || idx >= n)
            idx = 0;
        ps.SelectedStageFooterTabIndex = idx;

        StagedConstructionVM.SyncConstructionStagesFromFeAnalysis(fe, true);
        RestoreCurrentStageFromStageFooter();
        OnPropertyChanged(nameof(ShowStageFooterRow));
    }

    /// <summary>Sets <see cref="CurrentStage"/> from the stage footer row when staged analysis is enabled.</summary>
    public void RestoreCurrentStageFromStageFooter()
    {
        if (!FEAnalysisVM.StagedExcavationEnabled)
        {
            CurrentStage = null;
            return;
        }

        var ps = ProjectSettingsVM;
        if (ps.SelectedStageFooterTabIndex is int idx && idx >= 0 && idx < ps.StageFooterTabs.Count)
            CurrentStage = ps.StageFooterTabs[idx].StageNumber;
        else
            CurrentStage = null;
    }

    /// <summary>Call after loading a project so footer matches staged stage definitions.</summary>
    public void RefreshStageFooterAfterProjectLoad()
    {
        SyncFooterWithStagedAnalysisGrid();
        RestoreCurrentStageFromStageFooter();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
