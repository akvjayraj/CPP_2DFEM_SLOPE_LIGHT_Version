using System.Collections.Generic;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

/// <summary>Pure-logic portion of the TunnelWizardSmokeTest PolygonElementMatcher checks.</summary>
[TestFixture]
public class PolygonElementMatcherTests
{
    private static readonly List<(double X, double Y)> UnitBox10 = new()
    {
        (0, 0), (10, 0), (10, 10), (0, 10),
    };

    [Test]
    public void PointInPolygon_ClassifiesInteriorAndExterior()
    {
        Assert.That(PolygonElementMatcher.PointInPolygon(5, 5, UnitBox10), Is.True);
        Assert.That(PolygonElementMatcher.PointInPolygon(50, 50, UnitBox10), Is.False);
    }

    [Test]
    public void ElementIdsInsidePolygon_ReturnsOnlyElementsWithCentroidInside()
    {
        var rows = new List<MeshInformationRow>
        {
            new() { ElementId = 1, Node1 = 1, Node2 = 2, Node3 = 3,
                    N1X = 1, N1Y = 1, N2X = 2, N2Y = 1, N3X = 1.5, N3Y = 2, Kind = "T3" },
            new() { ElementId = 2, Node1 = 4, Node2 = 5, Node3 = 6,
                    N1X = 20, N1Y = 20, N2X = 21, N2Y = 20, N3X = 20.5, N3Y = 21, Kind = "T3" },
            new() { ElementId = 3, Node1 = 7, Node2 = 8, Node3 = 9,
                    N1X = 8, N1Y = 8, N2X = 9, N2Y = 8, N3X = 8.5, N3Y = 9, Kind = "T3" },
        };

        var inside = PolygonElementMatcher.ElementIdsInsidePolygon(rows, UnitBox10);

        Assert.That(inside, Has.Count.EqualTo(2));
        Assert.That(inside, Does.Contain(1));
        Assert.That(inside, Does.Contain(3));
        Assert.That(inside, Does.Not.Contain(2));
    }
}
