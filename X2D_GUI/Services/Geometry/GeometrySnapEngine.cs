using System;
using System.Collections.Generic;
using System.Windows;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Services.Geometry;

/// <summary>What a snapped point latched onto, so the canvas can say so and draw the right marker.</summary>
public enum GeometrySnapKind
{
    None,
    Grid,
    Vertex,
    Midpoint,
    Intersection,
    Edge,
}

public readonly record struct GeometrySnapResult(Point Point, GeometrySnapKind Kind)
{
    public bool IsSnapped => Kind != GeometrySnapKind.None;

    public string Label => Kind switch
    {
        GeometrySnapKind.Grid => "grid",
        GeometrySnapKind.Vertex => "vertex",
        GeometrySnapKind.Midpoint => "midpoint",
        GeometrySnapKind.Intersection => "intersection",
        GeometrySnapKind.Edge => "edge",
        _ => ""
    };
}

/// <summary>
/// Decides where a click really lands. Works entirely in model coordinates and holds no state, so
/// the canvas, the drawing tools and the tests all use the same rules.
/// </summary>
public static class GeometrySnapEngine
{
    /// <summary>
    /// Finds the best snap for <paramref name="point"/>. Object snaps win over the grid, and among
    /// the object snaps the more specific one wins: a vertex beats an intersection, which beats a
    /// midpoint, which beats an arbitrary point along an edge. Within one kind the nearest wins.
    /// </summary>
    public static GeometrySnapResult Snap(
        Point point,
        IReadOnlyList<Point>? vertices,
        IReadOnlyList<(Point A, Point B)>? segments,
        GeometrySnapSettings settings,
        double tolerance)
    {
        if (settings == null || !settings.SnapEnabled || tolerance <= 0)
            return new GeometrySnapResult(point, GeometrySnapKind.None);

        if (settings.VertexSnap && TryNearestPoint(point, vertices, tolerance, out var vertex))
            return new GeometrySnapResult(vertex, GeometrySnapKind.Vertex);

        if (settings.IntersectionSnap
            && TryNearestPoint(point, Intersections(segments, point, tolerance), tolerance, out var crossing))
        {
            return new GeometrySnapResult(crossing, GeometrySnapKind.Intersection);
        }

        if (settings.MidpointSnap && TryNearestPoint(point, Midpoints(segments), tolerance, out var midpoint))
            return new GeometrySnapResult(midpoint, GeometrySnapKind.Midpoint);

        if (settings.EdgeSnap && TryNearestOnSegments(point, segments, tolerance, out var onEdge))
            return new GeometrySnapResult(onEdge, GeometrySnapKind.Edge);

        if (settings.GridSnap && settings.GridSnapSpacing > 0)
        {
            var snapped = SnapToGrid(point, settings.GridSnapSpacing);
            // The grid only takes when the cursor is already close to a node, so a coarse grid does
            // not drag every click across the section.
            if (Distance(snapped, point) <= tolerance)
                return new GeometrySnapResult(snapped, GeometrySnapKind.Grid);
        }

        return new GeometrySnapResult(point, GeometrySnapKind.None);
    }

    public static Point SnapToGrid(Point point, double spacing)
    {
        if (spacing <= 0) return point;
        return new Point(
            Math.Round(point.X / spacing, MidpointRounding.AwayFromZero) * spacing,
            Math.Round(point.Y / spacing, MidpointRounding.AwayFromZero) * spacing);
    }

    /// <summary>
    /// Constrains a point to lie on a ray leaving <paramref name="anchor"/> at a multiple of
    /// <paramref name="angleStepDegrees"/>, keeping the distance the cursor implies along that ray.
    /// </summary>
    public static Point ApplyOrtho(Point anchor, Point point, double angleStepDegrees)
    {
        if (angleStepDegrees <= 0) return point;

        var dx = point.X - anchor.X;
        var dy = point.Y - anchor.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-12) return point;

