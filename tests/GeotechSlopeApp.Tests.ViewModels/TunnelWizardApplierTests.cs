using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Tests.ViewModels.Support;
using GeotechSlopeApp.ViewModels;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.ViewModels;

/// <summary>Full TunnelWizardApplier pipeline (circle + horseshoe presets) from the smoke harness.</summary>
[TestFixture]
public class TunnelWizardApplierTests
{
    [SetUp]
    public void SetUp() => RecordingDialogService.Install();

    [Test]
    public void Apply_CirclePreset_RegistersCircleMeshStagesAndStress()
    {
        var (gd, mesh, bcs, fs, staged) = HeadlessVms.Build();
        var applier = new TunnelWizardApplier(gd, mesh, bcs, fs, staged);

        var wizard = new TunnelWizardViewModel
        {
            ShapeKind = TunnelShapeKind.Circle,
            Radius = 4.0,
            CenterX = 0,
            CenterY = 0,
            StressPreset = TunnelStressPreset.Hydrostatic,
            StressMagnitude = 1000.0,
            MeshPreset = TunnelMeshPreset.Coarse,
        };
        wizard.AutoSizeDomain(margin: 5.0);

        var report = applier.Apply(wizard);

        Assert.That(report.Success, Is.True, string.Join("; ", report.Failures));
        Assert.That(gd.FilteredTunnelExcavationCircleRows.Count, Is.EqualTo(1));
        Assert.That(gd.FilteredTunnelExcavationPolygons.Count, Is.EqualTo(0));
        Assert.That(mesh.MeshInformationRows.Count, Is.GreaterThan(10));
        Assert.That(bcs.BoundaryConditions.Count, Is.GreaterThan(4));
        Assert.That(staged.Stages.Count, Is.EqualTo(2));
        Assert.That(staged.Stages[1].ExcavationZones.Count, Is.GreaterThan(0));
        Assert.That((int)fs.Global.Mode, Is.EqualTo((int)FieldStressMode.Constant));
        Assert.That(fs.Global.Sigma1, Is.EqualTo(-1000.0).Within(1e-6));
    }

    [Test]
    public void Apply_HorseshoePreset_UsesPolygonPathAndK0Stress()
    {
        var (gd, mesh, bcs, fs, staged) = HeadlessVms.Build();
        var applier = new TunnelWizardApplier(gd, mesh, bcs, fs, staged);

        var wizard = new TunnelWizardViewModel
        {
            ShapeKind = TunnelShapeKind.Horseshoe,
            HorseshoeSpan = 10.0,
            HorseshoeHeight = 8.0,
            StressPreset = TunnelStressPreset.K0Lateral,
            StressGamma = 25.0,
            StressDepth = 100.0,
            StressK = 0.5,
            MeshPreset = TunnelMeshPreset.Coarse,
        };
        wizard.AutoSizeDomain(margin: 5.0);

        var report = applier.Apply(wizard);

        Assert.That(report.Success, Is.True, string.Join("; ", report.Failures));
        Assert.That(gd.FilteredTunnelExcavationCircleRows.Count, Is.EqualTo(0));
        Assert.That(gd.FilteredTunnelExcavationPolygons.Count, Is.EqualTo(1));
        Assert.That(mesh.MeshInformationRows.Count, Is.GreaterThan(10));
        Assert.That(bcs.BoundaryConditions.Count, Is.GreaterThan(4));
        Assert.That(staged.Stages.Count, Is.EqualTo(2));
        Assert.That((int)fs.Global.Mode, Is.EqualTo((int)FieldStressMode.Constant));
        Assert.That(fs.Global.Sigma1, Is.EqualTo(-2500.0).Within(1e-3));
        Assert.That(fs.Global.Sigma3, Is.EqualTo(-1250.0).Within(1e-3));
    }
}
