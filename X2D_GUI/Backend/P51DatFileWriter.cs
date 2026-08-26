using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Writes p51 (plane/axisymmetric strain) .dat files for the C++ FEM backend.
/// Supports rectangular meshes with 3-node triangles.
/// </summary>
public class P51DatFileWriter
{
    /// <summary>
    /// Input data for p51 analysis.
    /// </summary>
    public class P51Input
    {
        public string Type2D { get; set; } = "plane";  // "plane" or "axisymmetric"

        /// <summary>
        /// When true, line 1 is <c>type_2d</c> and line 2 is <c>'Plane'</c> or <c>'Axisymmetric'</c> (p51 reads both formats).
        /// </summary>
        public bool UseType2dHeaderLines { get; set; }
        public string Element { get; set; } = "triangle";
        public int Nod { get; set; } = 3;
        public char Dir { get; set; } = 'x';  // 'x' or 'y'
        public int Nxe { get; set; }
        public int Nye { get; set; }
        public int Nip { get; set; } = 1;
        public int NpTypes { get; set; } = 1;
        public double YoungsModulus { get; set; } = 1e6;
        public double PoissonsRatio { get; set; } = 0.3;
        public bool EmitExplanatoryComments { get; set; } = true;
        public IReadOnlyList<MaterialProperty> MaterialProperties { get; set; } = new List<MaterialProperty>();
        public IReadOnlyList<int> ElementMaterialTypes { get; set; } = new List<int>();
        public IReadOnlyList<double> XCoords { get; set; } = new List<double>();
        public IReadOnlyList<double> YCoords { get; set; } = new List<double>();
        /// <summary>Restrained nodes: (nodeId 1-based, nf_x, nf_y) where 0=fixed, 1=free.</summary>
        public IReadOnlyList<(int NodeId, int NfX, int NfY)> RestrainedNodes { get; set; } = new List<(int, int, int)>();
        /// <summary>Loaded nodes: (nodeId 1-based, loadX, loadY).</summary>
        public IReadOnlyList<(int NodeId, double LoadX, double LoadY)> LoadedNodes { get; set; } = new List<(int, double, double)>();
        /// <summary>Fixed freedoms: (nodeId, sense 1=x 2=y, value). Usually empty when using restrained nodes.</summary>
        public IReadOnlyList<(int NodeId, int Sense, double Value)> FixedFreedoms { get; set; } = new List<(int, int, double)>();
    }

    public sealed record MaterialProperty(double YoungsModulus, double PoissonsRatio, string Label);

    /// <summary>
    /// Build P51Input from a rectangular mesh definition (CreateRectangularMesh style).
    /// </summary>
    public static P51Input FromRectangularMesh(
        double width, double height,
        int nxe, int nye,
        double youngsModulus = 1e6,
        double poissonsRatio = 0.3,
        IReadOnlyList<(int NodeId, int NfX, int NfY)>? restrainedNodes = null,
        IReadOnlyList<(int NodeId, double LoadX, double LoadY)>? loadedNodes = null,
        char dir = 'x')
    {
        var xCoords = new List<double>();
        var yCoords = new List<double>();
        for (int i = 0; i <= nxe; i++)
            xCoords.Add(width * i / nxe);
        for (int i = 0; i <= nye; i++)
            yCoords.Add(-height * i / nye);  // Y down for geotechnical convention

        return new P51Input
        {
            Type2D = "plane",
            Element = "triangle",
            Nod = 3,
            Dir = dir is 'x' or 'y' or 'r' ? dir : 'x',
            Nxe = nxe,
            Nye = nye,
            Nip = 1,
            NpTypes = 1,
            YoungsModulus = youngsModulus,
            PoissonsRatio = poissonsRatio,
            XCoords = xCoords,
            YCoords = yCoords,
            RestrainedNodes = restrainedNodes ?? new List<(int, int, int)>(),
            LoadedNodes = loadedNodes ?? new List<(int, double, double)>(),
            FixedFreedoms = new List<(int, int, double)>()
        };
    }

    /// <summary>
    /// Write .dat file to the given path.
    /// </summary>
    public void Write(string path, P51Input input)
    {
        using var sw = new StreamWriter(path, false, Encoding.ASCII);
        Write(sw, input);
    }

