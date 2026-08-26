using System.Globalization;
using System.Text;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Services.Wave3;

public static class FeAnsysExporter
{
    public static string Build(IReadOnlyList<Node> nodes, IReadOnlyList<ElementBase> elements)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/PREP7");
        sb.AppendLine("! CPP_2DFEM_SLOPE ANSYS CDB-style export");
        sb.AppendLine("ET,1,PLANE182");
        sb.AppendLine("KEYOPT,1,3,2");
        foreach (var n in nodes)
            sb.AppendLine($"N,{n.Id},{F(n.X)},{F(n.Y)},0");

        foreach (var e in elements.Where(e => e.NodeIds.Length >= 3))
        {
            var ids = e.NodeIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
            while (ids.Count < 8) ids.Add("0");
            sb.Append("EN,").Append(e.Id);
            foreach (var id in ids.Take(8))
                sb.Append(',').Append(id);
            sb.AppendLine();
        }

        sb.AppendLine("FINISH");
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
