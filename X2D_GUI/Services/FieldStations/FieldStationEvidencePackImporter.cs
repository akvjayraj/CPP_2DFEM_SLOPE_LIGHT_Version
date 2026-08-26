using System.Globalization;
using System.IO;
using System.Text;
using GeotechSlopeApp.Models.FieldStations;

namespace GeotechSlopeApp.Services.FieldStations;

/// <summary>Column indices (0-based) for an Evidence Pack CSV. Unmapped optional columns are null.</summary>
public sealed class FieldStationColumnMap
{
    public int StationId { get; set; }
    public int X { get; set; }
    public int UnitCode { get; set; }
    public int? TopY { get; set; }
    public int? BottomY { get; set; }
    public int? Depth { get; set; }
    public int? DepthTop { get; set; }
    public int? CollarY { get; set; }
    public int? Note { get; set; }
    public int? Label { get; set; }

    public bool UsesDepth => Depth != null || DepthTop != null;
}

public sealed class FieldStationCsvPreview
{
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IReadOnlyList<string>> PreviewRows { get; init; } = Array.Empty<IReadOnlyList<string>>();
    public int DataRowCount { get; init; }
}

public sealed class FieldStationImportResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<FieldStation> Stations { get; init; } = Array.Empty<FieldStation>();
}

/// <summary>
/// Parses the proprietary Field Stations Evidence Pack CSV (not gINT / AGS / GEO5 / Slide).
/// </summary>
public static class FieldStationEvidencePackImporter
{
    public static FieldStationCsvPreview ReadPreview(string path, int previewRows = 3)
    {
        var lines = ReadDataLines(path);
        if (lines.Count == 0)
            return new FieldStationCsvPreview();

        var headers = SplitCsv(lines[0]);
        var rows = new List<IReadOnlyList<string>>();
        for (var i = 1; i < lines.Count && rows.Count < previewRows; i++)
            rows.Add(SplitCsv(lines[i]));

        return new FieldStationCsvPreview
        {
            Headers = headers,
            PreviewRows = rows,
            DataRowCount = Math.Max(0, lines.Count - 1)
        };
    }

    public static FieldStationColumnMap TryAutoMap(IReadOnlyList<string> headers)
    {
        var map = new FieldStationColumnMap();
        map.StationId = FindHeader(headers, "stationid", "station", "id", "bh", "borehole") ?? 0;
        map.X = FindHeader(headers, "x", "stationx", "modelx") ?? Math.Min(1, Math.Max(0, headers.Count - 1));
        map.UnitCode = FindHeader(headers, "unitcode", "unit", "soil", "lithology", "code") ?? Math.Min(4, Math.Max(0, headers.Count - 1));
        map.TopY = FindHeader(headers, "topy", "top", "elevtop", "top_elev", "topelev");
        map.BottomY = FindHeader(headers, "bottomy", "bottom", "elevbottom", "bottom_elev", "bottomelev");
        map.Depth = FindHeader(headers, "depth", "depthbottom", "z", "bottomdepth");
        map.DepthTop = FindHeader(headers, "depthtop", "topdepth");
        map.CollarY = FindHeader(headers, "collary", "collar", "groundy", "ground", "collar_elev");
        map.Note = FindHeader(headers, "note", "notes", "comment");
        map.Label = FindHeader(headers, "label", "name", "stationlabel");
        return map;
    }

    public static FieldStationImportResult Import(string path, FieldStationColumnMap map)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Fail("Evidence Pack file was not found.");

        var lines = ReadDataLines(path);
        if (lines.Count < 2)
            return Fail("Evidence Pack CSV needs a header row and at least one data row.");

        var headers = SplitCsv(lines[0]);
        var requiredMax = Math.Max(map.StationId, Math.Max(map.X, map.UnitCode));
        if (headers.Count <= requiredMax)
            return Fail("Column map points past the header row.");

        if (map.UsesDepth && map.CollarY == null)
            return Fail("Depth-based import needs a Collar Y column.");

        if (!map.UsesDepth && (map.TopY == null || map.BottomY == null))
            return Fail("Elevation import needs Top Y and Bottom Y columns (or map Depth + Collar Y).");

