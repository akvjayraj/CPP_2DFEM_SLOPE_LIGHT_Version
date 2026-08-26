using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

public sealed record P610AnalysisOptions
{
    public FailureCriterion FailureCriterion { get; init; } = FailureCriterion.Elastic;
    public MaterialType MaterialType { get; init; } = MaterialType.Isotropic;
    public string MeshType { get; init; } = "T3";
    public string MeshSource { get; init; } = "";
    public string SrfMode { get; init; } = "";
    public double? SrfF { get; init; }
    /// <summary>SRF search mode for GUI solver control (independent of solver-level <see cref="SrfMode"/>): "sweep" | "bracket".</summary>
    public string SrfSearchMode { get; init; } = "";
    public bool? SrfBracketAuto { get; init; }
    public double? SrfBracketSeed { get; init; }
    public double? SrfBracketExpansionFactor { get; init; }
    public int? SrfMaxExpansionSteps { get; init; }
    public double? SrfFMin { get; init; }
    public double? SrfFMax { get; init; }
    public double? SrfBracketTol { get; init; }
    public int? SrfMaxBracketIters { get; init; }
    /// <summary>Gravity-only SRF in-situ path: <c>gravity</c> | <c>k0</c> (resolved Auto token).</summary>
    public string SrfInsituMode { get; init; } = "";
    /// <summary>Pseudo-static horizontal seismic coefficient from <c>! k_h</c> (Load Conditions / Script Editor).</summary>
    public double? Kh { get; init; }
    /// <summary>Pseudo-static vertical seismic coefficient from <c>! k_v</c>.</summary>
    public double? Kv { get; init; }
    public string PreferredExecutable { get; init; } = "";
    /// <summary><c>plane</c> or <c>axisymmetric</c> continuum kinematics for SRF (<c>! analysis_type</c>).</summary>
    public string AnalysisType { get; init; } = "";
    public ExpectedStressBlock? Expected { get; init; }
    public ExpectedRadialFieldBlock? ExpectedRadialField { get; init; }
    public ExpectedBearingCapacityBlock? ExpectedBearing { get; init; }
    public ExpectedSlopeStabilityBlock? ExpectedSlope { get; init; }
    public ExpectedSalenconBlock? ExpectedSalencon { get; init; }
    public ExpectedPeckMairBlock? ExpectedPeckMair { get; init; }
    public ExpectedFemDeltaBlock? ExpectedFemDelta { get; init; }
}

/// <summary>
/// Optional Peck-Mair Gaussian surface-settlement expectation declared via the .dat
/// AnalysisOptions header. Drives a shallow-tunnel verification row that compares
/// the FEM surface settlement trough magnitude to the analytical Gaussian peak.
///
/// Required header keys:
///   ! expected_pm_mode peck_mair_gaussian
///   ! expected_pm_z0 &lt;depth_to_axis_m&gt;
///   ! expected_pm_a &lt;tunnel_radius_m&gt;
///   ! expected_pm_VL_pct &lt;volume_loss_percent&gt;
///   ! expected_pm_K &lt;trough_width_param&gt;       (typical 0.4-0.7)
/// Optional:
///   ! expected_pm_tolerance_pct &lt;pct&gt;          (default 15.0)
///   ! expected_pm_surface_y &lt;y_m&gt;              (default 0.0)
/// </summary>
public sealed record ExpectedPeckMairBlock(
    string Mode,
    double Z0,
    double A,
    double VolumeLossPct,
    double K,
    double SurfaceY,
    double TolerancePct);

/// <summary>
/// Optional FEM-vs-FEM delta expectation declared via the .dat AnalysisOptions
/// header. Used by the twin-tunnel and tunnel-near-slope benchmarks where the
/// closed-form is itself another FEM run (a baseline). The .json output is
/// compared against a sibling baseline FEM result file shipped alongside.
///
/// Required header keys:
///   ! expected_fem_delta_mode &lt;label&gt;             (e.g. twin_tunnel, near_slope)
///   ! expected_fem_delta_baseline &lt;baseline_basename&gt;  (resolved relative to the .dat folder)
/// Optional:
///   ! expected_fem_delta_metric {umax|sxx_mean|syy_mean}  (default umax)
///   ! expected_fem_delta_tolerance_pct &lt;pct&gt;     (default 15.0)
///   ! expected_fem_delta_min_change_pct &lt;pct&gt;    (require at least this much delta to pass)
/// </summary>
public sealed record ExpectedFemDeltaBlock(
    string Mode,
    string BaselineBasename,
    string Metric,
    double TolerancePct,
    double MinChangePct);

/// <summary>
/// Optional analytical/expected stresses declared via the .dat AnalysisOptions
/// header (! expected_sxx_kPa / _syy_kPa / _txy_kPa / _szz_kPa / tolerance_kPa).
/// Drives the auto-triggered Verification-tab patch comparison and mirrors what
/// tools/build_all_nafems_smoke_packages.ps1 already consumes.
/// </summary>
public sealed record ExpectedStressBlock(
    double? Sxx,
    double? Syy,
    double? Txy,
    double? Szz,
    double? TolerancekPa);

/// <summary>
/// Optional closed-form radial-stress field declared via the .dat AnalysisOptions
/// header. Used by NAFEMS LE10 / LE11 thick-cylinder benchmarks (Lame solution).
/// All elements in the mesh are evaluated against the closed-form at their
/// centroid radius (rather than just at hard-coded ring stations).
///
/// Required header keys:
///   ! expected_radial_mode lame_pressurized_cylinder
///   ! expected_radial_inner_a &lt;a&gt;
///   ! expected_radial_outer_b &lt;b&gt;
///   ! expected_radial_pressure_kPa &lt;p&gt;
///   ! expected_radial_nu &lt;nu&gt;
///   ! expected_radial_tolerance_kPa &lt;tol&gt;
/// Optional:
///   ! expected_radial_center_x &lt;cx&gt; (default 0)
///   ! expected_radial_center_y &lt;cy&gt; (default 0)
///   ! expected_radial_plane_strain {true|false} (default true; controls sigma_zz check)
///
/// Sign convention follows the rest of the solver (tension-positive). A positive
/// internal pressure produces compressive sigma_rr at r=a (i.e. sigma_rr(a) = -p).
/// </summary>
public sealed record ExpectedRadialFieldBlock(
    string Mode,
    double InnerRadius,
    double OuterRadius,
    double InternalPressure,
    double PoissonRatio,
    double CenterX,
    double CenterY,
    bool PlaneStrain,
    double TolerancekPa);

/// <summary>
/// Optional Prandtl-style bearing-capacity expectation declared via the .dat
/// AnalysisOptions header. Drives a Mohr-Coulomb-aware Verification-tab
/// comparison that reports load-transfer error and yield-saturation ratio
/// against the textbook q_ult = c * Nc.
///
/// Required header keys:
///   ! expected_qult_per_c &lt;Nc&gt;          (e.g. 5.14 for phi=0, 14.83 for phi=20)
///   ! expected_phi_deg &lt;phi&gt;
///   ! expected_c_kPa &lt;c&gt;
///   ! expected_p_applied_kPa &lt;p_applied&gt;  (the actual footing pressure used)
/// Optional:
///   ! expected_qult_tolerance_pct &lt;pct&gt;  (default 5.0 % of p_applied)
/// </summary>
public sealed record ExpectedBearingCapacityBlock(
    double QultPerC,
    double PhiDeg,
    double C_kPa,
    double PAppliedkPa,
    double TolerancePct);

