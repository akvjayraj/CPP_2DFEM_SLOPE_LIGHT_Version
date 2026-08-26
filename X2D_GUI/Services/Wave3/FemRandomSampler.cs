using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Wave3;

namespace GeotechSlopeApp.Services.Wave3;

public sealed class FemRandomVariable
{
    public int MaterialId { get; init; }
    public string PropertyName { get; init; } = "";
    public MaterialStatPropertyId PropertyId { get; init; }
    public FemStatDistribution Distribution { get; init; }
    public double Mean { get; init; }
    public double StdDev { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public Dictionary<int, double> Correlations { get; } = new();
}

public static class FemRandomSampler
{
    public static List<FemRandomVariable> Collect(IEnumerable<MaterialModel> materials)
    {
        var list = new List<FemRandomVariable>();
        foreach (var m in materials)
        {
            if (!m.EnableProbabilisticVariation) continue;
            var slots = new Dictionary<MaterialStatPropertyId, int>();
            foreach (MaterialStatPropertyId id in Enum.GetValues<MaterialStatPropertyId>())
            {
                var s = FemMaterialAccess.GetStats(m, id);
                if (!s.IsActiveRv) continue;
                slots[id] = list.Count;
                var mean = FemMaterialAccess.Get(m, id);
                var std = Math.Abs(mean) * Math.Max(0, s.CovPercent) / 100.0;
                list.Add(new FemRandomVariable
                {
                    MaterialId = m.Id,
                    PropertyName = id.ToString(),
                    PropertyId = id,
                    Distribution = s.Distribution,
                    Mean = mean,
                    StdDev = Math.Max(std, 1e-12),
                    Min = s.Min,
                    Max = s.Max,
                });
            }

            Link(list, slots, MaterialStatPropertyId.Cohesion, MaterialStatPropertyId.FrictionAngle,
                m.CohesionPhiCorrelation);
        }

        return list;
    }

    private static void Link(
        List<FemRandomVariable> list,
        Dictionary<MaterialStatPropertyId, int> slots,
        MaterialStatPropertyId a,
        MaterialStatPropertyId b,
        double rho)
    {
        if (Math.Abs(rho) < 1e-9) return;
        if (!slots.TryGetValue(a, out var i) || !slots.TryGetValue(b, out var j)) return;
        var clamped = Math.Clamp(rho, -0.999, 0.999);
        list[i].Correlations[j] = clamped;
        list[j].Correlations[i] = clamped;
    }

    public static List<double[]> Draw(
        IReadOnlyList<FemRandomVariable> rvs,
        FemProbabilisticEngine engine,
        int sampleCount,
        int seed)
    {
        if (rvs.Count == 0) return new List<double[]>();
        return engine switch
        {
            FemProbabilisticEngine.PointEstimate => BuildPem(rvs),
            FemProbabilisticEngine.LatinHypercube => LatinHypercube(rvs, sampleCount, seed),
            _ => MonteCarlo(rvs, sampleCount, seed),
        };
    }

    public static List<MaterialModel> ApplyDraw(
        IReadOnlyList<MaterialModel> baseline,
        IReadOnlyList<FemRandomVariable> rvs,
        double[] values)
    {
        var clone = FemMaterialAccess.CloneAll(baseline);
        var byId = clone.ToDictionary(m => m.Id);
        for (var i = 0; i < rvs.Count && i < values.Length; i++)
        {
            if (!byId.TryGetValue(rvs[i].MaterialId, out var m)) continue;
            FemMaterialAccess.Set(m, rvs[i].PropertyId, values[i]);
        }
        return clone;
    }

    private static List<double[]> MonteCarlo(IReadOnlyList<FemRandomVariable> rvs, int n, int seed)
    {
        var rng = new Random(seed);
        var factor = CholeskyFactor(rvs);
        var draws = new List<double[]>(n);
        for (var i = 0; i < n; i++)
        {
            var z = new double[rvs.Count];
            for (var j = 0; j < rvs.Count; j++)
                z[j] = InvNorm(rng.NextDouble());
            ApplyCorrelation(factor, z);
            draws.Add(Transform(rvs, z));
        }
        return draws;
    }

