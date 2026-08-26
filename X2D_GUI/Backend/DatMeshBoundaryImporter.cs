using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using GeotechSlopeApp.ViewModels;

namespace GeotechSlopeApp.Backend;

public sealed class MeshBoundaryImportSummary
{
    public int NNodes { get; init; }
    public int NElements { get; init; }
    public int NBoundaryConditions { get; init; }
    public string MeshType { get; init; } = "T3";
    public string Source { get; init; } = "";
}

/// <summary>
/// Imports explicit mesh + BC blocks (and compact p64 parametric generation) from SRF .dat into UDM + BC VM.
/// </summary>
public static class DatMeshBoundaryImporter
{
    public static bool TryImportToViewModels(
        string datContent,
        UserDefinedMeshViewModel udm,
        BoundaryConditionsViewModel bcVm,
        out MeshBoundaryImportSummary summary)
    {
        summary = new MeshBoundaryImportSummary();
        if (string.IsNullOrWhiteSpace(datContent) || udm == null || bcVm == null)
            return false;

        P610AnalysisOptionsHelper.TryParse(datContent, out var options);
        var meshType = !string.IsNullOrWhiteSpace(options.MeshType)
            ? options.MeshType.Trim().ToUpperInvariant()
            : P610AnalysisOptionsHelper.TryResolveMeshTypeFromDat(datContent, out var mt) ? mt : "T3";

        if (TryParseExplicitMesh(datContent, options, meshType, out var nodes, out var elements, out var bcs, out var src))
        {
            udm.LoadFromDatMesh(meshType, nodes, elements, datContent);
            DatBoundaryConditionApplier.Apply(bcVm, bcs);
    summary = new MeshBoundaryImportSummary
            {
                NNodes = nodes.Count,
                NElements = elements.Count,
                NBoundaryConditions = CountApplicableBcs(bcs),
                MeshType = meshType,
                Source = src,
            };
            return true;
        }

        if (TryBuildCompactP64Mesh(datContent, options, out var built))
        {
            udm.LoadFromDatMesh("Q8", built.Nodes, built.Elements, datContent);
            DatBoundaryConditionApplier.Apply(bcVm, built.BoundaryConditions);
            summary = new MeshBoundaryImportSummary
            {
                NNodes = built.Nn,
                NElements = built.Nels,
                NBoundaryConditions = built.BoundaryConditions.Count,
                MeshType = "Q8",
                Source = "compact_p64_parametric",
            };
            return true;
        }

        return false;
    }

    private static bool TryParseExplicitMesh(
        string datContent,
        P610AnalysisOptions? options,
        string meshType,
        out List<(int id, double x, double y)> nodes,
        out List<(int id, int[] conn, int etype)> elements,
        out List<(int node, int fx, int fy)> bcs,
        out string source)
    {
        nodes = new List<(int, double, double)>();
        elements = new List<(int, int[], int)>();
        bcs = new List<(int, int, int)>();
        source = "explicit";

        var tokens = Tokenise(datContent);
        if (!TryDeqInt(tokens, out int nels) ||
            !TryDeqInt(tokens, out int nn) ||
            !TryDeqInt(tokens, out int npTypes) ||
            nels < 1 || nn < 4 || npTypes < 1 || npTypes > 64)
            return false;

        if (!LooksLikeExplicitMeshHeader(nels, nn, npTypes))
            return false;

        // p64 Q8 decks use one 6-value material line per type; p610 T3 explicit decks use 7 properties.
        // mesh_source explicit controls coordinate handling only — not material layout.
        bool p64LineLayout = string.Equals(options?.MeshType, "Q8", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(FemCppProgramRouter.ResolveProgramId(datContent), "p64", StringComparison.OrdinalIgnoreCase)
                && nels > 80);

        if (p64LineLayout)
        {
            for (int m = 0; m < npTypes; m++)
                if (!SkipMaterialLine(tokens)) return false;
        }
        else
        {
            const int nprops = 7;
            for (int p = 0; p < nprops; p++)
                for (int m = 0; m < npTypes; m++)
                    if (!TryDeqDbl(tokens, out _)) return false;
        }

        var elementTypes = new List<int>(nels);
        if (npTypes > 1)
        {
            for (int i = 0; i < nels; i++)
            {
                if (!TryDeqInt(tokens, out int et) || et < 1)
                    return false;
                elementTypes.Add(et);
            }
        }

        for (int i = 0; i < nn; i++)
        {
            if (!TryDeqDbl(tokens, out double x) || !TryDeqDbl(tokens, out double y))
                return false;
            nodes.Add((i + 1, x, y));
        }

        int connNod = ConnectivityNodeCount(meshType);
        for (int i = 0; i < nels; i++)
        {
            var conn = new int[connNod];
            for (int k = 0; k < connNod; k++)
                if (!TryDeqInt(tokens, out conn[k])) return false;
            int etype = elementTypes.Count > i ? elementTypes[i] : 1;
            elements.Add((i + 1, conn, etype));
        }

        if (!IsExplicitMeshSourceDeck(datContent)
            && IsSmithEmbankmentQ8Deck(options, datContent)
            && TryRebuildEmbankmentNodeCoords(datContent, options, meshType, nn, elements, out var rebuiltNodes))
        {
            nodes = rebuiltNodes;
        }

        if (!TryDeqInt(tokens, out int nr) || nr < 0)
        {
            if (!TryParseBcBlockFromLines(datContent, out bcs) || CountApplicableBcs(bcs) == 0)
                return false;
        }
        else
        {
            for (int i = 0; i < nr; i++)
            {
                if (!TryDeqInt(tokens, out int nd) ||
                    !TryDeqInt(tokens, out int fx) ||
                    !TryDeqInt(tokens, out int fy))
                    break;
                bcs.Add((nd, fx, fy));
            }

            if (CountApplicableBcs(bcs) == 0
                && TryParseBcBlockFromLines(datContent, out var fromLines)
                && CountApplicableBcs(fromLines) > 0)
                bcs = fromLines;
        }

        return nodes.Count == nn && elements.Count == nels && CountApplicableBcs(bcs) > 0;
    }

