using System;

namespace GeotechSlopeApp.Models.FiniteElement;

/// <summary>
/// Stress output at element integration point.
/// </summary>
public class ElementStress
{
    public int ElementId { get; set; }
    public double StressXX { get; set; }
    public double StressYY { get; set; }
    public double StressXY { get; set; }
    public double PrincipalStressMax { get; set; }
    public double PrincipalStressMin { get; set; }
    public double VonMisesStress { get; set; }
    public double PorePressure { get; set; }
    public double TotalHead { get; set; }
    public double PressureHead { get; set; }
    public double EffectiveStressXX { get; set; }
    public double EffectiveStressYY { get; set; }
    public double EffectiveStressXY { get; set; }
    public double EffectivePrincipalMax { get; set; }
    public double EffectivePrincipalMin { get; set; }
    /// <summary>Normal strain εxx (dimensionless).</summary>
    public double StrainXX { get; set; }
    /// <summary>Normal strain εyy (dimensionless).</summary>
    public double StrainYY { get; set; }
    /// <summary>Engineering shear strain γxy (dimensionless).</summary>
    public double ShearStrain { get; set; }

    /// <summary>Peak γmax over Gauss points when exported by p64 (<c>gamma_max</c> in JSON).</summary>
    public double PeakMaximumShearStrain { get; set; }

    /// <summary>
    /// Mohr–Coulomb yield ratio (mobilized shear / strength) at the element centroid.
    /// Values near/above 1 mark the shear zone at failure.
    /// </summary>
    public double YieldRatio { get; set; }

    /// <summary>Viscoplastic strain norm √(Σ εvp²) — plastic indicator for the shear zone.</summary>
    public double PlasticStrainNorm { get; set; }

    /// <summary>
    /// Maximum shear strain γmax = |ε₁ − ε₂| = √((εxx − εyy)² + γxy²), as used by RS2 and PLAXIS 2D.
    /// </summary>
    public double MaximumShearStrain =>
        Math.Sqrt((StrainXX - StrainYY) * (StrainXX - StrainYY) + ShearStrain * ShearStrain);

    /// <summary>γmax used for peak-GP diagnostic contour — prefers solver peak GP value when available.</summary>
    public double ContourMaximumShearStrain =>
        PeakMaximumShearStrain > 0 ? PeakMaximumShearStrain : MaximumShearStrain;
}
