using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.Services.Geometry;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Services;

[TestFixture]
public class FeJointMeshSplitterTests
{
    [Test]
    public void SharedJointEdge_DuplicatesInteriorNode_KeepsClosedEnds_RemapsPlusSide()
    {
        var nodes = new Dictionary<int, Point>
        {
            [1] = new(0, 0),
            [2] = new(1, 0),
            [3] = new(0.5, 1),
            [4] = new(0.5, -1),
            [5] = new(0.5, 0)
        };
        var elements = new List<MeshInformationRow>
        {
            T3(1, 1, 5, 3, 0, 0, 0.5, 1),
            T3(2, 5, 2, 3, 0.5, 0, 1, 0),
            T3(3, 1, 4, 5, 0, 0, 0.5, -1),
            T3(4, 5, 4, 2, 0.5, 0, 0.5, -1)
        };

        var feature = new GeologicalFeature
        {
            Id = "j1",
            Kind = GeologicalFeatureKind.WeakPlane,
            Name = "Joint",
            EndCondition = FeJointEndCondition.BothClosed,
            KnKpa = 1e5,
            KsKpa = 1e4
        };
        feature.Vertices.Add((0, 0));
        feature.Vertices.Add((1, 0));

        var result = FeJointMeshSplitter.Split(elements, nodes, new[] { feature }, null, 1e-6);

        Assert.That(result.AddedNodes, Has.Count.EqualTo(1));
        int dup = result.AddedNodes[0].Id;
        Assert.That(result.AddedNodes[0].Location, Is.EqualTo(new Point(0.5, 0)));

        var plusA = result.Elements.First(e => e.ElementId == 1);
        var plusB = result.Elements.First(e => e.ElementId == 2);
        var minusA = result.Elements.First(e => e.ElementId == 3);
        Assert.That(plusA.Node2, Is.EqualTo(dup));
        Assert.That(plusB.Node1, Is.EqualTo(dup));
        Assert.That(minusA.Node3, Is.EqualTo(5));
        Assert.That(plusA.Node1, Is.EqualTo(1));
        Assert.That(plusB.Node2, Is.EqualTo(2));

        Assert.That(result.Interfaces, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(result.Interfaces.Any(i =>
            (i.N1 == 5 || i.N2 == 5) && (i.N3 == dup || i.N4 == dup)));
        Assert.That(result.Interfaces[0].Kn, Is.EqualTo(1e5).Within(1e-6));
        Assert.That(result.Interfaces[0].Ks, Is.EqualTo(1e4).Within(1e-6));
    }

    private static MeshInformationRow T3(
        int id, int n1, int n2, int n3,
        double x1, double y1, double x2, double y2)
        => new()
        {
            Kind = "T3",
            ElementId = id,
            Node1 = n1,
            Node2 = n2,
            Node3 = n3,
            N1X = x1,
            N1Y = y1,
            N2X = x2,
            N2Y = y2
        };
}
