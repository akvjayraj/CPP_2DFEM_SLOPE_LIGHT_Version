using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Services.Wave3;

public sealed record FemEvaluateRequest(
    IReadOnlyList<MaterialModel> Materials,
    double SeismicKh = 0,
    double SeismicKv = 0,
    double LoadFactor = 1);

public interface IFemFactorEvaluator
{
    /// <summary>Returns an SRF / FoS for the request, or NaN when evaluation is not possible.</summary>
    double Evaluate(FemEvaluateRequest request);
}

/// <summary>
/// Scales the last computed SRF by Mohr–Coulomb strength, load factor, and a
/// first-order pseudo-static penalty 1+|k_h|. Used when a full SRF re-run is
/// not requested or the native solver is unavailable.
/// </summary>
public sealed class LastSrfScaledEvaluator : IFemFactorEvaluator
{
    private readonly double _baselineSrf;
    private readonly double _baselineStrength;
    private readonly double _baselineKh;
    private readonly double _baselineLoadFactor;

    public LastSrfScaledEvaluator(
        double baselineSrf,
        IEnumerable<MaterialModel> baselineMaterials,
        double baselineKh = 0,
        double baselineLoadFactor = 1)
    {
        _baselineSrf = baselineSrf;
        _baselineStrength = FemMaterialAccess.TotalReferenceStrength(baselineMaterials);
        _baselineKh = baselineKh;
        _baselineLoadFactor = Math.Max(1e-9, baselineLoadFactor);
    }

    public double Evaluate(FemEvaluateRequest request)
    {
        if (!(_baselineSrf > 0) || double.IsNaN(_baselineSrf))
            return double.NaN;

        var strength = FemMaterialAccess.TotalReferenceStrength(request.Materials);
        var load = Math.Max(1e-9, request.LoadFactor);
        var seismicDenom = 1.0 + Math.Abs(request.SeismicKh);
        var seismicBase = 1.0 + Math.Abs(_baselineKh);
        return _baselineSrf
            * (strength / _baselineStrength)
            * (_baselineLoadFactor / load)
            * (seismicBase / seismicDenom);
    }
}
