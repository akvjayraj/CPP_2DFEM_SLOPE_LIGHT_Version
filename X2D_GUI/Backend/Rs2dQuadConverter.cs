using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Converts an RS2D-generated triangle mesh (T3) into Q4 (and optionally Q8 by midside insertion)
/// using quality-aware pairing of adjacent triangles. Triangles that cannot be merged into a
/// quad of acceptable shape are kept unchanged so the output mesh quality is never silently
/// degraded.
/// </summary>
public static class Rs2dQuadConverter
{
    public sealed class Quality
    {
        /// <summary>Lowest internal angle (deg) acceptable in a paired quad. Below this rejects pairing.</summary>
        public double MinAngleDeg { get; init; } = 25.0;
        /// <summary>Maximum acceptable aspect ratio (longest/shortest edge) for a paired quad.</summary>
        public double MaxAspectRatio { get; init; } = 4.0;
        /// <summary>Maximum acceptable skew (1 - 4*minSubArea/totalArea) for a paired quad.</summary>
        public double MaxSkew { get; init; } = 0.7;
        /// <summary>Maximum allowed triangle-area ratio (large/small) for two triangles to be paired.</summary>
        public double MaxAreaRatio { get; init; } = 3.0;

        public static Quality Default { get; } = new Quality();

        /// <summary>Slider 0..1: 0=prefer more quads (relax), 1=prefer higher quality (tighten). 0.5 = default.</summary>
        public static Quality FromSlider(double sliderValue)
        {
            sliderValue = Math.Max(0.0, Math.Min(1.0, sliderValue));
            // At slider=0 we relax: minAngle 15°, AR 6, skew 0.85, areaRatio 4
            // At slider=1 we tighten: minAngle 30°, AR 3, skew 0.55, areaRatio 2
            return new Quality
            {
                MinAngleDeg = 15.0 + 15.0 * sliderValue,
                MaxAspectRatio = 6.0 - 3.0 * sliderValue,
                MaxSkew = 0.85 - 0.30 * sliderValue,
                MaxAreaRatio = 4.0 - 2.0 * sliderValue,
            };
        }
    }

    public sealed class TriRef
    {
        public int Region;
        public int Etype;
        public int A;
        public int B;
        public int C;
    }

    public sealed class QuadResult
    {
        /// <summary>4-tuple of corner node indices (0-based) in CCW order.</summary>
        public int Q1;
        public int Q2;
        public int Q3;
        public int Q4;
        public int Etype;
    }

    public sealed class UnmatchedTri
    {
        public int Region;
        public int A, B, C;
        public int Etype;
        public string Reason = "";
    }

    /// <summary>When set, triangles in steep gradation bands are kept as T3 fallbacks (not paired to Q4).</summary>
    public sealed class GradationPolicy
    {
        public double NearSize { get; init; }
        public double GlobalSize { get; init; }
        public double GrowthFactor { get; init; } = 1.3;
        /// <summary>Max ratio of local edge length to expected size-field value before a triangle is treated as transition-zone.</summary>
        public double TransitionRatioThreshold { get; init; } = 1.75;
        public IReadOnlyList<Point>? BoundaryPoints { get; init; }
        /// <summary>Horizontal material layer cut elevations — triangles straddling any cut are not paired to Q4.</summary>
        public IReadOnlyList<double>? LayerCutYs { get; init; }
        public double LayerCutTol { get; init; } = 1e-4;
    }

    public sealed class ConversionResult
    {
        public List<QuadResult> Quads { get; } = new();
        public List<UnmatchedTri> Unmatched { get; } = new();
        /// <summary>Optional debug / status counts useful for the dashboard.</summary>
        public int TrianglesIn { get; set; }
        public int QuadsOut { get; set; }
        public int TrianglesOut { get; set; }
        /// <summary>Quality of paired quads only (legacy).</summary>
        public double MinAngleDeg { get; set; }
        public double MaxAspectRatio { get; set; }
        public double MeanSkew { get; set; }
        /// <summary>Quality across all output elements (quads + T3 fallbacks).</summary>
        public double OverallMinAngleDeg { get; set; }
        public double OverallMaxAspectRatio { get; set; }
        public double OverallMaxSkew { get; set; }
        public int TransitionZoneTriangles { get; set; }
    }

