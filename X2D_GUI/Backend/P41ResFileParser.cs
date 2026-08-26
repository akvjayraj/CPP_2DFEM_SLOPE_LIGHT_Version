using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models.FiniteElement;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Parses p41 .res output (1D rod: node displacements and element actions) into C# models for X2D_GUI.
/// Node positions are derived from .dat element lengths (cumulative along bar; Y=0 for 1D display).
/// </summary>
public class P41ResFileParser
{
    /// <summary>
    /// Parsed result from a p41 .res file.
    /// </summary>
    public class P41ParseResult
    {
        public IReadOnlyList<Node> Nodes { get; init; } = new List<Node>();
        public IReadOnlyList<ElementBase> Elements { get; init; } = new List<ElementBase>();
        /// <summary>Element axial forces: ElementId (0-based), StressXX = axial force at node 1 end.</summary>
        public IReadOnlyList<ElementStress> ElementStresses { get; init; } = new List<ElementStress>();
        public int EquationCount { get; init; }
        public int SkylineStorage { get; init; }
    }

    /// <summary>
    /// Parse .res and .dat. Uses .dat to get nels and ell for node positions (cumulative along x; y=0).
    /// </summary>
    public P41ParseResult Parse(string resPath, string datPath)
    {
        var (nels, ell) = ParseDatForMesh(datPath);
        int nn = nels + 1;
        var positions = new List<(double x, double y)>();
        double cum = 0;
        positions.Add((0, 0));
        for (int i = 0; i < nels && i < ell.Count; i++)
        {
            cum += ell[i];
            positions.Add((cum, 0));
        }
        while (positions.Count < nn)
            positions.Add((cum, 0));

        var resLines = File.ReadAllLines(resPath).ToList();
        var displacements = ParseDisplacements(resLines, nn);
        var actions = ParseElementActions(resLines, nels);

        var nodes = new List<Node>();
        for (int i = 0; i < nn; i++)
        {
            double disp = i < displacements.Count ? displacements[i] : 0;
            var (x, y) = i < positions.Count ? positions[i] : (0.0, 0.0);
            nodes.Add(new Node
            {
                Id = i,
                X = x,
                Y = y,
                Displacements = new[] { disp, 0.0 }
            });
        }

        var elements = new List<ElementBase>();
        for (int i = 0; i < nels; i++)
        {
            elements.Add(new LinearRodElement
            {
                Id = i,
                NodeIds = new[] { i, i + 1 }
            });
        }

        var stresses = new List<ElementStress>();
        for (int i = 0; i < actions.Count; i++)
        {
            // action[0], action[1] are end forces; use first end as "axial force" for display
            double axial = i < actions.Count ? actions[i].a1 : 0;
            stresses.Add(new ElementStress
            {
                ElementId = i,
                StressXX = axial,
                StressYY = 0,
                StressXY = 0,
                PrincipalStressMax = axial,
                PrincipalStressMin = 0,
                VonMisesStress = Math.Abs(axial)
            });
        }

        return new P41ParseResult
        {
            Nodes = nodes,
            Elements = elements,
            ElementStresses = stresses,
            EquationCount = ParseEquationCount(resLines),
            SkylineStorage = ParseSkylineStorage(resLines)
        };
    }

    private static (int nels, List<double> ell) ParseDatForMesh(string datPath)
    {
        var lines = File.ReadAllLines(datPath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count < 2) return (0, new List<double>());

        var first = lines[0].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (first.Length < 1 || !int.TryParse(first[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nels))
            return (0, new List<double>());
        int npTypes = first.Length >= 2 && int.TryParse(first[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nt) ? nt : 1;

        int idx = 1;
        if (idx < lines.Count) idx++; // prop
        if (npTypes > 1 && idx < lines.Count) idx++; // etype
        if (idx >= lines.Count) return (nels, new List<double>());

        var ellStr = lines[idx].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var ell = new List<double>();
        foreach (var s in ellStr)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double L))
                ell.Add(L);
        }
        return (nels, ell);
    }

    private static List<double> ParseDisplacements(List<string> lines, int nn)
    {
        var result = new List<double>();
        int idx = lines.FindIndex(s => s.Contains("Node") && s.Contains("Disp"));
        if (idx < 0) return result;
        idx++;

        for (int k = 0; k < nn && idx < lines.Count; k++, idx++)
        {
            var parts = lines[idx].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double disp))
            {
                result.Add(disp);
            }
        }
        return result;
    }

    private static List<(double a1, double a2)> ParseElementActions(List<string> lines, int nels)
    {
        var result = new List<(double, double)>();
        int idx = lines.FindIndex(s => s.Contains("Element") && s.Contains("Actions"));
        if (idx < 0) return result;
        idx++;

        for (int iel = 0; iel < nels && idx < lines.Count; iel++, idx++)
        {
            var parts = lines[idx].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double a1) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double a2))
            {
                result.Add((a1, a2));
            }
        }
        return result;
    }

    private static int ParseEquationCount(List<string> lines)
    {
        var line = lines.FirstOrDefault() ?? "";
        var m = Regex.Match(line, @"There are\s*(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static int ParseSkylineStorage(List<string> lines)
    {
        var line = lines.FirstOrDefault() ?? "";
        var m = Regex.Match(line, @"skyline storage is\s*(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }
}
