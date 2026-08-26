using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Builds hydrostatic ponded-water face pressures when free water lies above
/// the ground (RS2 Ponded Water Load / Auto Assign). Complements γsat body forces + Terzaghi u:
/// without these tractions the free surface is under-supported and SRF never equilibrates
/// (e.g. Duncan &amp; Wright Ex35, WT 30 ft above crest).
/// Magnitude: q = γw · max(0, y_water − y), applied normal into the soil.
/// Supports T3 / T6 / Q4 / Q8 (serendipity and Smith emb_2d); quadratic edges emit NodeC.
/// </summary>
public static class PondedWaterEdgeLoadBuilder
{
    private const double MinPressure = 1.0e-6;

    public static List<DistributedEdgeLoadDatWriter.LoadGroup> CollectGroups(
        GroundwaterModel model,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel? geometry)
    {
        var result = new List<DistributedEdgeLoadDatWriter.LoadGroup>();
        if (model is not { IsEnabled: true, Method: GroundwaterMethod.Hydrostatic })
            return result;
        // Ponded is a sub-option of Water Surfaces. Missing flags (older projects) → allow.
        if (model.StaticSettings?.IncludeWaterSurfaces == false)
            return result;
        if (model.StaticSettings?.IncludePondedFreeWater == false)
            return result;
        if (mesh is not { IsDiscretized: true } || mesh.MeshInformationRows.Count == 0)
            return result;
        if (mesh.DiscretizationNodes.Count == 0)
            return result;

        double gamW = model.GammaW;
        if (!(gamW > 0.0))
            return result;

        if (!TryResolveWaterSurface(model, mesh, out var surf) || surf.Count < 1)
            return result;

        double yMin = mesh.DiscretizationNodes.Min(n => n.Location.Y);
        double ySpan = Math.Max(1e-6, mesh.DiscretizationNodes.Max(n => n.Location.Y) - yMin);
        double baseBand = 0.02 * ySpan; // skip true foundation edges

        // Boundary edges owned by a unique element — use that element's centroid for
        // the inward normal (global mesh centroid flips toe benches that sit below the mass).
        var edgeCount = new Dictionary<(int A, int B), int>();
        var edgeOwner = new Dictionary<(int A, int B), (int N1, int N2, int Mid, MeshInformationRow Row)>();
        foreach (var row in mesh.MeshInformationRows)
        {
            foreach (var (n1, n2, mid) in row.EnumerateCornerEdgesWithMid())
            {
                if (n1 <= 0 || n2 <= 0) continue;
                var key = n1 < n2 ? (n1, n2) : (n2, n1);
                edgeCount[key] = edgeCount.GetValueOrDefault(key) + 1;
                edgeOwner[key] = (n1, n2, mid, row);
            }
        }

        var grouped = new Dictionary<(int kind, long dx, long dy), DistributedEdgeLoadDatWriter.LoadGroup>();

        foreach (var kv in edgeCount)
        {
            if (kv.Value != 1) continue;
            var (n1, n2, midNode, row) = edgeOwner[kv.Key];
            if (!TryNode(mesh, n1, out var p1) || !TryNode(mesh, n2, out var p2))
                continue;
            if (!TryElementCentroid(mesh, row, out var elCentroid))
                continue;

            double midY = 0.5 * (p1.Y + p2.Y);
            if (midY <= yMin + baseBand)
                continue; // foundation / base contact

            double q1 = PressureAt(gamW, surf, p1.X, p1.Y);
            double q2 = PressureAt(gamW, surf, p2.X, p2.Y);
            if (q1 < MinPressure && q2 < MinPressure)
                continue;

            var mid = new Point(0.5 * (p1.X + p2.X), midY);
            var tangent = new Vector(p2.X - p1.X, p2.Y - p1.Y);
            if (tangent.Length < 1e-12)
                continue;
            tangent.Normalize();

            // Inward (into soil) unit normal — same convention as NormalToBoundary export.
            var nA = new Vector(-tangent.Y, tangent.X);
            var nB = new Vector(tangent.Y, -tangent.X);
            var toInterior = new Vector(elCentroid.X - mid.X, elCentroid.Y - mid.Y);
            var inward = Vector.Multiply(nA, toInterior) >= Vector.Multiply(nB, toInterior) ? nA : nB;
            if (inward.Length < 1e-12)
                continue;
            inward.Normalize();

            // Skip steep domain sidewalls (|nx| dominant): rollers at xmin/xmax get
            // unequal heads on truncated submerged models and invent net horizontal force.
            if (Math.Abs(inward.X) > 0.85 && Math.Abs(inward.Y) < 0.35)
                continue;

            int kind = 0;
            long dxKey = (long)Math.Round(inward.X * 1.0e6);
            long dyKey = (long)Math.Round(inward.Y * 1.0e6);
            var gkey = (kind, dxKey, dyKey);
            if (!grouped.TryGetValue(gkey, out var group))
            {
                group = new DistributedEdgeLoadDatWriter.LoadGroup
                {
                    Kind = kind,
                    DirX = inward.X,
                    DirY = inward.Y
                };
                grouped[gkey] = group;
            }

            // Quadratic edges (T6/Q8): include midside so the solver splits A–C–B.
            int nodeC = midNode > 0 && midNode != n1 && midNode != n2 && TryNode(mesh, midNode, out _)
                ? midNode
                : 0;

            group.Edges.Add(new DistributedEdgeLoadDatWriter.EdgeLoadRow
            {
                NodeA = n1,
                NodeB = n2,
                NodeC = nodeC,
                QA = q1,
                QB = q2
            });
        }

        result.AddRange(grouped.Values.Where(g => g.Edges.Count > 0));
        return result;
    }

