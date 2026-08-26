using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GeotechSlopeApp.Models.Geometry;

namespace GeotechSlopeApp.Services.Geometry;

/// <summary>
/// True boolean / fill operations on the planar kernel using WPF geometry combine,
/// then writing result rings back as faces (recomputable from <see cref="KernelBooleanOperation"/> history).
/// </summary>
public static class KernelBooleanEngine
{
    public static PlanarGeometryKernel RecomputeFromHistory(PlanarGeometryKernel seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var kernel = CloneShallow(seed);
        // Drop faces created by prior boolean commits; keep seed soil faces that are not voids/fills from history.
        var historyFaceIds = new HashSet<int>(
            seed.BooleanHistory.Select(op => op.SubjectFaceId)
                .Concat(seed.BooleanHistory.Select(op => op.ToolFaceId)));

        foreach (var op in seed.BooleanHistory.OrderBy(o => o.Sequence))
            Apply(kernel, op);

        _ = historyFaceIds;
        return kernel;
    }

    public static PlanarMapFace? Apply(PlanarGeometryKernel kernel, KernelBooleanOperation op)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(op);

        if (op.Kind == KernelBooleanKind.Fill)
            return ApplyFill(kernel, op);

        if (!TryGetFaceRing(kernel, op.SubjectFaceId, out var subject)
            || !TryGetFaceRing(kernel, op.ToolFaceId, out var tool))
            return null;

        var mode = op.Kind switch
        {
            KernelBooleanKind.Union => GeometryCombineMode.Union,
            KernelBooleanKind.Subtract => GeometryCombineMode.Exclude,
            KernelBooleanKind.Intersect => GeometryCombineMode.Intersect,
            _ => GeometryCombineMode.Union
        };

        var combined = System.Windows.Media.Geometry.Combine(
            ToPath(subject), ToPath(tool), mode, Transform.Identity);
        var rings = ExtractRings(combined);
        if (rings.Count == 0)
            return null;

        // Primary result = largest-area ring.
        var best = rings.OrderByDescending(r => Math.Abs(SignedArea(r))).First();
        var faceId = NextFaceId(kernel);
        var face = new PlanarMapFace
        {
            Id = faceId,
            Name = string.IsNullOrWhiteSpace(op.Label) ? $"{op.Kind} result" : op.Label,
            MaterialId = op.ResultMaterialId,
            IsExcavationVoid = op.Kind == KernelBooleanKind.Subtract,
            IsFill = false
        };
        AddRingAsFace(kernel, face, best);

        if (op.Kind == KernelBooleanKind.Subtract)
        {
            var subjectFace = kernel.Faces.FirstOrDefault(f => f.Id == op.SubjectFaceId);
            if (subjectFace != null)
                subjectFace.IsExcavationVoid = false;
        }

        if (!kernel.BooleanHistory.Contains(op))
            kernel.BooleanHistory.Add(op);

