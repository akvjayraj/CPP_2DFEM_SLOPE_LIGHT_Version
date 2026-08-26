using System;
using System.Collections.Generic;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>Boundary segment recorded during RS2D discretization (line or circular arc).</summary>
public sealed class Rs2dBoundarySegment
{
    public Point A { get; init; }
    public Point B { get; init; }
    public bool IsArc { get; init; }
    public double ArcCenterX { get; init; }
    public double ArcCenterY { get; init; }
    public double ArcRadius { get; init; }
    public double ArcStartAngle { get; init; }
    public double ArcEndAngle { get; init; }

    public static Rs2dBoundarySegment Line(Point a, Point b) => new() { A = a, B = b };

    public static Rs2dBoundarySegment Arc(Point a, Point b, double cx, double cy, double r, double aStart, double aEnd)
        => new()
        {
            A = a, B = b, IsArc = true,
            ArcCenterX = cx, ArcCenterY = cy, ArcRadius = r,
            ArcStartAngle = aStart, ArcEndAngle = aEnd,
        };
}

/// <summary>Boundary-curve helpers for Q8 midside placement and gradation-zone detection.</summary>
public static class Rs2dBoundaryGeometry
{
    private const double Quant = 1e6;
    private const double MatchTol = 1e-5;

    public static (long, long) QuantKey(Point p)
        => ((long)Math.Round(p.X * Quant), (long)Math.Round(p.Y * Quant));

    /// <summary>Midpoint on the discretized boundary curve when edge (a,b) lies on a recorded segment.</summary>
    public static Point? TryMidpointOnBoundary(Point a, Point b, IReadOnlyList<Rs2dBoundarySegment> segments)
    {
        if (segments == null || segments.Count == 0) return null;
        var ka = QuantKey(a);
        var kb = QuantKey(b);

        foreach (var seg in segments)
        {
            var sa = QuantKey(seg.A);
            var sb = QuantKey(seg.B);
            bool forward = sa == ka && sb == kb;
            bool reverse = sa == kb && sb == ka;
            if (!forward && !reverse) continue;

            if (seg.IsArc && seg.ArcRadius > 1e-12)
            {
                double angA = Math.Atan2(a.Y - seg.ArcCenterY, a.X - seg.ArcCenterX);
                double angB = Math.Atan2(b.Y - seg.ArcCenterY, b.X - seg.ArcCenterX);
                double midAng = (angA + angB) * 0.5;
                if (Math.Abs(NormAngle(angB - angA)) > Math.PI)
                    midAng += Math.PI;
                return new Point(
                    seg.ArcCenterX + seg.ArcRadius * Math.Cos(midAng),
                    seg.ArcCenterY + seg.ArcRadius * Math.Sin(midAng));
            }

            return new Point((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
        }

        foreach (var seg in segments)
        {
            if (DistToSegment(a, seg.A, seg.B) > MatchTol || DistToSegment(b, seg.A, seg.B) > MatchTol)
                continue;
            if (seg.IsArc && seg.ArcRadius > 1e-12)
            {
                double angA = Math.Atan2(a.Y - seg.ArcCenterY, a.X - seg.ArcCenterX);
                double angB = Math.Atan2(b.Y - seg.ArcCenterY, b.X - seg.ArcCenterX);
                double midAng = (angA + angB) * 0.5;
                if (Math.Abs(NormAngle(angB - angA)) > Math.PI)
                    midAng += Math.PI;
                return new Point(
                    seg.ArcCenterX + seg.ArcRadius * Math.Cos(midAng),
                    seg.ArcCenterY + seg.ArcRadius * Math.Sin(midAng));
            }
            return new Point((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
        }

        return null;
    }

    public static double MinDistanceToBoundaryPoints(Point p, IReadOnlyList<Point> boundaryPoints)
    {
        if (boundaryPoints == null || boundaryPoints.Count == 0) return 0;
        double best = double.MaxValue;
        foreach (var bp in boundaryPoints)
        {
            double dx = p.X - bp.X, dy = p.Y - bp.Y;
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
        }
        return best;
    }

    /// <summary>Expected edge length from RS2D gradation settings at distance d from boundary.</summary>
    public static double ExpectedSizeAtDistance(double distance, double nearSize, double globalSize, double growthFactor)
    {
        nearSize = Math.Max(nearSize, 1e-12);
        globalSize = Math.Max(globalSize, nearSize);
        growthFactor = Math.Max(growthFactor, 1.01);
        if (distance <= 0) return nearSize;
        double rings = distance / nearSize;
        return Math.Min(globalSize, nearSize * Math.Pow(growthFactor, rings));
    }

    private static double DistToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-24) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        double qx = a.X + t * dx, qy = a.Y + t * dy;
        return Math.Sqrt((p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy));
    }

    private static double NormAngle(double a)
    {
        while (a <= -Math.PI) a += 2 * Math.PI;
        while (a > Math.PI) a -= 2 * Math.PI;
        return a;
    }
}
