using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Parses p51 .res output into C# models for the X2D_GUI.
/// </summary>
public class P51ResFileParser
{
    /// <summary>
    /// Parsed result from a p51 .res file.
    /// </summary>
    public class P51ParseResult
    {
        public IReadOnlyList<Node> Nodes { get; init; } = new List<Node>();
        public IReadOnlyList<LinearTriangleElement> Elements { get; init; } = new List<LinearTriangleElement>();
        public IReadOnlyList<ElementStress> ElementStresses { get; init; } = new List<ElementStress>();
        public int EquationCount { get; init; }
        public int SkylineStorage { get; init; }
    }

    /// <summary>
    /// Parse a .res file. Requires corresponding .dat to get mesh topology (nodes, elements).
    /// The .dat defines the mesh; .res has displacements and stresses.
    /// </summary>
    /// <param name="resPath">Path to .res file</param>
    /// <param name="datPath">Path to .dat file (for mesh topology)</param>
    /// <param name="elementType">"triangle" or "quadrilateral"</param>
    /// <param name="nod">3 for 3-node triangle</param>
    /// <param name="nxe">Elements in x</param>
    /// <param name="nye">Elements in y</param>
    /// <param name="dir">'x' or 'y'</param>
    public P51ParseResult Parse(string resPath, string datPath,
        string elementType = "triangle", int nod = 3, int nxe = 2, int nye = 2, char dir = 'x')
    {
        var datInput = ParseDatForMesh(datPath);
        var (nodes, elements) = BuildMeshFromDat(datInput, elementType, nod);

        var resLines = File.ReadAllLines(resPath).ToList();
        var displacements = ParseDisplacements(resLines, nodes.Count);
        var stresses = ParseStresses(resLines, elements.Count);

        // Apply displacements to nodes (nodes are 0-indexed in our model)
        for (int i = 0; i < Math.Min(nodes.Count, displacements.Count); i++)
        {
            nodes[i] = nodes[i] with
            {
                Displacements = new[] { displacements[i].Ux, displacements[i].Uy }
            };
        }

        return new P51ParseResult
        {
            Nodes = nodes,
            Elements = elements,
            ElementStresses = stresses,
            EquationCount = ParseEquationCount(resLines),
            SkylineStorage = ParseSkylineStorage(resLines)
        };
    }

    /// <summary>
    /// Parse .res when mesh is already known (e.g. from MeshVM).
    /// </summary>
    public P51ParseResult ParseWithMesh(string resPath,
        IReadOnlyList<Node> nodes,
        IReadOnlyList<LinearTriangleElement> elements)
    {
        var resLines = File.ReadAllLines(resPath).ToList();
        var displacements = ParseDisplacements(resLines, nodes.Count);
        var stresses = ParseStresses(resLines, elements.Count);

        var updatedNodes = nodes.ToList();
        for (int i = 0; i < Math.Min(updatedNodes.Count, displacements.Count); i++)
        {
            updatedNodes[i] = updatedNodes[i] with
            {
                Displacements = new[] { displacements[i].Ux, displacements[i].Uy }
            };
        }

        return new P51ParseResult
        {
            Nodes = updatedNodes,
            Elements = elements,
            ElementStresses = stresses,
            EquationCount = ParseEquationCount(resLines),
            SkylineStorage = ParseSkylineStorage(resLines)
        };
    }

    private static (int nxe, int nye, char dir, List<double> XCoords, List<double> YCoords) ParseDatForMesh(string datPath)
    {
        var lines = File.ReadAllLines(datPath).ToList();
        int idx = 0;
        while (idx < lines.Count && string.IsNullOrWhiteSpace(lines[idx])) idx++;

        if (idx < lines.Count && lines[idx].Trim() == "type_2d")
        {
            idx++; // type_2d
            if (idx < lines.Count) idx++; // 'Plane' / 'Axisymmetric'
        }
        else
            idx++; // legacy: 'plane' / 'axisymmetric' line

        var line2 = lines[idx++];  // 'element' nod 'dir'
        char dir = line2.Contains("'y'") ? 'y' : 'x';

        var line3 = lines[idx++].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int nxe = int.Parse(line3[0]);
        int nye = int.Parse(line3[1]);
        int npTypes = int.Parse(line3[3]);

        var propLine = lines[idx++].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        for (int j = 1; j < npTypes; j++) idx++;

        if (npTypes > 1)
        {
            int nels = nxe * nye * 2;
            idx += (nels + 19) / 20;
        }

        var xCoords = lines[idx++].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToList();
        var yCoords = lines[idx++].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToList();

        return (nxe, nye, dir, xCoords, yCoords);
    }

