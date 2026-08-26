using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Resolves horizontal material layer bands from geometry cuts and assigns Q8 element types
/// so mesh rows align with layer interfaces.
/// </summary>
public static class Q8MeshMaterialLayerPlanner
{
    public const int MaxSubdivisionsPerZone = 200;
    private const double DefaultTol = 1e-4;

    public sealed class LayerPlan
    {
        public bool HasLayers { get; init; }
        public IReadOnlyList<double> BandBreaks { get; init; } = Array.Empty<double>();
        public IReadOnlyList<int> LayerMaterialIds { get; init; } = Array.Empty<int>();
        public IReadOnlyDictionary<int, int> MaterialIdToEtype { get; init; } =
            new Dictionary<int, int>();
        public string? StatusNote { get; init; }

        public int LayerCount => Math.Max(0, BandBreaks.Count - 1);

        public int ResolveLayerIndex(double displayY, double tol = DefaultTol)
        {
            if (BandBreaks.Count < 2)
                return 0;

            for (int i = 0; i < BandBreaks.Count - 1; i++)
            {
                double lo = BandBreaks[i];
                double hi = BandBreaks[i + 1];
                bool isTopBand = i == BandBreaks.Count - 2;
                if (displayY >= lo - tol && (isTopBand ? displayY <= hi + tol : displayY < hi - tol))
                    return i;
            }

            if (displayY < BandBreaks[0])
                return 0;
            return BandBreaks.Count - 2;
        }

        public int ResolveMaterialId(double displayY, double tol = DefaultTol)
        {
            int idx = ResolveLayerIndex(displayY, tol);
            if (LayerMaterialIds.Count == 0)
                return 1;
            if (idx >= 0 && idx < LayerMaterialIds.Count)
                return LayerMaterialIds[idx];
            return LayerMaterialIds[^1];
        }

        public int ResolveEtype(double displayY, double tol = DefaultTol)
        {
            int matId = ResolveMaterialId(displayY, tol);
            return MaterialIdToEtype.TryGetValue(matId, out int etype) ? etype : 1;
        }
    }

    public sealed class EmbankmentAlignment
    {
        public int Ny1 { get; init; }
        public int Ny2 { get; init; }
        public string? AdjustmentNote { get; init; }
    }

    public static bool TryCreatePlan(
        GeometryDefinitionViewModel? geometry,
        MaterialPropertiesViewModel? materials,
        out LayerPlan plan,
        out string error)
    {
        plan = new LayerPlan();
        error = "";

        if (geometry == null || geometry.MaterialLayerCutElevations.Count == 0)
        {
            plan = new LayerPlan { HasLayers = false };
            return true;
        }

        var (_, _, yMin, yMax) = geometry.GetBoundingBoxBounds();
        double eps = Math.Max(DefaultTol, Math.Max(Math.Abs(yMax - yMin), 1.0) * 1e-6);

        var breaks = new List<double> { yMin };
        foreach (double cutY in geometry.MaterialLayerCutElevations.OrderBy(y => y))
        {
            if (cutY <= yMin + eps || cutY >= yMax - eps)
                continue;
            if (breaks.Count == 0 || Math.Abs(cutY - breaks[^1]) > eps)
                breaks.Add(cutY);
        }

        if (breaks.Count == 0 || Math.Abs(yMax - breaks[^1]) > eps)
            breaks.Add(yMax);

        breaks.Sort();
        if (breaks.Count < 2)
        {
            error = "Layer cuts did not define any horizontal material bands.";
            return false;
        }

        int layerCount = breaks.Count - 1;
        var materialIds = new List<int>(layerCount);
        for (int layer = 1; layer <= layerCount; layer++)
        {
            int? matId = geometry.GetLayerMaterialAssignmentMaterialId(layer);
            if (matId is not int id || id <= 0)
            {
                error = $"Layer {layer} has no material assignment. Assign materials in Material Properties before generating the mesh.";
                return false;
            }

            if (materials != null && materials.Materials.All(m => m.Id != id))
            {
                error = $"Layer {layer} references material id {id}, which is not in the material library.";
                return false;
            }

            materialIds.Add(id);
        }

        plan = new LayerPlan
        {
            HasLayers = true,
            BandBreaks = breaks,
            LayerMaterialIds = materialIds,
            MaterialIdToEtype = materialIds
                .Distinct()
                .OrderBy(i => i)
                .Select((id, idx) => (id, etype: idx + 1))
                .ToDictionary(x => x.id, x => x.etype),
        };
        return true;
    }

