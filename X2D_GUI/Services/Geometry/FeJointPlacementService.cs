using System;
using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Services.Geometry;

public enum FeJointPlacementMode
{
    CrestToe,
    LayerContact,
    TensionCrack,
    ThroughPoint,
    ParallelSet,
    ManualDraw,
    JointNetwork
}

public sealed record FeJointPolyline(string Label, IReadOnlyList<(double X, double Y)> Vertices);

public sealed record FeJointSuggestion(
    string Id,
    string Label,
    FeJointPlacementMode Mode,
    int? LayerIndex,
    string Hint);

public sealed record FeJointPlacementRequest(
    FeJointPlacementMode Mode,
    IReadOnlyList<(double X, double Y)> GroundEnvelope,
    IReadOnlyList<(double X, double Y)>? DomainPolygon,
    IReadOnlyList<double>? LayerCutElevations,
    double AxisXMin,
    double AxisXMax,
    double AxisYMin,
    double AxisYMax,
    double DipAngleDeg,
    bool UseDipOverride,
    double ThroughX,
    double ThroughY,
    double TensionCrackDepth,
    int ParallelCount,
    double ParallelSpacing,
    int? LayerIndex = null);

/// <summary>
/// Computes open joint polylines at engineering-sensible locations from the current slope domain.
/// </summary>
public static class FeJointPlacementService
{
    public const int MaxParallelCount = 20;
    private const double Eps = 1e-9;

    public static IReadOnlyList<(double X, double Y)> BuildGroundEnvelope(
        IEnumerable<(double X, double Y)> vertices)
    {
        if (vertices == null)
            return Array.Empty<(double, double)>();
        return vertices
            .GroupBy(v => Math.Round(v.X, 6))
            .Select(g => (X: g.Key, Y: g.Max(p => p.Y)))
            .OrderBy(p => p.X)
            .ToList();
    }

    public static IReadOnlyList<FeJointSuggestion> Suggest(
        IReadOnlyList<(double X, double Y)> ground,
        IReadOnlyList<double>? layerCuts)
    {
        var list = new List<FeJointSuggestion>();
        if (ground.Count >= 2)
        {
            list.Add(new FeJointSuggestion(
                "crest-toe", "Crest–toe", FeJointPlacementMode.CrestToe, null,
                "Plane from crest to the slope-face toe."));
            list.Add(new FeJointSuggestion(
                "tension", "Tension crack", FeJointPlacementMode.TensionCrack, null,
                "Vertical crack downward from the crest."));
        }

        if (layerCuts != null)
        {
            for (var i = 0; i < layerCuts.Count; i++)
            {
                list.Add(new FeJointSuggestion(
                    $"layer-{i}",
                    $"Layer {i + 1}",
                    FeJointPlacementMode.LayerContact,
                    i,
                    $"Horizontal contact at Y = {layerCuts[i]:0.###}."));
            }
        }

        return list;
    }

    public static bool TryFindCrest(IReadOnlyList<(double X, double Y)> ground, out (double X, double Y) crest)
    {
        crest = default;
        if (ground == null || ground.Count == 0)
            return false;
        var maxY = ground.Max(p => p.Y);
        var bestDrop = -1.0;
        var best = ground[0];
        var found = false;
        for (var i = 0; i < ground.Count; i++)
        {
            if (ground[i].Y < maxY - 1e-6)
                continue;
            var drop = 0.0;
            if (i > 0)
                drop = Math.Max(drop, ground[i].Y - ground[i - 1].Y);
            if (i + 1 < ground.Count)
                drop = Math.Max(drop, ground[i].Y - ground[i + 1].Y);
            if (!found || drop > bestDrop + Eps || (Math.Abs(drop - bestDrop) <= Eps && ground[i].X > best.X))
            {
                best = ground[i];
                bestDrop = drop;
                found = true;
            }
        }

        crest = best;
        return found;
    }

    public static bool TryFindToe(
        IReadOnlyList<(double X, double Y)> ground,
        (double X, double Y) crest,
        out (double X, double Y) toe)
    {
        toe = default;
        if (ground == null || ground.Count < 2)
            return false;
        var ci = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < ground.Count; i++)
        {
            var d = Dist2(ground[i], crest);
            if (d < bestDist)
            {
                bestDist = d;
                ci = i;
            }
        }

        var rightLow = ground.Skip(ci + 1).Any(p => p.Y < crest.Y - 1e-6);
        var leftLow = ground.Take(ci).Any(p => p.Y < crest.Y - 1e-6);
        var dir = rightLow || !leftLow ? 1 : -1;

