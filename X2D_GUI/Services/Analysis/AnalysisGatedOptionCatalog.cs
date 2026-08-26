namespace GeotechSlopeApp.Services.Analysis;

/// <summary>
/// Display catalogs for analysis-option dropdowns that the live FEM engine does not consume.
/// First item is the UI default.
/// </summary>
public static class AnalysisGatedOptionCatalog
{
    public static readonly IReadOnlyList<string> LoadingTypes =
    [
        "Static (Constant)",
        "Incremental",
        "Cyclic",
        "Transient",
        "Dynamic"
    ];

    public static readonly IReadOnlyList<string> GravityDirections =
        ["-Y", "+Y", "-X", "+X"];

    public static readonly IReadOnlyList<string> TangentStiffness =
    [
        "Auto",
        "Every Iteration",
        "Every Load Step",
        "Initial Stiffness",
        "Secant"
    ];

    public static readonly IReadOnlyList<string> ConvergenceOptions =
    [
        "Energy Norm",
        "Displacement Norm",
        "Residual Force",
        "Absolute Residual",
        "Relative Residual"
    ];

    public static readonly IReadOnlyList<string> K0Methods =
    [
        "Jaky",
        "Manual",
        "Mayne & Kulhawy",
        "Brooker & Ireland",
        "User Defined"
    ];

    public static readonly IReadOnlyList<string> DampingTypes =
    [
        "None",
        "Rayleigh",
        "Mass Proportional",
        "Stiffness Proportional",
        "Modal"
    ];

    public static readonly IReadOnlyList<string> DampingRatios =
        ["0.00", "0.02", "0.05", "0.10", "0.20"];

    public static readonly IReadOnlyList<string> IncompatibleModes =
        ["Automatic", "On", "Off", "Enhanced Strain"];

    public static readonly IReadOnlyList<string> BoundaryStiffness =
        ["Program Controlled", "Soft", "Medium", "Stiff", "User Defined"];

    public static readonly IReadOnlyList<string> StressRecovery =
    [
        "At Gauss Points",
        "At Nodes",
        "Extrapolated",
        "Superconvergent Patch Recovery"
    ];

    public static readonly IReadOnlyList<string> StiffnessMatrix =
        ["Symmetric", "Unsymmetric", "Sparse", "Banded"];

    public static readonly IReadOnlyList<string> LineSearch =
        ["On", "Off", "Automatic"];

    public static readonly IReadOnlyList<string> SeepageFlowTypes =
        ["Static", "Steady-State", "Transient"];
    public static readonly IReadOnlyList<string> PoreFluids =
        ["Water"];
    public static readonly IReadOnlyList<string> BoundaryConductance =
        ["Program Controlled", "User Defined"];
    public static readonly IReadOnlyList<string> InitialConditionTypes =
        ["Hydrostatic", "At Rest", "User Defined"];
    public static readonly IReadOnlyList<string> OutputLocations =
        ["Nodal", "Gauss Points"];
    public static readonly IReadOnlyList<string> ResultTypes =
        ["Scalar", "Vector"];
    public static readonly IReadOnlyList<string> ExportIntervals =
        ["Each Stage", "Each Step", "Final Only"];

    public static readonly IReadOnlyList<string> ConsolidationTypes =
        ["1D Primary (Terzaghi)", "2D Coupled"];
    public static readonly IReadOnlyList<string> CompressionModels =
        ["Isotropic", "Anisotropic"];
    public static readonly IReadOnlyList<string> TimeUnits =
        ["day", "hour", "s"];
    public static readonly IReadOnlyList<string> TimeIncrementation =
        ["Automatic", "Fixed"];

    public static readonly IReadOnlyList<string> DynamicDampingTypes =
        ["Rayleigh Damping", "None"];
    public static readonly IReadOnlyList<string> IntegrationMethods =
        ["Newmark", "HHT"];
    public static readonly IReadOnlyList<string> MassFormulations =
        ["Consistent", "Lumped"];
    public static readonly IReadOnlyList<string> DynamicLoadTypes =
        ["Seismic Input (Acceleration)", "Force Time History"];
    public static readonly IReadOnlyList<string> SeismicDirections =
        ["Horizontal (X)", "Vertical (Y)"];
    public static readonly IReadOnlyList<string> SeismicInputTypes =
        ["Time History", "Response Spectrum"];
    public static readonly IReadOnlyList<string> SeismicFunctions =
        ["EQ_Acc_X"];
    public static readonly IReadOnlyList<string> BoundaryDamping =
        ["Viscous Boundary", "Absorbing", "None"];
    public static readonly IReadOnlyList<string> SamplingIntervals =
        ["Every Step", "Every N Steps"];
    public static readonly IReadOnlyList<string> TimeHistorySave =
        ["All Steps", "Final Only"];

    public static readonly IReadOnlyList<string> ThermalProblemTypes =
        ["Steady-State", "Transient"];
    public static readonly IReadOnlyList<string> ThermalFormulations =
        ["Heat Conduction"];
    public static readonly IReadOnlyList<string> CoupledSchemes =
        ["Seepage → SRF (live)", "Fully Coupled Biot"];
}
