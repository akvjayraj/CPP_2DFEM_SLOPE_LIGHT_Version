using NUnit.Framework;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.FiniteElement;
using GeotechSlopeApp.Models.Wave3;
using GeotechSlopeApp.Services.Wave3;

namespace GeotechSlopeApp.Tests.Core.Services;

[TestFixture]
public sealed class Wave3DeliveryTests
{
    [Test]
    public void ProbabilisticResults_ComputeMeanSigmaAndBeta()
    {
        var samples = new[] { 1.0, 1.2, 0.8, 1.1, 0.9 };
        var r = FemProbabilisticResults.FromSamples(samples, 5, 0.85, "Monte Carlo");
        Assert.That(r.ValidSampleCount, Is.EqualTo(5));
        Assert.That(r.MeanFs, Is.EqualTo(1.0).Within(1e-12));
        Assert.That(r.ProbabilityOfFailure, Is.EqualTo(0.2).Within(1e-12));
        Assert.That(r.ReliabilityIndex, Is.GreaterThan(0));
    }

    [Test]
    public void LatinHypercube_DrawsRequestedCountInsideBounds()
    {
        var m = Clay();
        m.EnableProbabilisticVariation = true;
        m.CohesionStats.UseAsRv = true;
        m.CohesionStats.Distribution = FemStatDistribution.Normal;
        m.CohesionStats.CovPercent = 20;
        m.CohesionStats.Min = 1;
        m.CohesionStats.Max = 50;
        var rvs = FemRandomSampler.Collect(new[] { m });
        var draws = FemRandomSampler.Draw(rvs, FemProbabilisticEngine.LatinHypercube, 32, seed: 7);
        Assert.That(draws, Has.Count.EqualTo(32));
        Assert.That(draws.All(d => d[0] >= 1 && d[0] <= 50), Is.True);
    }

    [Test]
    public void Sensitivity_PerturbsCohesionBothWays()
    {
        var m = Clay();
        var evaluator = new LastSrfScaledEvaluator(1.4, new[] { m });
        var rows = FemSensitivityRunner.RunOneAtATime(
            new FemSensitivitySettings { MaterialVariationPercent = 20 },
            new[] { m },
            evaluator);
        Assert.That(rows.Count, Is.GreaterThan(1));
        Assert.That(rows[0].Parameter, Is.EqualTo("Baseline"));
        Assert.That(rows[0].FactorOfSafety, Is.EqualTo(1.4).Within(1e-9));
        var cRows = rows.Where(r => r.Parameter.Contains("Cohesion")).ToList();
        Assert.That(cRows, Has.Count.EqualTo(2));
        Assert.That(cRows[0].FactorOfSafety, Is.Not.EqualTo(cRows[1].FactorOfSafety));
    }

    [Test]
    public void BackAnalysis_FindsFactorForTargetFs()
    {
        var m = Clay();
        var evaluator = new LastSrfScaledEvaluator(1.4, new[] { m });
        var result = FemBackAnalysisRunner.Run(
            new FemBackAnalysisSettings { TargetFs = 1.0, FsTolerance = 1e-3 },
            new[] { m },
            evaluator);
        Assert.That(result.Converged, Is.True);
        Assert.That(result.Factor, Is.EqualTo(1.4).Within(0.02));
        Assert.That(result.AchievedFs, Is.EqualTo(1.0).Within(1e-3));
    }

    [Test]
    public void AnalysisCase_SeismicPenaltyLowersFoS()
    {
        var m = Clay();
        var evaluator = new LastSrfScaledEvaluator(1.5, new[] { m });
        var c = new FemAnalysisCase { SeismicKh = 0.15, LoadFactor = 1 };
        var fos = FemAnalysisCaseRunner.EvaluateCase(c, new[] { m }, evaluator);
        Assert.That(fos, Is.LessThan(1.5));
        Assert.That(c.Status, Is.EqualTo("Evaluated"));
    }

    [Test]
    public void ReportMarkdown_IncludesSrfAndMaterials()
    {
        var md = FemReportBuilder.BuildMarkdown(new FemReportSnapshot
        {
            Options = new FemReportOptions { ReportTitle = "Wave 3 check", Company = "Acme" },
            ProjectName = "Demo",
            EstimatedSrf = 1.37,
            Materials = new[] { Clay() },
        });
        Assert.That(md, Does.Contain("Wave 3 check"));
        Assert.That(md, Does.Contain("1.370"));
        Assert.That(md, Does.Contain("Clay"));
    }

    [Test]
    public void CsvAndVtk_WriteMeshAndDisplacements()
    {
        var nodes = new[]
        {
            new Node { Id = 1, X = 0, Y = 0, Displacements = new[] { 0.01, 0.0 } },
            new Node { Id = 2, X = 1, Y = 0, Displacements = new[] { 0.02, 0.0 } },
            new Node { Id = 3, X = 0, Y = 1, Displacements = new[] { 0.0, 0.03 } },
        };
        var elements = new ElementBase[]
        {
            new LinearTriangleElement { Id = 1, NodeIds = new[] { 1, 2, 3 } },
        };
        var stresses = new[] { new ElementStress { ElementId = 1, VonMisesStress = 12, YieldRatio = 0.4 } };

        var csv = FeResultsExportService.BuildNodalCsv(nodes);
        Assert.That(csv, Does.Contain("Ux"));
        Assert.That(csv, Does.Contain("0.01"));

        var vtk = FeResultsExportService.BuildVtk(nodes, elements, stresses);
        Assert.That(vtk, Does.StartWith("# vtk DataFile Version 3.0"));
        Assert.That(vtk, Does.Contain("POINTS 3"));
        Assert.That(vtk, Does.Contain("von_mises"));
    }

    [Test]
    public void AbaqusAndAnsys_EmitNodesAndElements()
    {
        var nodes = new[]
        {
            new Node { Id = 1, X = 0, Y = 0 },
            new Node { Id = 2, X = 1, Y = 0 },
            new Node { Id = 3, X = 0, Y = 1 },
        };
        var elements = new ElementBase[]
        {
            new LinearTriangleElement { Id = 10, NodeIds = new[] { 1, 2, 3 } },
        };
        var inp = FeAbaqusExporter.Build(nodes, elements, new[] { Clay() });
        Assert.That(inp, Does.Contain("*Node"));
        Assert.That(inp, Does.Contain("CPE3"));
        Assert.That(inp, Does.Contain("*Mohr Coulomb"));

        var cdb = FeAnsysExporter.Build(nodes, elements);
        Assert.That(cdb, Does.Contain("/PREP7"));
        Assert.That(cdb, Does.Contain("N,1,"));
        Assert.That(cdb, Does.Contain("EN,10,"));
    }

    private static MaterialModel Clay() => new()
    {
        Id = 1,
        Name = "Clay",
        Cohesion = 20,
        FrictionAngle = 18,
        UnitWeight = 19,
        YoungsModulus = 15000,
    };
}
