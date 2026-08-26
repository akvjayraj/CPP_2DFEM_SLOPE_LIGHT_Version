using System;
using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Extracts the outer domain boundary loop from a FEM connectivity table
/// (same edge-counting logic as <see cref="P610T3DatImporter"/>).
/// </summary>
public static class MeshBoundaryGeometryExtractor
{
    public static bool TryExtractOuterBoundary(
        IReadOnlyList<(double x, double y)> coords,
        IReadOnlyList<int[]> connectivity,
        string meshType,
        bool smithEmb2dQ8LocalOrder,
        out List<int> boundaryNodeIds)
    {
        boundaryNodeIds = new List<int>();
        if (coords.Count < 3 || connectivity.Count == 0)
            return false;

        var edgeCount = new Dictionary<(int, int), int>();
        foreach (var el in connectivity)
        {
            foreach (var (a, b) in ElementBoundarySegments(meshType, el, smithEmb2dQ8LocalOrder))
                CountEdge(edgeCount, a, b);
        }

        var adj = new Dictionary<int, List<int>>();
        foreach (var kvp in edgeCount)
        {
            if (kvp.Value != 1) continue;
            var (a, b) = kvp.Key;
            if (!adj.ContainsKey(a)) adj[a] = new List<int>();
            if (!adj.ContainsKey(b)) adj[b] = new List<int>();
            adj[a].Add(b);
            adj[b].Add(a);
        }

        if (adj.Count == 0) return false;

        var loops = ExtractBoundaryLoops(adj);
        if (loops.Count == 0) return false;

        List<int> boundary = loops[0];
        double bestAbsArea = -1.0;
        foreach (var loop in loops)
        {
            double a = SignedArea(loop, coords);
            double absA = Math.Abs(a);
            if (absA > bestAbsArea)
            {
                bestAbsArea = absA;
                boundary = loop;
            }
        }

        if (SignedArea(boundary, coords) < 0)
            boundary.Reverse();

        boundaryNodeIds = boundary;
        return boundaryNodeIds.Count >= 3;
    }

