using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services.Geometry;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Services;

[TestFixture]
public class FeJointNetworkGeneratorTests
{
    private static readonly (double X, double Y)[] UnitSquare =
    {
        (0, 0), (1, 0), (1, 1), (0, 1)
    };

    private static readonly (double X, double Y)[] RightSquare =
    {
        (2, 0), (3, 0), (3, 1), (2, 1)
    };

    [Test]
    public void ParallelDeterministic_ConstantSpacing_AllSegmentsInsideUnitSquare()
    {
        var def = FeJointNetworkDefinition.CreateDefault("A");
        def.Model = FeJointNetworkModel.ParallelDeterministic;
        def.RandomLocation = false;
        def.RandomSeed = 7;
        def.Set1.DipDeg = 0;
        def.Set1.Spacing.Mean = 0.25;
        def.Set1.InfiniteLength = true;
        def.Set1.FirstJointOffset = 0.125;

        var result = FeJointNetworkGenerator.Generate(def, new[] { UnitSquare });
        Assert.That(result.Polylines, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(result.WasCapped, Is.False);
        foreach (var poly in result.Polylines)
        {
            Assert.That(poly.Vertices, Has.Count.GreaterThanOrEqualTo(2));
            foreach (var v in poly.Vertices)
                AssertInside(v, UnitSquare);
        }
    }

    [Test]
    public void CrossJointed_HasTwoOrientations()
    {
        var def = FeJointNetworkDefinition.CreateDefault("Cross");
        def.Model = FeJointNetworkModel.CrossJointed;
        def.RandomLocation = false;
        def.RandomSeed = 3;
        def.Set1.DipDeg = 0;
        def.Set1.Spacing.Mean = 0.25;
        def.Set2.DipDeg = 90;
        def.Set2.Spacing.Mean = 0.25;

        var result = FeJointNetworkGenerator.Generate(def, new[] { UnitSquare });
        Assert.That(result.Polylines, Has.Count.GreaterThanOrEqualTo(4));
        var dips = result.Polylines
            .Select(DipOf)
            .Select(WrapDip)
            .ToList();
        Assert.That(dips.Any(d => Math.Abs(d) < 15 || Math.Abs(Math.Abs(d) - 180) < 15), Is.True);
        Assert.That(dips.Any(d => Math.Abs(Math.Abs(d) - 90) < 15), Is.True);
    }

    [Test]
    public void Baecher_SameSeed_IsReproducible_AndClipped()
    {
        var def = BaecherDef(seed: 11, intensity: 8);
        var a = FeJointNetworkGenerator.Generate(def, new[] { UnitSquare });
        var b = FeJointNetworkGenerator.Generate(def, new[] { UnitSquare });
        Assert.That(a.Polylines.Count, Is.EqualTo(b.Polylines.Count));
        for (var i = 0; i < a.Polylines.Count; i++)
        {
            Assert.That(a.Polylines[i].Vertices.Count, Is.EqualTo(b.Polylines[i].Vertices.Count));
            for (var j = 0; j < a.Polylines[i].Vertices.Count; j++)
            {
                Assert.That(a.Polylines[i].Vertices[j].X, Is.EqualTo(b.Polylines[i].Vertices[j].X).Within(1e-9));
                Assert.That(a.Polylines[i].Vertices[j].Y, Is.EqualTo(b.Polylines[i].Vertices[j].Y).Within(1e-9));
            }
        }

        Assert.That(a.Polylines, Has.Count.GreaterThan(0));
        foreach (var poly in a.Polylines)
        foreach (var v in poly.Vertices)
            AssertInside(v, UnitSquare);
    }

    [Test]
    public void Veneziano_SameSeed_IsReproducible_AndClipped()
    {
        var def = FeJointNetworkDefinition.CreateDefault("Ven");
        def.Model = FeJointNetworkModel.Veneziano;
        def.RandomSeed = 19;
        def.Intensity = 1.2;
        def.Orientation.Kind = FeJointDistributionKind.Constant;
        def.Orientation.Mean = 30;
        def.Length.Kind = FeJointDistributionKind.Constant;
        def.Length.Mean = 0.4;
        def.Persistence.Kind = FeJointDistributionKind.Constant;
        def.Persistence.Mean = 0.6;

        var a = FeJointNetworkGenerator.Generate(def, new[] { UnitSquare });
        var b = FeJointNetworkGenerator.Generate(def, new[] { UnitSquare });
        Assert.That(a.Polylines.Count, Is.EqualTo(b.Polylines.Count));
        Assert.That(a.Polylines, Has.Count.GreaterThan(0));
        foreach (var poly in a.Polylines)
        foreach (var v in poly.Vertices)
            AssertInside(v, UnitSquare);
    }

    [Test]
    public void Voronoi_ClosedNetwork_AllEdgesClipped()
    {
        var def = FeJointNetworkDefinition.CreateDefault("Voro");
        def.Model = FeJointNetworkModel.Voronoi;
        def.RandomSeed = 5;
        def.VoronoiDensityMethod = FeJointVoronoiDensityMethod.SiteCount;
        def.SiteCount = 12;
        def.VoronoiRegularity = FeJointVoronoiRegularity.Irregular;

        var result = FeJointNetworkGenerator.Generate(def, new[] { UnitSquare });
        Assert.That(result.Polylines, Has.Count.GreaterThanOrEqualTo(6));
        foreach (var poly in result.Polylines)
        {
            Assert.That(poly.Vertices, Has.Count.GreaterThanOrEqualTo(2));
            foreach (var v in poly.Vertices)
                AssertInside(v, UnitSquare);
        }
    }

    [Test]
    public void TwoNetworks_StayInTheirTargetPolygons()
    {
        var a = FeJointNetworkDefinition.CreateDefault("Left");
        a.Model = FeJointNetworkModel.ParallelDeterministic;
        a.RandomLocation = false;
        a.Set1.DipDeg = 90;
        a.Set1.Spacing.Mean = 0.25;
        a.Set1.InfiniteLength = true;

        var b = FeJointNetworkDefinition.CreateDefault("Right");
        b.Model = FeJointNetworkModel.ParallelDeterministic;
        b.RandomLocation = false;
        b.Set1.DipDeg = 0;
        b.Set1.Spacing.Mean = 0.25;
        b.Set1.InfiniteLength = true;

        var left = FeJointNetworkGenerator.Generate(a, new[] { UnitSquare });
        var right = FeJointNetworkGenerator.Generate(b, new[] { RightSquare });
        Assert.That(left.Polylines, Has.Count.GreaterThan(0));
        Assert.That(right.Polylines, Has.Count.GreaterThan(0));
        foreach (var poly in left.Polylines)
        foreach (var v in poly.Vertices)
            AssertInside(v, UnitSquare);
        foreach (var poly in right.Polylines)
        foreach (var v in poly.Vertices)
            AssertInside(v, RightSquare);
    }

    [Test]
    public void ReplaceNetworkA_DoesNotDeleteNetworkB()
    {
        var features = new List<GeologicalFeature>
        {
            Feature("A", "net-a", (0, 0), (1, 0)),
            Feature("B1", "net-b", (2, 0), (3, 0)),
            Feature("B2", "net-b", (2, 1), (3, 1))
        };
        var replacement = new[]
        {
            Feature("A-new", "net-a", (0, 0.5), (1, 0.5))
        };

        FeJointNetworkGenerator.ReplaceNetworkFeatures(features, "net-a", replacement);

        Assert.That(features.Count(f => f.JointNetworkId == "net-b"), Is.EqualTo(2));
        Assert.That(features.Count(f => f.JointNetworkId == "net-a"), Is.EqualTo(1));
        Assert.That(features.Single(f => f.JointNetworkId == "net-a").Name, Is.EqualTo("A-new"));
    }

    private static FeJointNetworkDefinition BaecherDef(int seed, double intensity)
    {
        var def = FeJointNetworkDefinition.CreateDefault("Baecher");
        def.Model = FeJointNetworkModel.Baecher;
        def.RandomSeed = seed;
        def.Intensity = intensity;
        def.Orientation.Kind = FeJointDistributionKind.Constant;
        def.Orientation.Mean = 20;
        def.Length.Kind = FeJointDistributionKind.Constant;
        def.Length.Mean = 0.35;
        return def;
    }

    private static GeologicalFeature Feature(string name, string networkId, (double X, double Y) a, (double X, double Y) b)
    {
        var f = new GeologicalFeature
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            JointNetworkId = networkId,
            JointNetworkName = networkId
        };
        f.Vertices.Add(a);
        f.Vertices.Add(b);
        return f;
    }

    private static void AssertInside((double X, double Y) p, IReadOnlyList<(double X, double Y)> poly)
    {
        Assert.That(FeJointNetworkGenerator.PointInPolygon(p, poly), Is.True,
            $"Vertex ({p.X:0.###},{p.Y:0.###}) left the target region.");
    }

    private static double DipOf(FeJointPolyline poly)
    {
        var a = poly.Vertices[0];
        var b = poly.Vertices[^1];
        return Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
    }

    private static double WrapDip(double deg)
    {
        while (deg > 90) deg -= 180;
        while (deg < -90) deg += 180;
        return deg;
    }
}
