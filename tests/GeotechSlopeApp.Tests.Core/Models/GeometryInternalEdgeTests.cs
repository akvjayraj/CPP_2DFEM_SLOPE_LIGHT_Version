using GeotechSlopeApp.Models;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Models;

[TestFixture]
public class GeometryInternalEdgeTests
{
    [Test]
    public void StraightEdge_NormalizesEndpointsToMinMax()
    {
        var edge = new GeometryInternalEdge(7, 3);

        Assert.That(edge.StableMin, Is.EqualTo(3));
        Assert.That(edge.StableMax, Is.EqualTo(7));
        Assert.That(edge.StableMid, Is.EqualTo(-1));
        Assert.That(edge.IsArc, Is.False);
    }

    [Test]
    public void ArcEdge_StoresThroughPointAndOrderedEndpoints()
    {
        var edge = new GeometryInternalEdge(stableStart: 10, stableMid: 5, stableEnd: 2);

        Assert.That(edge.IsArc, Is.True);
        Assert.That(edge.StableMin, Is.EqualTo(2));
        Assert.That(edge.StableMax, Is.EqualTo(10));
        Assert.That(edge.StableMid, Is.EqualTo(5));
        Assert.That(edge.StableStart, Is.EqualTo(10));
        Assert.That(edge.StableEnd, Is.EqualTo(2));
    }

    [TestCase(3, true)]
    [TestCase(7, true)]
    [TestCase(5, true)]
    [TestCase(99, false)]
    public void TouchesStableId_MatchesEndpointsAndMidpoint(int id, bool expected)
    {
        var edge = new GeometryInternalEdge(stableStart: 7, stableMid: 5, stableEnd: 3);

        Assert.That(edge.TouchesStableId(id), Is.EqualTo(expected));
    }
}
