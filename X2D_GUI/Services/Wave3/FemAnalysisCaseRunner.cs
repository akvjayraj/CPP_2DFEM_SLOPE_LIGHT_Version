using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Wave3;

namespace GeotechSlopeApp.Services.Wave3;

public static class FemAnalysisCaseRunner
{
    public static double EvaluateCase(
        FemAnalysisCase analysisCase,
        IReadOnlyList<MaterialModel> materials,
        IFemFactorEvaluator evaluator)
    {
        var fos = evaluator.Evaluate(new FemEvaluateRequest(
            materials,
            analysisCase.SeismicKh,
            analysisCase.SeismicKv,
            analysisCase.LoadFactor));
        analysisCase.LastFoS = double.IsFinite(fos) ? fos : null;
        analysisCase.Status = double.IsFinite(fos) ? "Evaluated" : "Failed";
        return fos;
    }

    public static int EvaluateActive(
        IEnumerable<FemAnalysisCase> cases,
        IReadOnlyList<MaterialModel> materials,
        IFemFactorEvaluator evaluator)
    {
        var n = 0;
        foreach (var c in cases.Where(c => c.IsActive))
        {
            EvaluateCase(c, materials, evaluator);
            n++;
        }
        return n;
    }
}
