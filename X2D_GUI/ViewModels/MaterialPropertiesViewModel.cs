using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Services;
using GeotechSlopeApp.ViewModels.Dialogs;
using Microsoft.Win32;

namespace GeotechSlopeApp.ViewModels;

public class MaterialPropertiesViewModel : INotifyPropertyChanged
{
    private GeometryLineSegment? _selectedLineSegment;
    private LayerCoordinateSummary? _selectedLayerSummaryForMaterials;
    private GeometryDefinitionViewModel? _geometryDefinition;
    private FailureCriterion _selectedFailureCriterion = FailureCriterion.Elastic;
    private MaterialType _selectedMaterialType = MaterialType.Isotropic;
    private ProjectSettingsViewModel? _projectSettings;
    private string? _selectedAnalysisCategory;
    private string? _selectedDetailedAnalysisType;
    /// <summary>Last Flow-Seepage tree leaf: Static, Steady State, or Transient State.</summary>
    private string? _lastGroundwaterTreeMode;
    /// <summary>Last steady seepage solver id (p72/p73/p74 type label) for SRF coupling.</summary>
    private string? _lastSteadyStateSolverType;
    /// <summary>Last transient consolidation solver id (p84/p88 type label).</summary>
    private string? _lastTransientSolverType;
    private IProjectUndoHost? _undoHost;
    private int _selectedMaterialSubtabIndex;
    private FailureCriterion _newMaterialFailureCriterion = FailureCriterion.Elastic;

    public void SetUndoHost(IProjectUndoHost? host) => _undoHost = host;
    private void RecordUndo(string description) => _undoHost?.RecordUndoPoint(description);

    public const int LayeringControlsSubtabIndex = 0;
    public const int ProjectMaterialsSubtabIndex = 1;
    public const int LayerMaterialAssignmentSubtabIndex = 2;

