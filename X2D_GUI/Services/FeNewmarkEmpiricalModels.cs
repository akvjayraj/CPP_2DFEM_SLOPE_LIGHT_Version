using System;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Services;

/// <summary>Published Newmark sliding-block regressions. Displacements in cm.</summary>
public static class FeNewmarkEmpiricalModels
{
    private const double Log10JibsonSigma = 0.510;
    private const double Log10JibsonAriasSigma = 0.616;
    private const double Log10JibsonMagnitudeSigma = 0.454;
    private const double LnBrayTravasarouSigma = 0.66;

    public static FeNewmarkModelResult Jibson2007(double ky, double amaxG)
    {
        const string name = "Jibson (2007) eq. 6";
        if (!(amaxG > 0)) return Unavailable(name, "Requires PGA > 0.");
        if (ky <= 0) return Unbounded(name);
        var ratio = ky / amaxG;
        if (ratio >= 1.0) return NoDisplacement(name, ky, amaxG);
        var log10D = 0.215 + Math.Log10(Math.Pow(1.0 - ratio, 2.341) * Math.Pow(ratio, -1.438));
        return FromLog10(name, log10D, Log10JibsonSigma, $"k_y/a_max = {ratio:0.###}");
    }

    public static FeNewmarkModelResult Jibson2007Magnitude(double ky, double amaxG, double magnitude)
    {
        const string name = "Jibson (2007) magnitude form";
        if (!(amaxG > 0)) return Unavailable(name, "Requires PGA > 0.");
        if (ky <= 0) return Unbounded(name);
        var ratio = ky / amaxG;
        if (ratio >= 1.0) return NoDisplacement(name, ky, amaxG);
        var log10D = -2.710 + Math.Log10(Math.Pow(1.0 - ratio, 2.335) * Math.Pow(ratio, -1.478)) + 0.424 * magnitude;
        return FromLog10(name, log10D, Log10JibsonMagnitudeSigma, $"M = {magnitude:0.##}, k_y/a_max = {ratio:0.###}");
    }

    public static FeNewmarkModelResult Jibson2007Arias(double ky, double amaxG, double ariasIntensity)
    {
        const string name = "Jibson (2007) Arias form";
        if (!(amaxG > 0) || !(ariasIntensity > 0)) return Unavailable(name, "Requires PGA and I_a > 0.");
        if (ky <= 0) return Unbounded(name);
        var ratio = ky / amaxG;
        if (ratio >= 1.0) return NoDisplacement(name, ky, amaxG);
        var log10D = 0.561 * Math.Log10(ariasIntensity) - 3.833 * Math.Log10(ratio) - 1.474;
        return FromLog10(name, log10D, Log10JibsonAriasSigma, $"I_a = {ariasIntensity:0.###} m/s");
    }

    public static FeNewmarkModelResult AmbraseysMenzies1988(double ky, double amaxG)
    {
        const string name = "Ambraseys and Menzies (1988)";
        if (!(amaxG > 0)) return Unavailable(name, "Requires PGA > 0.");
        if (ky <= 0) return Unbounded(name);
        var ratio = ky / amaxG;
        if (ratio >= 1.0) return NoDisplacement(name, ky, amaxG);
        var log10D = 0.90 + Math.Log10(Math.Pow(1.0 - ratio, 2.53) * Math.Pow(ratio, -1.09));
        return new FeNewmarkModelResult
        {
            ModelName = name,
            DisplacementCm = Math.Pow(10.0, log10D),
            Notes = $"k_y/a_max = {ratio:0.###}"
        };
    }

    public static FeNewmarkModelResult MakdisiSeed1978(double ky, double kmaxG, double magnitude, double periodT0)
    {
        const string name = "Makdisi and Seed (1978)";
        if (!(kmaxG > 0) || !(periodT0 > 0)) return Unavailable(name, "Requires k_max and T_0 > 0.");
        if (ky <= 0) return Unbounded(name);
        var ratio = ky / kmaxG;
        if (ratio >= 1.0) return NoDisplacement(name, ky, kmaxG);
        var clamped = Math.Max(MakdisiRatios[0], ratio);
        var normalized = InterpolateMakdisi(clamped, magnitude);
        var displacementM = normalized * kmaxG * 9.81 * periodT0;
        return new FeNewmarkModelResult
        {
            ModelName = name,
            DisplacementCm = displacementM * 100.0,
            Notes = $"k_y/k_max = {ratio:0.###}, T_0 = {periodT0:0.##} s"
        };
    }

