using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Infers horizontal material layers from explicit p64 <c>etype</c> blocks and mesh element centroids,
/// then drives Material Properties layering controls and layer–material assignments.
/// </summary>
public static class DatEtypeLayeringSync
{
    private const double DefaultBandTolerance = 1e-4;

    /// <summary>
    /// Horizontal (Y-band) layering only: materials at the same elevation but different X collapse via majority vote.
    /// </summary>
    public static bool TrySyncLayeringFromMeshEtype(
        string? datContent,
        MeshViewModel mesh,
        MaterialPropertiesViewModel materials,
        GeometryDefinitionViewModel geometry,
        out string message)
    {
        message = "";
        if (mesh is not { IsDiscretized: true } || mesh.MeshInformationRows.Count == 0)
        {
            message = "Layer sync skipped: mesh not discretized.";
            return false;
        }

        int npTypes = ResolveNpTypes(datContent, materials);
        if (npTypes <= 1)
        {
            message = "Layer sync skipped: single material type (np_types = 1).";
            return false;
        }

        var rows = mesh.MeshInformationRows;
        if (!rows.Any(r => r.Etype > 0))
        {
            message = "Layer sync skipped: mesh rows have no element type (etype).";
            return false;
        }

        if (ShouldReimportGeometryFromMesh(datContent, mesh, geometry))
        {
            geometry.ImportBoundaryFromMesh();
            if (geometry.GeometryVertexRows.Count < 3)
            {
                message = "Layer sync skipped: could not import geometry from mesh boundary (need closed outline).";
                return false;
            }
        }
        else
        {
            geometry.RebuildGeometryAfterProjectLoad();
        }

        var samples = rows
            .Select(r => (Cy: ElementCentroidY(r), Etype: r.Etype > 0 ? r.Etype : 1))
            .ToList();

        if (!TryInferInteriorCutElevations(samples, out var cuts))
        {
            message = "Layer sync skipped: could not infer horizontal material boundaries from etype.";
            return false;
        }

        geometry.ApplyInferredMaterialLayerCuts(cuts);
        ApplyAssignmentsFromEtype(samples, geometry, materials);

        MeshCanvasAxisFitter.FitToDiscretizationNodes(geometry, mesh.DiscretizationNodes);

        if (materials.LayerAssignments.Count > 0)
        {
            materials.SelectedLayerAssignmentRow = materials.LayerAssignments[0];
            materials.SynchronizeMaterialAssignmentCanvasPreview();
        }

        if (geometry.LayerCoordinateSummaries.Count > 0
            && geometry.TryGetHorizontalStripElevationRange(1, out _, out var topY))
        {
            geometry.LayerElevationInput = topY.ToString("G6", CultureInfo.InvariantCulture);
        }

        message =
            $"Layering synced from script etype: {geometry.LayerCoordinateSummaries.Count} layer(s), " +
            $"{cuts.Count} cut elevation(s).";
        return true;
    }

    /// <summary>
    /// True when script mesh should replace stale/custom geometry outline (orphan vertices, explicit p64 deck, bbox mismatch).
    /// </summary>
    private static bool ShouldReimportGeometryFromMesh(
        string? datContent,
        MeshViewModel mesh,
        GeometryDefinitionViewModel geometry)
    {
        if (geometry.GeometryVertexRows.Count < 3)
            return true;

        if (!string.IsNullOrWhiteSpace(datContent)
            && DatMeshBoundaryImporter.IsExplicitMeshSourceDeck(datContent))
            return true;

        if (!TryGetMeshNodeBounds(mesh, out var mxMin, out var mxMax, out var myMin, out var myMax))
            return false;

        var (gxMin, gxMax, gyMin, gyMax) = geometry.GetBoundingBoxBounds();
        var meshW = Math.Max(mxMax - mxMin, 1e-9);
        var meshH = Math.Max(myMax - myMin, 1e-9);
        var geomW = Math.Max(gxMax - gxMin, 1e-9);
        var geomH = Math.Max(gyMax - gyMin, 1e-9);

        if (geomW > meshW * 1.10 || geomH > meshH * 1.10)
            return true;

        const double margin = 0.05;
        if (gxMin < mxMin - meshW * margin || gxMax > mxMax + meshW * margin
            || gyMin < myMin - meshH * margin || gyMax > myMax + meshH * margin)
            return true;

        // Residual bulk-delete / orphan vertices: many geometry points vs mesh node count.
        if (geometry.GeometryVertexRows.Count > mesh.DiscretizationNodes.Count * 0.5
            && geometry.GeometryVertexRows.Count > 80)
            return true;

        return false;
    }

    private static bool TryGetMeshNodeBounds(
        MeshViewModel mesh,
        out double minX,
        out double maxX,
        out double minY,
        out double maxY)
    {
        minX = maxX = minY = maxY = 0;
        if (mesh.DiscretizationNodes.Count == 0)
            return false;

        minX = double.MaxValue;
        maxX = double.MinValue;
        minY = double.MaxValue;
        maxY = double.MinValue;
        foreach (var n in mesh.DiscretizationNodes)
        {
            if (n.Location.X < minX) minX = n.Location.X;
            if (n.Location.X > maxX) maxX = n.Location.X;
            if (n.Location.Y < minY) minY = n.Location.Y;
            if (n.Location.Y > maxY) maxY = n.Location.Y;
        }

        return maxX > minX && maxY > minY;
    }

