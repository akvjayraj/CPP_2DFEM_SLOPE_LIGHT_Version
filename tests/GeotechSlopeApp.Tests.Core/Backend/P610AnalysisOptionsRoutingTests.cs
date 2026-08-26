using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class P610AnalysisOptionsRoutingTests
{
    [TestCase("T3", "slope_stability_T3_SRF")]
    [TestCase("T6", "slope_stability_T6_SRF")]
    [TestCase("Q4", "slope_stability_Q4_SRF")]
    [TestCase("Q8", "slope_stability_Q8_SRF")]
    public void ResolveRunnerExecutableForFeAnalysis_GuiSrfDeck_UsesLiteratureOptimalProcessExe(string meshType, string expectedExe)
    {
        var options = new P610AnalysisOptions
        {
            FailureCriterion = FailureCriterion.MohrCoulomb,
            MaterialType = MaterialType.Isotropic,
            MeshType = meshType,
            MeshSource = "explicit",
            SrfMode = "multi",
            PreferredExecutable = "SRF",
        };

        var program = P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(
            options,
            meshVm: null,
            srfAnalysisMode: true);

        Assert.That(program, Is.EqualTo(expectedExe),
            "Run FE Analysis SRF should route through literature-optimal branded slope_stability process, not p64.");
    }

    [Test]
    public void ResolveRunnerExecutableForFeAnalysis_GuiSrfDeck_NeverReturnsP64()
    {
        foreach (var meshType in new[] { "T3", "T6", "Q4", "Q8" })
        {
            var options = new P610AnalysisOptions
            {
                FailureCriterion = FailureCriterion.MohrCoulomb,
                MaterialType = MaterialType.Isotropic,
                MeshType = meshType,
                SrfMode = "multi",
                PreferredExecutable = "SRF",
            };

            var program = P610AnalysisOptionsHelper.ResolveRunnerExecutableForFeAnalysis(
                options,
                meshVm: null,
                srfAnalysisMode: true);

            Assert.That(program, Does.Not.EqualTo("p64"));
            Assert.That(P610AnalysisOptionsHelper.IsSlopeStabilitySrfExecutable(program), Is.True);
        }
    }

    [TestCase("T3", "slope_stability_T3_SRF")]
    [TestCase("T6", "slope_stability_T6_SRF")]
    [TestCase("Q4", "slope_stability_Q4_SRF")]
    [TestCase("Q8", "slope_stability_Q8_SRF")]
    public void BrandedExecutable_AlwaysMatchesMeshToken(string meshType, string expectedBrandedExe)
    {
        var route = SrfLiteratureOptimalRouting.Resolve(meshType, FailureCriterion.MohrCoulomb);
        Assert.That(route.Executable, Is.EqualTo(expectedBrandedExe));
    }
}
