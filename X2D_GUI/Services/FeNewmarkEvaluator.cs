using System;
using System.Collections.Generic;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Services;

public static class FeNewmarkEvaluator
{
    /// <summary>
    /// Evaluates selected empirical models (and optional time history) at k_y.
    /// When <paramref name="srf"/> is known and EstimateKyFromSrf is on,
    /// k_y ≈ max(0, (SRF − 1) / SRF) — a first-order sliding-block estimate.
    /// </summary>
    public static FeNewmarkResult Evaluate(FeNewmarkSettings settings, double? srf)
    {
        var warnings = new List<string>();
        var ky = settings.YieldAcceleration;
        var kySource = "user k_y";
        if (settings.EstimateKyFromSrf && srf is > 0)
        {
            ky = Math.Max(0.0, (srf.Value - 1.0) / srf.Value);
            kySource = $"estimated from SRF = {srf.Value:0.###} as (SRF−1)/SRF";
            if (srf.Value <= 1.0)
                warnings.Add("Static SRF ≤ 1: k_y = 0 and displacement is unbounded.");
        }

        var models = new List<FeNewmarkModelResult>();
        if (settings.UseJibson)
            models.Add(FeNewmarkEmpiricalModels.Jibson2007(ky, settings.PgaG));
        if (settings.UseJibsonMagnitude)
            models.Add(FeNewmarkEmpiricalModels.Jibson2007Magnitude(ky, settings.PgaG, settings.Magnitude));
        if (settings.UseJibsonArias)
            models.Add(FeNewmarkEmpiricalModels.Jibson2007Arias(ky, settings.PgaG, settings.AriasIntensity));
        if (settings.UseAmbraseys)
            models.Add(FeNewmarkEmpiricalModels.AmbraseysMenzies1988(ky, settings.PgaG));
        if (settings.UseMakdisiSeed)
            models.Add(FeNewmarkEmpiricalModels.MakdisiSeed1978(ky, settings.PgaG, settings.Magnitude, settings.FundamentalPeriodT0));
        if (settings.UseBrayTravasarou)
            models.Add(FeNewmarkEmpiricalModels.BrayTravasarou2007(
                ky, settings.SpectralAccelerationG, settings.SlidingMassPeriodTs, settings.Magnitude, settings.PgaG));

        var thCm = double.NaN;
        var thCount = 0;
        if (settings.UseTimeHistory && !string.IsNullOrWhiteSpace(settings.TimeHistoryPath))
        {
            try
            {
                var motion = FeNewmarkTimeHistory.Load(
                    settings.TimeHistoryPath, settings.TimeHistoryUnits, settings.TimeHistoryScale);
                var integrated = FeNewmarkTimeHistory.Integrate(motion, ky, settings.TwoWaySliding);
                thCm = integrated.DisplacementCm;
                thCount = integrated.ExceedanceCount;
            }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
            }
        }

        return new FeNewmarkResult
        {
            YieldAcceleration = ky,
            StaticSrf = srf ?? double.NaN,
            KySource = kySource,
            ModelResults = models,
            TimeHistoryDisplacementCm = thCm,
            TimeHistoryExceedanceCount = thCount,
            Warnings = warnings
        };
    }
}
