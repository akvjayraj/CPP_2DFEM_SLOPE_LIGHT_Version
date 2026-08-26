using System;
using System.Collections.Generic;

namespace GeotechSlopeApp.Analytical;

/// <summary>
/// Plane-strain Kirsch (1898) closed-form solution for a circular hole of radius
/// <see cref="Inputs.A"/> in an infinite, homogeneous, isotropic, linear-elastic medium
/// loaded by a biaxial far-field stress field (sigma_h along +x, sigma_v along +y).
///
/// Sign convention: tension-positive throughout (matches the p610_t3_elastic solver
/// and Verification/visualize.html). Inputs are typically negative for compressive
/// ground stresses; outputs follow suit.
///
/// References: Jaeger, Cook &amp; Zimmerman, Fundamentals of Rock Mechanics (4th ed.),
/// sections 8.4 (stresses) and 8.5 (wall displacements). Brady &amp; Brown, Rock Mechanics
/// for Underground Mining, section 6.4.
/// </summary>
public static class KirschCircularHole
{
    /// <summary>
    /// Inputs match the p610_t3_elastic JSON meta block (kPa for stresses and modulus,
    /// metres for lengths). <see cref="K0"/> is the in-plane horizontal-to-vertical
    /// stress ratio (sigma_h = K0 * sigma_v).
    /// </summary>
    public readonly record struct Inputs(
        double E_kPa,
        double Nu,
        double SigV_kPa,
        double K0,
        double A,
        double Cx,
        double Cy);

    /// <summary>Wall sample at angle <see cref="ThetaDeg"/> (CCW from +x).</summary>
    public readonly record struct WallPoint(
        double ThetaDeg,
        double UrInward_m,
        double SigTheta_kPa,
        double SigR_kPa);

    /// <summary>Sample along a radial ray at radius <see cref="R"/>, angle <see cref="ThetaDeg"/>.</summary>
    public readonly record struct RadialPoint(
        double R,
        double ThetaDeg,
        double SigR_kPa,
        double SigTheta_kPa);

    /// <summary>Plane-strain shear modulus G = E / [2(1 + nu)].</summary>
    public static double ShearModulus(double E, double nu) => E / (2.0 * (1.0 + nu));

    /// <summary>
    /// Inward radial wall displacement at polar angle <paramref name="thetaRad"/>
    /// (CCW from +x). Positive values mean the wall moves into the void — the same
    /// sign convention used by Verification/visualize.html so the comparison plots
    /// line up directly.
    ///
    /// u_r(a, theta) = a / (4G) * [ 2(1 - nu)(sigma_h + sigma_v)
    ///                              + (3 - 4 nu)(sigma_h - sigma_v) cos(2 theta) ]
    ///
    /// Hydrostatic sanity check (K0 = 1, sigma_h = sigma_v = p):
    ///   u_r(a) = p * a * (1 - nu) / G  (NOT p*a/(2G) which is plane stress).
    /// </summary>
    public static double UrInward(in Inputs i, double thetaRad)
    {
        double G = ShearModulus(i.E_kPa, i.Nu);
        double sigV = i.SigV_kPa;
        double sigH = i.K0 * sigV;
        double sum = sigH + sigV;
        double diff = sigH - sigV;
        double ur = (i.A / (4.0 * G))
                  * (2.0 * (1.0 - i.Nu) * sum
                     + (3.0 - 4.0 * i.Nu) * diff * Math.Cos(2.0 * thetaRad));
        // Inward-positive: flip the outward-positive solver convention.
        return -ur;
    }

