using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core;

[TestFixture]
public class SrfInsituModeExportTests
{
    [Test]
    public void TryPatchSrfInsituMode_WritesK0Token()
    {
        const string dat = """
            ! <BEGIN:AnalysisOptions>
            ! failure_criterion MohrCoulomb
            ! material_type Isotropic
            ! mesh_type T6
            ! srf_mode single
            ! srf_F 1.0
            ! <END:AnalysisOptions>
            """;

        Assert.That(P610AnalysisOptionsHelper.TryPatchSrfInsituMode(dat, "k0", out var patched, out var changed), Is.True);
        Assert.That(changed, Is.True);
        Assert.That(patched, Does.Contain("! srf_insitu_mode k0"));
        Assert.That(P610AnalysisOptionsHelper.TryParse(patched, out var opts), Is.True);
        Assert.That(opts.SrfInsituMode, Is.EqualTo("k0"));
    }

    [Test]
    public void ResolveEffective_ExplicitK0AndGravity()
    {
        var fe = new FEAnalysisViewModel(new ResultsViewModel());
        fe.SelectedSrfInsituMode = SrfInsituMode.K0;
        Assert.That(fe.ResolveEffectiveSrfInsituModeToken(), Is.EqualTo("k0"));
        fe.SelectedSrfInsituMode = SrfInsituMode.Gravity;
        Assert.That(fe.ResolveEffectiveSrfInsituModeToken(), Is.EqualTo("gravity"));
        fe.SelectedSrfInsituMode = SrfInsituMode.Auto;
        Assert.That(fe.ResolveEffectiveSrfInsituModeToken(), Is.EqualTo("gravity"));
    }

    [Test]
    public void BuildBlock_EmitsSrfInsituMode()
    {
        var opts = new P610AnalysisOptions
        {
            FailureCriterion = FailureCriterion.MohrCoulomb,
            MaterialType = MaterialType.Isotropic,
            MeshType = "T6",
            SrfMode = "single",
            SrfF = 1.0,
            SrfInsituMode = "k0",
        };
        var block = P610AnalysisOptionsHelper.BuildBlock(opts);
        Assert.That(block, Does.Contain("! srf_insitu_mode k0"));
    }
}