    private static List<double[]> LatinHypercube(IReadOnlyList<FemRandomVariable> rvs, int n, int seed)
    {
        var rng = new Random(seed);
        var dim = rvs.Count;
        var u = new double[dim, n];
        for (var d = 0; d < dim; d++)
        {
            var perm = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).ToArray();
            for (var i = 0; i < n; i++)
                u[d, i] = (perm[i] + rng.NextDouble()) / n;
        }

        var factor = CholeskyFactor(rvs);
        var draws = new List<double[]>(n);
        for (var i = 0; i < n; i++)
        {
            var z = new double[dim];
            for (var d = 0; d < dim; d++)
                z[d] = InvNorm(u[d, i]);
            ApplyCorrelation(factor, z);
            draws.Add(Transform(rvs, z));
        }
        return draws;
    }

    private static List<double[]> BuildPem(IReadOnlyList<FemRandomVariable> rvs)
    {
        var n = Math.Min(rvs.Count, 10);
        var count = 1 << n;
        var list = new List<double[]>(count);
        for (var mask = 0; mask < count; mask++)
        {
            var z = new double[rvs.Count];
            for (var j = 0; j < rvs.Count; j++)
                z[j] = j >= n ? 0 : (((mask >> j) & 1) == 0 ? -1.0 : 1.0);
            list.Add(Transform(rvs, z));
        }
        return list;
    }

    private static double[] Transform(IReadOnlyList<FemRandomVariable> rvs, double[] z)
    {
        var row = new double[rvs.Count];
        for (var j = 0; j < rvs.Count; j++)
            row[j] = FromStandardNormal(rvs[j], z[j]);
        return row;
    }

    private static double FromStandardNormal(FemRandomVariable rv, double z)
    {
        var raw = rv.Distribution switch
        {
            FemStatDistribution.Lognormal => Lognormal(rv, z),
            FemStatDistribution.Uniform => Interpolate(rv.Min, rv.Max, NormCdf(z)),
            FemStatDistribution.Triangular => Triangular(rv, NormCdf(z)),
            FemStatDistribution.Beta => Beta(rv, NormCdf(z)),
            FemStatDistribution.Exponential => -Math.Max(rv.Mean, 1e-12) * Math.Log(Math.Max(1e-12, 1 - NormCdf(z))),
            FemStatDistribution.TruncatedNormal => TruncatedNormal(rv, NormCdf(z)),
            FemStatDistribution.TruncatedLognormal => TruncatedLognormal(rv, NormCdf(z)),
            _ => rv.Mean + rv.StdDev * z,
        };
        if (!double.IsFinite(raw)) raw = rv.Mean;
        return Math.Clamp(raw, rv.Min, rv.Max);
    }

    private static double Lognormal(FemRandomVariable rv, double z)
    {
        var mean = Math.Max(rv.Mean, 1e-12);
        var cv = rv.StdDev / mean;
        var s2 = Math.Log(1 + cv * cv);
        return Math.Exp(Math.Log(mean) - 0.5 * s2 + Math.Sqrt(s2) * z);
    }

    private static double Interpolate(double lo, double hi, double u) => lo + (hi - lo) * u;

    private static double Triangular(FemRandomVariable rv, double u)
    {
        var a = rv.Min;
        var b = rv.Max;
        if (!(b > a)) return rv.Mean;
        var c = Math.Clamp(rv.Mean, a, b);
        var split = (c - a) / (b - a);
        return u < split
            ? a + Math.Sqrt(u * (b - a) * (c - a))
            : b - Math.Sqrt((1 - u) * (b - a) * (b - c));
    }

    private static double Beta(FemRandomVariable rv, double u)
    {
        var a = rv.Min;
        var b = rv.Max;
        if (!(b > a)) return rv.Mean;
        var span = b - a;
        var m = Math.Clamp((rv.Mean - a) / span, 1e-6, 1 - 1e-6);
        var s = rv.StdDev / span;
        var maxVar = m * (1 - m);
        if (!(s > 0) || s * s >= maxVar)
            return Interpolate(a, b, u);
        return Interpolate(a, b, u);
    }

