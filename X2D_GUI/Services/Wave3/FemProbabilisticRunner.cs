using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Wave3;

namespace GeotechSlopeApp.Services.Wave3;

public static class FemProbabilisticRunner
{
    public static FemProbabilisticResults Run(
        FemProbabilisticSettings settings,
        IReadOnlyList<MaterialModel> materials,
        IFemFactorEvaluator evaluator,
        Action<int, int>? progress = null)
    {
        var rvs = FemRandomSampler.Collect(materials);
        var engineName = FemStatDistributionInfo.DisplayName(settings.Engine);
        if (rvs.Count == 0)
        {
            return FemProbabilisticResults.FromSamples(
                Array.Empty<double>(), settings.SampleCount, settings.DesignFactorOfSafety, engineName);
        }

        var draws = FemRandomSampler.Draw(rvs, settings.Engine, settings.SampleCount, settings.ResolveSeed());
        var rows = new List<FemProbabilisticSampleRow>(draws.Count);
        for (var i = 0; i < draws.Count; i++)
        {
            progress?.Invoke(i + 1, draws.Count);
            var trial = FemRandomSampler.ApplyDraw(materials, rvs, draws[i]);
            var fos = evaluator.Evaluate(new FemEvaluateRequest(trial));
            var map = new Dictionary<string, double>(rvs.Count);
            for (var j = 0; j < rvs.Count && j < draws[i].Length; j++)
                map[FemProbabilisticResults.RvKey(rvs[j].MaterialId, rvs[j].PropertyName)] = draws[i][j];
            rows.Add(new FemProbabilisticSampleRow
            {
                Index = i + 1,
                FactorOfSafety = fos,
                IsValid = !double.IsNaN(fos) && !double.IsInfinity(fos) && fos > 0,
                RandomVariableDraws = map,
            });
        }

        return FemProbabilisticResults.FromSamples(
            rows.Select(r => r.FactorOfSafety).ToList(),
            rows.Count,
            settings.DesignFactorOfSafety,
            engineName,
            rows,
            rvs.Count);
    }

    public static string BuildSampleCsv(FemProbabilisticResults results)
    {
        var keys = results.SampleRows
            .SelectMany(r => r.RandomVariableDraws.Keys)
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append("Index,FactorOfSafety,Valid");
        foreach (var k in keys)
            sb.Append(',').Append(k);
        sb.AppendLine();
        foreach (var row in results.SampleRows)
        {
            sb.Append(row.Index).Append(',')
                .Append(row.FactorOfSafety.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(row.IsValid ? "1" : "0");
            foreach (var k in keys)
            {
                sb.Append(',');
                if (row.RandomVariableDraws.TryGetValue(k, out var v))
                    sb.Append(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
