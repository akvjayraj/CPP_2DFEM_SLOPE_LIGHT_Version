using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Services.Geometry
{
/// <summary>
/// Topology checks for the soil boundary and the planar geometry kernel
/// (closed faces, dangling internals, overlapping materials, tunnels, geological features).
/// </summary>
public static class GeometryVerifier
{
    public sealed class Issue
    {
        public string Severity { get; init; } = "Warning";
        public string Code { get; init; } = "";
        public string Message { get; init; } = "";
    }

    public static List<Issue> Verify(GeometryDefinitionViewModel? geometry)
    {
        var issues = new List<Issue>();
        if (geometry == null)
        {
            issues.Add(new Issue { Severity = "Error", Code = "GEOM", Message = "No geometry view model." });
            return issues;
        }

        VerifyOuterBoundary(geometry, issues);

        PlanarGeometryKernel? kernel = null;
        try
        {
            kernel = PlanarGeometryKernelBuilder.Build(geometry);
        }
        catch (Exception ex)
        {
            issues.Add(new Issue
            {
                Severity = "Warning",
                Code = "KERNEL",
                Message = $"Could not rebuild planar kernel: {ex.Message}"
            });
        }

        if (kernel != null)
            VerifyKernel(kernel, geometry, issues);

        if (issues.Count == 0 || issues.All(i => i.Severity == "Info"))
        {
            if (!issues.Any(i => i.Code == "OK"))
            {
                issues.Add(new Issue
                {
                    Severity = "Info",
                    Code = "OK",
                    Message = "Geometry verification passed."
                });
            }
        }

        return issues;
    }

    private static void VerifyOuterBoundary(GeometryDefinitionViewModel geometry, List<Issue> issues)
    {
        if (!geometry.TryGetClosedBoundaryAxisPolygon(out var poly) || poly.Count < 3)
        {
            issues.Add(new Issue
            {
                Severity = "Error",
                Code = "CLOSED",
                Message = "Soil boundary is missing or not a closed polygon (need ≥3 vertices)."
            });
            return;
        }

        var ring = new List<Point>(poly);
        if (ring.Count >= 2
            && Math.Abs(ring[0].X - ring[^1].X) < 1e-9
            && Math.Abs(ring[0].Y - ring[^1].Y) < 1e-9)
        {
            ring.RemoveAt(ring.Count - 1);
        }

        if (ring.Count < 3)
        {
            issues.Add(new Issue
            {
                Severity = "Error",
                Code = "CLOSED",
                Message = "Closed boundary degenerates to fewer than 3 unique vertices."
            });
            return;
        }

        var n = ring.Count;
        var crossings = 0;
        for (var i = 0; i < n; i++)
        {
            var a1 = ring[i];
            var a2 = ring[(i + 1) % n];
            for (var j = i + 1; j < n; j++)
            {
                if (j == i || j == (i + 1) % n || i == (j + 1) % n)
                    continue;
                if (i == 0 && j == n - 1)
                    continue;

                var b1 = ring[j];
                var b2 = ring[(j + 1) % n];
                if (SegmentsProperlyIntersect(a1, a2, b1, b2))
                    crossings++;
            }
        }

        if (crossings > 0)
        {
            issues.Add(new Issue
            {
                Severity = "Error",
                Code = "SELF_INTERSECT",
                Message = $"Boundary has {crossings} non-adjacent self-intersection(s)."
            });
        }

        var area = Math.Abs(SignedArea(ring));
        if (area < 1e-6)
        {
            issues.Add(new Issue
            {
                Severity = "Error",
                Code = "AREA",
                Message = "Boundary polygon has near-zero area."
            });
        }
        else if (!issues.Any(i => i.Severity == "Error"))
        {
            issues.Add(new Issue
            {
                Severity = "Info",
                Code = "OK",
                Message = $"Closed boundary OK: {ring.Count} vertices, area ≈ {area:G4}."
            });
        }
    }

    private static void VerifyKernel(
        PlanarGeometryKernel kernel,
        GeometryDefinitionViewModel geometry,
        List<Issue> issues)
    {
        foreach (var face in kernel.Faces.Where(f => !f.IsExcavationVoid))
        {
            if (face.VertexStableIds.Count < 3)
            {
                issues.Add(new Issue
                {
                    Severity = "Warning",
                    Code = "FACE_OPEN",
                    Message = $"Face '{face.Name}' (#{face.Id}) has fewer than 3 vertices."
                });
            }
        }

        var endpointCount = new Dictionary<int, int>();
        foreach (var edge in kernel.Edges)
        {
            endpointCount[edge.StartStableId] = endpointCount.GetValueOrDefault(edge.StartStableId) + 1;
            endpointCount[edge.EndStableId] = endpointCount.GetValueOrDefault(edge.EndStableId) + 1;
        }

        foreach (var edge in kernel.Edges.Where(e =>
                     e.Role is GeometryEdgeRole.MaterialContact or GeometryEdgeRole.Boundary))
        {
            var startDeg = endpointCount.GetValueOrDefault(edge.StartStableId);
            var endDeg = endpointCount.GetValueOrDefault(edge.EndStableId);
            if (startDeg == 1 || endDeg == 1)
            {
                issues.Add(new Issue
                {
                    Severity = "Warning",
                    Code = "DANGLING",
                    Message = $"Edge #{edge.Id} ({edge.Role}) has a dangling endpoint."
                });
            }
        }

        var materialFaces = kernel.SoilFaces
            .Where(f => f.MaterialId != null && f.VertexStableIds.Count >= 3)
            .ToList();
        for (var i = 0; i < materialFaces.Count; i++)
        {
            for (var j = i + 1; j < materialFaces.Count; j++)
            {
                if (materialFaces[i].MaterialId == materialFaces[j].MaterialId)
                    continue;
                if (!TryFaceRing(kernel, materialFaces[i], out var a)
                    || !TryFaceRing(kernel, materialFaces[j], out var b))
                    continue;
                if (RingsOverlap(a, b))
                {
                    issues.Add(new Issue
                    {
                        Severity = "Warning",
                        Code = "OVERLAP",
                        Message =
                            $"Faces '{materialFaces[i].Name}' and '{materialFaces[j].Name}' appear to overlap with different materials."
                    });
                }
            }
        }

        var layerCuts = geometry.GetNonHorizontalLayerCutPolylines();
        if (layerCuts.Count > 0)
        {
            issues.Add(new Issue
            {
                Severity = "Info",
                Code = "LAYER_CUTS",
                Message = $"{layerCuts.Count} non-horizontal layer cut(s) present."
            });
        }

        foreach (var feature in kernel.GeologicalFeatures)
        {
            if (feature.Vertices.Count < 2)
            {
                issues.Add(new Issue
                {
                    Severity = "Warning",
                    Code = "GEO_FEATURE",
                    Message = $"{feature.Kind} '{feature.Name}' needs at least 2 vertices."
                });
            }
        }

        if (geometry.TunnelExcavationCircles.Count > 0 || geometry.FilteredTunnelExcavationPolygons.Count > 0)
        {
            if (geometry.TryGetClosedBoundaryAxisPolygon(out var soil) && soil.Count >= 3)
            {
                foreach (var tunnel in geometry.TunnelExcavationCircles)
                {
                    if (!PointInPolygon(tunnel.Center, soil))
                    {
                        issues.Add(new Issue
                        {
                            Severity = "Warning",
                            Code = "TUNNEL",
                            Message = $"Tunnel center ({tunnel.Center.X:G4}, {tunnel.Center.Y:G4}) is outside the soil boundary."
                        });
                    }
                }
            }
        }
    }

    private static bool TryFaceRing(PlanarGeometryKernel kernel, PlanarMapFace face, out List<Point> ring)
    {
        ring = new List<Point>();
        foreach (var id in face.VertexStableIds)
        {
            var v = kernel.FindVertex(id);
            if (v == null) return false;
            ring.Add(new Point(v.X, v.Y));
        }

        return ring.Count >= 3;
    }

    private static bool RingsOverlap(IReadOnlyList<Point> a, IReadOnlyList<Point> b)
    {
        // Cheap test: centroid of A inside B or centroid of B inside A.
        var ca = Centroid(a);
        var cb = Centroid(b);
        return PointInPolygon(ca, b) || PointInPolygon(cb, a);
    }

    private static Point Centroid(IReadOnlyList<Point> ring)
    {
        double x = 0, y = 0;
        foreach (var p in ring)
        {
            x += p.X;
            y += p.Y;
        }

        return new Point(x / ring.Count, y / ring.Count);
    }

    private static bool PointInPolygon(Point p, IReadOnlyList<Point> ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var pi = ring[i];
            var pj = ring[j];
            var intersect = ((pi.Y > p.Y) != (pj.Y > p.Y))
                            && (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y + 1e-30) + pi.X);
            if (intersect) inside = !inside;
        }

        return inside;
    }

    public static string Format(IReadOnlyList<Issue> issues)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Geometry verification: {issues.Count} message(s)");
        foreach (var i in issues)
            sb.AppendLine($"  [{i.Severity}] {i.Code}: {i.Message}");
        return sb.ToString();
    }

    public static bool HasErrors(IReadOnlyList<Issue> issues)
    {
        foreach (var i in issues)
        {
            if (string.Equals(i.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static double SignedArea(IReadOnlyList<Point> ring)
    {
        double sum = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return 0.5 * sum;
    }

    private static bool SegmentsProperlyIntersect(Point a1, Point a2, Point b1, Point b2)
    {
        var d1 = Cross(a2 - a1, b1 - a1);
        var d2 = Cross(a2 - a1, b2 - a1);
        var d3 = Cross(b2 - b1, a1 - b1);
        var d4 = Cross(b2 - b1, a2 - b1);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;
        return false;
    }

    private static double Cross(Vector u, Vector v) => u.X * v.Y - u.Y * v.X;
}
}

namespace GeotechSlopeApp.Services.Lem
{
/// <summary>Compatibility alias so existing callers keep compiling after the rename to <see cref="Geometry.GeometryVerifier"/>.</summary>
public static class LemGeometryVerifier
{
    public static List<Geometry.GeometryVerifier.Issue> Verify(GeometryDefinitionViewModel? geometry)
        => Geometry.GeometryVerifier.Verify(geometry);

    public static string Format(IReadOnlyList<Geometry.GeometryVerifier.Issue> issues)
        => Geometry.GeometryVerifier.Format(issues);

    public static bool HasErrors(IReadOnlyList<Geometry.GeometryVerifier.Issue> issues)
        => Geometry.GeometryVerifier.HasErrors(issues);
}
}
