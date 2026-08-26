using System;
using System.Collections.Generic;
using System.Linq;
using GeotechSlopeApp.Backend;
using NUnit.Framework;

namespace GeotechSlopeApp.Tests.Core.Mesh;

/// <summary>NUnit port of the StructuredMeshClipSmokeTest console harness (scripts/StructuredMeshClipSmokeTest).</summary>
[TestFixture]
public class StructuredMeshClipTests
{
    [Test]
    public void AxisAlignedRectangle_NeedsNoBoundaryFill()
    {
        var rect = new List<(double x, double y)> { (0, 0), (10, 0), (10, 6), (0, 6) };
        var stats = StructuredRectangularQ8MeshBuilder.BuildWithStats(
            0, 10, 0, 6, 5, 3,
            new StructuredRectangularQ8MeshBuilder.BuildOptions { BoundaryPolygon = rect });

        Assert.That(StructuredQuadPolygonClipper.ValidateMeshInsidePolygon(stats.Mesh, rect, out var error),
            Is.True, error);
        Assert.That(stats.BoundaryFillElementCount, Is.EqualTo(0));
        Assert.That(stats.Mesh.Nels, Is.EqualTo(15));
    }

    [Test]
    public void SteppedDiagonalSlope_UsesBoundaryFittedQ8Columns()
    {
        var stepped = new List<(double x, double y)> { (0, 0), (10, 0), (10, 3), (7, 6), (0, 6) };
        var stats = StructuredRectangularQ8MeshBuilder.BuildWithStats(
            0, 10, 0, 6, 8, 4,
            new StructuredRectangularQ8MeshBuilder.BuildOptions { BoundaryPolygon = stepped });

        Assert.That(StructuredQuadPolygonClipper.ValidateMeshInsidePolygon(stats.Mesh, stepped, out var error),
            Is.True, error);
        Assert.That(stats.UsedBoundaryFittedStrategy, Is.True,
            "stepped diagonal slope should use the new boundary-fitted structured Q8 strategy");
        Assert.That(stats.BoundaryFillElementCount, Is.EqualTo(0),
            "boundary-fitted slope strategy should not need local boundary-fill clip elements");
        Assert.That(CountUncoveredInteriorPoints(stats.Mesh, stepped), Is.EqualTo(0),
            "interior polygon should have no uncovered sample points");
        Assert.That(IsPointCovered(8.5, 4.0, stats.Mesh), Is.True,
            "sample point near the slope should be covered");
        Assert.That(HasNode(stats.Mesh, 8.75, 4.25), Is.True,
            "top midside/corner nodes should lie on the actual sloped boundary profile");
    }

    // ── Helpers ported from the console harness ──────────────────────────────

    private static bool IsPointCovered(double px, double py, P64EmbankmentMeshBuilder.MeshResult mesh)
    {
        var nodeMap = mesh.Nodes.ToDictionary(n => n.id, n => (n.x, n.y));
        foreach (var (_, conn, _) in mesh.Elements)
        {
            if (conn.Length < 8) continue;
            var q8Outline = new List<(double x, double y)>
            {
                nodeMap[conn[0]], nodeMap[conn[4]], nodeMap[conn[1]], nodeMap[conn[5]],
                nodeMap[conn[2]], nodeMap[conn[6]], nodeMap[conn[3]], nodeMap[conn[7]],
            };
            if (PointInQuad(px, py, q8Outline))
                return true;
        }
        return false;
    }

    private static int CountUncoveredInteriorPoints(
        P64EmbankmentMeshBuilder.MeshResult mesh,
        IReadOnlyList<(double x, double y)> polygon,
        int samplesPerAxis = 40,
        double boundaryInset = 0.05)
    {
        double minX = polygon.Min(p => p.x), maxX = polygon.Max(p => p.x);
        double minY = polygon.Min(p => p.y), maxY = polygon.Max(p => p.y);
        double dx = (maxX - minX) / samplesPerAxis;
        double dy = (maxY - minY) / samplesPerAxis;
        double x0 = minX + 0.5 * dx, y0 = minY + 0.5 * dy;
        int uncovered = 0;

        for (int ix = 0; ix < samplesPerAxis; ix++)
        for (int iy = 0; iy < samplesPerAxis; iy++)
        {
            double px = x0 + ix * dx, py = y0 + iy * dy;
            if (!PolygonElementMatcher.PointInPolygon(px, py, polygon)) continue;
            if (IsNearPolygonBoundary(px, py, polygon, boundaryInset)) continue;
            if (!IsPointCovered(px, py, mesh)) uncovered++;
        }
        return uncovered;
    }

    private static bool IsNearPolygonBoundary(
        double px, double py, IReadOnlyList<(double x, double y)> polygon, double margin)
    {
        int n = polygon.Count;
        for (int i = 0; i < n; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % n];
            double dx = b.x - a.x, dy = b.y - a.y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-18) continue;
            double t = Math.Clamp(((px - a.x) * dx + (py - a.y) * dy) / lenSq, 0, 1);
            double cx = a.x + t * dx, cy = a.y + t * dy;
            if ((px - cx) * (px - cx) + (py - cy) * (py - cy) <= margin * margin)
                return true;
        }
        return false;
    }

    private static bool PointInQuad(double px, double py, IReadOnlyList<(double x, double y)> corners)
    {
        if (corners.Count < 3) return false;
        var unique = new List<(double x, double y)>();
        foreach (var c in corners)
            if (unique.Count == 0 || Math.Abs(unique[^1].x - c.x) > 1e-9 || Math.Abs(unique[^1].y - c.y) > 1e-9)
                unique.Add(c);
        if (unique.Count >= 2 &&
            Math.Abs(unique[0].x - unique[^1].x) < 1e-9 && Math.Abs(unique[0].y - unique[^1].y) < 1e-9)
            unique.RemoveAt(unique.Count - 1);

        if (unique.Count == 3)
            return PointInTriangle(px, py, unique[0], unique[1], unique[2]);

        int n = unique.Count;
        if (n < 3) return false;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = unique[i].x, yi = unique[i].y;
            double xj = unique[j].x, yj = unique[j].y;
            if (Math.Abs(yj - yi) < 1e-12) continue;
            if ((yi > py) != (yj > py))
            {
                double xInt = (xj - xi) * (py - yi) / (yj - yi) + xi;
                if (px < xInt) inside = !inside;
            }
        }
        return inside;
    }

    private static bool HasNode(P64EmbankmentMeshBuilder.MeshResult mesh, double x, double y, double tol = 1e-8)
    {
        foreach (var (_, nx, ny) in mesh.Nodes)
        {
            if (Math.Abs(nx - x) <= tol && Math.Abs(ny - y) <= tol)
                return true;
        }
        return false;
    }

    private static bool PointInTriangle(
        double px, double py, (double x, double y) a, (double x, double y) b, (double x, double y) c)
    {
        static double Cross((double x, double y) p, (double x, double y) q, (double x, double y) r)
            => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
        double s1 = Cross(a, b, (px, py));
        double s2 = Cross(b, c, (px, py));
        double s3 = Cross(c, a, (px, py));
        bool hasNeg = s1 < -1e-12 || s2 < -1e-12 || s3 < -1e-12;
        bool hasPos = s1 > 1e-12 || s2 > 1e-12 || s3 > 1e-12;
        return !(hasNeg && hasPos);
    }
}
