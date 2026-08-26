using System;
using System.Collections.Generic;
using System.Linq;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Backend;

/// <summary>Validates that mesh elements do not span horizontal material layer interfaces.</summary>
public static class Q8MeshLayerValidator
{
    public sealed class ValidationResult
    {
        public bool IsValid { get; init; }
        public IReadOnlyList<int> CrossingElementIds { get; init; } = Array.Empty<int>();
        public string Message { get; init; } = "";
    }

    /// <summary>Validate unstructured GUI mesh rows (T3/T6/Q4/Q8) against horizontal layer bands.</summary>
    public static ValidationResult ValidateMeshInformationRows(
        IEnumerable<MeshInformationRow> rows,
        Q8MeshMaterialLayerPlanner.LayerPlan plan,
        double tol = 1e-4)
    {
        if (!plan.HasLayers || plan.BandBreaks.Count < 2)
        {
            return new ValidationResult
            {
                IsValid = true,
                Message = "Single-material mesh (no layer cuts).",
            };
        }

        var crossing = new List<int>();
        foreach (var row in rows)
        {
            if (row == null || row.ElementId <= 0) continue;
            var ys = CollectCornerYs(row);
            if (ys.Count == 0) continue;

            double minY = ys.Min();
            double maxY = ys.Max();
            bool straddlesCut = plan.BandBreaks.Any(cut =>
                cut > minY + tol && cut < maxY - tol);

            if (straddlesCut)
                crossing.Add(row.ElementId);
        }

        if (crossing.Count == 0)
        {
            return new ValidationResult
            {
                IsValid = true,
                Message = $"Layer check passed ({plan.LayerCount} horizontal layer(s)).",
            };
        }

        string sample = string.Join(", ", crossing.Take(8));
        if (crossing.Count > 8)
            sample += ", …";

        return new ValidationResult
        {
            IsValid = false,
            CrossingElementIds = crossing,
            Message = $"{crossing.Count} element(s) cross a material layer interface (ids: {sample}). Refine discretization or adjust layer cut elevations.",
        };
    }

    private static List<double> CollectCornerYs(MeshInformationRow row)
    {
        var ys = new List<double>(8);
        void Add(double y) { if (!double.IsNaN(y) && !double.IsInfinity(y)) ys.Add(y); }

        Add(row.N1Y); Add(row.N2Y); Add(row.N3Y);
        if (row.Node4 > 0 || row.IsQuad) Add(row.N4Y);
        if (row.IsQ8)
        {
            Add(row.N5Y); Add(row.N6Y); Add(row.N7Y); Add(row.N8Y);
        }

        return ys;
    }

    public static ValidationResult Validate(
        P64EmbankmentMeshBuilder.MeshResult mesh,
        Q8MeshMaterialLayerPlanner.LayerPlan plan,
        double coordShift,
        double tol = 1e-4,
        double? horizontalZoneMaxX = null)
    {
        if (!plan.HasLayers || plan.BandBreaks.Count < 2)
        {
            return new ValidationResult
            {
                IsValid = true,
                Message = "Single-material mesh (no layer cuts).",
            };
        }

        var nodeMap = mesh.Nodes.ToDictionary(n => n.id, n => (x: n.x, y: n.y + coordShift));
        var crossing = new List<int>();

        foreach (var (id, conn, _) in mesh.Elements)
        {
            double minY = double.PositiveInfinity;
            double maxY = double.NegativeInfinity;
            var nodeYs = new List<double>();
            for (int k = 0; k < Math.Min(8, conn.Length); k++)
            {
                if (!nodeMap.TryGetValue(conn[k], out var p))
                    continue;

                if (horizontalZoneMaxX is double xMax && p.x > xMax + tol)
                {
                    nodeYs.Clear();
                    break;
                }

                nodeYs.Add(p.y);
                minY = Math.Min(minY, p.y);
                maxY = Math.Max(maxY, p.y);
            }

            if (nodeYs.Count == 0)
                continue;

            bool straddlesCut = plan.BandBreaks.Any(cut =>
                cut > minY + tol && cut < maxY - tol);

            if (straddlesCut)
            {
                crossing.Add(id);
            }
        }

        if (crossing.Count == 0)
        {
            return new ValidationResult
            {
                IsValid = true,
                Message = $"Layer check passed ({plan.LayerCount} horizontal layer(s)).",
            };
        }

        string sample = string.Join(", ", crossing.Take(8));
        if (crossing.Count > 8)
            sample += ", …";

        return new ValidationResult
        {
            IsValid = false,
            CrossingElementIds = crossing,
            Message = $"{crossing.Count} element(s) cross a material layer interface (ids: {sample}). Adjust subdivisions or layer cut elevations.",
        };
    }
}
