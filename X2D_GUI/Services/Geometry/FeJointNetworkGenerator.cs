using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models.Geometry;

namespace GeotechSlopeApp.Services.Geometry;

public sealed record FeJointNetworkGenerateResult(
    IReadOnlyList<FeJointPolyline> Polylines,
    bool WasCapped,
    int GeneratedCount);

/// <summary>
/// Generates RS2-style joint networks (Parallel Deterministic/Statistical, Cross Jointed,
/// Baecher, Veneziano, Voronoi) clipped to selected region polygons.
/// </summary>
public static class FeJointNetworkGenerator
{
    public const int MaxSegments = 2000;
    private const double Eps = 1e-9;
    private const double BoundaryEps = 1e-7;

    public static FeJointNetworkGenerateResult Generate(
        FeJointNetworkDefinition definition,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var polys = NormalizeRegions(regions);
        if (polys.Count == 0)
            return new FeJointNetworkGenerateResult(Array.Empty<FeJointPolyline>(), false, 0);

        var rng = new Random(definition.RandomSeed);
        var traces = definition.Model switch
        {
            FeJointNetworkModel.ParallelDeterministic => GenerateParallel(definition, definition.Set1, polys, rng, statistical: false, labelPrefix: "J"),
            FeJointNetworkModel.ParallelStatistical => GenerateParallel(definition, definition.Set1, polys, rng, statistical: true, labelPrefix: "J"),
            FeJointNetworkModel.CrossJointed => GenerateCrossJointed(definition, polys, rng),
            FeJointNetworkModel.Baecher => GenerateBaecher(definition, polys, rng),
            FeJointNetworkModel.Veneziano => GenerateVeneziano(definition, polys, rng),
            FeJointNetworkModel.Voronoi => GenerateVoronoi(definition, polys, rng),
            _ => new List<FeJointPolyline>()
        };

        var capped = traces.Count > MaxSegments;
        if (capped)
            traces = traces.Take(MaxSegments).ToList();
        return new FeJointNetworkGenerateResult(traces, capped, traces.Count);
    }

    /// <summary>Removes traces tagged with <paramref name="networkId"/> then appends <paramref name="replacement"/>.</summary>
    public static void ReplaceNetworkFeatures(
        IList<GeologicalFeature> features,
        string networkId,
        IEnumerable<GeologicalFeature> replacement)
    {
        if (features == null || string.IsNullOrWhiteSpace(networkId)) return;
        for (var i = features.Count - 1; i >= 0; i--)
        {
            if (string.Equals(features[i].JointNetworkId, networkId, StringComparison.Ordinal))
                features.RemoveAt(i);
        }

        if (replacement == null) return;
        foreach (var f in replacement)
            features.Add(f);
    }

    public static bool PointInPolygon((double X, double Y) p, IReadOnlyList<(double X, double Y)> poly)
        => FeJointPlacementService.PointInPolygon(p, poly) || PointOnBoundary(p, poly);

    public static IReadOnlyList<(double X, double Y)> ClipInfiniteLine(
        double x0,
        double y0,
        double dipDeg,
        IReadOnlyList<(double X, double Y)> polygon)
    {
        var box = BoundingBox(new[] { polygon });
        return FeJointPlacementService.ClipLineThrough(x0, y0, dipDeg, polygon, box);
    }

    public static IReadOnlyList<((double X, double Y) A, (double X, double Y) B)> ClipSegmentToRegions(
        (double X, double Y) a,
        (double X, double Y) b,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions)
    {
        var result = new List<((double X, double Y) A, (double X, double Y) B)>();
        foreach (var region in regions)
        {
            foreach (var seg in ClipSegmentToPolygon(a, b, region))
                result.Add(seg);
        }

        return result;
    }

    public static IReadOnlyList<((double X, double Y) A, (double X, double Y) B)> ClipSegmentToPolygon(
        (double X, double Y) a,
        (double X, double Y) b,
        IReadOnlyList<(double X, double Y)> poly)
    {
        if (poly == null || poly.Count < 3)
            return Array.Empty<((double, double), (double, double))>();

        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 < Eps * Eps)
            return PointInPolygon(a, poly)
                ? new[] { (a, b) }
                : Array.Empty<((double, double), (double, double))>();

