using System;
using System.Collections.Generic;
using System.Linq;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Resolves material/etype for elements of a structured mesh that conforms to inclined
/// (non-horizontal) material layer cuts drawn on the Material Properties canvas. Unlike the
/// horizontal <see cref="Q8MeshMaterialLayerPlanner.LayerPlan"/> (which bands purely by Y), this
/// plan resolves etype by point-in-zone test against the per-layer strip polygons.
/// </summary>
public sealed class NonHorizontalLayerMeshPlan
{
    public sealed class Zone
    {
        public int DisplayLayer { get; init; }
        public IReadOnlyList<(double x, double y)> Polygon { get; init; } = Array.Empty<(double, double)>();
        public int MaterialId { get; init; }
        public int Etype { get; init; }
        public (double x, double y) Centroid { get; init; }
    }

    public bool HasZones { get; private init; }
    public IReadOnlyList<Zone> Zones { get; private init; } = Array.Empty<Zone>();
    public IReadOnlyDictionary<int, int> MaterialIdToEtype { get; private init; } = new Dictionary<int, int>();
    public string? StatusNote { get; private init; }

    /// <summary>
    /// Returns the etype for an element centroid by locating the zone polygon that contains it;
    /// falls back to the nearest-centroid zone, then to 1 when no zones are present.
    /// </summary>
    public int ResolveEtype(double x, double y)
    {
        if (!HasZones || Zones.Count == 0)
            return 1;

        foreach (var zone in Zones)
        {
            if (PolygonElementMatcher.PointInPolygon(x, y, zone.Polygon))
                return zone.Etype;
        }

        // Centroid may straddle a clipped boundary; pick the nearest zone centroid.
        Zone? best = null;
        double bestDistSq = double.PositiveInfinity;
        foreach (var zone in Zones)
        {
            double dx = zone.Centroid.x - x;
            double dy = zone.Centroid.y - y;
            double d = dx * dx + dy * dy;
            if (d < bestDistSq)
            {
                bestDistSq = d;
                best = zone;
            }
        }
        return best?.Etype ?? 1;
    }

    /// <summary>
    /// Builds a plan from the geometry's non-horizontal layer zones. Returns a no-zone plan (success)
    /// when no inclined cuts exist; fails with an error when a zone lacks a material assignment or
    /// references a material id not in the library.
    /// </summary>
    public static bool TryCreatePlan(
        GeometryDefinitionViewModel? geometry,
        MaterialPropertiesViewModel? materials,
        out NonHorizontalLayerMeshPlan plan,
        out string error)
    {
        plan = new NonHorizontalLayerMeshPlan { HasZones = false };
        error = "";

        if (geometry == null)
            return true;

        var rawZones = geometry.GetNonHorizontalLayerZones();
        if (rawZones.Count == 0)
            return true;

        var materialIds = new List<int>(rawZones.Count);
        foreach (var (displayLayer, _, materialId) in rawZones)
        {
            if (materialId is not int id || id <= 0)
            {
                error = $"Non-horizontal layer {displayLayer} has no material assignment. Assign materials in Material Properties before generating the mesh.";
                return false;
            }
            if (materials != null && materials.Materials.All(m => m.Id != id))
            {
                error = $"Non-horizontal layer {displayLayer} references material id {id}, which is not in the material library.";
                return false;
            }
            materialIds.Add(id);
        }

        var materialIdToEtype = materialIds
            .Distinct()
            .OrderBy(i => i)
            .Select((id, idx) => (id, etype: idx + 1))
            .ToDictionary(x => x.id, x => x.etype);

        var zones = new List<Zone>(rawZones.Count);
        foreach (var (displayLayer, polygon, materialId) in rawZones)
        {
            int id = materialId!.Value;
            var poly = polygon.Select(p => (x: p.X, y: p.Y)).ToList();
            zones.Add(new Zone
            {
                DisplayLayer = displayLayer,
                Polygon = poly,
                MaterialId = id,
                Etype = materialIdToEtype[id],
                Centroid = PolygonCentroid(poly),
            });
        }

        plan = new NonHorizontalLayerMeshPlan
        {
            HasZones = true,
            Zones = zones,
            MaterialIdToEtype = materialIdToEtype,
            StatusNote = $"Resolved etype from {zones.Count} non-horizontal layer zone(s).",
        };
        return true;
    }

    private static (double x, double y) PolygonCentroid(IReadOnlyList<(double x, double y)> poly)
    {
        if (poly.Count == 0) return (0, 0);
        double area = 0, cx = 0, cy = 0;
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            var p0 = poly[i];
            var p1 = poly[(i + 1) % n];
            double cross = p0.x * p1.y - p1.x * p0.y;
            area += cross;
            cx += (p0.x + p1.x) * cross;
            cy += (p0.y + p1.y) * cross;
        }
        if (Math.Abs(area) < 1e-12)
        {
            // Degenerate: fall back to vertex average.
            return (poly.Average(p => p.x), poly.Average(p => p.y));
        }
        area *= 0.5;
        return (cx / (6 * area), cy / (6 * area));
    }
}