    private static (List<Node> nodes, List<LinearTriangleElement> elements) BuildMeshFromDat(
        (int nxe, int nye, char dir, List<double> XCoords, List<double> YCoords) datInput,
        string elementType, int nod)
    {
        var (nxe, nye, dir, xCoords, yCoords) = datInput;
        int nn = (nxe + 1) * (nye + 1);
        int nels = nxe * nye * 2;

        var nodes = new List<Node>();
        for (int i = 0; i < nn; i++)
        {
            int iy = i / (nxe + 1);
            int ix = i % (nxe + 1);
            double x = ix < xCoords.Count ? xCoords[ix] : 0;
            double y = iy < yCoords.Count ? yCoords[iy] : 0;
            nodes.Add(new Node { Id = i, X = x, Y = y, Displacements = new[] { 0.0, 0.0 } });
        }

        var elements = new List<LinearTriangleElement>();
        for (int iel = 1; iel <= nels; iel++)
        {
            var (n1, n2, n3) = GeomRectTriangleMeshBuilder.GetTriangleNodeIds(iel, nxe, nye, dir);
            elements.Add(new LinearTriangleElement
            {
                Id = iel - 1,
                NodeIds = new[] { n1 - 1, n2 - 1, n3 - 1 }  // 0-based
            });
        }

        return (nodes, elements);
    }

    private static List<(double Ux, double Uy)> ParseDisplacements(List<string> lines, int nn)
    {
        var result = new List<(double, double)>();
        int idx = lines.FindIndex(s => s.Contains("x-disp") || s.Contains("r-disp"));
        if (idx < 0) return result;
        idx++;

        for (int k = 0; k < nn && idx < lines.Count; k++, idx++)
        {
            var parts = lines[idx].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 &&
                int.TryParse(parts[0], out int nodeNum) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ux) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double uy))
            {
                result.Add((ux, uy));
            }
        }
        return result;
    }

    private static List<ElementStress> ParseStresses(List<string> lines, int nels)
    {
        var result = new List<ElementStress>();
        int idx = lines.FindIndex(s => s.Contains("Element") && (s.Contains("x-coord") || s.Contains("sig_x")));
        if (idx < 0) return result;
        idx++;

        for (int iel = 0; iel < nels && idx < lines.Count; iel++, idx++)
        {
            var parts = lines[idx].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 6 &&
                int.TryParse(parts[0], out int elNum) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double xc) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double yc) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double sigX) &&
                double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double sigY) &&
                double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double tauXY))
            {
                double principalMax = (sigX + sigY) / 2 + Math.Sqrt(Math.Pow((sigX - sigY) / 2, 2) + tauXY * tauXY);
                double principalMin = (sigX + sigY) / 2 - Math.Sqrt(Math.Pow((sigX - sigY) / 2, 2) + tauXY * tauXY);
                double vonMises = Math.Sqrt(principalMax * principalMax - principalMax * principalMin + principalMin * principalMin);
                result.Add(new ElementStress
                {
                    ElementId = elNum - 1,
                    StressXX = sigX,
                    StressYY = sigY,
                    StressXY = tauXY,
                    PrincipalStressMax = principalMax,
                    PrincipalStressMin = principalMin,
                    VonMisesStress = vonMises
                });
            }
        }
        return result;
    }

    private static int ParseEquationCount(List<string> lines)
    {
        var m = Regex.Match(lines.FirstOrDefault() ?? "", @"There are\s*(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    private static int ParseSkylineStorage(List<string> lines)
    {
        var m = Regex.Match(lines.FirstOrDefault() ?? "", @"skyline storage is\s*(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }
}
