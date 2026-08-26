using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Maps mesh elements to Material Properties swatch colors for canvas fill.
/// When horizontal layer cuts exist, resolves by element centroid Y and current layer assignments
/// so canvas colors stay in sync with the Assign button without remeshing.
/// </summary>
public static class MeshElementMaterialFillResolver
{
    public const byte FillAlpha = 140;

    /// <summary>A non-horizontal layer zone with its polygon pre-converted to axis tuples for fast
    /// point-in-zone hit testing. Build once per render via <see cref="BuildNonHorizontalFillZones"/>
    /// and reuse across all elements to avoid per-element allocations.</summary>
    public sealed class NonHorizontalFillZone
    {
        public IReadOnlyList<(double X, double Y)> Polygon { get; init; } = System.Array.Empty<(double, double)>();
        public int? MaterialId { get; init; }
        public int DisplayLayer { get; init; }
    }

    /// <summary>Pre-compute the non-horizontal zone polygons once (per snapshot). Returns null when the
    /// project has no inclined layer cuts so callers can cheaply skip the zone path.</summary>
    public static IReadOnlyList<NonHorizontalFillZone>? BuildNonHorizontalFillZones(GeometryDefinitionViewModel geometry)
    {
        var raw = geometry.GetNonHorizontalLayerZones();
        if (raw.Count == 0)
            return null;

        var list = new List<NonHorizontalFillZone>(raw.Count);
        foreach (var (displayLayer, polygon, materialId) in raw)
        {
            list.Add(new NonHorizontalFillZone
            {
                Polygon = polygon.Select(p => (p.X, p.Y)).ToList(),
                MaterialId = materialId,
                DisplayLayer = displayLayer,
            });
        }
        return list;
    }

    public static SolidColorBrush? ResolveFillBrushForElement(
        MeshInformationRow? infoRow,
        double axisCentroidY,
        int etype,
        MaterialPropertiesViewModel? materials,
        GeometryDefinitionViewModel geometry,
        IReadOnlyList<NonHorizontalFillZone>? cachedNonHorizontalZones = null)
    {
        var color = ResolveColorForElement(infoRow, axisCentroidY, etype, materials, geometry, cachedNonHorizontalZones);
        if (color == default)
            return null;

        var brush = new SolidColorBrush(Color.FromArgb(FillAlpha, color.R, color.G, color.B));
        if (brush.CanFreeze)
            brush.Freeze();
        return brush;
    }

    public static SolidColorBrush? ResolveFillBrush(
        int etype,
        MaterialPropertiesViewModel? materials,
        GeometryDefinitionViewModel geometry)
        => ResolveFillBrushForElement(null, double.NaN, etype, materials, geometry);

    public static Color ResolveColorForElement(
        MeshInformationRow? infoRow,
        double axisCentroidY,
        int etype,
        MaterialPropertiesViewModel? materials,
        GeometryDefinitionViewModel geometry,
        IReadOnlyList<NonHorizontalFillZone>? cachedNonHorizontalZones = null)
    {
        if (TryResolveColorByNonHorizontalZone(infoRow, materials, geometry, cachedNonHorizontalZones, out var zoneColor))
            return zoneColor;

        if (TryResolveColorByLayerCentroid(infoRow, axisCentroidY, materials, geometry, out var layerColor))
            return layerColor;

        return ResolveColor(etype, materials, geometry);
    }

    public static Color ResolveColor(
        int etype,
        MaterialPropertiesViewModel? materials,
        GeometryDefinitionViewModel geometry)
    {
        if (etype <= 0)
            return default;

        if (materials != null && TryResolveMaterialColour(etype, materials, geometry, out var matColor))
            return matColor;

        if (TryResolveSurfaceMaterialColour(etype, materials, geometry, out matColor))
            return matColor;

        return geometry.GetLayerDisplayColor(etype);
    }

    private static bool TryResolveColorByNonHorizontalZone(
        MeshInformationRow? infoRow,
        MaterialPropertiesViewModel? materials,
        GeometryDefinitionViewModel geometry,
        IReadOnlyList<NonHorizontalFillZone>? cachedZones,
        out Color color)
    {
        color = default;
        if (infoRow == null)
            return false;

        var zones = cachedZones ?? BuildNonHorizontalFillZones(geometry);
        if (zones == null || zones.Count == 0)
            return false;

        double cx = ComputeCentroidX(infoRow);
        double cy = ComputeCentroidY(infoRow);
        if (!double.IsFinite(cx) || !double.IsFinite(cy))
            return false;

        foreach (var zone in zones)
        {
            int displayLayer = zone.DisplayLayer;
            int? materialId = zone.MaterialId;
            if (!PolygonElementMatcher.PointInPolygon(cx, cy, zone.Polygon))
                continue;

            if (materials != null && materialId is int id)
            {
                var mat = materials.Materials.FirstOrDefault(m => m.Id == id);
                if (mat != null)
                {
                    color = mat.Colour;
                    return true;
                }
            }

            color = geometry.GetLayerDisplayColor(displayLayer);
            return true;
        }

        return false;
    }

