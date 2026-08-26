using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Threading;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.Services;
using OxyPlot;

namespace GeotechSlopeApp.ViewModels;

public class StageResult
{
    public int StageNumber { get; set; }
    public IReadOnlyList<int> RemovedElementIds { get; set; } = new List<int>();
}

public class StagedAnalysisResult
{
    public IReadOnlyList<StageResult> StageResults { get; set; } = new List<StageResult>();
}

public sealed class FePseudoStaticForceRow
{
    public int NodeId { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Fx { get; init; }
    public double Fy { get; init; }
    public double Magnitude => Math.Sqrt(Fx * Fx + Fy * Fy);
}

public sealed class SrfTrialDisplayRow
{
    public double Srf { get; init; }
    public double MaxDisplacement { get; init; }
    public int Iterations { get; init; }
    public bool Failed { get; init; }
    public string Status => Failed ? "Failed (limit)" : "Converged";
}

public partial class ResultsViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<Node> _feNodes = new List<Node>();
    private IReadOnlyList<ElementBase> _feElements = new List<ElementBase>();
    private IReadOnlyList<object> _feNodalDisplacements = new List<object>();
    private IReadOnlyList<ElementStress> _feElementStresses = new List<ElementStress>();
    private IReadOnlyList<P610T3JointResult> _jointResults = Array.Empty<P610T3JointResult>();
    private StagedAnalysisResult? _stagedAnalysisResult;
    private StageResult? _selectedStageResult;
    private string _vectorOutputType = "Displacement Vectors";
    private string _vectorDisplacementComponent = "Both (Ux + Uy)";
    private double _vectorScale = 1.0;
    private double _deformedMeshExaggeration = 50.0;
    private bool _showUndeformedOutline = true;
    private bool _showMesh = true;
    private bool _isLinePlotMode;
    private PlotModel? _linePlotModel;
    private string _linePlotHint = "";
    private string _selectedOutputType = "Displacement Magnitude";
    private string _visualizationMode = "Contour";
    private string _contourFillMode = ContourFillElement;
    private string _feStatusMessage = "Run FE Analysis to view results.";
    private ProblemDimension _projectDimension = ProblemDimension.TwoD;
    private P610T3RunResult? _p610RunResult;
    private SeepageRunResult? _seepageRunResult;
    private TransientRunResult? _transientRunResult;
    private Dictionary<int, SeepageNodeResult> _seepageNodesById = new();
    private IReadOnlyList<(double X, double Y)> _seepageOriginalDomainOutline =
        Array.Empty<(double X, double Y)>();
    private IReadOnlyList<object> _availableStages = new List<object>();
    private object? _selectedStageInfo;
    private bool _suppressFooterStageSelectionRequest;
    private bool _suppressTransientIndexSync;
    private string _stageSummary = "";
    private IReadOnlyList<Node> _constructionBaseNodes = new List<Node>();
    private IReadOnlyList<ElementBase> _constructionBaseElements = new List<ElementBase>();
    private List<ConstructionStageResultsSnapshot>? _constructionStageSnapshots;
    private string _femCppArtifactSummary = "";
    private bool _feMeshSmithEmb2dQ8LocalOrder;
    private MeshViewModel? _feMeshGeometrySource;
    private ProjectSettingsViewModel? _projectSettings;
    private double _pseudoStaticKh;
    private double _pseudoStaticKv;
    private IReadOnlyList<FePseudoStaticForceRow> _fePseudoStaticNodalForces = Array.Empty<FePseudoStaticForceRow>();
    private bool _showPseudoStaticForceVectors = true;
    private bool _showPhreaticOnDeformedMesh = true;
    private IReadOnlyList<(double X, double Y)> _phreaticSurfaceAxisPoints = Array.Empty<(double, double)>();
    private PlotModel? _srfConvergencePlotModel;
    private PlotModel? _transientTimeHistoryPlotModel;
    private int _transientTimeIndex;
    private bool _isTransientPlaying;
    private DispatcherTimer? _transientPlaybackTimer;
    private IReadOnlyList<SrfTrialDisplayRow> _srfTrialRows = Array.Empty<SrfTrialDisplayRow>();
    private string _srfConvergenceStatus = "Run a multi-SRF slope analysis to view SRF vs displacement.";
    private double? _estimatedSrfFoS;
    private FeNewmarkResult? _newmarkResult;
    private string _srfFoSNote = "";
    private int _srfIterationLimit = 1000;
    private IReadOnlyList<P64ResFileParser.SrfTrialRow> _p64SrfTrials = Array.Empty<P64ResFileParser.SrfTrialRow>();
    private Dictionary<int, (int FixX, int FixY)> _nodeFixity = new();
    private static readonly Regex SrfStageLabelRegex = new(
        @"srf[_\s-]*([\d.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private string _elasticForceDisplacementStatus =
        "Elastic foundation curve: enter node id and load steps, then Generate Curve.";
    private PlotModel? _elasticForceDisplacementPlotModel = null;

    public ResultsViewModel()
    {
        GenerateForceDisplacementCurveCommand = new RelayCommand(
            OnGenerateElasticForceDisplacementCurve,
            () => ShowElasticForceDisplacementControls);
        PlayTransientCommand = new RelayCommand(OnPlayTransient, () => HasTransientResults && !IsTransientPlaying);
        PauseTransientCommand = new RelayCommand(OnPauseTransient, () => HasTransientResults && IsTransientPlaying);
        StepTransientBackwardCommand = new RelayCommand(
            () => TransientTimeIndex = Math.Max(0, TransientTimeIndex - 1),
            () => HasTransientResults && TransientTimeIndex > 0);
        StepTransientForwardCommand = new RelayCommand(
            () => TransientTimeIndex = Math.Min(TransientFrameCount - 1, TransientTimeIndex + 1),
            () => HasTransientResults && TransientTimeIndex < TransientFrameCount - 1);
        JumpTransientToStartCommand = new RelayCommand(
            () => TransientTimeIndex = 0,
            () => HasTransientResults);
        JumpTransientToEndCommand = new RelayCommand(
            () => TransientTimeIndex = Math.Max(0, TransientFrameCount - 1),
            () => HasTransientResults);
    }

    private void OnGenerateElasticForceDisplacementCurve()
    {
        _elasticForceDisplacementStatus =
            "Elastic foundation force–displacement curve is not implemented. Run a multi-SRF slope analysis to populate the SRF vs displacement plot.";
        OnPropertyChanged(nameof(ForceDisplacementStatus));
    }

    public bool ShowPhreaticOnDeformedMesh
    {
        get => _showPhreaticOnDeformedMesh;
        set { _showPhreaticOnDeformedMesh = value; OnPropertyChanged(); }
    }

    /// <summary>RESULTS workspace Plot Options → Show Legend. Also gates the color-scale bar.</summary>
    public bool ShowContourLegend
    {
        get => _showContourLegend;
        set { if (_showContourLegend == value) return; _showContourLegend = value; OnPropertyChanged(); }
    }
    private bool _showContourLegend = true;

    public IReadOnlyList<(double X, double Y)> PhreaticSurfaceAxisPoints
    {
        get => _phreaticSurfaceAxisPoints;
        private set { _phreaticSurfaceAxisPoints = value; OnPropertyChanged(); }
    }

    public void ApplyGroundwaterPhreaticFromDat(string? datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent)
            || !GroundwaterDatImporter.TryImportFromDat(datContent, out var model)
            || !model.IsEnabled
            || model.SurfPoints.Count == 0)
        {
            PhreaticSurfaceAxisPoints = Array.Empty<(double, double)>();
            return;
        }

