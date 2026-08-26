using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Backend;

public static class GroundwaterDatImporter
{
    public static bool TryImportFromDat(string datContent, out GroundwaterModel model)
    {
        model = new GroundwaterModel();
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        bool found = TryParseSentinelBlock(datContent, model);
        if (!found)
            TryParseNumericTail(datContent, model);
        return found || model.SurfPoints.Count > 0 || model.IsEnabled
               || (model.IsEnabled && model.Method == GroundwaterMethod.Ru && model.Ru > 0)
               || (model.IsEnabled && model.Method == GroundwaterMethod.Bbar && model.Bbar > 0);
    }

    private static bool TryParseSentinelBlock(string content, GroundwaterModel model)
    {
        const string begin = "<BEGIN:Groundwater>";
        const string end = "<END:Groundwater>";
        bool inBlock = false;
        var lines = new List<string>();

        foreach (var raw in content.Split('\n'))
        {
            var t = raw.Trim();
            if (t.Contains(begin, StringComparison.OrdinalIgnoreCase)) { inBlock = true; continue; }
            if (t.Contains(end, StringComparison.OrdinalIgnoreCase)) break;
            if (!inBlock) continue;
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line[(bang + 1)..];
            lines.Add(line.Trim());
        }

        if (lines.Count == 0) return false;

        foreach (var line in lines)
        {
            if (TryParseOption(line, "include", out var inc))
                model.IsEnabled = inc.Equals("true", StringComparison.OrdinalIgnoreCase) || inc == "1";
            if (TryParseOption(line, "method", out var method))
            {
                model.Method = method.Equals("ru", StringComparison.OrdinalIgnoreCase)
                    ? GroundwaterMethod.Ru
                    : method.Equals("bbar", StringComparison.OrdinalIgnoreCase)
                      || method.Equals("b-bar", StringComparison.OrdinalIgnoreCase)
                        ? GroundwaterMethod.Bbar
                        : GroundwaterMethod.Hydrostatic;
            }
            if (TryParseOption(line, "Ru", out var ruStr) || TryParseOption(line, "ru", out ruStr))
            {
                if (double.TryParse(ruStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var ruVal))
                    model.Ru = ruVal;
            }
            if (TryParseOption(line, "Bbar", out var bbarStr) || TryParseOption(line, "bbar", out bbarStr))
            {
                if (double.TryParse(bbarStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var bbarVal))
                    model.Bbar = bbarVal;
            }
            if (TryParseOption(line, "gamma_w", out var gw) &&
                double.TryParse(gw, NumberStyles.Float, CultureInfo.InvariantCulture, out var g))
                model.GammaW = g;
        }

        int surfIdx = lines.FindIndex(l => l.Equals("surf", StringComparison.OrdinalIgnoreCase));
        int nosurf = 0;
        foreach (var line in lines)
        {
            if (TryParseOption(line, "nosurf", out var ns) &&
                int.TryParse(ns, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                nosurf = n;
        }

        model.SurfPoints.Clear();
        if (surfIdx >= 0)
        {
            for (int i = surfIdx + 1; i < lines.Count && model.SurfPoints.Count < nosurf; i++)
            {
                var parts = lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    model.SurfPoints.Add((x, y));
            }
        }

        return true;
    }

    private static void TryParseNumericTail(string content, GroundwaterModel model)
    {
        var numeric = Regex.Replace(content, @"!.*", "");
        var tokens = numeric.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count < 3) return;

        if (!int.TryParse(tokens[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nosurf) || nosurf < 0)
            return;
        if (!double.TryParse(tokens[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double gamW))
            return;

        int need = nosurf * 2 + 2;
        if (tokens.Count < need) return;

        int start = tokens.Count - need;
        model.GammaW = gamW;
        model.IsEnabled = nosurf > 0;
        model.SurfPoints.Clear();
        int p = start + 1;
        for (int i = 0; i < nosurf; i++)
        {
            if (!double.TryParse(tokens[p++], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(tokens[p++], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return;
            model.SurfPoints.Add((x, y));
        }
    }

    private static bool TryParseOption(string line, string key, out string value)
    {
        value = "";
        var m = Regex.Match(line, $@"^{Regex.Escape(key)}\s+(\S+)\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        value = m.Groups[1].Value;
        return true;
    }

    /// <summary>
    /// Phreatic <c>surf</c> Y in p64 .dat is solver-native (Smith y-down); mesh/geometry canvases use
    /// Y-up with <c>mesh_coord_y_offset</c> added to node Y. Convert solver → display in-place.
    /// </summary>
    public static void ApplyMeshCoordYOffsetForDisplay(string? datContent, GroundwaterModel model)
    {
        if (model.SurfPoints.Count == 0 || string.IsNullOrWhiteSpace(datContent))
            return;
        if (!DatMeshBoundaryImporter.TryGetMeshCoordYOffset(datContent, out var yOff) || yOff <= 0)
            return;
        for (int i = 0; i < model.SurfPoints.Count; i++)
        {
            var p = model.SurfPoints[i];
            model.SurfPoints[i] = (p.X, p.Y + yOff);
        }
    }

    /// <summary>Convert GUI/canvas surf points (display Y) back to solver-native coords for .dat export.</summary>
    public static GroundwaterModel CloneForSolverExport(GroundwaterModel displayModel, string? datContent)
    {
        GroundwaterStaticAnalysisSettings? staticCopy = null;
        if (displayModel.StaticSettings != null)
        {
            staticCopy = new GroundwaterStaticAnalysisSettings();
            staticCopy.CopyFrom(displayModel.StaticSettings);
        }

        var clone = new GroundwaterModel
        {
            IsEnabled = displayModel.IsEnabled,
            GammaW = displayModel.GammaW,
            Method = displayModel.Method,
            Ru = displayModel.Ru,
            DrawMode = displayModel.DrawMode,
            SurfPoints = displayModel.SurfPoints.ToList(),
            WaterPressureGrids = displayModel.WaterPressureGrids
                .Select(g => new WaterPressureGridData
                {
                    Name = g.Name,
                    GridType = g.GridType,
                    Points = g.Points.ToList()
                }).ToList(),
            TotalHeadGridPoints = displayModel.TotalHeadGridPoints.ToList(),
            PressureHeadGridPoints = displayModel.PressureHeadGridPoints.ToList(),
            PorePressureGridPoints = displayModel.PorePressureGridPoints.ToList(),
            GammaSat = displayModel.GammaSat.ToList(),
            StaticSettings = staticCopy,
        };

        // SRF / structural solvers need Spec.active(); enable export flags for available data.
        SrfGroundwaterExport.EnsureActiveExportSettings(clone);

        if (string.IsNullOrWhiteSpace(datContent))
            return clone;
        if (!DatMeshBoundaryImporter.TryGetMeshCoordYOffset(datContent, out var yOff) || yOff <= 0)
            return clone;

        for (int i = 0; i < clone.SurfPoints.Count; i++)
        {
            var p = clone.SurfPoints[i];
            clone.SurfPoints[i] = (p.X, p.Y - yOff);
        }

        static List<(double X, double Y, double Value)> ShiftY(
            List<(double X, double Y, double Value)> pts, double dy)
            => pts.Select(p => (p.X, p.Y - dy, p.Value)).ToList();

        clone.TotalHeadGridPoints = ShiftY(clone.TotalHeadGridPoints, yOff);
        clone.PressureHeadGridPoints = ShiftY(clone.PressureHeadGridPoints, yOff);
        clone.PorePressureGridPoints = ShiftY(clone.PorePressureGridPoints, yOff);
        foreach (var g in clone.WaterPressureGrids)
            g.Points = ShiftY(g.Points, yOff);

        return clone;
    }

    /// <summary>Map solver-native surf polyline to mesh-canvas axis coordinates.</summary>
    public static IReadOnlyList<(double X, double Y)> ToDisplaySurfPoints(
        IReadOnlyList<(double X, double Y)> solverPoints,
        string? datContent)
    {
        if (solverPoints.Count == 0)
            return solverPoints;
        if (string.IsNullOrWhiteSpace(datContent)
            || !DatMeshBoundaryImporter.TryGetMeshCoordYOffset(datContent, out var yOff)
            || yOff <= 0)
            return solverPoints;
        return solverPoints.Select(p => (p.X, p.Y + yOff)).ToList();
    }
}
