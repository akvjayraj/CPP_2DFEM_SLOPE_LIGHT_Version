using GeotechSlopeApp.Backend;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class P610T3ElasticSolverControlTests
{
    private const string ElasticDeck = """
        ! solver:p610_t3_elastic
        ! p610_t3_elastic — linear-elastic plane-strain T3
        ! <BEGIN:SolverControl>
        ! (single-solve linear-elastic — p610_t3_elastic ignores tol/limit/incs/layers)
        ! <END:SolverControl>
        ! <BEGIN:Layers>
        1
        0  -10  -20  0
        1
        1  2
        ! <END:Layers>
        """;

    private const string ElasticDeckWithInjectedControl = """
        ! solver:p610_t3_elastic
        ! p610_t3_elastic — linear-elastic plane-strain T3
        ! <BEGIN:SolverControl>
        ! (single-solve linear-elastic — p610_t3_elastic ignores tol/limit/incs/layers)
        1E-05  1000  5  1
        ! <END:SolverControl>
        ! <BEGIN:Layers>
        1
        0  -10  -20  0
        1
        1  2
        ! <END:Layers>
        """;

    [Test]
    public void TryPatchSolverControl_ElasticDeck_DoesNotInjectNumericLine()
    {
        var fe = new FEAnalysisViewModel(new ResultsViewModel())
        {
            ConvergenceTolerance = 1e-5,
            MaxIterations = 1000,
            LoadIncrements = 5
        };

        var ok = DatSolverControlExporter.TryPatchSolverControl(
            ElasticDeck, fe, srfAnalysisMode: false, out var patched, out var changed);

        Assert.That(ok, Is.False);
        Assert.That(changed, Is.False);
        Assert.That(patched, Is.EqualTo(ElasticDeck));
        Assert.That(patched, Does.Not.Match(@"1E-0?5|0\.00001"));
    }

    [Test]
    public void SanitizeP610DatForSolver_ElasticDeck_StripsInjectedControlLine()
    {
        var sanitized = DatSolverControlExporter.SanitizeP610DatForSolver(ElasticDeckWithInjectedControl);

        Assert.That(sanitized, Does.Not.Contain("1E-05"));
        Assert.That(sanitized, Does.Not.Contain("1000  5  1"));
        Assert.That(sanitized, Does.Contain("0  -10  -20  0"));
        Assert.That(sanitized, Does.Contain("<BEGIN:SolverControl>"));
        Assert.That(sanitized, Does.Contain("<END:SolverControl>"));
    }

    [Test]
    public void SanitizeP610DatForSolver_CleanElasticDeck_LeavesTractions()
    {
        var sanitized = DatSolverControlExporter.SanitizeP610DatForSolver(ElasticDeck);

        Assert.That(sanitized, Is.EqualTo(ElasticDeck));
    }
}