/// <summary>
/// Optional slope-stability expectation declared via the .dat AnalysisOptions
/// header. Two complementary verification modes are supported:
///
/// <para><b>Option A — gravity λ-sweep (no solver changes):</b> a folder holds
/// several .dat copies of the same mesh, each with its material γ scaled by a
/// declared λ. The orchestrator runs every λ, classifies each as
/// converged / non-converged, then estimates FoS_FE as the midpoint between the
/// highest converged λ and the lowest non-converged λ. The expectation is that
/// FoS_FE matches the analytical FoS within tolerance.</para>
///
/// <para><b>Option B — solver-internal c-φ reduction (SRF):</b> a single .dat
/// declares <c>srf_mode 1</c> and the C++ solver bisects on a strength-reduction
/// factor F (φ→atan(tan φ/F), c→c/F) until non-convergence, then writes the
/// converged F into the .json. The orchestrator/GUI just reads it back and
/// compares to expected_FoS.</para>
///
/// Required header keys (both modes):
///   ! expected_FoS &lt;F&gt;
/// Optional:
///   ! expected_FoS_tolerance_pct &lt;pct&gt;     (default 15.0 %)
///   ! expected_FoS_mode {lambda_sweep|srf}   (default lambda_sweep)
///   ! lambda_factor &lt;λ&gt;                   (only on λ-sweep variants;
///                                              the per-file gravity multiplier)
///   ! slope_geometry &lt;label&gt;              (e.g. "taylor_phi0_vertical")
///   ! slope_angle_deg &lt;β&gt;
///   ! slope_height_m &lt;H&gt;
/// </summary>
public sealed record ExpectedSlopeStabilityBlock(
    double FoS,
    double TolerancePct,
    string Mode,
    double? LambdaFactor,
    string? Geometry,
    double? SlopeAngleDeg,
    double? SlopeHeightM);

/// <summary>
/// M2.3 Slice A. Optional Salencon (1969) closed-form expectation declared via
/// the .dat AnalysisOptions header. Drives a Mohr-Coulomb-aware Verification-tab
/// comparison that evaluates the analytical sigma_rr / sigma_theta / inward u_r
/// at every element centroid (filtered to the springline strip used by the
/// existing BuildSalenconRows path) and compares against the FEM run.
///
/// Required header keys:
///   ! expected_salencon_mode infinite_mc_tunnel
///   ! expected_salencon_a &lt;a&gt;            (tunnel radius, m)
///   ! expected_salencon_P0_kPa &lt;P0&gt;      (far-field hydrostatic pressure, compression-positive)
///   ! expected_salencon_phi_deg &lt;phi&gt;
///   ! expected_salencon_c_kPa &lt;c&gt;
///   ! expected_salencon_psi_deg &lt;psi&gt;
///   ! expected_salencon_E_kPa &lt;E&gt;
///   ! expected_salencon_nu &lt;nu&gt;
/// Optional:
///   ! expected_salencon_Pi_kPa &lt;Pi&gt;       (internal pressure; default 0)
///   ! expected_salencon_center_x &lt;cx&gt;    (default 0)
///   ! expected_salencon_center_y &lt;cy&gt;    (default 0)
///   ! expected_salencon_tolerance_pct &lt;pct&gt;  (default 5.0 %)
///   ! expected_salencon_stress_tolerance_kPa &lt;kPa&gt;  (absolute floor, default 50)
///   ! expected_salencon_disp_tolerance_m &lt;m&gt;        (absolute floor, default 1e-5)
/// </summary>
public sealed record ExpectedSalenconBlock(
    string Mode,
    double A,
    double P0_kPa,
    double Pi_kPa,
    double PhiDeg,
    double C_kPa,
    double PsiDeg,
    double E_kPa,
    double Nu,
    double CenterX,
    double CenterY,
    double TolerancePct,
    double StressTolKpa,
    double DispTolM);

public static class P610AnalysisOptionsHelper
{
    private const string BeginTag = "! <BEGIN:AnalysisOptions>";
    private const string EndTag = "! <END:AnalysisOptions>";

