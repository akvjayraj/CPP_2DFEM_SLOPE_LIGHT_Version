namespace GeotechSlopeApp.Models;

public enum ProblemDimension
{
    OneD,
    TwoD,
    ThreeD
}

/// <summary>Load application direction for .dat: 1D uses Axial only; 2D can use X, Y, or Axial (along bar).</summary>
public enum LoadDirection
{
    Axial,
    X,
    Y
}

public enum FailureCriterion
{
    Elastic,
    ElastoPlastic,
    MohrCoulomb,
    HoekBrown,
    DruckerPrager,
    GeneralizedHoekBrown
}

public enum MaterialType
{
    Isotropic,
    TransverselyIsotropic,
    Orthotropic,
    DuncanChangHyperbolic,
    NonlinearIsotropic,
    ViscoElastic,
    Plastic
}

public enum TunnelMeshGenerationMethod
{
    CDT,
    RingRefinement,
    AFM,
    Smoothing
}

public enum MeshSmoothingMethod
{
    None,
    Laplacian,
    Optimize
}

public enum LoadOrientation
{
    NormalToBoundary,
    VerticalDown,
    HorizontalRight,
    AngleFromHorizontal,
    AngleToBoundary
}

/// <summary>Intensity distribution along a surface or line load (properties panel).</summary>
public enum LoadDistribution
{
    Uniform,
    Linear,
    Triangular
}

/// <summary>
/// SRF solver control search strategy: fixed trial sweep (existing nsrf + F list) or an
/// iterative bracket search (auto-detected or manual F_min/F_max, bisected to a critical FoS).
/// </summary>
public enum SrfSearchMode
{
    Sweep,
    Bracket
}

/// <summary>
/// Gravity-only SRF in-situ stress path written as <c>! srf_insitu_mode</c>.
/// Auto picks K0 when Coupled GW+SRF or Ru is active; otherwise Gravity turn-on.
/// </summary>
public enum SrfInsituMode
{
    /// <summary>Coupled GW / Ru → k0; dry SRF → gravity.</summary>
    Auto,
    /// <summary>Clear Stage-0 and ramp self-weight from σ=0 (Program 6.4).</summary>
    Gravity,
    /// <summary>Keep Stage-0 K0 (+ Ru/WT) and plastic-equilibrate under reduced strength.</summary>
    K0
}

/// <summary>
/// Continuum idealization for mechanical SRF / p610. Maps to
/// <c>ProjectSettingsViewModel.SrfAxisymmetric</c> (plane strain = false).
/// </summary>
public enum AnalysisIdealization
{
    PlaneStrain,
    Axisymmetric
}

/// <summary>
/// Honest water-coupling role derived from the Flow-Seepage/Groundwater tree leaf.
/// Persisted as <c>LastGroundwaterTreeMode</c>, not as its own project field.
/// </summary>
public enum WaterParticipation
{
    None,
    /// <summary>Static leaf: prescribed u (WT / Ru / B-bar / grid) → Terzaghi σ′.</summary>
    Prescribed,
    /// <summary>Steady State leaf: FE seepage heads → u; optional one-way transfer into SRF.</summary>
    Seepage,
    /// <summary>Transient State leaf: θ-method excess-u dissipation (not Biot).</summary>
    Dissipation,
    /// <summary>Future Sequential leaf (disabled).</summary>
    Sequential,
    /// <summary>Future Fully coupled leaf (disabled). Biot theory only — not implemented.</summary>
    FullyCoupled
}

/// <summary>
/// Derived Run-FE routing kind. Not a second picker — sync from category ON + tree leaf.
/// </summary>
public enum AnalysisKind
{
    Off,
    Structural,
    Srf,
    GroundwaterStatic,
    GroundwaterSteady,
    GroundwaterTransient,
    CoupledStaticSrf,
    CoupledSeepageSrf
}