    public int SelectedMaterialSubtabIndex
    {
        get => _selectedMaterialSubtabIndex;
        set
        {
            if (_selectedMaterialSubtabIndex == value) return;
            _selectedMaterialSubtabIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Failure criterion seeded into the Add Material dialog from the explorer group.</summary>
    public FailureCriterion NewMaterialFailureCriterion
    {
        get => _newMaterialFailureCriterion;
        set
        {
            if (_newMaterialFailureCriterion == value) return;
            _newMaterialFailureCriterion = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Project Settings → Analysis: slope strength-reduction (SRF) category.</summary>
    public const string SrfAnalysisCategory = "Slope stability – strength reduction (SRF)";

    /// <summary>Project Settings → Analysis: flow / seepage / groundwater category.</summary>
    public const string GroundwaterAnalysisCategory = "Flow-Seepage/Groundwater";

    /// <summary>Legacy persisted labels for <see cref="GroundwaterAnalysisCategory"/>.</summary>
    public const string GroundwaterAnalysisCategoryLegacy = "Flow – seepage / groundwater";
    public const string GroundwaterAnalysisCategoryLegacyShort = "Groundwater";

    /// <summary>Prescribed pore pressure (Ru, hydrostatic phreatic) without a seepage flow solve.</summary>
    public const string GroundwaterStaticType = "Static";

    /// <summary>
    /// Analysis tree group under Flow-Seepage/Groundwater for steady seepage solvers.
    /// UI-only; never stored as <see cref="SelectedDetailedAnalysisType"/>.
    /// </summary>
    public const string GroundwaterSteadyStateType = "Steady State";

    /// <summary>
    /// Analysis tree group under Flow-Seepage/Groundwater for transient consolidation solvers.
    /// UI-only; never stored as <see cref="SelectedDetailedAnalysisType"/>.
    /// </summary>
    public const string GroundwaterTransientStateType = "Transient State";

    /// <summary>Future Flow-Seepage leaf: undrained increment → dissipate → update σ. Disabled.</summary>
    public const string GroundwaterSequentialType = "Sequential";

    /// <summary>Future Flow-Seepage leaf: two-way solid–fluid (Biot). Disabled — not implemented.</summary>
    public const string GroundwaterFullyCoupledType = "Fully coupled";

    /// <summary>Steady confined seepage on a structured Q4 mesh (solver program p72).</summary>
    public const string GroundwaterSeepagePlaneAxisymType = "2D Plane/axisymmetric";

    /// <summary>Unconfined seepage with iteratively located phreatic surface (solver program p73).</summary>
    public const string GroundwaterSeepageFreeSurfaceType = "Unconfined / free-surface flow";

    /// <summary>General-mesh steady seepage for homogeneous T3/T6/Q4/Q8 (solver program p74).</summary>
    public const string GroundwaterSeepageGeneralType = "General 2D";

    /// <summary>General-mesh steady seepage (3D).</summary>
    public const string GroundwaterSeepageGeneral3DType = "General 3D seepage";

    /// <summary>Transient consolidation on a structured Q4 mesh (solver program p84).</summary>
    public const string GroundwaterTransientPlaneAxisymType = "Transient (plane/axisymmetric)";

    /// <summary>Transient consolidation on a general 2D mesh (solver program p88).</summary>
    public const string GroundwaterTransientGeneral2DType = "General 2D consolidation";

    /// <summary>Legacy persisted labels for the seepage analysis types (pre-rename).</summary>
    public const string GroundwaterSeepagePlaneAxisymTypeLegacy = "2D steady seepage (plane/axisymmetric)";
    public const string GroundwaterSeepageFreeSurfaceTypeLegacy = "Free-surface flow";
    public const string GroundwaterSeepageGeneralTypeLegacy = "General 2D seepage";
    public const string GroundwaterSeepageGeneralTypeLegacyAll = "General 2D/3D seepage";

    public static bool IsGroundwaterAnalysisCategory(string? category) =>
        string.Equals(category, GroundwaterAnalysisCategory, StringComparison.Ordinal)
        || string.Equals(category, GroundwaterAnalysisCategoryLegacy, StringComparison.Ordinal)
        || string.Equals(category, GroundwaterAnalysisCategoryLegacyShort, StringComparison.Ordinal);

    public static string? NormalizeAnalysisCategory(string? category)
    {
        if (string.Equals(category, GroundwaterAnalysisCategoryLegacy, StringComparison.Ordinal)
            || string.Equals(category, GroundwaterAnalysisCategoryLegacyShort, StringComparison.Ordinal))
            return GroundwaterAnalysisCategory;
        return category;
    }

    /// <summary>Maps legacy persisted seepage type labels to their renamed equivalents.</summary>
    public static string? NormalizeDetailedAnalysisType(string? type) => type switch
    {
        GroundwaterSeepagePlaneAxisymTypeLegacy => GroundwaterSeepagePlaneAxisymType,
        GroundwaterSeepageFreeSurfaceTypeLegacy => GroundwaterSeepageFreeSurfaceType,
        GroundwaterSeepageGeneralTypeLegacy => GroundwaterSeepageGeneralType,
        GroundwaterSeepageGeneralTypeLegacyAll => GroundwaterSeepageGeneralType,
        // Steady State group leaf → default selectable solver (p72 Plane/axisymmetric removed from UI).
        GroundwaterSteadyStateType => GroundwaterSeepageGeneralType,
        // Transient State group leaf → default p84.
        GroundwaterTransientStateType => GroundwaterTransientPlaneAxisymType,
        _ => type
    };

    /// <summary>True when the selected type is a steady seepage flow solve (p72/p73/p74 / 3D).</summary>
    public static bool IsSeepageFlowAnalysisType(string? type) =>
        string.Equals(type, GroundwaterSeepagePlaneAxisymType, StringComparison.Ordinal)
        || string.Equals(type, GroundwaterSeepageFreeSurfaceType, StringComparison.Ordinal)
        || string.Equals(type, GroundwaterSeepageGeneralType, StringComparison.Ordinal)
        || string.Equals(type, GroundwaterSeepageGeneral3DType, StringComparison.Ordinal);

    /// <summary>True when the selected type is a transient consolidation solve (p84 / p88).</summary>
    public static bool IsTransientFlowAnalysisType(string? type) =>
        string.Equals(type, GroundwaterTransientPlaneAxisymType, StringComparison.Ordinal)
        || string.Equals(type, GroundwaterTransientGeneral2DType, StringComparison.Ordinal);

    /// <summary>
    /// True when the type is offered as a Steady State checkbox in Project Settings
    /// (p72 Plane/axisymmetric is retired from the UI).
    /// </summary>
    public static bool IsSelectableSteadyStateSolver(string? type)
    {
        var t = NormalizeDetailedAnalysisType(type);
        return string.Equals(t, GroundwaterSeepageFreeSurfaceType, StringComparison.Ordinal)
            || string.Equals(t, GroundwaterSeepageGeneralType, StringComparison.Ordinal)
            || string.Equals(t, GroundwaterSeepageGeneral3DType, StringComparison.Ordinal);
    }

    /// <summary>True when the type is offered as a Transient State checkbox in Project Settings.</summary>
    public static bool IsSelectableTransientSolver(string? type)
    {
        var t = NormalizeDetailedAnalysisType(type);
        return string.Equals(t, GroundwaterTransientPlaneAxisymType, StringComparison.Ordinal)
            || string.Equals(t, GroundwaterTransientGeneral2DType, StringComparison.Ordinal);
    }

    /// <summary>True when the Analysis tree leaf is the Steady State group (not a solver id).</summary>
    public static bool IsGroundwaterSteadyStateTreeType(string? type) =>
        string.Equals(type, GroundwaterSteadyStateType, StringComparison.Ordinal);

    /// <summary>True when the Analysis tree leaf is the Transient State group (not a solver id).</summary>
    public static bool IsGroundwaterTransientStateTreeType(string? type) =>
        string.Equals(type, GroundwaterTransientStateType, StringComparison.Ordinal);

    /// <summary>True when the Analysis tree leaf is Sequential (future; disabled).</summary>
    public static bool IsGroundwaterSequentialTreeType(string? type) =>
        string.Equals(type, GroundwaterSequentialType, StringComparison.Ordinal);

    /// <summary>True when the Analysis tree leaf is Fully coupled (future; disabled).</summary>
    public static bool IsGroundwaterFullyCoupledTreeType(string? type) =>
        string.Equals(type, GroundwaterFullyCoupledType, StringComparison.Ordinal);

    /// <summary>True when the leaf is a visible-but-disabled future Flow-Seepage type.</summary>
    public static bool IsGroundwaterFutureTreeType(string? type) =>
        IsGroundwaterSequentialTreeType(type) || IsGroundwaterFullyCoupledTreeType(type);

    /// <summary>
    /// Leftover Smith-list category that does not route Run FE. Hidden from the Analysis tree;
    /// Transient State under Flow-Seepage is the live dissipation path.
    /// </summary>
    public const string LegacyConsolidationCategory = "Consolidation / diffusion / transient";

    /// <summary>Major analysis categories for the Analysis tree (File → Project Settings → Analysis Type).</summary>
    public IReadOnlyList<string> AnalysisCategories { get; } = new[]
    {
        "Structural – static elasticity",
        SrfAnalysisCategory,
        GroundwaterAnalysisCategory,
        "Coupled / fluid–structure",
        "Eigenvalue (natural frequencies / buckling)",
        "Dynamics – forced vibration"
    };

    private static readonly List<string> SrfDetailedTypes = new()
    {
        "Multi-λ strength reduction (SRF sweep)",
    };

    /// <summary>Detailed analysis types for the selected category.</summary>
    public ObservableCollection<string> DetailedAnalysisTypes { get; } = new();

    /// <summary>1D-only options for Structural – static elasticity when Project Settings → Analysis type is 1D Problem.</summary>
    private static readonly List<string> StructuralStaticElasticity1DTypes = new()
    {
        "1D axial rod",
        "Pin-jointed frame / truss",
        "Elastic beam",
        "Rigid-jointed frame",
        "Elasto-plastic beam / frame",
        "Beam stability (buckling)"
    };

    /// <summary>2D-only options for Structural – static elasticity when Project Settings → Analysis type is 2D Problem.</summary>
    private static readonly List<string> StructuralStaticElasticity2DTypes = new()
    {
        "Plane strain",
        "Axisymmetric strain",
        "Axisymmetric 8-node",
        "General 2D elastic",
        "Thermal strain"
    };

    /// <summary>3D-only options for Structural – static elasticity when Project Settings → Analysis type is 3D Problem.</summary>
    private static readonly List<string> StructuralStaticElasticity3DTypes = new()
    {
        "3D solid (hex)",
        "General 3D elastic",
        "3D hex, no global assembly (ebe)",
        "3D hex, UMAT-style"
    };

    /// <summary>Fallback list for Structural – static elasticity when dimension is unspecified.</summary>
    private static readonly List<string> StructuralStaticElasticityAllTypes = new()
    {
        "Plane / axisymmetric strain",
        "Axisymmetric 8-node",
        "3D solid (hex)",
        "General 2D/3D elastic",
        "Thermal strain",
        "3D hex, no global assembly (ebe)",
        "3D hex, UMAT-style"
    };

    /// <summary>1D-only options for Groundwater when Project Settings → Analysis type is 1D Problem.</summary>
    private static readonly List<string> FlowSeepage1DTypes = new()
    {
        GroundwaterStaticType
    };

    /// <summary>
    /// 2D Analysis-tree leaves for Groundwater (Steady/Transient solvers are checkboxes in the detail panel).
    /// </summary>
    private static readonly List<string> FlowSeepage2DTreeTypes = new()
    {
        GroundwaterStaticType,
        GroundwaterSteadyStateType,
        GroundwaterTransientStateType,
        GroundwaterSequentialType,
        GroundwaterFullyCoupledType
    };

    /// <summary>2D selectable solver ids for Groundwater (persisted SelectedDetailedAnalysisType).</summary>
    private static readonly List<string> FlowSeepage2DTypes = new()
    {
        GroundwaterStaticType,
        GroundwaterSeepageFreeSurfaceType,
        GroundwaterSeepageGeneralType,
        GroundwaterTransientPlaneAxisymType,
        GroundwaterTransientGeneral2DType
    };

    /// <summary>3D Analysis-tree leaves for Groundwater.</summary>
    private static readonly List<string> FlowSeepage3DTreeTypes = new()
    {
        GroundwaterStaticType,
        GroundwaterSteadyStateType,
        GroundwaterSequentialType,
        GroundwaterFullyCoupledType
    };

    /// <summary>3D-only options for Groundwater when Project Settings → Analysis type is 3D Problem.</summary>
    private static readonly List<string> FlowSeepage3DTypes = new()
    {
        GroundwaterStaticType,
        GroundwaterSeepageGeneral3DType
    };

    private static readonly List<string> FlowSeepageAllTreeTypes = new()
    {
        GroundwaterStaticType,
        GroundwaterSteadyStateType,
        GroundwaterTransientStateType,
        GroundwaterSequentialType,
        GroundwaterFullyCoupledType
    };

    private static readonly List<string> FlowSeepageAllTypes = new()
    {
        GroundwaterStaticType,
        GroundwaterSeepageFreeSurfaceType,
        GroundwaterSeepageGeneralType,
        GroundwaterSeepageGeneral3DType,
        GroundwaterTransientPlaneAxisymType,
        GroundwaterTransientGeneral2DType
    };

    /// <summary>Steady State detail-panel solver checkboxes (2D). p72 Plane/axisymmetric removed.</summary>
    public static IReadOnlyList<string> SteadyStateSolverTypes2D { get; } = new[]
    {
        GroundwaterSeepageFreeSurfaceType,
        GroundwaterSeepageGeneralType
    };

    /// <summary>Steady State detail-panel solver checkboxes (3D).</summary>
    public static IReadOnlyList<string> SteadyStateSolverTypes3D { get; } = new[]
    {
        GroundwaterSeepageGeneral3DType
    };

    /// <summary>Transient State detail-panel solver checkboxes (2D).</summary>
    public static IReadOnlyList<string> TransientStateSolverTypes2D { get; } = new[]
    {
        GroundwaterTransientPlaneAxisymType,
        GroundwaterTransientGeneral2DType
    };

    /// <summary>1D-only options for Consolidation / diffusion / transient when Project Settings → Analysis type is 1D Problem.</summary>
    private static readonly List<string> Consolidation1DTypes = new()
    {
        "1D consolidation",
        "1D consolidation (settlement)",
        "1D consolidation (explicit)"
    };

    /// <summary>2D-only options for Consolidation / diffusion / transient when Project Settings → Analysis type is 2D Problem.</summary>
    private static readonly List<string> Consolidation2DTypes = new()
    {
        "Transient (plane/axisymmetric)",
        "Consolidation 4-node",
        "Consolidation 4-node (variant)",
        "Consolidation 4-node (ebe)",
        "General 2D consolidation",
        "Diffusion–convection",
        "Diffusion–convection (theta)",
        "Thermal conduction"
    };

    /// <summary>3D-only options for Consolidation / diffusion / transient when Project Settings → Analysis type is 3D Problem.</summary>
    private static readonly List<string> Consolidation3DTypes = new()
    {
        "General 3D consolidation"
    };

    private static readonly List<string> ConsolidationAllTypes = new()
    {
        "1D consolidation",
        "1D consolidation (settlement)",
        "1D consolidation (explicit)",
        "Transient (plane/axisymmetric)",
        "Consolidation 4-node",
        "Consolidation 4-node (variant)",
        "Consolidation 4-node (ebe)",
        "General consolidation",
        "Diffusion–convection",
        "Diffusion–convection (theta)",
        "Thermal conduction"
    };

    /// <summary>1D-only options for Coupled / fluid–structure when Project Settings → Analysis type is 1D Problem.</summary>
    private static readonly List<string> Coupled1DTypes = new()
    {
        "1D Biot (coupled consolidation)"
    };

    /// <summary>2D-only options for Coupled / fluid–structure when Project Settings → Analysis type is 2D Problem.</summary>
    private static readonly List<string> Coupled2DTypes = new()
    {
        "Steady Navier–Stokes",
        "Navier–Stokes (PCG / ebe)"
    };

    private static readonly List<string> CoupledAllTypes = new()
    {
        "1D Biot (coupled consolidation)",
        "Steady Navier–Stokes",
        "Navier–Stokes (PCG / ebe)"
    };

    /// <summary>1D-only options for Eigenvalue (natural frequencies / buckling) when Project Settings → Analysis type is 1D Problem.</summary>
    private static readonly List<string> Eigenvalue1DTypes = new()
    {
        "Beam eigenvalues"
    };

    /// <summary>2D-only options for Eigenvalue (natural frequencies / buckling) when Project Settings → Analysis type is 2D Problem.</summary>
    private static readonly List<string> Eigenvalue2DTypes = new()
    {
        "Plane strain eigenvalue (4/8-node)",
        "Plane strain eigenvalue (4-node)",
        "Plane strain eigenvalue (ARPACK)"
    };

    private static readonly List<string> EigenvalueAllTypes = new()
    {
        "Beam eigenvalues",
        "Plane strain eigenvalue (4/8-node)",
        "Plane strain eigenvalue (4-node)",
        "Plane strain eigenvalue (ARPACK)"
    };

    /// <summary>1D-only options for Dynamics – forced vibration when Project Settings → Analysis type is 1D Problem.</summary>
    private static readonly List<string> Dynamics1DTypes = new()
    {
        "Beam forced vibration"
    };

    /// <summary>2D-only options for Dynamics – forced vibration when Project Settings → Analysis type is 2D Problem.</summary>
    private static readonly List<string> Dynamics2DTypes = new()
    {
        "Plane strain forced vibration (4/8-node)",
        "Forced vibration 8-node",
        "Forced vibration 8-node (variant)",
        "Forced vibration 8-node (complex)",
        "Forced vibration 4-node",
        "Forced vibration 8-node (theta)",
        "Elasto-plastic forced vibration"
    };

    private static readonly List<string> DynamicsAllTypes = new()
    {
        "Beam forced vibration",
        "Plane strain forced vibration (4/8-node)",
        "Forced vibration 8-node",
        "Forced vibration 8-node (variant)",
        "Forced vibration 8-node (complex)",
        "Forced vibration 4-node",
        "Forced vibration 8-node (theta)",
        "Elasto-plastic forced vibration"
    };

    private static readonly Dictionary<string, List<string>> CategoryToDetailedTypes = new()
    {
        ["Structural – static elasticity"] = StructuralStaticElasticityAllTypes,
        [GroundwaterAnalysisCategory] = FlowSeepageAllTypes,
        ["Consolidation / diffusion / transient"] = ConsolidationAllTypes,
        ["Coupled / fluid–structure"] = CoupledAllTypes,
        ["Eigenvalue (natural frequencies / buckling)"] = EigenvalueAllTypes,
        ["Dynamics – forced vibration"] = DynamicsAllTypes
    };

    /// <summary>Selected major analysis category. Drives DetailedAnalysisTypes list.</summary>
    public string? SelectedAnalysisCategory
    {
        get => _selectedAnalysisCategory;
        set
        {
            if (_selectedAnalysisCategory == value) return;
            _selectedAnalysisCategory = NormalizeAnalysisCategory(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSrfAnalysisMode));
            OnPropertyChanged(nameof(IsSrfReadyForRun));
            UpdateDetailedAnalysisTypes();
            if (IsSrfAnalysisMode)
                ReconcileSrfFailureCriterion(null);
        }
    }

    /// <summary>True when Project Settings → Analysis is the SRF category.</summary>
    public bool IsSrfAnalysisMode =>
        string.Equals(_selectedAnalysisCategory, SrfAnalysisCategory, StringComparison.Ordinal);

    /// <summary>SRF mode with a supported slope failure criterion (MC, DP, HB, GHB).</summary>
    public bool IsSrfReadyForRun =>
        IsSrfAnalysisMode && P610AnalysisOptionsHelper.IsSlopeStabilityFailure(SelectedFailureCriterion);

    /// <summary>
    /// SRF projects: align global Failure criterion with script AnalysisOptions or material library
    /// when the saved dropdown is Elastic/non-slope but library materials use slope criteria.
    /// </summary>
    public bool ReconcileSrfFailureCriterion(string? scriptContent)
    {
        if (!IsSrfAnalysisMode || IsSrfReadyForRun)
            return false;

        if (!P610AnalysisOptionsHelper.TryResolveSrfFailureCriterion(
                scriptContent, Materials, out var resolved))
            return false;

        if (SelectedFailureCriterion == resolved)
            return false;

        SelectedFailureCriterion = resolved;
        return true;
    }

    /// <summary>Selected detailed analysis type within the current category.</summary>
    public string? SelectedDetailedAnalysisType
    {
        get => _selectedDetailedAnalysisType;
        set
        {
            var normalized = NormalizeDetailedAnalysisType(value);
            if (_selectedDetailedAnalysisType == normalized) return;
            _selectedDetailedAnalysisType = normalized;
            OnPropertyChanged();
            if (IsSeepageFlowAnalysisType(normalized))
                RememberSteadyStateSolver(normalized);
            else if (IsTransientFlowAnalysisType(normalized))
                RememberTransientSolver(normalized);
        }
    }

    /// <summary>
    /// Last Flow-Seepage/Groundwater tree mode used for coupling:
    /// Static, Steady State, or Transient State.
    /// </summary>
    public string? LastGroundwaterTreeMode
    {
        get => _lastGroundwaterTreeMode;
        set
        {
            if (_lastGroundwaterTreeMode == value) return;
            _lastGroundwaterTreeMode = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Last selected steady seepage solver type (persisted for SRF coupling when SRF is selected).</summary>
    public string? LastSteadyStateSolverType
    {
        get => _lastSteadyStateSolverType;
        set
        {
            var normalized = NormalizeDetailedAnalysisType(value);
            if (_lastSteadyStateSolverType == normalized) return;
            _lastSteadyStateSolverType = normalized;
            OnPropertyChanged();
        }
    }

    /// <summary>Last selected transient consolidation solver type (p84 / p88).</summary>
    public string? LastTransientSolverType
    {
        get => _lastTransientSolverType;
        set
        {
            var normalized = NormalizeDetailedAnalysisType(value);
            if (_lastTransientSolverType == normalized) return;
            _lastTransientSolverType = normalized;
            OnPropertyChanged();
        }
    }

    /// <summary>Records Static / Steady State / Transient State tree selection for coupled SRF runs.</summary>
    public void RememberGroundwaterTreeMode(string? treeTypeOrSolver)
    {
        if (IsGroundwaterSteadyStateTreeType(treeTypeOrSolver) || IsSeepageFlowAnalysisType(treeTypeOrSolver))
        {
            LastGroundwaterTreeMode = GroundwaterSteadyStateType;
            if (IsSeepageFlowAnalysisType(treeTypeOrSolver))
                RememberSteadyStateSolver(treeTypeOrSolver);
            return;
        }

        if (IsGroundwaterTransientStateTreeType(treeTypeOrSolver) || IsTransientFlowAnalysisType(treeTypeOrSolver))
        {
            LastGroundwaterTreeMode = GroundwaterTransientStateType;
            if (IsTransientFlowAnalysisType(treeTypeOrSolver))
                RememberTransientSolver(treeTypeOrSolver);
            return;
        }

        if (IsGroundwaterFutureTreeType(treeTypeOrSolver))
            return;

        if (string.Equals(NormalizeDetailedAnalysisType(treeTypeOrSolver), GroundwaterStaticType, StringComparison.Ordinal))
            LastGroundwaterTreeMode = GroundwaterStaticType;
    }

    private void RememberSteadyStateSolver(string? solverType)
    {
        if (!IsSeepageFlowAnalysisType(solverType)) return;
        LastSteadyStateSolverType = solverType;
        LastGroundwaterTreeMode = GroundwaterSteadyStateType;
    }

    private void RememberTransientSolver(string? solverType)
    {
        if (!IsTransientFlowAnalysisType(solverType)) return;
        LastTransientSolverType = solverType;
        LastGroundwaterTreeMode = GroundwaterTransientStateType;
    }

    /// <summary>
    /// Dimension-aware Analysis-tree leaves for a category.
    /// For Flow-Seepage/Groundwater this is Static + Steady State + Transient State plus disabled future leaves.
    /// </summary>
    public IReadOnlyList<string> GetAnalysisTreeTypesForCategory(string? category)
    {
        category = NormalizeAnalysisCategory(category);
        if (string.IsNullOrEmpty(category))
            return Array.Empty<string>();

        if (IsGroundwaterAnalysisCategory(category))
        {
            if (Is1DProblem)
                return FlowSeepage1DTypes;
            if (Is2DProblem)
                return FlowSeepage2DTreeTypes;
            if (Is3DProblem)
                return FlowSeepage3DTreeTypes;
            return FlowSeepageAllTreeTypes;
        }

        return GetDetailedTypesForCategory(category);
    }

    /// <summary>Dimension-aware detailed analysis types for a given category (solver / persisted ids).</summary>
    public IReadOnlyList<string> GetDetailedTypesForCategory(string? category)
    {
        category = NormalizeAnalysisCategory(category);
        if (string.IsNullOrEmpty(category))
            return Array.Empty<string>();

        List<string>? list = null;
        if (category == "Structural – static elasticity")
        {
            if (Is1DProblem)
                list = StructuralStaticElasticity1DTypes;
            else if (Is2DProblem)
                list = StructuralStaticElasticity2DTypes;
            else if (Is3DProblem)
                list = StructuralStaticElasticity3DTypes;
            else
                list = StructuralStaticElasticityAllTypes;
        }
        else if (IsGroundwaterAnalysisCategory(category))
        {
            if (Is1DProblem)
                list = FlowSeepage1DTypes;
            else if (Is2DProblem)
                list = FlowSeepage2DTypes;
            else if (Is3DProblem)
                list = FlowSeepage3DTypes;
            else
                list = FlowSeepageAllTypes;
        }
        else if (category == LegacyConsolidationCategory)
        {
            if (Is1DProblem)
                list = Consolidation1DTypes;
            else if (Is2DProblem)
                list = Consolidation2DTypes;
            else if (Is3DProblem)
                list = Consolidation3DTypes;
            else
                list = ConsolidationAllTypes;
        }
        else if (category == "Coupled / fluid–structure")
        {
            if (Is1DProblem)
                list = Coupled1DTypes;
            else if (Is2DProblem)
                list = Coupled2DTypes;
            else
                list = CoupledAllTypes;
        }
        else if (category == "Eigenvalue (natural frequencies / buckling)")
        {
            if (Is1DProblem)
                list = Eigenvalue1DTypes;
            else if (Is2DProblem)
                list = Eigenvalue2DTypes;
            else
                list = EigenvalueAllTypes;
        }
        else if (category == "Dynamics – forced vibration")
        {
            if (Is1DProblem)
                list = Dynamics1DTypes;
            else if (Is2DProblem)
                list = Dynamics2DTypes;
            else
                list = DynamicsAllTypes;
        }
        else if (category == SrfAnalysisCategory)
            list = SrfDetailedTypes;
        else if (CategoryToDetailedTypes.TryGetValue(category, out var fromDict))
            list = fromDict;

        return (IReadOnlyList<string>?)list ?? Array.Empty<string>();
    }

    private void UpdateDetailedAnalysisTypes()
    {
        var previous = _selectedDetailedAnalysisType;
        DetailedAnalysisTypes.Clear();
        var list = GetDetailedTypesForCategory(_selectedAnalysisCategory);
        if (list.Count == 0)
        {
            SelectedDetailedAnalysisType = null;
            OnPropertyChanged(nameof(DetailedAnalysisTypes));
            return;
        }
        foreach (var item in list)
            DetailedAnalysisTypes.Add(item);

        // Keep the current type when still valid so Analysis-tree clicks that set
        // Category then Type do not briefly reset to list[0] and double-fire routing.
        if (!string.IsNullOrEmpty(previous)
            && DetailedAnalysisTypes.Any(t => string.Equals(t, previous, StringComparison.Ordinal)))
            SelectedDetailedAnalysisType = previous;
        else
            SelectedDetailedAnalysisType = DetailedAnalysisTypes[0];

        OnPropertyChanged(nameof(DetailedAnalysisTypes));
    }

    /// <summary>Called by MainWindowViewModel so Material Properties can reflect Project Settings dimension (1D vs 2D/3D).</summary>
    public void SetProjectSettings(ProjectSettingsViewModel? projectSettings)
    {
        if (_projectSettings != null)
        {
            _projectSettings.PropertyChanged -= OnProjectSettingsPropertyChanged;
            _projectSettings.Project.PropertyChanged -= OnProjectModelPropertyChanged;
        }
        _projectSettings = projectSettings;
        if (_projectSettings != null)
        {
            _projectSettings.PropertyChanged += OnProjectSettingsPropertyChanged;
            _projectSettings.Project.PropertyChanged += OnProjectModelPropertyChanged;
        }
        OnPropertyChanged(nameof(Is1DProblem));
        OnPropertyChanged(nameof(Is2DProblem));
        OnPropertyChanged(nameof(Is3DProblem));
        NotifyUnitSystemLabels();
        UpdateDetailedAnalysisTypes();
    }

    private void OnProjectSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettingsViewModel.SelectedDimension))
        {
            OnPropertyChanged(nameof(Is1DProblem));
            OnPropertyChanged(nameof(Is2DProblem));
            OnPropertyChanged(nameof(Is3DProblem));
            UpdateDetailedAnalysisTypes();
        }
        else if (e.PropertyName is nameof(ProjectSettingsViewModel.LengthUnitLabel)
                 or nameof(ProjectSettingsViewModel.UnitWeightUnitLabel)
                 or nameof(ProjectSettingsViewModel.StressUnitLabel))
        {
            NotifyUnitSystemLabels();
        }
    }

    private void OnProjectModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettingsModel.Units))
            NotifyUnitSystemLabels();
    }

    private void NotifyUnitSystemLabels()
    {
        OnPropertyChanged(nameof(UnitWeightUnitLabel));
        OnPropertyChanged(nameof(StressUnitLabel));
        OnPropertyChanged(nameof(CohesionHeader));
        OnPropertyChanged(nameof(UnitWeightHeader));
        OnPropertyChanged(nameof(YoungsModulusHeader));
    }

    public string UnitWeightUnitLabel =>
        ProjectUnitSystem.UnitWeightLabel(_projectSettings?.Project.Units);
    public string StressUnitLabel =>
        ProjectUnitSystem.StressLabel(_projectSettings?.Project.Units);
    public string CohesionHeader => $"c ({StressUnitLabel})";
    public string UnitWeightHeader => $"γ ({UnitWeightUnitLabel})";
    public string YoungsModulusHeader => $"E ({StressUnitLabel})";

    /// <summary>True when Project Settings → Analysis Type → Dimension is 1D Problem.</summary>
    public bool Is1DProblem => _projectSettings?.SelectedDimension == ProblemDimension.OneD;

    /// <summary>True when Project Settings → Analysis Type → Dimension is 2D Problem. Used to show 2D-only options in Structural – static elasticity Type dropdown.</summary>
    public bool Is2DProblem => _projectSettings?.SelectedDimension == ProblemDimension.TwoD;

    /// <summary>True when Project Settings → Analysis Type → Dimension is 3D Problem. Used to show 3D-only options in Structural – static elasticity Type dropdown.</summary>
    public bool Is3DProblem => _projectSettings?.SelectedDimension == ProblemDimension.ThreeD;

    public GeometryDefinitionViewModel GeometryDefinition
    {
        get => _geometryDefinition ??= new();
        set
        {
            if (_geometryDefinition == value) return;
            if (_geometryDefinition?.GeometryLineSegments is INotifyCollectionChanged oldLines)
                oldLines.CollectionChanged -= OnGeometryLineSegmentsChanged;
            if (_geometryDefinition?.LayerCoordinateSummaries is INotifyCollectionChanged oldLayerSummaries)
                oldLayerSummaries.CollectionChanged -= OnLayerCoordinateSummariesChanged;
            _geometryDefinition = value;
            if (_geometryDefinition?.GeometryLineSegments is INotifyCollectionChanged newLines)
                newLines.CollectionChanged += OnGeometryLineSegmentsChanged;
            if (_geometryDefinition?.LayerCoordinateSummaries is INotifyCollectionChanged newLayerSummaries)
                newLayerSummaries.CollectionChanged += OnLayerCoordinateSummariesChanged;
            RebuildLayerAssignments();
        }
    }

    private void OnLayerCoordinateSummariesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            if (_selectedLayerSummaryForMaterials != null)
            {
                _selectedLayerSummaryForMaterials = null;
                OnPropertyChanged(nameof(SelectedLayerSummaryForMaterials));
                UpdateMaterialLayerCanvasHighlight();
            }
        }
        else if (_selectedLayerSummaryForMaterials != null && GeometryDefinition != null)
        {
            var displayNumber = _selectedLayerSummaryForMaterials.DisplayLayerNumber;
            var updated = GeometryDefinition.LayerCoordinateSummaries
                .FirstOrDefault(s => s.DisplayLayerNumber == displayNumber);
            if (updated == null)
            {
                _selectedLayerSummaryForMaterials = null;
                OnPropertyChanged(nameof(SelectedLayerSummaryForMaterials));
                UpdateMaterialLayerCanvasHighlight();
            }
            else if (!ReferenceEquals(updated, _selectedLayerSummaryForMaterials))
            {
                _selectedLayerSummaryForMaterials = updated;
                OnPropertyChanged(nameof(SelectedLayerSummaryForMaterials));
            }
        }

        RebuildLayerAssignments();
    }

    private void OnGeometryLineSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedLayerSummaryForMaterials != null && GeometryDefinition != null)
        {
            var segment = GeometryDefinition.GeometryLineSegments.FirstOrDefault(s => s.LineNumber == SelectedLayerSummaryForMaterials.LayerNumber);
            SelectedMaterial = segment;
        }
    }

    private GeometryLineSegment? _selectedMaterial;
    private GeometryLineSegment? _selectedSegmentForMaterials;

    /// <summary>
    /// Selected geometry segment for material inputs. Synced with <see cref="SelectedLayerSummaryForMaterials"/>.
    /// </summary>
    public GeometryLineSegment? SelectedSegmentForMaterials
    {
        get => _selectedSegmentForMaterials;
        set
        {
            if (_selectedSegmentForMaterials == value) return;
            _selectedSegmentForMaterials = value;
            var summary = value != null && GeometryDefinition != null
                ? GeometryDefinition.LayerCoordinateSummaries.FirstOrDefault(s => s.LayerNumber == value.LineNumber)
                : null;
            _selectedLayerSummaryForMaterials = summary;
            SelectedMaterial = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedLayerSummaryForMaterials));
            UpdateMaterialLayerCanvasHighlight();
        }
    }

    /// <summary>
    /// Selection in Material Properties popup (Layer Number &amp; Name, Layering Controls).
    /// Uses this instead of GeometryDefinition.SelectedLayerSummary so that Material Properties
    /// changes do not trigger highlight in Geometry Definition popup/canvas.
    /// When set, highlights the horizontal material strip on the Material Properties canvas (not the boundary polyline).
    /// </summary>
    public LayerCoordinateSummary? SelectedLayerSummaryForMaterials
    {
        get => _selectedLayerSummaryForMaterials;
        set
        {
            if (_selectedLayerSummaryForMaterials == value) return;
            _selectedLayerSummaryForMaterials = value;
            var segment = value != null && GeometryDefinition != null
                ? GeometryDefinition.GeometryLineSegments.FirstOrDefault(s => s.LineNumber == value.LayerNumber)
                : null;
            _selectedSegmentForMaterials = segment;
            SelectedMaterial = segment;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSegmentForMaterials));
            SyncLayerAssignmentRowSelection(value?.DisplayLayerNumber);
            UpdateLayerElevationInputForDisplayLayer(value?.DisplayLayerNumber);
            UpdateMaterialLayerCanvasHighlight();
            UpdateMaterialAssignmentSurfacePreview();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Keeps Layer–Material Assignments grid selection aligned with Layering Controls (no highlight override).</summary>
    private void SyncLayerAssignmentRowSelection(int? displayLayerNumber)
    {
        var row = displayLayerNumber is int dln
            ? LayerAssignments.FirstOrDefault(r => r.DisplayLayerNumber == dln)
            : null;
        if (ReferenceEquals(_selectedLayerAssignmentRow, row)) return;
        _selectedLayerAssignmentRow = row;
        OnPropertyChanged(nameof(SelectedLayerAssignmentRow));
    }

    private static int? ResolveMaterialLayerHighlightDisplayNumber(
        LayerCoordinateSummary? layeringSelection,
        LayerAssignmentRow? assignmentSelection) =>
        layeringSelection?.DisplayLayerNumber ?? assignmentSelection?.DisplayLayerNumber;

    /// <summary>
    /// The line segment for the selected layer. Used by Deterministic Material Inputs.
    /// Each layer can have its own Young's Modulus, Poisson's Ratio, etc., saved via Apply.
    /// </summary>
    public GeometryLineSegment? SelectedMaterial
    {
        get => _selectedMaterial;
        private set
        {
            if (_selectedMaterial == value) return;
            _selectedMaterial = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Selected line segment from Geometry Line Segments (Geometry Definition tab).
    /// Layer name in the Material Properties popup comes from this segment's Tag.
    /// </summary>
    public GeometryLineSegment? SelectedLineSegment
    {
        get => _selectedLineSegment;
        set
        {
            if (_selectedLineSegment == value) return;
            _selectedLineSegment = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Failure criterion. Default: Elastic.
    /// </summary>
    public FailureCriterion SelectedFailureCriterion
    {
        get => _selectedFailureCriterion;
        set
        {
            if (_selectedFailureCriterion == value) return;
            _selectedFailureCriterion = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowElasticProperties));
            OnPropertyChanged(nameof(ShowStrengthProperties));
            OnPropertyChanged(nameof(IsSrfReadyForRun));
        }
    }

    /// <summary>
    /// Material type. Default: Isotropic.
    /// </summary>
    public MaterialType SelectedMaterialType
    {
        get => _selectedMaterialType;
        set
        {
            if (_selectedMaterialType == value) return;
            _selectedMaterialType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowElasticProperties));
            OnPropertyChanged(nameof(ShowStrengthProperties));
        }
    }

    public FailureCriterion[] FailureCriterionOptions => Enum.GetValues<FailureCriterion>();
    public MaterialType[] MaterialTypeOptions => Enum.GetValues<MaterialType>();

    /// <summary>
    /// True when Elastic + Isotropic: only Young's modulus and Poisson's ratio are shown.
    /// </summary>
    public bool ShowElasticProperties =>
        SelectedFailureCriterion == FailureCriterion.Elastic &&
        SelectedMaterialType == MaterialType.Isotropic;

    /// <summary>
    /// True when NOT Elastic+Isotropic: cohesion and friction angle are shown.
    /// </summary>
    public bool ShowStrengthProperties => !ShowElasticProperties;

    // ── Material library ──────────────────────────────────────────────────────
    /// <summary>All named materials defined in the project (shown in material list panel).</summary>
    public ObservableCollection<MaterialModel> Materials { get; } = new();

    /// <summary>Shared swatch palette (Add/Edit Material dialog and inline grid colour pickers).</summary>
    public IReadOnlyList<Color> ColourPalette { get; } =
        new AddMaterialDialogViewModel(new MaterialModel { Id = 0, Name = "_" }).ColourPalette;

    private readonly HashSet<MaterialModel> _colourSyncHookedMaterials = new();
    private readonly HashSet<LayerCoordinateSummary> _layerColorSyncHookedSummaries = new();

    /// <summary>Number of material types (= p64 <c>np_types</c>).</summary>
    public int MaterialTypeCount => Materials.Count;

    /// <summary>Summary for Project Materials grid header.</summary>
    public string MaterialTypeCountSummary =>
        Materials.Count == 1
            ? "1 material type (np_types = 1) — each row is one φ/c/ψ/γ/E/ν block in the .dat"
            : $"{Materials.Count} material types (np_types = {Materials.Count}) — each row is one φ/c/ψ/γ/E/ν block in the .dat";

    /// <summary>
    /// Smith &amp; Griffiths Example 2 (two-layer slope): strong upper + weak lower Mohr-Coulomb materials.
    /// </summary>
    public void ApplyEx2TwoLayerMaterialsPreset()
    {
        SelectedAnalysisCategory = SrfAnalysisCategory;
        SelectedFailureCriterion = FailureCriterion.MohrCoulomb;
        SelectedMaterialType = MaterialType.Isotropic;
        Materials.Clear();
        Materials.Add(new MaterialModel
        {
            Id = 1,
            Name = "Ex2 upper (strong)",
            FailureCriterion = FailureCriterion.MohrCoulomb,
            MaterialType = MaterialType.Isotropic,
            FrictionAngle = 25.0,
            Cohesion = 1.0,
            DilationAngle = 0.0,
            UnitWeight = 20.0,
            YoungsModulus = 100_000.0,
            PoissonsRatio = 0.3,
            Colour = Color.FromRgb(180, 210, 160),
        });
        Materials.Add(new MaterialModel
        {
            Id = 2,
            Name = "Ex2 lower (weak)",
            FailureCriterion = FailureCriterion.MohrCoulomb,
            MaterialType = MaterialType.Isotropic,
            FrictionAngle = 15.0,
            Cohesion = 0.5,
            DilationAngle = 0.0,
            UnitWeight = 20.0,
            YoungsModulus = 100_000.0,
            PoissonsRatio = 0.3,
            Colour = Color.FromRgb(210, 180, 140),
        });
        OnPropertyChanged(nameof(MaterialTypeCount));
        OnPropertyChanged(nameof(MaterialTypeCountSummary));
        OnPropertyChanged(nameof(IsSrfReadyForRun));
        RebuildLayerAssignments();
    }

    private MaterialModel? _selectedMaterialModel;

    /// <summary>Currently selected row in the material library list.</summary>
    public MaterialModel? SelectedMaterialModel
    {
        get => _selectedMaterialModel;
        set
        {
            _selectedMaterialModel = value;
            OnPropertyChanged();
            (RemoveMaterialCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (EditMaterialCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DuplicateMaterialCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private bool _deleteTargetsAllMaterials;
    private bool _showProjectMaterialsToolbar = true;

    /// <summary>False when Material Explorer All Materials (or a group/leaf) is selected.</summary>
    public bool ShowProjectMaterialsToolbar
    {
        get => _showProjectMaterialsToolbar;
        set
        {
            if (_showProjectMaterialsToolbar == value) return;
            _showProjectMaterialsToolbar = value;
            OnPropertyChanged();
        }
    }

    /// <summary>True when Material Explorer All Materials is selected; Delete removes every material.</summary>
    public bool DeleteTargetsAllMaterials
    {
        get => _deleteTargetsAllMaterials;
        set
        {
            if (_deleteTargetsAllMaterials == value) return;
            _deleteTargetsAllMaterials = value;
            OnPropertyChanged();
            (RemoveMaterialCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    // ── Layer–material assignment rows (Materials &amp; Regions; names from Layer Inputs → Layering Controls) ──
    /// <summary>
    /// One row per layering summary row; assignments stored on <see cref="GeometryDefinitionViewModel"/> by display layer #.
    /// </summary>
    public ObservableCollection<LayerAssignmentRow> LayerAssignments { get; } = new();

    private LayerAssignmentRow? _selectedLayerAssignmentRow;

    /// <summary>Selected row in Layer–Material Assignments.</summary>
    public LayerAssignmentRow? SelectedLayerAssignmentRow
    {
        get => _selectedLayerAssignmentRow;
        set
        {
            if (ReferenceEquals(_selectedLayerAssignmentRow, value)) return;
            _selectedLayerAssignmentRow = value;
            OnPropertyChanged();
            if (value != null)
            {
                var summary = GeometryDefinition?.LayerCoordinateSummaries
                    .FirstOrDefault(s => s.DisplayLayerNumber == value.DisplayLayerNumber);
                if (summary != null && !ReferenceEquals(_selectedLayerSummaryForMaterials, summary))
                {
                    _selectedLayerSummaryForMaterials = summary;
                    OnPropertyChanged(nameof(SelectedLayerSummaryForMaterials));
                    OnPropertyChanged(nameof(SelectedSegmentForMaterials));
                }
            }
            else
            {
                _selectedLayerSummaryForMaterials = null;
                OnPropertyChanged(nameof(SelectedLayerSummaryForMaterials));
            }

            UpdateLayerElevationInputForDisplayLayer(value?.DisplayLayerNumber);
            UpdateMaterialLayerCanvasHighlight();
            UpdateMaterialAssignmentSurfacePreview();
        }
    }

    /// <summary>Replace the project material library after .xfem load and rebuild layer-assignment rows.</summary>
    public void ReplaceMaterialLibraryFromProject(
        IReadOnlyList<MaterialModel> materials,
        int? selectedMaterialId,
        int? selectedLayerDisplayNumber,
        int? selectedLayerAssignmentDisplayNumber)
    {
        UnhookAllMaterialsForColourSync();
        Materials.Clear();
        foreach (var m in materials)
            Materials.Add(m);
        OnPropertyChanged(nameof(MaterialTypeCount));
        OnPropertyChanged(nameof(MaterialTypeCountSummary));

        RebuildLayerAssignments();

        SelectedMaterialModel = selectedMaterialId is int mid
            ? Materials.FirstOrDefault(m => m.Id == mid) ?? Materials.FirstOrDefault()
            : Materials.FirstOrDefault();

        if (_geometryDefinition?.LayerCoordinateSummaries is { Count: > 0 } summaries && selectedLayerDisplayNumber is int dln)
            SelectedLayerSummaryForMaterials = summaries.FirstOrDefault(s => s.DisplayLayerNumber == dln);

        if (selectedLayerAssignmentDisplayNumber is int adln)
        {
            var row = LayerAssignments.FirstOrDefault(r => r.DisplayLayerNumber == adln);
            if (!ReferenceEquals(_selectedLayerAssignmentRow, row))
            {
                _selectedLayerAssignmentRow = row;
                OnPropertyChanged(nameof(SelectedLayerAssignmentRow));
                UpdateMaterialAssignmentSurfacePreview();
            }
        }
    }

    /// <summary>Rebuilds layer–material rows from geometry layering summaries (e.g. after script etype sync).</summary>
    public void RebuildLayerAssignments()
    {
        var prevDisplay = SelectedLayerAssignmentRow?.DisplayLayerNumber;
        foreach (var row in LayerAssignments)
        {
            row.PropertyChanged -= OnLayerAssignmentRowPropertyChanged;
            row.Dispose();
        }

        UnhookAllLayerSummariesForColourSync();
        LayerAssignments.Clear();
        var summaries = _geometryDefinition?.LayerCoordinateSummaries;
        if (summaries == null || summaries.Count == 0)
        {
            if (_selectedLayerAssignmentRow != null)
            {
                _selectedLayerAssignmentRow = null;
                OnPropertyChanged(nameof(SelectedLayerAssignmentRow));
            }

            UpdateMaterialAssignmentSurfacePreview();
            return;
        }

        foreach (var s in summaries)
        {
            HookLayerSummaryForColourSync(s);
            var row = new LayerAssignmentRow(this, s);
            row.PropertyChanged += OnLayerAssignmentRowPropertyChanged;
            LayerAssignments.Add(row);
        }

        var next = prevDisplay.HasValue
            ? LayerAssignments.FirstOrDefault(r => r.DisplayLayerNumber == prevDisplay.Value)
            : null;

        if (!ReferenceEquals(_selectedLayerAssignmentRow, next))
        {
            _selectedLayerAssignmentRow = next;
            OnPropertyChanged(nameof(SelectedLayerAssignmentRow));
        }

        SyncLayerDisplayColorsFromAssignments();
        UpdateMaterialAssignmentSurfacePreview();
    }

    private void SyncLayerDisplayColorsFromAssignments()
    {
        if (_geometryDefinition == null) return;
        foreach (var row in LayerAssignments)
        {
            if (row.AssignedMaterial is MaterialModel mat)
                _geometryDefinition.SetLayerDisplayColor(row.DisplayLayerNumber, mat.Colour);
        }
    }

    private void SyncLayerDisplayColorsForMaterial(int materialId, Color colour)
    {
        if (_geometryDefinition == null) return;
        foreach (var row in LayerAssignments)
        {
            if (GetLayerMaterialAssignmentMaterialId(row) == materialId)
                _geometryDefinition.SetLayerDisplayColor(row.DisplayLayerNumber, colour);
        }
    }

    private void RefreshSurfaceFillsForMaterial(MaterialModel mat)
    {
        if (_geometryDefinition == null) return;
        foreach (var entry in _geometryDefinition.SurfaceMaterialFillsSnapshot)
        {
            if (entry.MaterialId != mat.Id) continue;
            var c = mat.Colour;
            var fill = new SolidColorBrush(Color.FromArgb(170, c.R, c.G, c.B));
            if (fill.CanFreeze) fill.Freeze();
            _geometryDefinition.SetSurfaceMaterialFill(entry.SurfaceNumber, fill, mat.Id);
        }
    }

    private void NotifyLayerAssignmentsForMaterial(int materialId)
    {
        foreach (var row in LayerAssignments)
        {
            if (row.AssignedMaterial?.Id == materialId)
                row.NotifyMaterialAppearanceChanged();
        }
    }

    private void HookMaterialForColourSync(MaterialModel material)
    {
        if (!_colourSyncHookedMaterials.Add(material)) return;
        material.PropertyChanged += OnProjectMaterialColourChanged;
    }

    private void UnhookMaterialForColourSync(MaterialModel material)
    {
        if (!_colourSyncHookedMaterials.Remove(material)) return;
        material.PropertyChanged -= OnProjectMaterialColourChanged;
    }

    private void UnhookAllMaterialsForColourSync()
    {
        foreach (var m in _colourSyncHookedMaterials.ToList())
            UnhookMaterialForColourSync(m);
    }

    private void OnProjectMaterialColourChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MaterialModel mat) return;
        if (e.PropertyName is not (nameof(MaterialModel.Colour) or nameof(MaterialModel.ColourBrush)))
            return;
        SyncLayerDisplayColorsForMaterial(mat.Id, mat.Colour);
        RefreshSurfaceFillsForMaterial(mat);
        NotifyLayerAssignmentsForMaterial(mat.Id);
    }

    private void HookLayerSummaryForColourSync(LayerCoordinateSummary summary)
    {
        if (!_layerColorSyncHookedSummaries.Add(summary)) return;
        summary.PropertyChanged += OnLayerSummaryColourChanged;
    }

    private void UnhookAllLayerSummariesForColourSync()
    {
        foreach (var s in _layerColorSyncHookedSummaries.ToList())
            s.PropertyChanged -= OnLayerSummaryColourChanged;
        _layerColorSyncHookedSummaries.Clear();
    }

    private void OnLayerSummaryColourChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(LayerCoordinateSummary.LayerColor) or nameof(LayerCoordinateSummary.LayerColorBrush)))
            return;
        if (sender is not LayerCoordinateSummary summary) return;
        var mat = LayerAssignments.FirstOrDefault(r => r.DisplayLayerNumber == summary.DisplayLayerNumber)?.AssignedMaterial;
        if (mat == null || mat.Colour == summary.LayerColor) return;
        mat.Colour = summary.LayerColor;
    }

    private static int? GetLayerMaterialAssignmentMaterialId(LayerAssignmentRow row) =>
        row.AssignedMaterial?.Id;

    private void OnLayerAssignmentRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(LayerAssignmentRow.AssignedMaterial) or nameof(LayerAssignmentRow.AssignedMaterialColour)))
            return;
        if (ReferenceEquals(sender, SelectedLayerAssignmentRow))
            UpdateMaterialAssignmentSurfacePreview();
    }

    /// <summary>Highlights the full material layer strip on the Material Properties canvas (never the boundary polyline).</summary>
    private void UpdateMaterialLayerCanvasHighlight()
    {
        if (_geometryDefinition == null) return;
        var displayLayer = ResolveMaterialLayerHighlightDisplayNumber(
            SelectedLayerSummaryForMaterials,
            SelectedLayerAssignmentRow);
        _geometryDefinition.SetHighlightedLineSegment(null);
        _geometryDefinition.SetMaterialLayerStripHighlight(displayLayer);
    }

    private void UpdateMaterialAssignmentSurfacePreview()
    {
        if (_geometryDefinition == null)
            return;

        var displayLayer = ResolveMaterialLayerHighlightDisplayNumber(
            SelectedLayerSummaryForMaterials,
            SelectedLayerAssignmentRow);
        if (displayLayer is int dln)
        {
            UpdateMaterialLayerCanvasHighlight();
            return;
        }

        _geometryDefinition.SetMaterialLayerStripHighlight(null);
        _geometryDefinition.SetMaterialAssignmentSurfacePreview(null, null);
    }

    private void UpdateLayerElevationInputForDisplayLayer(int? displayLayerNumber)
    {
        if (_geometryDefinition == null || displayLayerNumber is not int dln)
            return;
        if (_geometryDefinition.TryGetHorizontalStripElevationRange(dln, out _, out var yTop))
            _geometryDefinition.LayerElevationInput = yTop.ToString("G6", CultureInfo.InvariantCulture);
    }

    /// <summary>Reapplies canvas tint after geometry sync (e.g. Material Properties popup opened).</summary>
    public void SynchronizeMaterialAssignmentCanvasPreview()
    {
        UpdateMaterialAssignmentSurfacePreview();
    }

    /// <summary>
    /// Set by the view (MainWindow.xaml.cs) so the ViewModel can request the Add/Edit dialog
    /// without a hard reference to a Window. Signature: (dialogVM) => bool? (true = OK).
    /// </summary>
    public Func<AddMaterialDialogViewModel, bool?>? RequestAddMaterialDialog { get; set; }

    /// <summary>
    /// Set by the view so the ViewModel can open the Assign Material to Layer dialog.
    /// Signature: (layers, materials) => (displayLayerNumber, materialId)? — null on cancel.
    /// </summary>
    public Func<IReadOnlyList<LayerCoordinateSummary>, IReadOnlyList<MaterialModel>, (int displayLayerNumber, int materialId)?>? RequestAssignMaterialDialog { get; set; }

    /// <summary>Set by the view to open the Material Library window (pass-through — no return value).</summary>
    public Action? RequestMaterialLibraryWindow { get; set; }

    // ── Commands ───────────────────────────────────────────────────────────────
    /// <summary>Open Add Material dialog and, on OK, append the new material to <see cref="Materials"/>.</summary>
    public ICommand OpenAddMaterialDialogCommand { get; }

    /// <summary>Open Edit dialog pre-populated with the selected material.</summary>
    public ICommand EditMaterialCommand { get; }

    /// <summary>Remove the selected material from the library (with confirmation).</summary>
    public ICommand RemoveMaterialCommand { get; }

    /// <summary>Deep-copy the selected material and add it as a new entry.</summary>
    public ICommand DuplicateMaterialCommand { get; }

    /// <summary>Open the Assign Material to Region dialog.</summary>
    public ICommand AssignMaterialToRegionCommand { get; }

    /// <summary>Remove all material assignments from all geometry surfaces (with confirmation).</summary>
    public ICommand ClearMaterialAssignmentsCommand { get; }

    /// <summary>Open the built-in material preset library window.</summary>
    public ICommand OpenMaterialLibraryCommand { get; }

    /// <summary>Import materials from a CSV file into the project library.</summary>
    public ICommand ImportMaterialDatabaseCommand { get; }

    /// <summary>Export all project materials to a CSV file.</summary>
    public ICommand ExportMaterialsCommand { get; }

    // ── Duplicate / next ID helper ────────────────────────────────────────────
    private int NextMaterialId() => Materials.Count == 0 ? 1 : Materials.Max(m => m.Id) + 1;

    private void ExecuteOpenAddMaterialDialog()
    {
        var dialogVm = new AddMaterialDialogViewModel();
        dialogVm.Material.Id = NextMaterialId();
        dialogVm.Material.FailureCriterion = NewMaterialFailureCriterion;
        var ok = RequestAddMaterialDialog?.Invoke(dialogVm);
        if (ok == true && dialogVm.Material != null)
        {
            RecordUndo("Add material");
            Materials.Add(dialogVm.Material);
            SelectedMaterialModel = dialogVm.Material;
        }
    }

    private void ExecuteEditMaterial()
    {
        if (_selectedMaterialModel == null) return;
        // Edit on a clone; only copy back if user clicks OK
        var clone = _selectedMaterialModel.Clone();
        var dialogVm = new AddMaterialDialogViewModel(clone);
        var ok = RequestAddMaterialDialog?.Invoke(dialogVm);
        if (ok != true) return;
        RecordUndo("Edit material");
        // Copy all edited values back to the original (keeps the same object reference in the list)
        var src = clone;
        var dst = _selectedMaterialModel;
        dst.Name                 = src.Name;
        dst.Description          = src.Description;
        dst.Colour               = src.Colour;
        dst.FailureCriterion     = src.FailureCriterion;
        dst.MaterialType         = src.MaterialType;
        dst.YoungsModulus        = src.YoungsModulus;
        dst.PoissonsRatio        = src.PoissonsRatio;
        dst.YoungsModulusH       = src.YoungsModulusH;
        dst.YoungsModulusV       = src.YoungsModulusV;
        dst.PoissonsRatioHH      = src.PoissonsRatioHH;
        dst.PoissonsRatioVH      = src.PoissonsRatioVH;
        dst.ShearModulusVH       = src.ShearModulusVH;
        dst.UnitWeight           = src.UnitWeight;
        dst.SaturatedUnitWeight  = src.SaturatedUnitWeight;
        dst.IsSaturatedEnabled   = src.IsSaturatedEnabled;
        dst.Cohesion             = src.Cohesion;
        dst.FrictionAngle        = src.FrictionAngle;
        dst.DilationAngle        = src.DilationAngle;
        dst.TensileCutoff        = src.TensileCutoff;
        dst.Ucs                  = src.Ucs;
        dst.HoekBrownMi          = src.HoekBrownMi;
        dst.Gsi                  = src.Gsi;
        dst.Disturbance          = src.Disturbance;
        dst.ModulusRatio         = src.ModulusRatio;
        dst.DpAlpha              = src.DpAlpha;
        dst.DpK                  = src.DpK;
        dst.PermeabilityX        = src.PermeabilityX;
        dst.PermeabilityY        = src.PermeabilityY;
        dst.Porosity             = src.Porosity;
        dst.InitialVoidRatio     = src.InitialVoidRatio;
        dst.SpecificStorage      = src.SpecificStorage;
        dst.ThermalConductivityX = src.ThermalConductivityX;
        dst.ThermalConductivityY = src.ThermalConductivityY;
        dst.HeatCapacity         = src.HeatCapacity;
        dst.ThermalExpansion     = src.ThermalExpansion;
        dst.Density              = src.Density;
        dst.DampingRatio         = src.DampingRatio;
        SyncLayerDisplayColorsForMaterial(dst.Id, dst.Colour);
        ReconcileSrfFailureCriterion(null);
    }

    private void ExecuteRemoveMaterial()
    {
        if (DeleteTargetsAllMaterials)
        {
            if (Materials.Count == 0) return;
            var all = MessageBox.Show(
                $"Delete all {Materials.Count} materials?",
                "Delete Material", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (all != MessageBoxResult.OK) return;
            ClearAllMaterials();
            return;
        }

        if (_selectedMaterialModel == null) return;
        var res = MessageBox.Show(
            $"Delete material \"{_selectedMaterialModel.Name}\"?",
            "Delete Material", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (res == MessageBoxResult.OK)
        {
            RecordUndo("Remove material");
            Materials.Remove(_selectedMaterialModel);
        }
    }

    public void ClearAllMaterials()
    {
        RecordUndo("Remove all materials");
        Materials.Clear();
        SelectedMaterialModel = null;
    }

    private void ExecuteDuplicateMaterial()
    {
        if (_selectedMaterialModel == null) return;
        RecordUndo("Duplicate material");
        var copy = _selectedMaterialModel.Clone();
        copy.Id   = NextMaterialId();
        copy.Name = _selectedMaterialModel.Name + " (Copy)";
        Materials.Add(copy);
        SelectedMaterialModel = copy;
    }

    private void ExecuteAssignMaterialToRegion()
    {
        var geo = GeometryDefinition;
        var layers = geo.LayerCoordinateSummaries.ToList();
        if (layers.Count == 0)
        {
            MessageBox.Show(
                "No layers defined. Use Layering Controls (Add layer) first.",
                "Assign Material", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Materials.Count == 0)
        {
            MessageBox.Show("No materials in the project library. Add materials first.",
                "Assign Material", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = RequestAssignMaterialDialog?.Invoke(
            layers.AsReadOnly(),
            Materials.ToList().AsReadOnly());
        if (result == null) return;
        RecordUndo("Assign material to layer");
        geo.SetLayerMaterialAssignmentMaterialId(result.Value.displayLayerNumber, result.Value.materialId);
        RebuildLayerAssignments();
    }

    private void ExecuteClearMaterialAssignments()
    {
        if (GeometryDefinition == null) return;
        var res = MessageBox.Show(
            "Remove all layer–material assignments?",
            "Clear Assignments", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (res != MessageBoxResult.OK) return;
        RecordUndo("Clear material assignments");
        GeometryDefinition.ClearLayerMaterialAssignments();
        RebuildLayerAssignments();
    }

    private void ExecuteImportMaterialDatabase()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Import Material Database",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var lines = File.ReadAllLines(dlg.FileName);
            int imported = 0;
            foreach (var line in lines.Skip(1)) // skip header
            {
                var f = line.Split(',');
                if (f.Length < 6) continue;
                var m = new MaterialModel
                {
                    Id            = NextMaterialId(),
                    Name          = f[0].Trim().Trim('"'),
                    YoungsModulus = double.TryParse(f[1].Trim(), out var e) ? e : 100_000,
                    PoissonsRatio = double.TryParse(f[2].Trim(), out var nu) ? nu : 0.3,
                    UnitWeight    = double.TryParse(f[3].Trim(), out var gam) ? gam : 20,
                    Cohesion      = double.TryParse(f[4].Trim(), out var c) ? c : 0,
                    FrictionAngle = double.TryParse(f[5].Trim(), out var phi) ? phi : 30,
                };
                if (f.Length > 6) m.Description = f[6].Trim().Trim('"');
                Materials.Add(m);
                imported++;
            }
            MessageBox.Show($"Imported {imported} material(s) from {Path.GetFileName(dlg.FileName)}.",
                "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed:\n{ex.Message}", "Import Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteExportMaterials()
    {
        if (Materials.Count == 0)
        {
            MessageBox.Show("No materials to export.", "Export Materials",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title      = "Export Materials",
            Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName   = "materials.csv"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            using var sw = new StreamWriter(dlg.FileName);
            sw.WriteLine("Name,E (kPa),nu,gamma (kN/m3),c (kPa),phi (deg),Description");
            foreach (var m in Materials)
                sw.WriteLine($"\"{m.Name}\",{m.YoungsModulus},{m.PoissonsRatio},{m.UnitWeight}," +
                             $"{m.Cohesion},{m.FrictionAngle},\"{m.Description}\"");
            MessageBox.Show($"Exported {Materials.Count} material(s) to {Path.GetFileName(dlg.FileName)}.",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public bool ShowDeterministicTab { get; set; } = true;
    public bool ShowProbabilisticTab { get; set; } = true;

    /// <summary>
    /// Saves the entered material properties (Young's Modulus, Poisson's Ratio, etc.) to the selected layer.
    /// Values are bound TwoWay with PropertyChanged, so they are saved to the segment as you type.
    /// </summary>
    // ── Staged Construction ───────────────────────────────────────────────────
    private StagedConstructionViewModel? _stagedConstruction;

    /// <summary>
    /// Per-stage element-ID summary surfaced in the Materials &amp; Regions tab as
    /// "Excavated Elements per Stage". Rebuilt whenever the underlying stage
    /// collection / excavation zones / element-ID lists mutate.
    /// </summary>
    public ObservableCollection<StageExcavationSummaryRow> ExcavationSummaryRows { get; } = new();

    /// <summary>True when there is at least one excavation stage to display in the GroupBox.</summary>
    public bool HasExcavationSummary => ExcavationSummaryRows.Count > 0;

    private readonly List<ConstructionStage> _excavSummarySubscribedStages = new();

    /// <summary>Set by MainWindowViewModel after both VMs exist.</summary>
    public StagedConstructionViewModel? StagedConstruction
    {
        get => _stagedConstruction;
        set
        {
            if (_stagedConstruction != null)
            {
                _stagedConstruction.Stages.CollectionChanged -= OnExcavStagesCollectionChanged;
                _stagedConstruction.ExcavatedElementIdsChanged -= OnExcavElementIdsChanged;
            }
            UnsubscribeStageZoneHandlers();

            _stagedConstruction = value;

            if (_stagedConstruction != null)
            {
                _stagedConstruction.Stages.CollectionChanged += OnExcavStagesCollectionChanged;
                _stagedConstruction.ExcavatedElementIdsChanged += OnExcavElementIdsChanged;
                SubscribeStageZoneHandlers();
            }
            RebuildExcavationSummaryRows();

            OnPropertyChanged();
            OnPropertyChanged(nameof(ToggleExcavateModeCommand));
            OnPropertyChanged(nameof(UndoExcavationCommand));
            OnPropertyChanged(nameof(RedoExcavationCommand));
            OnPropertyChanged(nameof(IsExcavateMode));
            OnPropertyChanged(nameof(ExcavateModeButtonLabel));
            OnPropertyChanged(nameof(StagedConstructionStatusMessage));
        }
    }

    private void OnExcavStagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnsubscribeStageZoneHandlers();
        SubscribeStageZoneHandlers();
        RebuildExcavationSummaryRows();
    }

    private void OnExcavElementIdsChanged(object? sender, EventArgs e)
        => RebuildExcavationSummaryRows();

    private void OnExcavZonesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildExcavationSummaryRows();

    private void SubscribeStageZoneHandlers()
    {
        if (_stagedConstruction == null) return;
        foreach (var stage in _stagedConstruction.Stages)
        {
            stage.ExcavationZones.CollectionChanged += OnExcavZonesCollectionChanged;
            _excavSummarySubscribedStages.Add(stage);
        }
    }

    private void UnsubscribeStageZoneHandlers()
    {
        foreach (var stage in _excavSummarySubscribedStages)
            stage.ExcavationZones.CollectionChanged -= OnExcavZonesCollectionChanged;
        _excavSummarySubscribedStages.Clear();
    }

    private void RebuildExcavationSummaryRows()
    {
        ExcavationSummaryRows.Clear();
        if (_stagedConstruction != null)
        {
            // Skip the implicit Stage 1 (Initial Stress) — it never excavates anything.
            // For Stage 2..N, surface the row when at least one ID is known. Empty stages
            // are hidden so the user only sees real excavation activity.
            foreach (var stage in _stagedConstruction.Stages
                .Where(s => s.ExcavatedElementIds.Count > 0)
                .OrderBy(s => s.StageNumber))
            {
                ExcavationSummaryRows.Add(StageExcavationSummaryRow.FromIds(
                    stage.StageNumber,
                    string.IsNullOrWhiteSpace(stage.StageName) ? $"Stage {stage.StageNumber}" : stage.StageName,
                    stage.ExcavatedElementIds));
            }
        }
        OnPropertyChanged(nameof(HasExcavationSummary));
    }

    /// <summary>Forwarded to StagedConstructionViewModel so XAML can bind directly.</summary>
    public ICommand ToggleExcavateModeCommand => _stagedConstruction?.ToggleExcavateModeCommand
        ?? new RelayCommand(() => { });

    public ICommand UndoExcavationCommand => _stagedConstruction?.UndoExcavationCommand
        ?? new RelayCommand(() => { });

    public ICommand RedoExcavationCommand => _stagedConstruction?.RedoExcavationCommand
        ?? new RelayCommand(() => { });

    public bool IsExcavateMode => _stagedConstruction?.IsExcavateMode ?? false;

    public string ExcavateModeButtonLabel => _stagedConstruction?.ExcavateModeButtonLabel ?? "Excavate";

    public string StagedConstructionStatusMessage => _stagedConstruction?.StatusMessage ?? "";

    // ── Assign-material-to-surface (paint bucket) mode ───────────────────────
    private bool _isAssignMaterialMode;
    private MaterialModel? _assignMaterialTarget;

    /// <summary>True while the user has activated Assign mode for a material and is about to click a canvas surface.</summary>
    public bool IsAssignMaterialMode
    {
        get => _isAssignMaterialMode;
        private set
        {
            if (_isAssignMaterialMode == value) return;
            _isAssignMaterialMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AssignMaterialStatusMessage));
        }
    }

    /// <summary>The material currently being painted onto surfaces.</summary>
    public MaterialModel? AssignMaterialTarget
    {
        get => _assignMaterialTarget;
        private set
        {
            _assignMaterialTarget = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AssignMaterialStatusMessage));
        }
    }

    /// <summary>Status bar message shown while in Assign mode.</summary>
    public string AssignMaterialStatusMessage =>
        _assignMaterialTarget != null
            ? GeometryDefinition.LayerCoordinateSummaries.Count > 0
                ? $"🎨  ASSIGN MODE  —  Click a material layer on the canvas to assign \"{_assignMaterialTarget.Name}\".  Click Assign again or press the × button to stop."
                : $"🎨  ASSIGN MODE  —  Click a surface on the canvas to assign \"{_assignMaterialTarget.Name}\".  Click Assign again or press the × button to stop."
            : "";

    /// <summary>Starts (or cancels) Assign mode for the material passed as CommandParameter.</summary>
    public ICommand AssignMaterialFromRowCommand { get; }

    /// <summary>Exits Assign mode without making an assignment.</summary>
    public ICommand CancelAssignMaterialModeCommand { get; }

    private void ExecuteAssignMaterialFromRow(MaterialModel? material)
    {
        if (material == null) return;

        // Toggle off if clicking the same material's Assign button again
        if (_isAssignMaterialMode && ReferenceEquals(_assignMaterialTarget, material))
        {
            ExitAssignMaterialMode();
            return;
        }

        // Clear highlight on previous target
        if (_assignMaterialTarget != null)
            _assignMaterialTarget.IsAssignTarget = false;

        AssignMaterialTarget = material;
        material.IsAssignTarget = true;
        IsAssignMaterialMode = true;

        var hasLayerStrips = GeometryDefinition.LayerCoordinateSummaries.Count > 0;
        GeometryDefinition.StartMaterialAssignMode(
            OnSurfaceClickedForMaterialAssignment,
            hasLayerStrips ? OnLayerClickedForMaterialAssignment : null);
    }

    private void OnLayerClickedForMaterialAssignment(int displayLayerNumber)
    {
        if (_assignMaterialTarget == null) return;
        RecordUndo("Assign material to layer");

        GeometryDefinition.SetLayerMaterialAssignmentMaterialId(displayLayerNumber, _assignMaterialTarget.Id);
        GeometryDefinition.SetLayerDisplayColor(displayLayerNumber, _assignMaterialTarget.Colour);
        GeometryDefinition.SetMaterialLayerStripHighlight(displayLayerNumber);

        var row = LayerAssignments.FirstOrDefault(r => r.DisplayLayerNumber == displayLayerNumber);
        row?.NotifyMaterialAppearanceChanged();
    }

    private void OnSurfaceClickedForMaterialAssignment(int surfaceNumber)
    {
        if (_assignMaterialTarget == null) return;
        RecordUndo("Assign material to surface");

        // Build a semi-transparent fill from the material's colour
        var c = _assignMaterialTarget.Colour;
        var fill = new SolidColorBrush(Color.FromArgb(170, c.R, c.G, c.B));
        if (fill.CanFreeze) fill.Freeze();

        GeometryDefinition.SetSurfaceMaterialFill(surfaceNumber, fill, _assignMaterialTarget.Id);

        // Stay in assign mode — like RS2/Plaxis the user keeps painting until they stop
    }

    private void ExitAssignMaterialMode()
    {
        if (_assignMaterialTarget != null)
            _assignMaterialTarget.IsAssignTarget = false;
        IsAssignMaterialMode = false;
        AssignMaterialTarget = null;
        GeometryDefinition?.StopMaterialAssignMode();
    }

    public ICommand ApplyMaterialToSlicesCommand { get; } = new RelayCommand(() => { });

    /// <summary>
    /// Applies the selected layer's material properties to all layers with the same Tag.
    /// </summary>
    public ICommand ApplyToLayerCommand { get; }

    /// <summary>For 1D: saves EA (YoungsModulus) to the selected layer and matching layers by Tag when Apply is clicked.</summary>
    public ICommand Apply1DMaterialCommand { get; }

    public MaterialPropertiesViewModel()
    {
        ApplyToLayerCommand       = new RelayCommand(ApplyToMatchingLayers, () => SelectedMaterial != null && GeometryDefinition != null);
        Apply1DMaterialCommand    = new RelayCommand(Apply1DMaterial, () => SelectedMaterial != null);
        OpenAddMaterialDialogCommand  = new RelayCommand(ExecuteOpenAddMaterialDialog);
        EditMaterialCommand           = new RelayCommand(ExecuteEditMaterial, () => _selectedMaterialModel != null);
        RemoveMaterialCommand         = new RelayCommand(ExecuteRemoveMaterial, () =>
            _selectedMaterialModel != null || DeleteTargetsAllMaterials);
        DuplicateMaterialCommand      = new RelayCommand(ExecuteDuplicateMaterial, () => _selectedMaterialModel != null);
        AssignMaterialToRegionCommand   = new RelayCommand(ExecuteAssignMaterialToRegion);
        ClearMaterialAssignmentsCommand = new RelayCommand(ExecuteClearMaterialAssignments);
        OpenMaterialLibraryCommand      = new RelayCommand(() => RequestMaterialLibraryWindow?.Invoke());
        ImportMaterialDatabaseCommand   = new RelayCommand(ExecuteImportMaterialDatabase);
        ExportMaterialsCommand          = new RelayCommand(ExecuteExportMaterials);
        AssignMaterialFromRowCommand    = new RelayCommand<MaterialModel>(ExecuteAssignMaterialFromRow);
        CancelAssignMaterialModeCommand = new RelayCommand(ExitAssignMaterialMode);
        SelectedAnalysisCategory  = AnalysisCategories.Count > 0 ? AnalysisCategories[0] : null;
        Materials.CollectionChanged += OnMaterialsCollectionChanged;
    }

    private void OnMaterialsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(MaterialTypeCount));
        OnPropertyChanged(nameof(MaterialTypeCountSummary));
        if (e.NewItems != null)
        {
            foreach (MaterialModel m in e.NewItems)
                HookMaterialForColourSync(m);
        }

        if (e.OldItems != null)
        {
            foreach (MaterialModel m in e.OldItems)
                UnhookMaterialForColourSync(m);
        }

        ReconcileSrfFailureCriterion(null);
    }

    private void Apply1DMaterial()
    {
        var source = SelectedMaterial;
        if (source == null || GeometryDefinition == null) return;
        var tag = source.Tag ?? "";
        foreach (var seg in GeometryDefinition.GeometryLineSegments)
        {
            if (seg == source) continue;
            if ((seg.Tag ?? "") == tag)
                seg.YoungsModulus = source.YoungsModulus;
        }
    }

    private void ApplyToMatchingLayers()
    {
        var source = SelectedMaterial;
        if (source == null || GeometryDefinition == null) return;
        var tag = source.Tag ?? "";
        foreach (var seg in GeometryDefinition.GeometryLineSegments)
        {
            if (seg == source) continue;
            if ((seg.Tag ?? "") == tag)
            {
                seg.UnitWeight = source.UnitWeight;
                seg.IsSaturatedUnitWeightEnabled = source.IsSaturatedUnitWeightEnabled;
                seg.SaturatedUnitWeight = source.SaturatedUnitWeight;
                seg.Cohesion = source.Cohesion;
                seg.FrictionAngle = source.FrictionAngle;
                seg.YoungsModulus = source.YoungsModulus;
                seg.PoissonsRatio = source.PoissonsRatio;
                seg.Density = source.Density;
                seg.ConsiderDensityForFE = source.ConsiderDensityForFE;
                seg.PlaneStrain = source.PlaneStrain;
            }
        }
    }

    /// <summary>Called by MainWindowViewModel when StagedConstructionViewModel raises a property change.</summary>
    public void OnStagedConstructionPropertyChanged(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(StagedConstructionViewModel.IsExcavateMode):
                OnPropertyChanged(nameof(IsExcavateMode));
                OnPropertyChanged(nameof(ExcavateModeButtonLabel));
                break;
            case nameof(StagedConstructionViewModel.ExcavateModeButtonLabel):
                OnPropertyChanged(nameof(ExcavateModeButtonLabel));
                break;
            case nameof(StagedConstructionViewModel.StatusMessage):
                OnPropertyChanged(nameof(StagedConstructionStatusMessage));
                break;
        }
    }

    private FEAnalysisViewModel? _feAnalysis;

    /// <summary>Wires FE staged-analysis flag so Materials &amp; Regions can show the Stage analysis group.</summary>
    public void SetFeAnalysisViewModel(FEAnalysisViewModel? fe)
    {
        if (_feAnalysis != null)
            _feAnalysis.PropertyChanged -= OnFeAnalysisPropertyChangedForStagedUi;
        _feAnalysis = fe;
        if (_feAnalysis != null)
            _feAnalysis.PropertyChanged += OnFeAnalysisPropertyChangedForStagedUi;
        OnPropertyChanged(nameof(FEAnalysis));
        OnPropertyChanged(nameof(IsStagedAnalysisEnabled));
    }

    public FEAnalysisViewModel? FEAnalysis => _feAnalysis;

    /// <summary>True when File → Project Settings → Analysis Type → Enable Staged Analysis is checked.</summary>
    public bool IsStagedAnalysisEnabled => _feAnalysis?.StagedExcavationEnabled == true;

    private void OnFeAnalysisPropertyChangedForStagedUi(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FEAnalysisViewModel.StagedExcavationEnabled))
            OnPropertyChanged(nameof(IsStagedAnalysisEnabled));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One row in Materials &amp; Regions → Layer–Material Assignments; ties to a <see cref="LayerCoordinateSummary"/> and project material library.
/// </summary>
public class LayerAssignmentRow : INotifyPropertyChanged, IDisposable
{
    private readonly MaterialPropertiesViewModel _owner;
    private readonly LayerCoordinateSummary _summary;
    private MaterialModel? _subscribedMaterial;

    public LayerAssignmentRow(MaterialPropertiesViewModel owner, LayerCoordinateSummary summary)
    {
        _owner = owner;
        _summary = summary;
        summary.PropertyChanged += OnSummaryPropertyChanged;
        RefreshMaterialSubscription();
    }

    private void OnSummaryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayerCoordinateSummary.LayerName) or nameof(LayerCoordinateSummary.LayerPickerDisplay))
        {
            OnPropertyChanged(nameof(LayerDisplayName));
            OnPropertyChanged(nameof(LayerPickerDisplay));
        }
    }

    public int DisplayLayerNumber => _summary.DisplayLayerNumber;

    public string LayerDisplayName => _summary.LayerName;

    public string LayerPickerDisplay => _summary.LayerPickerDisplay;

    public MaterialModel? AssignedMaterial
    {
        get
        {
            var id = _owner.GeometryDefinition.GetLayerMaterialAssignmentMaterialId(DisplayLayerNumber);
            return id == null ? null : _owner.Materials.FirstOrDefault(m => m.Id == id.Value);
        }
        set
        {
            _owner.GeometryDefinition.SetLayerMaterialAssignmentMaterialId(DisplayLayerNumber, value?.Id);
            if (value != null)
                _owner.GeometryDefinition.SetLayerDisplayColor(DisplayLayerNumber, value.Colour);
            RefreshMaterialSubscription();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AssignedMaterialColour));
        }
    }

    public System.Windows.Media.SolidColorBrush? AssignedMaterialColour => AssignedMaterial?.ColourBrush;

    public void NotifyMaterialAppearanceChanged()
    {
        OnPropertyChanged(nameof(AssignedMaterial));
        OnPropertyChanged(nameof(AssignedMaterialColour));
    }

    private void RefreshMaterialSubscription()
    {
        var mat = AssignedMaterial;
        if (ReferenceEquals(_subscribedMaterial, mat)) return;
        if (_subscribedMaterial != null)
            _subscribedMaterial.PropertyChanged -= OnSubscribedMaterialPropertyChanged;
        _subscribedMaterial = mat;
        if (_subscribedMaterial != null)
            _subscribedMaterial.PropertyChanged += OnSubscribedMaterialPropertyChanged;
    }

    private void OnSubscribedMaterialPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MaterialModel.Colour) or nameof(MaterialModel.ColourBrush))
            NotifyMaterialAppearanceChanged();
    }

    public void Dispose()
    {
        _summary.PropertyChanged -= OnSummaryPropertyChanged;
        if (_subscribedMaterial != null)
            _subscribedMaterial.PropertyChanged -= OnSubscribedMaterialPropertyChanged;
        _subscribedMaterial = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