        var breakPt = crest;
        var foundBreak = false;
        var i0 = ci;
        while (true)
        {
            var i1 = i0 + dir;
            if (i1 < 0 || i1 >= ground.Count)
                break;
            var steep = IsSteep(ground[i0], ground[i1]);
            var i2 = i1 + dir;
            if (steep && i2 >= 0 && i2 < ground.Count && !IsSteep(ground[i1], ground[i2]))
            {
                breakPt = ground[i1];
                foundBreak = true;
                break;
            }

            i0 = i1;
        }

        if (foundBreak)
        {
            toe = breakPt;
            return true;
        }

        var slice = dir > 0 ? ground.Skip(ci) : ground.Take(ci + 1);
        var lowest = slice.OrderBy(p => p.Y).ThenBy(p => Math.Abs(p.X - crest.X)).First();
        if (Dist2(lowest, crest) < Eps)
            return false;
        toe = lowest;
        return true;
    }

    public static IReadOnlyList<FeJointPolyline> Place(FeJointPlacementRequest request)
    {
        if (request == null || request.Mode == FeJointPlacementMode.ManualDraw)
            return Array.Empty<FeJointPolyline>();

        var ground = request.GroundEnvelope ?? Array.Empty<(double, double)>();
        var poly = NormalizePolygon(request.DomainPolygon);
        var box = (request.AxisXMin, request.AxisXMax, request.AxisYMin, request.AxisYMax);

        return request.Mode switch
        {
            FeJointPlacementMode.CrestToe => PlaceCrestToe(request, ground, poly, box),
            FeJointPlacementMode.LayerContact => PlaceLayerContacts(request, ground, poly),
            FeJointPlacementMode.TensionCrack => PlaceTensionCrack(request, ground, poly),
            FeJointPlacementMode.ThroughPoint => PlaceThrough(request, poly, box, request.ThroughX, request.ThroughY),
            FeJointPlacementMode.ParallelSet => PlaceParallel(request, poly, box),
            _ => Array.Empty<FeJointPolyline>()
        };
    }

    public static double ChordDipDeg((double X, double Y) a, (double X, double Y) b)
        => Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;

    public static double DefaultTensionDepth(IReadOnlyList<(double X, double Y)> ground)
    {
        if (ground == null || ground.Count == 0)
            return 1.0;
        var h = ground.Max(p => p.Y) - ground.Min(p => p.Y);
        return Math.Max(0.1, 0.1 * h);
    }

    private static IReadOnlyList<FeJointPolyline> PlaceCrestToe(
        FeJointPlacementRequest request,
        IReadOnlyList<(double X, double Y)> ground,
        IReadOnlyList<(double X, double Y)>? poly,
        (double XMin, double XMax, double YMin, double YMax) box)
    {
        if (!TryFindCrest(ground, out var crest) || !TryFindToe(ground, crest, out var toe))
            return Array.Empty<FeJointPolyline>();

        if (!request.UseDipOverride)
            return new[] { new FeJointPolyline("Crest–toe", new[] { crest, toe }) };

        var mid = ((crest.X + toe.X) * 0.5, (crest.Y + toe.Y) * 0.5);
        var clipped = ClipLineThrough(mid.Item1, mid.Item2, request.DipAngleDeg, poly, box);
        return clipped.Count >= 2
            ? new[] { new FeJointPolyline("Crest–toe (dip)", clipped) }
            : Array.Empty<FeJointPolyline>();
    }

    private static IReadOnlyList<FeJointPolyline> PlaceLayerContacts(
        FeJointPlacementRequest request,
        IReadOnlyList<(double X, double Y)> ground,
        IReadOnlyList<(double X, double Y)>? poly)
    {
        var cuts = request.LayerCutElevations;
        if (cuts == null || cuts.Count == 0)
            return Array.Empty<FeJointPolyline>();

        var result = new List<FeJointPolyline>();
        for (var i = 0; i < cuts.Count; i++)
        {
            if (request.LayerIndex is int only && only != i)
                continue;
            var y = cuts[i];
            var seg = HorizontalAt(y, ground, poly);
            if (seg.Count >= 2)
                result.Add(new FeJointPolyline($"Layer {i + 1}", seg));
        }

        return result;
    }

    private static IReadOnlyList<FeJointPolyline> PlaceTensionCrack(
        FeJointPlacementRequest request,
        IReadOnlyList<(double X, double Y)> ground,
        IReadOnlyList<(double X, double Y)>? poly)
    {
        if (!TryFindCrest(ground, out var crest))
            return Array.Empty<FeJointPolyline>();
        var depth = request.TensionCrackDepth > 0
            ? request.TensionCrackDepth
            : DefaultTensionDepth(ground);
        var dip = request.UseDipOverride ? request.DipAngleDeg : 90.0;
        var rad = dip * Math.PI / 180.0;
        var dx = Math.Cos(rad);
        var dy = Math.Sin(rad);
        if (dy > 0)
        {
            dx = -dx;
            dy = -dy;
        }

        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < Eps)
        {
            dx = 0;
            dy = -1;
            len = 1;
        }

        dx /= len;
        dy /= len;
        var tail = (crest.X + dx * depth, crest.Y + dy * depth);
        if (poly != null && poly.Count >= 3 && !PointInPolygon(tail, poly))
        {
            var hit = FirstHit(crest, tail, poly);
            if (hit != null)
                tail = hit.Value;
        }

        if (Dist2(crest, tail) < Eps)
            return Array.Empty<FeJointPolyline>();
        return new[] { new FeJointPolyline("Tension crack", new[] { crest, tail }) };
    }

    private static IReadOnlyList<FeJointPolyline> PlaceThrough(
        FeJointPlacementRequest request,
        IReadOnlyList<(double X, double Y)>? poly,
        (double XMin, double XMax, double YMin, double YMax) box,
        double x,
        double y)
    {
        var clipped = ClipLineThrough(x, y, request.DipAngleDeg, poly, box);
        return clipped.Count >= 2
            ? new[] { new FeJointPolyline("Through-point", clipped) }
            : Array.Empty<FeJointPolyline>();
    }

    private static IReadOnlyList<FeJointPolyline> PlaceParallel(
        FeJointPlacementRequest request,
        IReadOnlyList<(double X, double Y)>? poly,
        (double XMin, double XMax, double YMin, double YMax) box)
    {
        var count = Math.Clamp(request.ParallelCount, 1, MaxParallelCount);
        var spacing = Math.Max(0.05, request.ParallelSpacing);
        var rad = request.DipAngleDeg * Math.PI / 180.0;
        var nx = -Math.Sin(rad);
        var ny = Math.Cos(rad);
        var result = new List<FeJointPolyline>(count);
        var origin = (count - 1) * 0.5;
        for (var i = 0; i < count; i++)
        {
            var o = (i - origin) * spacing;
            var x = request.ThroughX + nx * o;
            var y = request.ThroughY + ny * o;
            var clipped = ClipLineThrough(x, y, request.DipAngleDeg, poly, box);
            if (clipped.Count >= 2)
                result.Add(new FeJointPolyline($"Joint {i + 1}", clipped));
        }

        return result;
    }

    public static IReadOnlyList<(double X, double Y)> ClipLineThrough(
        double x0,
        double y0,
        double dipDeg,
        IReadOnlyList<(double X, double Y)>? polygon,
        (double XMin, double XMax, double YMin, double YMax) box)
    {
        var rad = dipDeg * Math.PI / 180.0;
        var dx = Math.Cos(rad);
        var dy = Math.Sin(rad);
        if (Math.Abs(dx) < Eps && Math.Abs(dy) < Eps)
            return Array.Empty<(double, double)>();

        if (polygon != null && polygon.Count >= 3)
        {
            var hits = new List<(double T, double X, double Y)>();
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                if (!TryIntersectLineSegment(x0, y0, dx, dy, a, b, out var t, out var ix, out var iy))
                    continue;
                if (hits.All(h => Math.Abs(h.T - t) > 1e-7))
                    hits.Add((t, ix, iy));
            }

            hits.Sort((p, q) => p.T.CompareTo(q.T));
            (double X, double Y)[]? best = null;
            var bestLen = -1.0;
            for (var i = 0; i + 1 < hits.Count; i++)
            {
                var midT = 0.5 * (hits[i].T + hits[i + 1].T);
                var mid = (x0 + dx * midT, y0 + dy * midT);
                if (!PointInPolygon(mid, polygon))
                    continue;
                var len = Dist2((hits[i].X, hits[i].Y), (hits[i + 1].X, hits[i + 1].Y));
                if (len > bestLen)
                {
                    bestLen = len;
                    best = new[] { (hits[i].X, hits[i].Y), (hits[i + 1].X, hits[i + 1].Y) };
                }
            }

            if (best != null)
                return best;
        }

        return ClipLineToBox(x0, y0, dx, dy, box);
    }

    private static IReadOnlyList<(double X, double Y)> HorizontalAt(
        double y,
        IReadOnlyList<(double X, double Y)> ground,
        IReadOnlyList<(double X, double Y)>? poly)
    {
        if (poly != null && poly.Count >= 3)
        {
            var xs = new List<double>();
            for (var i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                if (Math.Abs(b.Y - a.Y) < Eps)
                {
                    if (Math.Abs(a.Y - y) < 1e-6)
                    {
                        xs.Add(a.X);
                        xs.Add(b.X);
                    }

                    continue;
                }

                var t = (y - a.Y) / (b.Y - a.Y);
                if (t < -1e-9 || t > 1 + 1e-9)
                    continue;
                xs.Add(a.X + t * (b.X - a.X));
            }

            xs.Sort();
            var unique = new List<double>();
            foreach (var x in xs)
            {
                if (unique.Count == 0 || Math.Abs(unique[^1] - x) > 1e-6)
                    unique.Add(x);
            }

            (double X, double Y)[]? best = null;
            var bestLen = -1.0;
            for (var i = 0; i + 1 < unique.Count; i++)
            {
                var mid = ((unique[i] + unique[i + 1]) * 0.5, y);
                if (!PointInPolygon(mid, poly))
                    continue;
                var len = unique[i + 1] - unique[i];
                if (len > bestLen)
                {
                    bestLen = len;
                    best = new[] { (unique[i], y), (unique[i + 1], y) };
                }
            }

            if (best != null)
                return best;
        }

        if (ground.Count < 2)
            return Array.Empty<(double, double)>();
        var above = ground.Where(p => p.Y + 1e-6 >= y).Select(p => p.X).ToList();
        if (above.Count < 2)
            return Array.Empty<(double, double)>();
        return new[] { (above.Min(), y), (above.Max(), y) };
    }

    private static IReadOnlyList<(double X, double Y)> ClipLineToBox(
        double x0,
        double y0,
        double dx,
        double dy,
        (double XMin, double XMax, double YMin, double YMax) box)
    {
        if (!(box.XMax > box.XMin) || !(box.YMax > box.YMin))
            return Array.Empty<(double, double)>();

        var hits = new List<(double T, double X, double Y)>();
        TryEdge(box.XMin, box.YMin, box.XMax, box.YMin);
        TryEdge(box.XMax, box.YMin, box.XMax, box.YMax);
        TryEdge(box.XMax, box.YMax, box.XMin, box.YMax);
        TryEdge(box.XMin, box.YMax, box.XMin, box.YMin);
        hits.Sort((a, b) => a.T.CompareTo(b.T));
        if (hits.Count < 2)
            return Array.Empty<(double, double)>();
        return new[] { (hits[0].X, hits[0].Y), (hits[^1].X, hits[^1].Y) };

        void TryEdge(double ax, double ay, double bx, double by)
        {
            if (!TryIntersectLineSegment(x0, y0, dx, dy, (ax, ay), (bx, by), out var t, out var ix, out var iy))
                return;
            if (hits.All(h => Math.Abs(h.T - t) > 1e-7))
                hits.Add((t, ix, iy));
        }
    }

    private static bool TryIntersectLineSegment(
        double x0,
        double y0,
        double dx,
        double dy,
        (double X, double Y) a,
        (double X, double Y) b,
        out double t,
        out double ix,
        out double iy)
    {
        t = 0;
        ix = 0;
        iy = 0;
        var ex = b.X - a.X;
        var ey = b.Y - a.Y;
        var den = dx * ey - dy * ex;
        if (Math.Abs(den) < Eps)
            return false;
        var rx = a.X - x0;
        var ry = a.Y - y0;
        t = (rx * ey - ry * ex) / den;
        var s = (rx * dy - ry * dx) / den;
        if (s < -1e-9 || s > 1 + 1e-9)
            return false;
        ix = x0 + t * dx;
        iy = y0 + t * dy;
        return true;
    }

    private static (double X, double Y)? FirstHit(
        (double X, double Y) start,
        (double X, double Y) end,
        IReadOnlyList<(double X, double Y)> poly)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var bestT = 1.0;
        (double X, double Y)? hit = null;
        for (var i = 0; i < poly.Count; i++)
        {
            if (!TryIntersectLineSegment(start.X, start.Y, dx, dy, poly[i], poly[(i + 1) % poly.Count],
                    out var t, out var ix, out var iy))
                continue;
            if (t > 1e-6 && t < bestT)
            {
                bestT = t;
                hit = (ix, iy);
            }
        }

        return hit;
    }

    private static IReadOnlyList<(double X, double Y)>? NormalizePolygon(
        IReadOnlyList<(double X, double Y)>? raw)
    {
        if (raw == null || raw.Count < 3)
            return null;
        var pts = raw.ToList();
        if (Dist2(pts[0], pts[^1]) < 1e-8)
            pts.RemoveAt(pts.Count - 1);
        return pts.Count >= 3 ? pts : null;
    }

    public static bool PointInPolygon((double X, double Y) p, IReadOnlyList<(double X, double Y)> poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if (((a.Y > p.Y) != (b.Y > p.Y)) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y + 1e-30) + a.X)
                inside = !inside;
        }

        return inside;
    }

    private static bool IsSteep((double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        if (Math.Abs(dx) < Eps)
            return Math.Abs(b.Y - a.Y) > Eps;
        return Math.Abs((b.Y - a.Y) / dx) > Math.Tan(10.0 * Math.PI / 180.0);
    }

    private static double Dist2((double X, double Y) a, (double X, double Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
