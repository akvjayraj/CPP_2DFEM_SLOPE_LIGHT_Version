using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using GeotechSlopeApp.Models.Wave3;

namespace GeotechSlopeApp.Models;

/// <summary>
/// A named material with all parameters used across stiffness, strength, hydraulic,
/// and thermal/dynamic analysis types. Follows the Plaxis/RS2/ANSYS material data model.
/// </summary>
public partial class MaterialModel : ObservableObject
{
    // ── Identity ───────────────────────────────────────────────────────────────
    [ObservableProperty] private int    _id;
    [ObservableProperty] private string _name        = "New Material";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private Color  _colour      = Color.FromRgb(173, 216, 230); // light-blue default
    [ObservableProperty] private FailureCriterion _failureCriterion = FailureCriterion.Elastic;
    [ObservableProperty] private MaterialType     _materialType     = MaterialType.Isotropic;

    // ── Stiffness – Isotropic ─────────────────────────────────────────────────
    [ObservableProperty] private double _youngsModulus  = 100_000.0;  // E  (kPa)
    [ObservableProperty] private double _poissonsRatio  = 0.3;        // ν

    // ── Stiffness – Transversely Isotropic (cross-anisotropic) ────────────────
    [ObservableProperty] private double _youngsModulusH = 100_000.0;  // E_h  horizontal (kPa)
    [ObservableProperty] private double _youngsModulusV = 50_000.0;   // E_v  vertical   (kPa)
    [ObservableProperty] private double _poissonsRatioHH = 0.3;       // ν_hh  in-plane
    [ObservableProperty] private double _poissonsRatioVH = 0.2;       // ν_vh  vertical-horizontal
    [ObservableProperty] private double _shearModulusVH  = 20_000.0;  // G_vh  (kPa)

    // ── Unit Weight ────────────────────────────────────────────────────────────
    [ObservableProperty] private double _unitWeight          = 20.0;  // γ    (kN/m³)
    [ObservableProperty] private double _saturatedUnitWeight = 22.0;  // γ_sat(kN/m³)
    [ObservableProperty] private bool   _isSaturatedEnabled  = false;

    // ── Strength – Mohr-Coulomb / Elasto-Plastic ─────────────────────────────
    [ObservableProperty] private double _cohesion      = 0.0;   // c  (kPa)
    [ObservableProperty] private double _frictionAngle = 30.0;  // φ  (°)
    [ObservableProperty] private double _dilationAngle = 0.0;   // ψ  (°)
    [ObservableProperty] private double _tensileCutoff = 0.0;   // σ_t (kPa)

    // ── Strength – Hoek-Brown ─────────────────────────────────────────────────
    [ObservableProperty] private double _ucs             = 50.0;   // σ_ci (MPa)
    [ObservableProperty] private double _hoekBrownMi     = 10.0;   // m_i
    [ObservableProperty] private double _gsi             = 50.0;   // GSI
    [ObservableProperty] private double _disturbance     = 0.0;    // D  [0..1]
    [ObservableProperty] private double _modulusRatio    = 400.0;  // MR (E_i / σ_ci)

    // ── Strength – Drucker-Prager ─────────────────────────────────────────────
    [ObservableProperty] private double _dpAlpha = 0.0;  // α_DP
    [ObservableProperty] private double _dpK     = 0.0;  // k_DP  (kPa)

    // ── Hydraulic ─────────────────────────────────────────────────────────────
    [ObservableProperty] private double _permeabilityX  = 1e-6;   // k_x (m/s)
    [ObservableProperty] private double _permeabilityY  = 1e-6;   // k_y (m/s)
    [ObservableProperty] private double _porosity       = 0.30;   // n
    [ObservableProperty] private double _initialVoidRatio  = 0.50; // e_0
    [ObservableProperty] private double _specificStorage   = 1e-4; // S_s (1/m)

    // ── Thermal / Dynamic ─────────────────────────────────────────────────────
    [ObservableProperty] private double _thermalConductivityX = 2.0;   // λ_x (W/m·K)
    [ObservableProperty] private double _thermalConductivityY = 2.0;   // λ_y (W/m·K)
    [ObservableProperty] private double _heatCapacity         = 1000.0; // C_p (J/kg·K)
    [ObservableProperty] private double _thermalExpansion     = 1e-5;  // α (1/°C)
    [ObservableProperty] private double _density              = 2000.0; // ρ (kg/m³)
    [ObservableProperty] private double _dampingRatio         = 0.05;  // ξ (Rayleigh)

    // ── Rapid drawdown envelopes (USACE R / Duncan Kc–Kf) ───────────────────
    [ObservableProperty] private bool   _developsUndrainedStrengthOnDrawdown;
    [ObservableProperty] private double _drawdownRCohesion;
    [ObservableProperty] private double _drawdownRFrictionAngle;
    [ObservableProperty] private double _drawdownKc1Cohesion;
    [ObservableProperty] private double _drawdownKc1Angle;
    [ObservableProperty] private double _drawdownKfCohesion;
    [ObservableProperty] private double _drawdownKfAngle;
    [ObservableProperty] private double _drawdownKf;

    // ── Probabilistic (Wave 3) ──────────────────────────────────────────────
    [ObservableProperty] private bool _enableProbabilisticVariation;
    [ObservableProperty] private double _cohesionPhiCorrelation;

