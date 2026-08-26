using System;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>Maps <see cref="LoadModel"/> direction/orientation to solver axis vectors.</summary>
public static class LoadExportDirectionResolver
{
    /// <summary>
    /// Distributed-edge group header. kind=1 uses edge normal in solver; dir_x/dir_y are placeholders.
    /// </summary>
    public static (int kind, double dirX, double dirY) ResolveEdgeGroupDirection(
        LoadModel load,
        GeometryDefinitionViewModel geometry,
        MeshViewModel? mesh)
    {
        if (load.Direction == LoadDirection.X)
            return (0, load.Magnitude >= 0 ? 1.0 : -1.0, 0.0);

        if (load.Direction == LoadDirection.Y)
            return (0, 0.0, load.Magnitude >= 0 ? -1.0 : 1.0);

        switch (load.Orientation)
        {
            case LoadOrientation.VerticalDown:
                return (0, 0.0, -1.0);
            case LoadOrientation.HorizontalRight:
                return (0, 1.0, 0.0);
            case LoadOrientation.AngleFromHorizontal:
                return TryUnitFromAngle(load.AngleDeg, out var ux, out var uy)
                    ? (0, ux, uy)
                    : (0, 0.0, -1.0);
            case LoadOrientation.AngleToBoundary:
            {
                var startAxis = geometry.ConvertCanvasToAxis(new System.Windows.Point(load.StartX, load.StartY));
                var endAxis = geometry.ConvertCanvasToAxis(new System.Windows.Point(load.EndX, load.EndY));
                var tangent = new Vector(endAxis.X - startAxis.X, endAxis.Y - startAxis.Y);
                if (TryResolveGlobalDirection(load, geometry, mesh, startAxis, tangent, out ux, out uy, preferEdgeNormal: true))
                    return (0, ux, uy);
                return (1, 0.0, -1.0);
            }
            case LoadOrientation.NormalToBoundary:
            default:
            {
                var startAxis = geometry.ConvertCanvasToAxis(new Point(load.StartX, load.StartY));
                var endAxis = geometry.ConvertCanvasToAxis(new Point(load.EndX, load.EndY));
                var mid = new Point((startAxis.X + endAxis.X) * 0.5, (startAxis.Y + endAxis.Y) * 0.5);
                var tangent = new Vector(endAxis.X - startAxis.X, endAxis.Y - startAxis.Y);
                if (TryPickInwardNormal(tangent, mid, mesh, geometry, out var nx, out var ny))
                    return (0, nx, ny);
                return (0, 0.0, -1.0);
            }
        }
    }

    /// <summary>Axis unit vector for point-load export and canvas (sign from magnitude only).</summary>
    public static bool TryResolvePointAxisDirection(
        LoadModel load,
        GeometryDefinitionViewModel geometry,
        MeshViewModel? mesh,
        Point axisPoint,
        out double ux,
        out double uy)
        => TryResolveAxisUnitDirection(load, geometry, mesh, axisPoint, null, out ux, out uy, preferEdgeNormal: true);

    /// <summary>Nodal force components (kN) in axis coordinates for 2D point loads.</summary>
    public static (double fx, double fy) ResolvePointForceComponents(
        LoadModel load,
        GeometryDefinitionViewModel geometry,
        MeshViewModel? mesh,
        int nodeId)
    {
        Point axisPt = ResolveNodeAxisPoint(mesh, geometry, nodeId, load);
        if (!TryResolveAxisUnitDirection(load, geometry, mesh, axisPt, null, out var ux, out var uy, preferEdgeNormal: true))
            return (0.0, load.Magnitude);

        return (load.Magnitude * ux, load.Magnitude * uy);
    }

    public static int ResolvePointNodeId(
        LoadModel load,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry)
    {
        if (load.AppliedToNodeId is > 0)
            return load.AppliedToNodeId.Value;

        if (mesh is not { IsDiscretized: true } || mesh.DiscretizationNodes.Count == 0)
            return 0;

        var clickCanvas = new Point(load.StartX, load.StartY);
        double bestDistSq = double.MaxValue;
        int bestIndex = -1;
        for (int i = 0; i < mesh.DiscretizationNodes.Count; i++)
        {
            var axis = mesh.DiscretizationNodes[i].Location;
            var canvas = geometry.ConvertAxisToCanvas(new Point(axis.X, axis.Y));
            double dx = canvas.X - clickCanvas.X;
            double dy = canvas.Y - clickCanvas.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestDistSq)
            {
                bestDistSq = d2;
                bestIndex = i;
            }
        }