        return face;
    }

    private static PlanarMapFace ApplyFill(PlanarGeometryKernel kernel, KernelBooleanOperation op)
    {
        if (op.FillRing.Count < 3)
            throw new InvalidOperationException("Fill requires a closed ring of at least 3 points.");

        var face = new PlanarMapFace
        {
            Id = NextFaceId(kernel),
            Name = string.IsNullOrWhiteSpace(op.Label) ? "Fill" : op.Label,
            MaterialId = op.ResultMaterialId,
            IsFill = true
        };
        var ring = op.FillRing.Select(p => new Point(p.X, p.Y)).ToList();
        AddRingAsFace(kernel, face, ring);
        if (!kernel.BooleanHistory.Contains(op))
            kernel.BooleanHistory.Add(op);
        return face;
    }

    private static void AddRingAsFace(PlanarGeometryKernel kernel, PlanarMapFace face, IReadOnlyList<Point> ring)
    {
        var n = ring.Count;
        if (n >= 2
            && Math.Abs(ring[0].X - ring[^1].X) < 1e-9
            && Math.Abs(ring[0].Y - ring[^1].Y) < 1e-9)
            n--;

        var nextVid = kernel.Vertices.Count == 0 ? 1 : kernel.Vertices.Max(v => v.StableId) + 1;
        for (var i = 0; i < n; i++)
        {
            var id = nextVid++;
            kernel.Vertices.Add(new PlanarMapVertex { StableId = id, X = ring[i].X, Y = ring[i].Y });
            face.VertexStableIds.Add(id);
        }

        for (var i = 0; i < face.VertexStableIds.Count; i++)
        {
            var a = face.VertexStableIds[i];
            var b = face.VertexStableIds[(i + 1) % face.VertexStableIds.Count];
            kernel.Edges.Add(new PlanarMapEdge
            {
                Id = kernel.Edges.Count + 1,
                StartStableId = a,
                EndStableId = b,
                Role = face.IsExcavationVoid ? GeometryEdgeRole.Excavation : GeometryEdgeRole.Boundary
            });
        }

        kernel.Faces.Add(face);
    }

    private static bool TryGetFaceRing(PlanarGeometryKernel kernel, int faceId, out List<Point> ring)
    {
        ring = new List<Point>();
        var face = kernel.Faces.FirstOrDefault(f => f.Id == faceId);
        if (face == null || face.VertexStableIds.Count < 3) return false;
        foreach (var id in face.VertexStableIds)
        {
            var v = kernel.FindVertex(id);
            if (v == null) return false;
            ring.Add(new Point(v.X, v.Y));
        }

        return ring.Count >= 3;
    }

    private static PathGeometry ToPath(IReadOnlyList<Point> ring)
    {
        var fig = new PathFigure { StartPoint = ring[0], IsClosed = true };
        for (var i = 1; i < ring.Count; i++)
            fig.Segments.Add(new LineSegment(ring[i], true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    private static List<List<Point>> ExtractRings(PathGeometry geometry)
    {
        var rings = new List<List<Point>>();
        if (geometry == null) return rings;
        foreach (var fig in geometry.Figures)
        {
            var ring = new List<Point> { fig.StartPoint };
            foreach (var seg in fig.Segments)
            {
                if (seg is LineSegment ls)
                    ring.Add(ls.Point);
                else if (seg is PolyLineSegment pls)
                    ring.AddRange(pls.Points);
            }

            if (ring.Count >= 3)
                rings.Add(ring);
        }

        return rings;
    }

    private static double SignedArea(IReadOnlyList<Point> ring)
    {
        double sum = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return 0.5 * sum;
    }

    private static int NextFaceId(PlanarGeometryKernel kernel) =>
        kernel.Faces.Count == 0 ? 1 : kernel.Faces.Max(f => f.Id) + 1;

    private static PlanarGeometryKernel CloneShallow(PlanarGeometryKernel seed)
    {
        var k = new PlanarGeometryKernel();
        foreach (var v in seed.Vertices)
            k.Vertices.Add(new PlanarMapVertex { StableId = v.StableId, X = v.X, Y = v.Y });
        foreach (var e in seed.Edges)
        {
            k.Edges.Add(new PlanarMapEdge
            {
                Id = e.Id,
                StartStableId = e.StartStableId,
                EndStableId = e.EndStableId,
                Role = e.Role,
                MaterialAId = e.MaterialAId,
                MaterialBId = e.MaterialBId,
                FeatureId = e.FeatureId
            });
        }

        foreach (var f in seed.Faces.Where(f => !f.IsFill && !f.IsExcavationVoid))
        {
            var face = new PlanarMapFace
            {
                Id = f.Id,
                Name = f.Name,
                MaterialId = f.MaterialId
            };
            face.VertexStableIds.AddRange(f.VertexStableIds);
            k.Faces.Add(face);
        }

        foreach (var g in seed.GeologicalFeatures)
        {
            var copy = new GeologicalFeature
            {
                Id = g.Id,
                Kind = g.Kind,
                Name = g.Name,
                IsEnabled = g.IsEnabled,
                MaterialId = g.MaterialId,
                WaterElevation = g.WaterElevation,
                DipAngleDeg = g.DipAngleDeg
            };
            copy.Vertices.AddRange(g.Vertices);
            k.GeologicalFeatures.Add(copy);
        }

        return k;
    }
}