    /// <summary>Per-property RV settings. Lazy-filled by <see cref="EnsurePropertyStatistics"/>.</summary>
    public Dictionary<MaterialStatPropertyId, MaterialPropertyStatistics> PropertyStatistics { get; }
        = new();

    public void EnsurePropertyStatistics()
    {
        foreach (MaterialStatPropertyId id in Enum.GetValues<MaterialStatPropertyId>())
        {
            if (!PropertyStatistics.ContainsKey(id))
                PropertyStatistics[id] = MaterialPropertyStatistics.CreateDefault(id);
        }
    }

    public MaterialPropertyStatistics CohesionStats
    {
        get { EnsurePropertyStatistics(); return PropertyStatistics[MaterialStatPropertyId.Cohesion]; }
    }

    public MaterialPropertyStatistics FrictionAngleStats
    {
        get { EnsurePropertyStatistics(); return PropertyStatistics[MaterialStatPropertyId.FrictionAngle]; }
    }

    public MaterialPropertyStatistics UnitWeightStats
    {
        get { EnsurePropertyStatistics(); return PropertyStatistics[MaterialStatPropertyId.UnitWeight]; }
    }

    // ── UI-only state (not persisted, not cloned) ─────────────────────────────
    /// <summary>True while this material is the active Assign-material target. Set by MaterialPropertiesViewModel.</summary>
    [ObservableProperty] private bool _isAssignTarget;

    /// <summary>Colour as a WPF Brush for direct canvas / swatch binding.</summary>
    public SolidColorBrush ColourBrush => new(Colour);

    partial void OnColourChanged(Color value) => OnPropertyChanged(nameof(ColourBrush));

    /// <summary>Compact p64 material row: φ, c, ψ, γ, E, ν (solver order).</summary>
    public string P64MaterialRow => string.Create(
        CultureInfo.InvariantCulture,
        $"{FrictionAngle:G10}  {Cohesion:G10}  {DilationAngle:G10}  {UnitWeight:G10}  {YoungsModulus:G10}  {PoissonsRatio:G10}");

    partial void OnFrictionAngleChanged(double value) => OnPropertyChanged(nameof(P64MaterialRow));
    partial void OnCohesionChanged(double value) => OnPropertyChanged(nameof(P64MaterialRow));
    partial void OnDilationAngleChanged(double value) => OnPropertyChanged(nameof(P64MaterialRow));
    partial void OnUnitWeightChanged(double value) => OnPropertyChanged(nameof(P64MaterialRow));
    partial void OnYoungsModulusChanged(double value) => OnPropertyChanged(nameof(P64MaterialRow));
    partial void OnPoissonsRatioChanged(double value) => OnPropertyChanged(nameof(P64MaterialRow));

    /// <summary>Deep-copy this material to a new instance (Id left as-is; caller sets new Id/Name).</summary>
    public MaterialModel Clone()
    {
        var clone = new MaterialModel
    {
        Id                   = Id,
        Name                 = Name,
        Description          = Description,
        Colour               = Colour,
        FailureCriterion     = FailureCriterion,
        MaterialType         = MaterialType,
        YoungsModulus        = YoungsModulus,
        PoissonsRatio        = PoissonsRatio,
        YoungsModulusH       = YoungsModulusH,
        YoungsModulusV       = YoungsModulusV,
        PoissonsRatioHH      = PoissonsRatioHH,
        PoissonsRatioVH      = PoissonsRatioVH,
        ShearModulusVH       = ShearModulusVH,
        UnitWeight           = UnitWeight,
        SaturatedUnitWeight  = SaturatedUnitWeight,
        IsSaturatedEnabled   = IsSaturatedEnabled,
        Cohesion             = Cohesion,
        FrictionAngle        = FrictionAngle,
        DilationAngle        = DilationAngle,
        TensileCutoff        = TensileCutoff,
        Ucs                  = Ucs,
        HoekBrownMi          = HoekBrownMi,
        Gsi                  = Gsi,
        Disturbance          = Disturbance,
        ModulusRatio         = ModulusRatio,
        DpAlpha              = DpAlpha,
        DpK                  = DpK,
        PermeabilityX        = PermeabilityX,
        PermeabilityY        = PermeabilityY,
        Porosity             = Porosity,
        InitialVoidRatio     = InitialVoidRatio,
        SpecificStorage      = SpecificStorage,
        ThermalConductivityX = ThermalConductivityX,
        ThermalConductivityY = ThermalConductivityY,
        HeatCapacity         = HeatCapacity,
        ThermalExpansion     = ThermalExpansion,
        Density              = Density,
        DampingRatio         = DampingRatio,
        DevelopsUndrainedStrengthOnDrawdown = DevelopsUndrainedStrengthOnDrawdown,
        DrawdownRCohesion    = DrawdownRCohesion,
        DrawdownRFrictionAngle = DrawdownRFrictionAngle,
        DrawdownKc1Cohesion  = DrawdownKc1Cohesion,
        DrawdownKc1Angle     = DrawdownKc1Angle,
        DrawdownKfCohesion   = DrawdownKfCohesion,
        DrawdownKfAngle      = DrawdownKfAngle,
        DrawdownKf           = DrawdownKf,
        EnableProbabilisticVariation = EnableProbabilisticVariation,
        CohesionPhiCorrelation = CohesionPhiCorrelation,
    };
    clone.EnsurePropertyStatistics();
    foreach (var kv in PropertyStatistics)
        clone.PropertyStatistics[kv.Key] = kv.Value.Clone();
    return clone;
}
}
