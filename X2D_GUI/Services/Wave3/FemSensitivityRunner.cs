using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Wave3;

namespace GeotechSlopeApp.Services.Wave3;

public static class FemSensitivityRunner
{
    public sealed record ParamKey(int MaterialId, string MaterialName, MaterialStatPropertyId Property, double BaseValue)
    {
        public string Display => $"{MaterialName} — {MaterialPropertyStatistics.DisplayName(Property)}";
        public override string ToString() => Display;
    }

    public sealed record ResultRow(string Parameter, double Value, double FactorOfSafety, string Note);

    public static List<ParamKey> BuildParameterList(IEnumerable<MaterialModel> materials)
    {
        var list = new List<ParamKey>();
        foreach (var m in materials)
        {
            foreach (var id in FemMaterialAccess.SensitivityProperties)
                list.Add(new ParamKey(m.Id, m.Name, id, FemMaterialAccess.Get(m, id)));
        }
        return list;
    }

    public static List<ResultRow> RunOneAtATime(
        FemSensitivitySettings settings,
        IReadOnlyList<MaterialModel> materials,
        IFemFactorEvaluator evaluator)
    {
        var baseline = FemMaterialAccess.CloneAll(materials);
        var baseFs = evaluator.Evaluate(new FemEvaluateRequest(baseline));
        var rows = new List<ResultRow>
        {
            new("Baseline", 0, baseFs, "Unperturbed materials"),
        };

        var pct = Math.Max(0, settings.MaterialVariationPercent) / 100.0;
        var keys = BuildParameterList(baseline);
        foreach (var key in keys)
        {
            foreach (var sign in new[] { -1.0, 1.0 })
            {
                var trial = FemMaterialAccess.CloneAll(baseline);
                var target = trial.First(m => m.Id == key.MaterialId);
                var value = key.BaseValue * (1.0 + sign * pct);
                FemMaterialAccess.Set(target, key.Property, value);
                var fos = evaluator.Evaluate(new FemEvaluateRequest(trial));
                var label = $"{key.Display} {(sign < 0 ? "−" : "+")}{settings.MaterialVariationPercent:G4}%";
                rows.Add(new ResultRow(label, value, fos, double.IsFinite(fos) ? "" : "invalid"));
            }
        }

        return rows;
    }

    public static List<ResultRow> RunParametric(
        ParamKey key,
        double start,
        double end,
        double increment,
        IReadOnlyList<MaterialModel> materials,
        IFemFactorEvaluator evaluator)
    {
        var rows = new List<ResultRow>();
        if (!(Math.Abs(increment) > 1e-12) || !double.IsFinite(start) || !double.IsFinite(end))
            return rows;

        var dir = Math.Sign(end - start);
        if (dir == 0) dir = 1;
        increment = Math.Abs(increment) * dir;
        var steps = 0;
        for (var v = start; dir > 0 ? v <= end + 1e-12 : v >= end - 1e-12; v += increment)
        {
            if (++steps > 500) break;
            var trial = FemMaterialAccess.CloneAll(materials);
            var target = trial.First(m => m.Id == key.MaterialId);
            FemMaterialAccess.Set(target, key.Property, v);
            var fos = evaluator.Evaluate(new FemEvaluateRequest(trial));
            rows.Add(new ResultRow(key.Display, v, fos, double.IsFinite(fos) ? "" : "invalid"));
        }

        return rows;
    }

    public static string BuildTornadoCsv(IEnumerable<ResultRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Parameter,Value,FactorOfSafety,Note");
        foreach (var r in rows)
        {
            sb.Append(Csv(r.Parameter)).Append(',')
                .Append(r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(r.FactorOfSafety.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(Csv(r.Note));
        }
        return sb.ToString();
    }

    private static string Csv(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
