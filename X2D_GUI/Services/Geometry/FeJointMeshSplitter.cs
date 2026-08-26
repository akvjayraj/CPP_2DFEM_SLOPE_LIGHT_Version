using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Geometry;

namespace GeotechSlopeApp.Services.Geometry;

public sealed class FeJointMeshSplitResult
{
    public List<MeshInformationRow> Elements { get; } = new();
    public Dictionary<int, Point> Nodes { get; } = new();
    public List<FeJointInterfaceElement> Interfaces { get; } = new();
    public List<(int Id, Point Location)> AddedNodes { get; } = new();
}

/// <summary>
/// Duplicates joint-trace nodes so the two sides can slip, remaps plus-side
/// continuum connectivity, and emits 4-node Goodman interface elements.
/// </summary>
public static class FeJointMeshSplitter
{
    public static FeJointMeshSplitResult Split(
        IReadOnlyList<MeshInformationRow> elements,
        IReadOnlyDictionary<int, Point> nodes,
        IEnumerable<GeologicalFeature>? features,
        IEnumerable<FeJointPropertySet>? propertySets,
        double tolerance = 1e-6)
    {
        var result = new FeJointMeshSplitResult();
        foreach (var kv in nodes)
            result.Nodes[kv.Key] = kv.Value;
        foreach (var el in elements)
            result.Elements.Add(CloneRow(el));

        var enabled = (features ?? Enumerable.Empty<GeologicalFeature>())
            .Where(f => f.IsEnabled && f.Vertices.Count >= 2)
            .ToList();
        if (enabled.Count == 0 || result.Elements.Count == 0)
            return result;

        var sets = (propertySets ?? Enumerable.Empty<FeJointPropertySet>())
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => g.First());

        double tol = Math.Max(tolerance, 1e-9);
        var jointEdges = new List<JointEdge>();
        var jointNodeIds = new HashSet<int>();
        var closedEndIds = new HashSet<int>();

        foreach (var feature in enabled)
        {
            var segs = FeatureSegments(feature);
            if (segs.Count == 0)
                continue;

            var onJoint = new HashSet<int>();
            foreach (var (id, pt) in result.Nodes)
            {
                if (DistanceToPolyline(pt, segs) <= tol)
                    onJoint.Add(id);
            }

            MarkClosedEnds(feature, result.Nodes, onJoint, tol, closedEndIds);

            foreach (var el in result.Elements)
            {
                foreach (var (a, b, mid) in el.EnumerateCornerEdgesWithMid())
                {
                    if (a <= 0 || b <= 0 || !onJoint.Contains(a) || !onJoint.Contains(b))
                        continue;
                    if (mid > 0 && !onJoint.Contains(mid))
                        continue;
                    if (!result.Nodes.TryGetValue(a, out var pa) || !result.Nodes.TryGetValue(b, out var pb))
                        continue;
                    var tangent = FeatureTangentAt(segs, Mid(pa, pb));
                    jointEdges.Add(new JointEdge(feature, a, b, mid, tangent));
                    jointNodeIds.Add(a);
                    jointNodeIds.Add(b);
                    if (mid > 0)
                        jointNodeIds.Add(mid);
                }
            }
        }

        if (jointEdges.Count == 0)
            return result;

        int nextId = result.Nodes.Keys.DefaultIfEmpty(0).Max() + 1;
        var plusOf = new Dictionary<int, int>();
        foreach (var id in jointNodeIds)
        {
            if (closedEndIds.Contains(id))
            {
                plusOf[id] = id;
                continue;
            }

            var pt = result.Nodes[id];
            int dup = nextId++;
            result.Nodes[dup] = pt;
            result.AddedNodes.Add((dup, pt));
            plusOf[id] = dup;
        }

        for (int ei = 0; ei < result.Elements.Count; ei++)
        {
            var el = result.Elements[ei];
            if (!TouchesJoint(el, jointNodeIds))
                continue;
            if (!IsPlusSide(el, result.Nodes, jointEdges, tol))
                continue;
            RemapRow(el, plusOf);
        }

