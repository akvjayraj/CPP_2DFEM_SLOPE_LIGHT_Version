using System;
using System.Collections.Generic;
using System.Windows;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Expands a <see cref="TunnelShapeParams"/> DTO into a closed counter-clockwise polygon in axis space.
/// All four standard tunnel families share a common contract:
/// <list type="bullet">
///   <item>Polygon centroid sits at <see cref="TunnelShapeParams.CenterX"/>/<see cref="TunnelShapeParams.CenterY"/>.</item>
///   <item>Output is CCW in standard math convention (Y up). The mesher and the in-polygon test both assume CCW.</item>
///   <item>The first vertex is repeated at the end to make the polygon explicitly closed.</item>
/// </list>
/// </summary>
public static class TunnelShapeBuilder
{
    /// <summary>Builds the tunnel polygon for the given parameters. Throws for unknown shape kinds.</summary>
    public static List<Point> BuildPolygon(TunnelShapeParams p)
    {
        if (p == null) throw new ArgumentNullException(nameof(p));

        int n = Math.Max(8, p.CurveSegments);
        List<Point> pts = p.Kind switch
        {
            TunnelShapeKind.Circle      => BuildCircle(p, n),
            TunnelShapeKind.Rectangular => BuildRectangle(p),
            TunnelShapeKind.Horseshoe   => BuildHorseshoe(p, n),
            TunnelShapeKind.NatmArch    => BuildNatmArch(p, n),
            TunnelShapeKind.Polyline    => throw new NotSupportedException("Polyline shapes are imported through PolylineImporter (Phase 2)."),
            _ => throw new ArgumentOutOfRangeException(nameof(p), p.Kind, "Unknown tunnel shape kind."),
        };

        OffsetByCenter(pts, p.CenterX, p.CenterY);
        ClosePolygon(pts);
        return pts;
    }

    // ── Family-specific builders (all centred at origin; OffsetByCenter shifts at the end) ───

    private static List<Point> BuildCircle(TunnelShapeParams p, int n)
    {
        var pts = new List<Point>(n + 1);
        double r = Math.Max(p.Radius, 1e-6);
        SampleArc(pts, 0.0, 0.0, r, startAng: 0.0, sweep: 2.0 * Math.PI, n);
        return pts;
    }

    private static List<Point> BuildRectangle(TunnelShapeParams p)
    {
        double w = Math.Max(p.Width, 1e-6) * 0.5;
        double h = Math.Max(p.Height, 1e-6) * 0.5;
        return new List<Point>
        {
            new(-w, -h),
            new(+w, -h),
            new(+w, +h),
            new(-w, +h),
        };
    }

    private static List<Point> BuildHorseshoe(TunnelShapeParams p, int n)
    {
        double W = Math.Max(p.HorseshoeSpan, 1e-6);
        double H = Math.Max(p.HorseshoeHeight, W * 0.55); // crown radius is W/2 → need at least that much height
        double crownR = W * 0.5;
        double wallH = Math.Max(0.0, H - crownR);

        // Bbox is [-W/2, +W/2] × [-H/2, +H/2]; springline at +H/2 - crownR.
        double xL = -W * 0.5, xR = +W * 0.5;
        double yBot = -H * 0.5;
        double ySL = yBot + wallH;       // springline (top of walls)

        var pts = new List<Point>();
        // CCW (Y up): bottom-right → up right wall → crown arc CCW → down left wall → close
        pts.Add(new Point(xR, yBot));
        pts.Add(new Point(xR, ySL));
        // Crown arc: centre at (0, ySL), radius crownR, sweep from angle 0 (right tangent) to π (left tangent)
        int crownN = Math.Max(8, n / 2);
        SampleArc(pts, 0.0, ySL, crownR, startAng: 0.0, sweep: Math.PI, crownN, skipFirst: true);
        pts.Add(new Point(xL, yBot));
        return pts;
    }