        return bestIndex >= 0 ? bestIndex + 1 : 0;
    }

    public static Point ResolveNodeAxisPoint(
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        int nodeId,
        LoadModel load)
    {
        if (mesh is { IsDiscretized: true })
        {
            int idx = nodeId - 1;
            if (idx >= 0 && idx < mesh.DiscretizationNodes.Count)
                return mesh.DiscretizationNodes[idx].Location;
        }

        return geometry.ConvertCanvasToAxis(new Point(load.StartX, load.StartY));
    }

    private static bool TryResolveGlobalDirection(
        LoadModel load,
        GeometryDefinitionViewModel geometry,
        MeshViewModel? mesh,
        Point? axisPoint,
        Vector? edgeTangentAxis,
        out double ux,
        out double uy,
        bool preferEdgeNormal)
    {
        if (!TryResolveAxisUnitDirection(load, geometry, mesh, axisPoint, edgeTangentAxis, out ux, out uy, preferEdgeNormal))
            return false;

        if (load.Direction == LoadDirection.X)
            ux = load.Magnitude >= 0 ? Math.Abs(ux) : -Math.Abs(ux);
        else if (load.Direction == LoadDirection.Y)
            uy = load.Magnitude >= 0 ? -Math.Abs(uy) : Math.Abs(uy);

        return true;
    }

    private static bool TryResolveAxisUnitDirection(
        LoadModel load,
        GeometryDefinitionViewModel geometry,
        MeshViewModel? mesh,
        Point? axisPoint,
        Vector? edgeTangentAxis,
        out double ux,
        out double uy,
        bool preferEdgeNormal)
    {
        ux = 0;
        uy = -1;

        if (load.Direction == LoadDirection.X)
        {
            ux = 1.0;
            uy = 0.0;
            return true;
        }

        if (load.Direction == LoadDirection.Y)
        {
            ux = 0.0;
            uy = -1.0;
            return true;
        }

        switch (load.Orientation)
        {
            case LoadOrientation.VerticalDown:
                ux = 0.0;
                uy = -1.0;
                return true;
            case LoadOrientation.HorizontalRight:
                ux = 1.0;
                uy = 0.0;
                return true;
            case LoadOrientation.AngleFromHorizontal:
                return TryUnitFromAngle(load.AngleDeg, out ux, out uy);
            case LoadOrientation.AngleToBoundary:
                if (edgeTangentAxis is { Length: > 1e-9 } tangent)
                {
                    tangent.Normalize();
                    double baseAngle = Math.Atan2(-tangent.Y, tangent.X) * 180.0 / Math.PI;
                    return TryUnitFromAngle(baseAngle + load.AngleDeg, out ux, out uy);
                }
                if (axisPoint is Point pt && TryNormalTowardInterior(pt, mesh, geometry, out var nx, out var ny))
                {
                    double baseAngle = Math.Atan2(-ny, nx) * 180.0 / Math.PI;
                    return TryUnitFromAngle(baseAngle + load.AngleDeg, out ux, out uy);
                }
                return TryUnitFromAngle(load.AngleDeg, out ux, out uy);
            case LoadOrientation.NormalToBoundary:
            default:
                if (preferEdgeNormal && edgeTangentAxis is { Length: > 1e-9 } t)
                {
                    t.Normalize();
                    if (TryPickInwardNormal(t, axisPoint, mesh, geometry, out ux, out uy))
                        return true;
                }

                if (axisPoint is Point p && TryNormalTowardInterior(p, mesh, geometry, out ux, out uy))
                    return true;

                uy = -1.0;
                ux = 0.0;
                return true;
        }
    }

    private static bool TryUnitFromAngle(double angleDeg, out double ux, out double uy)
    {
        double rad = angleDeg * Math.PI / 180.0;
        ux = Math.Cos(rad);
        uy = -Math.Sin(rad);
        double len = Math.Sqrt(ux * ux + uy * uy);
        if (len < 1e-12)
        {
            ux = 0;
            uy = -1;
            return false;
        }
        ux /= len;
        uy /= len;
        return true;
    }

    private static bool TryNormalTowardInterior(
        Point axisPoint,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        out double ux,
        out double uy)
    {
        ux = 0;
        uy = -1;
        if (!TryGetCentroidAxis(mesh, geometry, out var centroid))
            return false;

        var toInterior = new Vector(centroid.X - axisPoint.X, centroid.Y - axisPoint.Y);
        if (toInterior.Length < 1e-9)
            return false;

        toInterior.Normalize();
        ux = toInterior.X;
        uy = toInterior.Y;
        return true;
    }

    private static bool TryPickInwardNormal(
        Vector tangentAxis,
        Point? axisPoint,
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        out double ux,
        out double uy)
    {
        var n1 = new Vector(-tangentAxis.Y, tangentAxis.X);
        var n2 = new Vector(tangentAxis.Y, -tangentAxis.X);
        if (axisPoint is Point p && TryGetCentroidAxis(mesh, geometry, out var centroid))
        {
            var toInterior = new Vector(centroid.X - p.X, centroid.Y - p.Y);
            var pick = Vector.Multiply(n1, toInterior) >= Vector.Multiply(n2, toInterior) ? n1 : n2;
            if (pick.Length < 1e-9)
            {
                ux = 0.0;
                uy = -1.0;
                return false;
            }
            pick.Normalize();
            ux = pick.X;
            uy = pick.Y;
            return true;
        }

        ux = n1.X;
        uy = n1.Y;
        return true;
    }

    private static bool TryGetCentroidAxis(
        MeshViewModel? mesh,
        GeometryDefinitionViewModel geometry,
        out Point centroid)
    {
        centroid = default;
        if (mesh is not { IsDiscretized: true } || mesh.DiscretizationNodes.Count == 0)
            return false;

        double sumX = 0;
        double sumY = 0;
        foreach (var node in mesh.DiscretizationNodes)
        {
            sumX += node.Location.X;
            sumY += node.Location.Y;
        }

        int count = mesh.DiscretizationNodes.Count;
        centroid = new Point(sumX / count, sumY / count);
        return true;
    }
}