    public static bool TryAlignEmbankmentSubdivisions(
        LayerPlan plan,
        int ny1,
        int ny2,
        double h1,
        double h2,
        double meshDensity,
        out EmbankmentAlignment alignment,
        out string error)
    {
        alignment = new EmbankmentAlignment { Ny1 = ny1, Ny2 = ny2 };
        error = "";

        if (!plan.HasLayers)
            return true;

        int density = Math.Max(1, (int)Math.Round(meshDensity));
        int baseNy1 = Math.Max(1, ny1 * density);
        int baseNy2 = Math.Max(1, ny2 * density);

        // Smith/display mapping for default embankment: display y in [0,h2] ↔ foundation, [h2,h1+h2] ↔ upper.
        double benchDisplayY = h2;
        var foundationCuts = plan.BandBreaks.Where(y => y < benchDisplayY - DefaultTol).ToList();
        var upperCuts = plan.BandBreaks.Where(y => y > benchDisplayY + DefaultTol && y < h1 + h2 - DefaultTol).ToList();

        int alignedNy2 = baseNy2;
        int alignedNy1 = baseNy1;
        var notes = new List<string>();

        if (foundationCuts.Count > 0 && h2 > DefaultTol)
        {
            int required = MinSubdivisionsForCuts(0, h2, foundationCuts, baseNy2);
            if (required > MaxSubdivisionsPerZone)
            {
                error = $"Foundation zone needs ny2≥{required} to honor layer cuts; reduce cuts or coarsen mesh density (max {MaxSubdivisionsPerZone}).";
                return false;
            }

            if (required > alignedNy2)
            {
                notes.Add($"ny2 {ny2}→{(int)Math.Ceiling(required / (double)density)}");
                alignedNy2 = required;
            }
        }

        if (upperCuts.Count > 0 && h1 > DefaultTol)
        {
            int required = MinSubdivisionsForCuts(h2, h1 + h2, upperCuts, baseNy1);
            if (required > MaxSubdivisionsPerZone)
            {
                error = $"Upper zone needs ny1≥{required} to honor layer cuts; reduce cuts or coarsen mesh density (max {MaxSubdivisionsPerZone}).";
                return false;
            }

            if (required > alignedNy1)
            {
                notes.Add($"ny1 {ny1}→{(int)Math.Ceiling(required / (double)density)}");
                alignedNy1 = required;
            }
        }

        alignment = new EmbankmentAlignment
        {
            Ny1 = alignedNy1,
            Ny2 = alignedNy2,
            AdjustmentNote = notes.Count > 0 ? string.Join("; ", notes) : null,
        };
        return true;
    }

    public static bool TryBuildRectangularYNodes(
        LayerPlan plan,
        double ymin,
        double ymax,
        int baseElementsY,
        double meshDensity,
        out IReadOnlyList<double> yNodes,
        out int elementsY,
        out string? adjustmentNote,
        out string error)
    {
        yNodes = Array.Empty<double>();
        elementsY = baseElementsY;
        adjustmentNote = null;
        error = "";

        if (!plan.HasLayers)
            return true;

        int density = Math.Max(1, (int)Math.Round(meshDensity));
        double eps = Math.Max(DefaultTol, Math.Max(Math.Abs(ymax - ymin), 1.0) * 1e-6);

        var breaks = plan.BandBreaks
            .Where(y => y >= ymin - eps && y <= ymax + eps)
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        if (breaks.Count == 0 || Math.Abs(breaks[0] - ymin) > eps)
            breaks.Insert(0, ymin);
        if (Math.Abs(breaks[^1] - ymax) > eps)
            breaks.Add(ymax);

        breaks = breaks.Distinct().OrderBy(y => y).ToList();
        int nye = breaks.Count - 1;
        if (nye < 1)
        {
            error = "Layer cuts did not intersect the rectangular mesh domain.";
            return false;
        }

        if (nye > MaxSubdivisionsPerZone)
        {
            error = $"Layer-aligned rectangular mesh requires {nye} element rows (max {MaxSubdivisionsPerZone}).";
            return false;
        }

        var nodeYs = new List<double>();
        for (int i = 0; i < breaks.Count - 1; i++)
        {
            double lo = breaks[i];
            double hi = breaks[i + 1];
            if (nodeYs.Count == 0 || Math.Abs(nodeYs[^1] - lo) > eps)
                nodeYs.Add(lo);
            nodeYs.Add(0.5 * (lo + hi));
        }

        nodeYs.Add(breaks[^1]);
        yNodes = nodeYs;
        elementsY = nye;
        if (nye != baseElementsY * density)
            adjustmentNote = $"elements Y {baseElementsY}→{nye} (layer-aligned)";

        return true;
    }

