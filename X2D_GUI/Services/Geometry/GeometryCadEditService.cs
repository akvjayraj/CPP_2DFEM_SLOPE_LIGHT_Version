using System;
using System.Collections.Generic;
using System.Linq;

namespace GeotechSlopeApp.Services.Geometry;

/// <summary>Multi-select transform helpers (move / rotate / scale) in axis coordinates.</summary>
public static class GeometryCadEditService
{
    public static List<(int StableId, double X, double Y)> Move(
        IEnumerable<(int StableId, double X, double Y)> vertices,
        IReadOnlyCollection<int> selectedIds,
        double dx,
        double dy)
    {
        return vertices.Select(v =>
            selectedIds.Contains(v.StableId) ? (v.StableId, v.X + dx, v.Y + dy) : v).ToList();
    }

    public static List<(int StableId, double X, double Y)> Rotate(
        IEnumerable<(int StableId, double X, double Y)> vertices,
        IReadOnlyCollection<int> selectedIds,
        double pivotX,
        double pivotY,
        double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180.0;
        var c = Math.Cos(rad);
        var s = Math.Sin(rad);
        return vertices.Select(v =>
        {
            if (!selectedIds.Contains(v.StableId)) return v;
            var x = v.X - pivotX;
            var y = v.Y - pivotY;
            return (v.StableId, pivotX + x * c - y * s, pivotY + x * s + y * c);
        }).ToList();
    }

    public static List<(int StableId, double X, double Y)> Scale(
        IEnumerable<(int StableId, double X, double Y)> vertices,
        IReadOnlyCollection<int> selectedIds,
        double pivotX,
        double pivotY,
        double scale)
    {
        if (Math.Abs(scale) < 1e-12) scale = 1;
        return vertices.Select(v =>
        {
            if (!selectedIds.Contains(v.StableId)) return v;
            return (v.StableId, pivotX + (v.X - pivotX) * scale, pivotY + (v.Y - pivotY) * scale);
        }).ToList();
    }

    /// <summary>Clipboard payload for cross-project paste (axis polylines + optional material ids).</summary>
    public sealed class GeometryClipboard
    {
        public List<(double X, double Y)> Boundary { get; } = new();
        public List<List<(double X, double Y)>> Surfaces { get; } = new();
        public List<(string Kind, List<(double X, double Y)> Verts, int? MaterialId)> Features { get; } = new();
    }
}