    /// <summary>
    /// Overwrite <paramref name="coords"/> (length nn, 0-based) with merged emb_2d builder positions for p64 Q8 decks.
    /// </summary>
    public static bool TryFillReconciledEmbankmentCoords(
        string datContent,
        Point[] coords,
        IReadOnlyList<int[]> connectivity)
    {
        if (coords == null || coords.Length == 0 || connectivity == null || connectivity.Count == 0)
            return false;
        P610AnalysisOptionsHelper.TryParse(datContent, out var options);
        var meshType = !string.IsNullOrWhiteSpace(options?.MeshType)
            ? options.MeshType.Trim().ToUpperInvariant()
            : P610AnalysisOptionsHelper.TryResolveMeshTypeFromDat(datContent, out var mt) ? mt : "T3";
        var elements = connectivity.Select((c, i) => (id: i + 1, conn: c, etype: 1)).ToList();
        if (!TryRebuildEmbankmentNodeCoords(datContent, options, meshType, coords.Length, elements, out var nodes))
            return false;
        foreach (var (id, x, y) in nodes)
        {
            int idx = id - 1;
            if (idx >= 0 && idx < coords.Length)
                coords[idx] = new Point(x, y);
        }
        return true;
    }

    /// <summary>
    /// Replace dense emb_2d coord table via Fortran-style g_coord(:,num) using connectivity.
    /// </summary>
    /// <summary>
    /// Structured rectangular Q8 decks use serendipity connectivity and explicit node coordinates.
    /// They must not be passed through Smith emb_2d coord averaging.
    /// </summary>
    /// <summary>True when AnalysisOptions mark a file-coordinate explicit mesh (do not rebuild Smith embankment coords).</summary>
    public static bool IsExplicitMeshSourceDeck(string? datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return false;
        if (P610AnalysisOptionsHelper.TryParse(datContent, out var options)
            && string.Equals(options.MeshSource, "explicit", StringComparison.OrdinalIgnoreCase))
            return true;
        return datContent.Contains("! mesh_source explicit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads the explicit-mesh header line <c>nels nn np_types</c> from a p64 / fem_cpp deck.</summary>
    public static bool TryGetExplicitMeshCounts(string? datContent, out int nels, out int nn)
    {
        nels = 0;
        nn = 0;
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        var sanitized = CppAnalysisRunner.StripFortranFullLineComments(datContent);
        var tokens = Tokenise(sanitized);
        if (tokens.Count == 0)
            return false;

        if (tokens.Peek().Equals("p64", StringComparison.OrdinalIgnoreCase))
            tokens.Dequeue();

        if (!TryDeqInt(tokens, out nels) || !TryDeqInt(tokens, out nn) || !TryDeqInt(tokens, out int npTypes))
            return false;

        return LooksLikeExplicitMeshHeader(nels, nn, npTypes);
    }

    /// <summary>Reads <c>np_types</c> from the explicit-mesh header line <c>nels nn np_types</c>.</summary>
    public static bool TryGetNpTypes(string? datContent, out int npTypes)
    {
        npTypes = 0;
        if (string.IsNullOrWhiteSpace(datContent))
            return false;

        var sanitized = CppAnalysisRunner.StripFortranFullLineComments(datContent);
        var tokens = Tokenise(sanitized);
        if (tokens.Count == 0)
            return false;

        if (tokens.Peek().Equals("p64", StringComparison.OrdinalIgnoreCase))
            tokens.Dequeue();

        if (!TryDeqInt(tokens, out int nels) || !TryDeqInt(tokens, out int nn) || !TryDeqInt(tokens, out npTypes))
            return false;

        return LooksLikeExplicitMeshHeader(nels, nn, npTypes);
    }

    public static bool IsSerendipityRectangularQ8Deck(string? datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return false;
        if (datContent.Contains("q8_connectivity serendipity", StringComparison.OrdinalIgnoreCase))
            return true;
        return TryReadAnalysisInt(datContent, "rectangular_nxe", out int nxe) && nxe > 0;
    }

    /// <summary>Smith Program 6.4 embankment Q8 (not serendipity rectangular).</summary>
    public static bool IsSmithEmbankmentQ8Deck(P610AnalysisOptions? options, string? datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return false;
        if (IsSerendipityRectangularQ8Deck(datContent))
            return false;
        var meshType = !string.IsNullOrWhiteSpace(options?.MeshType)
            ? options.MeshType.Trim()
            : P610AnalysisOptionsHelper.TryResolveMeshTypeFromDat(datContent, out var mt) ? mt : "";
        if (!string.Equals(meshType, "Q8", StringComparison.OrdinalIgnoreCase))
            return false;
        return (options != null && P610AnalysisOptionsHelper.IsP64Q8Deck(options))
            || string.Equals(FemCppProgramRouter.ResolveProgramId(datContent), "p64", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryRebuildEmbankmentNodeCoords(
        string datContent,
        P610AnalysisOptions? options,
        string meshType,
        int nn,
        List<(int id, int[] conn, int etype)>? elements,
        out List<(int id, double x, double y)> nodes)
    {
        nodes = new List<(int, double, double)>();
        if (!string.Equals(meshType, "Q8", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!IsSmithEmbankmentQ8Deck(options, datContent))
            return false;
        if (elements == null || elements.Count == 0)
            return false;

        int nx1 = 20, nx2 = 10, ny1 = 10, ny2 = 5;
        double w1 = 20, s1 = 20, w2 = 20, h1 = 10, h2 = 5;
        TryReadEmbankmentOverrides(datContent, ref nx1, ref nx2, ref ny1, ref ny2, ref w1, ref s1, ref w2, ref h1, ref h2);

        try
        {
            if (nn != elements.Max(e => e.conn.Max()) || elements.Count < 1)
            { /* allow nn mismatch check below */ }

            var coordX = new double[nn];
            var coordY = new double[nn];
            var conn = elements.Select(e => e.conn).ToList();
            TryGetMeshCoordYOffset(datContent, out double yOffset);
            P64EmbankmentMeshBuilder.ApplyAveragedEmbankmentNodeCoords(
                coordX, coordY, conn, nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2, yOffset);

            for (int nid = 1; nid <= nn; nid++)
                nodes.Add((nid, coordX[nid - 1], coordY[nid - 1]));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int CountApplicableBcs(IReadOnlyList<(int node, int fx, int fy)> bcs)
    {
        int n = 0;
        foreach (var (_, fx, fy) in bcs)
            if (fx == 0 || fy == 0) n++;
        return n;
    }

    /// <summary>Parse restrained-node BC block from a p610/p64 .dat deck (0 = restrained, 1 = free per DOF).</summary>
    public static bool TryParseBcFixMap(
        string? datContent,
        out Dictionary<int, (int FixX, int FixY)> fixMap)
    {
        fixMap = new Dictionary<int, (int, int)>();
        if (string.IsNullOrWhiteSpace(datContent)
            || !TryParseBcBlockFromLines(datContent, out var bcs))
            return false;

        foreach (var (node, fx, fy) in bcs)
            fixMap[node] = (fx, fy);
        return fixMap.Count > 0;
    }

    private static bool TryParseBcBlockFromLines(
        string datContent,
        out List<(int node, int fx, int fy)> bcs)
    {
        bcs = new List<(int, int, int)>();
        var dataLines = CollectDataLines(datContent);
        for (int i = 0; i < dataLines.Count; i++)
        {
            var head = SplitLineTokens(dataLines[i]);
            if (head.Count != 1 || !int.TryParse(head[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nr) || nr < 1)
                continue;
            if (i + nr >= dataLines.Count)
                continue;

            var trial = new List<(int, int, int)>(nr);
            bool ok = true;
            for (int j = 0; j < nr; j++)
            {
                var parts = SplitLineTokens(dataLines[i + 1 + j]);
                if (parts.Count < 3
                    || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nd)
                    || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fx)
                    || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fy))
                {
                    ok = false;
                    break;
                }
                trial.Add((nd, fx, fy));
            }
            if (!ok || CountApplicableBcs(trial) == 0)
                continue;

            int tailIdx = i + 1 + nr;
            if (tailIdx >= dataLines.Count)
                continue;

            var afterBc = SplitLineTokens(dataLines[tailIdx]);
            if (afterBc.Count == 1
                && int.TryParse(afterBc[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nouts)
                && nouts >= 0
                && tailIdx + 1 + nouts < dataLines.Count)
            {
                tailIdx += 1 + nouts;
            }

            if (tailIdx >= dataLines.Count)
                continue;
            var tail = SplitLineTokens(dataLines[tailIdx]);
            if (tail.Count >= 2
                && double.TryParse(tail[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                && int.TryParse(tail[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                bcs = trial;
                return true;
            }
        }
        return false;
    }

    private static List<string> CollectDataLines(string datContent)
    {
        var lines = new List<string>();
        foreach (var raw in datContent.Split('\n'))
        {
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(0, bang);
            if (string.IsNullOrWhiteSpace(line)) continue;
            lines.Add(line.Trim());
        }
        return lines;
    }

    private static List<string> SplitLineTokens(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool TryBuildCompactP64Mesh(
        string datContent,
        P610AnalysisOptions? options,
        out P64EmbankmentMeshBuilder.MeshResult result)
    {
        result = null!;
        var sanitized = CppAnalysisRunner.StripFortranFullLineComments(datContent);
        var tokens = Tokenise(sanitized);
        if (tokens.Count == 0)
            return false;

        var firstTokenIsP64 = tokens.Peek().Equals("p64", StringComparison.OrdinalIgnoreCase);
        if (!firstTokenIsP64
            && !string.Equals(FemCppProgramRouter.ResolveProgramId(datContent), "p64", StringComparison.OrdinalIgnoreCase))
            return false;

        if (firstTokenIsP64)
            tokens.Dequeue();

        if (LooksLikeExplicitMeshHeader(tokens))
            return false;

        if (!TryDeqDbl(tokens, out double w1) || !TryDeqDbl(tokens, out double s1) ||
            !TryDeqDbl(tokens, out double w2) || !TryDeqDbl(tokens, out double h1) ||
            !TryDeqDbl(tokens, out double h2))
            return false;

        if (!TryDeqInt(tokens, out int nx1) || !TryDeqInt(tokens, out int nx2) ||
            !TryDeqInt(tokens, out int ny1) || !TryDeqInt(tokens, out int ny2))
            return false;

        TryReadEmbankmentOverrides(datContent, ref nx1, ref nx2, ref ny1, ref ny2, ref w1, ref s1, ref w2, ref h1, ref h2);

        if (!TryDeqInt(tokens, out int npTypes) || npTypes < 1)
            return false;

        for (int m = 0; m < npTypes; m++)
            if (!SkipMaterialLine(tokens)) return false;

        int nelsCompact = nx1 * (ny1 + ny2) + ny2 * nx2;
        if (npTypes > 1)
        {
            for (int i = 0; i < nelsCompact; i++)
                if (!TryDeqInt(tokens, out _)) return false;
        }

        try
        {
            result = P64EmbankmentMeshBuilder.BuildQ8Mesh(nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Y added to Smith emb_2d coords so explicit verification decks store non-negative node Y.
    /// </summary>
    public static bool TryGetMeshCoordYOffset(string datContent, out double offset)
    {
        offset = 0;
        if (TryReadAnalysisDouble(datContent, "mesh_coord_y_offset", out double d) && d > 0)
        {
            offset = d;
            return true;
        }
        return false;
    }

    public static void TryGetEmbankmentMeshParams(
        string datContent,
        out int nx1, out int nx2, out int ny1, out int ny2,
        out double w1, out double s1, out double w2, out double h1, out double h2)
    {
        nx1 = 20; nx2 = 10; ny1 = 10; ny2 = 5;
        w1 = 20; s1 = 20; w2 = 20; h1 = 10; h2 = 5;
        TryReadEmbankmentOverrides(datContent, ref nx1, ref nx2, ref ny1, ref ny2, ref w1, ref s1, ref w2, ref h1, ref h2);
    }

    private static void TryReadEmbankmentOverrides(
        string datContent,
        ref int nx1, ref int nx2, ref int ny1, ref int ny2,
        ref double w1, ref double s1, ref double w2, ref double h1, ref double h2)
    {
        if (TryReadAnalysisInt(datContent, "embankment_nx1", out int v) && v > 0) nx1 = v;
        if (TryReadAnalysisInt(datContent, "embankment_nx2", out v) && v >= 0) nx2 = v;
        if (TryReadAnalysisInt(datContent, "embankment_ny1", out v) && v > 0) ny1 = v;
        if (TryReadAnalysisInt(datContent, "embankment_ny2", out v) && v >= 0) ny2 = v;
        if (TryReadAnalysisDouble(datContent, "embankment_w1", out double d) && d > 0) w1 = d;
        if (TryReadAnalysisDouble(datContent, "embankment_s1", out d) && d > 0) s1 = d;
        if (TryReadAnalysisDouble(datContent, "embankment_w2", out d) && d > 0) w2 = d;
        if (TryReadAnalysisDouble(datContent, "embankment_h1", out d) && d > 0) h1 = d;
        if (TryReadAnalysisDouble(datContent, "embankment_h2", out d) && d > 0) h2 = d;
    }

    private static bool TryReadAnalysisInt(string datContent, string key, out int value)
    {
        value = 0;
        return TryReadAnalysisToken(datContent, key, out var tok)
            && int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadAnalysisDouble(string datContent, string key, out double value)
    {
        value = 0;
        return TryReadAnalysisToken(datContent, key, out var tok)
            && double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadAnalysisToken(string datContent, string key, out string token)
    {
        token = "";
        foreach (var raw in datContent.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.Contains(key, StringComparison.OrdinalIgnoreCase))
                continue;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line[(bang + 1)..].Trim();
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                token = parts[1];
                return true;
            }
        }
        return false;
    }

    private static bool LooksLikeExplicitMeshHeader(int nels, int nn, int npTypes) =>
        nels >= 1 && nn >= 4 && npTypes >= 1 && npTypes <= 64;

    private static bool LooksLikeExplicitMeshHeader(Queue<string> tokens)
    {
        if (tokens.Count < 3) return false;
        if (!int.TryParse(tokens.ElementAt(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nels))
            return false;
        if (!int.TryParse(tokens.ElementAt(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nn))
            return false;
        if (!int.TryParse(tokens.ElementAt(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int npTypes))
            return false;
        return LooksLikeExplicitMeshHeader(nels, nn, npTypes);
    }

    private static int ConnectivityNodeCount(string meshType) =>
        meshType.ToUpperInvariant() switch
        {
            "Q8" => 8,
            "Q4" => 4,
            "T6" => 6,
            _ => 3,
        };

    private static bool SkipMaterialLine(Queue<string> tokens)
    {
        for (int i = 0; i < 6; i++)
            if (!TryDeqDbl(tokens, out _)) return false;
        // Do not call TrySkipOptionalK0 here: the next token is often the first node X (0),
        // which was mis-read as K0 and shifted all nodal coordinates (rectangular Q8 fan artifact).
        return true;
    }

    private static Queue<string> Tokenise(string content)
    {
        var q = new Queue<string>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw;
            var bang = line.IndexOf('!');
            if (bang >= 0) line = line.Substring(0, bang);
            foreach (var tok in line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                q.Enqueue(tok);
        }
        return q;
    }

    private static bool TryDeqInt(Queue<string> q, out int v)
    {
        v = 0;
        return q.Count > 0 && int.TryParse(q.Dequeue(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }

    private static bool TryDeqDbl(Queue<string> q, out double v)
    {
        v = 0;
        return q.Count > 0 && double.TryParse(q.Dequeue(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