    /// <summary>
    /// Write .dat content to a TextWriter.
    /// </summary>
    public void Write(TextWriter writer, P51Input input)
    {
        void Comment(string text)
        {
            if (input.EmitExplanatoryComments)
                writer.WriteLine("! " + text);
        }

        void Comments(params string[] lines)
        {
            foreach (var line in lines) Comment(line);
        }

        if (input.UseType2dHeaderLines)
        {
            Comments(
                "P51 analysis type block:",
                "type_2d keyword followed by plane or axisymmetric analysis type.");
            writer.WriteLine("type_2d");
            writer.WriteLine(input.Type2D == "axisymmetric" ? "'Axisymmetric'" : "'Plane'");
        }
        else
        {
            Comments(
                "P51 analysis type block:",
                "'plane' = plane strain; 'axisymmetric' = radial/z axisymmetric strain.");
            writer.WriteLine($"'{input.Type2D}'");
        }

        Comments(
            "P51 element definition block:",
            "element nod dir",
            "element = triangle or quadrilateral family",
            "nod = nodes per element",
            "dir = mesh numbering direction");
        writer.WriteLine($"'{input.Element}'   {input.Nod}   '{input.Dir}'");

        Comments(
            "P51 grid/material-count block:",
            "nxe nye nip np_types",
            "nxe = number of x-direction element divisions",
            "nye = number of y-direction element divisions",
            "nip = number of integration points",
            "np_types = number of material/property types");
        writer.WriteLine($"{input.Nxe}  {input.Nye}  {input.Nip}  {input.NpTypes}");

        var materials = BuildMaterialRows(input);
        Comments(
            "P51 material/property block:",
            "One row per material type: Young's modulus E and Poisson ratio nu.");
        for (int j = 0; j < materials.Count; j++)
        {
            var material = materials[j];
            writer.WriteLine($"{Format(material.YoungsModulus)}  {Format(material.PoissonsRatio)}");
        }

        if (input.NpTypes > 1)
        {
            int nels = input.Nxe * input.Nye * 2;  // right-angled triangular mesh
            var etype = BuildElementTypes(input, nels);
            Comments(
                "P51 element material-type block:",
                "etype values assign one material ID to each element.",
                "1 = first material row, 2 = second material row, and so on.");
            for (int start = 0; start < etype.Count; start += 20)
            {
                var row = etype.Skip(start).Take(20).ToArray();
                writer.WriteLine(string.Join(" ", row));
            }
        }

        Comments(
            "P51 x-coordinate block:",
            "x_coords = grid x coordinates in model units.");
        writer.WriteLine(string.Join("  ", input.XCoords.Select(Format)));

        Comments(
            "P51 y-coordinate block:",
            "y_coords = grid y coordinates in model units.");
        writer.WriteLine(string.Join("  ", input.YCoords.Select(Format)));

        Comments(
            "P51 boundary condition block:",
            "First line is nr = number of restrained nodes.",
            "Each following row is: node_id nf_x nf_y",
            "0 = fixed/restrained, 1 = free.");
        writer.WriteLine(input.RestrainedNodes.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var (nodeId, nfX, nfY) in input.RestrainedNodes)
        {
            writer.WriteLine($"{nodeId} {nfX} {nfY}");
        }

        Comments(
            "P51 nodal load block:",
            "First line is loaded_nodes = number of nodal load rows.",
            "Each following row is: node_id load_x load_y.");
        writer.WriteLine(input.LoadedNodes.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var (nodeId, loadX, loadY) in input.LoadedNodes)
        {
            writer.WriteLine($"{nodeId}  {Format(loadX)} {Format(loadY)}");
        }

        Comments(
            "P51 fixed freedom block:",
            "fixed_freedoms = number of prescribed displacement rows.",
            "Each following row is: node_id sense value, where sense 1=x and 2=y.");
        writer.WriteLine(input.FixedFreedoms.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (nodeId, sense, value) in input.FixedFreedoms)
        {
            writer.WriteLine($"{nodeId}  {sense}  {Format(value)}");
        }
    }

    private static string Format(double value)
        => value.ToString("G12", CultureInfo.InvariantCulture);

    private static IReadOnlyList<MaterialProperty> BuildMaterialRows(P51Input input)
    {
        if (input.MaterialProperties.Count >= input.NpTypes)
            return input.MaterialProperties.Take(input.NpTypes).ToArray();

        var rows = new List<MaterialProperty>(input.MaterialProperties);
        while (rows.Count < input.NpTypes)
        {
            rows.Add(new MaterialProperty(
                input.YoungsModulus,
                input.PoissonsRatio,
                rows.Count == 0 ? "Default elastic material" : $"Material type {rows.Count + 1}"));
        }
        return rows;
    }

    private static IReadOnlyList<int> BuildElementTypes(P51Input input, int nels)
    {
        if (input.ElementMaterialTypes.Count >= nels)
            return input.ElementMaterialTypes.Take(nels).ToArray();

        var values = new List<int>(input.ElementMaterialTypes);
        while (values.Count < nels) values.Add(1);
        return values;
    }
}
