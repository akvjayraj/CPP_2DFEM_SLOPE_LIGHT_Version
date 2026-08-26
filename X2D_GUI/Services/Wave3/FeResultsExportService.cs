using System.Globalization;
using System.Text;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Services.Wave3;

public static class FeResultsExportService
{
    public static string BuildNodalCsv(IReadOnlyList<Node> nodes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,X,Y,Ux,Uy,Umag");
        foreach (var n in nodes)
        {
            var ux = n.Displacements.Length > 0 ? n.Displacements[0] : 0;
            var uy = n.Displacements.Length > 1 ? n.Displacements[1] : 0;
            sb.Append(n.Id).Append(',')
                .Append(F(n.X)).Append(',')
                .Append(F(n.Y)).Append(',')
                .Append(F(ux)).Append(',')
                .Append(F(uy)).Append(',')
                .AppendLine(F(Math.Sqrt(ux * ux + uy * uy)));
        }
        return sb.ToString();
    }

    public static string BuildElementCsv(
        IReadOnlyList<ElementBase> elements,
        IReadOnlyList<ElementStress>? stresses)
    {
        var byId = (stresses ?? Array.Empty<ElementStress>()).ToDictionary(s => s.ElementId);
        var sb = new StringBuilder();
        sb.AppendLine("ElementId,Active,NodeIds,StressXX,StressYY,StressXY,VonMises,YieldRatio,PorePressure");
        foreach (var e in elements)
        {
            byId.TryGetValue(e.Id, out var s);
            sb.Append(e.Id).Append(',')
                .Append(e.IsActive ? 1 : 0).Append(',')
                .Append('"').Append(string.Join(' ', e.NodeIds)).Append('"').Append(',')
                .Append(F(s?.StressXX ?? 0)).Append(',')
                .Append(F(s?.StressYY ?? 0)).Append(',')
                .Append(F(s?.StressXY ?? 0)).Append(',')
                .Append(F(s?.VonMisesStress ?? 0)).Append(',')
                .Append(F(s?.YieldRatio ?? 0)).Append(',')
                .AppendLine(F(s?.PorePressure ?? 0));
        }
        return sb.ToString();
    }

    public static string BuildVtk(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> elements,
        IReadOnlyList<ElementStress>? stresses)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# vtk DataFile Version 3.0");
        sb.AppendLine("CPP_2DFEM_SLOPE results");
        sb.AppendLine("ASCII");
        sb.AppendLine("DATASET UNSTRUCTURED_GRID");
        sb.AppendLine($"POINTS {nodes.Count} double");
        var indexOf = new Dictionary<int, int>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            indexOf[nodes[i].Id] = i;
            sb.Append(F(nodes[i].X)).Append(' ')
                .Append(F(nodes[i].Y)).AppendLine(" 0");
        }

        var conn = new List<int>();
        var types = new List<int>();
        foreach (var e in elements)
        {
            var ids = e.NodeIds;
            if (ids.Length < 3) continue;
            var mapped = new int[ids.Length];
            var ok = true;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!indexOf.TryGetValue(ids[i], out var idx)
                    && (ids[i] < 0 || ids[i] >= nodes.Count))
                {
                    ok = false;
                    break;
                }
                mapped[i] = indexOf.TryGetValue(ids[i], out var found) ? found : ids[i];
            }
            if (!ok) continue;
            conn.Add(mapped.Length);
            conn.AddRange(mapped);
            types.Add(VtkCellType(mapped.Length));
        }

        sb.AppendLine($"CELLS {types.Count} {conn.Count}");
        for (var i = 0; i < conn.Count; )
        {
            var n = conn[i];
            sb.Append(n);
            for (var k = 0; k < n; k++)
                sb.Append(' ').Append(conn[i + 1 + k]);
            sb.AppendLine();
            i += n + 1;
        }

        sb.AppendLine($"CELL_TYPES {types.Count}");
        foreach (var t in types)
            sb.AppendLine(t.ToString(CultureInfo.InvariantCulture));

        sb.AppendLine($"POINT_DATA {nodes.Count}");
        sb.AppendLine("VECTORS displacement double");
        foreach (var n in nodes)
        {
            var ux = n.Displacements.Length > 0 ? n.Displacements[0] : 0;
            var uy = n.Displacements.Length > 1 ? n.Displacements[1] : 0;
            sb.Append(F(ux)).Append(' ').Append(F(uy)).AppendLine(" 0");
        }

        if (stresses != null && stresses.Count > 0)
        {
            var byId = stresses.ToDictionary(s => s.ElementId);
            sb.AppendLine($"CELL_DATA {types.Count}");
            WriteCellScalars(sb, "von_mises", elements, byId, s => s.VonMisesStress);
            WriteCellScalars(sb, "yield_ratio", elements, byId, s => s.YieldRatio);
        }

        return sb.ToString();
    }

    private static void WriteCellScalars(
        StringBuilder sb,
        string name,
        IReadOnlyList<ElementBase> elements,
        Dictionary<int, ElementStress> byId,
        Func<ElementStress, double> pick)
    {
        sb.AppendLine($"SCALARS {name} double 1");
        sb.AppendLine("LOOKUP_TABLE default");
        foreach (var e in elements)
        {
            if (e.NodeIds.Length < 3) continue;
            sb.AppendLine(F(byId.TryGetValue(e.Id, out var s) ? pick(s) : 0));
        }
    }

    private static int VtkCellType(int nodeCount) => nodeCount switch
    {
        3 => 5,   // VTK_TRIANGLE
        4 => 9,   // VTK_QUAD
        6 => 22,  // VTK_QUADRATIC_TRIANGLE
        8 => 23,  // VTK_QUADRATIC_QUAD
        _ => 7,   // VTK_POLYGON
    };

    private static string F(double v) => v.ToString("G17", CultureInfo.InvariantCulture);
}
