using System;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Models;

/// <summary>
/// Derives <see cref="AnalysisKind"/> and <see cref="WaterParticipation"/> from the
/// Analysis tree (category ON + Flow-Seepage leaf). Not a second picker.
/// </summary>
public static class AnalysisKindMapper
{
    public static WaterParticipation ResolveWaterParticipation(string? treeLeafOrMode, bool groundwaterOn)
    {
        if (!groundwaterOn)
            return WaterParticipation.None;

        var leaf = MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(treeLeafOrMode);
        if (MaterialPropertiesViewModel.IsGroundwaterFullyCoupledTreeType(leaf)
            || string.Equals(treeLeafOrMode, MaterialPropertiesViewModel.GroundwaterFullyCoupledType, StringComparison.Ordinal))
            return WaterParticipation.FullyCoupled;
        if (MaterialPropertiesViewModel.IsGroundwaterSequentialTreeType(leaf)
            || string.Equals(treeLeafOrMode, MaterialPropertiesViewModel.GroundwaterSequentialType, StringComparison.Ordinal))
            return WaterParticipation.Sequential;
        if (MaterialPropertiesViewModel.IsGroundwaterTransientStateTreeType(treeLeafOrMode)
            || MaterialPropertiesViewModel.IsTransientFlowAnalysisType(leaf))
            return WaterParticipation.Dissipation;
        if (MaterialPropertiesViewModel.IsGroundwaterSteadyStateTreeType(treeLeafOrMode)
            || MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(leaf))
            return WaterParticipation.Seepage;
        if (string.Equals(leaf, MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal)
            || string.Equals(treeLeafOrMode, MaterialPropertiesViewModel.GroundwaterStaticType, StringComparison.Ordinal))
            return WaterParticipation.Prescribed;
        return WaterParticipation.None;
    }

    public static string WaterParticipationSummary(WaterParticipation participation) => participation switch
    {
        WaterParticipation.Prescribed =>
            "Prescribed pore pressure (WT / Ru / B-bar / grid) → Terzaghi σ′ = σ + u",
        WaterParticipation.Seepage =>
            "FE seepage heads → u; optional one-way transfer into SRF",
        WaterParticipation.Dissipation =>
            "Transient dissipation: θ-method excess u(t) (not two-way Biot coupling)",
        WaterParticipation.Sequential =>
            "Sequential undrained increment → dissipate → update σ (not in this release)",
        WaterParticipation.FullyCoupled =>
            "Fully coupled solid–fluid (Biot). Theory citation only — not in this release",
        _ => "Flow-Seepage/Groundwater is OFF"
    };

    /// <summary>
    /// Run-FE kind from category ON flags plus the remembered Flow-Seepage leaf.
    /// Coupling (SRF + GW) is armed by ON switches; Transient is never treated as Static coupling.
    /// </summary>
    public static AnalysisKind Resolve(
        string? selectedCategory,
        string? detailedType,
        string? lastGroundwaterTreeMode,
        bool srfOn,
        bool groundwaterOn,
        bool selectedCategoryOn,
        bool is2DProblem)
    {
        if (!is2DProblem)
            return selectedCategoryOn ? AnalysisKind.Structural : AnalysisKind.Off;

        var water = ResolveWaterParticipation(lastGroundwaterTreeMode ?? detailedType, groundwaterOn);

        if (srfOn && groundwaterOn && water == WaterParticipation.Seepage)
            return AnalysisKind.CoupledSeepageSrf;
        if (srfOn && groundwaterOn && water == WaterParticipation.Prescribed)
            return AnalysisKind.CoupledStaticSrf;

        if (!selectedCategoryOn)
            return AnalysisKind.Off;

        if (MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(selectedCategory))
        {
            if (MaterialPropertiesViewModel.IsTransientFlowAnalysisType(detailedType)
                || MaterialPropertiesViewModel.IsGroundwaterTransientStateTreeType(detailedType)
                || water == WaterParticipation.Dissipation)
                return AnalysisKind.GroundwaterTransient;
            if (MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(detailedType)
                || MaterialPropertiesViewModel.IsGroundwaterSteadyStateTreeType(detailedType)
                || water == WaterParticipation.Seepage)
                return AnalysisKind.GroundwaterSteady;
            return AnalysisKind.GroundwaterStatic;
        }

        if (string.Equals(selectedCategory, MaterialPropertiesViewModel.SrfAnalysisCategory, StringComparison.Ordinal))
            return AnalysisKind.Srf;

        return AnalysisKind.Structural;
    }

