using System;
using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>Acceptance thresholds for hybrid mesh quality gates. Defaults follow common geotech FE
/// practice (Plaxis/RS2-style): warn on very small angles, high aspect ratios, or inverted elements.</summary>
public sealed class HybridMeshQualityGates
{
    /// <summary>Minimum acceptable interior angle (degrees).</summary>
    public double MinAngleDeg { get; init; } = 15.0;

    /// <summary>Maximum acceptable element aspect ratio.</summary>
    public double MaxAspectRatio { get; init; } = 10.0;

    /// <summary>Minimum acceptable scaled Jacobian (corner sine). &lt;= 0 means inverted/degenerate.</summary>
    public double MinScaledJacobian { get; init; } = 0.10;

    public static HybridMeshQualityGates Default { get; } = new();
}

/// <summary>
/// Mesh quality metrics for a hybrid mesh, plus pass/fail against <see cref="HybridMeshQualityGates"/>.
/// Metrics are unified across element kinds: the scaled Jacobian of an element is the minimum, over its
/// corners, of the sine of the corner angle (1.0 = ideal square/equilateral; &lt;= 0 = inverted). This
/// is the quality-gate stage of the hybrid pipeline (Phase 2), letting the driver report (and callers
/// react to) sliver/inverted elements the same way leading geotech meshers surface mesh-quality warnings.
/// </summary>
public sealed class HybridMeshQuality
{
    public double MinAngleDeg { get; init; }
    public double MaxAspectRatio { get; init; }
    public double MinScaledJacobian { get; init; }

    public int ElementsBelowMinAngle { get; init; }
    public int ElementsAboveMaxAspect { get; init; }
    public int InvertedElements { get; init; }
    public int EvaluatedElements { get; init; }

    public bool GatesPassed { get; init; } = true;

    public static HybridMeshQuality Empty { get; } = new() { MinAngleDeg = 0, MaxAspectRatio = 0, MinScaledJacobian = 0 };

    public string Summary() =>
        $"min angle {MinAngleDeg:F1}\u00B0, max AR {MaxAspectRatio:F2}, min scaled-J {MinScaledJacobian:F3}" +
        (GatesPassed ? " (gates OK)"
                     : $" (gate fails: {ElementsBelowMinAngle} small-angle, {ElementsAboveMaxAspect} high-AR, {InvertedElements} inverted)");

    /// <summary>Evaluate quality over every element in every region of a hybrid mesh.</summary>
    public static HybridMeshQuality Evaluate(
        IReadOnlyList<Point> nodes,
        IEnumerable<HybridRegionMesh> regions,
        HybridMeshQualityGates? gates = null)
    {
        gates ??= HybridMeshQualityGates.Default;

        if (nodes == null || nodes.Count == 0 || regions == null)
            return Empty;

        double minAngle = 180.0;
        double maxAspect = 1.0;
        double minScaledJ = 1.0;
        int belowAngle = 0, aboveAspect = 0, inverted = 0, count = 0;

        void Accumulate(Point[] corners)
        {
            ElementMetrics(corners, out double elemMinAngle, out double elemAspect, out double elemScaledJ);
            if (elemMinAngle < minAngle) minAngle = elemMinAngle;
            if (elemAspect > maxAspect) maxAspect = elemAspect;
            if (elemScaledJ < minScaledJ) minScaledJ = elemScaledJ;

            if (elemMinAngle < gates.MinAngleDeg) belowAngle++;
            if (elemAspect > gates.MaxAspectRatio) aboveAspect++;
            if (elemScaledJ <= 0.0) inverted++;
            count++;
        }

        foreach (var region in regions)
        {
            foreach (var (a, b, c) in region.Triangles)
                Accumulate(new[] { nodes[a], nodes[b], nodes[c] });
            foreach (var (a, b, c, d) in region.Quads)
                Accumulate(new[] { nodes[a], nodes[b], nodes[c], nodes[d] });
        }

        if (count == 0)
            return Empty;

        bool pass = minAngle >= gates.MinAngleDeg
                    && maxAspect <= gates.MaxAspectRatio
                    && minScaledJ >= gates.MinScaledJacobian
                    && inverted == 0;

        return new HybridMeshQuality
        {
            MinAngleDeg = minAngle,
            MaxAspectRatio = maxAspect,
            MinScaledJacobian = minScaledJ,
            ElementsBelowMinAngle = belowAngle,
            ElementsAboveMaxAspect = aboveAspect,
            InvertedElements = inverted,
            EvaluatedElements = count,
            GatesPassed = pass,
        };
    }

    /// <summary>Per-element metrics for a 3- or 4-corner polygon (corner order around the element).</summary>
    private static void ElementMetrics(Point[] p, out double minAngleDeg, out double aspectRatio, out double scaledJacobian)
    {
        int n = p.Length;
        minAngleDeg = 180.0;
        scaledJacobian = 1.0;
        double minEdge = double.MaxValue, maxEdge = 0;

        for (int i = 0; i < n; i++)
        {
            Point c = p[i];
            Point prev = p[(i - 1 + n) % n];
            Point next = p[(i + 1) % n];

            double ux = prev.X - c.X, uy = prev.Y - c.Y;
            double vx = next.X - c.X, vy = next.Y - c.Y;
            double lu = Math.Sqrt(ux * ux + uy * uy);
            double lv = Math.Sqrt(vx * vx + vy * vy);

            double edge = Math.Sqrt(vx * vx + vy * vy);
            if (edge < minEdge) minEdge = edge;
            if (edge > maxEdge) maxEdge = edge;

            if (lu < 1e-12 || lv < 1e-12)
            {
                minAngleDeg = 0;
                scaledJacobian = 0;
                continue;
            }

            double cos = Math.Clamp((ux * vx + uy * vy) / (lu * lv), -1.0, 1.0);
            double ang = Math.Acos(cos) * 180.0 / Math.PI;
            if (ang < minAngleDeg) minAngleDeg = ang;

            double sin = Math.Abs(ux * vy - uy * vx) / (lu * lv); // |cross|/(|u||v|) = sin(corner angle)
            if (sin < scaledJacobian) scaledJacobian = sin;
        }

        // Aspect ratio: triangle uses longest edge / shortest altitude; quad uses longest / shortest edge.
        if (n == 3)
        {
            double area = TriangleArea(p[0], p[1], p[2]);
            double shortestAltitude = area > 1e-18 ? 2.0 * area / maxEdge : 0.0;
            aspectRatio = shortestAltitude > 1e-12 ? maxEdge / shortestAltitude : double.MaxValue;
            if (area <= 1e-18) scaledJacobian = 0;
        }
        else
        {
            aspectRatio = minEdge > 1e-12 ? maxEdge / minEdge : double.MaxValue;
            if (QuadArea(p) <= 1e-18) scaledJacobian = 0;
        }
    }

    private static double TriangleArea(Point a, Point b, Point c) =>
        Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) * 0.5;

    private static double QuadArea(Point[] p)
    {
        double a2 = 0;
        for (int i = 0; i < p.Length; i++)
        {
            Point p0 = p[i], p1 = p[(i + 1) % p.Length];
            a2 += p0.X * p1.Y - p1.X * p0.Y;
        }
        return Math.Abs(a2) * 0.5;
    }
}