    /// <summary>
    /// Merges collinear intermediate nodes on a closed outer-boundary walk from a .dat/mesh
    /// perimeter. Vertices with turn angle below <paramref name="minTurnDegrees"/> are removed
    /// until the ring is stable; consecutive duplicate coordinates are collapsed first.
    /// </summary>
    public static bool TryMergeCollinearBoundaryVertices(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords,
        out List<int> mergedNodeIds,
        double minTurnDegrees = 3.0)
    {
        mergedNodeIds = new List<int>();
        if (boundaryLoop == null || boundaryLoop.Count < 3)
            return false;

        var loop = boundaryLoop.ToList();
        CollapseDuplicateConsecutiveBoundaryNodes(loop, coords);
        if (loop.Count < 3)
            return false;

        bool changed = true;
        while (changed && loop.Count > 3)
        {
            changed = false;
            for (int i = 0; i < loop.Count; i++)
            {
                int prev = loop[(i - 1 + loop.Count) % loop.Count];
                int cur = loop[i];
                int next = loop[(i + 1) % loop.Count];
                if (TurningAngleDegrees(prev, cur, next, coords) < minTurnDegrees)
                {
                    loop.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }

        if (loop.Count < 3)
            return false;

        mergedNodeIds = loop;
        return true;
    }

    private static void CollapseDuplicateConsecutiveBoundaryNodes(
        List<int> loop,
        IReadOnlyList<(double x, double y)> coords)
    {
        if (loop.Count < 2)
            return;

        double coordEps = ComputeBoundaryCoordEpsilon(loop, coords);
        bool changed = true;
        while (changed && loop.Count >= 2)
        {
            changed = false;
            for (int i = 0; i < loop.Count; i++)
            {
                int cur = loop[i];
                int next = loop[(i + 1) % loop.Count];
                if (cur == next)
                {
                    loop.RemoveAt(i);
                    changed = true;
                    break;
                }

                if (cur < 1 || cur > coords.Count || next < 1 || next > coords.Count)
                    continue;

                var (cx, cy) = coords[cur - 1];
                var (nx, ny) = coords[next - 1];
                double dx = nx - cx;
                double dy = ny - cy;
                if (dx * dx + dy * dy <= coordEps * coordEps)
                {
                    loop.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }
    }

    private static double ComputeBoundaryCoordEpsilon(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords)
    {
        if (TryGetBoundaryExtents(boundaryLoop, coords, out _, out _, out _, out _, out double xRange, out double yRange))
            return Math.Max(1e-9, Math.Max(xRange, yRange) * 1e-9);

        return 1e-9;
    }

    /// <summary>
    /// Reduces a dense mesh outer-boundary loop to macro corner nodes (e.g. 6 embankment corners)
    /// while preserving boundary walk order for L1..Ln segment creation.
    /// </summary>
    public static bool TryExtractMacroCornerBoundary(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords,
        out List<int> cornerNodeIds)
    {
        cornerNodeIds = new List<int>();
        if (boundaryLoop == null || boundaryLoop.Count < 4)
            return false;

        double medianEdge = MedianBoundaryEdgeLength(boundaryLoop, coords);
        if (medianEdge <= 0)
            medianEdge = 0.01;

        bool embankmentProfile = LooksLikeEmbankmentProfile(boundaryLoop, coords);

        // Embankment profiles: always prefer feature-corner pickers over Douglas–Peucker bbox corners.
        if (embankmentProfile)
        {
            if (TryExtractEmbankmentCornersBySegmentTransitions(boundaryLoop, coords, out var segCorners)
                && segCorners.Count == 6)
            {
                cornerNodeIds = segCorners;
                return true;
            }

            if (TryExtractEmbankmentCornersByGeometry(boundaryLoop, coords, out var geomCorners)
                && geomCorners.Count == 6)
            {
                cornerNodeIds = geomCorners;
                return true;
            }
        }

        // Non-embankment (and embankment fallback): Douglas–Peucker polyline simplification.
        if (TryExtractCornersByDouglasPeucker(boundaryLoop, coords, medianEdge, out cornerNodeIds))
            return true;

        if (embankmentProfile
            && TryExtractEmbankmentCornersByGeometry(boundaryLoop, coords, out cornerNodeIds))
            return cornerNodeIds.Count >= 4;

        // Smith p64 deck ids when present on the boundary loop.
        if (TryExtractSmithEmbankmentCorners(boundaryLoop, out cornerNodeIds))
            return cornerNodeIds.Count >= 4;

        // Last resort: turning-angle filter with adaptive threshold.
        return TryExtractCornersByTurningAngle(boundaryLoop, coords, out cornerNodeIds)
               && cornerNodeIds.Count >= 4;
    }

    private static bool TryExtractCornersByDouglasPeucker(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords,
        double medianEdge,
        out List<int> cornerNodeIds)
    {
        cornerNodeIds = new List<int>();
        double[] scaleFactors = { 0.45, 0.65, 0.30, 0.85, 0.20 };

        foreach (double scale in scaleFactors)
        {
            var simplified = SimplifyClosedBoundaryDouglasPeucker(boundaryLoop, coords, medianEdge * scale);
            if (simplified.Count is >= 4 and <= 12)
            {
                cornerNodeIds = simplified;
                return true;
            }
        }

        cornerNodeIds.Clear();
        return false;
    }

    /// <summary>Douglas–Peucker on a closed boundary ring; returns node ids in walk order.</summary>
    private static List<int> SimplifyClosedBoundaryDouglasPeucker(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords,
        double tolerance)
    {
        int n = boundaryLoop.Count;
        if (n < 4 || tolerance <= 0)
            return boundaryLoop.ToList();

        int startIdx = FindStableLoopStartIndex(boundaryLoop, coords);
        var rotated = new List<int>(n);
        for (int k = 0; k < n; k++)
            rotated.Add(boundaryLoop[(startIdx + k) % n]);

        var keep = new HashSet<int>();
        DouglasPeuckerClosedRecursive(rotated, coords, 0, n - 1, tolerance, keep);

        var simplified = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (keep.Contains(i))
                simplified.Add(rotated[i]);
        }

        return simplified.Count >= 3 ? simplified : boundaryLoop.ToList();
    }

    private static void DouglasPeuckerClosedRecursive(
        IReadOnlyList<int> nodeIds,
        IReadOnlyList<(double x, double y)> coords,
        int iStart,
        int iEnd,
        double tolerance,
        HashSet<int> keep)
    {
        keep.Add(iStart);
        keep.Add(iEnd);

        double maxDist = 0;
        int maxIdx = -1;
        for (int i = iStart + 1; i < iEnd; i++)
        {
            double d = PerpendicularDistanceToSegment(nodeIds[i], nodeIds[iStart], nodeIds[iEnd], coords);
            if (d > maxDist)
            {
                maxDist = d;
                maxIdx = i;
            }
        }

        if (maxIdx >= 0 && maxDist > tolerance)
        {
            DouglasPeuckerClosedRecursive(nodeIds, coords, iStart, maxIdx, tolerance, keep);
            DouglasPeuckerClosedRecursive(nodeIds, coords, maxIdx, iEnd, tolerance, keep);
        }
    }

    private static int FindStableLoopStartIndex(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords)
    {
        int best = 0;
        double bestScore = double.PositiveInfinity;
        for (int i = 0; i < boundaryLoop.Count; i++)
        {
            int id = boundaryLoop[i];
            if (id < 1 || id > coords.Count) continue;
            var (x, y) = coords[id - 1];
            double score = x + y * 0.001;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    private static double PerpendicularDistanceToSegment(
        int pointId, int segStartId, int segEndId,
        IReadOnlyList<(double x, double y)> coords)
    {
        if (pointId < 1 || pointId > coords.Count
            || segStartId < 1 || segStartId > coords.Count
            || segEndId < 1 || segEndId > coords.Count)
            return 0;

        var (px, py) = coords[pointId - 1];
        var (ax, ay) = coords[segStartId - 1];
        var (bx, by) = coords[segEndId - 1];
        double dx = bx - ax, dy = by - ay;
        double len2 = dx * dx + dy * dy;
        if (len2 <= 1e-18)
        {
            double ex = px - ax, ey = py - ay;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        double t = ((px - ax) * dx + (py - ay) * dy) / len2;
        t = Math.Clamp(t, 0, 1);
        double projX = ax + t * dx;
        double projY = ay + t * dy;
        double ox = px - projX, oy = py - projY;
        return Math.Sqrt(ox * ox + oy * oy);
    }

    private static double MedianBoundaryEdgeLength(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords)
    {
        var lengths = new List<double>();
        int n = boundaryLoop.Count;
        for (int i = 0; i < n; i++)
        {
            int a = boundaryLoop[i];
            int b = boundaryLoop[(i + 1) % n];
            if (a < 1 || a > coords.Count || b < 1 || b > coords.Count) continue;
            var (ax, ay) = coords[a - 1];
            var (bx, by) = coords[b - 1];
            lengths.Add(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)));
        }

        if (lengths.Count == 0) return 0;
        lengths.Sort();
        return lengths[lengths.Count / 2];
    }

    private static bool LooksLikeEmbankmentProfile(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords)
    {
        if (boundaryLoop.Count < 8) return false;

        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        foreach (int id in boundaryLoop)
        {
            if (id < 1 || id > coords.Count) continue;
            var (x, y) = coords[id - 1];
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }

        double xRange = xMax - xMin;
        double yRange = yMax - yMin;
        if (xRange <= 0 || yRange <= 0) return false;

        double eps = Math.Max(1e-6, Math.Max(xRange, yRange) * 1e-4);
        double leftMaxY = double.NegativeInfinity;
        double rightMaxY = double.NegativeInfinity;
        foreach (int id in boundaryLoop)
        {
            if (id < 1 || id > coords.Count) continue;
            var (x, y) = coords[id - 1];
            if (Math.Abs(x - xMin) <= eps && y > leftMaxY) leftMaxY = y;
            if (Math.Abs(x - xMax) <= eps && y > rightMaxY) rightMaxY = y;
        }

        return leftMaxY > rightMaxY + eps * 2 && rightMaxY > yMin + eps;
    }

    /// <summary>Smith p64 embankment deck corner node ids (CCW from bottom-left).</summary>
    private static readonly int[] SmithEmbankmentCornerIds = { 31, 1, 941, 961, 1131, 1141 };

    private enum BoundarySegmentKind { Horizontal, Vertical, Slope }

    /// <summary>
    /// Finds embankment macro corners by H/V/slope segment transitions on the boundary walk.
    /// </summary>
    private static bool TryExtractEmbankmentCornersBySegmentTransitions(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords,
        out List<int> cornerNodeIds)
    {
        cornerNodeIds = new List<int>();
        int n = boundaryLoop.Count;
        if (n < 8)
            return false;

        if (!TryGetBoundaryExtents(boundaryLoop, coords, out _, out _, out _, out _, out double xRange, out double yRange))
            return false;

        double flatTol = Math.Max(1e-6, Math.Max(xRange, yRange) * 0.02);
        var edgeKinds = new BoundarySegmentKind[n];
        for (int i = 0; i < n; i++)
        {
            int a = boundaryLoop[i];
            int b = boundaryLoop[(i + 1) % n];
            edgeKinds[i] = ClassifyBoundaryEdge(a, b, coords, flatTol);
        }

        // Smooth micro-segments: replace short runs sandwiched by equal neighbors.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                int next = (i + 1) % n;
                if (edgeKinds[prev] == edgeKinds[next] && edgeKinds[i] != edgeKinds[prev])
                    edgeKinds[i] = edgeKinds[prev];
            }
        }

        var cornerSet = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            BoundarySegmentKind incoming = edgeKinds[(i - 1 + n) % n];
            BoundarySegmentKind outgoing = edgeKinds[i];
            if (incoming != outgoing)
                cornerSet.Add(boundaryLoop[i]);
        }

        if (cornerSet.Count < 4)
            return false;

        foreach (int id in boundaryLoop)
        {
            if (cornerSet.Contains(id))
                cornerNodeIds.Add(id);
        }

        return cornerNodeIds.Count >= 4;
    }

    private static BoundarySegmentKind ClassifyBoundaryEdge(
        int fromId,
        int toId,
        IReadOnlyList<(double x, double y)> coords,
        double flatTol)
    {
        if (fromId < 1 || fromId > coords.Count || toId < 1 || toId > coords.Count)
            return BoundarySegmentKind.Slope;

        var (x1, y1) = coords[fromId - 1];
        var (x2, y2) = coords[toId - 1];
        double dx = x2 - x1;
        double dy = y2 - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= flatTol)
            return BoundarySegmentKind.Horizontal;

        double absDx = Math.Abs(dx);
        double absDy = Math.Abs(dy);
        if (absDy <= flatTol || absDy / len <= 0.12)
            return BoundarySegmentKind.Horizontal;
        if (absDx <= flatTol || absDx / len <= 0.12)
            return BoundarySegmentKind.Vertical;
        return BoundarySegmentKind.Slope;
    }