        var step = angleStepDegrees * Math.PI / 180.0;
        var angle = Math.Round(Math.Atan2(dy, dx) / step, MidpointRounding.AwayFromZero) * step;

        // Project onto the chosen direction rather than rotating, so dragging along the ray keeps
        // the point under the cursor instead of running ahead of it.
        var ux = Math.Cos(angle);
        var uy = Math.Sin(angle);
        var along = dx * ux + dy * uy;
        return new Point(anchor.X + along * ux, anchor.Y + along * uy);
    }

    /// <summary>Segments of a closed ring of vertices, for callers that hold a boundary polygon.</summary>
    public static List<(Point A, Point B)> ClosedRingSegments(IReadOnlyList<Point>? vertices)
    {
        var segments = new List<(Point, Point)>();
        if (vertices == null || vertices.Count < 2) return segments;

        for (var i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            if (Distance(a, b) > 1e-9) segments.Add((a, b));
        }

        return segments;
    }

    public static Point NearestPointOnSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-18) return a;

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        return new Point(a.X + t * dx, a.Y + t * dy);
    }

    private static IEnumerable<Point> Midpoints(IReadOnlyList<(Point A, Point B)>? segments)
    {
        if (segments == null) yield break;
        foreach (var (a, b) in segments)
            yield return new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    }

    /// <summary>
    /// Crossings between segments near the cursor. Only segments that come within the tolerance are
    /// paired up, which keeps this linear in practice even though the crossing test is pairwise.
    /// </summary>
    private static IEnumerable<Point> Intersections(
        IReadOnlyList<(Point A, Point B)>? segments,
        Point near,
        double tolerance)
    {
        if (segments == null || segments.Count < 2) yield break;

        var candidates = new List<(Point A, Point B)>();
        foreach (var segment in segments)
        {
            if (Distance(NearestPointOnSegment(near, segment.A, segment.B), near) <= tolerance * 4)
                candidates.Add(segment);
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                if (TryIntersect(candidates[i], candidates[j], out var crossing))
                    yield return crossing;
            }
        }
    }

    private static bool TryIntersect((Point A, Point B) first, (Point A, Point B) second, out Point crossing)
    {
        crossing = default;

        var r = new Vector(first.B.X - first.A.X, first.B.Y - first.A.Y);
        var s = new Vector(second.B.X - second.A.X, second.B.Y - second.A.Y);
        var denominator = r.X * s.Y - r.Y * s.X;
        if (Math.Abs(denominator) < 1e-15) return false;

        var qp = new Vector(second.A.X - first.A.X, second.A.Y - first.A.Y);
        var t = (qp.X * s.Y - qp.Y * s.X) / denominator;
        var u = (qp.X * r.Y - qp.Y * r.X) / denominator;
        if (t < 0 || t > 1 || u < 0 || u > 1) return false;

        crossing = new Point(first.A.X + t * r.X, first.A.Y + t * r.Y);
        return true;
    }

    private static bool TryNearestPoint(
        Point point,
        IEnumerable<Point>? candidates,
        double tolerance,
        out Point nearest)
    {
        nearest = point;
        if (candidates == null) return false;

        var best = double.MaxValue;
        var found = false;
        foreach (var candidate in candidates)
        {
            var d = Distance(candidate, point);
            if (d > tolerance || d >= best) continue;
            best = d;
            nearest = candidate;
            found = true;
        }

        return found;
    }

    private static bool TryNearestOnSegments(
        Point point,
        IReadOnlyList<(Point A, Point B)>? segments,
        double tolerance,
        out Point nearest)
    {
        nearest = point;
        if (segments == null) return false;

        var best = double.MaxValue;
        var found = false;
        foreach (var (a, b) in segments)
        {
            var candidate = NearestPointOnSegment(point, a, b);
            var d = Distance(candidate, point);
            if (d > tolerance || d >= best) continue;
            best = d;
            nearest = candidate;
            found = true;
        }

        return found;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