        var grouped = new Dictionary<string, FieldStation>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        for (var i = 1; i < lines.Count; i++)
        {
            var cols = SplitCsv(lines[i]);
            if (!TryGet(cols, map.StationId, out var stationId) || string.IsNullOrWhiteSpace(stationId))
                return Fail($"Row {i + 1}: missing Station Id.");
            if (!TryGetDouble(cols, map.X, out var x))
                return Fail($"Row {i + 1}: missing or invalid X.");
            if (!TryGet(cols, map.UnitCode, out var unit) || string.IsNullOrWhiteSpace(unit))
                return Fail($"Row {i + 1}: missing Unit code.");

            if (!grouped.TryGetValue(stationId, out var station))
            {
                station = new FieldStation
                {
                    Id = stationId.Trim(),
                    X = x,
                    Label = TryGet(cols, map.Label ?? -1, out var label) ? label : ""
                };
                grouped[stationId] = station;
                order.Add(stationId);
            }
            else if (Math.Abs(station.X - x) > 1e-6)
            {
                return Fail($"Station {stationId}: X is not unique across rows.");
            }

            double topY;
            double bottomY;
            if (map.UsesDepth)
            {
                if (!TryGetDouble(cols, map.CollarY!.Value, out var collar))
                    return Fail($"Row {i + 1}: missing or invalid Collar Y.");
                station.CollarY = collar;

                double depthBottom;
                if (!TryGetDouble(cols, map.Depth ?? -1, out depthBottom))
                    return Fail($"Row {i + 1}: missing or invalid Depth.");

                double depthTop = 0;
                if (map.DepthTop != null && TryGetDouble(cols, map.DepthTop.Value, out var dt))
                    depthTop = dt;
                else if (station.Intervals.Count > 0)
                    depthTop = collar - station.Intervals[^1].BottomY;

                topY = collar - depthTop;
                bottomY = collar - depthBottom;
            }
            else
            {
                if (!TryGetDouble(cols, map.TopY!.Value, out topY) || !TryGetDouble(cols, map.BottomY!.Value, out bottomY))
                    return Fail($"Row {i + 1}: missing or invalid Top Y / Bottom Y.");
                if (map.CollarY != null && TryGetDouble(cols, map.CollarY.Value, out var collar))
                    station.CollarY = collar;
            }

            if (topY < bottomY)
                (topY, bottomY) = (bottomY, topY);

            TryGet(cols, map.Note ?? -1, out var note);
            station.Intervals.Add(new FieldUnitInterval
            {
                TopY = topY,
                BottomY = bottomY,
                UnitCode = unit.Trim(),
                Note = note
            });
        }

        var stations = new List<FieldStation>(order.Count);
        foreach (var id in order)
        {
            var station = grouped[id];
            if (station.CollarY == 0 && station.Intervals.Count > 0)
                station.CollarY = station.Intervals.Max(iv => iv.TopY);
            if (!FieldStationCommitService.TryNormalize(station, out var error))
                return Fail(error);
            stations.Add(station);
        }

        return new FieldStationImportResult
        {
            Success = true,
            Message = $"{stations.Count} station(s) imported.",
            Stations = stations
        };
    }

    private static FieldStationImportResult Fail(string message) =>
        new() { Success = false, Message = message };

    private static List<string> ReadDataLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var result = new List<string>();
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            result.Add(line);
        }
        return result;
    }

    internal static IReadOnlyList<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        fields.Add(sb.ToString().Trim());
        return fields;
    }

    private static int? FindHeader(IReadOnlyList<string> headers, params string[] aliases)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var key = NormalizeHeader(headers[i]);
            foreach (var alias in aliases)
            {
                if (key == alias)
                    return i;
            }
        }
        return null;
    }

    private static string NormalizeHeader(string header) =>
        new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static bool TryGet(IReadOnlyList<string> cols, int index, out string value)
    {
        value = "";
        if (index < 0 || index >= cols.Count) return false;
        value = cols[index].Trim();
        return true;
    }

    private static bool TryGetDouble(IReadOnlyList<string> cols, int index, out double value)
    {
        value = 0;
        if (index < 0 || index >= cols.Count) return false;
        return double.TryParse(cols[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