        var ts = new List<double>();
        if (PointInPolygon(a, poly)) ts.Add(0);
        if (PointInPolygon(b, poly)) ts.Add(1);

        for (var i = 0; i < poly.Count; i++)
        {
            if (!TryIntersectSegments(a, b, poly[i], poly[(i + 1) % poly.Count], out var t, out _))
                continue;
            if (t >= -1e-9 && t <= 1 + 1e-9)
                ts.Add(Math.Clamp(t, 0, 1));
        }

        ts.Sort();
        var unique = new List<double>();
        foreach (var t in ts)
        {
            if (unique.Count == 0 || Math.Abs(unique[^1] - t) > 1e-8)
                unique.Add(t);
        }

        var segs = new List<((double X, double Y) A, (double X, double Y) B)>();
        for (var i = 0; i + 1 < unique.Count; i++)
        {
            var t0 = unique[i];
            var t1 = unique[i + 1];
            if (t1 - t0 < 1e-8) continue;
            var mid = (a.X + dx * 0.5 * (t0 + t1), a.Y + dy * 0.5 * (t0 + t1));
            if (!PointInPolygon(mid, poly)) continue;
            segs.Add(((a.X + dx * t0, a.Y + dy * t0), (a.X + dx * t1, a.Y + dy * t1)));
        }