    private static bool TryResolveColorByLayerCentroid(
        MeshInformationRow? infoRow,
        double axisCentroidY,
        MaterialPropertiesViewModel? materials,
        GeometryDefinitionViewModel geometry,
        out Color color)
    {
        color = default;
        if (geometry.MaterialLayerCutElevations.Count == 0)
            return false;

        double cy = infoRow != null ? ComputeCentroidY(infoRow) : axisCentroidY;
        if (!double.IsFinite(cy))
            return false;

        if (Q8MeshMaterialLayerPlanner.TryCreatePlan(geometry, materials, out var plan, out _) && plan.HasLayers)
        {
            int matId = plan.ResolveMaterialId(cy);
            if (materials != null)
            {
                var mat = materials.Materials.FirstOrDefault(m => m.Id == matId);
                if (mat != null)
                {
                    color = mat.Colour;
                    return true;
                }
            }

            int displayLayer = plan.ResolveLayerIndex(cy) + 1;
            color = geometry.GetLayerDisplayColor(displayLayer);
            return true;
        }

        if (!geometry.TryGetDisplayLayerNumberAtAxisY(cy, out int layerAtY))
            return false;

        if (materials != null && geometry.GetLayerMaterialAssignmentMaterialId(layerAtY) is int assignedId)
        {
            var mat = materials.Materials.FirstOrDefault(m => m.Id == assignedId);
            if (mat != null)
            {
                color = mat.Colour;
                return true;
            }
        }

        color = geometry.GetLayerDisplayColor(layerAtY);
        return true;
    }

    private static double ComputeCentroidY(MeshInformationRow row)
    {
        double sum = 0;
        int count = 0;
        void Add(double y)
        {
            if (!double.IsFinite(y)) return;
            sum += y;
            count++;
        }

        Add(row.N1Y);
        Add(row.N2Y);
        Add(row.N3Y);
        if (row.Node4 > 0 || row.IsQuad) Add(row.N4Y);
        return count > 0 ? sum / count : double.NaN;
    }

    private static double ComputeCentroidX(MeshInformationRow row)
    {
        double sum = 0;
        int count = 0;
        void Add(double x)
        {
            if (!double.IsFinite(x)) return;
            sum += x;
            count++;
        }

        Add(row.N1X);
        Add(row.N2X);
        Add(row.N3X);
        if (row.Node4 > 0 || row.IsQuad) Add(row.N4X);
        return count > 0 ? sum / count : double.NaN;
    }

    private static bool TryResolveMaterialColour(
        int etype,
        MaterialPropertiesViewModel materials,
        GeometryDefinitionViewModel geometry,
        out Color color)
    {
        color = default;
        if (!TryResolveMaterialId(etype, materials, geometry, out int materialId))
            return false;

        var mat = materials.Materials.FirstOrDefault(m => m.Id == materialId);
        if (mat == null)
            return false;

        color = mat.Colour;
        return true;
    }

    private static bool TryResolveMaterialId(
        int etype,
        MaterialPropertiesViewModel materials,
        GeometryDefinitionViewModel geometry,
        out int materialId)
    {
        materialId = 0;

        if (Q8MeshMaterialLayerPlanner.TryCreatePlan(geometry, materials, out var plan, out _)
            && plan.MaterialIdToEtype.Count > 0)
        {
            foreach (var kv in plan.MaterialIdToEtype)
            {
                if (kv.Value == etype)
                {
                    materialId = kv.Key;
                    return true;
                }
            }
        }

        if (NonHorizontalLayerMeshPlan.TryCreatePlan(geometry, materials, out var nhPlan, out _)
            && nhPlan.HasZones)
        {
            foreach (var kv in nhPlan.MaterialIdToEtype)
            {
                if (kv.Value == etype)
                {
                    materialId = kv.Key;
                    return true;
                }
            }
        }

        var sorted = materials.Materials.OrderBy(m => m.Id).ToList();
        if (etype >= 1 && etype <= sorted.Count)
        {
            materialId = sorted[etype - 1].Id;
            return true;
        }

        return false;
    }

    private static bool TryResolveSurfaceMaterialColour(
        int surfaceNumber,
        MaterialPropertiesViewModel? materials,
        GeometryDefinitionViewModel geometry,
        out Color color)
    {
        color = default;
        foreach (var entry in geometry.SurfaceMaterialFillsSnapshot)
        {
            if (entry.SurfaceNumber != surfaceNumber)
                continue;

            if (materials != null && entry.MaterialId is int materialId)
            {
                var mat = materials.Materials.FirstOrDefault(m => m.Id == materialId);
                if (mat != null)
                {
                    color = mat.Colour;
                    return true;
                }
            }

            color = Color.FromArgb(
                (byte)(entry.Argb >> 24),
                (byte)(entry.Argb >> 16),
                (byte)(entry.Argb >> 8),
                (byte)entry.Argb);
            return true;
        }

        return false;
    }
}
