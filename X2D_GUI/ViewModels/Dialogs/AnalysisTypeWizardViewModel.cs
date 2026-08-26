using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Analysis;
using GeotechSlopeApp.Services.Analysis;
using GeotechSlopeApp.ViewModels.Workspace;

namespace GeotechSlopeApp.ViewModels.Dialogs;

/// <summary>
/// File → Settings → Analysis Type wizard. Pending intent and staged flag write on Finish only.
/// Hosts existing FE / field-stress / stage VMs — does not invent solvers.
/// </summary>
public sealed class AnalysisTypeWizardViewModel : INotifyPropertyChanged
{
    public const int StepSelectType = 1;
    public const int StepDefineOptions = 2;
    public const int StepStageSettings = 3;
    public const int StepSolverSettings = 4;
    public const int StepReview = 5;
    public const string StagesCardKey = "Stages";

    private readonly MainWindowViewModel _host;
    private int _currentStep = StepSelectType;
    private AnalysisIntent _pendingIntent;
    private bool _pendingStaged;
    private string _selectedCardKey = "Static";
    private string _componentTab = "Surfaces";
    private StageDefinition? _selectedStage;

    public AnalysisTypeWizardViewModel(MainWindowViewModel host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pendingIntent = MapInitialIntent(host.Workspace.Settings.Intent);
        _pendingStaged = host.FEAnalysisVM.StagedExcavationEnabled;
        _selectedCardKey = CardKeyForIntent(_pendingIntent);
        Cards = BuildCards();
        SyncCardSelection();

        FieldStressModeChoices = Enum.GetValues<FieldStressMode>();
        StageTypeOptions = FEAnalysisViewModel.StageTypeOptions;
        LoadConditionsVM.FieldStress.Global.PropertyChanged += OnFieldStressChanged;

        SelectCardCommand = new RelayCommand<AnalysisTypeCard>(OnSelectCard);
        NextCommand = new RelayCommand(OnNext);
        BackCommand = new RelayCommand(OnBack, () => CanGoBack);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
        AddStageCommand = new RelayCommand(OnAddStage);
        InsertStageCommand = new RelayCommand(OnInsertStage);
        DuplicateStageCommand = new RelayCommand(OnDuplicateStage, () => SelectedStage != null);
        RemoveStageCommand = new RelayCommand(OnRemoveStage, () => SelectedStage != null);
        MoveStageUpCommand = new RelayCommand(OnMoveStageUp, () => CanMoveSelected(-1));
        MoveStageDownCommand = new RelayCommand(OnMoveStageDown, () => CanMoveSelected(1));
        ImportSequenceCommand = new RelayCommand(() =>
            _host.ShowCapabilityMessage(
                AnalysisCapabilityCatalog.FeatureNotAvailable
                + " Import Sequence is not in this engine. Use staged analysis presets."));
        SelectComponentTabCommand = new RelayCommand<string>(tab =>
        {
            if (!string.IsNullOrWhiteSpace(tab))
                ComponentTab = tab!;
        });
        ToggleSurfaceActiveCommand = new RelayCommand<StageSurfaceActivityRow>(OnToggleSurfaceActive);
        DefineZonesCommand = new RelayCommand(() =>
            _host.ShowCapabilityMessage(
                AnalysisCapabilityCatalog.FeatureNotAvailable
                + " Spatial permeability zones are not in this engine. Use one kx/ky per material."));
        EditWaterHeadsCommand = _host.NavigateToGroundwaterCommand;

        if (StageDefinitions.Count > 0)
            SelectedStage = StageDefinitions[0];
        RebuildSurfaceRows();
        NotifyPending();
    }

    public event Action<bool>? CloseRequested;

    public MainWindowViewModel Host => _host;
    public AnalysisWorkspaceViewModel Workspace => _host.Workspace;
    public FEAnalysisViewModel FEAnalysisVM => _host.FEAnalysisVM;
    public LoadConditionsViewModel LoadConditionsVM => _host.LoadConditionsVM;
    public ProjectSettingsViewModel ProjectSettingsVM => _host.ProjectSettingsVM;
    public GroundwaterViewModel GroundwaterVM => _host.GroundwaterVM;
    public MaterialPropertiesViewModel MaterialPropertiesVM => _host.MaterialPropertiesVM;
    public ObservableCollection<StageDefinition> StageDefinitions => _host.FEAnalysisVM.StageDefinitions;
    public IReadOnlyList<string> StageTypeOptions { get; }
    public IReadOnlyList<FieldStressMode> FieldStressModeChoices { get; }
    public IReadOnlyList<string> PorePressureChoices => Workspace.PorePressureChoices;
    public IReadOnlyList<string> PhysicsTypeChoices { get; } =
    [
        "Static", "Seepage", "Consolidation", "Dynamic",
        "Thermal", "Coupled THM", "Stability (SSR)"
    ];
    public IReadOnlyList<string> FormulationChoices { get; } = ["Total Stress", "Effective Stress"];
    public IReadOnlyList<string> InitialStressMethodChoices { get; } =
        ["In-Situ", "Constant", "Direct", "Traction"];
    public IReadOnlyList<string> GatedLoadingTypes => Workspace.GatedLoadingTypes;
    public IReadOnlyList<string> GatedGravityDirections => Workspace.GatedGravityDirections;
    public IReadOnlyList<string> GatedTangentStiffness => Workspace.GatedTangentStiffness;
    public IReadOnlyList<string> GatedConvergenceOptions => Workspace.GatedConvergenceOptions;
    public IReadOnlyList<string> GatedK0Methods => Workspace.GatedK0Methods;
    public IReadOnlyList<string> GatedDampingTypes => Workspace.GatedDampingTypes;
    public IReadOnlyList<string> GatedDampingRatios => Workspace.GatedDampingRatios;
    public IReadOnlyList<string> GatedIncompatibleModes => Workspace.GatedIncompatibleModes;
    public IReadOnlyList<string> GatedBoundaryStiffness => Workspace.GatedBoundaryStiffness;
    public IReadOnlyList<string> GatedStressRecovery => Workspace.GatedStressRecovery;
    public IReadOnlyList<string> GatedStiffnessMatrix => Workspace.GatedStiffnessMatrix;
    public IReadOnlyList<string> GatedLineSearch => Workspace.GatedLineSearch;