    public static FeNewmarkModelResult BrayTravasarou2007(
        double ky, double spectralAccelerationG, double periodTs, double magnitude, double pgaG)
    {
        const string name = "Bray and Travasarou (2007)";
        if (ky <= 0) return Unbounded(name);
        var rigid = periodTs < 0.05;
        var sa = rigid ? pgaG : spectralAccelerationG;
        if (!(sa > 0)) return Unavailable(name, "Requires S_a or PGA > 0.");
        var lnKy = Math.Log(ky);
        var lnSa = Math.Log(sa);
        var lnD = (rigid ? -0.22 : -1.10)
            - 2.83 * lnKy - 0.333 * lnKy * lnKy + 0.566 * lnKy * lnSa
            + 3.04 * lnSa - 0.244 * lnSa * lnSa
            + (rigid ? 0.0 : 1.50 * periodTs)
            + 0.278 * (magnitude - 7.0);
        return new FeNewmarkModelResult
        {
            ModelName = name,
            DisplacementCm = Math.Exp(lnD),
            DisplacementLowerCm = Math.Exp(lnD - LnBrayTravasarouSigma),
            DisplacementUpperCm = Math.Exp(lnD + LnBrayTravasarouSigma),
            Notes = rigid ? $"Rigid block, PGA = {pgaG:0.###} g" : $"T_s = {periodTs:0.##} s, S_a = {sa:0.###} g"
        };
    }

    private static readonly double[] MakdisiRatios =
        { 0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 };

    private static readonly double[] MakdisiMagnitudes = { 6.5, 7.5, 8.25 };

    private static readonly double[][] MakdisiNormalized =
    {
        new[] { 1.0, 0.30, 0.080, 0.030, 0.0130, 0.0060, 0.0030, 0.00150, 0.00070, 0.00040, 0.00020 },
        new[] { 3.0, 1.00, 0.300, 0.120, 0.0500, 0.0240, 0.0120, 0.00600, 0.00300, 0.00150, 0.00080 },
        new[] { 10.0, 3.00, 1.000, 0.400, 0.2000, 0.1000, 0.0500, 0.02500, 0.01200, 0.00600, 0.00300 }
    };

    private static double InterpolateMakdisi(double ratio, double magnitude)
    {
        var m = Math.Clamp(magnitude, MakdisiMagnitudes[0], MakdisiMagnitudes[^1]);
        var curves = new double[MakdisiMagnitudes.Length];
        for (var i = 0; i < curves.Length; i++)
            curves[i] = LogInterpolate(MakdisiRatios, MakdisiNormalized[i], ratio);
        for (var i = 0; i < MakdisiMagnitudes.Length - 1; i++)
        {
            if (m > MakdisiMagnitudes[i + 1]) continue;
            var t = (m - MakdisiMagnitudes[i]) / (MakdisiMagnitudes[i + 1] - MakdisiMagnitudes[i]);
            return Math.Exp((1 - t) * Math.Log(curves[i]) + t * Math.Log(curves[i + 1]));
        }
        return curves[^1];
    }

    private static double LogInterpolate(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        for (var i = 0; i < xs.Length - 1; i++)
        {
            if (x > xs[i + 1]) continue;
            var t = (x - xs[i]) / (xs[i + 1] - xs[i]);
            return Math.Exp((1 - t) * Math.Log(ys[i]) + t * Math.Log(ys[i + 1]));
        }
        return ys[^1];
    }

    private static FeNewmarkModelResult FromLog10(string name, double log10D, double sigma, string notes) => new()
    {
        ModelName = name,
        DisplacementCm = Math.Pow(10.0, log10D),
        DisplacementLowerCm = Math.Pow(10.0, log10D - sigma),
        DisplacementUpperCm = Math.Pow(10.0, log10D + sigma),
        Notes = notes
    };

    private static FeNewmarkModelResult NoDisplacement(string name, double ky, double amax) => new()
    {
        ModelName = name,
        DisplacementCm = 0.0,
        Notes = $"k_y ({ky:0.###} g) exceeds a_max ({amax:0.###} g)"
    };

    private static FeNewmarkModelResult Unavailable(string name, string notes) => new()
    {
        ModelName = name,
        Notes = notes
    };

    private static FeNewmarkModelResult Unbounded(string name) => new()
    {
        ModelName = name,
        DisplacementCm = double.PositiveInfinity,
        Notes = "k_y = 0: the slope yields under gravity."
    };
}