        return segs;
    }

    private static List<FeJointPolyline> GenerateParallel(
        FeJointNetworkDefinition def,
        FeJointSetParameters set,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions,
        Random rng,
        bool statistical,
        string labelPrefix)
    {
        var traces = new List<FeJointPolyline>();
        var box = BoundingBox(regions);
        var dip0 = set.DipDeg;
        var rad = dip0 * Math.PI / 180.0;
        var nx = -Math.Sin(rad);
        var ny = Math.Cos(rad);
        var corners = new[]
        {
            (box.XMin, box.YMin), (box.XMax, box.YMin),
            (box.XMax, box.YMax), (box.XMin, box.YMax)
        };
        var nMin = corners.Min(p => p.Item1 * nx + p.Item2 * ny);
        var nMax = corners.Max(p => p.Item1 * nx + p.Item2 * ny);
        var span = nMax - nMin;
        if (span < Eps) return traces;

        var meanSpacing = Math.Max(1e-3, set.Spacing.Mean);
        var originN = nMin + set.FirstJointOffset;
        if (def.RandomLocation)
            originN = nMin + rng.NextDouble() * meanSpacing;
        else
        {
            var throughN = def.ThroughX * nx + def.ThroughY * ny;
            var k = Math.Round((throughN - originN) / meanSpacing);
            originN = throughN - k * meanSpacing;
        }

        var n = originN;
        var index = 0;
        var guard = 0;
        while (n <= nMax + meanSpacing && guard++ < MaxSegments + 8)
        {
            var spacing = statistical
                ? Math.Max(1e-3, set.Spacing.Sample(rng, 1e-3, span))
                : meanSpacing;
            var dip = statistical && set.DipStdDeg > 0
                ? dip0 + set.DipStdDeg * SampleUnitNormal(rng)
                : dip0;
            var x0 = 0.5 * (box.XMin + box.XMax) + nx * (n - 0.5 * (nMin + nMax));
            var y0 = 0.5 * (box.YMin + box.YMax) + ny * (n - 0.5 * (nMin + nMax));
            AddParallelTracesOnLine(traces, set, regions, rng, statistical, labelPrefix, dip, x0, y0, index++, box);
            n += spacing;
            if (traces.Count >= MaxSegments) break;
        }

        return traces;
    }

    private static void AddParallelTracesOnLine(
        List<FeJointPolyline> traces,
        FeJointSetParameters set,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions,
        Random rng,
        bool statistical,
        string labelPrefix,
        double dip,
        double x0,
        double y0,
        int lineIndex,
        (double XMin, double XMax, double YMin, double YMax) box)
    {
        foreach (var region in regions)
        {
            var chord = FeJointPlacementService.ClipLineThrough(x0, y0, dip, region, box);
            if (chord.Count < 2) continue;
            var a = chord[0];
            var b = chord[^1];
            if (set.InfiniteLength)
            {
                traces.Add(new FeJointPolyline($"{labelPrefix}{lineIndex + 1}", new[] { a, b }));
                continue;
            }

            var dirx = b.X - a.X;
            var diry = b.Y - a.Y;
            var chordLen = Math.Sqrt(dirx * dirx + diry * diry);
            if (chordLen < Eps) continue;
            dirx /= chordLen;
            diry /= chordLen;

            var persistence = statistical
                ? Math.Clamp(set.Persistence.Sample(rng, 0.05, 0.99), 0.05, 0.99)
                : Math.Clamp(set.Persistence.Mean, 0.05, 0.99);
            var jointLen = statistical
                ? Math.Max(1e-3, set.Length.Sample(rng, 1e-3, chordLen))
                : Math.Max(1e-3, set.Length.Mean);
            var period = jointLen / persistence;
            var offset = set.RandomOffset
                ? rng.NextDouble() * period
                : set.OffsetLength + lineIndex * set.OffsetLength;
            offset = ((offset % period) + period) % period;

            var s = -offset;
            var seg = 0;
            while (s < chordLen && traces.Count < MaxSegments)
            {
                var s0 = Math.Max(0, s);
                var s1 = Math.Min(chordLen, s + jointLen);
                if (s1 - s0 > 1e-6)
                {
                    var p0 = (a.X + dirx * s0, a.Y + diry * s0);
                    var p1 = (a.X + dirx * s1, a.Y + diry * s1);
                    traces.Add(new FeJointPolyline($"{labelPrefix}{lineIndex + 1}.{++seg}", new[] { p0, p1 }));
                }

                s += period;
            }
        }
    }

    private static List<FeJointPolyline> GenerateCrossJointed(
        FeJointNetworkDefinition def,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions,
        Random rng)
    {
        var bedding = def.Set1.Clone();
        bedding.InfiniteLength = true;
        var traces = GenerateParallel(def, bedding, regions, rng, statistical: def.Set1.UseStatisticalSpacing || def.Set1.Spacing.Kind != FeJointDistributionKind.Constant, labelPrefix: "B");

        var box = BoundingBox(regions);
        var bedDip = def.Set1.DipDeg;
        var crossDip = def.Set2.DipDeg;
        if (Math.Abs(NormalizeDipDelta(crossDip - bedDip)) < 1.0)
            crossDip = bedDip + 90;

        var bedRad = bedDip * Math.PI / 180.0;
        var tx = Math.Cos(bedRad);
        var ty = Math.Sin(bedRad);
        var nx = -Math.Sin(bedRad);
        var ny = Math.Cos(bedRad);
        var corners = new[]
        {
            (box.XMin, box.YMin), (box.XMax, box.YMin),
            (box.XMax, box.YMax), (box.XMin, box.YMax)
        };
        var nMin = corners.Min(p => p.Item1 * nx + p.Item2 * ny);
        var nMax = corners.Max(p => p.Item1 * nx + p.Item2 * ny);
        var tMin = corners.Min(p => p.Item1 * tx + p.Item2 * ty);
        var tMax = corners.Max(p => p.Item1 * tx + p.Item2 * ty);

        var meanBed = Math.Max(1e-3, def.Set1.Spacing.Mean);
        var originN = nMin + (def.RandomLocation ? rng.NextDouble() * meanBed : def.Set1.FirstJointOffset);
        var planes = new List<double>();
        var n = originN;
        var guard = 0;
        while (n <= nMax + meanBed && guard++ < 400)
        {
            planes.Add(n);
            n += def.Set1.UseStatisticalSpacing || def.Set1.Spacing.Kind != FeJointDistributionKind.Constant
                ? Math.Max(1e-3, def.Set1.Spacing.Sample(rng, 1e-3, nMax - nMin))
                : meanBed;
        }

        var meanCross = Math.Max(1e-3, def.Set2.Spacing.Mean);
        var statisticalCross = def.Set2.UseStatisticalSpacing || def.Set2.Spacing.Kind != FeJointDistributionKind.Constant;
        var cx = Math.Cos(crossDip * Math.PI / 180.0);
        var cy = Math.Sin(crossDip * Math.PI / 180.0);
        var sinDelta = Math.Abs(Math.Sin((crossDip - bedDip) * Math.PI / 180.0));
        if (sinDelta < 1e-3) sinDelta = 1;

        var idx = 0;
        for (var i = 0; i + 1 < planes.Count && traces.Count < MaxSegments; i++)
        {
            var n0 = planes[i];
            var n1 = planes[i + 1];
            var gap = n1 - n0;
            var crossLen = gap / sinDelta;
            var t = tMin + (def.RandomLocation ? rng.NextDouble() * meanCross : 0);
            var walkGuard = 0;
            while (t <= tMax + meanCross && walkGuard++ < 400 && traces.Count < MaxSegments)
            {
                var spacing = statisticalCross
                    ? Math.Max(1e-3, def.Set2.Spacing.Sample(rng, 1e-3, tMax - tMin))
                    : meanCross;
                var midN = 0.5 * (n0 + n1);
                var px = tx * t + nx * midN;
                var py = ty * t + ny * midN;
                var a = (px - cx * 0.5 * crossLen, py - cy * 0.5 * crossLen);
                var b = (px + cx * 0.5 * crossLen, py + cy * 0.5 * crossLen);
                foreach (var seg in ClipSegmentToRegions(a, b, regions))
                {
                    traces.Add(new FeJointPolyline($"C{++idx}", new[] { seg.A, seg.B }));
                    if (traces.Count >= MaxSegments) break;
                }

                t += spacing;
            }
        }

        return traces;
    }

    private static List<FeJointPolyline> GenerateBaecher(
        FeJointNetworkDefinition def,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions,
        Random rng)
    {
        var traces = new List<FeJointPolyline>();
        var area = regions.Sum(PolygonArea);
        var expected = Math.Max(0, def.Intensity) * Math.Max(area, 1e-6);
        var count = SamplePoisson(expected, rng);
        count = Math.Min(count, MaxSegments);
        var box = BoundingBox(regions);
        var expand = Math.Max(def.Length.Mean, 1.0);
        var inflated = InflateBox(box, expand);
        var idx = 0;
        var attempts = 0;
        while (idx < count && attempts++ < count * 40 && traces.Count < MaxSegments)
        {
            var c = RandomPointInBox(rng, inflated);
            if (!PointInAnyExpanded(c, regions, expand) && !PointInAny(c, regions))
                continue;
            var dip = def.Orientation.Kind == FeJointDistributionKind.Constant
                ? def.Orientation.Mean
                : def.Orientation.Sample(rng, -90, 90);
            var len = Math.Max(1e-3, def.Length.Sample(rng, 1e-3, 1e6));
            var rad = dip * Math.PI / 180.0;
            var a = (c.X - 0.5 * len * Math.Cos(rad), c.Y - 0.5 * len * Math.Sin(rad));
            var b = (c.X + 0.5 * len * Math.Cos(rad), c.Y + 0.5 * len * Math.Sin(rad));
            var added = false;
            foreach (var seg in ClipSegmentToRegions(a, b, regions))
            {
                traces.Add(new FeJointPolyline($"Baecher {traces.Count + 1}", new[] { seg.A, seg.B }));
                added = true;
                if (traces.Count >= MaxSegments) break;
            }

            if (added || PointInAny(c, regions))
                idx++;
        }

        return traces;
    }

    private static List<FeJointPolyline> GenerateVeneziano(
        FeJointNetworkDefinition def,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions,
        Random rng)
    {
        var traces = new List<FeJointPolyline>();
        var box = BoundingBox(regions);
        var cx = 0.5 * (box.XMin + box.XMax);
        var cy = 0.5 * (box.YMin + box.YMax);
        var r = 0.5 * Math.Sqrt((box.XMax - box.XMin) * (box.XMax - box.XMin) + (box.YMax - box.YMin) * (box.YMax - box.YMin)) + Math.Max(def.Length.Mean, 1);
        var expected = Math.Max(0, def.Intensity) * 2.0 * r;
        var lineCount = Math.Min(SamplePoisson(expected, rng), MaxSegments);
        var idx = 0;
        for (var i = 0; i < lineCount && traces.Count < MaxSegments; i++)
        {
            var dip = def.Orientation.Kind == FeJointDistributionKind.Constant
                ? def.Orientation.Mean
                : def.Orientation.Sample(rng, -90, 90);
            var offset = (rng.NextDouble() * 2 - 1) * r;
            var rad = dip * Math.PI / 180.0;
            var nx = -Math.Sin(rad);
            var ny = Math.Cos(rad);
            var x0 = cx + nx * offset;
            var y0 = cy + ny * offset;

            foreach (var region in regions)
            {
                var chord = FeJointPlacementService.ClipLineThrough(x0, y0, dip, region, box);
                if (chord.Count < 2) continue;
                var a = chord[0];
                var b = chord[^1];
                var dirx = b.X - a.X;
                var diry = b.Y - a.Y;
                var chordLen = Math.Sqrt(dirx * dirx + diry * diry);
                if (chordLen < Eps) continue;
                dirx /= chordLen;
                diry /= chordLen;

                var persistence = Math.Clamp(def.Persistence.Sample(rng, 0.05, 0.99), 0.05, 0.99);
                var jointLen = Math.Max(1e-3, def.Length.Sample(rng, 1e-3, chordLen));
                var period = jointLen / persistence;
                var s = rng.NextDouble() * period;
                while (s < chordLen && traces.Count < MaxSegments)
                {
                    var s0 = s;
                    var s1 = Math.Min(chordLen, s + jointLen);
                    if (s1 - s0 > 1e-6)
                    {
                        var p0 = (a.X + dirx * s0, a.Y + diry * s0);
                        var p1 = (a.X + dirx * s1, a.Y + diry * s1);
                        traces.Add(new FeJointPolyline($"V{++idx}", new[] { p0, p1 }));
                    }

                    s += period;
                }
            }
        }

        return traces;
    }

    private static List<FeJointPolyline> GenerateVoronoi(
        FeJointNetworkDefinition def,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions,
        Random rng)
    {
        var traces = new List<FeJointPolyline>();
        var box = BoundingBox(regions);
        var area = Math.Max(regions.Sum(PolygonArea), 1e-6);
        var density = ResolveVoronoiDensity(def, area);
        var sites = BuildVoronoiSites(def, regions, box, density, rng);
        if (sites.Count < 3)
            return traces;

        var ax = Math.Max(0.05, def.AnisotropyX);
        var ay = Math.Max(0.05, def.AnisotropyY);
        var scaled = sites.Select(p => new Point(p.X / ax, p.Y / ay)).ToList();
        var tris = BowyerWatsonTriangulator.Triangulate(scaled);
        if (tris.Count == 0)
            return traces;

        var circum = new List<(double X, double Y)>(tris.Count);
        foreach (var (a, b, c) in tris)
        {
            if (!TryCircumcenter(scaled[a], scaled[b], scaled[c], out var cc))
                circum.Add((double.NaN, double.NaN));
            else
                circum.Add((cc.X * ax, cc.Y * ay));
        }

        var edgeTris = new Dictionary<(int, int), List<int>>();
        for (var ti = 0; ti < tris.Count; ti++)
        {
            var (a, b, c) = tris[ti];
            AddEdge(edgeTris, a, b, ti);
            AddEdge(edgeTris, b, c, ti);
            AddEdge(edgeTris, c, a, ti);
        }

        var far = 2.0 * Math.Max(box.XMax - box.XMin, box.YMax - box.YMin) + 10;
        var idx = 0;
        foreach (var (edge, owners) in edgeTris)
        {
            if (owners.Count == 2)
            {
                var c0 = circum[owners[0]];
                var c1 = circum[owners[1]];
                if (double.IsNaN(c0.X) || double.IsNaN(c1.X)) continue;
                AddClipped(traces, c0, c1, regions, ref idx);
            }
            else if (owners.Count == 1)
            {
                var ti = owners[0];
                var c0 = circum[ti];
                if (double.IsNaN(c0.X)) continue;
                var (ia, ib, ic) = tris[ti];
                var third = ia != edge.Item1 && ia != edge.Item2 ? ia : ib != edge.Item1 && ib != edge.Item2 ? ib : ic;
                var pa = sites[edge.Item1];
                var pb = sites[edge.Item2];
                var mid = ((pa.X + pb.X) * 0.5, (pa.Y + pb.Y) * 0.5);
                var p3 = sites[third];
                var ex = pb.X - pa.X;
                var ey = pb.Y - pa.Y;
                var rx = -ey;
                var ry = ex;
                if (rx * (mid.Item1 - p3.X) + ry * (mid.Item2 - p3.Y) < 0)
                {
                    rx = -rx;
                    ry = -ry;
                }

                var len = Math.Sqrt(rx * rx + ry * ry);
                if (len < Eps) continue;
                rx = rx / len * far;
                ry = ry / len * far;
                AddClipped(traces, c0, (c0.X + rx, c0.Y + ry), regions, ref idx);
            }

            if (traces.Count >= MaxSegments) break;
        }

        return traces;
    }

    private static List<(double X, double Y)> BuildVoronoiSites(
        FeJointNetworkDefinition def,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions,
        (double XMin, double XMax, double YMin, double YMax) box,
        double density,
        Random rng)
    {
        var inflated = InflateBox(box, 0.15 * Math.Max(box.XMax - box.XMin, box.YMax - box.YMin) + 0.5);
        if (def.VoronoiRegularity == FeJointVoronoiRegularity.RegularHexagon)
            return HexagonSites(inflated, density, def.HexagonHorizontal);

        int target;
        if (def.VoronoiDensityMethod == FeJointVoronoiDensityMethod.SiteCount)
            target = def.SiteCount;
        else
        {
            var area = Math.Max((inflated.XMax - inflated.XMin) * (inflated.YMax - inflated.YMin), 1e-6);
            target = Math.Max(3, SamplePoisson(density * area, rng));
        }

        target = Math.Min(target, 800);
        var sites = new List<(double X, double Y)>(target);
        var attempts = 0;
        while (sites.Count < target && attempts++ < target * 30)
        {
            var p = RandomPointInBox(rng, inflated);
            if (PointInAny(p, regions) || PointInExpandedBox(p, inflated))
                sites.Add(p);
        }

        var iterations = def.VoronoiRegularity switch
        {
            FeJointVoronoiRegularity.MediumRegular => 3,
            FeJointVoronoiRegularity.AlmostRegular => 8,
            _ => 0
        };
        if (iterations > 0 && sites.Count >= 3)
            RelaxSites(sites, inflated, iterations);

        return sites;
    }

    private static List<(double X, double Y)> HexagonSites(
        (double XMin, double XMax, double YMin, double YMax) box,
        double density,
        bool horizontal)
    {
        var sites = new List<(double X, double Y)>();
        density = Math.Max(density, 1e-4);
        var area = (3.0 * Math.Sqrt(3.0)) / 2.0;
        var side = Math.Sqrt(1.0 / (area * density));
        if (horizontal)
        {
            var dx = Math.Sqrt(3.0) * side;
            var dy = 1.5 * side;
            var row = 0;
            for (var y = box.YMin - dy; y <= box.YMax + dy; y += dy, row++)
            {
                var x0 = box.XMin - dx + (row % 2 == 0 ? 0 : 0.5 * dx);
                for (var x = x0; x <= box.XMax + dx; x += dx)
                    sites.Add((x, y));
            }
        }
        else
        {
            var dx = 1.5 * side;
            var dy = Math.Sqrt(3.0) * side;
            var col = 0;
            for (var x = box.XMin - dx; x <= box.XMax + dx; x += dx, col++)
            {
                var y0 = box.YMin - dy + (col % 2 == 0 ? 0 : 0.5 * dy);
                for (var y = y0; y <= box.YMax + dy; y += dy)
                    sites.Add((x, y));
            }
        }

        return sites;
    }

    private static void RelaxSites(
        List<(double X, double Y)> sites,
        (double XMin, double XMax, double YMin, double YMax) box,
        int iterations)
    {
        var nx = 28;
        var ny = 28;
        var dx = (box.XMax - box.XMin) / nx;
        var dy = (box.YMax - box.YMin) / ny;
        for (var iter = 0; iter < iterations; iter++)
        {
            var accX = new double[sites.Count];
            var accY = new double[sites.Count];
            var accN = new int[sites.Count];
            for (var iy = 0; iy < ny; iy++)
            for (var ix = 0; ix < nx; ix++)
            {
                var px = box.XMin + (ix + 0.5) * dx;
                var py = box.YMin + (iy + 0.5) * dy;
                var best = 0;
                var bestD = double.MaxValue;
                for (var s = 0; s < sites.Count; s++)
                {
                    var ddx = sites[s].X - px;
                    var ddy = sites[s].Y - py;
                    var d = ddx * ddx + ddy * ddy;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = s;
                    }
                }

                accX[best] += px;
                accY[best] += py;
                accN[best]++;
            }

            for (var s = 0; s < sites.Count; s++)
            {
                if (accN[s] == 0) continue;
                sites[s] = (accX[s] / accN[s], accY[s] / accN[s]);
            }
        }
    }

    private static double ResolveVoronoiDensity(FeJointNetworkDefinition def, double area)
    {
        return def.VoronoiDensityMethod switch
        {
            FeJointVoronoiDensityMethod.SiteCount => def.SiteCount / area,
            FeJointVoronoiDensityMethod.AverageJointLength => AverageLengthToDensity(def.Intensity),
            _ => Math.Max(1e-4, def.Intensity)
        };
    }

    private static double AverageLengthToDensity(double avgLen)
    {
        var a = Math.Max(avgLen, 1e-3);
        return 1.0 / (0.5 * Math.Sqrt(3.0) * a * a);
    }

    private static void AddClipped(
        List<FeJointPolyline> traces,
        (double X, double Y) a,
        (double X, double Y) b,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions,
        ref int idx)
    {
        foreach (var seg in ClipSegmentToRegions(a, b, regions))
        {
            if (Dist2(seg.A, seg.B) < 1e-12) continue;
            traces.Add(new FeJointPolyline($"Voro {++idx}", new[] { seg.A, seg.B }));
            if (traces.Count >= MaxSegments) return;
        }
    }

    private static void AddEdge(Dictionary<(int, int), List<int>> map, int a, int b, int tri)
    {
        var key = a < b ? (a, b) : (b, a);
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<int>(2);
            map[key] = list;
        }

        list.Add(tri);
    }

    private static bool TryCircumcenter(Point a, Point b, Point c, out (double X, double Y) center)
    {
        center = default;
        var d = 2.0 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Math.Abs(d) < 1e-12) return false;
        var a2 = a.X * a.X + a.Y * a.Y;
        var b2 = b.X * b.X + b.Y * b.Y;
        var c2 = c.X * c.X + c.Y * c.Y;
        center = (
            (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d,
            (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
        return true;
    }

    private static List<IReadOnlyList<(double X, double Y)>> NormalizeRegions(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>>? regions)
    {
        var list = new List<IReadOnlyList<(double X, double Y)>>();
        if (regions == null) return list;
        foreach (var raw in regions)
        {
            if (raw == null || raw.Count < 3) continue;
            var pts = raw.ToList();
            if (Dist2(pts[0], pts[^1]) < 1e-10)
                pts.RemoveAt(pts.Count - 1);
            if (pts.Count >= 3)
                list.Add(pts);
        }

        return list;
    }

    private static (double XMin, double XMax, double YMin, double YMax) BoundingBox(
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions)
    {
        var xMin = double.MaxValue;
        var xMax = double.MinValue;
        var yMin = double.MaxValue;
        var yMax = double.MinValue;
        foreach (var r in regions)
        foreach (var p in r)
        {
            if (p.X < xMin) xMin = p.X;
            if (p.X > xMax) xMax = p.X;
            if (p.Y < yMin) yMin = p.Y;
            if (p.Y > yMax) yMax = p.Y;
        }

        if (!(xMax > xMin) || !(yMax > yMin))
            return (0, 1, 0, 1);
        return (xMin, xMax, yMin, yMax);
    }

    private static (double XMin, double XMax, double YMin, double YMax) InflateBox(
        (double XMin, double XMax, double YMin, double YMax) box,
        double pad)
        => (box.XMin - pad, box.XMax + pad, box.YMin - pad, box.YMax + pad);

    private static bool PointInAny((double X, double Y) p, IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions)
        => regions.Any(r => PointInPolygon(p, r));

    private static bool PointInAnyExpanded(
        (double X, double Y) p,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> regions,
        double expand)
    {
        foreach (var r in regions)
        {
            var box = BoundingBox(new[] { r });
            if (p.X >= box.XMin - expand && p.X <= box.XMax + expand &&
                p.Y >= box.YMin - expand && p.Y <= box.YMax + expand)
                return true;
        }

        return false;
    }

    private static bool PointInExpandedBox((double X, double Y) p, (double XMin, double XMax, double YMin, double YMax) box)
        => p.X >= box.XMin && p.X <= box.XMax && p.Y >= box.YMin && p.Y <= box.YMax;

    private static (double X, double Y) RandomPointInBox(Random rng, (double XMin, double XMax, double YMin, double YMax) box)
        => (box.XMin + rng.NextDouble() * (box.XMax - box.XMin),
            box.YMin + rng.NextDouble() * (box.YMax - box.YMin));

    private static double PolygonArea(IReadOnlyList<(double X, double Y)> poly)
    {
        var a = 0.0;
        for (var i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            a += p.X * q.Y - q.X * p.Y;
        }

        return Math.Abs(a) * 0.5;
    }

    private static bool PointOnBoundary((double X, double Y) p, IReadOnlyList<(double X, double Y)> poly)
    {
        for (var i = 0; i < poly.Count; i++)
        {
            if (DistanceToSegment(p, poly[i], poly[(i + 1) % poly.Count]) <= BoundaryEps)
                return true;
        }

        return false;
    }

    private static double DistanceToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 < Eps) return Math.Sqrt(Dist2(p, a));
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Math.Sqrt(Dist2(p, (a.X + t * dx, a.Y + t * dy)));
    }

    private static bool TryIntersectSegments(
        (double X, double Y) a,
        (double X, double Y) b,
        (double X, double Y) c,
        (double X, double Y) d,
        out double tAb,
        out (double X, double Y) hit)
    {
        tAb = 0;
        hit = default;
        var rX = b.X - a.X;
        var rY = b.Y - a.Y;
        var sX = d.X - c.X;
        var sY = d.Y - c.Y;
        var den = rX * sY - rY * sX;
        if (Math.Abs(den) < Eps) return false;
        var qX = c.X - a.X;
        var qY = c.Y - a.Y;
        tAb = (qX * sY - qY * sX) / den;
        var tCd = (qX * rY - qY * rX) / den;
        if (tAb < -1e-9 || tAb > 1 + 1e-9 || tCd < -1e-9 || tCd > 1 + 1e-9)
            return false;
        hit = (a.X + tAb * rX, a.Y + tAb * rY);
        return true;
    }

    private static int SamplePoisson(double lambda, Random rng)
    {
        if (lambda <= 0) return 0;
        if (lambda > 60)
            return Math.Max(0, (int)Math.Round(lambda + Math.Sqrt(lambda) * SampleUnitNormal(rng)));
        var l = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;
        do
        {
            k++;
            p *= rng.NextDouble();
        } while (p > l);

        return k - 1;
    }

    private static double SampleUnitNormal(Random rng)
    {
        var u1 = Math.Max(1e-12, rng.NextDouble());
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double NormalizeDipDelta(double deg)
    {
        while (deg > 90) deg -= 180;
        while (deg < -90) deg += 180;
        return deg;
    }

    private static double Dist2((double X, double Y) a, (double X, double Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
