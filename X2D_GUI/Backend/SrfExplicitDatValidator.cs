using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Validates explicit p64-layout SRF decks exported for Core B (Q4/Q8) runs.
/// </summary>
public static class SrfExplicitDatValidator
{
    public sealed class ValidationResult
    {
        public bool IsValid { get; init; }
        public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
    }

    public static ValidationResult Validate(string datContent)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(datContent))
        {
            issues.Add("Deck is empty.");
            return new ValidationResult { IsValid = false, Issues = issues };
        }

        if (datContent.IndexOf("! <solver:p64>", StringComparison.OrdinalIgnoreCase) < 0)
            issues.Add("Missing ! <solver:p64> header (not an explicit p64-layout deck).");

        var meshType = ReadAnalysisOption(datContent, "mesh_type");
        if (string.IsNullOrWhiteSpace(meshType))
            issues.Add("AnalysisOptions missing mesh_type.");
        else
            meshType = P610AnalysisOptionsHelper.NormalizeMeshTypeToken(meshType);

        var nodesPerElement = meshType switch
        {
            "Q4" => 4,
            "Q8" => 8,
            "T6" => 6,
            "T3" => 3,
            _ => 0,
        };
        if (nodesPerElement == 0)
            issues.Add($"Unsupported or missing mesh_type '{meshType}'.");

        var dataLines = ExtractDataLines(datContent);
        if (dataLines.Count == 0)
        {
            issues.Add("No data lines found after comment stripping.");
            return new ValidationResult { IsValid = false, Issues = issues };
        }

        if (!TryParseCounts(dataLines[0], out int nels, out int nn, out int npTypes, out var countError))
        {
            issues.Add(countError ?? "Could not parse nels/nn/np_types header.");
            return new ValidationResult { IsValid = false, Issues = issues };
        }

        if (nels < 1 || nn < 1 || npTypes < 1)
            issues.Add($"Invalid mesh size header: nels={nels}, nn={nn}, np_types={npTypes}.");

        int lineIndex = 1;
        lineIndex += npTypes; // material rows

        if (npTypes > 1)
        {
            if (lineIndex >= dataLines.Count)
                issues.Add("Missing etype row for multi-material deck.");
            else
            {
                var etypeTokens = SplitTokens(dataLines[lineIndex]);
                if (etypeTokens.Count != nels)
                    issues.Add($"etype row has {etypeTokens.Count} value(s); expected {nels}.");
                lineIndex++;
            }
        }

        int coordLines = 0;
        for (int i = 0; i < nn && lineIndex < dataLines.Count; i++, lineIndex++)
        {
            var xy = SplitTokens(dataLines[lineIndex]);
            if (xy.Count < 2)
                issues.Add($"Node coordinate line {i + 1} is incomplete.");
            coordLines++;
        }
        if (coordLines != nn)
            issues.Add($"Expected {nn} node coordinate lines; found {coordLines}.");

        int badElements = 0;
        for (int iel = 0; iel < nels && lineIndex < dataLines.Count; iel++, lineIndex++)
        {
            var conn = SplitTokens(dataLines[lineIndex]);
            if (conn.Count != nodesPerElement)
            {
                badElements++;
                if (badElements <= 3)
                    issues.Add($"Element {iel + 1} has {conn.Count} node(s); mesh_type {meshType} requires {nodesPerElement}.");
            }
            else
            {
                foreach (var tok in conn)
                {
                    if (!int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nid)
                        || nid < 1 || nid > nn)
                    {
                        badElements++;
                        if (badElements <= 3)
                            issues.Add($"Element {iel + 1} references invalid node id '{tok}' (nn={nn}).");
                        break;
                    }
                }
            }
        }
        if (badElements > 3)
            issues.Add($"{badElements} element connectivity row(s) failed validation.");

        if (lineIndex >= dataLines.Count)
            issues.Add("Missing boundary-condition count line.");
        else if (int.TryParse(dataLines[lineIndex].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nr))
        {
            lineIndex++;
            for (int i = 0; i < nr && lineIndex < dataLines.Count; i++, lineIndex++)
            {
                var bc = SplitTokens(dataLines[lineIndex]);
                if (bc.Count < 3)
                    issues.Add($"Boundary condition row {i + 1} is incomplete.");
            }
        }

        return new ValidationResult { IsValid = issues.Count == 0, Issues = issues };
    }

    private static string? ReadAnalysisOption(string datContent, string key)
    {
        var pattern = $@"^\s*!\s*{Regex.Escape(key)}\s+(\S+)\s*$";
        foreach (var line in datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var m = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (m.Success)
                return m.Groups[1].Value;
        }
        return null;
    }

    private static List<string> ExtractDataLines(string datContent)
    {
        var lines = new List<string>();
        foreach (var raw in datContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('!'))
                continue;
            lines.Add(trimmed);
        }
        return lines;
    }

    private static bool TryParseCounts(string line, out int nels, out int nn, out int npTypes, out string? error)
    {
        nels = nn = npTypes = 0;
        error = null;
        var tokens = SplitTokens(line);
        if (tokens.Count < 3)
        {
            error = "Header line must contain nels, nn, and np_types.";
            return false;
        }
        if (!int.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out nels)
            || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out nn)
            || !int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out npTypes))
        {
            error = "Header line nels/nn/np_types are not integers.";
            return false;
        }
        return true;
    }

    private static List<string> SplitTokens(string line)
        => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
}
