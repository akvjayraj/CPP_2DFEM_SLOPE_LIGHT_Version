using System;
using GeotechSlopeApp.Services;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Services;

[TestFixture]
public class FeNewmarkEmpiricalModelsTests
{
    [Test]
    public void Jibson2007_MatchesPublishedEquation6()
    {
        const double ky = 0.1;
        const double amax = 0.3;
        var ratio = ky / amax;
        var expectedLog10 = 0.215 + Math.Log10(Math.Pow(1.0 - ratio, 2.341) * Math.Pow(ratio, -1.438));
        var expectedCm = Math.Pow(10.0, expectedLog10);

        var result = FeNewmarkEmpiricalModels.Jibson2007(ky, amax);

        Assert.That(result.HasDisplacement, Is.True);
        Assert.That(result.DisplacementCm, Is.EqualTo(expectedCm).Within(1e-9));
        Assert.That(result.ModelName, Does.Contain("Jibson"));
    }

    [Test]
    public void Jibson2007_ReturnsNoDisplacement_WhenKyAtLeastPga()
    {
        var result = FeNewmarkEmpiricalModels.Jibson2007(0.4, 0.3);
        Assert.That(result.HasDisplacement, Is.True);
        Assert.That(result.DisplacementCm, Is.EqualTo(0.0).Within(1e-12));
    }

    [Test]
    public void Evaluator_EstimatesKyFromSrf()
    {
        var settings = new GeotechSlopeApp.Models.FeNewmarkSettings
        {
            IsEnabled = true,
            EstimateKyFromSrf = true,
            UseJibson = true,
            UseAmbraseys = false,
            PgaG = 0.3
        };

        var result = FeNewmarkEvaluator.Evaluate(settings, srf: 1.25);

        Assert.That(result.YieldAcceleration, Is.EqualTo(0.2).Within(1e-12));
        Assert.That(result.KySource, Does.Contain("SRF"));
        Assert.That(result.ModelResults, Has.Count.EqualTo(1));
    }
}
