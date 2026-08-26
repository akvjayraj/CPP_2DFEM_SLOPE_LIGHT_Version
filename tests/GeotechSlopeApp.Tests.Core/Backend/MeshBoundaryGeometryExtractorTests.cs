using System.Collections.Generic;
using GeotechSlopeApp.Backend;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

/// <summary>Collinear-merge portion of the MeshBcImportSmokeTest harness.</summary>
[TestFixture]
public class MeshBoundaryGeometryExtractorTests
{
    [Test]
    public void TryMergeCollinearBoundaryVertices_RectangleWithMidsides_CollapsesToFourCorners()
    {
        var coords = new (double x, double y)[]
        {
            (0, 0), (5, 0), (10, 0), (10, 2.5), (10, 5), (5, 5), (0, 5), (0, 2.5),
        };
        var boundary = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };

        bool ok = MeshBoundaryGeometryExtractor.TryMergeCollinearBoundaryVertices(
            boundary, coords, out var merged);

        Assert.That(ok, Is.True);
        Assert.That(merged, Has.Count.EqualTo(4));
        Assert.That(merged, Is.EqualTo(new[] { 1, 3, 5, 7 }));
    }
}
