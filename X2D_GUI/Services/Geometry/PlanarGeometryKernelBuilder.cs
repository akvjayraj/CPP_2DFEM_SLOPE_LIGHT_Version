using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.Geometry;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Services.Geometry;

/// <summary>Rebuilds a <see cref="PlanarGeometryKernel"/> from the live geometry view-model collections.</summary>
public static class PlanarGeometryKernelBuilder
{
    public static PlanarGeometryKernel Build(GeometryDefinitionViewModel geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var kernel = new PlanarGeometryKernel();

        foreach (var v in geometry.GeometryVertexRows)
        {
            kernel.Vertices.Add(new PlanarMapVertex
            {
                StableId = v.StablePointId,
                X = v.AxisX,
                Y = v.AxisY
            });
        }

        var edgeId = 1;
        foreach (var seg in geometry.GeometryLineSegments)
        {
            if (seg.IsCircle) continue;
            if (!TryResolvePoint(geometry, seg.FromPoint, out var a)) continue;
            if (!TryResolvePoint(geometry, seg.ToPoint, out var b)) continue;
            if (a == b) continue;
            kernel.Edges.Add(new PlanarMapEdge
            {
                Id = edgeId++,
                StartStableId = a,
                EndStableId = b,
                Role = seg.IsBoundaryEdge ? GeometryEdgeRole.Boundary : GeometryEdgeRole.MaterialContact
            });
        }

        foreach (var edge in geometry.GeometryInternalEdges)
        {
            if (edge.IsArc) continue;
            kernel.Edges.Add(new PlanarMapEdge
            {
                Id = edgeId++,
                StartStableId = edge.StableMin,
                EndStableId = edge.StableMax,
                Role = GeometryEdgeRole.MaterialContact
            });
        }

        foreach (var surface in geometry.GeometrySurfaces)
        {
            var face = new PlanarMapFace
            {
                Id = surface.SurfaceNumber,
                Name = string.IsNullOrWhiteSpace(surface.SurfaceName)
                    ? $"Surface {surface.SurfaceNumber}"
                    : surface.SurfaceName,
                MaterialId = surface.AssignedMaterialId
            };

            if (TryGetSurfaceRing(geometry, surface, out var ringIds))
                face.VertexStableIds.AddRange(ringIds);

            kernel.Faces.Add(face);
        }

        if (kernel.Faces.Count == 0
            && geometry.TryGetClosedBoundaryAxisPolygon(out var poly)
            && poly.Count >= 3)
        {
            var face = new PlanarMapFace { Id = 1, Name = "Soil boundary" };
            EnsureBoundaryFace(kernel, poly, face);
            kernel.Faces.Add(face);
        }

        foreach (var feature in geometry.GeologicalFeatures)
            kernel.GeologicalFeatures.Add(CloneFeature(feature));

        var seq = 0;
        foreach (var op in geometry.KernelBooleanHistory)
        {
            op.Sequence = seq++;
            kernel.BooleanHistory.Add(CloneBoolean(op));
        }

        return kernel;
    }

    private static void EnsureBoundaryFace(PlanarGeometryKernel kernel, IReadOnlyList<Point> poly, PlanarMapFace face)
    {
        var n = poly.Count;
        if (n >= 2
            && Math.Abs(poly[0].X - poly[^1].X) < 1e-9
            && Math.Abs(poly[0].Y - poly[^1].Y) < 1e-9)
            n--;

        var nextId = kernel.Vertices.Count == 0 ? 1 : kernel.Vertices.Max(v => v.StableId) + 1;
        for (var i = 0; i < n; i++)
        {
            var existing = kernel.Vertices.FirstOrDefault(v =>
                Math.Abs(v.X - poly[i].X) < 1e-9 && Math.Abs(v.Y - poly[i].Y) < 1e-9);
            if (existing != null)
            {
                face.VertexStableIds.Add(existing.StableId);
                continue;
            }

            var id = nextId++;
            kernel.Vertices.Add(new PlanarMapVertex { StableId = id, X = poly[i].X, Y = poly[i].Y });
            face.VertexStableIds.Add(id);
        }

        for (var i = 0; i < face.VertexStableIds.Count; i++)
        {
            var a = face.VertexStableIds[i];
            var b = face.VertexStableIds[(i + 1) % face.VertexStableIds.Count];
            if (kernel.Edges.Any(e =>
                    (e.StartStableId == a && e.EndStableId == b)
                    || (e.StartStableId == b && e.EndStableId == a)))
                continue;
            kernel.Edges.Add(new PlanarMapEdge
            {
                Id = kernel.Edges.Count + 1,
                StartStableId = a,
                EndStableId = b,
                Role = GeometryEdgeRole.Boundary
            });
        }
    }

    private static bool TryResolvePoint(GeometryDefinitionViewModel geometry, string? label, out int stableId)
    {
        stableId = -1;
        if (string.IsNullOrWhiteSpace(label)) return false;
        var v = geometry.GeometryVertexRows.FirstOrDefault(r =>
            string.Equals(r.PointLabel, label, StringComparison.OrdinalIgnoreCase)
            || string.Equals($"P{r.PointNumber}", label, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.PointNumber.ToString(), label, StringComparison.OrdinalIgnoreCase));
        if (v == null) return false;
        stableId = v.StablePointId;
        return true;
    }

    private static bool TryGetSurfaceRing(
        GeometryDefinitionViewModel geometry,
        GeometrySurface surface,
        out List<int> ringIds)
    {
        ringIds = new List<int>();
        var labels = surface.IsPolygonLocked && surface.LockedPolygonVertexLabels.Count > 0
            ? surface.LockedPolygonVertexLabels
            : surface.DraftPolygonVertexLabels;
        if (labels.Count == 0)
            return false;

        foreach (var label in labels)
        {
            if (TryResolvePoint(geometry, label, out var id))
                ringIds.Add(id);
        }

        return ringIds.Count >= 3;
    }

    private static GeologicalFeature CloneFeature(GeologicalFeature src)
    {
        var copy = new GeologicalFeature
        {
            Id = src.Id,
            Kind = src.Kind,
            Name = src.Name,
            IsEnabled = src.IsEnabled,
            MaterialId = src.MaterialId,
            WaterElevation = src.WaterElevation,
            DipAngleDeg = src.DipAngleDeg,
            PropertySetId = src.PropertySetId,
            EndCondition = src.EndCondition,
            InstallStage = src.InstallStage,
            JointNetworkId = src.JointNetworkId,
            JointNetworkName = src.JointNetworkName
        };
        copy.Vertices.AddRange(src.Vertices);
        return copy;
    }

    private static KernelBooleanOperation CloneBoolean(KernelBooleanOperation src)
    {
        var copy = new KernelBooleanOperation
        {
            Sequence = src.Sequence,
            Kind = src.Kind,
            SubjectFaceId = src.SubjectFaceId,
            ToolFaceId = src.ToolFaceId,
            ResultMaterialId = src.ResultMaterialId,
            StageNumber = src.StageNumber,
            Label = src.Label
        };
        copy.FillRing.AddRange(src.FillRing);
        return copy;
    }
}
