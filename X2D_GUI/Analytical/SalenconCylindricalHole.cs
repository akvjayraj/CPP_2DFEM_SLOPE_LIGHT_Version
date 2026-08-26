using System;
using System.Collections.Generic;

namespace GeotechSlopeApp.Analytical;

/// <summary>
/// Salencon (1969) closed-form plane-strain solution for a cylindrical hole in
/// an infinite, hydrostatically loaded, Mohr-Coulomb material.
///
/// Inputs use the app's user-facing convention for loading and strength:
/// far-field pressure <c>P0</c>, internal pressure <c>Pi</c>, cohesion, and
/// angles are compression-positive magnitudes. Returned stresses are
/// tension-positive so they compare directly with p610_t3 element stresses.
/// Radial displacement is inward-positive, matching the Kirsch verification UI.
/// </summary>
public static class SalenconCylindricalHole
{
    public readonly record struct Inputs(
        double E_kPa,
        double Nu,
        double P0_kPa,
        double Pi_kPa,
        double PhiDeg,
        double C_kPa,
        double PsiDeg,
        double A,
        double Cx,
        double Cy);

    public readonly record struct State(
        double G_kPa,
        double Kp,
        double kp,
        double q_kPa,
        double Kps,
        double R0,
        double R0OverA,
        double SigmaRe_kPa);

    public readonly record struct RadialPoint(
        double R,
        double ROverA,
        double UrInward_m,
        double SigR_kPa,
        double SigTheta_kPa,
        bool IsPlastic);

    public static double ShearModulus(double E, double nu) => E / (2.0 * (1.0 + nu));

    public static State EvaluateState(in Inputs i)
    {
        Validate(i);

        double phi = DegToRad(i.PhiDeg);
        double psi = DegToRad(i.PsiDeg);
        double sinPhi = Math.Sin(phi);
        double sinPsi = Math.Sin(psi);
        double kpBig = (1.0 + sinPhi) / (1.0 - sinPhi);
        double kpSmall = 1.0 / (kpBig - 1.0);
        double q = 2.0 * i.C_kPa * Math.Sqrt(kpBig);
        double kps = (1.0 + sinPsi) / (1.0 - sinPsi);
        double p0 = i.P0_kPa;
        double denom = i.Pi_kPa / p0 + (q / p0) * kpSmall;
        if (denom <= 0.0)
            throw new ArgumentException("Salencon denominator is non-positive; check internal pressure and strength inputs.");

        double bracket = (2.0 / (kpBig + 1.0))
                       * ((1.0 + (q / p0) * kpSmall) / denom);
        if (bracket <= 0.0)
            throw new ArgumentException("Salencon plastic-radius expression is non-positive; check input parameters.");

        double r0OverA = Math.Pow(bracket, kpSmall);
        double sigRe = -p0 / (kpBig + 1.0) * (2.0 - q / p0);

        return new State(
            G_kPa: ShearModulus(i.E_kPa, i.Nu),
            Kp: kpBig,
            kp: kpSmall,
            q_kPa: q,
            Kps: kps,
            R0: i.A * r0OverA,
            R0OverA: r0OverA,
            SigmaRe_kPa: sigRe);
    }

    /// <summary>Returns tension-positive (sigma_r, sigma_theta) at radius <paramref name="r"/>.</summary>
    public static (double SigR, double SigTheta, bool IsPlastic) StressPolar(in Inputs i, double r)
    {
        var s = EvaluateState(i);
        double x = r / i.A;
        if (x <= s.R0OverA)
        {
            double p0 = i.P0_kPa;
            double denom = i.Pi_kPa / p0 + (s.q_kPa / p0) * s.kp;
            double xp = Math.Pow(x, s.Kp - 1.0);
            double sr = p0 * ((s.q_kPa / p0) * s.kp - denom * xp);
            double st = p0 * ((s.q_kPa / p0) * s.kp - s.Kp * denom * xp);
            return (sr, st, true);
        }

        double ratioSquared = Math.Pow(s.R0 / r, 2.0);
        double srOverP0 = -1.0 + (1.0 - s.SigmaRe_kPa / i.P0_kPa) * ratioSquared;
        double stOverP0 = -srOverP0 - 2.0;
        return (i.P0_kPa * srOverP0, i.P0_kPa * stOverP0, false);
    }

    /// <summary>Returns inward-positive radial displacement at radius <paramref name="r"/>.</summary>
    public static double UrInward(in Inputs i, double r)
    {
        var s = EvaluateState(i);
        double x = r / i.A;
        double scale = i.A * i.P0_kPa / (2.0 * s.G_kPa);

        if (x > s.R0OverA)
            return scale * (1.0 + s.SigmaRe_kPa / i.P0_kPa) * s.R0OverA * s.R0OverA / x;

        double p0 = i.P0_kPa;
        double denom = i.Pi_kPa / p0 + (s.q_kPa / p0) * s.kp;
        double rOverR0 = x / s.R0OverA;
        double chi =
            (2.0 * i.Nu - 1.0) * (1.0 + (s.q_kPa / p0) * s.kp)
            + ((1.0 - i.Nu) * (s.Kp * s.Kp - 1.0) / (s.Kp + s.Kps))
                * denom
                * Math.Pow(s.R0OverA, s.Kp + s.Kps)
                * Math.Pow(rOverR0, -s.Kps - 1.0)
            + (((1.0 - i.Nu) * (s.Kp * s.Kps + 1.0) / (s.Kp + s.Kps)) - i.Nu)
                * denom
                * Math.Pow(x, s.Kp - 1.0);

        return scale * x * chi;
    }

    public static RadialPoint Evaluate(in Inputs i, double r)
    {
        var (sr, st, plastic) = StressPolar(i, r);
        return new RadialPoint(
            R: r,
            ROverA: r / i.A,
            UrInward_m: UrInward(i, r),
            SigR_kPa: sr,
            SigTheta_kPa: st,
            IsPlastic: plastic);
    }

    public static IEnumerable<RadialPoint> RadialProfile(Inputs inputs, IEnumerable<double> rOverA)
    {
        foreach (var k in rOverA)
            yield return Evaluate(inputs, inputs.A * k);
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;

    private static void Validate(in Inputs i)
    {
        if (i.E_kPa <= 0.0) throw new ArgumentException("Young's modulus must be positive.");
        if (i.A <= 0.0) throw new ArgumentException("Tunnel radius must be positive.");
        if (i.P0_kPa <= 0.0) throw new ArgumentException("Far-field pressure P0 must be positive.");
        if (i.C_kPa < 0.0) throw new ArgumentException("Cohesion cannot be negative.");
        if (i.Nu <= -1.0 || i.Nu >= 0.5) throw new ArgumentException("Poisson's ratio must be between -1 and 0.5.");
        if (i.PhiDeg <= 0.0 || i.PhiDeg >= 90.0) throw new ArgumentException("Friction angle must be between 0 and 90 degrees.");
        if (i.PsiDeg < 0.0 || i.PsiDeg >= 90.0) throw new ArgumentException("Dilation angle must be between 0 and 90 degrees.");
    }
}