    private static bool TryGetBoundaryExtents(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords,
        out double xMin,
        out double xMax,
        out double yMin,
        out double yMax,
        out double xRange,
        out double yRange)
    {
        xMin = double.PositiveInfinity;
        xMax = double.NegativeInfinity;
        yMin = double.PositiveInfinity;
        yMax = double.NegativeInfinity;

        foreach (int id in boundaryLoop)
        {
            if (id < 1 || id > coords.Count) continue;
            var (x, y) = coords[id - 1];
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }

        xRange = xMax - xMin;
        yRange = yMax - yMin;
        return xRange > 0 && yRange > 0;
    }

    private static bool TryExtractSmithEmbankmentCorners(
        IReadOnlyList<int> boundaryLoop,
        out List<int> cornerNodeIds)
    {
        cornerNodeIds = new List<int>();
        var smithSet = new HashSet<int>(SmithEmbankmentCornerIds);
        foreach (int id in boundaryLoop)
        {
            if (smithSet.Contains(id))
                cornerNodeIds.Add(id);
        }

        return cornerNodeIds.Count >= SmithEmbankmentCornerIds.Length - 1;
    }

    /// <summary>
    /// Finds embankment macro corners by axis-aligned extrema on the boundary loop,
    /// then returns them in boundary walk order.
    /// </summary>
    private static bool TryExtractEmbankmentCornersByGeometry(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords,
        out List<int> cornerNodeIds)
    {
        cornerNodeIds = new List<int>();
        var boundaryNodes = new List<(int id, double x, double y)>();
        foreach (int id in boundaryLoop)
        {
            if (id < 1 || id > coords.Count) continue;
            var (x, y) = coords[id - 1];
            boundaryNodes.Add((id, x, y));
        }

        if (boundaryNodes.Count < 4)
            return false;

        double xMin = boundaryNodes.Min(n => n.x);
        double xMax = boundaryNodes.Max(n => n.x);
        double yMin = boundaryNodes.Min(n => n.y);
        double yMax = boundaryNodes.Max(n => n.y);
        double xRange = xMax - xMin;
        double yRange = yMax - yMin;
        if (xRange <= 0 || yRange <= 0)
            return false;

        double eps = Math.Max(1e-6, Math.Max(xRange, yRange) * 1e-4);

        int leftToe = NearestCornerOnBoundary(boundaryNodes, xMin, yMin, eps);
        int leftCrest = NearestCornerOnBoundary(boundaryNodes, xMin, yMax, eps);
        int rightToe = NearestCornerOnBoundary(boundaryNodes, xMax, yMin, eps);

        var interiorYs = boundaryNodes
            .Select(n => n.y)
            .Where(y => y > yMin + eps && y < yMax - eps)
            .GroupBy(y => Math.Round(y / eps))
            .OrderByDescending(g => g.Count())
            .Select(g => g.Average())
            .ToList();

        int crestBreak = -1;
        int slopeBreak = -1;
        int rightBench = -1;
        if (interiorYs.Count > 0)
        {
            double benchY = interiorYs[0];
            rightBench = NearestCornerOnBoundary(boundaryNodes, xMax, benchY, eps * 2);
            slopeBreak = boundaryNodes
                .Where(n => Math.Abs(n.y - benchY) <= eps * 2 && n.x < xMax - eps && n.x > xMin + eps)
                .OrderBy(n => n.x)
                .Select(n => n.id)
                .FirstOrDefault();
            crestBreak = boundaryNodes
                .Where(n => Math.Abs(n.y - yMax) <= eps * 2 && n.x > xMin + eps && n.x < xMax - eps)
                .OrderByDescending(n => n.x)
                .Select(n => n.id)
                .FirstOrDefault();
        }

        if (crestBreak <= 0)
        {
            crestBreak = boundaryNodes
                .Where(n => Math.Abs(n.y - yMax) <= eps * 2 && n.x > xMin + eps)
                .OrderByDescending(n => n.x)
                .Select(n => n.id)
                .FirstOrDefault();
        }

        var candidateSet = new HashSet<int>();
        foreach (int id in new[] { leftToe, leftCrest, crestBreak, slopeBreak, rightBench, rightToe })
        {
            if (id > 0)
                candidateSet.Add(id);
        }

        if (candidateSet.Count < 4)
            return false;

        foreach (int id in boundaryLoop)
        {
            if (candidateSet.Contains(id))
                cornerNodeIds.Add(id);
        }

        return cornerNodeIds.Count >= 4;
    }