    private static int ResolveNpTypes(string? datContent, MaterialPropertiesViewModel materials)
    {
        if (!string.IsNullOrWhiteSpace(datContent)
            && DatMeshBoundaryImporter.TryGetExplicitMeshCounts(datContent, out _, out _)
            && TryReadNpTypesFromDat(datContent, out int np))
            return np;
        return Math.Max(1, materials.Materials.Count);
    }

    private static bool TryReadNpTypesFromDat(string datContent, out int npTypes)
    {
        npTypes = 0;
        var sanitized = CppAnalysisRunner.StripFortranFullLineComments(datContent);
        var tokens = Tokenise(sanitized);
        if (tokens.Count < 3)
            return false;
        if (tokens[0].Equals("p64", StringComparison.OrdinalIgnoreCase))
            tokens.RemoveAt(0);
        if (!int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out npTypes))
            return false;
        return npTypes >= 1;
    }

    private static List<string> Tokenise(string content)
    {
        var list = new List<string>();
        foreach (var part in content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            list.Add(part);
        return list;
    }

    private static double ElementCentroidY(MeshInformationRow r)
    {
        var ys = new List<double> { r.N1Y, r.N2Y, r.N3Y };
        if (r.Node4 > 0) ys.Add(r.N4Y);
        if (r.Node5 > 0) ys.Add(r.N5Y);
        if (r.Node6 > 0) ys.Add(r.N6Y);
        if (r.Node7 > 0) ys.Add(r.N7Y);
        if (r.Node8 > 0) ys.Add(r.N8Y);
        return ys.Count > 0 ? ys.Average() : 0;
    }

    private static bool TryInferInteriorCutElevations(
        IReadOnlyList<(double Cy, int Etype)> samples,
        out List<double> cuts)
    {
        cuts = new List<double>();
        if (samples.Count == 0)
            return false;

        var ordered = samples.OrderBy(s => s.Cy).ToList();
        var runs = new List<(int Etype, double MinY, double MaxY)>();
        var et = ordered[0].Etype;
        var minY = ordered[0].Cy;
        var maxY = ordered[0].Cy;

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Etype != et)
            {
                runs.Add((et, minY, maxY));
                et = ordered[i].Etype;
                minY = ordered[i].Cy;
                maxY = ordered[i].Cy;
            }
            else
            {
                maxY = ordered[i].Cy;
            }
        }

        runs.Add((et, minY, maxY));
        if (runs.Count < 2)
            return false;

        var yMin = samples.Min(s => s.Cy);
        var yMax = samples.Max(s => s.Cy);
        var eps = Math.Max(1e-9, (yMax - yMin) * 1e-9);

        for (var i = 0; i < runs.Count - 1; i++)
        {
            var cut = (runs[i].MaxY + runs[i + 1].MinY) * 0.5;
            if (cut <= yMin + eps || cut >= yMax - eps)
                continue;
            if (cuts.Count > 0 && Math.Abs(cut - cuts[^1]) <= eps)
                continue;
            cuts.Add(cut);
        }

        return cuts.Count > 0;
    }

    private static void ApplyAssignmentsFromEtype(
        IReadOnlyList<(double Cy, int Etype)> samples,
        GeometryDefinitionViewModel geometry,
        MaterialPropertiesViewModel materials)
    {
        geometry.ClearLayerMaterialAssignments();
        var eps = DefaultBandTolerance;

        foreach (var summary in geometry.LayerCoordinateSummaries)
        {
            if (!geometry.TryGetHorizontalStripElevationRange(summary.DisplayLayerNumber, out var yLo, out var yHi))
                continue;

            var etype = MajorityEtypeInBand(samples, yLo, yHi, eps);
            geometry.SetLayerMaterialAssignmentMaterialId(summary.DisplayLayerNumber, etype);

            var mat = materials.Materials.FirstOrDefault(m => m.Id == etype);
            if (mat != null)
            {
                if (!string.IsNullOrWhiteSpace(mat.Name))
                    geometry.SetLayerDisplayName(summary.DisplayLayerNumber, mat.Name);
                geometry.SetLayerDisplayColor(summary.DisplayLayerNumber, mat.Colour);
            }
        }

        materials.RebuildLayerAssignments();
    }

    private static int MajorityEtypeInBand(
        IReadOnlyList<(double Cy, int Etype)> samples,
        double yLo,
        double yHi,
        double eps)
    {
        var counts = new Dictionary<int, int>();
        foreach (var (cy, etype) in samples)
        {
            if (cy < yLo - eps || cy > yHi + eps)
                continue;
            counts.TryGetValue(etype, out var c);
            counts[etype] = c + 1;
        }

        if (counts.Count == 0)
            return 1;

        return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
    }
}
