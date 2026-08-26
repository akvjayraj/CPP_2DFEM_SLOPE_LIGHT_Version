using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Wave3;

namespace GeotechSlopeApp.Services.Wave3;

public static class FemBackAnalysisRunner
{
    public sealed record MaterialRow(int MaterialId, string Name, double CohesionBefore, double PhiBefore, double CohesionAfter, double PhiAfter);

    public sealed record IterationRow(int Iteration, double Factor, double FactorOfSafety);

    public sealed record Result(
        bool Converged,
        double Factor,
        double BaselineFs,
        double AchievedFs,
        IReadOnlyList<MaterialRow> Materials,
        IReadOnlyList<IterationRow> Iterations);

    /// <summary>
    /// Finds F such that SRF(c/F, atan(tan φ / F)) ≈ target. When the evaluator is
    /// homogeneous of degree 1 in strength, F = baseline / target.
    /// </summary>
    public static Result Run(
        FemBackAnalysisSettings settings,
        IReadOnlyList<MaterialModel> materials,
        IFemFactorEvaluator evaluator,
        IReadOnlySet<int>? excludedMaterialIds = null)
    {
        var baseline = FemMaterialAccess.CloneAll(materials);
        var include = baseline
            .Where(m => excludedMaterialIds == null || !excludedMaterialIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToHashSet();
        var baseFs = evaluator.Evaluate(new FemEvaluateRequest(baseline));
        var iterations = new List<IterationRow>();

        if (!double.IsFinite(baseFs) || baseFs <= 0)
        {
            return new Result(false, double.NaN, baseFs, double.NaN, Array.Empty<MaterialRow>(), iterations);
        }

        var lo = Math.Min(settings.MinFactor, settings.MaxFactor);
        var hi = Math.Max(settings.MinFactor, settings.MaxFactor);
        var factor = Math.Clamp(baseFs / Math.Max(1e-9, settings.TargetFs), lo, hi);
        var achieved = EvaluateScaled(baseline, include, factor, evaluator);
        iterations.Add(new IterationRow(0, factor, achieved));

        for (var i = 1; i <= settings.MaxIterations && double.IsFinite(achieved); i++)
        {
            if (Math.Abs(achieved - settings.TargetFs) <= settings.FsTolerance)
                break;

            if (achieved > settings.TargetFs)
                lo = factor;
            else
                hi = factor;
            if (hi - lo < 1e-12) break;
            factor = 0.5 * (lo + hi);
            achieved = EvaluateScaled(baseline, include, factor, evaluator);
            iterations.Add(new IterationRow(i, factor, achieved));
        }

        var scaled = Scale(baseline, include, factor);
        var rows = baseline.Select(b =>
        {
            var a = scaled.First(s => s.Id == b.Id);
            return new MaterialRow(b.Id, b.Name, b.Cohesion, b.FrictionAngle, a.Cohesion, a.FrictionAngle);
        }).ToList();

        var converged = double.IsFinite(achieved)
            && Math.Abs(achieved - settings.TargetFs) <= settings.FsTolerance;
        return new Result(converged, factor, baseFs, achieved, rows, iterations);
    }

    public static void ApplyToMaterials(IList<MaterialModel> live, Result result)
    {
        var byId = live.ToDictionary(m => m.Id);
        foreach (var row in result.Materials)
        {
            if (!byId.TryGetValue(row.MaterialId, out var m)) continue;
            m.Cohesion = row.CohesionAfter;
            m.FrictionAngle = row.PhiAfter;
        }
    }

    public static string BuildCsv(Result result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Converged,{result.Converged}");
        sb.AppendLine($"Factor,{result.Factor.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"BaselineFs,{result.BaselineFs.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine($"AchievedFs,{result.AchievedFs.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("MaterialId,Name,CohesionBefore,PhiBefore,CohesionAfter,PhiAfter");
        foreach (var m in result.Materials)
        {
            sb.Append(m.MaterialId).Append(',')
                .Append(m.Name).Append(',')
                .Append(m.CohesionBefore.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(m.PhiBefore.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(m.CohesionAfter.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(m.PhiAfter.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static double EvaluateScaled(
        IReadOnlyList<MaterialModel> baseline,
        HashSet<int> include,
        double factor,
        IFemFactorEvaluator evaluator)
        => evaluator.Evaluate(new FemEvaluateRequest(Scale(baseline, include, factor)));

    private static List<MaterialModel> Scale(
        IReadOnlyList<MaterialModel> baseline,
        HashSet<int> include,
        double factor)
    {
        var f = Math.Max(1e-9, factor);
        var clone = FemMaterialAccess.CloneAll(baseline);
        foreach (var m in clone)
        {
            if (!include.Contains(m.Id)) continue;
            m.Cohesion = m.Cohesion / f;
            var tan = Math.Tan(m.FrictionAngle * Math.PI / 180.0) / f;
            m.FrictionAngle = Math.Atan(tan) * 180.0 / Math.PI;
        }
        return clone;
    }
}
