using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class Q4SrfRoutingAndDeckTests
{
    private const string MinimalQ4ExplicitDeck = """
        ! <solver:p64>
        ! <BEGIN:AnalysisOptions>
        ! failure_criterion MohrCoulomb
        ! material_type Isotropic
        ! mesh_type Q4
        ! mesh_source explicit
        ! solver_executable slope_stability_Q4_SRF
        ! srf_mode multi
        ! <END:AnalysisOptions>
        2  4  1
        20.0  15.0  0.0  20.0  100000  0.3
        0.0  0.0
        1.0  0.0
        1.0  1.0
        0.0  1.0
        1  2  3  4
        1  4  3  2
        4
        1  1  1
        2  0  1
        3  0  0
        4  1  0
        1e-4  50
        1
        1.0
        """;

    [Test]
    public void FormatSolverErrorLabel_Q4_ReturnsProcessOnly()
    {
        var label = CppAnalysisRunner.FormatSolverErrorLabel(
            "slope_stability_Q4_SRF",
            "slope_stability_Q4_SRF");
        Assert.That(label, Is.EqualTo("slope_stability_Q4_SRF"));
    }

    [Test]
    public void FormatSolverErrorLabel_SameName_ReturnsProcessOnly()
    {
        var label = CppAnalysisRunner.FormatSolverErrorLabel(
            "slope_stability_T3_SRF",
            "slope_stability_T3_SRF");
        Assert.That(label, Is.EqualTo("slope_stability_T3_SRF"));
    }

    [Test]
    public void Validate_MinimalQ4ExplicitDeck_IsValid()
    {
        var result = SrfExplicitDatValidator.Validate(MinimalQ4ExplicitDeck);
        Assert.That(result.IsValid, Is.True, string.Join("; ", result.Issues));
    }

    [Test]
    public void Validate_Q4DeckWithEightNodeRow_IsInvalid()
    {
        var bad = MinimalQ4ExplicitDeck.Replace("1  2  3  4", "1  2  3  4  5  6  7  8");
        var result = SrfExplicitDatValidator.Validate(bad);
        Assert.That(result.IsValid, Is.False);
        Assert.That(string.Join(" ", result.Issues), Does.Contain("requires 4"));
    }

    [Test]
    public void PrepareDatForNativeSlopeSrfExecutable_Q4ProcessOnQ4Deck_PreservesMeshTypeQ4()
    {
        var prepared = P610AnalysisOptionsHelper.PrepareDatForNativeSlopeSrfExecutable(
            MinimalQ4ExplicitDeck,
            "slope_stability_Q4_SRF");

        Assert.That(prepared.ToUpperInvariant(), Does.Contain("MESH_TYPE Q4"));
        Assert.That(prepared.ToUpperInvariant(), Does.Not.Contain("MESH_TYPE Q8"));
    }

    [Test]
    public void Validate_MeshTypeQ8WithQ4Connectivity_IsInvalid()
    {
        var bad = MinimalQ4ExplicitDeck.Replace("mesh_type Q4", "mesh_type Q8");
        var result = SrfExplicitDatValidator.Validate(bad);
        Assert.That(result.IsValid, Is.False);
        Assert.That(string.Join(" ", result.Issues), Does.Contain("requires 8"));
    }

    [Test]
    public void Q4Route_UsesCoreBBrandedProcess()
    {
        var route = SrfLiteratureOptimalRouting.Resolve("Q4", FailureCriterion.MohrCoulomb);
        Assert.That(route.Executable, Is.EqualTo("slope_stability_Q4_SRF"));
        Assert.That(route.ProcessExecutable, Is.EqualTo("slope_stability_Q4_SRF"));
        Assert.That(route.Core, Is.EqualTo(SrfLiteratureOptimalRouting.SolverCore.CoreB));
    }
}
