using System.Windows;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services.Geometry;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Services;

[TestFixture]
public class FeJointConstraintCollectorTests
{
    [Test]
    public void TwoPointJoint_BecomesAtLeastOneConstraintSegment()
    {
        var feature = new GeologicalFeature
        {
            Id = "j1",
            Kind = GeologicalFeatureKind.WeakPlane,
            Name = "Joint",
            IsEnabled = true
        };
        feature.Vertices.Add((0.1, 0.5));
        feature.Vertices.Add((0.9, 0.5));

        var square = new[] { new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1) };
        var segs = FeJointConstraintCollector.Collect(new[] { feature }, new[] { square }, 0.25);

        Assert.That(segs, Has.Count.GreaterThanOrEqualTo(1));
        foreach (var (a, b) in segs)
        {
            Assert.That(a.Y, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(b.Y, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(a.X, Is.GreaterThanOrEqualTo(0.1 - 1e-9));
            Assert.That(b.X, Is.LessThanOrEqualTo(0.9 + 1e-9));
        }
    }

    [Test]
    public void DisabledFeature_YieldsNoSegments()
    {
        var feature = new GeologicalFeature
        {
            Id = "j1",
            Kind = GeologicalFeatureKind.WeakPlane,
            IsEnabled = false
        };
        feature.Vertices.Add((0, 0));
        feature.Vertices.Add((1, 0));
        var square = new[] { new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1) };
        var segs = FeJointConstraintCollector.Collect(new[] { feature }, new[] { square }, 0.5);
        Assert.That(segs, Is.Empty);
    }
}