    /// <summary>Nearest boundary node to a corner target; both x and y must be within band.</summary>
    private static int NearestCornerOnBoundary(
        IReadOnlyList<(int id, double x, double y)> boundaryNodes,
        double targetX,
        double targetY,
        double band)
    {
        var inBand = boundaryNodes
            .Where(n => Math.Abs(n.x - targetX) <= band && Math.Abs(n.y - targetY) <= band)
            .ToList();
        if (inBand.Count == 0)
        {
            inBand = boundaryNodes
                .Where(n => Math.Abs(n.x - targetX) <= band || Math.Abs(n.y - targetY) <= band)
                .ToList();
        }

        if (inBand.Count == 0)
            inBand = boundaryNodes.ToList();

        return inBand
            .OrderBy(n => (n.x - targetX) * (n.x - targetX) + (n.y - targetY) * (n.y - targetY))
            .Select(n => n.id)
            .FirstOrDefault();
    }

    private static bool TryExtractCornersByTurningAngle(
        IReadOnlyList<int> boundaryLoop,
        IReadOnlyList<(double x, double y)> coords,
        out List<int> cornerNodeIds)
    {
        cornerNodeIds = new List<int>();
        int n = boundaryLoop.Count;
        if (n < 4)
            return false;

        double minTurnDeg = 8.0;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            cornerNodeIds.Clear();
            for (int i = 0; i < n; i++)
            {
                int prev = boundaryLoop[(i - 1 + n) % n];
                int cur = boundaryLoop[i];
                int next = boundaryLoop[(i + 1) % n];
                if (TurningAngleDegrees(prev, cur, next, coords) >= minTurnDeg)
                    cornerNodeIds.Add(cur);
            }

            if (cornerNodeIds.Count is >= 4 and <= 12)
                return true;

            minTurnDeg *= 0.5;
        }