    public string GatedLoadingType
    {
        get => Workspace.GatedLoadingType;
        set
        {
            Workspace.GatedLoadingType = value;
            OnPropertyChanged();
        }
    }
    public string GatedGravityDirection
    {
        get => Workspace.GatedGravityDirection;
        set
        {
            Workspace.GatedGravityDirection = value;
            OnPropertyChanged();
        }
    }
    public string GatedTangentStiffnessValue
    {
        get => Workspace.GatedTangentStiffnessValue;
        set
        {
            Workspace.GatedTangentStiffnessValue = value;
            OnPropertyChanged();
        }
    }
    public string GatedConvergenceOption
    {
        get => Workspace.GatedConvergenceOption;
        set
        {
            Workspace.GatedConvergenceOption = value;
            OnPropertyChanged();
        }
    }
    public string GatedK0Method
    {
        get => Workspace.GatedK0Method;
        set
        {
            Workspace.GatedK0Method = value;
            OnPropertyChanged();
        }
    }
    public string GatedDampingType
    {
        get => Workspace.GatedDampingType;
        set
        {
            Workspace.GatedDampingType = value;
            OnPropertyChanged();
        }
    }
    public string GatedDampingRatio
    {
        get => Workspace.GatedDampingRatio;
        set
        {
            Workspace.GatedDampingRatio = value;
            OnPropertyChanged();
        }
    }
    public string GatedIncompatibleMode
    {
        get => Workspace.GatedIncompatibleMode;
        set
        {
            Workspace.GatedIncompatibleMode = value;
            OnPropertyChanged();
        }
    }
    public string GatedBoundaryStiffnessValue
    {
        get => Workspace.GatedBoundaryStiffnessValue;
        set
        {
            Workspace.GatedBoundaryStiffnessValue = value;
            OnPropertyChanged();
        }
    }
    public string GatedStressRecoveryValue
    {
        get => Workspace.GatedStressRecoveryValue;
        set
        {
            Workspace.GatedStressRecoveryValue = value;
            OnPropertyChanged();
        }
    }
    public string GatedStiffnessMatrixValue
    {
        get => Workspace.GatedStiffnessMatrixValue;
        set
        {
            Workspace.GatedStiffnessMatrixValue = value;
            OnPropertyChanged();
        }
    }
    public string GatedLineSearchValue
    {
        get => Workspace.GatedLineSearchValue;
        set
        {
            Workspace.GatedLineSearchValue = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> SeepageFlowTypes => AnalysisGatedOptionCatalog.SeepageFlowTypes;
    public IReadOnlyList<string> PoreFluids => AnalysisGatedOptionCatalog.PoreFluids;
    public IReadOnlyList<string> BoundaryConductanceChoices => AnalysisGatedOptionCatalog.BoundaryConductance;
    public IReadOnlyList<string> InitialConditionTypes => AnalysisGatedOptionCatalog.InitialConditionTypes;
    public IReadOnlyList<string> OutputLocations => AnalysisGatedOptionCatalog.OutputLocations;
    public IReadOnlyList<string> ResultTypes => AnalysisGatedOptionCatalog.ResultTypes;
    public IReadOnlyList<string> ExportIntervals => AnalysisGatedOptionCatalog.ExportIntervals;
    public IReadOnlyList<string> ConsolidationTypes => AnalysisGatedOptionCatalog.ConsolidationTypes;
    public IReadOnlyList<string> CompressionModels => AnalysisGatedOptionCatalog.CompressionModels;
    public IReadOnlyList<string> TimeUnits => AnalysisGatedOptionCatalog.TimeUnits;
    public IReadOnlyList<string> TimeIncrementationChoices => AnalysisGatedOptionCatalog.TimeIncrementation;
    public IReadOnlyList<string> DynamicDampingTypes => AnalysisGatedOptionCatalog.DynamicDampingTypes;
    public IReadOnlyList<string> IntegrationMethods => AnalysisGatedOptionCatalog.IntegrationMethods;
    public IReadOnlyList<string> MassFormulations => AnalysisGatedOptionCatalog.MassFormulations;
    public IReadOnlyList<string> DynamicLoadTypes => AnalysisGatedOptionCatalog.DynamicLoadTypes;
    public IReadOnlyList<string> SeismicDirections => AnalysisGatedOptionCatalog.SeismicDirections;
    public IReadOnlyList<string> SeismicInputTypes => AnalysisGatedOptionCatalog.SeismicInputTypes;
    public IReadOnlyList<string> SeismicFunctions => AnalysisGatedOptionCatalog.SeismicFunctions;
    public IReadOnlyList<string> BoundaryDampingChoices => AnalysisGatedOptionCatalog.BoundaryDamping;
    public IReadOnlyList<string> SamplingIntervals => AnalysisGatedOptionCatalog.SamplingIntervals;
    public IReadOnlyList<string> TimeHistorySaveChoices => AnalysisGatedOptionCatalog.TimeHistorySave;
    public IReadOnlyList<string> ThermalProblemTypes => AnalysisGatedOptionCatalog.ThermalProblemTypes;
    public IReadOnlyList<string> ThermalFormulations => AnalysisGatedOptionCatalog.ThermalFormulations;
    public IReadOnlyList<string> CoupledSchemes => AnalysisGatedOptionCatalog.CoupledSchemes;

    private string _gatedPoreFluid = AnalysisGatedOptionCatalog.PoreFluids[0];
    private string _gatedBoundaryConductance = AnalysisGatedOptionCatalog.BoundaryConductance[0];
    private string _gatedConductanceFactor = "1.0";
    private string _gatedViscosity = "1.00E-03";
    private string _gatedFluidDensity = "1000";
    private string _gatedWaterCompressibility = "4.40E-10";
    private string _gatedOutputLocation = AnalysisGatedOptionCatalog.OutputLocations[0];
    private string _gatedResultType = AnalysisGatedOptionCatalog.ResultTypes[0];
    private string _gatedExportInterval = AnalysisGatedOptionCatalog.ExportIntervals[0];
    private string _gatedConsolidationType = AnalysisGatedOptionCatalog.ConsolidationTypes[0];
    private string _gatedCompressionModel = AnalysisGatedOptionCatalog.CompressionModels[0];
    private string _gatedCompressionCc = "0.250";
    private string _gatedCompressionCr = "0.045";
    private string _gatedCompressionCs = "0.020";
    private string _gatedPreconsolidationPc = "150.00";
    private string _gatedMinTimeStep = "1.00E-03";
    private string _gatedTimeUnit = AnalysisGatedOptionCatalog.TimeUnits[0];
    private string _gatedTimeIncrementation = AnalysisGatedOptionCatalog.TimeIncrementation[0];
    private string _gatedKzEcho = "1.00E-06";
    private string _gatedDynamicDamping = AnalysisGatedOptionCatalog.DynamicDampingTypes[0];
    private string _gatedRayleighAlpha = "0.020";
    private string _gatedRayleighBeta = "0.0005";
    private string _gatedHystereticRatio = "0.05";
    private string _gatedIntegrationMethod = AnalysisGatedOptionCatalog.IntegrationMethods[0];
    private string _gatedNewmarkGamma = "0.50";
    private string _gatedNewmarkBeta = "0.25";
    private string _gatedHhtAlpha = "-0.05";
    private string _gatedMassFormulation = AnalysisGatedOptionCatalog.MassFormulations[0];
    private string _gatedDynamicLoadType = AnalysisGatedOptionCatalog.DynamicLoadTypes[0];
    private string _gatedSeismicDirection = AnalysisGatedOptionCatalog.SeismicDirections[0];
    private string _gatedScaleFactor = "1.00";
    private string _gatedSeismicInputType = AnalysisGatedOptionCatalog.SeismicInputTypes[0];
    private string _gatedSeismicFunction = AnalysisGatedOptionCatalog.SeismicFunctions[0];
    private string _gatedSeismicTimeStep = "0.005";
    private string _gatedSeismicDuration = "20.00";
    private string _gatedAtRestInitial = "At Rest";
    private string _gatedInitialVelocity = "0.0";
    private string _gatedBoundaryDamping = AnalysisGatedOptionCatalog.BoundaryDamping[0];
    private string _gatedDampingCoefficient = "0.10";
    private string _gatedSamplingInterval = AnalysisGatedOptionCatalog.SamplingIntervals[0];
    private string _gatedTimeHistorySave = AnalysisGatedOptionCatalog.TimeHistorySave[0];
    private string _gatedThermalProblemType = AnalysisGatedOptionCatalog.ThermalProblemTypes[0];
    private string _gatedThermalFormulation = AnalysisGatedOptionCatalog.ThermalFormulations[0];
    private string _gatedHeatGeneration = "0";
    private string _gatedInitialTemperature = "0";
    private string _gatedCoupledScheme = AnalysisGatedOptionCatalog.CoupledSchemes[0];
    private string _gatedReferenceTemperature = "20.0";
    private bool _spatiallyVaryingPermeability;
    private bool _checkSeepageForce;
    private bool _includeTemperatureEffect;
    private bool _outputPorePressureHead = true;
    private bool _outputSeepageVelocity = true;
    private bool _outputSeepageFlux = true;
    private bool _permeabilityVariesWithVoidRatio;
    private bool _includeSecondaryCompression;
    private bool _includeLargeDeformation;
    private bool _outputExcessPorePressure = true;
    private bool _outputSettlement = true;
    private bool _outputEffectiveStress = true;
    private bool _outputDegreeOfConsolidation = true;
    private bool _useMassScaling;
    private bool _useRayleighDamping = true;
    private bool _useHystereticDamping;
    private bool _useHht;
    private bool _lumpedMass;
    private bool _rotaryInertia = true;
    private bool _includeAddedMass;
    private bool _applyInAllActivatedStages = true;
    private bool _initialVelocityEnabled;
    private bool _outputAcceleration = true;
    private bool _outputVelocity = true;
    private bool _outputDisplacement = true;
    private bool _outputStressStrain = true;
    private bool _includeHeatGeneration;
    private bool _outputTemperature = true;
    private bool _outputHeatFlux = true;
    private bool _outputIsotherms = true;
    private bool _tensionCutoff;
    private bool _outputSrf = true;
    private bool _outputSrfDisplacement = true;
    private bool _outputPlasticZone = true;
    private bool _generateInitialPorePressure = true;
    private bool _generateInitialStress = true;
    private bool _includeMaterialNonlinearity = true;
    private bool _includeGeometricNonlinearity;
    private bool _includeContactNonlinearity;
    private bool _automaticTimeStepping;

    public string GatedSeepageFlowType
    {
        get
        {
            if (IsSeepageTransientFlow)
                return AnalysisGatedOptionCatalog.SeepageFlowTypes[2];
            if (IsSeepageStaticFlow)
                return AnalysisGatedOptionCatalog.SeepageFlowTypes[0];
            return AnalysisGatedOptionCatalog.SeepageFlowTypes[1];
        }
        set => ApplySeepageFlowType(value);
    }

    public bool IsSeepageStaticFlow =>
        string.Equals(MaterialPropertiesVM.SelectedDetailedAnalysisType,
            MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal)
        || string.Equals(MaterialPropertiesVM.LastGroundwaterTreeMode,
            MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal);

    public bool IsSeepageTransientFlow =>
        MaterialPropertiesViewModel.IsTransientFlowAnalysisType(MaterialPropertiesVM.SelectedDetailedAnalysisType)
        || MaterialPropertiesViewModel.IsGroundwaterTransientStateTreeType(MaterialPropertiesVM.LastGroundwaterTreeMode);

    public bool ShowSeepageStaticMethod => IsSeepageStaticFlow;
    public bool ShowSeepageSteadyMethod => !IsSeepageStaticFlow && !IsSeepageTransientFlow;
    public bool ShowSeepageTransientMethod => IsSeepageTransientFlow;

    public string GatedPoreFluid { get => _gatedPoreFluid; set => SetGated(ref _gatedPoreFluid, value); }
    public string GatedBoundaryConductance { get => _gatedBoundaryConductance; set => SetGated(ref _gatedBoundaryConductance, value); }
    public string GatedConductanceFactor { get => _gatedConductanceFactor; set => SetGated(ref _gatedConductanceFactor, value); }

    public string GatedInitialConditionType
    {
        get => GroundwaterVM.Method == GroundwaterMethod.Grid
            ? AnalysisGatedOptionCatalog.InitialConditionTypes[2]
            : AnalysisGatedOptionCatalog.InitialConditionTypes[0];
        set
        {
            if (string.Equals(value, AnalysisGatedOptionCatalog.InitialConditionTypes[2], StringComparison.OrdinalIgnoreCase))
            {
                GroundwaterVM.Method = GroundwaterMethod.Grid;
                ProjectSettingsVM.GroundwaterStaticSettings.IncludeWaterPressureGridPorePressure = true;
            }
            else
            {
                GroundwaterVM.Method = GroundwaterMethod.Hydrostatic;
            }

            OnPropertyChanged();
        }
    }

    public string GatedReferenceLevel
    {
        get => GroundwaterVM.WaterTableInput;
        set
        {
            GroundwaterVM.WaterTableInput = value ?? "";
            OnPropertyChanged();
        }
    }

    public string GatedPorePressureAtReference
    {
        get => ProjectSettingsVM.TransientSettings.InitialPressure.ToString("0.##", CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pressure)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out pressure))
            {
                ProjectSettingsVM.TransientSettings.InitialPressure = pressure;
            }

            OnPropertyChanged();
        }
    }

    public double SeepageUpstreamHead
    {
        get => ProjectSettingsVM.SeepageSettings.UpstreamHead;
        set
        {
            ProjectSettingsVM.SeepageSettings.UpstreamHead = value;
            OnPropertyChanged();
        }
    }

    public double SeepageDownstreamHead
    {
        get => ProjectSettingsVM.SeepageSettings.DownstreamHead;
        set
        {
            ProjectSettingsVM.SeepageSettings.DownstreamHead = value;
            OnPropertyChanged();
        }
    }
    public string GatedViscosity { get => _gatedViscosity; set => SetGated(ref _gatedViscosity, value); }
    public string GatedFluidDensity { get => _gatedFluidDensity; set => SetGated(ref _gatedFluidDensity, value); }
    public string GatedWaterCompressibility { get => _gatedWaterCompressibility; set => SetGated(ref _gatedWaterCompressibility, value); }
    public string GatedOutputLocation { get => _gatedOutputLocation; set => SetGated(ref _gatedOutputLocation, value); }
    public string GatedResultType { get => _gatedResultType; set => SetGated(ref _gatedResultType, value); }
    public string GatedExportInterval { get => _gatedExportInterval; set => SetGated(ref _gatedExportInterval, value); }
    public string GatedConsolidationType { get => _gatedConsolidationType; set => SetGated(ref _gatedConsolidationType, value); }
    public string GatedCompressionModel { get => _gatedCompressionModel; set => SetGated(ref _gatedCompressionModel, value); }
    public string GatedCompressionCc { get => _gatedCompressionCc; set => SetGated(ref _gatedCompressionCc, value); }
    public string GatedCompressionCr { get => _gatedCompressionCr; set => SetGated(ref _gatedCompressionCr, value); }
    public string GatedCompressionCs { get => _gatedCompressionCs; set => SetGated(ref _gatedCompressionCs, value); }
    public string GatedPreconsolidationPc { get => _gatedPreconsolidationPc; set => SetGated(ref _gatedPreconsolidationPc, value); }
    public string GatedMinTimeStep { get => _gatedMinTimeStep; set => SetGated(ref _gatedMinTimeStep, value); }
    public string GatedTimeUnit { get => _gatedTimeUnit; set => SetGated(ref _gatedTimeUnit, value); }
    public string GatedTimeIncrementation { get => _gatedTimeIncrementation; set => SetGated(ref _gatedTimeIncrementation, value); }
    public string GatedKzEcho { get => _gatedKzEcho; set => SetGated(ref _gatedKzEcho, value); }
    public string GatedDynamicDamping { get => _gatedDynamicDamping; set => SetGated(ref _gatedDynamicDamping, value); }
    public string GatedRayleighAlpha { get => _gatedRayleighAlpha; set => SetGated(ref _gatedRayleighAlpha, value); }
    public string GatedRayleighBeta { get => _gatedRayleighBeta; set => SetGated(ref _gatedRayleighBeta, value); }
    public string GatedHystereticRatio { get => _gatedHystereticRatio; set => SetGated(ref _gatedHystereticRatio, value); }
    public string GatedIntegrationMethod { get => _gatedIntegrationMethod; set => SetGated(ref _gatedIntegrationMethod, value); }
    public string GatedNewmarkGamma { get => _gatedNewmarkGamma; set => SetGated(ref _gatedNewmarkGamma, value); }
    public string GatedNewmarkBeta { get => _gatedNewmarkBeta; set => SetGated(ref _gatedNewmarkBeta, value); }
    public string GatedHhtAlpha { get => _gatedHhtAlpha; set => SetGated(ref _gatedHhtAlpha, value); }
    public string GatedMassFormulation { get => _gatedMassFormulation; set => SetGated(ref _gatedMassFormulation, value); }
    public string GatedDynamicLoadType { get => _gatedDynamicLoadType; set => SetGated(ref _gatedDynamicLoadType, value); }
    public string GatedSeismicDirection { get => _gatedSeismicDirection; set => SetGated(ref _gatedSeismicDirection, value); }
    public string GatedScaleFactor { get => _gatedScaleFactor; set => SetGated(ref _gatedScaleFactor, value); }
    public string GatedSeismicInputType { get => _gatedSeismicInputType; set => SetGated(ref _gatedSeismicInputType, value); }
    public string GatedSeismicFunction { get => _gatedSeismicFunction; set => SetGated(ref _gatedSeismicFunction, value); }
    public string GatedSeismicTimeStep { get => _gatedSeismicTimeStep; set => SetGated(ref _gatedSeismicTimeStep, value); }
    public string GatedSeismicDuration { get => _gatedSeismicDuration; set => SetGated(ref _gatedSeismicDuration, value); }
    public string GatedAtRestInitial { get => _gatedAtRestInitial; set => SetGated(ref _gatedAtRestInitial, value); }
    public string GatedInitialVelocity { get => _gatedInitialVelocity; set => SetGated(ref _gatedInitialVelocity, value); }
    public string GatedBoundaryDamping { get => _gatedBoundaryDamping; set => SetGated(ref _gatedBoundaryDamping, value); }
    public string GatedDampingCoefficient { get => _gatedDampingCoefficient; set => SetGated(ref _gatedDampingCoefficient, value); }
    public string GatedSamplingInterval { get => _gatedSamplingInterval; set => SetGated(ref _gatedSamplingInterval, value); }
    public string GatedTimeHistorySave { get => _gatedTimeHistorySave; set => SetGated(ref _gatedTimeHistorySave, value); }
    public string GatedThermalProblemType { get => _gatedThermalProblemType; set => SetGated(ref _gatedThermalProblemType, value); }
    public string GatedThermalFormulation { get => _gatedThermalFormulation; set => SetGated(ref _gatedThermalFormulation, value); }
    public string GatedHeatGeneration { get => _gatedHeatGeneration; set => SetGated(ref _gatedHeatGeneration, value); }
    public string GatedInitialTemperature { get => _gatedInitialTemperature; set => SetGated(ref _gatedInitialTemperature, value); }
    public string GatedCoupledScheme { get => _gatedCoupledScheme; set => SetGated(ref _gatedCoupledScheme, value); }
    public string GatedReferenceTemperature { get => _gatedReferenceTemperature; set => SetGated(ref _gatedReferenceTemperature, value); }

    public bool SpatiallyVaryingPermeability { get => _spatiallyVaryingPermeability; set => SetGated(ref _spatiallyVaryingPermeability, value); }
    public bool CheckSeepageForce { get => _checkSeepageForce; set => SetGated(ref _checkSeepageForce, value); }
    public bool IncludeTemperatureEffect { get => _includeTemperatureEffect; set => SetGated(ref _includeTemperatureEffect, value); }
    public bool IncludeTimeDependency
    {
        get => IsSeepageTransientFlow;
        set
        {
            if (IsSeepageTransientFlow == value)
                return;
            ApplySeepageFlowType(value
                ? AnalysisGatedOptionCatalog.SeepageFlowTypes[2]
                : AnalysisGatedOptionCatalog.SeepageFlowTypes[1]);
        }
    }
    public bool OutputPorePressureHead { get => _outputPorePressureHead; set => SetGated(ref _outputPorePressureHead, value); }
    public bool OutputSeepageVelocity { get => _outputSeepageVelocity; set => SetGated(ref _outputSeepageVelocity, value); }
    public bool OutputSeepageFlux { get => _outputSeepageFlux; set => SetGated(ref _outputSeepageFlux, value); }
    public bool PermeabilityVariesWithVoidRatio { get => _permeabilityVariesWithVoidRatio; set => SetGated(ref _permeabilityVariesWithVoidRatio, value); }
    public bool IncludeSecondaryCompression { get => _includeSecondaryCompression; set => SetGated(ref _includeSecondaryCompression, value); }
    public bool IncludeLargeDeformation { get => _includeLargeDeformation; set => SetGated(ref _includeLargeDeformation, value); }
    public bool OutputExcessPorePressure { get => _outputExcessPorePressure; set => SetGated(ref _outputExcessPorePressure, value); }
    public bool OutputSettlement { get => _outputSettlement; set => SetGated(ref _outputSettlement, value); }
    public bool OutputEffectiveStress { get => _outputEffectiveStress; set => SetGated(ref _outputEffectiveStress, value); }
    public bool OutputDegreeOfConsolidation { get => _outputDegreeOfConsolidation; set => SetGated(ref _outputDegreeOfConsolidation, value); }
    public bool UseMassScaling { get => _useMassScaling; set => SetGated(ref _useMassScaling, value); }
    public bool UseRayleighDamping { get => _useRayleighDamping; set => SetGated(ref _useRayleighDamping, value); }
    public bool UseHystereticDamping { get => _useHystereticDamping; set => SetGated(ref _useHystereticDamping, value); }
    public bool UseHht { get => _useHht; set => SetGated(ref _useHht, value); }
    public bool LumpedMass { get => _lumpedMass; set => SetGated(ref _lumpedMass, value); }
    public bool RotaryInertia { get => _rotaryInertia; set => SetGated(ref _rotaryInertia, value); }
    public bool IncludeAddedMass { get => _includeAddedMass; set => SetGated(ref _includeAddedMass, value); }
    public bool ApplyInAllActivatedStages { get => _applyInAllActivatedStages; set => SetGated(ref _applyInAllActivatedStages, value); }
    public bool InitialVelocityEnabled { get => _initialVelocityEnabled; set => SetGated(ref _initialVelocityEnabled, value); }
    public bool OutputAcceleration { get => _outputAcceleration; set => SetGated(ref _outputAcceleration, value); }
    public bool OutputVelocity { get => _outputVelocity; set => SetGated(ref _outputVelocity, value); }
    public bool OutputDisplacement { get => _outputDisplacement; set => SetGated(ref _outputDisplacement, value); }
    public bool OutputStressStrain { get => _outputStressStrain; set => SetGated(ref _outputStressStrain, value); }
    public bool IncludeHeatGeneration { get => _includeHeatGeneration; set => SetGated(ref _includeHeatGeneration, value); }
    public bool OutputTemperature { get => _outputTemperature; set => SetGated(ref _outputTemperature, value); }
    public bool OutputHeatFlux { get => _outputHeatFlux; set => SetGated(ref _outputHeatFlux, value); }
    public bool OutputIsotherms { get => _outputIsotherms; set => SetGated(ref _outputIsotherms, value); }
    public bool TensionCutoff { get => _tensionCutoff; set => SetGated(ref _tensionCutoff, value); }
    public bool OutputSrf { get => _outputSrf; set => SetGated(ref _outputSrf, value); }
    public bool OutputSrfDisplacement { get => _outputSrfDisplacement; set => SetGated(ref _outputSrfDisplacement, value); }
    public bool OutputPlasticZone { get => _outputPlasticZone; set => SetGated(ref _outputPlasticZone, value); }
    public bool IncludeMaterialNonlinearity { get => _includeMaterialNonlinearity; set => SetGated(ref _includeMaterialNonlinearity, value); }
    public bool IncludeGeometricNonlinearity { get => _includeGeometricNonlinearity; set => SetGated(ref _includeGeometricNonlinearity, value); }
    public bool IncludeContactNonlinearity { get => _includeContactNonlinearity; set => SetGated(ref _includeContactNonlinearity, value); }
    public bool AutomaticTimeStepping { get => _automaticTimeStepping; set => SetGated(ref _automaticTimeStepping, value); }

    private void SetGated<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    public MaterialModel? SelectedMaterial =>
        MaterialPropertiesVM.SelectedMaterialModel
        ?? (MaterialPropertiesVM.Materials.Count > 0 ? MaterialPropertiesVM.Materials[0] : null);

    public bool HasSelectedMaterial => SelectedMaterial != null;

    public bool IsPermeabilityIsotropic
    {
        get
        {
            var material = SelectedMaterial;
            return material == null || Math.Abs(material.PermeabilityX - material.PermeabilityY) < 1e-18;
        }
        set
        {
            if (SelectedMaterial == null || !value)
                return;
            SelectedMaterial.PermeabilityY = SelectedMaterial.PermeabilityX;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPermeabilityAnisotropic));
        }
    }

    public bool IsPermeabilityAnisotropic
    {
        get => !IsPermeabilityIsotropic;
        set
        {
            if (value)
                OnPropertyChanged(nameof(IsPermeabilityIsotropic));
        }
    }

    public double SelectedPermeabilityX
    {
        get => SelectedMaterial?.PermeabilityX ?? 1e-6;
        set
        {
            if (SelectedMaterial == null) return;
            SelectedMaterial.PermeabilityX = value;
            if (IsPermeabilityIsotropic)
                SelectedMaterial.PermeabilityY = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPermeabilityY));
        }
    }

    public double SelectedPermeabilityY
    {
        get => SelectedMaterial?.PermeabilityY ?? 1e-6;
        set
        {
            if (SelectedMaterial == null) return;
            SelectedMaterial.PermeabilityY = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPermeabilityIsotropic));
            OnPropertyChanged(nameof(IsPermeabilityAnisotropic));
        }
    }

    public bool IsSeepageHydraulicHead
    {
        get
        {
            var s = ProjectSettingsVM.SeepageSettings;
            return s.IncludeWaterHeadPlaneAxisym
                || s.IncludeWaterHeadFreeSurface
                || s.IncludeWaterHeadGeneral2D
                || s.IncludeWaterHeadGeneral3D;
        }
        set
        {
            ProjectSettingsVM.SeepageSettings.SetAllIncludeWaterHead(value);
            NotifySeepageRouting();
        }
    }

    public bool IsSeepagePorePressureFormulation
    {
        get => !IsSeepageHydraulicHead;
        set
        {
            if (value)
                IsSeepageHydraulicHead = false;
        }
    }

    public bool IsConsolidationPorePressureFormulation
    {
        get => LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio;
        set
        {
            LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConsolidationTotalStressFormulation));
            OnPropertyChanged(nameof(SelectedFormulation));
        }
    }

    public bool IsConsolidationTotalStressFormulation
    {
        get => !IsConsolidationPorePressureFormulation;
        set
        {
            if (value)
                IsConsolidationPorePressureFormulation = false;
        }
    }

    public double ConsolidationTotalTime
    {
        get => ProjectSettingsVM.TransientSettings.TimeStep * ProjectSettingsVM.TransientSettings.NumberOfSteps;
        set
        {
            var dt = ProjectSettingsVM.TransientSettings.TimeStep;
            if (dt > 0)
                ProjectSettingsVM.TransientSettings.NumberOfSteps = Math.Max(1, (int)Math.Round(value / dt));
            OnPropertyChanged();
        }
    }

    public bool GenerateInitialPorePressure
    {
        get => _generateInitialPorePressure;
        set => SetGated(ref _generateInitialPorePressure, value);
    }

    public bool GenerateInitialStress
    {
        get => _generateInitialStress;
        set => SetGated(ref _generateInitialStress, value);
    }

    public double DynamicSeismicKh
    {
        get => LoadConditionsVM.SeismicLegendKh;
        set
        {
            LoadConditionsVM.SeismicAnalysisEnabled = true;
            if (LoadConditionsVM.SeismicEditTarget != null)
                LoadConditionsVM.SeismicEditTarget.PseudoStaticKh = value;
            OnPropertyChanged();
        }
    }

    public double DynamicSeismicKv
    {
        get => LoadConditionsVM.SeismicLegendKv;
        set
        {
            LoadConditionsVM.SeismicAnalysisEnabled = true;
            if (LoadConditionsVM.SeismicEditTarget != null)
                LoadConditionsVM.SeismicEditTarget.PseudoStaticKv = value;
            OnPropertyChanged();
        }
    }

    public double SelectedThermalConductivityX
    {
        get => SelectedMaterial?.ThermalConductivityX ?? 2.0;
        set
        {
            if (SelectedMaterial == null) return;
            SelectedMaterial.ThermalConductivityX = value;
            OnPropertyChanged();
        }
    }

    public double SelectedThermalConductivityY
    {
        get => SelectedMaterial?.ThermalConductivityY ?? 2.0;
        set
        {
            if (SelectedMaterial == null) return;
            SelectedMaterial.ThermalConductivityY = value;
            OnPropertyChanged();
        }
    }

    public double SelectedHeatCapacity
    {
        get => SelectedMaterial?.HeatCapacity ?? 1000.0;
        set
        {
            if (SelectedMaterial == null) return;
            SelectedMaterial.HeatCapacity = value;
            OnPropertyChanged();
        }
    }

    public double SelectedDensity
    {
        get => SelectedMaterial?.Density ?? 2000.0;
        set
        {
            if (SelectedMaterial == null) return;
            SelectedMaterial.Density = value;
            OnPropertyChanged();
        }
    }

    public bool IsSiUnits
    {
        get => !ProjectSettingsVM.IsImperialUnits;
        set
        {
            if (value)
                ProjectSettingsVM.Project.Units = ProjectUnitSystem.Si;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUsUnits));
            OnPropertyChanged(nameof(StatusUnits));
            OnPropertyChanged(nameof(StatusBarText));
        }
    }

    public bool IsUsUnits
    {
        get => ProjectSettingsVM.IsImperialUnits;
        set
        {
            if (value)
                ProjectSettingsVM.Project.Units = ProjectUnitSystem.Imperial;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSiUnits));
            OnPropertyChanged(nameof(StatusUnits));
            OnPropertyChanged(nameof(StatusBarText));
        }
    }

    public bool IsLimitEquilibriumSelected =>
        string.Equals(Workspace.Settings.Method, "Limit Equilibrium", StringComparison.Ordinal);
    public bool IsLocalSsrSelected =>
        string.Equals(Workspace.Settings.SsrType, "Local", StringComparison.Ordinal);

    public string SelectedPhysicsType
    {
        get => PendingIntentDisplayName;
        set
        {
            var card = Cards.FirstOrDefault(c => !c.IsStageConstruction && c.Title == value);
            if (card?.Intent is not { } intent)
                return;
            PendingIntent = intent;
            SelectedCardKey = card.Key;
        }
    }

    public string SelectedFormulation
    {
        get => LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio
            ? "Effective Stress"
            : "Total Stress";
        set
        {
            var useEffective = string.Equals(value, "Effective Stress", StringComparison.Ordinal);
            if (LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio == useEffective)
                return;
            LoadConditionsVM.FieldStress.Global.UseEffectiveStressRatio = useEffective;
            OnPropertyChanged();
        }
    }

    public string SelectedInitialStressMethod
    {
        get => LoadConditionsVM.FieldStress.Global.Mode switch
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
            if (LoadConditionsVM.FieldStress.Global.Mode == mode)
                return;
            LoadConditionsVM.FieldStress.Global.Mode = mode;
            OnPropertyChanged();
        }
    }
    public IReadOnlyList<AnalysisTypeCard> Cards { get; }
    public ObservableCollection<StageSurfaceActivityRow> SurfaceRows { get; } = new();

    public ICommand SelectCardCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand AddStageCommand { get; }
    public ICommand InsertStageCommand { get; }
    public ICommand DuplicateStageCommand { get; }
    public ICommand RemoveStageCommand { get; }
    public ICommand MoveStageUpCommand { get; }
    public ICommand MoveStageDownCommand { get; }
    public ICommand ImportSequenceCommand { get; }
    public ICommand SelectComponentTabCommand { get; }
    public ICommand ToggleSurfaceActiveCommand { get; }
    public ICommand DefineZonesCommand { get; }
    public ICommand EditWaterHeadsCommand { get; }

    public int CurrentStep
    {
        get => _currentStep;
        set
        {
            var next = Math.Clamp(value, StepSelectType, StepReview);
            if (_currentStep == next) return;
            _currentStep = next;
            OnPropertyChanged();
            NotifyStep();
            ((RelayCommand)BackCommand).RaiseCanExecuteChanged();
        }
    }

    public AnalysisIntent PendingIntent
    {
        get => _pendingIntent;
        set
        {
            if (_pendingIntent == value) return;
            _pendingIntent = value;
            OnPropertyChanged();
            NotifyPending();
        }
    }

    public bool PendingStaged
    {
        get => _pendingStaged;
        set
        {
            if (_pendingStaged == value) return;
            _pendingStaged = value;
            OnPropertyChanged();
        }
    }

    public string SelectedCardKey
    {
        get => _selectedCardKey;
        set
        {
            if (_selectedCardKey == value) return;
            _selectedCardKey = value;
            OnPropertyChanged();
            SyncCardSelection();
        }
    }

    public StageDefinition? SelectedStage
    {
        get => _selectedStage;
        set
        {
            if (ReferenceEquals(_selectedStage, value)) return;
            _selectedStage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedStage));
            RebuildSurfaceRows();
            ((RelayCommand)DuplicateStageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RemoveStageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)MoveStageUpCommand).RaiseCanExecuteChanged();
            ((RelayCommand)MoveStageDownCommand).RaiseCanExecuteChanged();
        }
    }

    public string ComponentTab
    {
        get => _componentTab;
        set
        {
            if (_componentTab == value) return;
            _componentTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSurfacesTab));
            OnPropertyChanged(nameof(IsSupportsTab));
            OnPropertyChanged(nameof(IsLoadsTab));
            OnPropertyChanged(nameof(IsBoundaryTab));
            OnPropertyChanged(nameof(IsMaterialsTab));
        }
    }

    public bool HasSelectedStage => SelectedStage != null;

    public bool IsSurfacesTab => ComponentTab == "Surfaces";
    public bool IsSupportsTab => ComponentTab == "Supports";
    public bool IsLoadsTab => ComponentTab == "Loads";
    public bool IsBoundaryTab => ComponentTab == "Boundary";
    public bool IsMaterialsTab => ComponentTab == "Materials";

    public bool IsStepSelectType => CurrentStep == StepSelectType;
    public bool IsStepDefineOptions => CurrentStep == StepDefineOptions;
    public bool IsStepStageSettings => CurrentStep == StepStageSettings;
    public bool IsStepSolverSettings => CurrentStep == StepSolverSettings;
    public bool IsStepReview => CurrentStep == StepReview;
    public bool CanGoBack => CurrentStep > StepSelectType;
    public bool IsLastStep => CurrentStep == StepReview;
    public string NextLabel => IsLastStep ? "Finish" : "Next >";

    public string StepTitle => CurrentStep switch
    {
        StepSelectType => "ANALYSIS TYPE",
        StepDefineOptions => PendingIntent switch
        {
            AnalysisIntent.Seepage => "DEFINE OPTIONS (SEEPAGE ANALYSIS)",
            AnalysisIntent.Consolidation => "DEFINE OPTIONS (CONSOLIDATION ANALYSIS)",
            AnalysisIntent.Dynamic => "DEFINE OPTIONS (DYNAMIC ANALYSIS)",
            AnalysisIntent.Thermal => "DEFINE OPTIONS (THERMAL ANALYSIS)",
            AnalysisIntent.Coupled => "DEFINE OPTIONS (COUPLED THM ANALYSIS)",
            AnalysisIntent.Srf => "DEFINE OPTIONS (STABILITY ANALYSIS - SSR)",
            _ => "DEFINE OPTIONS"
        },
        StepStageSettings => "STAGE CONSTRUCTION",
        StepSolverSettings => "SOLVER SETTINGS",
        _ => "REVIEW & CONFIRM"
    };

    public string StepSubtitle => CurrentStep switch
    {
        StepSelectType => "Select Analysis Type",
        StepDefineOptions => PendingIntent switch
        {
            AnalysisIntent.Seepage => "Specify the physical and numerical options for seepage analysis.",
            AnalysisIntent.Consolidation => "Specify the physical and numerical options for consolidation analysis.",
            AnalysisIntent.Dynamic => "Specify the physical and numerical options for dynamic analysis.",
            AnalysisIntent.Thermal => "Specify the physical and numerical options for thermal analysis.",
            AnalysisIntent.Coupled => "Specify the physical and numerical options for coupled THM analysis.",
            AnalysisIntent.Srf => "Specify the physical and numerical options for strength-reduction stability.",
            _ => "Physics and numerical options for the selected type"
        },
        StepStageSettings => "Build the construction sequence",
        StepSolverSettings => "Existing solver controls",
        _ => "Confirm analysis type and staging"
    };

    public string InfoBarText => CurrentStep switch
    {
        StepSelectType =>
            "The analysis type defines the governing physics and equations used in the analysis. You can define multiple stages after selecting the analysis type.",
        StepDefineOptions =>
            "These options define the physics and numerical behavior of the analysis. You can modify them at any time before solving.",
        StepStageSettings =>
            "Stages are solved in the order listed above. You can insert, delete or reorder stages to build the construction sequence.",
        StepSolverSettings =>
            "Tolerance, iteration limit, and load increments write to the live solver control block.",
        _ =>
            "Finish writes the selected analysis type and staged-excavation flag. Cancel leaves intent unchanged."
    };

    public string PendingIntentDisplayName => PendingIntent switch
    {
        AnalysisIntent.Srf => "Stability (SSR)",
        AnalysisIntent.Coupled => "Coupled THM",
        AnalysisIntent.Seepage => "Seepage",
        AnalysisIntent.Consolidation => "Consolidation",
        AnalysisIntent.Dynamic => "Dynamic",
        AnalysisIntent.Thermal => "Thermal",
        AnalysisIntent.InitialStress => "Initial Conditions",
        _ => "Static"
    };

    public IAnalysisCapability PendingCapability => AnalysisCapabilityCatalog.Get(PendingIntent);
    public string CapabilityBanner =>
        PendingCapability.Status == AnalysisCapabilityStatus.Available
            ? ""
            : PendingCapability.UnavailableReason;
    public bool HasCapabilityBanner => !string.IsNullOrWhiteSpace(CapabilityBanner);
    public bool IsSrfPending => PendingIntent == AnalysisIntent.Srf;
    public bool IsConsolidationPending => PendingIntent == AnalysisIntent.Consolidation;
    public bool IsSeepagePending => PendingIntent == AnalysisIntent.Seepage;
    public bool IsDynamicPending => PendingIntent == AnalysisIntent.Dynamic;
    public bool IsThermalPending => PendingIntent == AnalysisIntent.Thermal;
    public bool IsCoupledPending => PendingIntent == AnalysisIntent.Coupled;
    public bool IsGenericDefineOptions =>
        !IsSeepagePending && !IsConsolidationPending && !IsDynamicPending
        && !IsThermalPending && !IsCoupledPending && !IsSrfPending;
    public bool ShowGenericDefineOptions => IsStepDefineOptions && IsGenericDefineOptions;
    public bool ShowSeepageDefineOptions => IsStepDefineOptions && IsSeepagePending;
    public bool ShowConsolidationDefineOptions => IsStepDefineOptions && IsConsolidationPending;
    public bool ShowDynamicDefineOptions => IsStepDefineOptions && IsDynamicPending;
    public bool ShowThermalDefineOptions => IsStepDefineOptions && IsThermalPending;
    public bool ShowCoupledDefineOptions => IsStepDefineOptions && IsCoupledPending;
    public bool ShowSrfDefineOptions => IsStepDefineOptions && IsSrfPending;
    public bool ShowTypedDefineOptions => IsStepDefineOptions && !IsGenericDefineOptions;
    public bool IsMaterialNonlinear =>
        MaterialPropertiesVM.SelectedFailureCriterion != FailureCriterion.Elastic;
    public string MaterialNonlinearityLabel =>
        IsMaterialNonlinear
            ? MaterialPropertiesVM.SelectedFailureCriterion.ToString()
            : "Linear (Elastic)";
    public string FeatureNotAvailable => AnalysisCapabilityCatalog.FeatureNotAvailable;
    public string ThermalUnavailableReason =>
        AnalysisCapabilityCatalog.Get(AnalysisIntent.Thermal).UnavailableReason;

    public string StatusUnits =>
        ProjectSettingsVM.Project.Units ?? ProjectUnitSystem.Si;
    public string StatusIdealization =>
        ProjectSettingsVM.IsAxisymmetricIdealization ? "2D Axisymmetric" : "2D Plane Strain";
    public string StatusDimension => StatusIdealization;
    public string StatusBarText =>
        $"Units: {StatusUnits}    Coordinate System: Global    Dimension: {StatusDimension}";

    public string ReviewSummary
    {
        get
        {
            var staged = PendingStaged ? "On" : "Off";
            return
                $"Analysis Type: {PendingIntentDisplayName}{Environment.NewLine}" +
                $"Capability: {(string.IsNullOrWhiteSpace(CapabilityBanner) ? "Available" : CapabilityBanner)}{Environment.NewLine}" +
                $"Staged excavation: {staged}{Environment.NewLine}" +
                $"Stages: {StageDefinitions.Count}{Environment.NewLine}" +
                Workspace.SettingsSummaryText;
        }
    }

    public string LoadsStageHint =>
        "Loads use Stage Number on the Loads workspace. Per-stage load sets are not in this engine.";
    public string BoundaryStageHint =>
        "Boundary conditions are not per-stage in this engine.";
    public string MaterialsStageHint =>
        "Material assignment is not per-stage in this engine.";

    public void Finish()
    {
        if (PendingIntent == AnalysisIntent.Seepage)
            ApplySeepageDefineToProject();
        else
        {
            Workspace.Settings.Intent = PendingIntent;
            Workspace.ApplySettingsCommand.Execute(null);
        }
        if (StageDefinitions.Count > 0)
            PendingStaged = true;
        FEAnalysisVM.StagedExcavationEnabled = PendingStaged;
        _host.SyncFooterWithStagedAnalysisGrid();
        CloseRequested?.Invoke(true);
    }

    /// <summary>
    /// Turns Flow-Seepage/Groundwater ON and writes Steady / Transient / Static from
    /// Flow Type + Hydraulic Head / Pore Pressure. Does not persist gated μ/ρ/Cw/conductance.
    /// </summary>
    public void ApplySeepageDefineToProject()
    {
        ProjectSettingsVM.SetCategoryOn(MaterialPropertiesViewModel.GroundwaterAnalysisCategory, true);

        AnalysisKind kind;
        if (IsSeepageStaticFlow)
        {
            kind = AnalysisKind.GroundwaterStatic;
            ProjectSettingsVM.SeepageSettings.SetAllIncludeWaterHead(false);
        }
        else if (IsSeepageTransientFlow)
        {
            kind = AnalysisKind.GroundwaterTransient;
            ProjectSettingsVM.SeepageSettings.SetAllIncludeWaterHead(true);
        }
        else
        {
            kind = AnalysisKind.GroundwaterSteady;
            ProjectSettingsVM.SeepageSettings.SetAllIncludeWaterHead(true);
        }

        AnalysisKindMapper.ApplyAnalysisKindToLegacyCategory(MaterialPropertiesVM, kind);
        Workspace.Settings.Intent = AnalysisIntent.Seepage;
        FEAnalysisVM.RefreshResolvedRunRoutingDisplay();
    }

    private void ApplySeepageFlowType(string? value)
    {
        var isTransient = string.Equals(
            value, AnalysisGatedOptionCatalog.SeepageFlowTypes[2], StringComparison.OrdinalIgnoreCase);
        var isStatic = string.Equals(
            value, AnalysisGatedOptionCatalog.SeepageFlowTypes[0], StringComparison.OrdinalIgnoreCase);
        if (isTransient)
        {
            AnalysisKindMapper.ApplyAnalysisKindToLegacyCategory(MaterialPropertiesVM, AnalysisKind.GroundwaterTransient);
            ProjectSettingsVM.SeepageSettings.SetAllIncludeWaterHead(true);
        }
        else if (isStatic)
        {
            AnalysisKindMapper.ApplyAnalysisKindToLegacyCategory(MaterialPropertiesVM, AnalysisKind.GroundwaterStatic);
            ProjectSettingsVM.SeepageSettings.SetAllIncludeWaterHead(false);
        }
        else
        {
            AnalysisKindMapper.ApplyAnalysisKindToLegacyCategory(MaterialPropertiesVM, AnalysisKind.GroundwaterSteady);
            ProjectSettingsVM.SeepageSettings.SetAllIncludeWaterHead(true);
        }

        NotifySeepageRouting();
    }

    private void EnsureSeepageStaticDefault()
    {
        if (!IsSeepageStaticFlow && !IsSeepageTransientFlow)
            ApplySeepageFlowType(AnalysisGatedOptionCatalog.SeepageFlowTypes[0]);
    }

    private void NotifySeepageRouting()
    {
        OnPropertyChanged(nameof(IsSeepageHydraulicHead));
        OnPropertyChanged(nameof(IsSeepagePorePressureFormulation));
        OnPropertyChanged(nameof(GatedSeepageFlowType));
        OnPropertyChanged(nameof(IncludeTimeDependency));
        OnPropertyChanged(nameof(IsSeepageTransientFlow));
        OnPropertyChanged(nameof(IsSeepageStaticFlow));
        OnPropertyChanged(nameof(ShowSeepageStaticMethod));
        OnPropertyChanged(nameof(ShowSeepageSteadyMethod));
        OnPropertyChanged(nameof(ShowSeepageTransientMethod));
    }

    private void OnNext()
    {
        if (IsLastStep)
        {
            Finish();
            return;
        }

        if (CurrentStep == StepSelectType && SelectedCardKey == StagesCardKey)
        {
            PendingStaged = true;
            CurrentStep = StepStageSettings;
            return;
        }

        CurrentStep++;
    }

    private void OnBack()
    {
        if (!CanGoBack) return;
        if (CurrentStep == StepStageSettings && SelectedCardKey == StagesCardKey)
        {
            CurrentStep = StepSelectType;
            return;
        }

        CurrentStep--;
    }

    private void OnSelectCard(AnalysisTypeCard? card)
    {
        if (card == null) return;
        SelectedCardKey = card.Key;
        if (card.IsStageConstruction)
        {
            PendingStaged = true;
            CurrentStep = StepStageSettings;
            return;
        }

        if (card.Intent is { } intent)
        {
            PendingIntent = intent;
            if (intent == AnalysisIntent.Seepage)
                EnsureSeepageStaticDefault();
        }
    }

    private void OnAddStage()
    {
        var stage = CreateDefaultStage(StageDefinitions.Count + 1);
        StageDefinitions.Add(stage);
        RenumberStages();
        SelectedStage = stage;
        SyncPendingStagedFromDefinitions();
    }

    private void OnInsertStage()
    {
        var index = SelectedStage == null ? StageDefinitions.Count : StageDefinitions.IndexOf(SelectedStage);
        if (index < 0) index = StageDefinitions.Count;
        var stage = CreateDefaultStage(index + 1);
        StageDefinitions.Insert(index, stage);
        RenumberStages();
        SelectedStage = stage;
        SyncPendingStagedFromDefinitions();
    }

    public StageDefinition DuplicateSelectedStage()
    {
        if (SelectedStage == null)
            throw new InvalidOperationException("No stage selected.");
        var copy = CloneStage(SelectedStage);
        var index = StageDefinitions.IndexOf(SelectedStage);
        StageDefinitions.Insert(index + 1, copy);
        RenumberStages();
        SelectedStage = copy;
        SyncPendingStagedFromDefinitions();
        return copy;
    }

    private void OnDuplicateStage() => DuplicateSelectedStage();

    private void OnRemoveStage()
    {
        if (SelectedStage == null) return;
        var index = StageDefinitions.IndexOf(SelectedStage);
        StageDefinitions.Remove(SelectedStage);
        RenumberStages();
        SelectedStage = StageDefinitions.Count == 0
            ? null
            : StageDefinitions[Math.Min(index, StageDefinitions.Count - 1)];
        SyncPendingStagedFromDefinitions();
    }

    private void OnMoveStageUp()
    {
        if (SelectedStage == null) return;
        var i = StageDefinitions.IndexOf(SelectedStage);
        if (i <= 0) return;
        StageDefinitions.Move(i, i - 1);
        RenumberStages();
        SyncPendingStagedFromDefinitions();
    }

    private void OnMoveStageDown()
    {
        if (SelectedStage == null) return;
        var i = StageDefinitions.IndexOf(SelectedStage);
        if (i < 0 || i >= StageDefinitions.Count - 1) return;
        StageDefinitions.Move(i, i + 1);
        RenumberStages();
        SyncPendingStagedFromDefinitions();
    }

    private void SyncPendingStagedFromDefinitions() =>
        PendingStaged = StageDefinitions.Count > 0;

    private bool CanMoveSelected(int delta)
    {
        if (SelectedStage == null) return false;
        var i = StageDefinitions.IndexOf(SelectedStage);
        var next = i + delta;
        return i >= 0 && next >= 0 && next < StageDefinitions.Count;
    }

    private void RenumberStages()
    {
        for (var i = 0; i < StageDefinitions.Count; i++)
            StageDefinitions[i].StageNumber = i + 1;
    }

    private static StageDefinition CreateDefaultStage(int number) => new()
    {
        StageNumber = number,
        StageName = number == 1 ? "Initial Stress" : $"Stage {number}",
        StageType = number == 1 ? "Initial Stress" : "Excavation",
        SolverMode = number == 1 ? "Elastic" : "Plastic",
        LoadFactor = 1.0,
        IsActive = true
    };

    public static StageDefinition CloneStage(StageDefinition source)
    {
        var copy = new StageDefinition
        {
            StageNumber = source.StageNumber,
            StageName = string.IsNullOrWhiteSpace(source.StageName)
                ? "Stage copy"
                : source.StageName + " copy",
            StageType = source.StageType,
            Description = source.Description,
            IsActive = source.IsActive,
            Duration = source.Duration,
            Units = source.Units,
            LoadFactor = source.LoadFactor,
            Notes = source.Notes,
            LiningEnabled = source.LiningEnabled,
            BoltsEnabled = source.BoltsEnabled,
            GroundwaterLineTag = source.GroundwaterLineTag,
            MaxIterations = source.MaxIterations,
            Tolerance = source.Tolerance,
            MaxLoadSteps = source.MaxLoadSteps,
            SolverMode = source.SolverMode,
            StressReleaseLambda = source.StressReleaseLambda,
            ResetDisplacementsAtStart = source.ResetDisplacementsAtStart,
            ArcLength = source.ArcLength,
            PseudoStaticKh = source.PseudoStaticKh,
            PseudoStaticKv = source.PseudoStaticKv
        };
        foreach (var n in source.ActivateSurfaceNumbers)
            copy.ActivateSurfaceNumbers.Add(n);
        foreach (var n in source.DeactivateSurfaceNumbers)
            copy.DeactivateSurfaceNumbers.Add(n);
        return copy;
    }

    private void OnSurfaceRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StageSurfaceActivityRow.IsActive)
            && sender is StageSurfaceActivityRow row)
            ApplySurfaceActivity(row);
    }

    private void RebuildSurfaceRows()
    {
        foreach (var existing in SurfaceRows)
            existing.PropertyChanged -= OnSurfaceRowChanged;
        SurfaceRows.Clear();
        foreach (var surface in _host.GeometryDefinitionVM.GeometrySurfaces)
        {
            var active = SelectedStage == null
                || !SelectedStage.DeactivateSurfaceNumbers.Contains(surface.SurfaceNumber);
                var row = new StageSurfaceActivityRow(surface.SurfaceNumber, surface.SurfaceDisplay, active);
            row.PropertyChanged += OnSurfaceRowChanged;
            SurfaceRows.Add(row);
        }
        OnPropertyChanged(nameof(SurfaceRows));
    }

    private void OnToggleSurfaceActive(StageSurfaceActivityRow? row)
    {
        if (row == null || SelectedStage == null) return;
        row.IsActive = !row.IsActive;
        ApplySurfaceActivity(row);
    }

    public void ApplySurfaceActivity(StageSurfaceActivityRow row)
    {
        if (SelectedStage == null) return;
        SelectedStage.ActivateSurfaceNumbers.Remove(row.SurfaceNumber);
        SelectedStage.DeactivateSurfaceNumbers.Remove(row.SurfaceNumber);
        if (row.IsActive)
            SelectedStage.ActivateSurfaceNumbers.Add(row.SurfaceNumber);
        else
            SelectedStage.DeactivateSurfaceNumbers.Add(row.SurfaceNumber);
    }

    private void SyncCardSelection()
    {
        foreach (var card in Cards)
            card.IsSelected = card.Key == SelectedCardKey;
    }

    private void NotifyStep()
    {
        OnPropertyChanged(nameof(IsStepSelectType));
        OnPropertyChanged(nameof(IsStepDefineOptions));
        OnPropertyChanged(nameof(IsStepStageSettings));
        OnPropertyChanged(nameof(IsStepSolverSettings));
        OnPropertyChanged(nameof(IsStepReview));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepSubtitle));
        OnPropertyChanged(nameof(InfoBarText));
        OnPropertyChanged(nameof(ReviewSummary));
        NotifyDefineOptionHosts();
    }

    private void OnFieldStressChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FieldStressSpec.UseEffectiveStressRatio))
        {
            OnPropertyChanged(nameof(SelectedFormulation));
            OnPropertyChanged(nameof(IsConsolidationPorePressureFormulation));
            OnPropertyChanged(nameof(IsConsolidationTotalStressFormulation));
        }
        if (e.PropertyName is nameof(FieldStressSpec.Mode))
            OnPropertyChanged(nameof(SelectedInitialStressMethod));
    }

    private void NotifyPending()
    {
        OnPropertyChanged(nameof(PendingIntentDisplayName));
        OnPropertyChanged(nameof(SelectedPhysicsType));
        OnPropertyChanged(nameof(PendingCapability));
        OnPropertyChanged(nameof(CapabilityBanner));
        OnPropertyChanged(nameof(HasCapabilityBanner));
        OnPropertyChanged(nameof(IsSrfPending));
        OnPropertyChanged(nameof(IsConsolidationPending));
        OnPropertyChanged(nameof(IsSeepagePending));
        OnPropertyChanged(nameof(IsDynamicPending));
        OnPropertyChanged(nameof(IsThermalPending));
        OnPropertyChanged(nameof(IsCoupledPending));
        OnPropertyChanged(nameof(IsGenericDefineOptions));
        OnPropertyChanged(nameof(ReviewSummary));
        NotifyDefineOptionHosts();
    }

    private void NotifyDefineOptionHosts()
    {
        OnPropertyChanged(nameof(ShowGenericDefineOptions));
        OnPropertyChanged(nameof(ShowSeepageDefineOptions));
        OnPropertyChanged(nameof(ShowConsolidationDefineOptions));
        OnPropertyChanged(nameof(ShowDynamicDefineOptions));
        OnPropertyChanged(nameof(ShowThermalDefineOptions));
        OnPropertyChanged(nameof(ShowCoupledDefineOptions));
        OnPropertyChanged(nameof(ShowSrfDefineOptions));
        OnPropertyChanged(nameof(ShowTypedDefineOptions));
        OnPropertyChanged(nameof(IsSeepageHydraulicHead));
        OnPropertyChanged(nameof(IsSeepagePorePressureFormulation));
        OnPropertyChanged(nameof(ShowSeepageStaticMethod));
        OnPropertyChanged(nameof(ShowSeepageSteadyMethod));
        OnPropertyChanged(nameof(ShowSeepageTransientMethod));
        OnPropertyChanged(nameof(IsLimitEquilibriumSelected));
        OnPropertyChanged(nameof(IsLocalSsrSelected));
    }

    private static AnalysisIntent MapInitialIntent(AnalysisIntent current) =>
        current == AnalysisIntent.InitialStress ? AnalysisIntent.Static : current;

    private static string CardKeyForIntent(AnalysisIntent intent) => intent switch
    {
        AnalysisIntent.Seepage => "Seepage",
        AnalysisIntent.Consolidation => "Consolidation",
        AnalysisIntent.Dynamic => "Dynamic",
        AnalysisIntent.Thermal => "Thermal",
        AnalysisIntent.Coupled => "Coupled",
        AnalysisIntent.Srf => "Srf",
        _ => "Static"
    };

    private List<AnalysisTypeCard> BuildCards() =>
    [
        new("Static", "Static",
            "Performs a static stress/deformation analysis under constant loading.",
            AnalysisIntent.Static),
        new("Seepage", "Seepage",
            "Computes steady-state or transient seepage through the model.",
            AnalysisIntent.Seepage),
        new("Consolidation", "Consolidation",
            "Performs time-dependent consolidation analysis of saturated soils.",
            AnalysisIntent.Consolidation),
        new("Dynamic", "Dynamic",
            "Performs dynamic time history analysis for seismic or other dynamic loading.",
            AnalysisIntent.Dynamic),
        new("Thermal", "Thermal",
            "Performs heat transfer analysis for steady-state or time-dependent conditions.",
            AnalysisIntent.Thermal),
        new("Coupled", "Coupled THM",
            "Performs coupled thermo-hydro-mechanical analysis.",
            AnalysisIntent.Coupled),
        new("Srf", "Stability (SSR)",
            "Performs slope stability analysis using Strength Reduction Technique.",
            AnalysisIntent.Srf),
        new("Stages", "Stage Construction",
            "Performs staged construction analysis to simulate construction sequence effects.",
            null)
    ];

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AnalysisTypeCard : INotifyPropertyChanged
{
    private bool _isSelected;

    public AnalysisTypeCard(string key, string title, string description, AnalysisIntent? intent)
    {
        Key = key;
        Title = title;
        Description = description;
        Intent = intent;
    }

    public string Key { get; }
    public string Title { get; }
    public string Description { get; }
    public AnalysisIntent? Intent { get; }
    public bool IsStageConstruction => Key == AnalysisTypeWizardViewModel.StagesCardKey;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class StageSurfaceActivityRow : INotifyPropertyChanged
{
    private bool _isActive;

    public StageSurfaceActivityRow(int surfaceNumber, string display, bool isActive)
    {
        SurfaceNumber = surfaceNumber;
        Display = display;
        _isActive = isActive;
    }

    public int SurfaceNumber { get; }
    public string Display { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