    /// <summary>
    /// Pair triangles into Q4. Triangles must be supplied with consistent CCW winding using node
    /// indices into <paramref name="nodes"/> (0-based).
    /// </summary>
    public static ConversionResult PairTrianglesToQuads(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<TriRef> triangles,
        Quality? quality = null,
        GradationPolicy? gradationPolicy = null)
    {
        var q = quality ?? Quality.Default;
        var result = new ConversionResult { TrianglesIn = triangles.Count };
        if (triangles.Count == 0) return result;

        var transitionTris = IdentifyTransitionTriangles(nodes, triangles, gradationPolicy);

        // Edge -> list of (triangleIndex, oppositeVertexIndex) (sorted-pair key)
        var edgeMap = new Dictionary<(int, int), List<(int triIdx, int opp)>>();
        for (int i = 0; i < triangles.Count; i++)
        {
            var t = triangles[i];
            AddEdge(edgeMap, t.A, t.B, i, t.C);
            AddEdge(edgeMap, t.B, t.C, i, t.A);
            AddEdge(edgeMap, t.C, t.A, i, t.B);
        }

        // Build a candidate-pair list, ranked by combined-quality score (best first).
        var candidates = new List<(double score, int triA, int triB, int aOpp, int bOpp, int eA, int eB)>();
        foreach (var kv in edgeMap)
        {
            if (kv.Value.Count != 2) continue; // boundary or degenerate
            var t1 = kv.Value[0];
            var t2 = kv.Value[1];
            // Reject if region/etype differs (don't merge across material zones).
            if (triangles[t1.triIdx].Region != triangles[t2.triIdx].Region) continue;
            if (triangles[t1.triIdx].Etype != triangles[t2.triIdx].Etype) continue;
            if (transitionTris != null &&
                (transitionTris.Contains(t1.triIdx) || transitionTris.Contains(t2.triIdx)))
                continue;

            var quadRaw = MakeQuadFromPair(triangles[t1.triIdx], triangles[t2.triIdx], kv.Key);
            if (quadRaw == null) continue;
            var quad = OrientCcw(nodes, quadRaw.Value);

            double area1 = TriArea(nodes, triangles[t1.triIdx]);
            double area2 = TriArea(nodes, triangles[t2.triIdx]);
            double aRatio = Math.Max(area1, area2) / Math.Max(1e-30, Math.Min(area1, area2));
            if (aRatio > q.MaxAreaRatio) continue;

            if (!QuadIsConvexCcw(nodes, quad, out double minAngleDeg, out double aspect, out double skew))
                continue;
            if (minAngleDeg < q.MinAngleDeg) continue;
            if (aspect > q.MaxAspectRatio) continue;
            if (skew > q.MaxSkew) continue;

            // Lower score is better: weighted distance from "perfect square".
            double score = (q.MinAngleDeg - minAngleDeg) * 0.0
                         + Math.Max(0.0, 90.0 - minAngleDeg) * 0.6
                         + Math.Max(0.0, aspect - 1.0) * 25.0
                         + skew * 50.0
                         + Math.Max(0.0, aRatio - 1.0) * 10.0;
            candidates.Add((score, t1.triIdx, t2.triIdx, t1.opp, t2.opp, kv.Key.Item1, kv.Key.Item2));
        }
        candidates.Sort((a, b) => a.score.CompareTo(b.score));

        var used = new bool[triangles.Count];
        double sumSkew = 0;
        int pairCount = 0;
        double minAngleAcc = 180.0, maxArAcc = 0.0;
        foreach (var cand in candidates)
        {
            if (used[cand.triA] || used[cand.triB]) continue;
            var quadRaw = MakeQuadFromPair(triangles[cand.triA], triangles[cand.triB], (cand.eA, cand.eB));
            if (quadRaw == null) continue;
            var quad = OrientCcw(nodes, quadRaw.Value);
            used[cand.triA] = used[cand.triB] = true;
            QuadIsConvexCcw(nodes, quad, out double minAngleDeg, out double aspect, out double skew);
            sumSkew += skew;
            pairCount++;
            if (minAngleDeg < minAngleAcc) minAngleAcc = minAngleDeg;
            if (aspect > maxArAcc) maxArAcc = aspect;

            result.Quads.Add(new QuadResult
            {
                Q1 = quad.q1,
                Q2 = quad.q2,
                Q3 = quad.q3,
                Q4 = quad.q4,
                Etype = triangles[cand.triA].Etype,
            });
        }

        for (int i = 0; i < triangles.Count; i++)
        {
            if (used[i]) continue;
            string reason = "no compatible neighbor";
            // If triangle has no shared edge counted twice, it's a boundary triangle; this is acceptable.
            var t = triangles[i];
            int shared = 0;
            foreach (var (a, b) in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
            {
                var key = a < b ? (a, b) : (b, a);
                if (edgeMap.TryGetValue(key, out var list) && list.Count == 2) shared++;
            }
            if (shared == 0) reason = "boundary triangle";
            if (transitionTris != null && transitionTris.Contains(i))
                reason = "transition zone (layer interface or gradation band)";
            result.Unmatched.Add(new UnmatchedTri { Region = t.Region, Etype = t.Etype, A = t.A, B = t.B, C = t.C, Reason = reason });
        }

        result.QuadsOut = result.Quads.Count;
        result.TrianglesOut = result.Unmatched.Count;
        result.TransitionZoneTriangles = transitionTris?.Count ?? 0;
        result.MinAngleDeg = pairCount > 0 ? minAngleAcc : 0.0;
        result.MaxAspectRatio = pairCount > 0 ? maxArAcc : 0.0;
        result.MeanSkew = pairCount > 0 ? sumSkew / pairCount : 0.0;

        var overall = MeshElementQualityMetrics.ComputeWholeMesh(nodes, result.Quads, result.Unmatched);
        result.OverallMinAngleDeg = overall.MinAngleDeg;
        result.OverallMaxAspectRatio = overall.MaxAspectRatio;
        result.OverallMaxSkew = overall.MaxSkew;
        return result;
    }

    private static HashSet<int> IdentifyTransitionTriangles(
        IReadOnlyList<Point> nodes,
        IReadOnlyList<TriRef> triangles,
        GradationPolicy? policy)
    {
        var set = new HashSet<int>();
        if (policy == null) return set;

        if (policy.LayerCutYs is { Count: > 0 })
        {
            for (int i = 0; i < triangles.Count; i++)
            {
                var t = triangles[i];
                var pa = nodes[t.A]; var pb = nodes[t.B]; var pc = nodes[t.C];
                double minY = Math.Min(Math.Min(pa.Y, pb.Y), pc.Y);
                double maxY = Math.Max(Math.Max(pa.Y, pb.Y), pc.Y);
                foreach (var cutY in policy.LayerCutYs)
                {
                    if (cutY > minY + policy.LayerCutTol && cutY < maxY - policy.LayerCutTol)
                    {
                        set.Add(i);
                        break;
                    }
                }
            }
        }

        if (policy.NearSize <= 0 || policy.GlobalSize <= 0) return set;
        var boundary = policy.BoundaryPoints ?? Array.Empty<Point>();

        for (int i = 0; i < triangles.Count; i++)
        {
            if (set.Contains(i)) continue;
            var t = triangles[i];
            var pa = nodes[t.A]; var pb = nodes[t.B]; var pc = nodes[t.C];
            var centroid = new Point((pa.X + pb.X + pc.X) / 3.0, (pa.Y + pb.Y + pc.Y) / 3.0);
            double dist = Rs2dBoundaryGeometry.MinDistanceToBoundaryPoints(centroid, boundary);
            double expected = Rs2dBoundaryGeometry.ExpectedSizeAtDistance(
                dist, policy.NearSize, policy.GlobalSize, policy.GrowthFactor);
            double maxEdge = Math.Max(Dist(pa, pb), Math.Max(Dist(pb, pc), Dist(pc, pa)));
            double ratio = Math.Max(maxEdge, expected) / Math.Max(Math.Min(maxEdge, expected), 1e-12);
            if (ratio >= policy.TransitionRatioThreshold)
                set.Add(i);
        }

        return set;
    }

    /// <summary>
    /// Insert midside nodes on the 4 edges of each quad and return Q8 corner+midside indices.
    /// Edge (A,B) midside indices are deduplicated across adjacent quads using a shared edge map.
    /// Appends new midside points to <paramref name="nodes"/>.
    /// </summary>
    public static List<(int n1, int n2, int n3, int n4, int n5, int n6, int n7, int n8)> UpgradeQ4ToQ8(
        List<Point> nodes,
        IReadOnlyList<QuadResult> quads,
        IReadOnlyList<Rs2dBoundarySegment>? boundarySegments = null)
    {
        var midCache = new Dictionary<(int, int), int>();
        int MidIndex(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (midCache.TryGetValue(key, out int idx)) return idx;
            var pa = nodes[a]; var pb = nodes[b];
            Point mid = boundarySegments != null && boundarySegments.Count > 0
                ? Rs2dBoundaryGeometry.TryMidpointOnBoundary(pa, pb, boundarySegments)
                  ?? new Point((pa.X + pb.X) * 0.5, (pa.Y + pb.Y) * 0.5)
                : new Point((pa.X + pb.X) * 0.5, (pa.Y + pb.Y) * 0.5);
            nodes.Add(mid);
            int newIdx = nodes.Count - 1;
            midCache[key] = newIdx;
            return newIdx;
        }
        var output = new List<(int, int, int, int, int, int, int, int)>(quads.Count);
        foreach (var q in quads)
        {
            int m12 = MidIndex(q.Q1, q.Q2);
            int m23 = MidIndex(q.Q2, q.Q3);
            int m34 = MidIndex(q.Q3, q.Q4);
            int m41 = MidIndex(q.Q4, q.Q1);
            output.Add((q.Q1, q.Q2, q.Q3, q.Q4, m12, m23, m34, m41));
        }
        return output;
    }

    /// <summary>Insert midside nodes on the 3 edges of each triangle and return T6 corner+midside indices.</summary>
    public static List<(int n1, int n2, int n3, int n4, int n5, int n6)> UpgradeTrianglesToT6(
        List<Point> nodes,
        IReadOnlyList<TriRef> triangles)
    {
        var midCache = new Dictionary<(int, int), int>();
        int MidIndex(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (midCache.TryGetValue(key, out int idx)) return idx;
            var pa = nodes[a]; var pb = nodes[b];
            nodes.Add(new Point((pa.X + pb.X) * 0.5, (pa.Y + pb.Y) * 0.5));
            int newIdx = nodes.Count - 1;
            midCache[key] = newIdx;
            return newIdx;
        }
        var output = new List<(int, int, int, int, int, int)>(triangles.Count);
        foreach (var t in triangles)
        {
            int m12 = MidIndex(t.A, t.B);
            int m23 = MidIndex(t.B, t.C);
            int m31 = MidIndex(t.C, t.A);
            output.Add((t.A, t.B, t.C, m12, m23, m31));
        }
        return output;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Internal helpers
    // ──────────────────────────────────────────────────────────────────────

    private static void AddEdge(Dictionary<(int, int), List<(int triIdx, int opp)>> edgeMap, int a, int b, int triIdx, int opp)
    {
        var key = a < b ? (a, b) : (b, a);
        if (!edgeMap.TryGetValue(key, out var list))
        {
            list = new List<(int, int)>();
            edgeMap[key] = list;
        }
        list.Add((triIdx, opp));
    }

    /// <summary>
    /// Build a 4-corner quad in CCW order from two triangles sharing <paramref name="sharedEdge"/>.
    /// Falls back to <c>null</c> when either triangle is degenerate or the merged quad cannot be
    /// oriented CCW. Returns the quad in (q1, q2, q3, q4) corner order suitable for Q4 storage.
    /// </summary>
    private static (int q1, int q2, int q3, int q4)? MakeQuadFromPair(TriRef t1, TriRef t2, (int a, int b) sharedEdge)
    {
        int opp1 = OppositeVertex(t1, sharedEdge.a, sharedEdge.b);
        int opp2 = OppositeVertex(t2, sharedEdge.a, sharedEdge.b);
        if (opp1 < 0 || opp2 < 0) return null;
        return (opp1, sharedEdge.a, opp2, sharedEdge.b);
    }

    /// <summary>
    /// Try both edge-traversal directions and return the one that yields CCW orientation.
    /// </summary>
    private static (int q1, int q2, int q3, int q4) OrientCcw(IReadOnlyList<Point> nodes, (int q1, int q2, int q3, int q4) quad)
    {
        var p1 = nodes[quad.q1]; var p2 = nodes[quad.q2]; var p3 = nodes[quad.q3]; var p4 = nodes[quad.q4];
        double s = (p2.X - p1.X) * (p2.Y + p1.Y)
                 + (p3.X - p2.X) * (p3.Y + p2.Y)
                 + (p4.X - p3.X) * (p4.Y + p3.Y)
                 + (p1.X - p4.X) * (p1.Y + p4.Y);
        // 's' here is -2 * standardSignedArea; CCW means standardSignedArea > 0 i.e. s < 0.
        if (s < 0) return quad;
        return (quad.q1, quad.q4, quad.q3, quad.q2);
    }

    private static int OppositeVertex(TriRef t, int a, int b)
    {
        if (t.A != a && t.A != b) return t.A;
        if (t.B != a && t.B != b) return t.B;
        if (t.C != a && t.C != b) return t.C;
        return -1;
    }

    private static double TriArea(IReadOnlyList<Point> nodes, TriRef t)
    {
        var pa = nodes[t.A]; var pb = nodes[t.B]; var pc = nodes[t.C];
        return 0.5 * Math.Abs((pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y));
    }

    /// <summary>
    /// Validate a quad: convex, CCW (positive area), then return the smallest interior angle (deg),
    /// the aspect ratio (longest edge / shortest edge), and a skew metric.
    /// </summary>
    public static bool TryQuadMetrics(
        IReadOnlyList<Point> nodes,
        (int q1, int q2, int q3, int q4) quad,
        out double minAngleDeg,
        out double aspectRatio,
        out double skew)
        => QuadIsConvexCcw(nodes, quad, out minAngleDeg, out aspectRatio, out skew);

    private static bool QuadIsConvexCcw(
        IReadOnlyList<Point> nodes,
        (int q1, int q2, int q3, int q4) quad,
        out double minAngleDeg,
        out double aspectRatio,
        out double skew)
    {
        minAngleDeg = 0; aspectRatio = double.PositiveInfinity; skew = 1.0;
        var p1 = nodes[quad.q1]; var p2 = nodes[quad.q2]; var p3 = nodes[quad.q3]; var p4 = nodes[quad.q4];

        double signedArea = 0.5 * (
            (p2.X - p1.X) * (p2.Y + p1.Y) +
            (p3.X - p2.X) * (p3.Y + p2.Y) +
            (p4.X - p3.X) * (p4.Y + p3.Y) +
            (p1.X - p4.X) * (p1.Y + p4.Y));
        if (Math.Abs(signedArea) < 1e-14) return false;
        // Make CCW (positive signed area). Note: above formula uses shoelace with reversed sign,
        // so positive 'signedArea' means CW; we negate to interpret CCW area.
        double area = -signedArea;
        if (area <= 0) return false;

        // Convexity: cross products at each corner must have the same sign.
        double c1 = Cross(p1, p2, p3);
        double c2 = Cross(p2, p3, p4);
        double c3 = Cross(p3, p4, p1);
        double c4 = Cross(p4, p1, p2);
        if (!(c1 > 0 && c2 > 0 && c3 > 0 && c4 > 0) && !(c1 < 0 && c2 < 0 && c3 < 0 && c4 < 0))
            return false;

        // Interior angles
        double a1 = Angle(p4, p1, p2);
        double a2 = Angle(p1, p2, p3);
        double a3 = Angle(p2, p3, p4);
        double a4 = Angle(p3, p4, p1);
        double minA = Math.Min(Math.Min(a1, a2), Math.Min(a3, a4));
        double maxA = Math.Max(Math.Max(a1, a2), Math.Max(a3, a4));
        if (maxA >= 178.0) return false;

        double e12 = Dist(p1, p2);
        double e23 = Dist(p2, p3);
        double e34 = Dist(p3, p4);
        double e41 = Dist(p4, p1);
        double eMin = Math.Min(Math.Min(e12, e23), Math.Min(e34, e41));
        double eMax = Math.Max(Math.Max(e12, e23), Math.Max(e34, e41));
        if (eMin <= 0) return false;

        minAngleDeg = minA;
        aspectRatio = eMax / eMin;
        // Equiangular skew: deviation from 90°
        skew = (Math.Max(maxA - 90.0, 90.0 - minA)) / 90.0;
        return true;
    }

    private static double Cross(Point a, Point b, Point c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static double Dist(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Angle(Point prev, Point pivot, Point next)
    {
        double v1x = prev.X - pivot.X, v1y = prev.Y - pivot.Y;
        double v2x = next.X - pivot.X, v2y = next.Y - pivot.Y;
        double dot = v1x * v2x + v1y * v2y;
        double cross = v1x * v2y - v1y * v2x;
        double a = Math.Atan2(Math.Abs(cross), dot);
        return a * 180.0 / Math.PI;
    }
}