        cornerNodeIds.Clear();
        return false;
    }

    private static double TurningAngleDegrees(
        int prevId, int curId, int nextId,
        IReadOnlyList<(double x, double y)> coords)
    {
        if (prevId < 1 || prevId > coords.Count
            || curId < 1 || curId > coords.Count
            || nextId < 1 || nextId > coords.Count)
            return 0;

        var (x0, y0) = coords[prevId - 1];
        var (x1, y1) = coords[curId - 1];
        var (x2, y2) = coords[nextId - 1];
        double v1x = x1 - x0, v1y = y1 - y0;
        double v2x = x2 - x1, v2y = y2 - y1;
        double len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
        double len2 = Math.Sqrt(v2x * v2x + v2y * v2y);
        if (len1 <= 1e-12 || len2 <= 1e-12)
            return 0;

        double cross = v1x * v2y - v1y * v2x;
        double dot = v1x * v2x + v1y * v2y;
        double turnRad = Math.Atan2(Math.Abs(cross), dot);
        return turnRad * 180.0 / Math.PI;
    }

    private static double SignedArea(List<int> loop, IReadOnlyList<(double x, double y)> coords)
    {
        double a = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            int j = (i + 1) % loop.Count;
            int ni = loop[i];
            int nj = loop[j];
            if (ni < 1 || ni > coords.Count || nj < 1 || nj > coords.Count)
                return 0;
            var (xi, yi) = coords[ni - 1];
            var (xj, yj) = coords[nj - 1];
            a += (xi * yj - xj * yi);
        }
        return a;
    }

    /// <summary>Traces closed loops along degree-2 boundary adjacency (one neighbor choice per step).</summary>
    private static List<List<int>> ExtractBoundaryLoops(Dictionary<int, List<int>> adj)
    {
        var usedUndirected = new HashSet<(int, int)>();
        var loops = new List<List<int>>();

        foreach (int start in adj.Keys.OrderBy(k => k))
        {
            if (adj[start].Count == 0) continue;
            int firstNext = adj[start][0];
            var seed = UndirectedEdge(start, firstNext);
            if (usedUndirected.Contains(seed)) continue;

            var loop = new List<int> { start };
            int prev = start;
            int cur = firstNext;
            usedUndirected.Add(seed);

            while (cur != start && loop.Count <= adj.Count + 2)
            {
                loop.Add(cur);
                int next = -1;
                foreach (int nb in adj[cur])
                {
                    if (nb == prev) continue;
                    var e = UndirectedEdge(cur, nb);
                    if (!usedUndirected.Contains(e))
                    {
                        next = nb;
                        usedUndirected.Add(e);
                        break;
                    }
                }
                if (next < 0) break;
                prev = cur;
                cur = next;
            }

            if (loop.Count >= 3 && cur == start)
                loops.Add(loop);
        }

        return loops;
    }

    private static (int, int) UndirectedEdge(int a, int b)
        => a < b ? (a, b) : (b, a);

    private static void CountEdge(Dictionary<(int, int), int> edgeCount, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        edgeCount.TryGetValue(key, out int c);
        edgeCount[key] = c + 1;
    }

    public static IEnumerable<(int A, int B)> ElementBoundarySegments(
        string meshType,
        int[] nodes,
        bool smithEmb2dQ8LocalOrder = false)
    {
        if (nodes.Length < 3) yield break;

        if (string.Equals(meshType, "Q8", StringComparison.OrdinalIgnoreCase) && nodes.Length >= 8)
        {
            if (smithEmb2dQ8LocalOrder)
            {
                for (int k = 0; k < 8; k++)
                    yield return (nodes[k], nodes[(k + 1) % 8]);
            }
            else
            {
                yield return (nodes[0], nodes[4]);
                yield return (nodes[4], nodes[1]);
                yield return (nodes[1], nodes[5]);
                yield return (nodes[5], nodes[2]);
                yield return (nodes[2], nodes[6]);
                yield return (nodes[6], nodes[3]);
                yield return (nodes[3], nodes[7]);
                yield return (nodes[7], nodes[0]);
            }
            yield break;
        }

        if (string.Equals(meshType, "T6", StringComparison.OrdinalIgnoreCase) && nodes.Length >= 6)
        {
            yield return (nodes[0], nodes[3]);
            yield return (nodes[3], nodes[1]);
            yield return (nodes[1], nodes[4]);
            yield return (nodes[4], nodes[2]);
            yield return (nodes[2], nodes[5]);
            yield return (nodes[5], nodes[0]);
            yield break;
        }

        if (string.Equals(meshType, "Q4", StringComparison.OrdinalIgnoreCase) && nodes.Length >= 4)
        {
            yield return (nodes[0], nodes[1]);
            yield return (nodes[1], nodes[2]);
            yield return (nodes[2], nodes[3]);
            yield return (nodes[3], nodes[0]);
            yield break;
        }

        yield return (nodes[0], nodes[1]);
        yield return (nodes[1], nodes[2]);
        yield return (nodes[2], nodes[0]);
    }
}
