using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.Backend;

/// <summary>Builds and merges ! &lt;BEGIN:Groundwater&gt; blocks and numeric solver tails.</summary>
public static class GroundwaterDatWriter
{
    public const string BeginTag = "! <BEGIN:Groundwater>";
    public const string EndTag = "! <END:Groundwater>";
    public const string SectionName = "Groundwater";

    public static string BuildBlock(GroundwaterModel model)
    {
        var staticSettings = model.StaticSettings;
        var includeWaterSurfaces = staticSettings?.IncludeWaterSurfaces ?? true;
        // SurfPoints in the model must export even if Project Settings Water Surfaces is OFF.
        if (model.IsEnabled && model.SurfPoints.Count > 0)
            includeWaterSurfaces = true;
        var includePonded = staticSettings?.IncludePondedFreeWater ?? true;
        var includeRu = staticSettings?.IncludeRuCoefficients ?? true;
        var includeGridTotalHead = staticSettings?.IncludeWaterPressureGridTotalHead ?? true;
        var includeGridPressureHead = staticSettings?.IncludeWaterPressureGridPressureHead ?? true;
        var includeGridPorePressure = staticSettings?.IncludeWaterPressureGridPorePressure ?? true;

        var sb = new StringBuilder();
        sb.AppendLine(BeginTag);
        sb.AppendLine($"! include {(model.IsEnabled ? "true" : "false")}");
        sb.AppendLine($"! static_water_surfaces {BoolStr(includeWaterSurfaces)}");
        sb.AppendLine($"! static_ponded_free_water {BoolStr(includePonded)}");
        sb.AppendLine($"! static_ru {BoolStr(includeRu)}");
        sb.AppendLine($"! static_grid_total_head {BoolStr(includeGridTotalHead)}");
        sb.AppendLine($"! static_grid_pressure_head {BoolStr(includeGridPressureHead)}");
        sb.AppendLine($"! static_grid_pore_pressure {BoolStr(includeGridPorePressure)}");
        if (model.IsEnabled && includeWaterSurfaces && includePonded
            && model.Method == GroundwaterMethod.Hydrostatic)
        {
            sb.AppendLine(model.PondedWaterLevelMethod == PondedWaterLevelMethod.TotalHead
                ? "! ponded_water_level total_head"
                : "! ponded_water_level phreatic");
            if (model.PondedWaterLevelMethod == PondedWaterLevelMethod.TotalHead)
                sb.AppendLine($"! ponded_total_head {Fmt(model.PondedTotalHead)}");
        }

        if (model.IsEnabled && model.Method == GroundwaterMethod.Ru && includeRu)
        {
            sb.AppendLine("! method ru");
            sb.AppendLine($"! Ru {Fmt(model.Ru)}");
        }
        else if (model.IsEnabled && model.Method == GroundwaterMethod.Bbar && includeRu)
        {
            sb.AppendLine("! method bbar");
            sb.AppendLine($"! Bbar {Fmt(model.Bbar)}");
            sb.AppendLine($"! static_bbar {BoolStr(includeRu)}");
        }
        else if (model.IsEnabled
                 && (model.Method == GroundwaterMethod.Grid
                     || HasExportableGrid(model, includeGridTotalHead, includeGridPressureHead, includeGridPorePressure)))
        {
            sb.AppendLine("! method grid");
        }
        else if (model.IsEnabled && includeWaterSurfaces)
        {
            sb.AppendLine("! method hydrostatic");
        }
        sb.AppendLine($"! gamma_w {Fmt(model.GammaW)}");
        if (model.IsEnabled && model.GammaSat.Count > 0)
            sb.AppendLine($"! gamma_sat {string.Join("  ", model.GammaSat.Select(Fmt))}");

        var pts = model.IsEnabled && model.Method == GroundwaterMethod.Hydrostatic && includeWaterSurfaces
            ? model.SurfPoints
            : new List<(double X, double Y)>();
        // Keep phreatic surf even when grid method is primary (γsat body-force switching).
        if (model.IsEnabled && includeWaterSurfaces && model.SurfPoints.Count > 0
            && model.Method != GroundwaterMethod.Ru
            && model.Method != GroundwaterMethod.Bbar)
            pts = model.SurfPoints;

        sb.AppendLine($"! nosurf {pts.Count}");
        if (pts.Count > 0)
        {
            sb.AppendLine("! surf");
            foreach (var (x, y) in pts)
                sb.AppendLine($"!   {Fmt(x)}  {Fmt(y)}");
        }

        AppendWaterPressureGrid(sb, model, includeGridTotalHead, includeGridPressureHead, includeGridPorePressure);
        sb.AppendLine(EndTag);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static bool HasExportableGrid(
        GroundwaterModel model,
        bool includeTotalHead,
        bool includePressureHead,
        bool includePorePressure)
    {
        if (TrySelectExportGrid(model, includeTotalHead, includePressureHead, includePorePressure, out _, out var points))
            return points.Count > 0;
        return false;
    }

    private static bool TrySelectExportGrid(
        GroundwaterModel model,
        bool includeTotalHead,
        bool includePressureHead,
        bool includePorePressure,
        out WaterPressureGridType type,
        out List<(double X, double Y, double Value)> points)
    {
        type = WaterPressureGridType.PorePressure;
        points = new List<(double X, double Y, double Value)>();

        // Prefer PorePressure, then PressureHead, then TotalHead (named grids or flat lists).
        WaterPressureGridType[] order =
        {
            WaterPressureGridType.PorePressure,
            WaterPressureGridType.PressureHead,
            WaterPressureGridType.TotalHead
        };

        foreach (var want in order)
        {
            var allowed = want switch
            {
                WaterPressureGridType.TotalHead => includeTotalHead,
                WaterPressureGridType.PressureHead => includePressureHead,
                WaterPressureGridType.PorePressure => includePorePressure,
                _ => false
            };
            if (!allowed) continue;

            if (model.WaterPressureGrids is { Count: > 0 })
            {
                foreach (var g in model.WaterPressureGrids)
                {
                    if (g.GridType != want || g.Points == null || g.Points.Count == 0) continue;
                    type = want;
                    points = g.Points.ToList();
                    return true;
                }
            }

            var flat = want switch
            {
                WaterPressureGridType.TotalHead => model.TotalHeadGridPoints,
                WaterPressureGridType.PressureHead => model.PressureHeadGridPoints,
                _ => model.PorePressureGridPoints
            };
            if (flat is { Count: > 0 })
            {
                type = want;
                points = flat.ToList();
                return true;
            }
        }
        return false;
    }

    private static void AppendWaterPressureGrid(
        StringBuilder sb,
        GroundwaterModel model,
        bool includeTotalHead,
        bool includePressureHead,
        bool includePorePressure)
    {
        if (!model.IsEnabled) return;
        if (!TrySelectExportGrid(model, includeTotalHead, includePressureHead, includePorePressure, out var type, out var points))
            return;
        if (points.Count == 0) return;

        var typeToken = type switch
        {
            WaterPressureGridType.TotalHead => "total_head",
            WaterPressureGridType.PressureHead => "pressure_head",
            _ => "pore_pressure"
        };
        sb.AppendLine($"! grid_type {typeToken}");
        sb.AppendLine($"! ngrid {points.Count}");
        sb.AppendLine("! grid");
        foreach (var (x, y, v) in points)
            sb.AppendLine($"!   {Fmt(x)}  {Fmt(y)}  {Fmt(v)}");
    }

    public static string BuildNumericTail(GroundwaterModel model)
    {
        if (!model.IsEnabled || model.SurfPoints.Count == 0)
            return $"0{Environment.NewLine}{Fmt(model.GammaW)}{Environment.NewLine}";

        var sb = new StringBuilder();
        sb.AppendLine(model.SurfPoints.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (x, y) in model.SurfPoints)
            sb.AppendLine($"{Fmt(x)}  {Fmt(y)}");
        sb.AppendLine(Fmt(model.GammaW));
        return sb.ToString();
    }

    public static string MergeSection(string datContent, string block)
    {
        if (string.IsNullOrEmpty(datContent))
            return block;

        var pattern = $@"! <BEGIN:{SectionName}>[\s\S]*?! <END:{SectionName}>\s*";
        if (Regex.IsMatch(datContent, pattern, RegexOptions.IgnoreCase))
            return Regex.Replace(datContent, pattern, block.TrimEnd() + Environment.NewLine, RegexOptions.IgnoreCase);

        int insertAt = datContent.IndexOf("! <BEGIN:", StringComparison.OrdinalIgnoreCase);
        if (insertAt < 0)
            return datContent.TrimEnd() + Environment.NewLine + Environment.NewLine + block;

        return datContent.Insert(insertAt, block + Environment.NewLine);
    }

    public static string AppendNumericTail(string datContent, GroundwaterModel model)
    {
        if (string.IsNullOrWhiteSpace(datContent) || !ShouldAppendNumericTail(datContent, model))
            return datContent;

        var stripped = StripExistingNumericTail(datContent);
        if (!model.IsEnabled)
            return stripped;

        return stripped.TrimEnd() + Environment.NewLine + BuildNumericTail(model).TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// p64 reads <c>! &lt;BEGIN:Groundwater&gt;</c> from the file path; appending a numeric tail after
    /// SRF controls corrupts <c>read_srf_controls</c> unless the deck already used that layout.
    /// </summary>
    public static bool ShouldAppendNumericTail(string datContent, GroundwaterModel model)
    {
        if (!model.IsEnabled || model.Method == GroundwaterMethod.Ru
            || model.Method == GroundwaterMethod.Bbar || model.SurfPoints.Count == 0)
            return false;
        if (HasGroundwaterCommentSurf(datContent))
            return false;
        return HasGroundwaterNumericTail(datContent);
    }

    public static bool HasGroundwaterCommentSurf(string datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent)) return false;
        bool inBlock = false, inSurf = false;
        foreach (var raw in datContent.Split('\n'))
        {
            var t = raw.Trim();
            if (t.Contains("<BEGIN:Groundwater>", StringComparison.OrdinalIgnoreCase)) { inBlock = true; continue; }
            if (t.Contains("<END:Groundwater>", StringComparison.OrdinalIgnoreCase)) break;
            if (!inBlock) continue;
            if (t.TrimStart('!').Trim().Equals("surf", StringComparison.OrdinalIgnoreCase))
            { inSurf = true; continue; }
            if (inSurf)
            {
                var line = t.TrimStart('!').Trim();
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Merge comment block only (never touch SRF tail).</summary>
    public static string MergeGroundwaterSection(string datContent, GroundwaterModel model)
        => MergeSection(datContent, BuildBlock(model));

    /// <summary>Remove trailing nosurf/surf/gam_w if present after SRF values.</summary>
    private static string StripExistingNumericTail(string content)
    {
        if (!TryParseGroundwaterNumericTailLineIndices(content, out int nosurfStart, out int nosurf, out _))
            return content;

        var lines = content.Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        if (nosurfStart < 0 || nosurfStart + nosurf + 1 >= lines.Count)
            return content;

        lines.RemoveRange(nosurfStart, nosurf + 2);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>
    /// Recognize GW numeric tail: nosurf, nosurf (x,y) rows, then single gam_w — not SRF (tol/limit, nsrf, F…F).
    /// </summary>
    internal static bool TryParseGroundwaterNumericTailLineIndices(
        string content, out int nosurfLineIndex, out int nosurf, out double gammaW)
    {
        nosurfLineIndex = -1;
        nosurf = 0;
        gammaW = 0;

        var lines = content.Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count < 3)
            return false;

        var gamTokens = Tokenize(lines[^1]).ToList();
        // SRF multi trial line has several F values; GW gam_w is a single number (~9.81).
        if (gamTokens.Count != 1)
            return false;
        if (!double.TryParse(gamTokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out gammaW))
            return false;
        if (gammaW < 5.0 || gammaW > 15.0)
            return false;

        int nsLine = lines.Count - 2;
        if (!int.TryParse(Tokenize(lines[nsLine]).FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out nosurf))
            return false;
        if (nosurf <= 0 || nsLine - nosurf < 0)
            return false;

        for (int j = nsLine - nosurf; j < nsLine; ++j)
        {
            var xy = Tokenize(lines[j]).ToList();
            if (xy.Count < 2 ||
                !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _) ||
                !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return false;
        }

        nosurfLineIndex = nsLine - nosurf;
        return true;
    }

    internal static bool HasGroundwaterNumericTail(string content)
        => TryParseGroundwaterNumericTailLineIndices(content, out _, out _, out _);

    private static IEnumerable<string> Tokenize(string line)
    {
        var bang = line.IndexOf('!');
        if (bang >= 0) line = line[..bang];
        return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string Fmt(double v) => v.ToString("G10", CultureInfo.InvariantCulture);
    private static string BoolStr(bool value) => value ? "true" : "false";

    public static GroundwaterModel ToModel(bool enabled, double gammaW, IReadOnlyList<GroundwaterSurfPoint> points,
        GroundwaterDrawMode drawMode, GroundwaterMethod method = GroundwaterMethod.Hydrostatic, double ru = 0.0)
    {
        return new GroundwaterModel
        {
            IsEnabled = enabled,
            GammaW = gammaW,
            Method = method,
            Ru = ru,
            DrawMode = drawMode,
            SurfPoints = points.Select(p => (p.AxisX, p.AxisY)).ToList()
        };
    }
}
