using System.Windows;
using GeotechSlopeApp.Models.Support;
using GeotechSlopeApp.Services.Support;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Services;

[TestFixture]
public class FeSupportPatternGeneratorTests
{
    [Test]
    public void Generate_PlacesEquallySpacedBarsAlongHorizontalFace()
    {
        var ground = new[]
        {
            new Point(0, 10),
            new Point(10, 10)
        };
        var request = new FeSupportPatternRequest(
            FeSupportType.SoilNail,
            StartX: 0,
            EndX: 8,
            SpacingM: 2,
            FixedCount: null,
            LengthM: 4,
            InclinationDeg: 0,
            FeSupportSpacingMeasure.AlongFace);

        var result = FeSupportPatternGenerator.Generate(request, ground);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Count, Is.EqualTo(5));
        Assert.That(result.Segments[0].HeadX, Is.EqualTo(0).Within(1e-9));
        Assert.That(result.Segments[0].HeadY, Is.EqualTo(10).Within(1e-9));
        Assert.That(result.Segments[^1].HeadX, Is.EqualTo(8).Within(1e-9));
        Assert.That(result.Segments[0].TailY, Is.EqualTo(10).Within(1e-6));
        Assert.That(result.Segments[0].TailX, Is.EqualTo(4).Within(1e-6));
    }

    [Test]
    public void Generate_RejectsFaceSupportTypes()
    {
        var result = FeSupportPatternGenerator.Generate(
            new FeSupportPatternRequest(
                FeSupportType.ShotcreteFace, 0, 10, 2, null, 4, 15,
                FeSupportSpacingMeasure.AlongFace),
            new[] { new Point(0, 10), new Point(10, 5) });

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Summary, Does.Contain("Face support"));
    }
}
