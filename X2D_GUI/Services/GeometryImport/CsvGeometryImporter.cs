using System.Globalization;
using System.IO;
using System.Text;

namespace GeotechSlopeApp.Services.GeometryImport;

public static class CsvGeometryImporter
{
    private enum Section
    {
        None,
        Points,
        Lines,
        Surfaces
    }

    public static GeometryImportDocument Import(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var doc = new GeometryImportDocument();
        var hasStructuredSections = lines.Any(l =>
            l.Trim().Equals("[POINTS]", StringComparison.OrdinalIgnoreCase)
            || l.Trim().Equals("[LINES]", StringComparison.OrdinalIgnoreCase)
            || l.Trim().Equals("[SURFACES]", StringComparison.OrdinalIgnoreCase));

        if (hasStructuredSections)
            ParseStructured(lines, doc);
        else
            ParseLegacy(lines, doc);

        return doc;
    }

    private static void ParseStructured(string[] lines, GeometryImportDocument doc)
    {
        var section = Section.None;
        var isHeaderRow = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.Equals("[POINTS]", StringComparison.OrdinalIgnoreCase))
            {
                section = Section.Points;
                isHeaderRow = true;
                continue;
            }

            if (line.Equals("[LINES]", StringComparison.OrdinalIgnoreCase))
            {
                section = Section.Lines;
                isHeaderRow = true;
                continue;
            }

            if (line.Equals("[SURFACES]", StringComparison.OrdinalIgnoreCase))
            {
                section = Section.Surfaces;
                isHeaderRow = true;
                continue;
            }

            if (isHeaderRow)
            {
                isHeaderRow = false;
                if (IsHeaderLine(line))
                    continue;
            }

            switch (section)
            {
                case Section.Points:
                    if (TryParsePointRow(line, out var pid, out var px, out var py))
                        doc.Points.Add(new GeometryImportPoint { Id = pid, X = px, Y = py });
                    break;
                case Section.Lines:
                    if (TryParseLineRow(line, out var fromId, out var toId, out var kind))
                        doc.Lines.Add(new GeometryImportLine { FromId = fromId, ToId = toId, Kind = kind });
                    break;
                case Section.Surfaces:
                    if (TryParseSurfaceRow(line, out var number, out var name, out var pointIds))
                        doc.Surfaces.Add(new GeometryImportSurface { Number = number, Name = name, PointIds = pointIds });
                    break;
            }
        }
    }

    private static void ParseLegacy(string[] lines, GeometryImportDocument doc)
    {
        foreach (var raw in lines)
        {
            if (TryParseLegacyCoordinateLine(raw, out var x, out var y))
                doc.LegacyBoundaryPoints.Add((x, y));
        }
    }

    private static bool IsHeaderLine(string line)
    {
        var lower = line.ToLowerInvariant();
        return lower.Contains("id") && lower.Contains('x') && lower.Contains('y')
               || lower.StartsWith("fromid")
               || lower.StartsWith("number");
    }

    private static bool TryParsePointRow(string line, out int? id, out double x, out double y)
    {
        id = null;
        x = y = 0;
        var parts = SplitCsv(line);
        if (parts.Length < 2)
            return false;

        if (parts.Length >= 3 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
        {
            id = parsedId;
            return TryParseDouble(parts[1], out x) && TryParseDouble(parts[2], out y);
        }

        if (TryParseDouble(parts[^2], out x) && TryParseDouble(parts[^1], out y))
            return true;

        return false;
    }

    private static bool TryParseLineRow(string line, out int fromId, out int toId, out string kind)
    {
        fromId = toId = 0;
        kind = "internal";
        var parts = SplitCsv(line);
        if (parts.Length < 2)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out fromId))
            return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out toId))
            return false;

        if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
            kind = parts[2].Trim();
        return true;
    }

    private static bool TryParseSurfaceRow(string line, out int number, out string name, out List<int> pointIds)
    {
        number = 0;
        name = "";
        pointIds = new List<int>();
        // Surface rows use ';' to separate the PointIds inside the third column, so the row itself
        // must only be split on ',' / tab — otherwise the id list collapses into separate columns.
        var parts = SplitSurfaceColumns(line);
        if (parts.Length < 2)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return false;

        name = parts[1].Trim();
        if (parts.Length >= 3)
        {
            foreach (var token in parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                    pointIds.Add(pid);
            }
        }

        return pointIds.Count >= 3;
    }

    private static bool TryParseLegacyCoordinateLine(string line, out double x, out double y)
    {
        x = y = 0;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var parts = SplitCsv(line.Trim());
        if (parts.Length < 2)
            return false;

        string? xCandidate = null;
        string? yCandidate = null;
        for (var i = parts.Length - 1; i >= 0 && (xCandidate is null || yCandidate is null); i--)
        {
            if (!TryParseDouble(parts[i], out _))
                continue;

            if (yCandidate is null)
                yCandidate = parts[i];
            else if (xCandidate is null)
                xCandidate = parts[i];
        }

        return xCandidate is not null
               && yCandidate is not null
               && TryParseDouble(xCandidate, out x)
               && TryParseDouble(yCandidate, out y);
    }

    private static string[] SplitCsv(string line) =>
        line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.TrimEntries);

    /// <summary>Splits a surface row on ',' / tab only, preserving the ';'-joined PointIds column.</summary>
    private static string[] SplitSurfaceColumns(string line) =>
        line.Split(new[] { ',', '\t' }, StringSplitOptions.TrimEntries);

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    public static void Export(string filePath, GeometryImportDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# GeotechSlope Geometry CSV v1");
        sb.AppendLine("[POINTS]");
        sb.AppendLine("Id,X,Y");
        foreach (var p in doc.Points.OrderBy(p => p.Id ?? 0))
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:0.######},{2:0.######}",
                p.Id ?? 0, p.X, p.Y));
        }

        if (doc.Lines.Count > 0)
        {
            sb.AppendLine("[LINES]");
            sb.AppendLine("FromId,ToId,Kind");
            foreach (var l in doc.Lines)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}",
                    l.FromId, l.ToId, l.Kind));
            }
        }

        if (doc.Surfaces.Count > 0)
        {
            sb.AppendLine("[SURFACES]");
            sb.AppendLine("Number,Name,PointIds");
            foreach (var s in doc.Surfaces.OrderBy(s => s.Number))
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}",
                    s.Number, s.Name, string.Join(";", s.PointIds)));
            }
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }
}