    /// <summary>
    /// Assigns <see cref="MeshInformationRow.Etype"/> from horizontal layer bands before p610 export.
    /// </summary>
    public static bool TryRefreshMeshInformationEtypes(
        IList<MeshInformationRow> rows,
        GeometryDefinitionViewModel? geometry,
        MaterialPropertiesViewModel? materials,
        out string? statusNote)
    {
        statusNote = null;
        if (rows == null || rows.Count == 0)
            return false;
        if (!TryCreatePlan(geometry, materials, out var plan, out var error))
        {
            statusNote = error;
            return false;
        }

        if (!plan.HasLayers)
            return false;

        ApplyElementTypesToMeshRows(rows, plan);
        statusNote = $"Assigned etype from {plan.LayerCount} material layer band(s).";
        return true;
    }

    public static void ApplyElementTypesToMeshRows(IList<MeshInformationRow> rows, LayerPlan plan)
    {
        if (!plan.HasLayers)
            return;

        foreach (var row in rows)
        {
            double cy = ComputeMeshRowCentroidY(row);
            if (!double.IsFinite(cy))
                continue;
            row.Etype = plan.ResolveEtype(cy);
        }
    }

    private static double ComputeMeshRowCentroidY(MeshInformationRow row)
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

    public static P64EmbankmentMeshBuilder.MeshResult ApplyElementTypes(
        P64EmbankmentMeshBuilder.MeshResult mesh,
        LayerPlan plan,
        double coordShift)
    {
        if (!plan.HasLayers)
            return mesh;

        var nodeMap = mesh.Nodes.ToDictionary(n => n.id, n => (n.x, n.y));
        var updated = new List<(int id, int[] conn, int etype)>(mesh.Elements.Count);
        foreach (var (id, conn, _) in mesh.Elements)
        {
            double cy = 0;
            int count = Math.Min(8, conn.Length);
            for (int k = 0; k < count; k++)
            {
                if (!nodeMap.TryGetValue(conn[k], out var p))
                    continue;
                cy += p.y + coordShift;
            }

            cy /= Math.Max(1, count);
            int etype = plan.ResolveEtype(cy);
            updated.Add((id, conn, etype));
        }

        return new P64EmbankmentMeshBuilder.MeshResult
        {
            Nels = mesh.Nels,
            Nn = mesh.Nn,
            Nodes = mesh.Nodes,
            Elements = updated,
            BoundaryConditions = mesh.BoundaryConditions,
        };
    }

    public static double ComputeCoordShift(P64EmbankmentMeshBuilder.MeshResult mesh) =>
        mesh.Nodes.Min(n => n.y) < 0 ? -mesh.Nodes.Min(n => n.y) : 0;

    private static int MinSubdivisionsForCuts(double zoneLo, double zoneHi, IReadOnlyList<double> cuts, int minNy)
    {
        double height = zoneHi - zoneLo;
        if (height <= DefaultTol)
            return minNy;

        int required = minNy;
        foreach (double cut in cuts)
        {
            if (cut <= zoneLo + DefaultTol || cut >= zoneHi - DefaultTol)
                continue;

            double frac = (cut - zoneLo) / height;
            int best = required;
            for (int ny = required; ny <= MaxSubdivisionsPerZone; ny++)
            {
                double k = frac * ny;
                if (Math.Abs(k - Math.Round(k)) <= DefaultTol * Math.Max(ny, 1))
                {
                    best = ny;
                    break;
                }
            }

            required = Math.Max(required, best);
        }

        return required;
    }
}
