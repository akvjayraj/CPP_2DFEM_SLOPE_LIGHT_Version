using CommunityToolkit.Mvvm.ComponentModel;

namespace GeotechSlopeApp.Models.Wave3;

public enum MaterialStatPropertyId
{
    YoungsModulus,
    PoissonsRatio,
    UnitWeight,
    SaturatedUnitWeight,
    Cohesion,
    FrictionAngle,
    DilationAngle,
    TensileCutoff,
    Ucs,
    HoekBrownMi,
    Gsi,
    Disturbance,
}

public sealed class MaterialPropertyStatistics : ObservableObject
{
    private bool _useAsRv;
    private FemStatDistribution _distribution = FemStatDistribution.None;
    private double _covPercent;
    private double _min;
    private double _max;

    public MaterialStatPropertyId PropertyId { get; init; }

    public bool UseAsRv
    {
        get => _useAsRv;
        set
        {
            if (!SetProperty(ref _useAsRv, value)) return;
            if (value && Distribution == FemStatDistribution.None)
                Distribution = FemStatDistribution.Normal;
        }
    }

    public FemStatDistribution Distribution
    {
        get => _distribution;
        set => SetProperty(ref _distribution, value);
    }

    public double CovPercent
    {
        get => _covPercent;
        set => SetProperty(ref _covPercent, value);
    }

    public double Min
    {
        get => _min;
        set => SetProperty(ref _min, value);
    }

    public double Max
    {
        get => _max;
        set => SetProperty(ref _max, value);
    }

    public bool IsActiveRv =>
        UseAsRv && Distribution != FemStatDistribution.None
        && (CovPercent > 0
            || (FemStatDistributionInfo.IsBoundsDriven(Distribution) && Max > Min));

    public MaterialPropertyStatistics Clone() => new()
    {
        PropertyId = PropertyId,
        UseAsRv = UseAsRv,
        Distribution = Distribution,
        CovPercent = CovPercent,
        Min = Min,
        Max = Max,
    };

    public static MaterialPropertyStatistics CreateDefault(MaterialStatPropertyId id)
    {
        var (cov, min, max) = id switch
        {
            MaterialStatPropertyId.Cohesion => (20, 0, 1e6),
            MaterialStatPropertyId.FrictionAngle => (15, 0, 45),
            MaterialStatPropertyId.UnitWeight => (5, 10, 30),
            MaterialStatPropertyId.SaturatedUnitWeight => (5, 10, 30),
            MaterialStatPropertyId.YoungsModulus => (20, 0, 1e9),
            MaterialStatPropertyId.PoissonsRatio => (10, 0, 0.49),
            _ => (20, 0, 1e6),
        };
        return new MaterialPropertyStatistics
        {
            PropertyId = id,
            Distribution = FemStatDistribution.None,
            CovPercent = cov,
            Min = min,
            Max = max,
        };
    }

    public static string DisplayName(MaterialStatPropertyId id) => id switch
    {
        MaterialStatPropertyId.YoungsModulus => "Young's modulus E",
        MaterialStatPropertyId.PoissonsRatio => "Poisson's ratio ν",
        MaterialStatPropertyId.UnitWeight => "Unit weight γ",
        MaterialStatPropertyId.SaturatedUnitWeight => "Sat. unit weight γ_sat",
        MaterialStatPropertyId.Cohesion => "Cohesion c",
        MaterialStatPropertyId.FrictionAngle => "Friction angle φ",
        MaterialStatPropertyId.DilationAngle => "Dilation angle ψ",
        MaterialStatPropertyId.TensileCutoff => "Tensile cutoff σ_t",
        MaterialStatPropertyId.Ucs => "UCS σ_ci",
        MaterialStatPropertyId.HoekBrownMi => "Hoek-Brown m_i",
        MaterialStatPropertyId.Gsi => "GSI",
        MaterialStatPropertyId.Disturbance => "Disturbance D",
        _ => id.ToString(),
    };
}