        PhreaticSurfaceAxisPoints = GroundwaterDatImporter.ToDisplaySurfPoints(model.SurfPoints, datContent);
    }

    public const string VisualizationModeContour2D = "Contour";
    public const string VisualizationModeContour1D = "1D Contour";
    public const string VisualizationModeVector = "Vector";
    public const string VisualizationModeDeformedMesh = "Deformed mesh";

    public const string ContourFillElement = "Element coloring";
    public const string ContourFillSmoothBands = "Smooth bands (interpolated)";

    public IReadOnlyList<string> AvailableContourFillModes { get; } =
        new[] { ContourFillElement, ContourFillSmoothBands };

    /// <summary>Synced from Project Settings → Analysis Type → Dimension.</summary>
    public ProblemDimension ProjectDimension
    {
        get => _projectDimension;
        set
        {
            if (_projectDimension == value) return;
            _projectDimension = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvailableVisualizationModes));
            OnPropertyChanged(nameof(IsOneDimensionalProject));
            if (IsOneDimensionalProject)
            {
                if (_visualizationMode == VisualizationModeContour2D)
                    VisualizationMode = VisualizationModeContour1D;
            }
            else if (_visualizationMode == VisualizationModeContour1D)
            {
                VisualizationMode = VisualizationModeContour2D;
            }
            else if (_visualizationMode == VisualizationModeDeformedMesh)
            {
                VisualizationMode = VisualizationModeContour1D;
            }
        }
    }

    public bool IsOneDimensionalProject => _projectDimension == ProblemDimension.OneD;

    /// <summary>FE contour vs vector visualization (bound by FEContourVisualizationView).</summary>
    public string VisualizationMode
    {
        get => _visualizationMode;
        set
        {
            if (_visualizationMode == value) return;
            _visualizationMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsContourLikeMode));
            OnPropertyChanged(nameof(IsDeformedMeshMode));
            OnPropertyChanged(nameof(IsVectorMode));
            OnPropertyChanged(nameof(UsesMeshDeformationExaggeration));
            if (value != VisualizationModeContour2D && _isLinePlotMode)
                IsLinePlotMode = false;
            // Project files without VectorScale deserialize as 0 — arrows become invisible.
            if (string.Equals(value, VisualizationModeVector, StringComparison.OrdinalIgnoreCase)
                && _vectorScale < 0.1)
                VectorScale = 1.0;
        }
    }

    /// <summary>True for 2D triangle contour or 1D rod contour (show output type + color legend).</summary>
    public bool IsContourLikeMode =>
        string.Equals(_visualizationMode, VisualizationModeContour2D, System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(_visualizationMode, VisualizationModeContour1D, System.StringComparison.OrdinalIgnoreCase);

    public bool IsDeformedMeshMode =>
        string.Equals(_visualizationMode, VisualizationModeDeformedMesh, System.StringComparison.OrdinalIgnoreCase);

    public bool IsVectorMode =>
        string.Equals(_visualizationMode, VisualizationModeVector, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>2D contour fill style: flat element colors vs nodally interpolated bands.</summary>
    public string ContourFillMode
    {
        get => _contourFillMode;
        set
        {
            if (_contourFillMode == value) return;
            _contourFillMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UsesSmoothContourBands));
        }
    }

    public bool UsesSmoothContourBands =>
        string.Equals(_contourFillMode, ContourFillSmoothBands, StringComparison.OrdinalIgnoreCase);

    /// <summary>Exaggeration applied to mesh wireframe (and vector bases) in Vector / Deformed mesh modes.</summary>
    public bool UsesMeshDeformationExaggeration => IsVectorMode || IsDeformedMeshMode;

    public IReadOnlyList<string> AvailableVisualizationModes =>
        IsOneDimensionalProject
            ? new[] { VisualizationModeContour1D, VisualizationModeVector }
            : new[] { VisualizationModeContour2D, VisualizationModeVector, VisualizationModeDeformedMesh };

    /// <summary>Scalar field options for contour plot (must match FEContourCanvasView parsing).</summary>
    public const string OutputDisplacementMagnitude = "Displacement Magnitude";
    public const string OutputDisplacementX = "Displacement X (u)";
    public const string OutputDisplacementY = "Displacement Y (v)";
    public const string OutputRs2TotalDisplacementMm = "RS2 Total Displacement |u| (mm)";
    public const string OutputRadialInwardDisplacementMm = "Radial Displacement uᵣ inward (mm, tunnel ref)";
    public const string OutputStressXX = "Stress XX (σₓₓ)";
    public const string OutputStressYY = "Stress YY (σᵧᵧ)";
    public const string OutputStressXY = "Stress XY (τₓᵧ)";
    public const string OutputPrincipalMax = "Principal Stress Max (σ₁)";
    public const string OutputPrincipalMin = "Principal Stress Min (σ₂)";
    public const string OutputMajorPrincipalCompMpA = "RS2 Major Principal σ₁ (MPa, comp+)";
    public const string OutputHoopStressCompMpA = "Tangential / Hoop Stress σθ (MPa, comp+)";
    public const string OutputRadialStressCompMpA = "Radial Stress σr (MPa, comp+)";
    public const string OutputVonMises = "Von Mises Stress";
    public const string OutputPorePressure = "Pore pressure u (kPa)";
    public const string OutputWaterPressureGridTotalHead = "Water pressure grid — Total Head (m)";
    public const string OutputWaterPressureGridPressureHead = "Water pressure grid — Pressure Head (m)";
    public const string OutputWaterPressureGridPorePressure = "Water pressure grid — Pore Pressure (kPa)";
    /// <summary>Nodal outputs of a seepage run (p72/p73/p74).</summary>
    public const string OutputSeepageTotalHead = "Total Head (m)";
    public const string OutputSeepagePressureHead = "Pressure Head (m)";
    /// <summary>Net nodal discharge (reaction flow K·h); sums to global inflow/outflow.</summary>
    public const string OutputSeepageDischarge = "Discharge (m³/s per m)";
    /// <summary>Nodal excess pore pressure from transient consolidation (p84/p88).</summary>
    public const string OutputTransientExcessPressure = "Excess Pore Pressure";
    public const string OutputEffectiveStressXX = "Effective stress σ'xx (kPa)";
    public const string OutputEffectiveStressYY = "Effective stress σ'yy (kPa)";
    public const string OutputEffectiveStressXY = "Effective stress σ'xy (kPa)";
    public const string OutputEffectivePrincipalMax = "Effective principal max (σ'₁)";
    public const string OutputEffectivePrincipalMin = "Effective principal min (σ'₂)";
    public const string OutputYieldRatio = "Yield ratio (shear zone)";
    public const string OutputPlasticStrainNorm = "Plastic strain norm (shear zone)";
    public const string OutputStrainXX = "Strain XX (εxx)";
    public const string OutputStrainYY = "Strain YY (εyy)";
    public const string OutputShearStrain = "Shear Strain (γxy)";
    public const string OutputMaximumShearStrain = "Maximum Shear Strain (γmax)";
    public const string OutputMaximumShearStrainPeakGp = "Maximum Shear Strain (γmax, peak GP)";
    public const string OutputJointNormalStress = "Joint Normal Stress (σn)";
    public const string OutputJointShearStress = "Joint Shear Stress (τ)";
    public const string OutputJointOpening = "Joint Opening (un)";
    public const string OutputJointClosure = "Joint Closure";
    public const string OutputJointSlip = "Joint Slip (us)";

    public IReadOnlyList<string> AvailableOutputTypes => BuildAvailableOutputTypes();

    private IReadOnlyList<string> BuildAvailableOutputTypes()
    {
        if (_transientRunResult != null)
            return new List<string> { OutputTransientExcessPressure };

        // A seepage run (p72/p73/p74) exposes nodal heads and net nodal discharge.
        if (_seepageRunResult != null)
            return new List<string>
            {
                OutputSeepageTotalHead,
                OutputSeepagePressureHead,
                OutputSeepageDischarge,
            };

        var list = new List<string>
        {
            OutputDisplacementMagnitude,
            OutputDisplacementX,
            OutputDisplacementY,
            OutputRs2TotalDisplacementMm,
            OutputRadialInwardDisplacementMm,
            OutputStressXX,
            OutputStressYY,
            OutputStressXY,
            OutputPrincipalMax,
            OutputPrincipalMin,
            OutputStrainXX,
            OutputStrainYY,
            OutputShearStrain,
            OutputMaximumShearStrain,
            OutputMaximumShearStrainPeakGp,
        };

        // Elemental hydrostatic / Ru pore pressure (u = γw·h or Ru·σ) is always available
        // after an FE/SRF run with groundwater — do not hide it behind water-pressure-grid flags.
        list.Add(OutputPorePressure);

        var staticOpen = _projectSettings?.IsGroundwaterStaticSelected == true;
        var gwStatic = _projectSettings?.GroundwaterStaticSettings;
        if (staticOpen && gwStatic != null)
        {
            if (gwStatic.IncludeWaterPressureGridTotalHead)
                list.Add(OutputWaterPressureGridTotalHead);
            if (gwStatic.IncludeWaterPressureGridPressureHead)
                list.Add(OutputWaterPressureGridPressureHead);
            if (gwStatic.IncludeWaterPressureGridPorePressure)
                list.Add(OutputWaterPressureGridPorePressure);
        }

        list.AddRange(new[]
        {
            OutputEffectiveStressXX,
            OutputEffectiveStressYY,
            OutputEffectiveStressXY,
            OutputEffectivePrincipalMax,
            OutputEffectivePrincipalMin,
            OutputYieldRatio,
            OutputPlasticStrainNorm,
            OutputMajorPrincipalCompMpA,
            OutputHoopStressCompMpA,
            OutputRadialStressCompMpA,
            OutputVonMises
        });

        if (HasJointResults)
        {
            list.Add(OutputJointNormalStress);
            list.Add(OutputJointShearStress);
            list.Add(OutputJointOpening);
            list.Add(OutputJointClosure);
            list.Add(OutputJointSlip);
        }

        return list;
    }

    public void SetProjectSettingsViewModel(ProjectSettingsViewModel? projectSettings)
    {
        if (_projectSettings != null)
        {
            _projectSettings.PropertyChanged -= OnProjectSettingsPropertyChanged;
            _projectSettings.GroundwaterStaticSettings.PropertyChanged -= OnGroundwaterStaticSettingsPropertyChanged;
            _projectSettings.SeepageSettings.PropertyChanged -= OnSeepageSettingsPropertyChanged;
        }

        _projectSettings = projectSettings;

        if (_projectSettings != null)
        {
            _projectSettings.PropertyChanged += OnProjectSettingsPropertyChanged;
            _projectSettings.GroundwaterStaticSettings.PropertyChanged += OnGroundwaterStaticSettingsPropertyChanged;
            _projectSettings.SeepageSettings.PropertyChanged += OnSeepageSettingsPropertyChanged;
        }

        RefreshAvailableOutputTypes();
        OnPropertyChanged(nameof(ShowAxisOfSymmetry));
        NotifyTransientOutputUiAvailability();
    }

    /// <summary>True when Project Settings seepage Axisymmetric is on — FE canvases draw r = 0.</summary>
    public bool ShowAxisOfSymmetry => _projectSettings?.SeepageSettings.Axisymmetric == true || _projectSettings?.SrfAxisymmetric == true;

    private void OnProjectSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectSettingsViewModel.IsGroundwaterStaticSelected))
            RefreshAvailableOutputTypes();
        if (e.PropertyName is null or "" or nameof(ProjectSettingsViewModel.SrfAxisymmetric))
            OnPropertyChanged(nameof(ShowAxisOfSymmetry));
        if (e.PropertyName is null or ""
            or nameof(ProjectSettingsViewModel.IsTransientP84Selected)
            or nameof(ProjectSettingsViewModel.IsTransientP88Selected)
            or nameof(ProjectSettingsViewModel.IsTransientP84Checked)
            or nameof(ProjectSettingsViewModel.IsTransientP88Checked)
            or nameof(ProjectSettingsViewModel.IsTransientAnalysisSelected)
            or nameof(ProjectSettingsViewModel.SelectedAnalysisCategoryNode))
            NotifyTransientOutputUiAvailability();
    }

    private void NotifyTransientOutputUiAvailability()
    {
        OnPropertyChanged(nameof(IsTransientAnalysisSelected));
        OnPropertyChanged(nameof(ShowTransientOutputSettings));
        OnPropertyChanged(nameof(TransientSettings));
        OnPropertyChanged(nameof(ShowElasticForceDisplacementControls));
        OnPropertyChanged(nameof(ShowSrfForceDisplacementPanel));
    }

    private void OnGroundwaterStaticSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RefreshAvailableOutputTypes();

    private void OnSeepageSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(SeepageAnalysisSettings.Axisymmetric))
            OnPropertyChanged(nameof(ShowAxisOfSymmetry));
    }

    private void RefreshAvailableOutputTypes()
    {
        OnPropertyChanged(nameof(AvailableOutputTypes));
        if (!AvailableOutputTypes.Contains(SelectedOutputType))
            SelectedOutputType = AvailableOutputTypes.FirstOrDefault() ?? OutputDisplacementMagnitude;
    }

    /// <summary>Vector plot options (must match FEVectorCanvasView).</summary>
    public IReadOnlyList<string> AvailableVectorOutputTypes { get; } =
        new[]
        {
            "Displacement Vectors",
            "Pseudo-static Force Vectors",
            "Principal Stress Directions",
            "Stress Vectors"
        };

    /// <summary>
    /// Which displacement component to visualise. "Horizontal (Ux only)" reveals SRF slope-failure
    /// patterns that are hidden when gravity-settlement Uy dominates the global normalization.
    /// </summary>
    public IReadOnlyList<string> AvailableVectorDisplacementComponents { get; } =
        new[]
        {
            "Both (Ux + Uy)",
            "Horizontal (Ux only)",
            "Vertical (Uy only)"
        };

    public IReadOnlyList<Node> FENodes
    {
        get => _feNodes;
        set { _feNodes = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Q8 elements from Smith <c>emb_2d_geom</c> / <c>fem_cpp p64</c> use corner,midside,corner,… local order.
    /// Contour fills and wireframes must use corners at local indices 0,2,4,6 — not 0,1,2,3.
    /// </summary>
    public bool FeMeshSmithEmb2dQ8LocalOrder
    {
        get => _feMeshSmithEmb2dQ8LocalOrder;
        private set
        {
            if (_feMeshSmithEmb2dQ8LocalOrder == value) return;
            _feMeshSmithEmb2dQ8LocalOrder = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<ElementBase> FEElements
    {
        get => _feElements;
        set { _feElements = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Original p73 model-domain outline before the free-surface solver moves the
    /// computational mesh. The contour view uses it to show the omitted unsaturated region.
    /// </summary>
    public IReadOnlyList<(double X, double Y)> SeepageOriginalDomainOutline
    {
        get => _seepageOriginalDomainOutline;
        private set
        {
            _seepageOriginalDomainOutline = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<object> FeNodalDisplacements
    {
        get => _feNodalDisplacements;
        set { _feNodalDisplacements = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<ElementStress> FeElementStresses
    {
        get => _feElementStresses;
        set
        {
            _feElementStresses = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasFeElementStresses));
        }
    }

    /// <summary>True when at least one element has non-zero stress components for vector/contour plots.</summary>
    public bool HasFeElementStresses =>
        FeElementStresses?.Any(s =>
            Math.Abs(s.StressXX) > 1e-10
            || Math.Abs(s.StressYY) > 1e-10
            || Math.Abs(s.StressXY) > 1e-10
            || Math.Abs(s.PrincipalStressMax) > 1e-10
            || Math.Abs(s.PrincipalStressMin) > 1e-10) == true;

    /// <summary>Goodman interface results for the selected stage (empty when the run has no joints).</summary>
    public IReadOnlyList<P610T3JointResult> JointResults
    {
        get => _jointResults;
        set
        {
            _jointResults = value ?? Array.Empty<P610T3JointResult>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasJointResults));
        }
    }

    public bool HasJointResults =>
        _jointResults.Count > 0
        || (_p610RunResult?.Joints.Count > 0)
        || (_p610RunResult?.Stages.Any(s => s.Joints.Count > 0) == true);

    public double PseudoStaticKh
    {
        get => _pseudoStaticKh;
        private set { _pseudoStaticKh = value; OnPropertyChanged(); }
    }

    public double PseudoStaticKv
    {
        get => _pseudoStaticKv;
        private set { _pseudoStaticKv = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<FePseudoStaticForceRow> FePseudoStaticNodalForces
    {
        get => _fePseudoStaticNodalForces;
        private set { _fePseudoStaticNodalForces = value; OnPropertyChanged(); }
    }

    public bool HasPseudoStaticResults =>
        Math.Abs(_pseudoStaticKh) > 1e-12 || Math.Abs(_pseudoStaticKv) > 1e-12;

    public double PseudoStaticMaxForceMagnitude =>
        FePseudoStaticNodalForces.Count == 0
            ? 0.0
            : FePseudoStaticNodalForces.Max(r => r.Magnitude);

    public string PseudoStaticConventionNote =>
        "Horizontal inertia +x (k_h×γ×weight); vertical load uses (1−k_v)×γ. Table shows pseudo-static components only.";

    public bool ShowPseudoStaticForceVectors
    {
        get => _showPseudoStaticForceVectors;
        set { _showPseudoStaticForceVectors = value; OnPropertyChanged(); }
    }

    public StagedAnalysisResult? StagedAnalysisResult
    {
        get => _stagedAnalysisResult;
        set
        {
            if (ReferenceEquals(_stagedAnalysisResult, value)) return;
            _stagedAnalysisResult = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStagedResults));
        }
    }

    public StageResult? SelectedStageResult
    {
        get => _selectedStageResult;
        set { _selectedStageResult = value; OnPropertyChanged(); }
    }

    public string VectorOutputType
    {
        get => _vectorOutputType;
        set { _vectorOutputType = value; OnPropertyChanged(); }
    }

    /// <summary>Active displacement component filter for the vector canvas.</summary>
    public string VectorDisplacementComponent
    {
        get => _vectorDisplacementComponent;
        set { _vectorDisplacementComponent = value ?? "Both (Ux + Uy)"; OnPropertyChanged(); }
    }

    public double VectorScale
    {
        get => _vectorScale;
        set
        {
            // Slider Minimum is 0.1; keep the bound value in range so arrows never draw at length 0.
            double clamped = value < 0.1 ? 0.1 : (value > 10.0 ? 10.0 : value);
            if (Math.Abs(_vectorScale - clamped) < 1e-12) return;
            _vectorScale = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>Displacement exaggeration factor for deformed-mesh wireframe (Smith dismsh style).</summary>
    public double DeformedMeshExaggeration
    {
        get => _deformedMeshExaggeration;
        set { _deformedMeshExaggeration = value; OnPropertyChanged(); }
    }

    public bool ShowUndeformedOutline
    {
        get => _showUndeformedOutline;
        set { _showUndeformedOutline = value; OnPropertyChanged(); }
    }

    public bool ShowMesh
    {
        get => _showMesh;
        set { _showMesh = value; OnPropertyChanged(); }
    }

    public bool IsLinePlotMode
    {
        get => _isLinePlotMode;
        set
        {
            if (_isLinePlotMode == value) return;
            _isLinePlotMode = value;
            OnPropertyChanged();
            if (!value)
            {
                LinePlotModel = null;
                LinePlotHint = "";
            }
            else
            {
                LinePlotHint = "Click first point on mesh";
            }
        }
    }

    public PlotModel? LinePlotModel
    {
        get => _linePlotModel;
        set { _linePlotModel = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLinePlot)); }
    }

    public bool HasLinePlot => _linePlotModel != null;

    public string LinePlotHint
    {
        get => _linePlotHint;
        set { _linePlotHint = value; OnPropertyChanged(); }
    }

    public string SelectedOutputType
    {
        get => _selectedOutputType;
        set
        {
            _selectedOutputType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedOutputUnitLabel));
            OnPropertyChanged(nameof(SelectedOutputReferenceNote));
            if (value == OutputMaximumShearStrain || value == OutputMaximumShearStrainPeakGp)
                SelectCriticalSrfStageForVisualization();
        }
    }

    /// <summary>Unit hint shown next to the contour output selector and color legend.</summary>
    public string SelectedOutputUnitLabel => $"Units: {GetScalarUnit(SelectedOutputType)}";

    /// <summary>
    /// Short convention hint shown below the visualization selector. RS2-style outputs are
    /// display-only transforms of the same FEM solution: stresses are compression-positive
    /// MPa; displacement is millimetres; polar quantities are measured about the JSON
    /// tunnel centre.
    /// </summary>
    public string SelectedOutputReferenceNote
    {
        get
        {
            if (SelectedOutputType is OutputHoopStressCompMpA or OutputRadialStressCompMpA)
                return BuildPolarReferenceNote("RS2-style: compression-positive MPa; polar axes about tunnel centre; plane strain; values are FEM element stresses projected to σr/σθ.");
            if (SelectedOutputType == OutputMajorPrincipalCompMpA)
                return "RS2-style: major compressive principal stress = -solver σ₂ / 1000 (MPa, comp+); plane strain.";
            if (SelectedOutputType == OutputRadialInwardDisplacementMm)
                return BuildPolarReferenceNote("RS2-style: inward radial displacement in mm, referenced to tunnel centre; positive means wall/point moves into the opening.");
            if (SelectedOutputType == OutputRs2TotalDisplacementMm)
                return "RS2-style: total displacement magnitude in mm from the current stage reference; includes any finite outer-boundary movement.";
            if (IsJointOutput(SelectedOutputType))
                return "Joint (Goodman interface): σn and τ in kPa, compression-positive on the joint; un opening-positive (m); closure = −un; us is tangential slip (m). Drawn on the interface, not as a continuum fill.";
            if (IsElementStressOutput(SelectedOutputType))
                return "Solver-native stress: kPa, tension-positive. Compression appears as negative values.";
            if (SelectedOutputType == OutputMaximumShearStrain)
                return "RS2/PLAXIS-style maximum shear strain γmax = |ε₁ − ε₂| from averaged element strains (centroid); dimensionless total strain at the critical SRF stage.";
            if (SelectedOutputType == OutputMaximumShearStrainPeakGp)
                return "Diagnostic peak Gauss-point γmax per element; can spike at fully fixed boundaries — prefer centroid γmax for RS2D-style slip bands.";
            if (IsElementStrainOutput(SelectedOutputType))
                return "Total element strain (dimensionless). γxy is engineering shear strain from the converged SRF stage.";
            if (SelectedOutputType.StartsWith("Displacement", StringComparison.Ordinal))
                return "Solver-native displacement: metres, from the current stage reference.";
            if (SelectedOutputType == OutputSeepageDischarge)
                return "Net nodal discharge (m³/s per m thickness). Non-zero mainly on fixed-head boundaries; positive ≈ inflow, negative ≈ outflow. Domain totals match the run Inflow/Outflow.";
            if (SelectedOutputType is OutputSeepageTotalHead or OutputSeepagePressureHead)
                return "Steady seepage nodal field from p72/p73/p74. Pressure head = total head − elevation.";
            if (SelectedOutputType == OutputTransientExcessPressure)
                return "Transient consolidation excess pore-pressure field (θ-method). Use the time scrubber to animate dissipation; the chart shows the monitor-node history.";
            return "";
        }
    }

    public static string GetScalarUnit(string outputType) => outputType switch
    {
        OutputRs2TotalDisplacementMm or OutputRadialInwardDisplacementMm => "mm",
        OutputMajorPrincipalCompMpA or OutputHoopStressCompMpA or OutputRadialStressCompMpA => "MPa",
        OutputPorePressure or OutputWaterPressureGridPorePressure => "kPa",
        OutputWaterPressureGridTotalHead or OutputWaterPressureGridPressureHead => "m",
        OutputSeepageTotalHead or OutputSeepagePressureHead => "m",
        OutputSeepageDischarge => "m³/s per m",
        OutputTransientExcessPressure => "pressure",
        OutputYieldRatio => "-",
        OutputPlasticStrainNorm => "strain",
        OutputStrainXX or OutputStrainYY or OutputShearStrain or OutputMaximumShearStrain or OutputMaximumShearStrainPeakGp => "strain",
        OutputJointNormalStress or OutputJointShearStress => "kPa",
        OutputJointOpening or OutputJointClosure or OutputJointSlip => "m",
        _ when outputType.StartsWith("Effective", StringComparison.Ordinal) => "kPa",
        _ when outputType.StartsWith("Displacement", StringComparison.Ordinal) => "m",
        _ when IsElementStressOutput(outputType) => "kPa",
        _ => "-"
    };

    private static bool IsElementStressOutput(string outputType)
        => outputType.StartsWith("Stress", StringComparison.Ordinal)
           || outputType.StartsWith("Principal", StringComparison.Ordinal)
           || outputType.StartsWith("Effective", StringComparison.Ordinal)
           || outputType.StartsWith("Pore pressure", StringComparison.Ordinal)
           || outputType.StartsWith("Water pressure grid", StringComparison.Ordinal)
           || outputType.StartsWith("Von Mises", StringComparison.Ordinal)
           || outputType is OutputYieldRatio
           || IsRs2StressOutput(outputType);

    private static bool IsElementStrainOutput(string outputType)
        => outputType is OutputStrainXX or OutputStrainYY or OutputShearStrain
            or OutputMaximumShearStrain or OutputMaximumShearStrainPeakGp
            or OutputPlasticStrainNorm;

    public static bool IsGammaMaxContourOutput(string outputType)
        => outputType is OutputMaximumShearStrain or OutputMaximumShearStrainPeakGp;

    public static bool IsElementScalarOutput(string outputType)
        => IsElementStressOutput(outputType) || IsElementStrainOutput(outputType);

    public static bool IsJointOutput(string outputType)
        => outputType is OutputJointNormalStress or OutputJointShearStress
            or OutputJointOpening or OutputJointClosure or OutputJointSlip;

    public double GetJointScalarValue(P610T3JointResult joint, string outputType)
        => outputType switch
        {
            OutputJointNormalStress => joint.SnKpa,
            OutputJointShearStress => joint.TauKpa,
            OutputJointOpening => joint.Un,
            OutputJointClosure => joint.Closure,
            OutputJointSlip => joint.Us,
            _ => 0.0
        };

    public static bool IsNodalScalarOutput(string outputType)
        => outputType.StartsWith("Displacement", StringComparison.Ordinal)
           || outputType == OutputRs2TotalDisplacementMm
           || outputType == OutputRadialInwardDisplacementMm
           || outputType == OutputSeepageTotalHead
           || outputType == OutputSeepagePressureHead
           || outputType == OutputSeepageDischarge
           || outputType == OutputTransientExcessPressure;

    private static bool IsRs2StressOutput(string outputType)
        => outputType is OutputMajorPrincipalCompMpA or OutputHoopStressCompMpA or OutputRadialStressCompMpA;

    public double GetNodeScalarValue(Node node, string outputType)
    {
        double ux = node.Displacements != null && node.Displacements.Length > 0 ? node.Displacements[0] : 0.0;
        double uy = node.Displacements is { Length: >= 2 } ? node.Displacements[1] : 0.0;
        return outputType switch
        {
            OutputDisplacementMagnitude => Math.Sqrt(ux * ux + uy * uy),
            OutputDisplacementX => ux,
            OutputDisplacementY => uy,
            OutputRs2TotalDisplacementMm => 1000.0 * Math.Sqrt(ux * ux + uy * uy),
            OutputRadialInwardDisplacementMm => GetRadialInwardDisplacementMm(node, ux, uy),
            OutputSeepageTotalHead => _seepageNodesById.TryGetValue(node.Id, out var sh) ? sh.TotalHead : 0.0,
            OutputSeepagePressureHead => _seepageNodesById.TryGetValue(node.Id, out var sp) ? sp.PressureHead : 0.0,
            OutputSeepageDischarge => _seepageNodesById.TryGetValue(node.Id, out var sq) ? sq.FlowRate : 0.0,
            OutputTransientExcessPressure => _seepageNodesById.TryGetValue(node.Id, out var tp) ? tp.PressureHead : 0.0,
            _ => 0.0
        };
    }

    public double GetElementScalarValue(ElementBase element, string outputType)
        => GetElementScalarValue(element, outputType, FENodes);

    public double GetElementScalarValue(ElementBase element, string outputType, IReadOnlyList<Node> nodes)
    {
        var stress = ResolveElementStress(element);
        if (stress == null) return 0.0;

        return outputType switch
        {
            OutputStressXX => stress.StressXX,
            OutputStressYY => stress.StressYY,
            OutputStressXY => stress.StressXY,
            OutputPrincipalMax => stress.PrincipalStressMax,
            OutputPrincipalMin => stress.PrincipalStressMin,
            OutputMajorPrincipalCompMpA => -stress.PrincipalStressMin / 1000.0,
            OutputHoopStressCompMpA => -ProjectElementStressToPolar(element, nodes, stress).SigTheta / 1000.0,
            OutputRadialStressCompMpA => -ProjectElementStressToPolar(element, nodes, stress).SigR / 1000.0,
            OutputVonMises => stress.VonMisesStress,
            OutputPorePressure or OutputWaterPressureGridPorePressure => stress.PorePressure,
            OutputWaterPressureGridTotalHead => stress.TotalHead,
            OutputWaterPressureGridPressureHead => stress.PressureHead,
            OutputEffectiveStressXX => stress.EffectiveStressXX,
            OutputEffectiveStressYY => stress.EffectiveStressYY,
            OutputEffectiveStressXY => stress.EffectiveStressXY,
            OutputEffectivePrincipalMax => stress.EffectivePrincipalMax,
            OutputEffectivePrincipalMin => stress.EffectivePrincipalMin,
            OutputYieldRatio => stress.YieldRatio,
            OutputPlasticStrainNorm => stress.PlasticStrainNorm,
            OutputStrainXX => stress.StrainXX,
            OutputStrainYY => stress.StrainYY,
            OutputShearStrain => stress.ShearStrain,
            OutputMaximumShearStrain => stress.MaximumShearStrain,
            OutputMaximumShearStrainPeakGp => stress.ContourMaximumShearStrain,
            _ => 0.0
        };
    }

    private ElementStress? ResolveElementStress(ElementBase element)
    {
        var stresses = FeElementStresses;
        if (stresses == null || stresses.Count == 0) return null;

        var byId = stresses.FirstOrDefault(s => s.ElementId == element.Id);
        if (byId != null) return byId;

        var elements = FEElements;
        if (elements == null) return null;
        for (int i = 0; i < elements.Count && i < stresses.Count; i++)
        {
            if (ReferenceEquals(elements[i], element) || elements[i].Id == element.Id)
                return stresses[i];
        }
        return null;
    }

    private double GetRadialInwardDisplacementMm(Node node, double ux, double uy)
    {
        var (cx, cy) = GetTunnelReferenceCenter();
        double dx = node.X - cx;
        double dy = node.Y - cy;
        double r = Math.Sqrt(dx * dx + dy * dy);
        if (r < 1e-12) return 0.0;
        return -1000.0 * (ux * dx / r + uy * dy / r);
    }

    private (double SigR, double SigTheta) ProjectElementStressToPolar(
        ElementBase element,
        IReadOnlyList<Node> nodes,
        ElementStress stress)
    {
        var (cx, cy) = GetTunnelReferenceCenter();
        var (ex, ey) = GetElementCentroid(element, nodes);
        double theta = Math.Atan2(ey - cy, ex - cx);
        double c = Math.Cos(theta);
        double s = Math.Sin(theta);
        double c2 = c * c;
        double s2 = s * s;
        double sc = s * c;
        double sr = stress.StressXX * c2 + stress.StressYY * s2 + 2.0 * stress.StressXY * sc;
        double st = stress.StressXX * s2 + stress.StressYY * c2 - 2.0 * stress.StressXY * sc;
        return (sr, st);
    }

    private static (double X, double Y) GetElementCentroid(ElementBase element, IReadOnlyList<Node> nodes)
    {
        if (element.NodeIds.Length == 0) return (0.0, 0.0);
        double sx = 0.0, sy = 0.0;
        int n = 0;
        foreach (var nodeId in element.NodeIds)
        {
            if (nodeId < 0 || nodeId >= nodes.Count) continue;
            sx += nodes[nodeId].X;
            sy += nodes[nodeId].Y;
            n++;
        }
        return n > 0 ? (sx / n, sy / n) : (0.0, 0.0);
    }

    private (double Cx, double Cy) GetTunnelReferenceCenter()
    {
        var meta = _p610RunResult?.Meta;
        if (meta?.Cx is { } cx && meta.Cy is { } cy) return (cx, cy);
        if (FENodes.Count == 0) return (0.0, 0.0);
        return (0.5 * (FENodes.Min(n => n.X) + FENodes.Max(n => n.X)),
                0.5 * (FENodes.Min(n => n.Y) + FENodes.Max(n => n.Y)));
    }

    private string BuildPolarReferenceNote(string prefix)
    {
        var meta = _p610RunResult?.Meta;
        if (meta == null) return prefix + " No JSON meta block found; using mesh bounding-box centre as fallback.";
        var parts = new List<string>();
        if (meta.Cx is { } cx && meta.Cy is { } cy) parts.Add($"c=({cx:G3},{cy:G3}) m");
        if (meta.A is { } a) parts.Add($"a={a:G4} m");
        if (meta.SigmaV_kPa is { } sv) parts.Add($"σv={sv / 1000.0:G4} MPa comp+");
        if (meta.K0 is { } k0) parts.Add($"K0={k0:G3}");
        if (meta.E_kPa is { } e) parts.Add($"E={e / 1000.0:G4} MPa");
        if (meta.Nu is { } nu) parts.Add($"ν={nu:G3}");
        return parts.Count > 0 ? prefix + " " + string.Join("; ", parts) + "." : prefix;
    }

    public string FeStatusMessage
    {
        get => _feStatusMessage;
        set { _feStatusMessage = value; OnPropertyChanged(); }
    }

    public bool HasStagedResults =>
        (StagedAnalysisResult?.StageResults?.Count > 0) ||
        (_p610RunResult?.Stages is { Count: > 0 }) ||
        (_constructionStageSnapshots is { Count: > 0 }) ||
        HasTransientResults;

    /// <summary>True when a multi-frame p84/p88 transient consolidation result is loaded.</summary>
    public bool HasTransientResults => _transientRunResult?.Steps is { Count: > 0 };

    /// <summary>True for staged excavation / SRF stage lists (not transient time frames).</summary>
    public bool HasStagedExcavationResults =>
        !HasTransientResults && (
            (StagedAnalysisResult?.StageResults?.Count > 0) ||
            (_p610RunResult?.Stages is { Count: > 0 }) ||
            (_constructionStageSnapshots is { Count: > 0 }));

    public IReadOnlyList<object> AvailableStages
    {
        get => _availableStages;
        set
        {
            _availableStages = value;
            OnPropertyChanged();
        }
    }

    public object? SelectedStageInfo
    {
        get => _selectedStageInfo;
        set
        {
            if (ReferenceEquals(_selectedStageInfo, value)) return;
            _selectedStageInfo = value;
            OnPropertyChanged();
            int? requestedFooterStageNumber = null;
            if (value is P610T3ResultsStageListItem item && _p610RunResult != null)
            {
                ApplyP610T3Stage(item.StageListIndex);
                UpdateStageSummaryForP610(item.StageListIndex);
                if (item.StageListIndex >= 0 && item.StageListIndex < _p610RunResult.Stages.Count)
                    requestedFooterStageNumber = _p610RunResult.Stages[item.StageListIndex].Index;

                // Mirror the chosen stage to SelectedStageResult so the contour/vector
                // views' per-stage RemovedElementIds filter (GetRemovedElementIds) renders
                // exactly the cumulative excavation up to this stage — not the union of
                // every stage (which would hide elements that are still solid at the
                // currently-viewed stage; see in_situ_pre_excavation showing as an empty
                // donut without this sync).
                if (_stagedAnalysisResult != null
                    && item.StageListIndex >= 0
                    && item.StageListIndex < _stagedAnalysisResult.StageResults.Count)
                {
                    SelectedStageResult = _stagedAnalysisResult.StageResults[item.StageListIndex];
                }
                else
                {
                    SelectedStageResult = null;
                }
            }
            else if (value is FeConstructionStageResultsListItem feItem
                     && _constructionStageSnapshots != null)
            {
                ApplyConstructionStage(feItem.StageListIndex);
                UpdateStageSummaryForConstruction(feItem.StageListIndex);
                requestedFooterStageNumber = feItem.StageNumber;
                if (_stagedAnalysisResult != null
                    && feItem.StageListIndex >= 0
                    && feItem.StageListIndex < _stagedAnalysisResult.StageResults.Count)
                    SelectedStageResult = _stagedAnalysisResult.StageResults[feItem.StageListIndex];
                else
                    SelectedStageResult = null;
            }
            else if (value is TransientTimeStepListItem tItem && _transientRunResult != null)
            {
                ApplyTransientFrame(tItem.FrameIndex);
                SelectedStageResult = null;
                if (!_suppressTransientIndexSync && _transientTimeIndex != tItem.FrameIndex)
                {
                    _transientTimeIndex = tItem.FrameIndex;
                    OnPropertyChanged(nameof(TransientTimeIndex));
                    OnPropertyChanged(nameof(CurrentTransientTimeLabel));
                    RefreshTransientPlaybackCommands();
                }
            }

            if (!_suppressFooterStageSelectionRequest
                && requestedFooterStageNumber is { } stageNumber
                && stageNumber > 0)
                FooterStageSelectionRequested?.Invoke(stageNumber);
        }
    }

    /// <summary>Raised when the Results/Visualization stage dropdown should activate the matching footer stage tab.</summary>
    public event Action<int>? FooterStageSelectionRequested;

    /// <summary>
    /// Selects the visualization stage that matches the footer tab stage number
    /// (Stage 1 = Initial Stress, Stage 2 = Excavation, etc.).
    /// </summary>
    public void SyncVisualizationToFooterStage(int? footerStageNumber)
    {
        if (footerStageNumber == null || AvailableStages.Count == 0)
            return;

        foreach (var item in AvailableStages)
        {
            if (item is FeConstructionStageResultsListItem fe && fe.StageNumber == footerStageNumber.Value)
            {
                SetSelectedStageFromFooter(item);
                return;
            }

            if (item is P610T3ResultsStageListItem p610 && _p610RunResult != null)
            {
                var snap = _p610RunResult.Stages[p610.StageListIndex];
                if (snap.Index == footerStageNumber.Value)
                {
                    SetSelectedStageFromFooter(item);
                    return;
                }
            }
        }
    }

    private void SetSelectedStageFromFooter(object item)
    {
        _suppressFooterStageSelectionRequest = true;
        try
        {
            SelectedStageInfo = item;
        }
        finally
        {
            _suppressFooterStageSelectionRequest = false;
        }
    }

    /// <summary>
    /// Build a <see cref="StagedAnalysisResult"/> from the <c>! &lt;BEGIN:Layers&gt; ... ! &lt;END:Layers&gt;</c>
    /// block of the .dat file used for this p610_t3 run. Stage 0 is the in-situ snapshot
    /// (no removals); stages 1..K mirror the K excavation layers from the .dat. Element IDs
    /// are 1-based to match <see cref="ElementBase.Id"/> as emitted by the solver JSON.
    ///
    /// Returns <c>null</c> when no .dat content is available or no Layers block was parsed —
    /// the contour views will then fall back to the cumulative <c>IsActive</c> flag from
    /// the JSON, which is correct for single-stage runs but cumulative-only for multi-stage.
    /// </summary>
    private static StagedAnalysisResult? BuildStagedAnalysisFromDat(string? datContent, int jsonStageCount)
    {
        if (string.IsNullOrWhiteSpace(datContent)) return null;

        // Use jsonStageCount - 1 as the upper bound: stage 0 is in-situ in the JSON,
        // so the .dat layers correspond to JSON stages 1..K. Capping the parser keeps
        // a malformed Layers block from over-running the per-stage array.
        int expectedLayers = Math.Max(0, jsonStageCount - 1);
        var layers = P610T3DatImporter.ParseLayersBlock(datContent, expectedLayers);
        if (layers.Count == 0) return null;

        var stageResults = new List<StageResult>(jsonStageCount);
        // Stage 0 — in-situ, full mesh present.
        stageResults.Add(new StageResult { StageNumber = 0, RemovedElementIds = Array.Empty<int>() });
        for (int i = 0; i < layers.Count; i++)
        {
            stageResults.Add(new StageResult
            {
                StageNumber = i + 1,
                RemovedElementIds = layers[i].ToArray()
            });
        }

        return new StagedAnalysisResult { StageResults = stageResults };
    }

    public string StageSummary
    {
        get => _stageSummary;
        set { _stageSummary = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// The most recent <c>p610_t3</c>/<c>p610_t3_elastic</c> run loaded into the Results
    /// tab. Exposed so downstream tabs (notably Verification) can pull the JSON
    /// <c>meta</c> block and the post-load stage snapshot directly without re-parsing.
    /// </summary>
    public P610T3RunResult? LastP610Run => _p610RunResult;

    private string? _lastP610RunDatContent;
    /// <summary>
    /// Verbatim .dat content of the most recent p610_t3 run. Stored so the
    /// Verification tab can re-trigger the header-driven patch comparison
    /// (NAFEMS expected_*_kPa) without needing the user to re-execute the script.
    /// </summary>
    public string? LastP610RunDatContent => _lastP610RunDatContent;

    /// <summary>
    /// Raised after <see cref="LoadP610T3Result"/> finishes installing a new run, so
    /// observers (e.g. <c>VerificationViewModel</c>) can refresh prefilled inputs.
    /// </summary>
    public event EventHandler? FeResultsChanged;

    public void LoadP610T3Result(
        P610T3RunResult r,
        string? statusMessage = null,
        string? datContent = null,
        MeshViewModel? meshGeometrySource = null)
    {
        if (r.Stages == null || r.Stages.Count == 0) return;
        // #region agent log
        AgentDebugRuntimeLog.Write("pre-fix", "H1,H2,H3,H4,H5", "ResultsViewModel.cs:LoadP610T3Result",
            "loading FE result into visualization model",
            new
            {
                statusMessage,
                currentVisualizationMode = VisualizationMode,
                deformedMeshExaggeration = DeformedMeshExaggeration,
                meshGeometrySource = AgentDebugRuntimeLog.SummarizeMesh(meshGeometrySource),
                result = AgentDebugRuntimeLog.SummarizeResult(r)
            });
        // #endregion

        _p610RunResult = r;
        ClearSeepageResult();
        _constructionStageSnapshots = null;
        _constructionBaseNodes = new List<Node>();
        _constructionBaseElements = new List<ElementBase>();
        _lastP610RunDatContent = datContent;
        _feMeshGeometrySource = meshGeometrySource;
        RefreshNodeFixityFromDat(datContent);
        FeMeshSmithEmb2dQ8LocalOrder = ResolveSmithQ8VisualizationOrder(meshGeometrySource, datContent);
        StagedAnalysisResult = BuildStagedAnalysisFromDat(datContent, r.Stages.Count);
        SelectedStageResult = null;

        FEElements = r.Elements.Cast<ElementBase>().ToList();
        if (!string.IsNullOrEmpty(statusMessage))
            FeStatusMessage = statusMessage;

        var list = new List<object>(r.Stages.Count);
        for (int i = 0; i < r.Stages.Count; i++)
        {
            var s = r.Stages[i];
            var label = string.IsNullOrWhiteSpace(s.Label) ? "Stage " + s.Index : s.Label;
            var disp = s.DisplacementsByNodeIndex;
            double umax = 0;
            if (disp != null)
            {
                for (int k = 0; k < disp.Count; k++)
                {
                    var t = disp[k];
                    umax = Math.Max(umax, Math.Sqrt(t.Item1 * t.Item1 + t.Item2 * t.Item2));
                }
            }

            int nStr = s.ElementsSolid >= 0 ? s.ElementsSolid : (s.ElementStresses?.Count ?? 0);
            var trialNote = BuildSrfStageTrialAnnotation(label);
            list.Add(new P610T3ResultsStageListItem
            {
                StageListIndex = i,
                DisplayName = $"{s.Index} — {label}",
                Summary = $"|u|max {umax:E2}  |  {nStr} elem.{trialNote}",
                StageResult = new P610T3StagedRowDetails()
            });
        }

        AvailableStages = list;
        ApplyPseudoStaticFromRun(r);
        if (HasPseudoStaticResults)
        {
            ShowPseudoStaticForceVectors = true;
            if (!IsVectorMode)
                VisualizationMode = VisualizationModeVector;
            if (VectorOutputType != "Pseudo-static Force Vectors"
                && VectorOutputType != "Displacement Vectors")
                VectorOutputType = "Pseudo-static Force Vectors";
        }
        OnPropertyChanged(nameof(HasStagedResults));
        SelectedStageInfo = SelectBestSrfStageItem(list);
        OnPropertyChanged(nameof(LastP610Run));
        ApplyGroundwaterPhreaticFromDat(datContent);
        FeResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFemCppArtifactSummary(string text)
    {
        _femCppArtifactSummary = text ?? "";
    }

    /// <summary>
    /// Installs a steady-seepage run (p72 / p73 / p74) into the visualization model:
    /// nodes carry no displacements; contours plot Total Head / Pressure Head.
    /// </summary>
    public void LoadSeepageResult(
        SeepageRunResult r,
        string? statusMessage = null,
        IReadOnlyList<(double X, double Y)>? originalDomainOutline = null)
    {
        if (r.Nodes.Count == 0) return;

        ClearTransientResultState();
        _seepageRunResult = r;
        _seepageNodesById = r.Nodes.ToDictionary(n => n.Id);
        SeepageOriginalDomainOutline =
            string.Equals(r.Solver, "p73", StringComparison.OrdinalIgnoreCase)
                ? originalDomainOutline ?? Array.Empty<(double X, double Y)>()
                : Array.Empty<(double X, double Y)>();
        _p610RunResult = null;
        _constructionStageSnapshots = null;
        _constructionBaseNodes = new List<Node>();
        _constructionBaseElements = new List<ElementBase>();
        _lastP610RunDatContent = null;
        _feMeshGeometrySource = null;
        FeMeshSmithEmb2dQ8LocalOrder = false;
        StagedAnalysisResult = null;
        AvailableStages = new List<object>();
        SelectedStageInfo = null;

        // Node.Id keeps the solver's 1-based id; NodeIds are 0-based list indices.
        var nodes = r.Nodes
            .OrderBy(n => n.Id)
            .Select(n => new Node { Id = n.Id, X = n.X, Y = n.Y, Displacements = new double[] { 0, 0 } })
            .ToList();
        var idToIndex = new Dictionary<int, int>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++) idToIndex[nodes[i].Id] = i;

        var elements = new List<ElementBase>(r.Elements.Count);
        for (int e = 0; e < r.Elements.Count; e++)
        {
            var conn = r.Elements[e];
            var idx = conn.Select(id => idToIndex.TryGetValue(id, out var k) ? k : 0).ToArray();
            ElementBase el = conn.Length switch
            {
                8 => new QuadraticQuadrilateralElement(),
                3 => new LinearTriangleElement(),
                _ => new LinearQuadrilateralElement(),
            };
            el.Id = e + 1;
            el.NodeIds = idx;
            elements.Add(el);
        }

        FEElements = elements;
        FENodes = nodes;
        FeNodalDisplacements = new List<object>();
        FeElementStresses = new List<ElementStress>();
        JointResults = Array.Empty<P610T3JointResult>();

        VisualizationMode = VisualizationModeContour2D;
        RefreshAvailableOutputTypes();
        SelectedOutputType = OutputSeepageTotalHead;

        if (!string.IsNullOrEmpty(statusMessage))
            FeStatusMessage = statusMessage;
        else
        {
            var iterNote = r.Iterations > 0
                ? $", {r.Iterations} free-surface iterations ({(r.Converged ? "converged" : "NOT converged")})"
                : "";
            FeStatusMessage =
                $"Seepage ({r.Solver}) solved: {r.NodeCount} nodes, {r.ElementCount} elements{iterNote}. " +
                $"Inflow {r.Inflow:E3}, outflow {r.Outflow:E3} m³/s per m.";
        }

        OnPropertyChanged(nameof(HasStagedResults));
        OnPropertyChanged(nameof(LastP610Run));
        FeResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops seepage results so displacement/stress outputs reappear for other solvers.</summary>
    private void ClearSeepageResult()
    {
        ClearTransientResultState();
        _seepageRunResult = null;
        _seepageNodesById = new Dictionary<int, SeepageNodeResult>();
        SeepageOriginalDomainOutline = Array.Empty<(double X, double Y)>();
    }

    /// <summary>Populate Force-Displacement subtab from p64 multi-SRF .res trials.</summary>
    public void ApplyP64ResSummary(
        P64ResFileParser.P64ResSummary summary,
        string? datContent = null,
        int? iterationLimitOverride = null)
    {
        if (summary.Trials.Count == 0)
        {
            ClearSrfConvergencePlot();
            return;
        }

        int iterationLimit = iterationLimitOverride
            ?? P64ResFileParser.TryParseIterationLimitFromDat(datContent);
        _srfIterationLimit = iterationLimit;
        _p64SrfTrials = summary.Trials;
        _srfTrialRows = summary.Trials
            .OrderBy(t => t.Srf)
            .Select(t => new SrfTrialDisplayRow
            {
                Srf = t.Srf,
                MaxDisplacement = t.MaxDisplacement,
                Iterations = t.Iterations,
                Failed = t.Iterations >= iterationLimit || !double.IsFinite(t.MaxDisplacement),
            })
            .ToList();

        bool hasNanTrials = summary.Trials.Any(t => !double.IsFinite(t.MaxDisplacement));

        if (P64ResFileParser.TryEstimateFoS(summary.Trials, iterationLimit, out var fos, out var note))
        {
            _estimatedSrfFoS = fos;
            _srfFoSNote = note;
        }
        else
        {
            _estimatedSrfFoS = null;
            _srfFoSNote = note;
        }

        _srfConvergencePlotModel = SrfConvergencePlotBuilder.Build(
            summary.Trials, iterationLimit, _estimatedSrfFoS);
        _srfConvergenceStatus =
            $"{summary.Trials.Count} SRF trial(s), iteration limit = {iterationLimit}."
            + (hasNanTrials ? " Warning: solver produced non-finite displacements (NaN/Inf); JSON zeros hide the failure — check the .res file." : "")
            + (_estimatedSrfFoS is > 0 ? $" Estimated FoS ≈ {_estimatedSrfFoS.Value:F2}." : $" {_srfFoSNote}")
            + " Select an SRF stage under FE Analysis Results to inspect displacements/stresses.";

        OnPropertyChanged(nameof(HasSrfConvergencePlot));
        OnPropertyChanged(nameof(ShowSrfForceDisplacementPanel));
        OnPropertyChanged(nameof(SrfConvergencePlotModel));
        OnPropertyChanged(nameof(SrfTrialRows));
        OnPropertyChanged(nameof(SrfConvergenceStatus));
        OnPropertyChanged(nameof(EstimatedSrfFoS));
        OnPropertyChanged(nameof(SrfFoSNote));
        OnPropertyChanged(nameof(EstimatedSrfFoSText));
        OnPropertyChanged(nameof(ForceDisplacementPlotModel));
        OnPropertyChanged(nameof(ForceDisplacementStatus));
        OnPropertyChanged(nameof(ShowElasticForceDisplacementControls));
        OnPropertyChanged(nameof(ShowCriticalSrfBanner));
        OnPropertyChanged(nameof(CriticalSrfBannerText));

        RefreshSrfStageListSummaries();

        if (_availableStages.Count > 0)
        {
            var best = SelectBestSrfStageItem(_availableStages);
            if (best != null)
                SelectedStageInfo = best;
        }
    }

    /// <summary>
    /// Overrides the estimated FoS/status text after <see cref="ApplyP64ResSummary"/> with the
    /// SRF bracket runner's converged critical FoS and bracket summary (auto-detected or manual,
    /// expansion + bisection trial counts, final [F_lo, F_hi]). Call immediately after
    /// <see cref="ApplyP64ResSummary"/> for bracket-mode runs.
    /// </summary>
    public void ApplySrfBracketSummary(double criticalFoS, string bracketSummary)
    {
        _estimatedSrfFoS = criticalFoS;
        _srfFoSNote = bracketSummary;
        _srfConvergenceStatus = bracketSummary
            + " Select an SRF stage under FE Analysis Results to inspect displacements/stresses.";

        if (_srfConvergencePlotModel == null && _p64SrfTrials.Count > 0)
            _srfConvergencePlotModel = SrfConvergencePlotBuilder.Build(_p64SrfTrials, _srfIterationLimit, _estimatedSrfFoS);

        OnPropertyChanged(nameof(SrfConvergenceStatus));
        OnPropertyChanged(nameof(EstimatedSrfFoS));
        OnPropertyChanged(nameof(SrfFoSNote));
        OnPropertyChanged(nameof(EstimatedSrfFoSText));
        OnPropertyChanged(nameof(ForceDisplacementStatus));
        OnPropertyChanged(nameof(ShowCriticalSrfBanner));
        OnPropertyChanged(nameof(CriticalSrfBannerText));
    }

    /// <summary>Re-apply SRF trial table from persisted fem_cpp artifact text after .xfem reload.</summary>
    public void ApplyP64ResSummaryFromArtifact(string? artifactSummary, string? datContent, int? iterationLimitOverride = null)
    {
        if (string.IsNullOrWhiteSpace(artifactSummary)
            || !P64ResFileParser.TryParseTrialsFromArtifactSummary(artifactSummary, out var trials))
            return;

        ApplyP64ResSummary(
            new P64ResFileParser.P64ResSummary { Trials = trials, RawText = artifactSummary },
            datContent,
            iterationLimitOverride);
    }

    public void ClearSrfConvergencePlot()
    {
        _srfConvergencePlotModel = null;
        _srfTrialRows = Array.Empty<SrfTrialDisplayRow>();
        _estimatedSrfFoS = null;
        _srfFoSNote = "";
        _p64SrfTrials = Array.Empty<P64ResFileParser.SrfTrialRow>();
        _srfConvergenceStatus = "Run a multi-SRF slope analysis to view SRF vs displacement.";
        OnPropertyChanged(nameof(HasSrfConvergencePlot));
        OnPropertyChanged(nameof(ShowSrfForceDisplacementPanel));
        OnPropertyChanged(nameof(SrfConvergencePlotModel));
        OnPropertyChanged(nameof(SrfTrialRows));
        OnPropertyChanged(nameof(SrfConvergenceStatus));
        OnPropertyChanged(nameof(EstimatedSrfFoS));
        OnPropertyChanged(nameof(SrfFoSNote));
        OnPropertyChanged(nameof(EstimatedSrfFoSText));
        OnPropertyChanged(nameof(ForceDisplacementPlotModel));
        OnPropertyChanged(nameof(ForceDisplacementStatus));
        OnPropertyChanged(nameof(ShowElasticForceDisplacementControls));
        OnPropertyChanged(nameof(ShowCriticalSrfBanner));
        OnPropertyChanged(nameof(CriticalSrfBannerText));
        ClearNewmarkResult();
    }

    public bool HasSrfConvergencePlot => _srfConvergencePlotModel != null && _srfTrialRows.Count > 0;

    /// <summary>Force-Displacement tab: SRF panel (hidden when transient analysis/results own the tab).</summary>
    public bool ShowSrfForceDisplacementPanel =>
        HasSrfConvergencePlot && !HasTransientResults && !IsTransientAnalysisSelected;

    public bool ShowElasticForceDisplacementControls =>
        !HasSrfConvergencePlot && !HasTransientResults && !IsTransientAnalysisSelected;

    public PlotModel? SrfConvergencePlotModel => _srfConvergencePlotModel;

    public IReadOnlyList<SrfTrialDisplayRow> SrfTrialRows => _srfTrialRows;

    public string SrfConvergenceStatus => _srfConvergenceStatus;

    public double? EstimatedSrfFoS => _estimatedSrfFoS;

    public string EstimatedSrfFoSText =>
        _estimatedSrfFoS is > 0 ? $"Estimated Factor of Safety ≈ {_estimatedSrfFoS.Value:F2}" : "";

    public FeNewmarkResult? NewmarkResult => _newmarkResult;

    public bool HasNewmarkResult => _newmarkResult is { HasYieldAcceleration: true };

    public string NewmarkSummaryText
    {
        get
        {
            if (_newmarkResult == null)
                return "";
            var lines = new List<string>
            {
                $"Newmark sliding-block: k_y = {_newmarkResult.YieldAcceleration:0.###} ({_newmarkResult.KySource})"
            };
            foreach (var m in _newmarkResult.ModelResults)
            {
                if (m.HasDisplacement)
                    lines.Add($"  {m.ModelName}: {m.DisplacementCm:0.##} cm");
                else if (!string.IsNullOrWhiteSpace(m.Notes))
                    lines.Add($"  {m.ModelName}: {m.Notes}");
            }
            if (double.IsFinite(_newmarkResult.TimeHistoryDisplacementCm))
                lines.Add($"  Time history: {_newmarkResult.TimeHistoryDisplacementCm:0.##} cm ({_newmarkResult.TimeHistoryExceedanceCount} exceedances)");
            foreach (var w in _newmarkResult.Warnings)
                lines.Add($"  Warning: {w}");
            return string.Join(Environment.NewLine, lines);
        }
    }

    public void ApplyNewmarkResult(FeNewmarkSettings settings, double? srf)
    {
        _newmarkResult = settings.IsEnabled ? FeNewmarkEvaluator.Evaluate(settings, srf) : null;
        OnPropertyChanged(nameof(NewmarkResult));
        OnPropertyChanged(nameof(HasNewmarkResult));
        OnPropertyChanged(nameof(NewmarkSummaryText));
    }

    public void ClearNewmarkResult()
    {
        _newmarkResult = null;
        OnPropertyChanged(nameof(NewmarkResult));
        OnPropertyChanged(nameof(HasNewmarkResult));
        OnPropertyChanged(nameof(NewmarkSummaryText));
    }

    public string SrfFoSNote => _srfFoSNote;

    public bool ShowCriticalSrfBanner => _estimatedSrfFoS is > 0 && HasStagedResults;

    public string CriticalSrfBannerText =>
        _estimatedSrfFoS is > 0 ? $"Critical SRF: {_estimatedSrfFoS.Value:F3}" : "";

    public string GammaMaxStageHint =>
        "RS2D-style slip band: use the critical SRF stage (banner FoS ≈ last stable), not post-failure trials such as srf_1.10.";

    /// <summary>Force-Displacement tab plot: SRF curve when available, else elastic foundation curve.</summary>
    public PlotModel? ForceDisplacementPlotModel =>
        HasSrfConvergencePlot ? _srfConvergencePlotModel : _elasticForceDisplacementPlotModel;

    /// <summary>
    /// Elastic-foundation-only plot model, distinct from <see cref="SrfConvergencePlotModel"/> so the
    /// XAML can bind two separate <c>oxy:PlotView</c> controls (SRF plot beside the trial grid, elastic
    /// plot below its node-selection controls) without both views attaching to the same OxyPlot
    /// <see cref="PlotModel"/> instance — OxyPlot throws "This PlotModel is already in use by some other
    /// PlotView control" if two <c>PlotView</c>s are ever bound to the same model, even when one is
    /// collapsed via <c>Visibility</c> (WPF still realizes/binds hidden elements).
    /// </summary>
    public PlotModel? ElasticForceDisplacementPlotModel => _elasticForceDisplacementPlotModel;

    public string ForceDisplacementStatus =>
        HasSrfConvergencePlot ? _srfConvergenceStatus : _elasticForceDisplacementStatus;

    public int ForceDisplacementNodeId { get; set; } = 1;
    public double ForceDisplacementMaxForceKN { get; set; }
    public int ForceDisplacementNumSteps { get; set; } = 11;
    public ICommand GenerateForceDisplacementCurveCommand { get; }

    /// <summary>p64/fem_cpp artifact text shown in Results (persisted in .xfem).</summary>
    public string FemCppArtifactSummary => _femCppArtifactSummary;

    /// <summary>Results Visualization inner TabControl index (0=FE Results, 1=Visualization, 2=Force-Displacement).</summary>
    public int ResultsSubTabIndex
    {
        get => _resultsSubTabIndex;
        set { _resultsSubTabIndex = value; OnPropertyChanged(); }
    }
    private int _resultsSubTabIndex;

    public void ClearP610T3StagedData()
    {
        _p610RunResult = null;
        _constructionStageSnapshots = null;
        _constructionBaseNodes = new List<Node>();
        _constructionBaseElements = new List<ElementBase>();
        _selectedStageInfo = null;
        _stageSummary = "";
        _femCppArtifactSummary = "";
        _availableStages = new List<object>();
        ClearSrfConvergencePlot();
        PseudoStaticKh = 0;
        PseudoStaticKv = 0;
        FePseudoStaticNodalForces = Array.Empty<FePseudoStaticForceRow>();
        OnPropertyChanged(nameof(HasPseudoStaticResults));
        OnPropertyChanged(nameof(PseudoStaticMaxForceMagnitude));
        OnPropertyChanged(nameof(PseudoStaticConventionNote));
        OnPropertyChanged(nameof(SelectedStageInfo));
        OnPropertyChanged(nameof(AvailableStages));
        OnPropertyChanged(nameof(StageSummary));
        OnPropertyChanged(nameof(HasStagedResults));
        OnPropertyChanged(nameof(LastP610Run));
        FeResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPseudoStaticFromRun(P610T3RunResult r)
    {
        var ps = r.PseudoStatic;
        if (ps == null)
        {
            PseudoStaticKh = 0;
            PseudoStaticKv = 0;
            FePseudoStaticNodalForces = Array.Empty<FePseudoStaticForceRow>();
        }
        else
        {
            PseudoStaticKh = ps.Kh;
            PseudoStaticKv = ps.Kv;
            var rows = new List<FePseudoStaticForceRow>(ps.NodalForces.Count);
            foreach (var f in ps.NodalForces)
            {
                int idx = f.NodeId - 1;
                double x = 0, y = 0;
                if (idx >= 0 && idx < r.Nodes.Count)
                {
                    x = r.Nodes[idx].X;
                    y = r.Nodes[idx].Y;
                }
                rows.Add(new FePseudoStaticForceRow
                {
                    NodeId = f.NodeId,
                    X = x,
                    Y = y,
                    Fx = f.Fx,
                    Fy = f.Fy,
                });
            }
            FePseudoStaticNodalForces = rows;
        }
        OnPropertyChanged(nameof(HasPseudoStaticResults));
        OnPropertyChanged(nameof(PseudoStaticMaxForceMagnitude));
        OnPropertyChanged(nameof(PseudoStaticConventionNote));
    }

    private void UpdateStageSummaryForP610(int stageListIndex)
    {
        if (_p610RunResult == null || stageListIndex < 0 || stageListIndex >= _p610RunResult.Stages.Count) return;
        var s = _p610RunResult.Stages[stageListIndex];
        var head = _p610RunResult.Description;
        if (string.IsNullOrWhiteSpace(head))
        {
            head = $"p610_t3 stage {s.Index} — {s.Label}. "
                 + (string.IsNullOrWhiteSpace(s.Subtitle) ? "" : s.Subtitle + " ");
        }
        var nN = s.DisplacementsByNodeIndex?.Count ?? 0;
        var nE = s.ElementStresses?.Count ?? 0;
        var stagePart = head.Trim() + $"\nNode displacement rows: {nN}, element stress rows: {nE}.";
        StageSummary = string.IsNullOrWhiteSpace(_femCppArtifactSummary)
            ? stagePart
            : _femCppArtifactSummary + "\n\n--- selected stage ---\n" + stagePart;
    }

    private void ApplyP610T3Stage(int stageListIndex)
    {
        if (_p610RunResult == null || stageListIndex < 0 || stageListIndex >= _p610RunResult.Stages.Count) return;
        var snap = _p610RunResult.Stages[stageListIndex];
        var baseN = _p610RunResult.Nodes;
        var disp = snap.DisplacementsByNodeIndex;
        var withDisp = new List<Node>(baseN.Count);
        for (int i = 0; i < baseN.Count; i++)
        {
            var bn = baseN[i];
            double ux = 0, uy = 0;
            if (disp != null && i < disp.Count)
            {
                var t = disp[i];
                ux = t.Item1;
                uy = t.Item2;
            }
            withDisp.Add(bn with { Displacements = new[] { ux, uy } });
        }
        FENodes = ApplyMeshCanvasCoordinates(withDisp, _feMeshGeometrySource);
        FeNodalDisplacements = withDisp.Select(n =>
        {
            double u = n.Displacements != null && n.Displacements.Length > 0 ? n.Displacements[0] : 0;
            double v = n.Displacements is { Length: >= 2 } ? n.Displacements[1] : 0;
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
        var stresses = snap.ElementStresses ?? (IReadOnlyList<ElementStress>)Array.Empty<ElementStress>();
        var stressById = new Dictionary<int, ElementStress>(stresses.Count);
        foreach (var st in stresses)
            stressById[st.ElementId] = st;

        var forFe = new List<ElementStress>(_p610RunResult.Elements.Count);
        for (int i = 0; i < _p610RunResult.Elements.Count; i++)
        {
            var el = _p610RunResult.Elements[i];
            var src = i < stresses.Count ? stresses[i] : null;
            if (src == null && !stressById.TryGetValue(el.Id, out src))
            {
                forFe.Add(new ElementStress { ElementId = el.Id });
                continue;
            }

            if (src!.ElementId == el.Id)
            {
                forFe.Add(src);
                continue;
            }

            forFe.Add(new ElementStress
            {
                ElementId = el.Id,
                StressXX = src.StressXX,
                StressYY = src.StressYY,
                StressXY = src.StressXY,
                PrincipalStressMax = src.PrincipalStressMax,
                PrincipalStressMin = src.PrincipalStressMin,
                VonMisesStress = src.VonMisesStress,
                PorePressure = src.PorePressure,
                EffectiveStressXX = src.EffectiveStressXX,
                EffectiveStressYY = src.EffectiveStressYY,
                EffectiveStressXY = src.EffectiveStressXY,
                EffectivePrincipalMax = src.EffectivePrincipalMax,
                EffectivePrincipalMin = src.EffectivePrincipalMin,
                StrainXX = src.StrainXX,
                StrainYY = src.StrainYY,
                ShearStrain = src.ShearStrain,
                PeakMaximumShearStrain = src.PeakMaximumShearStrain,
            });
        }
        FeElementStresses = forFe;
        JointResults = ResolveJointsForStage(snap);
        RefreshAvailableOutputTypes();
    }

    private IReadOnlyList<P610T3JointResult> ResolveJointsForStage(P610T3StageSnapshot snap)
    {
        if (snap.Joints.Count > 0)
            return snap.Joints;

        var template = _p610RunResult?.Joints;
        if (template == null || template.Count == 0)
        {
            template = _p610RunResult?.Stages
                .SelectMany(s => s.Joints)
                .ToList();
        }

        if (template == null || template.Count == 0 || _p610RunResult == null)
            return Array.Empty<P610T3JointResult>();

        return P610T3JointKinematics.RecomputeAll(
            template, _p610RunResult.Nodes, snap.DisplacementsByNodeIndex);
    }

    /// <summary>
    /// Installs per-stage FE results from staged excavation analysis so the Results
    /// and Visualization dropdown can show stage-specific displacements, stresses,
    /// and excavation masks.
    /// </summary>
    public void LoadFromConstructionStages(
        IReadOnlyList<ConstructionStage> stages,
        IReadOnlyList<Node> baseNodes,
        IReadOnlyList<ElementBase> elements)
    {
        if (stages.Count == 0 || baseNodes.Count == 0 || elements.Count == 0)
            return;

        _constructionBaseNodes = baseNodes;
        _constructionBaseElements = elements;
        _p610RunResult = null;
        JointResults = Array.Empty<P610T3JointResult>();
        ClearSeepageResult();
        _lastP610RunDatContent = null;
        _feMeshGeometrySource = null;
        FEElements = elements;
        _constructionStageSnapshots = new List<ConstructionStageResultsSnapshot>(stages.Count);

        var stageResults = new List<StageResult>(stages.Count);
        var list = new List<object>(stages.Count);

        for (int i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            var removed = stage.ExcavatedElementIds.Count > 0
                ? stage.ExcavatedElementIds.ToList()
                : new List<int>();

            stageResults.Add(new StageResult
            {
                StageNumber = stage.StageNumber,
                RemovedElementIds = removed
            });

            _constructionStageSnapshots.Add(new ConstructionStageResultsSnapshot
            {
                StageNumber = stage.StageNumber,
                NodalDisplacements = new Dictionary<int, (double Ux, double Uy)>(stage.NodalDisplacements),
                ElementStresses = new Dictionary<int, (double Sx, double Sy, double Txy)>(stage.ElementStresses),
            });

            double umax = 0;
            foreach (var (_, (ux, uy)) in stage.NodalDisplacements)
                umax = Math.Max(umax, Math.Sqrt(ux * ux + uy * uy));

            var name = string.IsNullOrWhiteSpace(stage.StageName)
                ? $"Stage {stage.StageNumber}"
                : stage.StageName.Trim();
            list.Add(new FeConstructionStageResultsListItem
            {
                StageListIndex = i,
                StageNumber = stage.StageNumber,
                DisplayName = $"{stage.StageNumber} — {name}",
                Summary = $"|u|max {umax:E2}  |  {removed.Count} excavated elem.",
                StageResult = new P610T3StagedRowDetails()
            });
        }

        StagedAnalysisResult = new StagedAnalysisResult { StageResults = stageResults };
        AvailableStages = list;
        OnPropertyChanged(nameof(HasStagedResults));
        OnPropertyChanged(nameof(LastP610Run));

        var lastSolved = stages.Where(s => s.IsSolved).OrderBy(s => s.StageNumber).LastOrDefault();
        int pickIndex = stages.Count - 1;
        if (lastSolved != null)
        {
            for (int i = 0; i < stages.Count; i++)
            {
                if (ReferenceEquals(stages[i], lastSolved))
                {
                    pickIndex = i;
                    break;
                }
            }
        }
        SelectedStageInfo = list[pickIndex];
        FeResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStageSummaryForConstruction(int stageListIndex)
    {
        if (_constructionStageSnapshots == null
            || stageListIndex < 0
            || stageListIndex >= _constructionStageSnapshots.Count)
            return;

        var snap = _constructionStageSnapshots[stageListIndex];
        double umax = 0;
        foreach (var (_, (ux, uy)) in snap.NodalDisplacements)
            umax = Math.Max(umax, Math.Sqrt(ux * ux + uy * uy));
        StageSummary = $"Stage {snap.StageNumber}: |u|max = {umax:E2} m, "
                     + $"{snap.NodalDisplacements.Count} node displacement rows, "
                     + $"{snap.ElementStresses.Count} element stress rows.";
    }

    private void ApplyConstructionStage(int stageListIndex)
    {
        if (_constructionStageSnapshots == null
            || stageListIndex < 0
            || stageListIndex >= _constructionStageSnapshots.Count
            || _constructionBaseNodes.Count == 0)
            return;

        var snap = _constructionStageSnapshots[stageListIndex];
        var withDisp = new List<Node>(_constructionBaseNodes.Count);
        foreach (var bn in _constructionBaseNodes)
        {
            double ux = 0, uy = 0;
            if (snap.NodalDisplacements.TryGetValue(bn.Id, out var d))
            {
                ux = d.Ux;
                uy = d.Uy;
            }
            withDisp.Add(bn with { Displacements = new[] { ux, uy } });
        }

        FENodes = withDisp;
        FeNodalDisplacements = withDisp.Select(n =>
        {
            double u = n.Displacements != null && n.Displacements.Length > 0 ? n.Displacements[0] : 0;
            double v = n.Displacements is { Length: >= 2 } ? n.Displacements[1] : 0;
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

        var stresses = new List<ElementStress>(_constructionBaseElements.Count);
        foreach (var el in _constructionBaseElements)
        {
            if (snap.ElementStresses.TryGetValue(el.Id, out var st))
            {
                double sxx = st.Sx, syy = st.Sy, sxy = st.Txy;
                double mid = (sxx + syy) / 2.0;
                double rad = Math.Sqrt(((sxx - syy) / 2.0) * ((sxx - syy) / 2.0) + sxy * sxy);
                stresses.Add(new ElementStress
                {
                    ElementId = el.Id,
                    StressXX = sxx,
                    StressYY = syy,
                    StressXY = sxy,
                    PrincipalStressMax = mid + rad,
                    PrincipalStressMin = mid - rad,
                    VonMisesStress = Math.Sqrt(sxx * sxx - sxx * syy + syy * syy + 3.0 * sxy * sxy)
                });
            }
            else
                stresses.Add(new ElementStress { ElementId = el.Id });
        }
        FeElementStresses = stresses;
    }

    private sealed class ConstructionStageResultsSnapshot
    {
        public int StageNumber { get; init; }
        public Dictionary<int, (double Ux, double Uy)> NodalDisplacements { get; init; } = new();
        public Dictionary<int, (double Sx, double Sy, double Txy)> ElementStresses { get; init; } = new();
    }

    /// <summary>
    /// When the Mesh tab has a discretized deck, use its axis coordinates for FE visualization
    /// so contour/vector views match the mesh canvas geometry.
    /// </summary>
    private static List<Node> ApplyMeshCanvasCoordinates(
        IReadOnlyList<Node> feNodes,
        MeshViewModel? mesh)
    {
        if (mesh is not { IsDiscretized: true } || mesh.DiscretizationNodes.Count != feNodes.Count)
            return feNodes.ToList();

        var disc = mesh.DiscretizationNodes;
        var result = new List<Node>(feNodes.Count);
        for (int i = 0; i < feNodes.Count; i++)
        {
            var n = feNodes[i];
            var mn = disc[i];
            result.Add(n with { X = mn.Location.X, Y = mn.Location.Y });
        }
        return result;
    }

    private static bool ResolveSmithQ8VisualizationOrder(MeshViewModel? mesh, string? datContent)
    {
        if (mesh?.MeshInformationRows is { Count: > 0 } rows)
            return rows.Any(r => r is { SmithEmb2dLocalOrder: true, IsQ8: true });

        if (string.IsNullOrEmpty(datContent))
            return false;
        if (!P610AnalysisOptionsHelper.TryParse(datContent, out var ao))
            return false;
        return P610AnalysisOptionsHelper.IsP64Q8Deck(ao)
            && string.Equals(ao.MeshType, "Q8", StringComparison.OrdinalIgnoreCase)
            && DatMeshBoundaryImporter.IsExplicitMeshSourceDeck(datContent)
            && !DatMeshBoundaryImporter.IsSerendipityRectangularQ8Deck(datContent);
    }

    /// <summary>Select last converged SRF stage for RS2D-style γmax slip-band viewing.</summary>
    public void SelectCriticalSrfStageForVisualization()
    {
        if (_availableStages.Count == 0)
            return;

        var best = SelectBestSrfStageItem(_availableStages);
        if (best != null)
            SelectedStageInfo = best;
    }

    private object? SelectBestSrfStageItem(IReadOnlyList<object> list)
    {
        if (HasSrfLabeledStages())
        {
            return FindCriticalSrfStageItem(list)
                ?? FindLastStableSrfStageItem(list)
                ?? (list.Count > 0 ? list[0] : null);
        }

        return list.Count > 0 ? list[0] : null;
    }

    private bool HasSrfLabeledStages()
    {
        if (_p610RunResult?.Stages is not { Count: > 0 } stages)
            return false;

        return stages.Any(s => TryParseSrfFromLabel(s.Label, out _));
    }

    private object? FindLastStableSrfStageItem(IReadOnlyList<object> list)
    {
        if (_p610RunResult?.Stages is not { Count: > 0 } stages)
            return null;

        if (!P64ResFileParser.TryGetLastConvergedSrf(_p64SrfTrials, _srfIterationLimit, out var lastConverged))
            return null;

        int bestIdx = -1;
        double bestSrf = double.NegativeInfinity;
        for (int i = 0; i < stages.Count; i++)
        {
            if (!TryParseSrfFromLabel(stages[i].Label, out var stageSrf))
                continue;
            if (stageSrf > lastConverged + 1e-4)
                continue;
            if (stageSrf > bestSrf)
            {
                bestSrf = stageSrf;
                bestIdx = i;
            }
        }

        if (bestIdx < 0 || bestIdx >= list.Count)
            return null;

        return list[bestIdx];
    }

    private object? FindCriticalSrfStageItem(IReadOnlyList<object> list)
    {
        if (_p610RunResult?.Stages is not { Count: > 0 } stages)
            return null;

        double targetSrf;
        if (_estimatedSrfFoS is > 0)
            targetSrf = _estimatedSrfFoS.Value;
        else if (P64ResFileParser.TryGetLastConvergedSrf(_p64SrfTrials, _srfIterationLimit, out var lastConverged))
            targetSrf = lastConverged;
        else
            return null;

        double firstFailedCap = double.PositiveInfinity;
        if (P64ResFileParser.TryGetFirstFailedSrf(_p64SrfTrials, _srfIterationLimit, out var firstFailed))
            firstFailedCap = firstFailed;

        int bestIdx = -1;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < stages.Count; i++)
        {
            if (!TryParseSrfFromLabel(stages[i].Label, out var stageSrf))
                continue;
            if (stageSrf > firstFailedCap + 1e-4)
                continue;

            double diff = Math.Abs(stageSrf - targetSrf);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIdx = i;
            }
        }

        if (bestIdx < 0 || bestIdx >= list.Count)
            return null;

        return list[bestIdx];
    }

    private void RefreshSrfStageListSummaries()
    {
        if (_p610RunResult?.Stages is not { Count: > 0 } stages || _availableStages.Count == 0)
            return;

        if (!HasSrfLabeledStages())
            return;

        var refreshed = new List<object>(_availableStages.Count);
        for (int i = 0; i < _availableStages.Count; i++)
        {
            if (_availableStages[i] is not P610T3ResultsStageListItem item || i >= stages.Count)
            {
                refreshed.Add(_availableStages[i]);
                continue;
            }

            var s = stages[i];
            var label = string.IsNullOrWhiteSpace(s.Label) ? "Stage " + s.Index : s.Label;
            var disp = s.DisplacementsByNodeIndex;
            double umax = 0;
            if (disp != null)
            {
                for (int k = 0; k < disp.Count; k++)
                {
                    var t = disp[k];
                    umax = Math.Max(umax, Math.Sqrt(t.Item1 * t.Item1 + t.Item2 * t.Item2));
                }
            }

            int nStr = s.ElementsSolid >= 0 ? s.ElementsSolid : (s.ElementStresses?.Count ?? 0);
            refreshed.Add(new P610T3ResultsStageListItem
            {
                StageListIndex = item.StageListIndex,
                DisplayName = item.DisplayName,
                Summary = $"|u|max {umax:E2}  |  {nStr} elem.{BuildSrfStageTrialAnnotation(label)}",
                StageResult = item.StageResult,
            });
        }

        AvailableStages = refreshed;
    }

    private string BuildSrfStageTrialAnnotation(string? stageLabel)
    {
        if (!TryParseSrfFromLabel(stageLabel, out var stageSrf) || _p64SrfTrials.Count == 0)
            return "";

        var trial = _p64SrfTrials
            .OrderBy(t => Math.Abs(t.Srf - stageSrf))
            .FirstOrDefault();
        if (trial == null || Math.Abs(trial.Srf - stageSrf) > 0.05)
            return "";

        bool failed = trial.Iterations >= _srfIterationLimit || !double.IsFinite(trial.MaxDisplacement);
        if (failed)
            return " (limit hit)";

        if (_estimatedSrfFoS is > 0 && Math.Abs(stageSrf - _estimatedSrfFoS.Value) < 0.06)
            return " (critical)";

        if (P64ResFileParser.TryGetLastConvergedSrf(_p64SrfTrials, _srfIterationLimit, out var lastStable)
            && Math.Abs(stageSrf - lastStable) < 1e-4)
            return " (last stable)";

        return " (converged)";
    }

    private void RefreshNodeFixityFromDat(string? datContent)
    {
        if (DatMeshBoundaryImporter.TryParseBcFixMap(datContent, out var fixMap))
            _nodeFixity = fixMap;
        else
            _nodeFixity = new Dictionary<int, (int, int)>();
    }

    /// <summary>True when every node of the element is fully fixed (ux and uy restrained).</summary>
    public bool IsElementFullyFixed(ElementBase element)
    {
        if (_nodeFixity.Count == 0 || element.NodeIds is not { Length: > 0 })
            return false;

        foreach (var nodeId in element.NodeIds)
        {
            if (!_nodeFixity.TryGetValue(nodeId, out var fix))
                return false;
            if (fix.FixX != 0 || fix.FixY != 0)
                return false;
        }

        return true;
    }

    private static bool TryParseSrfFromLabel(string? label, out double srf)
    {
        srf = 0;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        var match = SrfStageLabelRegex.Match(label);
        if (!match.Success)
            return false;

        return double.TryParse(
            match.Groups[1].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out srf);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
