using System.Globalization;
using System.Text;

namespace GeotechSlopeApp.Models.Wave3;

public sealed class FemProbabilisticSampleRow
{
    public int Index { get; init; }
    public double FactorOfSafety { get; init; }
    public bool IsValid { get; init; }
    public IReadOnlyDictionary<string, double> RandomVariableDraws { get; init; }
        = new Dictionary<string, double>();
}

public sealed class FemProbabilisticResults
{
    public string EngineName { get; init; } = "";
    public int SampleCount { get; init; }
    public int ValidSampleCount { get; init; }
    public double DesignFactorOfSafety { get; init; } = 1.0;
    public double MeanFs { get; init; }
    public double SigmaFs { get; init; }
    public double CovFs { get; init; }
    public double ProbabilityOfFailure { get; init; }
    public double ReliabilityIndex { get; init; }
    public double MinFs { get; init; }
    public double MaxFs { get; init; }
    public double Percentile5Fs { get; init; }
    public double Percentile50Fs { get; init; }
    public double Percentile95Fs { get; init; }
    public IReadOnlyList<double> FactorOfSafetySamples { get; init; } = Array.Empty<double>();
    public IReadOnlyList<(double BinCenter, int Count)> HistogramBins { get; init; }
        = Array.Empty<(double, int)>();
    public IReadOnlyList<FemProbabilisticSampleRow> SampleRows { get; init; }
        = Array.Empty<FemProbabilisticSampleRow>();
    public int ActiveRandomVariableCount { get; init; }

    public string FormatSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Probabilistic ({EngineName}): {ValidSampleCount:N0}/{SampleCount:N0} samples");
        if (ValidSampleCount <= 0 || double.IsNaN(MeanFs))
        {
            sb.AppendLine("No valid SRF samples.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"Mean SRF = {MeanFs:F3}");
        sb.AppendLine($"σ(SRF) = {SigmaFs:F3}");
        if (!double.IsNaN(CovFs))
            sb.AppendLine($"COV(SRF) = {CovFs:P1}");
        sb.AppendLine($"P(SRF < {DesignFactorOfSafety:F2}) = {ProbabilityOfFailure:P2}");
        sb.AppendLine(double.IsFinite(ReliabilityIndex)
            ? $"Reliability index β = {ReliabilityIndex:F3}"
            : "Reliability index β = N/A");
        sb.AppendLine($"P5 / P50 / P95 = {Percentile5Fs:F3} / {Percentile50Fs:F3} / {Percentile95Fs:F3}");
        return sb.ToString().TrimEnd();
    }

    public static string RvKey(int materialId, string propertyName) =>
        string.Create(CultureInfo.InvariantCulture, $"M{materialId}.{propertyName}");

    public static FemProbabilisticResults FromSamples(
        IReadOnlyList<double> samples,
        int requestedCount,
        double designFs,
        string engineName,
        IReadOnlyList<FemProbabilisticSampleRow>? sampleRows = null,
        int activeRandomVariableCount = 0)
    {
        var rows = sampleRows ?? samples.Select((f, i) => new FemProbabilisticSampleRow
        {
            Index = i + 1,
            FactorOfSafety = f,
            IsValid = IsValidFs(f),
        }).ToList();
        var valid = rows.Select(r => r.FactorOfSafety).Where(IsValidFs).ToList();
        if (valid.Count == 0)
        {
            return new FemProbabilisticResults
            {
                EngineName = engineName,
                SampleCount = requestedCount,
                ValidSampleCount = 0,
                DesignFactorOfSafety = designFs,
                MeanFs = double.NaN,
                SigmaFs = double.NaN,
                CovFs = double.NaN,
                ProbabilityOfFailure = double.NaN,
                ReliabilityIndex = double.NaN,
                MinFs = double.NaN,
                MaxFs = double.NaN,
                Percentile5Fs = double.NaN,
                Percentile50Fs = double.NaN,
                Percentile95Fs = double.NaN,
                FactorOfSafetySamples = rows.Select(r => r.FactorOfSafety).ToList(),
                SampleRows = rows,
                ActiveRandomVariableCount = activeRandomVariableCount,
            };
        }

        var mean = valid.Average();
        var variance = valid.Count > 1
            ? valid.Sum(v => (v - mean) * (v - mean)) / (valid.Count - 1)
            : 0.0;
        var sigma = Math.Sqrt(Math.Max(0, variance));
        var cov = Math.Abs(mean) > 1e-12 ? sigma / Math.Abs(mean) : double.NaN;
        var pf = valid.Count(v => v < designFs) / (double)valid.Count;
        var beta = sigma > 1e-12 ? (mean - designFs) / sigma : double.NaN;
        var sorted = valid.OrderBy(v => v).ToList();

        return new FemProbabilisticResults
        {
            EngineName = engineName,
            SampleCount = requestedCount,
            ValidSampleCount = valid.Count,
            DesignFactorOfSafety = designFs,
            MeanFs = mean,
            SigmaFs = sigma,
            CovFs = cov,
            ProbabilityOfFailure = pf,
            ReliabilityIndex = beta,
            MinFs = sorted[0],
            MaxFs = sorted[^1],
            Percentile5Fs = Percentile(sorted, 0.05),
            Percentile50Fs = Percentile(sorted, 0.50),
            Percentile95Fs = Percentile(sorted, 0.95),
            FactorOfSafetySamples = rows.Select(r => r.FactorOfSafety).ToList(),
            HistogramBins = BuildHistogram(valid, 20),
            SampleRows = rows,
            ActiveRandomVariableCount = activeRandomVariableCount,
        };
    }

    private static bool IsValidFs(double f) =>
        !double.IsNaN(f) && !double.IsInfinity(f) && f > 0;

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 1) return sorted[0];
        var pos = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(pos);
        var hi = Math.Min(sorted.Count - 1, lo + 1);
        var t = pos - lo;
        return sorted[lo] * (1 - t) + sorted[hi] * t;
    }

    private static List<(double, int)> BuildHistogram(IReadOnlyList<double> values, int bins)
    {
        if (values.Count == 0) return new List<(double, int)>();
        var min = values.Min();
        var max = values.Max();
        if (Math.Abs(max - min) < 1e-12)
            return new List<(double, int)> { (min, values.Count) };

        var width = (max - min) / bins;
        var counts = new int[bins];
        foreach (var v in values)
        {
            var idx = Math.Min(bins - 1, (int)((v - min) / width));
            counts[idx]++;
        }

        var list = new List<(double, int)>(bins);
        for (var i = 0; i < bins; i++)
            list.Add((min + (i + 0.5) * width, counts[i]));
        return list;
    }
}
