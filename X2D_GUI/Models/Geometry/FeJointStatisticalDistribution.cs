using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models.Geometry;

/// <summary>
/// RS2-style statistical parameter (Constant / Uniform / Normal / Lognormal / Negative Exponential).
/// When <see cref="AutoMinMax3Std"/> is on, bounds are mean ± 3σ, then clamped to stay valid.
/// </summary>
public sealed class FeJointStatisticalDistribution : INotifyPropertyChanged
{
    private FeJointDistributionKind _kind = FeJointDistributionKind.Constant;
    private double _mean = 2;
    private double _stdDev = 0.4;
    private double _min;
    private double _max = 4;
    private bool _autoMinMax3Std = true;

    public FeJointDistributionKind Kind
    {
        get => _kind;
        set { if (Set(ref _kind, value)) OnPropertyChanged(nameof(UsesStdDev)); }
    }

    public double Mean { get => _mean; set => Set(ref _mean, value); }
    public double StdDev { get => _stdDev; set => Set(ref _stdDev, Math.Max(0, value)); }
    public double Min { get => _min; set => Set(ref _min, value); }
    public double Max { get => _max; set => Set(ref _max, value); }
    public bool AutoMinMax3Std { get => _autoMinMax3Std; set => Set(ref _autoMinMax3Std, value); }

    public bool UsesStdDev => Kind is FeJointDistributionKind.Normal
        or FeJointDistributionKind.Lognormal
        or FeJointDistributionKind.NegativeExponential;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static FeJointStatisticalDistribution Constant(double mean) => new()
    {
        Kind = FeJointDistributionKind.Constant,
        Mean = mean,
        Min = mean,
        Max = mean,
        StdDev = 0,
        AutoMinMax3Std = false
    };

    public static FeJointStatisticalDistribution Normal(double mean, double stdDev) => new()
    {
        Kind = FeJointDistributionKind.Normal,
        Mean = mean,
        StdDev = stdDev,
        AutoMinMax3Std = true
    };

    public FeJointStatisticalDistribution Clone() => new()
    {
        Kind = Kind,
        Mean = Mean,
        StdDev = StdDev,
        Min = Min,
        Max = Max,
        AutoMinMax3Std = AutoMinMax3Std
    };

    public void CopyFrom(FeJointStatisticalDistribution src)
    {
        Kind = src.Kind;
        Mean = src.Mean;
        StdDev = src.StdDev;
        Min = src.Min;
        Max = src.Max;
        AutoMinMax3Std = src.AutoMinMax3Std;
    }

    public void GetBounds(double floor, double ceiling, out double lo, out double hi)
    {
        if (Kind == FeJointDistributionKind.Constant)
        {
            lo = hi = Mean;
            return;
        }

        if (Kind == FeJointDistributionKind.Uniform && Max > Min)
        {
            lo = Min;
            hi = Max;
        }
        else if (AutoMinMax3Std && StdDev > 0)
        {
            var span = 3.0 * StdDev;
            lo = Mean - span;
            hi = Mean + span;
        }
        else if (Max > Min)
        {
            lo = Min;
            hi = Max;
        }
        else
        {
            lo = Mean - Math.Max(StdDev, 1e-6);
            hi = Mean + Math.Max(StdDev, 1e-6);
        }

        if (lo < floor) lo = floor;
        if (hi > ceiling) hi = ceiling;
        if (hi < lo) hi = lo;
    }

    public double Sample(Random rng, double floor = 1e-6, double ceiling = 1e6)
    {
        rng ??= Random.Shared;
        GetBounds(floor, ceiling, out var lo, out var hi);
        if (Kind == FeJointDistributionKind.Constant || hi <= lo + 1e-15)
            return Clamp(Mean, lo, hi);

        for (var attempt = 0; attempt < 24; attempt++)
        {
            var value = Kind switch
            {
                FeJointDistributionKind.Uniform => lo + rng.NextDouble() * (hi - lo),
                FeJointDistributionKind.Normal => SampleNormal(rng),
                FeJointDistributionKind.Lognormal => SampleLognormal(rng),
                FeJointDistributionKind.NegativeExponential => SampleExponential(rng),
                _ => Mean
            };
            if (value >= lo - 1e-12 && value <= hi + 1e-12)
                return Clamp(value, lo, hi);
        }

        return Clamp(Mean, lo, hi);
    }

    private double SampleNormal(Random rng)
    {
        var u1 = Math.Max(1e-12, rng.NextDouble());
        var u2 = rng.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Mean + StdDev * z;
    }

    private double SampleLognormal(Random rng)
    {
        var m = Math.Max(1e-9, Mean);
        var s = Math.Max(0, StdDev);
        var cv = s / m;
        var sigma = Math.Sqrt(Math.Log(1.0 + cv * cv));
        var mu = Math.Log(m / Math.Sqrt(1.0 + cv * cv));
        var u1 = Math.Max(1e-12, rng.NextDouble());
        var u2 = rng.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Math.Exp(mu + sigma * z);
    }

    private double SampleExponential(Random rng)
    {
        var mean = Math.Max(1e-9, Mean);
        var u = Math.Max(1e-12, 1.0 - rng.NextDouble());
        return -mean * Math.Log(u);
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
