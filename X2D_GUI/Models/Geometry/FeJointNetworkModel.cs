namespace GeotechSlopeApp.Models.Geometry;

/// <summary>RS2-style joint-network generator (select this before other parameters).</summary>
public enum FeJointNetworkModel
{
    ParallelDeterministic,
    ParallelStatistical,
    CrossJointed,
    Baecher,
    Veneziano,
    Voronoi
}

public enum FeJointDistributionKind
{
    Constant,
    Uniform,
    Normal,
    Lognormal,
    NegativeExponential
}

/// <summary>RS2 Voronoi regularity (Lloyd relaxation vs hex lattice).</summary>
public enum FeJointVoronoiRegularity
{
    Irregular,
    MediumRegular,
    AlmostRegular,
    RegularHexagon
}

public enum FeJointVoronoiDensityMethod
{
    Density,
    SiteCount,
    AverageJointLength
}

public static class FeJointNetworkModelNames
{
    public static string DisplayName(FeJointNetworkModel model) => model switch
    {
        FeJointNetworkModel.ParallelDeterministic => "Parallel Deterministic",
        FeJointNetworkModel.ParallelStatistical => "Parallel Statistical",
        FeJointNetworkModel.CrossJointed => "Cross Jointed",
        FeJointNetworkModel.Baecher => "Baecher",
        FeJointNetworkModel.Veneziano => "Veneziano",
        FeJointNetworkModel.Voronoi => "Voronoi",
        _ => model.ToString()
    };

    public static string DisplayName(FeJointDistributionKind kind) => kind switch
    {
        FeJointDistributionKind.Constant => "Constant",
        FeJointDistributionKind.Uniform => "Uniform",
        FeJointDistributionKind.Normal => "Normal",
        FeJointDistributionKind.Lognormal => "Lognormal",
        FeJointDistributionKind.NegativeExponential => "Negative Exponential",
        _ => kind.ToString()
    };

    public static string DisplayName(FeJointVoronoiRegularity regularity) => regularity switch
    {
        FeJointVoronoiRegularity.Irregular => "Irregular",
        FeJointVoronoiRegularity.MediumRegular => "Medium Regular",
        FeJointVoronoiRegularity.AlmostRegular => "Almost Regular",
        FeJointVoronoiRegularity.RegularHexagon => "Regular Hexagon",
        _ => regularity.ToString()
    };
}
