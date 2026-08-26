using System.Windows;
using GeotechSlopeApp.Backend;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Backend;

[TestFixture]
public class MeshElementQualityMetricsTests
{
    [Test]
    public void TryTriangleMetrics_Equilateral_MinAngleIsSixtyDegrees()
    {
        var nodes = new[]
        {
            new Point(0, 0),
            new Point(1, 0),
            new Point(0.5, System.Math.Sqrt(3) / 2.0),
        };

        bool ok = MeshElementQualityMetrics.TryTriangleMetrics(nodes, 0, 1, 2, out var minAngle, out var aspect);

        Assert.That(ok, Is.True);
        Assert.That(minAngle, Is.EqualTo(60.0).Within(1e-6));
        // Equilateral aspect ratio = longestEdge / (2 * inradius) = 1 / (2 * (sqrt(3)/6)) = sqrt(3).
        Assert.That(aspect, Is.EqualTo(System.Math.Sqrt(3)).Within(1e-6));
    }

    [Test]
    public void TryTriangleMetrics_CollinearPoints_ReturnsFalse()
    {
        var nodes = new[] { new Point(0, 0), new Point(1, 0), new Point(2, 0) };

        bool ok = MeshElementQualityMetrics.TryTriangleMetrics(nodes, 0, 1, 2, out _, out _);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void TryTriangleMetrics_IndexOutOfRange_ReturnsFalse()
    {
        var nodes = new[] { new Point(0, 0), new Point(1, 0) };

        bool ok = MeshElementQualityMetrics.TryTriangleMetrics(nodes, 0, 1, 2, out _, out _);

        Assert.That(ok, Is.False);
    }

    [Test]
    public void ComputeWholeMesh_EmptyMesh_ReturnsZeroedStats()
    {
        var stats = MeshElementQualityMetrics.ComputeWholeMesh(
            System.Array.Empty<Point>(),
            System.Array.Empty<Rs2dQuadConverter.QuadResult>(),
            System.Array.Empty<Rs2dQuadConverter.UnmatchedTri>());

        Assert.That(stats.TotalElements, Is.EqualTo(0));
        Assert.That(stats.MinAngleDeg, Is.EqualTo(0.0));
    }

    [Test]
    public void ComputeWholeMesh_UnitSquareQuad_ReportsRightAngles()
    {
        var nodes = new[]
        {
            new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1),
        };
        var quads = new[] { new Rs2dQuadConverter.QuadResult { Q1 = 0, Q2 = 1, Q3 = 2, Q4 = 3 } };

        var stats = MeshElementQualityMetrics.ComputeWholeMesh(
            nodes, quads, System.Array.Empty<Rs2dQuadConverter.UnmatchedTri>());

        Assert.That(stats.QuadCount, Is.EqualTo(1));
        Assert.That(stats.TriangleCount, Is.EqualTo(0));
        Assert.That(stats.MinAngleDeg, Is.EqualTo(90.0).Within(1e-6));
    }
}
