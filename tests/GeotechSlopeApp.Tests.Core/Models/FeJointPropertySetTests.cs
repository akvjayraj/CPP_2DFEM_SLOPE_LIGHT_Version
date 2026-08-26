using GeotechSlopeApp.Models.Geometry;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Models;

[TestFixture]
public class FeJointPropertySetTests
{
    [Test]
    public void CreateDefault_SeedsStiffnessAndGatesResidual()
    {
        var set = FeJointPropertySet.CreateDefault(1);

        Assert.That(set.Name, Is.EqualTo("Joint 1"));
        Assert.That(set.Criterion, Is.EqualTo(FeJointSlipCriterion.MohrCoulomb));
        Assert.That(set.UseResidualStrength, Is.False);
        Assert.That(set.NormalStiffnessKpaPerM, Is.EqualTo(1e5).Within(1e-6));
        Assert.That(set.ShearStiffnessKpaPerM, Is.EqualTo(1e4).Within(1e-6));
        Assert.That(set.UseGroundwaterPorePressure, Is.True);
        Assert.That(set.AllowSlipFromStage, Is.EqualTo(1));
        Assert.That(set.UseStagedProperties, Is.False);
        Assert.That(set.StageFactors, Is.Empty);
    }

    [Test]
    public void Clone_CopiesResidualAndStageFactorsIndependently()
    {
        var src = FeJointPropertySet.CreateDefault(2);
        src.UseResidualStrength = true;
        src.ResidualCohesionKpa = 12;
        src.ResidualFrictionDeg = 18;
        src.ResidualTensileStrengthKpa = 3;
        src.UseStagedProperties = true;
        src.StageFactors.Add(new FeJointStageFactor { StageNumber = 2, KnFactor = 0.5 });

        var copy = src.Clone(3);

        Assert.That(copy.Id, Is.EqualTo(3));
        Assert.That(copy.Name, Is.EqualTo("Joint 3"));
        Assert.That(copy.UseResidualStrength, Is.True);
        Assert.That(copy.ResidualCohesionKpa, Is.EqualTo(12).Within(1e-9));
        Assert.That(copy.ResidualFrictionDeg, Is.EqualTo(18).Within(1e-9));
        Assert.That(copy.StageFactors, Has.Count.EqualTo(1));
        Assert.That(copy.StageFactors[0].KnFactor, Is.EqualTo(0.5).Within(1e-9));

        copy.ResidualCohesionKpa = 99;
        copy.StageFactors[0].KnFactor = 2;
        Assert.That(src.ResidualCohesionKpa, Is.EqualTo(12).Within(1e-9));
        Assert.That(src.StageFactors[0].KnFactor, Is.EqualTo(0.5).Within(1e-9));
    }
}