        var emitted = new HashSet<(int, int, int, int)>();
        foreach (var edge in jointEdges)
        {
            void Emit(int a, int b)
            {
                int n1 = a;
                int n2 = b;
                int n3 = plusOf.TryGetValue(b, out var pb) ? pb : b;
                int n4 = plusOf.TryGetValue(a, out var pa) ? pa : a;
                var key = (n1, n2, n3, n4);
                if (!emitted.Add(key))
                    return;
                if (n1 == n4 && n2 == n3)
                    return;
                sets.TryGetValue(edge.Feature.PropertySetId ?? int.MinValue, out var set);
                result.Interfaces.Add(BuildInterface(n1, n2, n3, n4, edge.Feature, set));
            }

            if (edge.Mid > 0)
            {
                Emit(edge.A, edge.Mid);
                Emit(edge.Mid, edge.B);
            }
            else
            {
                Emit(edge.A, edge.B);
            }
        }

        return result;
    }

    /// <summary>
    /// True when joint-trace nodes already exist as coincident pairs (mesh was split
    /// earlier, e.g. a loaded .xfem). Calling <see cref="Split"/> again would duplicate
    /// those nodes a second time.
    /// </summary>
    public static bool LooksAlreadySplit(
        IReadOnlyDictionary<int, Point> nodes,
        IEnumerable<GeologicalFeature>? features,
        double tolerance = 1e-6)
    {
        var enabled = (features ?? Enumerable.Empty<GeologicalFeature>())
            .Where(f => f.IsEnabled && f.Vertices.Count >= 2)
            .ToList();
        if (enabled.Count == 0 || nodes.Count == 0)
            return false;

        double tol = Math.Max(tolerance, 1e-9);
        var buckets = new Dictionary<(long X, long Y), int>();
        foreach (var feature in enabled)
        {
            var segs = FeatureSegments(feature);
            if (segs.Count == 0)
                continue;
            foreach (var (id, pt) in nodes)
            {
                if (DistanceToPolyline(pt, segs) > tol)
                    continue;
                var key = Quantize(pt, tol);
                buckets[key] = buckets.GetValueOrDefault(key) + 1;
                if (buckets[key] >= 2)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rebuild Goodman interfaces from an already-split mesh (coincident node pairs
    /// along each joint) without remapping continuum connectivity.
    /// </summary>
    public static List<FeJointInterfaceElement> RecoverInterfaces(
        IReadOnlyList<MeshInformationRow> elements,
        IReadOnlyDictionary<int, Point> nodes,
        IEnumerable<GeologicalFeature>? features,
        IEnumerable<FeJointPropertySet>? propertySets,
        double tolerance = 1e-6)
    {
        var recovered = new List<FeJointInterfaceElement>();
        var enabled = (features ?? Enumerable.Empty<GeologicalFeature>())
            .Where(f => f.IsEnabled && f.Vertices.Count >= 2)
            .ToList();
        if (enabled.Count == 0 || elements.Count == 0)
            return recovered;

        var sets = (propertySets ?? Enumerable.Empty<FeJointPropertySet>())
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => g.First());
        double tol = Math.Max(tolerance, 1e-9);
        var emitted = new HashSet<(int, int, int, int)>();

        foreach (var feature in enabled)
        {
            var segs = FeatureSegments(feature);
            if (segs.Count == 0)
                continue;

            var onJoint = new HashSet<int>();
            foreach (var (id, pt) in nodes)
            {
                if (DistanceToPolyline(pt, segs) <= tol)
                    onJoint.Add(id);
            }

            var groups = new Dictionary<(long X, long Y), List<int>>();
            foreach (var id in onJoint)
            {
                var key = Quantize(nodes[id], tol);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<int>();
                list.Add(id);
            }

            var minusOf = new Dictionary<int, int>();
            var plusOf = new Dictionary<int, int>();
            foreach (var pair in groups.Values)
            {
                if (pair.Count < 2)
                    continue;
                int minus = 0, plus = 0;
                foreach (var id in pair)
                {
                    bool isPlus = IsNodeOnPlusSide(id, elements, nodes, segs, onJoint, tol);
                    if (isPlus) plus = plus == 0 ? id : plus;
                    else minus = minus == 0 ? id : minus;
                }
                if (minus == 0 || plus == 0 || minus == plus)
                    continue;
                minusOf[plus] = minus;
                plusOf[minus] = plus;
            }

            if (plusOf.Count == 0)
                continue;

            sets.TryGetValue(feature.PropertySetId ?? int.MinValue, out var set);
            foreach (var el in elements)
            {
                var ids = new HashSet<int>(EnumerateAllNodeIds(el));
                if (!ids.Any(plusOf.ContainsKey))
                    continue;
                foreach (var (a, b, _) in el.EnumerateCornerEdgesWithMid())
                {
                    if (a <= 0 || b <= 0 || !plusOf.ContainsKey(a) || !plusOf.ContainsKey(b))
                        continue;
                    int n1 = a, n2 = b, n3 = plusOf[b], n4 = plusOf[a];
                    var key = (n1, n2, n3, n4);
                    if (!emitted.Add(key))
                        continue;
                    recovered.Add(BuildInterface(n1, n2, n3, n4, feature, set));
                }
            }
        }

        return recovered;
    }

    private static (long X, long Y) Quantize(Point p, double tol)
    {
        double s = 1.0 / tol;
        return ((long)Math.Round(p.X * s), (long)Math.Round(p.Y * s));
    }

    private static bool IsNodeOnPlusSide(
        int nodeId,
        IReadOnlyList<MeshInformationRow> elements,
        IReadOnlyDictionary<int, Point> nodes,
        List<((double X, double Y) A, (double X, double Y) B)> segs,
        HashSet<int> onJoint,
        double tol)
    {
        if (!nodes.TryGetValue(nodeId, out var jp))
            return false;
        var tangent = FeatureTangentAt(segs, jp);
        foreach (var el in elements)
        {
            if (!EnumerateAllNodeIds(el).Contains(nodeId))
                continue;
            if (!TryCentroid(el, nodes, out var cx, out var cy))
                continue;
            double tx = tangent.X, ty = tangent.Y;
            if (Math.Abs(tx) + Math.Abs(ty) < 1e-15)
                continue;
            double cross = (cx - jp.X) * ty - (cy - jp.Y) * tx;
            if (Math.Abs(cross) > tol * tol)
                return cross < 0;
        }

        return false;
    }

    public static Dictionary<int, Point> CollectNodesFromRows(IEnumerable<MeshInformationRow> rows)
    {
        var nodes = new Dictionary<int, Point>();
        foreach (var row in rows)
        {
            void Add(int id, double x, double y)
            {
                if (id > 0 && !nodes.ContainsKey(id))
                    nodes[id] = new Point(x, y);
            }

            Add(row.Node1, row.N1X, row.N1Y);
            Add(row.Node2, row.N2X, row.N2Y);
            Add(row.Node3, row.N3X, row.N3Y);
            Add(row.Node4, row.N4X, row.N4Y);
            Add(row.Node5, row.N5X, row.N5Y);
            Add(row.Node6, row.N6X, row.N6Y);
            Add(row.Node7, row.N7X, row.N7Y);
            Add(row.Node8, row.N8X, row.N8Y);
        }

        return nodes;
    }

    private readonly record struct JointEdge(
        GeologicalFeature Feature,
        int A,
        int B,
        int Mid,
        (double X, double Y) Tangent);

    private static List<((double X, double Y) A, (double X, double Y) B)> FeatureSegments(GeologicalFeature feature)
    {
        var segs = new List<((double, double), (double, double))>();
        for (int i = 0; i + 1 < feature.Vertices.Count; i++)
        {
            var a = feature.Vertices[i];
            var b = feature.Vertices[i + 1];
            if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < 1e-15)
                continue;
            segs.Add((a, b));
        }

        return segs;
    }

    private static void MarkClosedEnds(
        GeologicalFeature feature,
        IReadOnlyDictionary<int, Point> nodes,
        HashSet<int> onJoint,
        double tol,
        HashSet<int> closedEndIds)
    {
        bool closeFirst = feature.EndCondition is FeJointEndCondition.BothClosed
            or FeJointEndCondition.FirstClosedLastOpen;
        bool closeLast = feature.EndCondition is FeJointEndCondition.BothClosed
            or FeJointEndCondition.FirstOpenLastClosed;
        var first = feature.Vertices[0];
        var last = feature.Vertices[^1];
        foreach (var id in onJoint)
        {
            var p = nodes[id];
            if (closeFirst && Dist(p, first) <= tol)
                closedEndIds.Add(id);
            if (closeLast && Dist(p, last) <= tol)
                closedEndIds.Add(id);
        }
    }

    private static bool TouchesJoint(MeshInformationRow el, HashSet<int> jointNodeIds)
        => EnumerateAllNodeIds(el).Any(jointNodeIds.Contains);

    private static bool IsPlusSide(
        MeshInformationRow el,
        IReadOnlyDictionary<int, Point> nodes,
        IReadOnlyList<JointEdge> edges,
        double tol)
    {
        if (!TryCentroid(el, nodes, out var cx, out var cy))
            return false;

        foreach (var edge in edges)
        {
            if (!RowUsesEdge(el, edge))
                continue;
            if (!nodes.TryGetValue(edge.A, out var pa) || !nodes.TryGetValue(edge.B, out var pb))
                continue;
            var mid = Mid(pa, pb);
            double tx = edge.Tangent.X, ty = edge.Tangent.Y;
            if (Math.Abs(tx) + Math.Abs(ty) < 1e-15)
            {
                tx = pb.X - pa.X;
                ty = pb.Y - pa.Y;
            }

            double cross = (cx - mid.X) * ty - (cy - mid.Y) * tx;
            if (Math.Abs(cross) > tol * tol)
                return cross < 0;
        }

        return false;
    }

    private static bool RowUsesEdge(MeshInformationRow el, JointEdge edge)
    {
        var ids = new HashSet<int>(EnumerateAllNodeIds(el));
        if (!ids.Contains(edge.A) || !ids.Contains(edge.B))
            return false;
        if (edge.Mid > 0 && !ids.Contains(edge.Mid))
            return false;
        return true;
    }

    private static IEnumerable<int> EnumerateAllNodeIds(MeshInformationRow el)
    {
        if (el.Node1 > 0) yield return el.Node1;
        if (el.Node2 > 0) yield return el.Node2;
        if (el.Node3 > 0) yield return el.Node3;
        if (el.Node4 > 0) yield return el.Node4;
        if (el.Node5 > 0) yield return el.Node5;
        if (el.Node6 > 0) yield return el.Node6;
        if (el.Node7 > 0) yield return el.Node7;
        if (el.Node8 > 0) yield return el.Node8;
    }

    private static void RemapRow(MeshInformationRow el, Dictionary<int, int> plusOf)
    {
        int Map(int id) => id > 0 && plusOf.TryGetValue(id, out var p) ? p : id;
        el.Node1 = Map(el.Node1);
        el.Node2 = Map(el.Node2);
        el.Node3 = Map(el.Node3);
        el.Node4 = Map(el.Node4);
        el.Node5 = Map(el.Node5);
        el.Node6 = Map(el.Node6);
        el.Node7 = Map(el.Node7);
        el.Node8 = Map(el.Node8);
    }

    private static FeJointInterfaceElement BuildInterface(
        int n1, int n2, int n3, int n4,
        GeologicalFeature feature,
        FeJointPropertySet? set)
    {
        double kn = feature.KnKpa ?? set?.NormalStiffnessKpaPerM ?? 1e5;
        double ks = feature.KsKpa ?? set?.ShearStiffnessKpaPerM ?? 1e4;
        double c = feature.JointCohesionKpa ?? set?.PeakCohesionKpa ?? 0;
        double phi = feature.JointFrictionDeg ?? set?.PeakFrictionDeg ?? 20;
        double tension = feature.JointTensionCutoffKpa ?? set?.TensileStrengthKpa ?? 0;
        double cres = set is { UseResidualStrength: true } ? set.ResidualCohesionKpa : c;
        double phires = set is { UseResidualStrength: true } ? set.ResidualFrictionDeg : phi;
        return new FeJointInterfaceElement
        {
            N1 = n1,
            N2 = n2,
            N3 = n3,
            N4 = n4,
            Kn = kn,
            Ks = ks,
            Cohesion = c,
            FrictionDeg = phi,
            Tension = tension,
            ResidualCohesion = cres,
            ResidualFrictionDeg = phires,
            InstallStage = Math.Max(1, feature.InstallStage),
            Ends = FormatEnds(feature.EndCondition),
            FeatureId = feature.Id
        };
    }

    private static string FormatEnds(FeJointEndCondition ends) => ends switch
    {
        FeJointEndCondition.BothOpen => "both_open",
        FeJointEndCondition.FirstClosedLastOpen => "first_closed_last_open",
        FeJointEndCondition.FirstOpenLastClosed => "first_open_last_closed",
        _ => "both_closed"
    };

    private static MeshInformationRow CloneRow(MeshInformationRow src) => new()
    {
        SmithEmb2dLocalOrder = src.SmithEmb2dLocalOrder,
        ElementId = src.ElementId,
        Node1 = src.Node1,
        Node2 = src.Node2,
        Node3 = src.Node3,
        Node4 = src.Node4,
        Node5 = src.Node5,
        Node6 = src.Node6,
        Node7 = src.Node7,
        Node8 = src.Node8,
        Etype = src.Etype,
        N1X = src.N1X,
        N1Y = src.N1Y,
        N2X = src.N2X,
        N2Y = src.N2Y,
        N3X = src.N3X,
        N3Y = src.N3Y,
        N4X = src.N4X,
        N4Y = src.N4Y,
        N5X = src.N5X,
        N5Y = src.N5Y,
        N6X = src.N6X,
        N6Y = src.N6Y,
        N7X = src.N7X,
        N7Y = src.N7Y,
        N8X = src.N8X,
        N8Y = src.N8Y,
        Kind = src.Kind,
        Status = src.Status
    };

    private static bool TryCentroid(
        MeshInformationRow el,
        IReadOnlyDictionary<int, Point> nodes,
        out double cx, out double cy)
    {
        cx = cy = 0;
        int n = 0;
        foreach (var id in el.EnumerateCornerNodeIds())
        {
            if (!nodes.TryGetValue(id, out var p))
                continue;
            cx += p.X;
            cy += p.Y;
            n++;
        }

        if (n == 0)
            return false;
        cx /= n;
        cy /= n;
        return true;
    }

    private static (double X, double Y) FeatureTangentAt(
        IReadOnlyList<((double X, double Y) A, (double X, double Y) B)> segs,
        Point p)
    {
        double best = double.MaxValue;
        double tx = 1, ty = 0;
        foreach (var (a, b) in segs)
        {
            double d = DistanceToSegment(p, a, b);
            if (d < best)
            {
                best = d;
                tx = b.X - a.X;
                ty = b.Y - a.Y;
            }
        }

        double len = Math.Sqrt(tx * tx + ty * ty);
        if (len < 1e-15)
            return (1, 0);
        return (tx / len, ty / len);
    }

    private static double DistanceToPolyline(Point p, IReadOnlyList<((double X, double Y) A, (double X, double Y) B)> segs)
    {
        double best = double.MaxValue;
        foreach (var (a, b) in segs)
            best = Math.Min(best, DistanceToSegment(p, a, b));
        return best;
    }

    private static double DistanceToSegment(Point p, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-24)
            return Dist(p, a);
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        t = Math.Clamp(t, 0, 1);
        return Dist(p, (a.X + t * dx, a.Y + t * dy));
    }

    private static Point Mid(Point a, Point b) => new((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);

    private static double Dist(Point p, (double X, double Y) q)
    {
        double dx = p.X - q.X, dy = p.Y - q.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
