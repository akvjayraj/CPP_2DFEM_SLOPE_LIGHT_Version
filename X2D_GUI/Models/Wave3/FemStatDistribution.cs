namespace GeotechSlopeApp.Models.Wave3;

public enum FemStatDistribution
{
    None,
    Normal,
    Lognormal,
    Uniform,
    Triangular,
    Beta,
    Exponential,
    TruncatedNormal,
    TruncatedLognormal,
}

public enum FemProbabilisticEngine
{
    LatinHypercube,
    MonteCarlo,
    PointEstimate,
}

public static class FemStatDistributionInfo
{
    public static bool IsBoundsDriven(FemStatDistribution d) =>
        d is FemStatDistribution.Uniform or FemStatDistribution.Triangular;

    public static string DisplayName(FemStatDistribution d) => d switch
    {
        FemStatDistribution.None => "None",
        FemStatDistribution.Normal => "Normal",
        FemStatDistribution.Lognormal => "Lognormal",
        FemStatDistribution.Uniform => "Uniform",
        FemStatDistribution.Triangular => "Triangular",
        FemStatDistribution.Beta => "Beta",
        FemStatDistribution.Exponential => "Exponential",
        FemStatDistribution.TruncatedNormal => "Truncated normal",
        FemStatDistribution.TruncatedLognormal => "Truncated lognormal",
        _ => d.ToString(),
    };

    public static string DisplayName(FemProbabilisticEngine e) => e switch
    {
        FemProbabilisticEngine.LatinHypercube => "Latin Hypercube",
        FemProbabilisticEngine.MonteCarlo => "Monte Carlo",
        FemProbabilisticEngine.PointEstimate => "Point Estimate (PEM)",
        _ => e.ToString(),
    };
}