    /// <summary>
    /// Polar-coordinate stresses (sigma_rr, sigma_theta_theta) at radius
    /// <paramref name="r"/> and polar angle <paramref name="thetaRad"/>.
    /// Tension-positive. Reduces to the hydrostatic case
    ///   sigma_rr = p (1 - a^2/r^2),   sigma_theta = p (1 + a^2/r^2)
    /// when K0 = 1.
    /// </summary>
    public static (double SigR, double SigTheta) StressPolar(in Inputs i, double r, double thetaRad)
    {
        double sigV = i.SigV_kPa;
        double sigH = i.K0 * sigV;
        double s = 0.5 * (sigH + sigV);   // hydrostatic part
        double d = 0.5 * (sigH - sigV);   // deviatoric part
        double a2 = i.A * i.A;
        double r2 = r * r;
        double r4 = r2 * r2;
        double a4 = a2 * a2;
        double c2 = Math.Cos(2.0 * thetaRad);

        double sr = s * (1.0 - a2 / r2)
                  + d * (1.0 - 4.0 * a2 / r2 + 3.0 * a4 / r4) * c2;
        double st = s * (1.0 + a2 / r2)
                  - d * (1.0 + 3.0 * a4 / r4) * c2;
        return (sr, st);
    }

    /// <summary>Convenience: sigma_rr at the wall (r = a), which is identically zero
    /// (free-surface boundary condition). Returned as a function for symmetry with
    /// <see cref="StressPolar"/>; useful as the "expected analytical value" in checks.</summary>
    public static double WallSigR(in Inputs _) => 0.0;

    /// <summary>Wall hoop stress sigma_theta (r = a):
    /// sigma_theta(a, theta) = (sigma_h + sigma_v) - 2(sigma_h - sigma_v) cos(2 theta).
    /// </summary>
    public static double WallSigTheta(in Inputs i, double thetaRad)
    {
        double sigV = i.SigV_kPa;
        double sigH = i.K0 * sigV;
        return (sigH + sigV) - 2.0 * (sigH - sigV) * Math.Cos(2.0 * thetaRad);
    }

    /// <summary>
    /// Rotate a 2D Cartesian stress tensor (sxx, syy, sxy) into polar components
    /// (sigma_rr, sigma_theta_theta, sigma_r_theta) at angle <paramref name="thetaRad"/>.
    /// Used to project FEM element stresses onto polar axes for the Verification table.
    /// </summary>
    public static (double SigR, double SigTheta, double TauRT) PolarFromCartesian(
        double sxx, double syy, double sxy, double thetaRad)
    {
        double c = Math.Cos(thetaRad);
        double s = Math.Sin(thetaRad);
        double c2 = c * c;
        double s2 = s * s;
        double sc = s * c;
        double sr = sxx * c2 + syy * s2 + 2.0 * sxy * sc;
        double st = sxx * s2 + syy * c2 - 2.0 * sxy * sc;
        double trt = (syy - sxx) * sc + sxy * (c2 - s2);
        return (sr, st, trt);
    }

    /// <summary>Yield wall samples at the given list of angles (degrees, CCW from +x).</summary>
    public static IEnumerable<WallPoint> WallSamples(Inputs inputs, double[] thetasDeg)
    {
        foreach (var thDeg in thetasDeg)
        {
            double th = thDeg * Math.PI / 180.0;
            yield return new WallPoint(
                ThetaDeg: thDeg,
                UrInward_m: UrInward(inputs, th),
                SigTheta_kPa: WallSigTheta(inputs, th),
                SigR_kPa: WallSigR(inputs));
        }
    }

    /// <summary>Yield radial-profile samples along a single ray <paramref name="thetaDeg"/> at the
    /// supplied <paramref name="rOverA"/> ratios (multiples of the hole radius).</summary>
    public static IEnumerable<RadialPoint> RadialProfile(Inputs inputs, double thetaDeg, double[] rOverA)
    {
        double th = thetaDeg * Math.PI / 180.0;
        foreach (var k in rOverA)
        {
            double r = k * inputs.A;
            var (sr, st) = StressPolar(inputs, r, th);
            yield return new RadialPoint(R: r, ThetaDeg: thetaDeg, SigR_kPa: sr, SigTheta_kPa: st);
        }
    }
}
