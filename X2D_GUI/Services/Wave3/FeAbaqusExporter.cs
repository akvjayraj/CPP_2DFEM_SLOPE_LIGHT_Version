using System.Globalization;
using System.Text;
using GeotechSlopeApp.Models;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Services.Wave3;

public static class FeAbaqusExporter
{
    public static string Build(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<ElementBase> elements,
        IReadOnlyList<MaterialModel>? materials = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("*Heading");
        sb.AppendLine("CPP_2DFEM_SLOPE Abaqus export");
        sb.AppendLine("*Preprint, echo=NO, model=NO, history=NO, contact=NO");
        sb.AppendLine("*Node");
        foreach (var n in nodes)
            sb.AppendLine($"{n.Id}, {F(n.X)}, {F(n.Y)}, 0");

        var groups = elements
            .Where(e => e.NodeIds.Length >= 3)
            .GroupBy(e => AbaqusType(e.NodeIds.Length));
        var elset = 1;
        foreach (var g in groups)
        {
            sb.AppendLine($"*Element, type={g.Key}, elset=ESET{elset}");
            foreach (var e in g)
                sb.AppendLine($"{e.Id}, {string.Join(", ", e.NodeIds.Select(id => id.ToString(CultureInfo.InvariantCulture)))}");
            elset++;
        }

        if (materials != null)
        {
            foreach (var m in materials)
            {
                sb.AppendLine($"*Material, name={Sanitize(m.Name)}");
                sb.AppendLine("*Elastic");
                sb.AppendLine($"{F(m.YoungsModulus)}, {F(m.PoissonsRatio)}");
                sb.AppendLine("*Density");
                sb.AppendLine(F(m.Density > 0 ? m.Density : m.UnitWeight * 1000.0 / 9.81));
                sb.AppendLine("*Mohr Coulomb");
                sb.AppendLine($"{F(m.FrictionAngle)}, {F(m.DilationAngle)}");
                sb.AppendLine("*Mohr Coulomb Hardening");
                sb.AppendLine($"{F(m.Cohesion)}, 0");
            }
        }

        sb.AppendLine("*Step, name=Gravity");
        sb.AppendLine("*Static");
        sb.AppendLine("1., 1., 1e-05, 1.");
        sb.AppendLine("*Dload");
        sb.AppendLine(", GRAV, 9.81, 0, -1, 0");
        sb.AppendLine("*End Step");
        return sb.ToString();
    }

    private static string AbaqusType(int n) => n switch
    {
        3 => "CPE3",
        4 => "CPE4",
        6 => "CPE6",
        8 => "CPE8",
        _ => "CPE3",
    };

    private static string Sanitize(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var s = new string(chars);
        return string.IsNullOrWhiteSpace(s) ? "MAT" : s;
    }

    private static string F(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
}