    public static string BuildBlock(P610AnalysisOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BeginTag);
        sb.AppendLine($"! failure_criterion {options.FailureCriterion}");
        sb.AppendLine($"! material_type {options.MaterialType}");
        sb.AppendLine($"! mesh_type {options.MeshType}");
        if (!string.IsNullOrWhiteSpace(options.MeshSource))
            sb.AppendLine($"! mesh_source {options.MeshSource}");
        if (!string.IsNullOrWhiteSpace(options.SrfMode))
            sb.AppendLine($"! srf_mode {options.SrfMode}");
        if (options.SrfF.HasValue)
            sb.AppendLine($"! srf_F {options.SrfF.Value.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(options.SrfSearchMode))
            sb.AppendLine($"! srf_search_mode {options.SrfSearchMode}");
        if (options.SrfBracketAuto.HasValue)
            sb.AppendLine($"! srf_bracket_auto {(options.SrfBracketAuto.Value ? "true" : "false")}");
        if (options.SrfBracketSeed.HasValue)
            sb.AppendLine($"! srf_bracket_seed {options.SrfBracketSeed.Value.ToString(CultureInfo.InvariantCulture)}");
        if (options.SrfBracketExpansionFactor.HasValue)
            sb.AppendLine($"! srf_bracket_expansion_factor {options.SrfBracketExpansionFactor.Value.ToString(CultureInfo.InvariantCulture)}");
        if (options.SrfMaxExpansionSteps.HasValue)
            sb.AppendLine($"! srf_max_expansion_steps {options.SrfMaxExpansionSteps.Value.ToString(CultureInfo.InvariantCulture)}");
        if (options.SrfFMin.HasValue)
            sb.AppendLine($"! srf_F_min {options.SrfFMin.Value.ToString(CultureInfo.InvariantCulture)}");
        if (options.SrfFMax.HasValue)
            sb.AppendLine($"! srf_F_max {options.SrfFMax.Value.ToString(CultureInfo.InvariantCulture)}");
        if (options.SrfBracketTol.HasValue)
            sb.AppendLine($"! srf_bracket_tol {options.SrfBracketTol.Value.ToString(CultureInfo.InvariantCulture)}");
        if (options.SrfMaxBracketIters.HasValue)
            sb.AppendLine($"! srf_max_bracket_iters {options.SrfMaxBracketIters.Value.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(options.SrfInsituMode))
            sb.AppendLine($"! srf_insitu_mode {options.SrfInsituMode.Trim().ToLowerInvariant()}");
        if (options.Kh.HasValue || options.Kv.HasValue)
        {
            sb.AppendLine($"! k_h {PseudoStaticSeismicRunHelper.FormatCoefficient(options.Kh ?? 0.0)}");
            sb.AppendLine($"! k_v {PseudoStaticSeismicRunHelper.FormatCoefficient(options.Kv ?? 0.0)}");
        }
        if (options.ExpectedSlope is { } slope)
        {
            sb.AppendLine($"! expected_FoS {slope.FoS.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"! expected_FoS_tolerance_pct {slope.TolerancePct.ToString(CultureInfo.InvariantCulture)}");
            if (!string.IsNullOrWhiteSpace(slope.Mode))
                sb.AppendLine($"! expected_FoS_mode {slope.Mode}");
        }
        if (!string.IsNullOrWhiteSpace(options.PreferredExecutable))
            sb.AppendLine($"! solver_executable {options.PreferredExecutable}");
        if (!string.IsNullOrWhiteSpace(options.AnalysisType))
            sb.AppendLine($"! analysis_type {options.AnalysisType.Trim().ToLowerInvariant()}");
        sb.AppendLine(EndTag);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Lightweight check for the presence of a <c>! &lt;BEGIN:AnalysisOptions&gt;</c> sentinel
    /// in the .dat buffer, without allocating or fully parsing the block. Used by the Script
    /// Editor router so a buffer carrying just the AnalysisOptions block (without the legacy
    /// <c>! solver:p610_t3</c> tag) still enters the p610 routing branch.
    /// </summary>
    public static bool HasAnalysisOptionsBlock(string datContent)
    {
        if (string.IsNullOrEmpty(datContent)) return false;
        return datContent.IndexOf(BeginTag, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Sets or updates <c>! srf_F</c> in the AnalysisOptions block for T3 SRF single-trial runs.</summary>
    public static bool TryPatchSrfF(string datContent, double srfF, out string patched, out bool changed)
    {
        patched = datContent ?? "";
        changed = false;
        if (string.IsNullOrWhiteSpace(datContent) || srfF <= 0.0)
            return false;

        if (!TryParse(datContent, out var options))
            return false;

        var updated = options with
        {
            SrfMode = string.IsNullOrWhiteSpace(options.SrfMode) ? "multi" : options.SrfMode,
            SrfF = srfF,
        };

        patched = ReplaceAnalysisOptionsBlock(datContent, updated);
        changed = !string.Equals(patched, datContent, StringComparison.Ordinal);
        return true;
    }

    /// <summary>
    /// Forces <c>! srf_mode single</c> + <c>! srf_F</c> in the AnalysisOptions block, regardless of the
    /// deck's existing mode. Used by the SRF bracket runner so a single-trial run at an arbitrary F
    /// (seed / expansion / bisection midpoint) is honored even for p64/Q8 decks that otherwise carry a
    /// <c>multi</c> tail (<c>nsrf</c> + F list) — <c>read_srf_controls</c> short-circuits to the single
    /// value when <c>srf_mode</c> is <c>single</c>, ignoring the trailing nsrf/F-list lines.
    /// </summary>
    public static bool TryPatchSrfFSingleTrial(string datContent, double srfF, out string patched, out bool changed)
    {
        patched = datContent ?? "";
        changed = false;
        if (string.IsNullOrWhiteSpace(datContent) || srfF <= 0.0)
            return false;

        if (!TryParse(datContent, out var options))
            return false;

        // Preserve Kh/Kv (and all other AnalysisOptions keys) so SRF trial rewrites cannot
        // strip pseudo-static seismic coefficients from the deck sent to the solver.
        var updated = options with
        {
            SrfMode = "single",
            SrfF = srfF,
        };

        patched = ReplaceAnalysisOptionsBlock(datContent, updated);
        changed = !string.Equals(patched, datContent, StringComparison.Ordinal);
        return true;
    }

    /// <summary>
    /// Writes the SRF bracket search settings (mode, auto-detect, seed, expansion factor, max
    /// expansion steps, F_min/F_max, tolerance, max bisection iterations) into the AnalysisOptions
    /// block, independent of the deck's tol/limit/incs/layers solver-control line or nsrf/F-list
    /// (bracket mode is GUI-orchestrated: single-trial F values are patched per solver call at run
    /// time via <see cref="TryPatchSrfFSingleTrial"/>/<see cref="TryPatchSrfF"/>, so the persisted
    /// tail is metadata only, used to restore the GUI controls on reload).
    /// </summary>
    public static bool TryPatchSrfBracketSettings(
        string datContent,
        bool auto,
        double seed,
        double expansionFactor,
        int maxExpansionSteps,
        double fMin,
        double fMax,
        double bracketTol,
        int maxBracketIters,
        out string patched,
        out bool changed)
    {
        patched = datContent ?? "";
        changed = false;
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        if (!TryParse(datContent, out var options))
            return false;

        var updated = options with
        {
            SrfSearchMode = "bracket",
            SrfBracketAuto = auto,
            SrfBracketSeed = seed,
            SrfBracketExpansionFactor = expansionFactor,
            SrfMaxExpansionSteps = maxExpansionSteps,
            SrfFMin = fMin,
            SrfFMax = fMax,
            SrfBracketTol = bracketTol,
            SrfMaxBracketIters = maxBracketIters,
        };

        patched = ReplaceAnalysisOptionsBlock(datContent, updated);
        changed = !string.Equals(patched, datContent, StringComparison.Ordinal);
        return true;
    }

    /// <summary>Writes resolved <c>! srf_insitu_mode</c> (<c>k0</c>|<c>gravity</c>) into AnalysisOptions.</summary>
    public static bool TryPatchSrfInsituMode(
        string datContent,
        string insituModeToken,
        out string patched,
        out bool changed)
    {
        patched = datContent ?? "";
        changed = false;
        if (string.IsNullOrWhiteSpace(datContent))
            return false;
        if (!TryParse(datContent, out var options))
            return false;

        var token = (insituModeToken ?? "").Trim().ToLowerInvariant();
        if (token is not ("k0" or "gravity" or "keep_k0"))
            token = "gravity";
        if (token == "keep_k0")
            token = "k0";

        var updated = options with { SrfInsituMode = token };
        patched = ReplaceAnalysisOptionsBlock(datContent, updated);
        changed = !string.Equals(patched, datContent, StringComparison.Ordinal);
        return true;
    }

    /// <summary>Writes <c>! srf_search_mode sweep</c> so a previously-bracket deck round-trips back to sweep mode cleanly.</summary>
    public static bool TryPatchSrfSearchModeSweep(string datContent, out string patched, out bool changed)
    {
        patched = datContent ?? "";
        changed = false;
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        if (!TryParse(datContent, out var options))
            return false;

        if (string.IsNullOrWhiteSpace(options.SrfSearchMode) || options.SrfSearchMode.Equals("sweep", StringComparison.OrdinalIgnoreCase))
            return true;

        // Keep Kh/Kv and other keys; only flip search mode. Bracket numeric keys stay in the
        // block (harmless metadata) so a later switch back to bracket can still round-trip.
        var updated = options with { SrfSearchMode = "sweep" };

        patched = ReplaceAnalysisOptionsBlock(datContent, updated);
        changed = !string.Equals(patched, datContent, StringComparison.Ordinal);
        return true;
    }

    public static P610AnalysisOptions BuildFromViewModels(
        MaterialPropertiesViewModel? materialsVM,
        MeshViewModel? meshVM,
        FEAnalysisViewModel? feVM = null,
        bool srfAxisymmetric = false)
    {
        var failure = materialsVM?.SelectedFailureCriterion ?? FailureCriterion.Elastic;
        var material = materialsVM?.SelectedMaterialType ?? MaterialType.Isotropic;
        var meshType = ResolveMeshType(meshVM);
        var isSrfMode = materialsVM?.IsSrfAnalysisMode == true || feVM?.IsSrfSolverMode == true;
        int matCount = materialsVM?.Materials?.Count ?? 0;
        var preferred = isSrfMode
            ? "SRF"
            : ResolveExecutableName(failure, material, meshType);

        double? srfF = null;
        if (isSrfMode && feVM?.SrfValues is { Count: > 0 } trials)
            srfF = trials[0].Value;

        ExpectedSlopeStabilityBlock? expectedSlope = null;
        if (isSrfMode && matCount >= 2 && failure == FailureCriterion.MohrCoulomb)
        {
            expectedSlope = new ExpectedSlopeStabilityBlock(
                FoS: 1.5,
                TolerancePct: 15.0,
                Mode: "srf",
                LambdaFactor: null,
                Geometry: "ex2_two_layer",
                SlopeAngleDeg: null,
                SlopeHeightM: null);
        }

        var srfInsitu = isSrfMode && feVM != null
            ? feVM.ResolveEffectiveSrfInsituModeToken()
            : "";

        var axisym = srfAxisymmetric || (feVM?.IsSrfAxisymmetric == true);

        return new P610AnalysisOptions
        {
            FailureCriterion = failure,
            MaterialType = material,
            MeshType = meshType,
            SrfMode = isSrfMode ? "multi" : "",
            SrfF = srfF ?? (isSrfMode ? 1.0 : null),
            SrfInsituMode = srfInsitu,
            PreferredExecutable = preferred,
            AnalysisType = isSrfMode && axisym ? "axisymmetric" : "",
            ExpectedSlope = expectedSlope,
        };
    }

    public static bool TryParse(string datContent, out P610AnalysisOptions options)
    {
        options = new P610AnalysisOptions();
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        var block = ExtractBlock(datContent);
        if (string.IsNullOrWhiteSpace(block))
            return false;

        var failure = TryParseEnumValue<FailureCriterion>(block, "failure_criterion", out var fc)
            ? fc : FailureCriterion.Elastic;
        var material = TryParseEnumValue<MaterialType>(block, "material_type", out var mt)
            ? mt : MaterialType.Isotropic;
        var mesh = TryParseTokenValue(block, "mesh_type", out var meshToken)
            ? NormalizeMeshTypeToken(meshToken) : "T3";
        var exe = TryParseTokenValue(block, "solver_executable", out var exeToken)
            ? exeToken : "";
        var analysisType = TryParseTokenValue(block, "analysis_type", out var analysisTypeTok)
            ? analysisTypeTok.ToLowerInvariant() : "";
        var meshSource = TryParseTokenValue(block, "mesh_source", out var meshSrcTok)
            ? meshSrcTok.ToLowerInvariant() : "";
        var srfMode = TryParseTokenValue(block, "srf_mode", out var srfModeTok)
            ? srfModeTok.ToLowerInvariant() : "";
        double? srfF = TryParseDoubleValue(block, "srf_F", out var srfFVal) && srfFVal > 0
            ? srfFVal : null;
        var srfSearchMode = TryParseTokenValue(block, "srf_search_mode", out var srfSearchModeTok)
            ? srfSearchModeTok.ToLowerInvariant() : "";
        bool? srfBracketAuto = TryParseTokenValue(block, "srf_bracket_auto", out var bracketAutoTok)
            ? !bracketAutoTok.Equals("false", StringComparison.OrdinalIgnoreCase)
              && !bracketAutoTok.Equals("0", StringComparison.OrdinalIgnoreCase)
            : (bool?)null;
        double? srfBracketSeed = TryParseDoubleValue(block, "srf_bracket_seed", out var seedVal) ? seedVal : null;
        double? srfBracketExpansionFactor = TryParseDoubleValue(block, "srf_bracket_expansion_factor", out var expFactorVal)
            ? expFactorVal : null;
        int? srfMaxExpansionSteps = TryParseIntValue(block, "srf_max_expansion_steps", out var maxExpStepsVal)
            ? maxExpStepsVal : null;
        double? srfFMin = TryParseDoubleValue(block, "srf_F_min", out var fMinVal) ? fMinVal : null;
        double? srfFMax = TryParseDoubleValue(block, "srf_F_max", out var fMaxVal) ? fMaxVal : null;
        double? srfBracketTol = TryParseDoubleValue(block, "srf_bracket_tol", out var bracketTolVal) ? bracketTolVal : null;
        int? srfMaxBracketIters = TryParseIntValue(block, "srf_max_bracket_iters", out var maxBracketItersVal)
            ? maxBracketItersVal : null;
        var srfInsituMode = TryParseTokenValue(block, "srf_insitu_mode", out var insituTok)
            ? insituTok.ToLowerInvariant() : "";
        double? kh = TryParseDoubleValue(block, "k_h", out var khVal) ? khVal : null;
        double? kv = TryParseDoubleValue(block, "k_v", out var kvVal) ? kvVal : null;

        ExpectedStressBlock? expected = null;
        bool hasSxx = TryParseDoubleValue(block, "expected_sxx_kPa", out var sxx);
        bool hasSyy = TryParseDoubleValue(block, "expected_syy_kPa", out var syy);
        bool hasTxy = TryParseDoubleValue(block, "expected_txy_kPa", out var txy);
        bool hasSzz = TryParseDoubleValue(block, "expected_szz_kPa", out var szz);
        bool hasTol = TryParseDoubleValue(block, "tolerance_kPa", out var tolKpa);
        if (hasSxx || hasSyy || hasTxy || hasSzz || hasTol)
        {
            expected = new ExpectedStressBlock(
                Sxx: hasSxx ? sxx : null,
                Syy: hasSyy ? syy : null,
                Txy: hasTxy ? txy : null,
                Szz: hasSzz ? szz : null,
                TolerancekPa: hasTol ? tolKpa : null);
        }

        ExpectedRadialFieldBlock? radial = null;
        if (TryParseTokenValue(block, "expected_radial_mode", out var radialMode))
        {
            bool hasA = TryParseDoubleValue(block, "expected_radial_inner_a", out var aVal);
            bool hasB = TryParseDoubleValue(block, "expected_radial_outer_b", out var bVal);
            bool hasP = TryParseDoubleValue(block, "expected_radial_pressure_kPa", out var pVal);
            bool hasNu = TryParseDoubleValue(block, "expected_radial_nu", out var nuVal);
            bool hasRadTol = TryParseDoubleValue(block, "expected_radial_tolerance_kPa", out var radTolVal);
            // Optional center & plane-strain toggle. Default: centered at origin, plane-strain on.
            if (!TryParseDoubleValue(block, "expected_radial_center_x", out var cx)) cx = 0.0;
            if (!TryParseDoubleValue(block, "expected_radial_center_y", out var cy)) cy = 0.0;
            bool planeStrain = true;
            if (TryParseTokenValue(block, "expected_radial_plane_strain", out var psToken))
                planeStrain = !psToken.Equals("false", StringComparison.OrdinalIgnoreCase)
                              && !psToken.Equals("0", StringComparison.OrdinalIgnoreCase);

            // Reuse the generic tolerance_kPa header if expected_radial_tolerance_kPa is absent.
            if (!hasRadTol && hasTol) { hasRadTol = true; radTolVal = tolKpa; }

            if (hasA && hasB && hasP && hasNu && hasRadTol && bVal > aVal && aVal > 0)
            {
                radial = new ExpectedRadialFieldBlock(
                    Mode: radialMode.ToLowerInvariant(),
                    InnerRadius: aVal,
                    OuterRadius: bVal,
                    InternalPressure: pVal,
                    PoissonRatio: nuVal,
                    CenterX: cx,
                    CenterY: cy,
                    PlaneStrain: planeStrain,
                    TolerancekPa: radTolVal);
            }
        }

        ExpectedBearingCapacityBlock? bearing = null;
        bool hasNc = TryParseDoubleValue(block, "expected_qult_per_c", out var ncVal);
        bool hasPhi = TryParseDoubleValue(block, "expected_phi_deg", out var phiVal);
        bool hasC  = TryParseDoubleValue(block, "expected_c_kPa", out var cVal);
        bool hasPApp = TryParseDoubleValue(block, "expected_p_applied_kPa", out var pAppVal);
        if (!TryParseDoubleValue(block, "expected_qult_tolerance_pct", out var bearingTolPct)) bearingTolPct = 5.0;
        if (hasNc && hasPhi && hasC && hasPApp && pAppVal > 0)
        {
            bearing = new ExpectedBearingCapacityBlock(
                QultPerC: ncVal,
                PhiDeg: phiVal,
                C_kPa: cVal,
                PAppliedkPa: pAppVal,
                TolerancePct: bearingTolPct);
        }

        ExpectedSalenconBlock? salencon = null;
        if (TryParseTokenValue(block, "expected_salencon_mode", out var salenconMode))
        {
            bool hasA = TryParseDoubleValue(block, "expected_salencon_a", out var aSal);
            bool hasP0 = TryParseDoubleValue(block, "expected_salencon_P0_kPa", out var p0Sal);
            bool hasPhi2 = TryParseDoubleValue(block, "expected_salencon_phi_deg", out var phiSal);
            bool hasC2 = TryParseDoubleValue(block, "expected_salencon_c_kPa", out var cSal);
            bool hasPsi = TryParseDoubleValue(block, "expected_salencon_psi_deg", out var psiSal);
            bool hasE = TryParseDoubleValue(block, "expected_salencon_E_kPa", out var eSal);
            bool hasNu = TryParseDoubleValue(block, "expected_salencon_nu", out var nuSal);
            if (!TryParseDoubleValue(block, "expected_salencon_Pi_kPa", out var piSal)) piSal = 0.0;
            if (!TryParseDoubleValue(block, "expected_salencon_center_x", out var cxSal)) cxSal = 0.0;
            if (!TryParseDoubleValue(block, "expected_salencon_center_y", out var cySal)) cySal = 0.0;
            if (!TryParseDoubleValue(block, "expected_salencon_tolerance_pct", out var salTolPct)) salTolPct = 5.0;
            if (!TryParseDoubleValue(block, "expected_salencon_stress_tolerance_kPa", out var salStressTol)) salStressTol = 50.0;
            if (!TryParseDoubleValue(block, "expected_salencon_disp_tolerance_m", out var salDispTol)) salDispTol = 1e-5;

            if (hasA && hasP0 && hasPhi2 && hasC2 && hasPsi && hasE && hasNu
                && aSal > 0 && p0Sal > 0 && phiSal > 0 && phiSal < 90 && eSal > 0)
            {
                salencon = new ExpectedSalenconBlock(
                    Mode: salenconMode.ToLowerInvariant(),
                    A: aSal,
                    P0_kPa: p0Sal,
                    Pi_kPa: piSal,
                    PhiDeg: phiSal,
                    C_kPa: cSal,
                    PsiDeg: psiSal,
                    E_kPa: eSal,
                    Nu: nuSal,
                    CenterX: cxSal,
                    CenterY: cySal,
                    TolerancePct: salTolPct,
                    StressTolKpa: salStressTol,
                    DispTolM: salDispTol);
            }
        }

        ExpectedSlopeStabilityBlock? slope = null;
        if (TryParseDoubleValue(block, "expected_FoS", out var fosVal) && fosVal > 0)
        {
            if (!TryParseDoubleValue(block, "expected_FoS_tolerance_pct", out var fosTolPct))
                fosTolPct = 15.0;
            string fosMode = TryParseTokenValue(block, "expected_FoS_mode", out var fosModeTok)
                ? fosModeTok.ToLowerInvariant() : "lambda_sweep";
            double? lambdaFactor = TryParseDoubleValue(block, "lambda_factor", out var lamVal)
                ? lamVal : (double?)null;
            string? geometry = TryParseTokenValue(block, "slope_geometry", out var geoTok)
                ? geoTok : null;
            double? slopeAngle = TryParseDoubleValue(block, "slope_angle_deg", out var betaVal)
                ? betaVal : (double?)null;
            double? slopeHeight = TryParseDoubleValue(block, "slope_height_m", out var hVal)
                ? hVal : (double?)null;
            slope = new ExpectedSlopeStabilityBlock(
                FoS: fosVal,
                TolerancePct: fosTolPct,
                Mode: fosMode,
                LambdaFactor: lambdaFactor,
                Geometry: geometry,
                SlopeAngleDeg: slopeAngle,
                SlopeHeightM: slopeHeight);
        }

        ExpectedPeckMairBlock? peckMair = null;
        if (TryParseTokenValue(block, "expected_pm_mode", out var pmMode))
        {
            bool hasZ0 = TryParseDoubleValue(block, "expected_pm_z0", out var z0);
            bool hasAr = TryParseDoubleValue(block, "expected_pm_a", out var aPm);
            bool hasVl = TryParseDoubleValue(block, "expected_pm_VL_pct", out var vlPct);
            bool hasK  = TryParseDoubleValue(block, "expected_pm_K", out var kVal);
            if (!TryParseDoubleValue(block, "expected_pm_surface_y", out var ySurf)) ySurf = 0.0;
            if (!TryParseDoubleValue(block, "expected_pm_tolerance_pct", out var pmTolPct)) pmTolPct = 15.0;
            if (hasZ0 && hasAr && hasVl && hasK && z0 > 0 && aPm > 0)
            {
                peckMair = new ExpectedPeckMairBlock(
                    Mode: pmMode.ToLowerInvariant(),
                    Z0: z0,
                    A: aPm,
                    VolumeLossPct: vlPct,
                    K: kVal,
                    SurfaceY: ySurf,
                    TolerancePct: pmTolPct);
            }
        }

        ExpectedFemDeltaBlock? femDelta = null;
        if (TryParseTokenValue(block, "expected_fem_delta_mode", out var fdMode)
            && TryParseTokenValue(block, "expected_fem_delta_baseline", out var fdBase))
        {
            string metric = TryParseTokenValue(block, "expected_fem_delta_metric", out var mTok)
                ? mTok.ToLowerInvariant() : "umax";
            if (!TryParseDoubleValue(block, "expected_fem_delta_tolerance_pct", out var fdTolPct)) fdTolPct = 15.0;
            if (!TryParseDoubleValue(block, "expected_fem_delta_min_change_pct", out var fdMinChange)) fdMinChange = 0.0;
            femDelta = new ExpectedFemDeltaBlock(
                Mode: fdMode.ToLowerInvariant(),
                BaselineBasename: fdBase,
                Metric: metric,
                TolerancePct: fdTolPct,
                MinChangePct: fdMinChange);
        }

        options = new P610AnalysisOptions
        {
            FailureCriterion = failure,
            MaterialType = material,
            MeshType = mesh,
            MeshSource = meshSource,
            SrfMode = srfMode,
            SrfF = srfF,
            SrfSearchMode = srfSearchMode,
            SrfBracketAuto = srfBracketAuto,
            SrfBracketSeed = srfBracketSeed,
            SrfBracketExpansionFactor = srfBracketExpansionFactor,
            SrfMaxExpansionSteps = srfMaxExpansionSteps,
            SrfFMin = srfFMin,
            SrfFMax = srfFMax,
            SrfBracketTol = srfBracketTol,
            SrfMaxBracketIters = srfMaxBracketIters,
            SrfInsituMode = srfInsituMode,
            Kh = kh,
            Kv = kv,
            PreferredExecutable = exe,
            AnalysisType = analysisType,
            Expected = expected,
            ExpectedRadialField = radial,
            ExpectedBearing = bearing,
            ExpectedSlope = slope,
            ExpectedSalencon = salencon,
            ExpectedPeckMair = peckMair,
            ExpectedFemDelta = femDelta,
        };
        return true;
    }

    public static string ResolveExecutableName(FailureCriterion failure, MaterialType material)
        => ResolveExecutableName(failure, material, "T3");

    public static string ResolveExecutableName(FailureCriterion failure, MaterialType material, string? meshType)
    {
        var prefix = "p610_" + NormalizeMeshTypeToken(meshType).ToLowerInvariant();
        if (material is MaterialType.NonlinearIsotropic or MaterialType.DuncanChangHyperbolic or MaterialType.ViscoElastic)
            return $"{prefix}_nonlinear";
        if (material == MaterialType.Plastic && failure is FailureCriterion.Elastic or FailureCriterion.ElastoPlastic)
            return $"{prefix}_plastic";

        return failure switch
        {
            FailureCriterion.Elastic => material switch
            {
                MaterialType.TransverselyIsotropic => $"{prefix}_elastic",
                MaterialType.Orthotropic => $"{prefix}_elastic",
                MaterialType.Plastic => $"{prefix}_plastic",
                MaterialType.NonlinearIsotropic or MaterialType.DuncanChangHyperbolic or MaterialType.ViscoElastic => $"{prefix}_nonlinear",
                _ => $"{prefix}_elastic",
            },
            FailureCriterion.ElastoPlastic => $"{prefix}_elastoplastic",
            FailureCriterion.MohrCoulomb => $"{prefix}_mohr_coulomb",
            FailureCriterion.HoekBrown => $"{prefix}_hoek_brown",
            FailureCriterion.DruckerPrager => $"{prefix}_drucker_prager",
            FailureCriterion.GeneralizedHoekBrown => $"{prefix}_generalized_hoek_brown",
            _ => $"{prefix}_elastoplastic",
        };
    }

    /// <summary>
    /// Maps GUI failure criterion to the <c>slope_stability_*_{criterion}_SRF</c> slug used in executable names.
    /// Mohr–Coulomb uses the short form <c>slope_stability_{Mesh}_SRF</c> (no criterion segment).
    /// </summary>
    public static string FailureCriterionSlug(FailureCriterion failure) => failure switch
    {
        FailureCriterion.DruckerPrager => "drucker_prager",
        FailureCriterion.HoekBrown => "hoek_brown",
        FailureCriterion.GeneralizedHoekBrown => "generalized_hoek_brown",
        FailureCriterion.MohrCoulomb => "mohr_coulomb",
        _ => "mohr_coulomb",
    };

    /// <summary>
    /// Branded slope SRF executable for mesh type + failure criterion.
    /// MC: <c>slope_stability_{Mesh}_SRF</c>; other slope criteria: <c>slope_stability_{Mesh}_{slug}_SRF</c>.
    /// When <paramref name="axisymmetric"/>, appends <c>_axisym</c>.
    /// </summary>
    public static string FormatSlopeSrfExecutableName(string meshType, FailureCriterion failure, bool axisymmetric = false)
    {
        var mesh = NormalizeMeshTypeToken(meshType);
        string name;
        if (failure == FailureCriterion.MohrCoulomb || !IsSlopeStabilityFailure(failure))
            name = $"slope_stability_{mesh}_SRF";
        else
            name = $"slope_stability_{mesh}_{FailureCriterionSlug(failure)}_SRF";
        return axisymmetric ? name + "_axisym" : name;
    }

    /// <summary>
    /// Rewrites legacy <c>*_mohr_coulomb_SRF</c> names to the deployed short MC executable.
    /// </summary>
    public static string NormalizeSlopeSrfExecutableName(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return executable;
        var exe = executable.Trim();
        const string mcSuffixAxisym = "_mohr_coulomb_SRF_axisym";
        if (exe.EndsWith(mcSuffixAxisym, StringComparison.OrdinalIgnoreCase))
            return exe[..^mcSuffixAxisym.Length] + "_SRF_axisym";
        const string mcSuffix = "_mohr_coulomb_SRF";
        if (exe.EndsWith(mcSuffix, StringComparison.OrdinalIgnoreCase))
            return exe[..^mcSuffix.Length] + "_SRF";
        return exe;
    }

    public static bool IsSlopeStabilitySrfExecutable(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        if (!n.StartsWith("slope_stability_", StringComparison.OrdinalIgnoreCase))
            return false;
        return n.EndsWith("_SRF_axisym", StringComparison.OrdinalIgnoreCase)
               || n.EndsWith("_SRF", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>GUI AnalysisOptions token marking a slope-stability SRF deck (not a runtime exe name).</summary>
    public static bool IsSrfGuiExecutable(string? name) =>
        string.Equals(name?.Trim(), "SRF", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves <c>mesh_type</c> (T3/T6/Q4/Q8) from an SRF script for Script → GUI mesh sync.
    /// Priority: AnalysisOptions <c>mesh_type</c> → branded <c>slope_stability_{Mesh}_*_SRF</c> → p64/Q8 deck.
    /// </summary>
    public static bool TryResolveMeshTypeFromDat(string datContent, out string meshType)
    {
        meshType = "T3";
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        if (TryParse(datContent, out var options))
        {
            if (!string.IsNullOrWhiteSpace(options.MeshType)
                && IsKnownMeshTypeToken(options.MeshType, out meshType))
                return true;

            if (TryParseMeshTypeFromSrfExecutable(options.PreferredExecutable, out meshType))
                return true;

            if (IsSrfGuiExecutable(options.PreferredExecutable) && IsP64ExplicitDeck(options))
            {
                meshType = NormalizeMeshTypeToken(options.MeshType);
                if (!IsP64ExplicitMeshType(meshType))
                    meshType = "T3";
                return true;
            }
        }

        if (datContent.Contains("! solver_executable slope_stability_", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var raw in datContent.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.Contains("solver_executable", StringComparison.OrdinalIgnoreCase))
                    continue;
                var bang = line.IndexOf('!');
                if (bang >= 0) line = line[(bang + 1)..].Trim();
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && TryParseMeshTypeFromSrfExecutable(parts[^1], out meshType))
                    return true;
            }
        }

        if (string.Equals(FemCppProgramRouter.ResolveProgramId(datContent), "p64", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParse(datContent, out var p64Opts)
                && IsKnownMeshTypeToken(p64Opts.MeshType, out meshType))
                return true;
            meshType = "Q8";
            return true;
        }

        return false;
    }

    private static bool IsKnownMeshTypeToken(string token, out string meshType)
    {
        meshType = NormalizeMeshTypeToken(token);
        if (meshType is "T3" or "T6" or "Q4" or "Q8")
            return true;
        meshType = "T3";
        return false;
    }

    public static string NormalizeMeshTypeToken(string? token)
    {
        var mesh = (token ?? "T3").Trim().ToUpperInvariant().Replace('\\', '/');
        mesh = Regex.Replace(mesh, @"\s+", "");
        return mesh switch
        {
            "Q3" or "QT3" or "Q/T3" or "Q-T3" or "T3" => "T3",
            "T6" => "T6",
            "Q4" => "Q4",
            "Q8" => "Q8",
            _ => "T3",
        };
    }

    private static bool TryParseMeshTypeFromSrfExecutable(string? executable, out string meshType)
    {
        meshType = "T3";
        if (!IsSlopeStabilitySrfExecutable(executable))
            return false;

        var parts = executable!.Trim().Split('_');
        // slope_stability_Q8_mohr_coulomb_SRF
        if (parts.Length < 3 || !IsKnownMeshTypeToken(parts[2], out meshType))
            return false;
        return true;
    }

    /// <summary>
    /// True when the script identifies a slope-stability SRF deck (auto-select Project Settings → Analysis).
    /// Primary trigger: <c>! solver_executable SRF</c>. <c>p64</c> alone does not trigger.
    /// </summary>
    public static bool IsSrfDatDeck(string datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        if (P610AnalysisOptionsHelper.TryParse(datContent, out var options))
        {
            if (IsSrfGuiExecutable(options.PreferredExecutable))
                return true;
            if (!string.IsNullOrEmpty(options.SrfMode) || options.SrfF.HasValue)
                return true;
            if (IsSlopeStabilitySrfExecutable(options.PreferredExecutable))
                return true;
        }

        if (datContent.Contains("! solver_executable SRF", StringComparison.OrdinalIgnoreCase)
            || datContent.Contains("! solver_executable srf", StringComparison.OrdinalIgnoreCase))
            return true;

        if (datContent.Contains("! srf_mode", StringComparison.OrdinalIgnoreCase)
            && string.Equals(FemCppProgramRouter.ResolveProgramId(datContent), "p64", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static bool IsSlopeStabilityFailure(FailureCriterion failure) =>
        failure is FailureCriterion.MohrCoulomb
            or FailureCriterion.DruckerPrager
            or FailureCriterion.HoekBrown
            or FailureCriterion.GeneralizedHoekBrown;

    /// <summary>
    /// When SRF analysis is active but the global failure criterion is non-slope (e.g. Elastic),
    /// resolve a slope criterion from script AnalysisOptions or the material library consensus.
    /// </summary>
    public static bool TryResolveSrfFailureCriterion(
        string? scriptContent,
        IEnumerable<MaterialModel>? materials,
        out FailureCriterion failure)
    {
        failure = FailureCriterion.Elastic;

        if (!string.IsNullOrWhiteSpace(scriptContent)
            && TryParse(scriptContent, out var options)
            && IsSlopeStabilityFailure(options.FailureCriterion))
        {
            failure = options.FailureCriterion;
            return true;
        }

        if (materials == null)
            return false;

        var slopeGroups = materials
            .Where(m => IsSlopeStabilityFailure(m.FailureCriterion))
            .GroupBy(m => m.FailureCriterion)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .ToList();

        if (slopeGroups.Count == 0)
            return false;

        failure = slopeGroups[0].Key;
        return true;
    }

    /// <summary>
    /// Resolves branded slope SRF executable for Run FE Analysis. Always returns
    /// <c>slope_stability_{Mesh}_SRF</c> (literature-optimal core per mesh), never <c>p64</c>.
    /// </summary>
    public static string ResolveSlopeSrfRunner(P610AnalysisOptions options, MeshViewModel? meshVm)
    {
        if (IsSlopeStabilitySrfExecutable(options.PreferredExecutable))
        {
            var normalized = NormalizeSlopeSrfExecutableName(options.PreferredExecutable.Trim());
            if (TryParseMeshTypeFromSrfExecutable(normalized, out _))
                return normalized;
        }

        return SrfLiteratureOptimalRouting.ResolveProcessExecutable(options, meshVm);
    }

    /// <summary>
    /// Rewrites AnalysisOptions so <c>solver_executable</c> matches the native exe about to run.
    /// GUI decks use <c>SRF</c> as a routing token; C++ validates the concrete executable name.
    /// Also aligns <c>mesh_type</c> with the branded executable when they differ (stale script metadata).
    /// </summary>
    public static string PrepareDatForNativeSlopeSrfExecutable(string datContent, string programName)
    {
        if (string.IsNullOrWhiteSpace(datContent) || string.IsNullOrWhiteSpace(programName))
            return datContent;

        var program = NormalizeSlopeSrfExecutableName(programName.Trim());
        if (!IsSlopeStabilitySrfExecutable(program))
            return datContent;

        if (!TryParse(datContent, out var options))
        {
            if (!datContent.Contains("solver_executable", StringComparison.OrdinalIgnoreCase))
                return datContent;
            return Regex.Replace(
                datContent,
                @"^\s*!\s*solver_executable\s+SRF\s*$",
                $"! solver_executable {program}",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }

        var meshType = options.MeshType;
        if (TryParseMeshTypeFromSrfExecutable(program, out var meshFromExe))
        {
            // Explicit p64-layout Q4 decks must keep mesh_type Q4 so 4-node connectivity
            // is not misread as 8-node Q8 when branded/process exe names differ from deck metadata.
            if (IsP64ExplicitDatDeck(datContent)
                && !string.IsNullOrWhiteSpace(options.MeshType)
                && !string.Equals(meshFromExe, options.MeshType, StringComparison.OrdinalIgnoreCase))
                meshType = options.MeshType;
            else
                meshType = meshFromExe;
        }

        var needsExePatch = IsSrfGuiExecutable(options.PreferredExecutable)
            || string.IsNullOrWhiteSpace(options.PreferredExecutable)
            || !string.Equals(
                NormalizeSlopeSrfExecutableName(options.PreferredExecutable),
                program,
                StringComparison.OrdinalIgnoreCase);

        if (!needsExePatch
            && string.Equals(options.MeshType, meshType, StringComparison.OrdinalIgnoreCase))
        {
            if (IsP64ExplicitDatDeck(datContent))
                return datContent;
            return DatSolverControlExporter.EnsureSrfT3GravityEquilibrationDeck(datContent);
        }

        var analysisType = program.EndsWith("_SRF_axisym", StringComparison.OrdinalIgnoreCase)
            ? "axisymmetric"
            : options.AnalysisType;
        options = options with
        {
            MeshType = meshType,
            PreferredExecutable = program,
            AnalysisType = analysisType,
        };

        return IsP64ExplicitDatDeck(datContent)
            ? ReplaceAnalysisOptionsBlock(datContent, options)
            : DatSolverControlExporter.EnsureSrfT3GravityEquilibrationDeck(ReplaceAnalysisOptionsBlock(datContent, options));
    }

    private static bool IsP64ExplicitDatDeck(string datContent)
        => !string.IsNullOrWhiteSpace(datContent)
           && datContent.IndexOf("! <solver:p64>", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// Material Properties → Script Editor: refresh <c>failure_criterion</c>, <c>material_type</c>,
    /// <c>mesh_type</c>, and SRF routing fields in the AnalysisOptions block without rebuilding the mesh.
    /// </summary>
    public static bool TryPatchAnalysisOptionsFromViewModels(
        string datContent,
        MaterialPropertiesViewModel? materialsVM,
        MeshViewModel? meshVM,
        out string patched,
        out bool changed)
    {
        patched = datContent ?? "";
        changed = false;
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        var built = BuildFromViewModels(materialsVM, meshVM);
        P610AnalysisOptions merged = built;

        if (TryParse(datContent, out var existing))
        {
            // Preserve Kh/Kv (and bracket / expected metadata) from the existing deck.
            merged = existing with
            {
                FailureCriterion = built.FailureCriterion,
                MaterialType = built.MaterialType,
                MeshType = built.MeshType,
                SrfMode = !string.IsNullOrWhiteSpace(existing.SrfMode) ? existing.SrfMode : built.SrfMode,
                PreferredExecutable = built.PreferredExecutable,
                AnalysisType = !string.IsNullOrWhiteSpace(built.AnalysisType) ? built.AnalysisType : existing.AnalysisType,
            };
        }

        if (HasAnalysisOptionsBlock(datContent))
        {
            var updated = ReplaceAnalysisOptionsBlock(datContent, merged);
            changed = !string.Equals(updated, datContent, StringComparison.Ordinal);
            patched = updated;
            return changed;
        }

        if (!IsSrfDatDeck(datContent)
            && datContent.IndexOf("! <solver:p64>", StringComparison.OrdinalIgnoreCase) < 0
            && datContent.IndexOf("! <solver:p610_t3", StringComparison.OrdinalIgnoreCase) < 0
            && datContent.IndexOf("! solver:p610_t3", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        patched = InsertAnalysisOptionsBlock(datContent, BuildBlock(merged));
        changed = !string.Equals(patched, datContent, StringComparison.Ordinal);
        return changed;
    }

    private static string InsertAnalysisOptionsBlock(string datContent, string block)
    {
        var lines = datContent.Replace("\r\n", "\n").Split('\n');
        int insertAt = 0;
        for (; insertAt < lines.Length; insertAt++)
        {
            var t = lines[insertAt].Trim();
            if (t.Length == 0 || t.StartsWith('!'))
                continue;
            break;
        }

        var nl = datContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var head = string.Join(nl, lines.Take(insertAt));
        var tail = string.Join(nl, lines.Skip(insertAt));
        if (!block.EndsWith(nl, StringComparison.Ordinal))
            block += nl;
        if (head.Length > 0 && !head.EndsWith(nl, StringComparison.Ordinal))
            head += nl;
        if (tail.Length > 0 && !block.EndsWith(nl, StringComparison.Ordinal))
            block += nl;
        return head + block + tail;
    }

    private static string ReplaceAnalysisOptionsBlock(string datContent, P610AnalysisOptions options)
    {
        var begin = datContent.IndexOf(BeginTag, StringComparison.OrdinalIgnoreCase);
        if (begin < 0) return datContent;
        var end = datContent.IndexOf(EndTag, begin, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return datContent;

        var newBlock = BuildBlock(options);
        var nl = datContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (!newBlock.EndsWith(nl, StringComparison.Ordinal))
            newBlock += nl;

        var endInclusive = end + EndTag.Length;
        return datContent[..begin] + newBlock + datContent[endInclusive..];
    }

    private static bool IsP64ExplicitMeshType(string? meshType)
    {
        if (string.IsNullOrWhiteSpace(meshType))
            return false;
        return NormalizeMeshTypeToken(meshType) is "T3" or "T6" or "Q4" or "Q8";
    }

    private static bool ShouldRouteThroughFemCppP64(P610AnalysisOptions options)
    {
        if (!IsP64ExplicitDeck(options))
            return false;
        if (options.FailureCriterion is not FailureCriterion.MohrCoulomb
            and not FailureCriterion.Elastic
            and not FailureCriterion.ElastoPlastic)
            return false;
        if (!string.IsNullOrWhiteSpace(options.PreferredExecutable))
        {
            var exe = options.PreferredExecutable.Trim();
            if (IsSlopeStabilitySrfExecutable(exe))
                return false;
            if (IsSrfGuiExecutable(exe))
                return true;
        }
        return true;
    }

    /// <summary>
    /// Chooses the native executable for Run FE Analysis and Script Editor Execute Script.
    /// </summary>
    public static string ResolveRunnerExecutableForFeAnalysis(
        P610AnalysisOptions options,
        MeshViewModel? meshVm,
        bool srfAnalysisMode = false)
    {
        if (srfAnalysisMode
            || IsSrfGuiExecutable(options.PreferredExecutable)
            || !string.IsNullOrEmpty(options.SrfMode)
            || options.SrfF.HasValue
            || IsSlopeStabilitySrfExecutable(options.PreferredExecutable))
        {
            if (IsSlopeStabilityFailure(options.FailureCriterion)
                || IsSrfGuiExecutable(options.PreferredExecutable)
                || IsSlopeStabilitySrfExecutable(options.PreferredExecutable))
                return ResolveSlopeSrfRunner(options, meshVm);
        }

        if (IsP64ExplicitDeck(options))
            return "p64";

        var mesh = meshVm is { IsDiscretized: true, MeshInformationRows.Count: > 0 }
            ? ResolveMeshType(meshVm)
            : options.MeshType;
        return ResolveExecutableName(options.FailureCriterion, options.MaterialType, mesh);
    }

    /// <summary>
    /// True when AnalysisOptions identify a Program 6.4 explicit/parametric SRF deck routed to <c>fem_cpp p64</c>.
    /// </summary>
    public static bool IsP64ExplicitDeck(P610AnalysisOptions options)
    {
        if (!IsP64ExplicitMeshType(options.MeshType))
            return false;
        if (!string.IsNullOrWhiteSpace(options.PreferredExecutable))
        {
            var exe = options.PreferredExecutable.Trim();
            return exe.Equals("p64", StringComparison.OrdinalIgnoreCase)
                || IsSrfGuiExecutable(exe)
                || exe.Equals("slope_q8_srf", StringComparison.OrdinalIgnoreCase)
                || exe.Equals("slope_Q8_SRF", StringComparison.OrdinalIgnoreCase)
                || exe.Equals("slope_stability_p64", StringComparison.OrdinalIgnoreCase);
        }
        return !string.IsNullOrEmpty(options.SrfMode)
            || !string.IsNullOrEmpty(options.MeshSource);
    }

    /// <summary>Backward-compatible alias for <see cref="IsP64ExplicitDeck"/> (Q8-only name retained).</summary>
    public static bool IsP64Q8Deck(P610AnalysisOptions options) => IsP64ExplicitDeck(options);

    public static string ResolveMeshType(MeshViewModel? meshVM)
    {
        if (meshVM == null) return "T3";

        // Discretized canvas rows are authoritative — UDM ElementType can stay Q8 after a Q4 regenerate.
        var rows = meshVM.MeshInformationRows;
        if (meshVM.IsDiscretized && rows is { Count: > 0 })
            return ClassifyHomogeneousMeshType(rows);

        var udm = meshVM.UserDefinedMeshVM;
        if (meshVM.MeshPopupSelectedTabIndex == MeshViewModel.MeshPopupUserDefinedTabIndex
            || udm.ElementRows.Count > 0)
        {
            var et = NormalizeMeshTypeToken(udm.ElementType);
            if (et is "T3" or "T6" or "Q4" or "Q8")
                return et;
        }

        // Prefer what is actually in the mesh rows (so RS2D-generated quads round-trip correctly).
        if (rows is { Count: > 0 })
            return ClassifyHomogeneousMeshType(rows);

        var family = meshVM.SelectedTwoDElementFamily ?? "";
        var nodeOption = meshVM.SelectedTwoDNodeOption ?? "";
        if (family.Contains("Quadrilateral", StringComparison.OrdinalIgnoreCase))
            return nodeOption.StartsWith("8", StringComparison.OrdinalIgnoreCase) ? "Q8" : "Q4";

        if (meshVM.MeshElementOrder.StartsWith("6", StringComparison.OrdinalIgnoreCase))
            return "T6";
        return "T3";
    }

    /// <summary>
    /// Branded p610 SRF solvers require a single element type. Mixed Q4+T3 RS2D meshes route to T3 (quads split on export).
    /// </summary>
    private static string ClassifyHomogeneousMeshType(IReadOnlyList<MeshInformationRow> rows)
    {
        if (rows.Count == 0) return "T3";

        bool anyQ8 = false, anyQ4 = false, anyT6 = false, anyT3 = false;
        foreach (var r in rows)
        {
            if (r.IsQ8) anyQ8 = true;
            else if (r.IsQuad && !r.IsQ8) anyQ4 = true;
            else if (r.IsT6) anyT6 = true;
            else anyT3 = true;
        }

        if (anyQ8 && !anyQ4 && !anyT6 && !anyT3) return "Q8";
        if (anyQ4 && !anyQ8 && !anyT6 && !anyT3) return "Q4";
        if (anyT6 && !anyQ8 && !anyQ4 && !anyT3) return "T6";
        return "T3";
    }

    private static string ExtractBlock(string datContent)
    {
        var begin = datContent.IndexOf(BeginTag, StringComparison.OrdinalIgnoreCase);
        if (begin < 0) return "";
        var end = datContent.IndexOf(EndTag, begin, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return "";
        return datContent.Substring(begin, end - begin);
    }

    private static bool TryParseTokenValue(string block, string key, out string value)
    {
        value = "";
        var rx = new Regex($@"^\s*!\s*{Regex.Escape(key)}\s+([A-Za-z0-9_\-\.\/]+)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var m = rx.Match(block);
        if (!m.Success) return false;
        value = m.Groups[1].Value.Trim();
        return value.Length > 0;
    }

    /// <summary>
    /// Parses a numeric AnalysisOptions header like
    /// <c>! expected_sxx_kPa -47.5520833333333428</c> or <c>! tolerance_kPa 1e-3</c>.
    /// Accepts standard floating-point including scientific notation.
    /// </summary>
    private static bool TryParseDoubleValue(string block, string key, out double value)
    {
        value = 0.0;
        var rx = new Regex($@"^\s*!\s*{Regex.Escape(key)}\s+([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var m = rx.Match(block);
        if (!m.Success) return false;
        return double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Parses an integer AnalysisOptions header like <c>! srf_max_expansion_steps 10</c>.</summary>
    private static bool TryParseIntValue(string block, string key, out int value)
    {
        value = 0;
        var rx = new Regex($@"^\s*!\s*{Regex.Escape(key)}\s+([+-]?\d+)\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var m = rx.Match(block);
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseEnumValue<T>(string block, string key, out T parsed) where T : struct, Enum
    {
        parsed = default;
        if (!TryParseTokenValue(block, key, out var token))
            return false;
        return Enum.TryParse(token, ignoreCase: true, out parsed);
    }
}