    private static List<Point> BuildNatmArch(TunnelShapeParams p, int n)
    {
        double W = Math.Max(p.NatmSpan, 1e-6);
        double H = Math.Max(p.NatmHeight, W * 0.4);
        double halfW = 0.5 * W;

        // Crown radius must reach the chord W (the springline), invert radius likewise.
        double rCrown  = Math.Max(p.NatmCrownRadius,  halfW * 1.01);
        double rInvert = Math.Max(p.NatmInvertRadius, halfW * 1.01);

        double hCrown  = rCrown  - Math.Sqrt(rCrown  * rCrown  - halfW * halfW); // sagitta of crown
        double hInvert = rInvert - Math.Sqrt(rInvert * rInvert - halfW * halfW); // sagitta of invert
        double wallH   = Math.Max(0.0, H - hCrown - hInvert);

        double yTop = +H * 0.5;
        double yBot = -H * 0.5;
        double ySLtop = yTop - hCrown;        // springline at top of walls
        double ySLbot = yBot + hInvert;       // springline at bottom of walls (top of invert arc)

        // Crown circle centre directly below crown apex by rCrown.
        double crownCY = yTop - rCrown;
        // Right tangent angle (from crownCY): θ_R = atan2(ySLtop - crownCY, +halfW)
        double thR = Math.Atan2(ySLtop - crownCY, +halfW);
        double thL = Math.PI - thR;
        double crownSweep = thL - thR; // CCW sweep across the top

        // Invert circle centre directly above invert apex by rInvert.
        double invertCY = yBot + rInvert;
        // Left tangent angle (from invertCY): atan2(ySLbot - invertCY, -halfW); negative below the centre.
        double thLi = Math.Atan2(ySLbot - invertCY, -halfW);   // ≈ −π + small
        double thRi = Math.Atan2(ySLbot - invertCY, +halfW);   // ≈ −small
        double invertSweep = thRi - thLi;                      // CCW (increasing angle) gives the lower arc

        var pts = new List<Point>();

        int crownN  = Math.Max(8, n / 2);
        int invertN = Math.Max(6, n / 3);

        // CCW polygon starting at bottom-right of invert tangent, going up right wall, across crown, down left wall, across invert.
        pts.Add(new Point(+halfW, ySLbot));
        if (wallH > 0) pts.Add(new Point(+halfW, ySLtop));
        SampleArc(pts, 0.0, crownCY, rCrown, startAng: thR, sweep: crownSweep, crownN, skipFirst: true);
        if (wallH > 0) pts.Add(new Point(-halfW, ySLbot));
        SampleArc(pts, 0.0, invertCY, rInvert, startAng: thLi, sweep: invertSweep, invertN, skipFirst: true);
        return pts;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void SampleArc(List<Point> output, double cx, double cy, double r,
                                  double startAng, double sweep, int segments,
                                  bool skipFirst = false)
    {
        int n = Math.Max(2, segments);
        int start = skipFirst ? 1 : 0;
        for (int i = start; i <= n; i++)
        {
            double t = (double)i / n;
            double a = startAng + t * sweep;
            output.Add(new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
        }
    }

    private static void OffsetByCenter(List<Point> pts, double cx, double cy)
    {
        for (int i = 0; i < pts.Count; i++)
            pts[i] = new Point(pts[i].X + cx, pts[i].Y + cy);
    }

    private static void ClosePolygon(List<Point> pts)
    {
        if (pts.Count < 3) return;
        var first = pts[0];
        var last = pts[^1];
        if (Math.Abs(first.X - last.X) > 1e-9 || Math.Abs(first.Y - last.Y) > 1e-9)
            pts.Add(first);
    }

    /// <summary>Suggests an outer-domain rectangle around a tunnel polygon (≥ 5× max diameter).</summary>
    public static (double XMin, double XMax, double YMin, double YMax) SuggestOuterDomain(IReadOnlyList<Point> tunnelPolygon, double margin = 5.0)
    {
        if (tunnelPolygon == null || tunnelPolygon.Count == 0)
            return (-50, 50, -50, 50);

        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var p in tunnelPolygon)
        {
            if (p.X < xMin) xMin = p.X;
            if (p.X > xMax) xMax = p.X;
            if (p.Y < yMin) yMin = p.Y;
            if (p.Y > yMax) yMax = p.Y;
        }
        double w = xMax - xMin;
        double h = yMax - yMin;
        double pad = Math.Max(w, h) * Math.Max(1.0, margin);
        return (xMin - pad, xMax + pad, yMin - pad, yMax + pad);
    }
}
