using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class SrfLiteratureOptimalRoutingTests
{
    [TestCase("T3", SrfLiteratureOptimalRouting.SolverCore.CoreA, SrfLiteratureOptimalRouting.DeckFormat.P610, "slope_stability_T3_SRF", "slope_stability_T3_SRF")]
    [TestCase("T6", SrfLiteratureOptimalRouting.SolverCore.CoreA, SrfLiteratureOptimalRouting.DeckFormat.P610, "slope_stability_T6_SRF", "slope_stability_T6_SRF")]
    [TestCase("Q4", SrfLiteratureOptimalRouting.SolverCore.CoreB, SrfLiteratureOptimalRouting.DeckFormat.P64Explicit, "slope_stability_Q4_SRF", "slope_stability_Q4_SRF")]
    [TestCase("Q8", SrfLiteratureOptimalRouting.SolverCore.CoreB, SrfLiteratureOptimalRouting.DeckFormat.P64Explicit, "slope_stability_Q8_SRF", "slope_stability_Q8_SRF")]
    public void Resolve_ReturnsLiteratureOptimalRoute(
        string meshType,
        SrfLiteratureOptimalRouting.SolverCore expectedCore,
        SrfLiteratureOptimalRouting.DeckFormat expectedDeck,
        string expectedBrandedExe,
        string expectedProcessExe)
    {
        var route = SrfLiteratureOptimalRouting.Resolve(meshType, FailureCriterion.MohrCoulomb);

        Assert.That(route.Core, Is.EqualTo(expectedCore));
        Assert.That(route.Deck, Is.EqualTo(expectedDeck));
        Assert.That(route.Executable, Is.EqualTo(expectedBrandedExe));
        Assert.That(route.ProcessExecutable, Is.EqualTo(expectedProcessExe));
    }
}
