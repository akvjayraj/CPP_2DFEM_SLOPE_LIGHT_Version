using GeotechSlopeApp.Models;
using GeotechSlopeApp.Services;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Services;

[TestFixture]
public class FeRapidDrawdownPlannerTests
{
    private static FeDrawdownSettings ActiveSettings(FeDrawdownMethod method)
    {
        var settings = new FeDrawdownSettings
        {
            IsEnabled = true,
            Method = method
        };
        settings.PreDrawdownSurface.Add(new FeDrawdownWaterPoint { X = 0, Y = 20 });
        settings.PreDrawdownSurface.Add(new FeDrawdownWaterPoint { X = 40, Y = 20 });
        settings.PostDrawdownSurface.Add(new FeDrawdownWaterPoint { X = 0, Y = 8 });
        settings.PostDrawdownSurface.Add(new FeDrawdownWaterPoint { X = 40, Y = 8 });
        return settings;
    }

    [Test]
    public void BuildStages_DuncanThreeStage_ReturnsThreePlans()
    {
        var stages = FeRapidDrawdownPlanner.BuildStages(
            ActiveSettings(FeDrawdownMethod.DuncanThreeStage),
            new GroundwaterModel { Method = GroundwaterMethod.Bbar, Bbar = 0.8 });

        Assert.That(stages, Has.Count.EqualTo(3));
        Assert.That(stages[1].Groundwater.Method, Is.EqualTo(GroundwaterMethod.Bbar));
        Assert.That(stages[1].Groundwater.Bbar, Is.EqualTo(0.8).Within(1e-12));
        Assert.That(stages[2].Groundwater.SurfPoints[0].Y, Is.EqualTo(8).Within(1e-12));
    }

    [Test]
    public void BuildStages_CorpsTwoStage_ReturnsTwoPlans()
    {
        var stages = FeRapidDrawdownPlanner.BuildStages(
            ActiveSettings(FeDrawdownMethod.CorpsTwoStage),
            new GroundwaterModel());

        Assert.That(stages, Has.Count.EqualTo(2));
    }

    [Test]
    public void BuildStages_InstantaneousDrained_ReturnsTwoDrainedPlans()
    {
        var stages = FeRapidDrawdownPlanner.BuildStages(
            ActiveSettings(FeDrawdownMethod.InstantaneousDrained),
            new GroundwaterModel());

        Assert.That(stages, Has.Count.EqualTo(2));
        Assert.That(stages[1].Name, Does.Contain("drained"));
    }

    [Test]
    public void BuildStages_Inactive_ReturnsEmpty()
    {
        var stages = FeRapidDrawdownPlanner.BuildStages(
            new FeDrawdownSettings(),
            new GroundwaterModel());

        Assert.That(stages, Is.Empty);
    }
}