    private static double TruncatedNormal(FemRandomVariable rv, double u)
    {
        var a = NormCdf((rv.Min - rv.Mean) / rv.StdDev);
        var b = NormCdf((rv.Max - rv.Mean) / rv.StdDev);
        var t = a + Math.Clamp(u, 1e-12, 1 - 1e-12) * Math.Max(1e-12, b - a);
        return rv.Mean + rv.StdDev * InvNorm(t);
    }

    private static double TruncatedLognormal(FemRandomVariable rv, double u)
    {
        var mean = Math.Max(rv.Mean, 1e-12);
        var cv = rv.StdDev / mean;
        var s2 = Math.Log(1 + cv * cv);
        var mu = Math.Log(mean) - 0.5 * s2;
        var sigma = Math.Sqrt(s2);
        var a = NormCdf((Math.Log(Math.Max(rv.Min, 1e-12)) - mu) / sigma);
        var b = NormCdf((Math.Log(Math.Max(rv.Max, 1e-12)) - mu) / sigma);
        var t = a + Math.Clamp(u, 1e-12, 1 - 1e-12) * Math.Max(1e-12, b - a);
        return Math.Exp(mu + sigma * InvNorm(t));
    }

    private static void ApplyCorrelation(double[,]? factor, double[] z)
    {
        if (factor == null) return;
        var n = z.Length;
        var correlated = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            for (var j = 0; j <= i; j++)
                sum += factor[i, j] * z[j];
            correlated[i] = sum;
        }
        Array.Copy(correlated, z, n);
    }

    private static double[,]? CholeskyFactor(IReadOnlyList<FemRandomVariable> rvs)
    {
        var n = rvs.Count;
        if (n == 0 || rvs.All(rv => rv.Correlations.Count == 0)) return null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var blend = attempt == 0 ? 1.0 : Math.Pow(0.7, attempt);
            var l = new double[n, n];
            var ok = true;
            for (var i = 0; i < n && ok; i++)
            {
                for (var j = 0; j <= i; j++)
                {
                    var target = i == j ? 1.0 : blend * Correlation(rvs, i, j);
                    var sum = 0.0;
                    for (var k = 0; k < j; k++)
                        sum += l[i, k] * l[j, k];
                    if (i == j)
                    {
                        var diag = target - sum;
                        if (diag <= 1e-12) { ok = false; break; }
                        l[i, j] = Math.Sqrt(diag);
                    }
                    else
                    {
                        l[i, j] = (target - sum) / l[j, j];
                    }
                }
            }
            if (ok) return l;
        }
        return null;
    }

    private static double Correlation(IReadOnlyList<FemRandomVariable> rvs, int i, int j) =>
        rvs[i].Correlations.TryGetValue(j, out var rho) ? rho : 0;

    internal static double NormCdf(double z)
    {
        const double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741, a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;
        var sign = z < 0 ? -1.0 : 1.0;
        var x = Math.Abs(z) / Math.Sqrt(2);
        var t = 1.0 / (1.0 + p * x);
        var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return 0.5 * (1.0 + sign * y);
    }

    internal static double InvNorm(double p)
    {
        p = Math.Clamp(p, 1e-12, 1 - 1e-12);
        if (p < 0.5)
            return -InvNorm(1 - p);
        var q = p - 0.5;
        if (q <= 0.425)
        {
            const double a0 = 2.50662823884, a1 = -18.61500062529, a2 = 41.39119773534, a3 = -25.44106049637;
            const double b1 = -8.47351093090, b2 = 23.08336743743, b3 = -21.06224101826, b4 = 3.13082909833;
            var r = 0.180625 - q * q;
            return q * (((a3 * r + a2) * r + a1) * r + a0) / ((((b4 * r + b3) * r + b2) * r + b1) * r + 1);
        }

        var r2 = Math.Sqrt(-Math.Log(1 - p));
        const double c0 = 1.42343711007, c1 = 4.36828118015, c2 = 6.37332945446;
        return r2 - (c0 + c1 / r2 + c2 / (r2 * r2));
    }
}