    /// <summary>
    /// Resolves the free-water surface polyline used for head: phreatic surf points or
    /// a horizontal Total Head line spanning the mesh x-range (RS2 Total Head).
    /// </summary>
    public static bool TryResolveWaterSurface(
        GroundwaterModel model,
        MeshViewModel mesh,
        out List<(double X, double Y)> surf)
    {
        surf = new List<(double X, double Y)>();
        if (model.PondedWaterLevelMethod == PondedWaterLevelMethod.TotalHead)
        {
            double y = model.PondedTotalHead;
            if (!double.IsFinite(y))
                return false;
            double xMin = mesh.DiscretizationNodes.Min(n => n.Location.X);
            double xMax = mesh.DiscretizationNodes.Max(n => n.Location.X);
            if (xMax <= xMin)
                return false;
            surf.Add((xMin, y));
            surf.Add((xMax, y));
            return true;
        }

        if (model.SurfPoints is not { Count: >= 1 })
            return false;
        surf = model.SurfPoints
            .OrderBy(p => p.X)
            .Select(p => (p.X, p.Y))
            .ToList();
        return surf.Count >= 1;
    }

    private static double PressureAt(
        double gamW,
        IReadOnlyList<(double X, double Y)> surf,
        double x,
        double y)
    {
        double yWt = WaterTableYAtX(surf, x);
        return Math.Max(0.0, gamW * (yWt - y));
    }

    private static double WaterTableYAtX(IReadOnlyList<(double X, double Y)> surf, double x)
    {
        if (surf.Count == 1)
            return surf[0].Y;
        if (x <= surf[0].X)
            return surf[0].Y;
        if (x >= surf[^1].X)
            return surf[^1].Y;
        for (int i = 1; i < surf.Count; i++)
        {
            double x0 = surf[i - 1].X, y0 = surf[i - 1].Y;
            double x1 = surf[i].X, y1 = surf[i].Y;
            if (x < x0 || x > x1) continue;
            if (Math.Abs(x1 - x0) < 1e-14)
                return 0.5 * (y0 + y1);
            double t = (x - x0) / (x1 - x0);
            return y0 + t * (y1 - y0);
        }
        return surf[^1].Y;
    }

    private static bool TryElementCentroid(MeshViewModel mesh, MeshInformationRow row, out Point centroid)
    {
        double sx = 0, sy = 0;
        int n = 0;
        foreach (int id in row.EnumerateCornerNodeIds())
        {
            if (!TryNode(mesh, id, out var p))
            {
                centroid = default;
                return false;
            }
            sx += p.X;
            sy += p.Y;
            n++;
        }

        if (n == 0)
        {
            centroid = default;
            return false;
        }

        centroid = new Point(sx / n, sy / n);
        return true;
    }

    private static bool TryNode(MeshViewModel mesh, int nodeId, out Point p)
    {
        p = default;
        if (nodeId <= 0) return false;

        foreach (var n in mesh.DiscretizationNodes)
        {
            if (n.NodeId == nodeId)
            {
                p = n.Location;
                return true;
            }
        }

        // Dense 1-based decks: index = nodeId - 1
        int i = nodeId - 1;
        if (i >= 0 && i < mesh.DiscretizationNodes.Count)
        {
            p = mesh.DiscretizationNodes[i].Location;
            return true;
        }

        return false;
    }
}
