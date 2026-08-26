using System;

namespace GeotechSlopeApp.ViewModels;

/// <summary>
/// One row of the Verification tab's analytical-vs-numerical comparison grid.
/// Shape matches the existing column bindings in
/// <c>X2D_GUI/Views/Tabs/VerificationView.xaml</c>
/// (<c>ProblemName</c>, <c>Description</c>, <c>AnalyticalValueFormatted</c>,
/// <c>FEMValueFormatted</c>, <c>Unit</c>, <c>RelativeErrorPercent</c>, <c>StatusText</c>,
/// <c>Passed</c>) so the grid renders without further XAML changes.
/// </summary>
public sealed class VerificationResultRow
{
    public string ProblemName { get; init; } = "";
    public string Description { get; init; } = "";
    public double AnalyticalValue { get; init; }
    public double FEMValue { get; init; }
    public string Unit { get; init; } = "";
    public double RelativeErrorPercent { get; init; }
    public bool Passed { get; init; }

    public string StatusText => Passed ? "PASS" : "FAIL";

    public string AnalyticalValueFormatted => Format(AnalyticalValue);
    public string FEMValueFormatted        => Format(FEMValue);

    /// <summary>
    /// Pretty-print picking exponential form for very small / very large magnitudes
    /// (typical of displacements in metres and stresses in kPa) and fixed-point
    /// otherwise so the grid stays readable for both regimes.
    /// </summary>
    private static string Format(double v)
    {
        double m = Math.Abs(v);
        if (double.IsNaN(v) || double.IsInfinity(v)) return v.ToString();
        if (m == 0.0) return "0";
        if (m >= 1e4 || m < 1e-2) return v.ToString("E3");
        return v.ToString("F4");
    }

    /// <summary>
    /// Build a row from analytical/FEM scalars, computing the relative error and
    /// pass/fail flag against <paramref name="tolPercent"/>. Falls back to absolute
    /// difference when <paramref name="analytical"/> is too close to zero (e.g. the
    /// wall <c>sigma_rr</c> sanity check).
    /// </summary>
    public static VerificationResultRow Build(
        string problemName,
        string description,
        double analytical,
        double fem,
        string unit,
        double tolPercent,
        double absoluteTolForZero = 1e-6)
    {
        double err;
        bool passed;
        if (Math.Abs(analytical) < absoluteTolForZero)
        {
            // Reference is ~0; report the absolute residual as a percentage of the
            // characteristic load (caller should pre-scale if needed) and accept when
            // the FEM value is also small in absolute terms.
            err = Math.Abs(fem) <= absoluteTolForZero ? 0.0 : 100.0;
            passed = Math.Abs(fem) <= Math.Max(absoluteTolForZero, tolPercent / 100.0 * absoluteTolForZero);
        }
        else
        {
            err = (fem - analytical) / analytical * 100.0;
            passed = Math.Abs(err) <= tolPercent;
        }

        return new VerificationResultRow
        {
            ProblemName = problemName,
            Description = description,
            AnalyticalValue = analytical,
            FEMValue = fem,
            Unit = unit,
            RelativeErrorPercent = err,
            Passed = passed,
        };
    }
}