    /// <summary>Older .xfem category / type labels → derived kind (no new persist field).</summary>
    public static AnalysisKind InferAnalysisKindFromLegacy(string? category, string? detailedType)
    {
        category = MaterialPropertiesViewModel.NormalizeAnalysisCategory(category);
        detailedType = MaterialPropertiesViewModel.NormalizeDetailedAnalysisType(detailedType);

        if (string.Equals(category, "Consolidation / diffusion / transient", StringComparison.Ordinal)
            || MaterialPropertiesViewModel.IsTransientFlowAnalysisType(detailedType))
            return AnalysisKind.GroundwaterTransient;

        if (MaterialPropertiesViewModel.IsGroundwaterAnalysisCategory(category))
        {
            if (MaterialPropertiesViewModel.IsSeepageFlowAnalysisType(detailedType)
                || MaterialPropertiesViewModel.IsGroundwaterSteadyStateTreeType(detailedType))
                return AnalysisKind.GroundwaterSteady;
            if (MaterialPropertiesViewModel.IsTransientFlowAnalysisType(detailedType)
                || MaterialPropertiesViewModel.IsGroundwaterTransientStateTreeType(detailedType))
                return AnalysisKind.GroundwaterTransient;
            return AnalysisKind.GroundwaterStatic;
        }

        if (string.Equals(category, MaterialPropertiesViewModel.SrfAnalysisCategory, StringComparison.Ordinal))
            return AnalysisKind.Srf;

        return AnalysisKind.Structural;
    }

    /// <summary>Applies a derived kind back onto the legacy category + leaf (no Kind combo).</summary>
    public static void ApplyAnalysisKindToLegacyCategory(MaterialPropertiesViewModel materials, AnalysisKind kind)
    {
        ArgumentNullException.ThrowIfNull(materials);
        switch (kind)
        {
            case AnalysisKind.GroundwaterStatic:
                materials.SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory;
                materials.SelectedDetailedAnalysisType = MaterialPropertiesViewModel.GroundwaterStaticType;
                materials.RememberGroundwaterTreeMode(MaterialPropertiesViewModel.GroundwaterStaticType);
                break;
            case AnalysisKind.GroundwaterSteady:
            case AnalysisKind.CoupledSeepageSrf:
                materials.SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory;
                var steady = MaterialPropertiesViewModel.IsSelectableSteadyStateSolver(materials.LastSteadyStateSolverType)
                    ? materials.LastSteadyStateSolverType!
                    : MaterialPropertiesViewModel.GroundwaterSeepageGeneralType;
                materials.SelectedDetailedAnalysisType = steady;
                materials.RememberGroundwaterTreeMode(MaterialPropertiesViewModel.GroundwaterSteadyStateType);
                break;
            case AnalysisKind.GroundwaterTransient:
                materials.SelectedAnalysisCategory = MaterialPropertiesViewModel.GroundwaterAnalysisCategory;
                var transient = MaterialPropertiesViewModel.IsSelectableTransientSolver(materials.LastTransientSolverType)
                    ? materials.LastTransientSolverType!
                    : MaterialPropertiesViewModel.GroundwaterTransientPlaneAxisymType;
                materials.SelectedDetailedAnalysisType = transient;
                materials.RememberGroundwaterTreeMode(MaterialPropertiesViewModel.GroundwaterTransientStateType);
                break;
            case AnalysisKind.Srf:
            case AnalysisKind.CoupledStaticSrf:
                materials.SelectedAnalysisCategory = MaterialPropertiesViewModel.SrfAnalysisCategory;
                if (kind == AnalysisKind.CoupledStaticSrf)
                    materials.RememberGroundwaterTreeMode(MaterialPropertiesViewModel.GroundwaterStaticType);
                break;
            case AnalysisKind.Off:
                break;
            default:
                materials.SelectedAnalysisCategory = "Structural – static elasticity";
                break;
        }
    }
}
