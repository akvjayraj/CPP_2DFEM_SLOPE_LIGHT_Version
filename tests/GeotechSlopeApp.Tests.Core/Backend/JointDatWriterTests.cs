using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models.Geometry;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class JointDatWriterTests
{
    [Test]
    public void BuildBlock_IncludesEndsAndInstallStage()
    {
        var feature = new GeologicalFeature
        {
            Id = "j1",
            Kind = GeologicalFeatureKind.WeakPlane,
            Name = "Joint A",
            PropertySetId = 1,
            EndCondition = FeJointEndCondition.FirstClosedLastOpen,
            InstallStage = 2,
            KnKpa = 1e5,
            KsKpa = 1e4
        };
        feature.Vertices.Add((0, 10));
        feature.Vertices.Add((8, 6));

        var set = FeJointPropertySet.CreateDefault(1);
        set.UseResidualStrength = true;
        set.ResidualCohesionKpa = 5;
        set.UseAdditionalPressure = true;
        set.AdditionalPressureKpa = 12;
        set.UseStagedProperties = true;
        set.AllowSlipFromStage = 3;

        var block = JointDatWriter.BuildBlock(new[] { feature }, new[] { set });

        Assert.That(block, Does.Contain("!   ends first_closed_last_open"));
        Assert.That(block, Does.Contain("!   install_stage 2"));
        Assert.That(block, Does.Contain("!   property 1"));
        Assert.That(block, Does.Contain("!   residual_cohesion 5"));
        Assert.That(block, Does.Contain("!   gw_pore true"));
        Assert.That(block, Does.Contain("!   additional_u 12"));
        Assert.That(block, Does.Contain("!   slip_stage 3"));
        Assert.That(block, Does.Contain("! ninterface 0"));
        Assert.That(block, Does.Contain("! interfaces"));
    }

    [Test]
    public void BuildBlock_WritesNumericInterfacesTable()
    {
        var feature = new GeologicalFeature
        {
            Id = "j1",
            Kind = GeologicalFeatureKind.WeakPlane,
            Name = "Joint A"
        };
        feature.Vertices.Add((0, 0));
        feature.Vertices.Add((2, 0));

        var iface = new FeJointInterfaceElement
        {
            N1 = 4,
            N2 = 5,
            N3 = 12,
            N4 = 11,
            Kn = 2e5,
            Ks = 3e4,
            Cohesion = 1,
            FrictionDeg = 25,
            Tension = 0,
            ResidualCohesion = 0.5,
            ResidualFrictionDeg = 20,
            InstallStage = 1,
            Ends = "both_open"
        };

        var block = JointDatWriter.BuildBlock(new[] { feature }, null, new[] { iface });
        Assert.That(block, Does.Contain("! ninterface 1"));
        Assert.That(block, Does.Contain("!   4 5 12 11"));
        Assert.That(block, Does.Contain("200000"));
        Assert.That(block, Does.Contain("30000"));
        Assert.That(block, Does.Contain("both_open"));
    }
}
